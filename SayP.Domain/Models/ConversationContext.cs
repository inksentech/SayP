using SayP.Domain.Enums;

namespace SayP.Domain.Models;

/// <summary>
/// Rich conversation context for intent detection
/// </summary>
public class ConversationContext
{
    public Guid ConversationId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public List<ContextMessage> RecentMessages { get; set; } = new();
    public DialogueState? CurrentDialogueState { get; set; }
    public Dictionary<string, object> SessionVariables { get; set; } = new();
    public DateTime LastInteraction { get; set; }
    public int TurnCount { get; set; }
}

/// <summary>
/// Message in conversation history
/// </summary>
public class ContextMessage
{
    public string Role { get; set; } = string.Empty; // "user" or "assistant"
    public string Content { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public MessageType Type { get; set; }
}

/// <summary>
/// Current dialogue state for multi-turn conversations
/// </summary>
public class DialogueState
{
    public Guid Id { get; set; }
    public CommandType? PendingIntent { get; set; }
    public Dictionary<string, object> CollectedSlots { get; set; } = new();
    public List<string> MissingSlots { get; set; } = new();
    public string? LastQuestion { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsComplete { get; set; }
}

/// <summary>
/// Slot filling result
/// </summary>
public class SlotFillingResult
{
    public bool IsComplete { get; set; }
    public Dictionary<string, object> FilledSlots { get; set; } = new();
    public List<string> MissingSlots { get; set; } = new();
    public string? NextQuestion { get; set; }
    public double Confidence { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
}

/// <summary>
/// Intent detection result with context
/// </summary>
public class IntentDetectionResult
{
    public string PrimaryIntent { get; set; } = string.Empty;
    public List<AlternativeIntent> AlternativeIntents { get; set; } = new();
    public double Confidence { get; set; }
    public bool IsAmbiguous { get; set; }
    public List<string> ClarificationQuestions { get; set; } = new();
    public Dictionary<string, object> ExtractedEntities { get; set; } = new();
    public bool RequiresSlotFilling { get; set; }
}

/// <summary>
/// Alternative intent with confidence
/// </summary>
public class AlternativeIntent
{
    public string Intent { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public string Reason { get; set; } = string.Empty;
}
