using SayP.Domain.Enums;

namespace SayP.Domain.Entities;

/// <summary>
/// Tracks dialogue state for multi-turn conversations
/// </summary>
public class DialogueState
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public CommandType? PendingIntent { get; set; }
    
    /// <summary>
    /// For generic AI: stores the discovered endpoint JSON
    /// </summary>
    public string? PendingEndpointJson { get; set; }
    
    public string CollectedSlotsJson { get; set; } = "{}";
    public string MissingSlotsJson { get; set; } = "[]";
    
    /// <summary>
    /// Pending media context JSON (for media attachment to entities)
    /// </summary>
    public string? MediaContextJson { get; set; }
    
    public string? LastQuestion { get; set; }
    public int AttemptCount { get; set; }
    public int TurnCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsComplete { get; set; }
    public bool IsActive { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Navigation
    public Conversation? Conversation { get; set; }
}
