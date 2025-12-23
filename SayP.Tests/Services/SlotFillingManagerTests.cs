using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using SayP.Application.Services;
using SayP.Domain.Enums;
using Xunit;

namespace SayP.Tests.Services;

public class SlotFillingManagerTests
{
    private readonly SlotFillingManager _manager;
    private readonly Mock<ILogger<SlotFillingManager>> _loggerMock;

    public SlotFillingManagerTests()
    {
        _loggerMock = new Mock<ILogger<SlotFillingManager>>();
        _manager = new SlotFillingManager(_loggerMock.Object);
    }

    [Fact]
    public void GetRequiredSlots_CreateProduct_ShouldReturnNameAndPrice()
    {
        // Act
        var slots = _manager.GetRequiredSlots(CommandType.CreateProduct);

        // Assert
        slots.Should().Contain("name");
        slots.Should().Contain("price");
    }

    [Fact]
    public void GetOptionalSlots_CreateProduct_ShouldReturnOptionalFields()
    {
        // Act
        var slots = _manager.GetOptionalSlots(CommandType.CreateProduct);

        // Assert
        slots.Should().Contain("description");
        slots.Should().Contain("taxRate");
        slots.Should().Contain("stockQuantity");
    }

    [Fact]
    public void FillSlots_AllRequiredPresent_ShouldBeComplete()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "name", "Laptop" },
            { "price", 15000m }
        };

        // Act
        var result = _manager.FillSlots(CommandType.CreateProduct, entities);

        // Assert
        result.IsComplete.Should().BeTrue();
        result.MissingSlots.Should().BeEmpty();
        result.FilledSlots.Should().ContainKey("name");
        result.FilledSlots.Should().ContainKey("price");
    }

    [Fact]
    public void FillSlots_MissingRequired_ShouldNotBeComplete()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "name", "Laptop" }
            // Missing price
        };

        // Act
        var result = _manager.FillSlots(CommandType.CreateProduct, entities);

        // Assert
        result.IsComplete.Should().BeFalse();
        result.MissingSlots.Should().Contain("price");
        result.NextQuestion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void FillSlots_InvalidPrice_ShouldHaveValidationError()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "name", "Laptop" },
            { "price", -100m } // Invalid negative price
        };

        // Act
        var result = _manager.FillSlots(CommandType.CreateProduct, entities);

        // Assert
        result.ValidationErrors.Should().NotBeEmpty();
        result.ValidationErrors.Should().Contain(e => e.Contains("price") || e.Contains("fiyat") || e.Contains("Fiyat"));
    }

    [Fact]
    public void FillSlots_InvalidTaxRate_ShouldHaveValidationError()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "name", "Laptop" },
            { "price", 15000m },
            { "taxRate", 150m } // Invalid > 100
        };

        // Act
        var result = _manager.FillSlots(CommandType.CreateProduct, entities);

        // Assert
        result.ValidationErrors.Should().NotBeEmpty();
        result.ValidationErrors.Should().Contain(e => e.Contains("KDV") || e.Contains("tax"));
    }

    [Fact]
    public void FillSlots_InvalidPhone_ShouldHaveValidationError()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "customerName", "Ahmet Yılmaz" },
            { "phone", "123" } // Too short
        };

        // Act
        var result = _manager.FillSlots(CommandType.CreateCustomer, entities);

        // Assert
        result.ValidationErrors.Should().NotBeEmpty();
        result.ValidationErrors.Should().Contain(e => e.Contains("telefon") || e.Contains("phone") || e.Contains("Telefon"));
    }

    [Fact]
    public void FillSlots_InvalidEmail_ShouldHaveValidationError()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "customerName", "Ahmet Yılmaz" },
            { "email", "invalid-email" } // Invalid format
        };

        // Act
        var result = _manager.FillSlots(CommandType.CreateCustomer, entities);

        // Assert
        result.ValidationErrors.Should().NotBeEmpty();
        result.ValidationErrors.Should().Contain(e => e.Contains("email") || e.Contains("e-posta") || e.Contains("E-posta"));
    }

    [Fact]
    public void FillSlots_GeneratesQuestion_ForMissingSlot()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "name", "Laptop" }
            // Missing price
        };

        // Act
        var result = _manager.FillSlots(CommandType.CreateProduct, entities);

        // Assert
        result.NextQuestion.Should().NotBeNullOrEmpty();
        result.NextQuestion.Should().Contain("Fiyat");
    }

    [Fact]
    public void FillSlots_WithOptionalSlots_ShouldIncludeThem()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "name", "Laptop" },
            { "price", 15000m },
            { "description", "Gaming laptop" },
            { "taxRate", 18m }
        };

        // Act
        var result = _manager.FillSlots(CommandType.CreateProduct, entities);

        // Assert
        result.IsComplete.Should().BeTrue();
        result.FilledSlots.Should().ContainKey("description");
        result.FilledSlots.Should().ContainKey("taxRate");
    }

    [Theory]
    [InlineData(CommandType.UpdateProduct, "id")]
    [InlineData(CommandType.DeleteProduct, "id")]
    [InlineData(CommandType.GetProduct, "id")]
    public void GetRequiredSlots_IdBasedCommands_ShouldRequireId(CommandType commandType, string expectedSlot)
    {
        // Act
        var slots = _manager.GetRequiredSlots(commandType);

        // Assert
        slots.Should().Contain(expectedSlot);
    }

    [Fact]
    public void FillSlots_CalculatesConfidence_BasedOnFilledSlots()
    {
        // Arrange
        var entities = new Dictionary<string, object>
        {
            { "name", "Laptop" },
            { "price", 15000m }
        };

        // Act
        var result = _manager.FillSlots(CommandType.CreateProduct, entities);

        // Assert
        result.Confidence.Should().BeGreaterThan(0);
        result.Confidence.Should().BeLessThanOrEqualTo(1.0);
    }
}

