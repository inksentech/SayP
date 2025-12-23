using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using SayP.Application.Services;
using SayP.Domain.Entities;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;
using SayP.Infrastructure.Persistence;
using Xunit;

namespace SayP.Tests.Services;

public class DialogueStateManagerTests : IDisposable
{
    private readonly SayPDbContext _context;
    private readonly DialogueStateManager _manager;
    private readonly Mock<ILogger<DialogueStateManager>> _loggerMock;

    public DialogueStateManagerTests()
    {
        var options = new DbContextOptionsBuilder<SayPDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new SayPDbContext(options);
        _loggerMock = new Mock<ILogger<DialogueStateManager>>();
        _manager = new DialogueStateManager(_context, _loggerMock.Object);
    }

    [Fact]
    public async Task CreateStateAsync_ShouldCreateNewState()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var collectedSlots = new Dictionary<string, object> { { "name", "Laptop" } };
        var missingSlots = new List<string> { "price" };

        // Act
        var state = await _manager.CreateStateAsync(
            conversationId,
            CommandType.CreateProduct,
            collectedSlots,
            missingSlots);

        // Assert
        state.Should().NotBeNull();
        state.ConversationId.Should().Be(conversationId);
        state.PendingIntent.Should().Be(CommandType.CreateProduct);
        state.IsActive.Should().BeTrue();
        state.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task CreateStateAsync_ShouldDeactivateExistingStates()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        
        // Create first state
        var firstState = await _manager.CreateStateAsync(
            conversationId,
            CommandType.CreateProduct,
            new Dictionary<string, object>(),
            new List<string> { "name" });

        // Act - Create second state
        var secondState = await _manager.CreateStateAsync(
            conversationId,
            CommandType.UpdateProduct,
            new Dictionary<string, object>(),
            new List<string> { "id" });

        // Assert
        var states = await _context.DialogueStates
            .Where(ds => ds.ConversationId == conversationId)
            .ToListAsync();

        states.Should().HaveCount(2);
        states.Count(s => s.IsActive).Should().Be(1);
        secondState.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveStateAsync_ShouldReturnActiveState()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var createdState = await _manager.CreateStateAsync(
            conversationId,
            CommandType.CreateProduct,
            new Dictionary<string, object>(),
            new List<string> { "name" });

        // Act
        var activeState = await _manager.GetActiveStateAsync(conversationId);

