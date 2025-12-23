using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SayP.Domain.Entities;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;

namespace SayP.Application.Services;

/// <summary>
/// Manages dialogue state for multi-turn conversations
/// </summary>
public class DialogueStateManager
{
    private readonly ISayPDbContext _context;
    private readonly ILogger<DialogueStateManager> _logger;
    private const int MAX_ATTEMPTS = 3;
    private const int STATE_TIMEOUT_MINUTES = 10;

    public DialogueStateManager(
        ISayPDbContext context,
        ILogger<DialogueStateManager> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get or create active dialogue state for conversation
    /// </summary>
    public async Task<DialogueState?> GetActiveStateAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var state = await _context.DialogueStates
            .FirstOrDefaultAsync(
                ds => ds.ConversationId == conversationId && ds.IsActive,
                cancellationToken);

        // Check if state is expired
        if (state != null && state.ExpiresAt.HasValue && state.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogInformation("Dialogue state {StateId} expired", state.Id);
            state.IsActive = false;
            state.ErrorMessage = "Timeout - conversation expired";
            await _context.SaveChangesAsync(cancellationToken);
            return null;
        }

        return state;
    }

    /// <summary>
    /// Create new dialogue state
    /// </summary>
    public async Task<DialogueState> CreateStateAsync(
        Guid conversationId,
        CommandType pendingIntent,
        Dictionary<string, object> collectedSlots,
        List<string> missingSlots,
        CancellationToken cancellationToken = default)
    {
        // Deactivate any existing active states
        var existingStates = await _context.DialogueStates
            .Where(ds => ds.ConversationId == conversationId && ds.IsActive)
            .ToListAsync(cancellationToken);

        foreach (var existing in existingStates)
        {
            existing.IsActive = false;
        }

        var state = new DialogueState
        {
            Id = Guid.NewGuid(),
            ConversationId = conversationId,
            PendingIntent = pendingIntent,
            CollectedSlotsJson = JsonConvert.SerializeObject(collectedSlots),
            MissingSlotsJson = JsonConvert.SerializeObject(missingSlots),
            AttemptCount = 0,
            TurnCount = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddMinutes(STATE_TIMEOUT_MINUTES),
            IsActive = true,
            IsComplete = false
        };

        _context.DialogueStates.Add(state);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created dialogue state {StateId} for conversation {ConversationId}, Intent: {Intent}",
            state.Id, conversationId, pendingIntent);

        return state;
    }

    /// <summary>
    /// Update dialogue state with new information
    /// </summary>
    public async Task<DialogueState> UpdateStateAsync(
        DialogueState state,
        Dictionary<string, object> newSlots,
        List<string> remainingMissingSlots,
        string? lastQuestion = null,
        CancellationToken cancellationToken = default)
    {
        // Merge collected slots
        var collectedSlots = JsonConvert.DeserializeObject<Dictionary<string, object>>(
            state.CollectedSlotsJson) ?? new();

        foreach (var slot in newSlots)
        {
            collectedSlots[slot.Key] = slot.Value;
        }

        state.CollectedSlotsJson = JsonConvert.SerializeObject(collectedSlots);
        state.MissingSlotsJson = JsonConvert.SerializeObject(remainingMissingSlots);
        state.LastQuestion = lastQuestion;
        state.TurnCount++;
        state.UpdatedAt = DateTime.UtcNow;
        state.IsComplete = !remainingMissingSlots.Any();

        // Check max attempts
        if (state.TurnCount >= MAX_ATTEMPTS && !state.IsComplete)
        {
            _logger.LogWarning(
                "Dialogue state {StateId} exceeded max attempts ({MaxAttempts})",
                state.Id, MAX_ATTEMPTS);
            
            state.IsActive = false;
            state.ErrorMessage = $"Exceeded maximum attempts ({MAX_ATTEMPTS})";
        }

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated dialogue state {StateId}: Turn {Turn}, Complete: {Complete}, Missing: {Missing}",
            state.Id, state.TurnCount, state.IsComplete, remainingMissingSlots.Count);

        return state;
    }

    /// <summary>
    /// Complete dialogue state
    /// </summary>
    public async Task CompleteStateAsync(
        DialogueState state,
        CancellationToken cancellationToken = default)
    {
        state.IsComplete = true;
        state.IsActive = false;
        state.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Completed dialogue state {StateId}", state.Id);
    }

    /// <summary>
    /// Cancel dialogue state
    /// </summary>
    public async Task CancelStateAsync(
        DialogueState state,
        string reason,
        CancellationToken cancellationToken = default)
    {
        state.IsActive = false;
        state.ErrorMessage = reason;
        state.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Cancelled dialogue state {StateId}: {Reason}", state.Id, reason);
    }

    /// <summary>
    /// Get collected slots from state
    /// </summary>
    public Dictionary<string, object> GetCollectedSlots(DialogueState state)
    {
        return JsonConvert.DeserializeObject<Dictionary<string, object>>(
            state.CollectedSlotsJson) ?? new();
    }

    /// <summary>
    /// Get missing slots from state
    /// </summary>
    public List<string> GetMissingSlots(DialogueState state)
    {
        return JsonConvert.DeserializeObject<List<string>>(
            state.MissingSlotsJson) ?? new();
    }

    /// <summary>
    /// Check if state should be retried or abandoned
    /// </summary>
    public bool ShouldRetry(DialogueState state)
    {
        return state.TurnCount < MAX_ATTEMPTS && state.IsActive;
    }

    /// <summary>
    /// Get state summary for logging/debugging
    /// </summary>
    public string GetStateSummary(DialogueState state)
    {
        var collectedSlots = GetCollectedSlots(state);
        var missingSlots = GetMissingSlots(state);

        return $"Intent: {state.PendingIntent}, " +
               $"Turn: {state.TurnCount}/{MAX_ATTEMPTS}, " +
               $"Collected: {collectedSlots.Count}, " +
               $"Missing: {string.Join(", ", missingSlots)}, " +
               $"Complete: {state.IsComplete}";
    }
}
