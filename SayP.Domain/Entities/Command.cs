using SayP.Domain.Enums;

namespace SayP.Domain.Entities;

/// <summary>
/// Represents a command extracted from AI and executed on main backend
/// </summary>
public class Command : BaseEntity
{
    /// <summary>
    /// Conversation ID
    /// </summary>
    public Guid ConversationId { get; set; }

    /// <summary>
    /// Message ID that triggered this command
    /// </summary>
    public Guid? MessageId { get; set; }

    /// <summary>
    /// Command type (CreateProduct, CreateInvoice, CreateContract, etc.)
    /// </summary>
    public CommandType Type { get; set; }

    /// <summary>
    /// Command status
    /// </summary>
    public CommandStatus Status { get; set; } = CommandStatus.Pending;

    /// <summary>
    /// Original user message
    /// </summary>
    public string OriginalMessage { get; set; } = string.Empty;

    /// <summary>
    /// AI extracted command parameters (JSON)
    /// </summary>
    public string Parameters { get; set; } = string.Empty;

    /// <summary>
    /// AI confidence score (0.0 - 1.0)
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// AI extracted command (JSON) - legacy
    /// </summary>
    public string CommandJson { get; set; } = string.Empty;

    /// <summary>
    /// Execution result (JSON)
    /// </summary>
    public string? Result { get; set; }

    /// <summary>
    /// Execution result (JSON) - legacy
    /// </summary>
    public string? ResultJson { get; set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Executed at
    /// </summary>
    public DateTime? ExecutedAt { get; set; }

    /// <summary>
    /// Retry count
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Max retry attempts
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Requires user confirmation?
    /// </summary>
    public bool RequiresConfirmation { get; set; } = false;

    /// <summary>
    /// Confirmed by user?
    /// </summary>
    public bool IsConfirmed { get; set; } = false;

    /// <summary>
    /// Confirmation message ID
    /// </summary>
    public Guid? ConfirmationMessageId { get; set; }

    /// <summary>
    /// Navigation property
    /// </summary>
    public Conversation? Conversation { get; set; }

    /// <summary>
    /// Navigation property
    /// </summary>
    public Message? Message { get; set; }
}