        // Assert
        activeState.Should().NotBeNull();
        activeState!.Id.Should().Be(createdState.Id);
        activeState.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveStateAsync_ExpiredState_ShouldReturnNull()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var state = new DialogueState
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            PendingIntent = CommandType.CreateProduct,
            CollectedSlotsJson = "{}",
            MissingSlotsJson = "[]",
            IsActive = true,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1), // Expired
            CreatedAt = DateTime.UtcNow.AddMinutes(-11),
            UpdatedAt = DateTime.UtcNow.AddMinutes(-11)
        };

        _context.DialogueStates.Add(state);
        await _context.SaveChangesAsync();

        // Act
        var activeState = await _manager.GetActiveStateAsync(conversationId);

        // Assert
        activeState.Should().BeNull();
        
        // Verify state was deactivated
        var updatedState = await _context.DialogueStates.FindAsync(state.Id);
        updatedState!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateStateAsync_ShouldMergeSlots()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var state = await _manager.CreateStateAsync(
            conversationId,
            CommandType.CreateProduct,
            new Dictionary<string, object> { { "name", "Laptop" } },
            new List<string> { "price" });

        // Act
        var newSlots = new Dictionary<string, object> { { "price", 15000m } };
        var updatedState = await _manager.UpdateStateAsync(
            state,
            newSlots,
            new List<string>());

        // Assert
        var collectedSlots = _manager.GetCollectedSlots(updatedState);
        collectedSlots.Should().ContainKey("name");
        collectedSlots.Should().ContainKey("price");
        collectedSlots["price"].Should().Be(15000m);
    }

    [Fact]
    public async Task UpdateStateAsync_ShouldIncrementTurnCount()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var state = await _manager.CreateStateAsync(
            conversationId,
            CommandType.CreateProduct,
            new Dictionary<string, object>(),
            new List<string> { "name", "price" });

        var initialTurnCount = state.TurnCount;

        // Act
        await _manager.UpdateStateAsync(
            state,
            new Dictionary<string, object> { { "name", "Laptop" } },
            new List<string> { "price" });

        // Assert
        state.TurnCount.Should().Be(initialTurnCount + 1);
    }

    [Fact]
    public async Task UpdateStateAsync_AllSlotsFilled_ShouldMarkComplete()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var state = await _manager.CreateStateAsync(
            conversationId,
            CommandType.CreateProduct,
            new Dictionary<string, object> { { "name", "Laptop" } },
            new List<string> { "price" });

        // Act
        await _manager.UpdateStateAsync(
            state,
            new Dictionary<string, object> { { "price", 15000m } },
            new List<string>()); // No missing slots

        // Assert
        state.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateStateAsync_ExceedMaxAttempts_ShouldDeactivate()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var state = await _manager.CreateStateAsync(
            conversationId,
            CommandType.CreateProduct,
            new Dictionary<string, object>(),
            new List<string> { "name", "price" });

        // Act - Simulate 3 attempts
        for (int i = 0; i < 3; i++)
        {
            await _manager.UpdateStateAsync(
                state,
                new Dictionary<string, object>(),
                new List<string> { "name", "price" });
        }

        // Assert
        state.IsActive.Should().BeFalse();
        state.ErrorMessage.Should().Contain("maximum attempts");
    }

    [Fact]
    public async Task CompleteStateAsync_ShouldMarkAsCompleteAndInactive()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var state = await _manager.CreateStateAsync(
            conversationId,
            CommandType.CreateProduct,
            new Dictionary<string, object>(),
            new List<string> { "name" });

        // Act
        await _manager.CompleteStateAsync(state);

        // Assert
        state.IsComplete.Should().BeTrue();
        state.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task CancelStateAsync_ShouldDeactivateWithReason()
    {
        // Arrange
        var conversationId = Guid.NewGuid();
        var state = await _manager.CreateStateAsync(
            conversationId,
            CommandType.CreateProduct,
            new Dictionary<string, object>(),
            new List<string> { "name" });

        var reason = "User cancelled";

        // Act
        await _manager.CancelStateAsync(state, reason);

        // Assert
        state.IsActive.Should().BeFalse();
        state.ErrorMessage.Should().Be(reason);
    }

    [Fact]
    public void ShouldRetry_WithinMaxAttempts_ShouldReturnTrue()
    {
        // Arrange
        var state = new DialogueState
        {
            TurnCount = 2,
            IsActive = true
        };

        // Act
        var shouldRetry = _manager.ShouldRetry(state);

        // Assert
        shouldRetry.Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_ExceededMaxAttempts_ShouldReturnFalse()
    {
        // Arrange
        var state = new DialogueState
        {
            TurnCount = 3,
            IsActive = true
        };

        // Act
        var shouldRetry = _manager.ShouldRetry(state);

        // Assert
        shouldRetry.Should().BeFalse();
    }

    [Fact]
    public void ShouldRetry_InactiveState_ShouldReturnFalse()
    {
        // Arrange
        var state = new DialogueState
        {
            TurnCount = 1,
            IsActive = false
        };

        // Act
        var shouldRetry = _manager.ShouldRetry(state);

        // Assert
        shouldRetry.Should().BeFalse();
    }

    [Fact]
    public void GetStateSummary_ShouldReturnFormattedString()
    {
        // Arrange
        var state = new DialogueState
        {
            PendingIntent = CommandType.CreateProduct,
            TurnCount = 1,
            CollectedSlotsJson = "{\"name\":\"Laptop\"}",
            MissingSlotsJson = "[\"price\"]",
            IsComplete = false
        };

        // Act
        var summary = _manager.GetStateSummary(state);

        // Assert
        summary.Should().Contain("CreateProduct");
        summary.Should().Contain("Turn: 1");
        summary.Should().Contain("price");
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
    }
}
