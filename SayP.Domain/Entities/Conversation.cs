using SayP.Domain.Enums;

namespace SayP.Domain.Entities;

/// <summary>
/// Represents a WhatsApp conversation with 24-hour window tracking
/// </summary>
public class Conversation : BaseEntity
{
    /// <summary>
    /// WhatsApp phone number (customer)
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Tenant ID from main backend
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Company ID from main backend
    /// </summary>
    public Guid? CompanyId { get; set; }

    /// <summary>
    /// Conversation status
    /// </summary>
    public ConversationStatus Status { get; set; } = ConversationStatus.Active;

    /// <summary>
    /// Last message timestamp (for 24h window tracking)
    /// </summary>
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 24-hour window expires at
    /// </summary>
    public DateTime WindowExpiresAt { get; set; } = DateTime.UtcNow.AddHours(24);

    /// <summary>
    /// Is within 24-hour free messaging window?
    /// </summary>
    public bool IsWithinWindow => DateTime.UtcNow < WindowExpiresAt;

    /// <summary>
    /// Conversation context (JSON) - for AI to maintain context
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// Messages in this conversation
    /// </summary>
    public List<Message> Messages { get; set; } = new();

    /// <summary>
    /// Commands executed in this conversation
    /// </summary>
    public List<Command> Commands { get; set; } = new();
}
