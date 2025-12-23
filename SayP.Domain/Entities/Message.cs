using SayP.Domain.Enums;

namespace SayP.Domain.Entities;

/// <summary>
/// Represents a WhatsApp message (incoming or outgoing)
/// </summary>
public class Message : BaseEntity
{
    /// <summary>
    /// Conversation ID
    /// </summary>
    public Guid ConversationId { get; set; }

    /// <summary>
    /// WhatsApp message ID (for idempotency)
    /// </summary>
    public string WhatsAppMessageId { get; set; } = string.Empty;

    /// <summary>
    /// Message type
    /// </summary>
    public MessageType Type { get; set; }

    /// <summary>
    /// Message direction
    /// </summary>
    public MessageDirection Direction { get; set; }

    /// <summary>
    /// Message content (text, JSON for interactive messages)
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// From phone number
    /// </summary>
    public string From { get; set; } = string.Empty;

    /// <summary>
    /// To phone number
    /// </summary>
    public string To { get; set; } = string.Empty;

    /// <summary>
    /// Message timestamp from WhatsApp
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Message status (sent, delivered, read, failed)
    /// </summary>
    public MessageStatus Status { get; set; } = MessageStatus.Pending;

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Media URL (for image, audio, video messages)
    /// </summary>
    public string? MediaUrl { get; set; }

    /// <summary>
    /// Retry count for failed messages
    /// </summary>
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Raw webhook payload (for debugging)
    /// </summary>
    public string? RawPayload { get; set; }

    /// <summary>
    /// Navigation property
    /// </summary>
    public Conversation? Conversation { get; set; }
}
