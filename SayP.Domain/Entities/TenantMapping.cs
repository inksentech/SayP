namespace SayP.Domain.Entities;

/// <summary>
/// Maps WhatsApp phone numbers to tenants in the main backend
/// </summary>
public class TenantMapping : BaseEntity
{
    /// <summary>
    /// WhatsApp phone number (e.g., "905551234567")
    /// </summary>
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Tenant ID from main backend
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Company ID from main backend (optional, for multi-company tenants)
    /// </summary>
    public Guid? CompanyId { get; set; }

    /// <summary>
    /// WhatsApp Business Account ID
    /// </summary>
    public string? WhatsAppBusinessAccountId { get; set; }

    /// <summary>
    /// WhatsApp Phone Number ID (from Meta)
    /// </summary>
    public string? WhatsAppPhoneNumberId { get; set; }

    /// <summary>
    /// Is this mapping active?
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Additional metadata (JSON)
    /// </summary>
    public string? Metadata { get; set; }
}
