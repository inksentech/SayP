namespace SayP.Domain.Interfaces;

/// <summary>
/// WhatsApp Cloud API service interface
/// </summary>
public interface IWhatsAppService
{
    /// <summary>
    /// Send a text message
    /// </summary>
    Task<WhatsAppMessageResult> SendTextMessageAsync(
        string to, 
        string message, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send an interactive message (buttons)
    /// </summary>
    Task<WhatsAppMessageResult> SendInteractiveButtonsAsync(
        string to, 
        string bodyText, 
        List<WhatsAppButton> buttons, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send an interactive message (list)
    /// </summary>
    Task<WhatsAppMessageResult> SendInteractiveListAsync(
        string to, 
        string bodyText, 
        string buttonText,
        List<WhatsAppListSection> sections, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Send a template message
    /// </summary>
    Task<WhatsAppMessageResult> SendTemplateMessageAsync(
        string to, 
        string templateName, 
        string languageCode,
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark message as read
    /// </summary>
    Task<bool> MarkMessageAsReadAsync(
        string messageId, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify webhook signature
    /// </summary>
    bool VerifyWebhookSignature(string payload, string signature, string appSecret);

    /// <summary>
    /// Download media file from WhatsApp
    /// </summary>
    Task<WhatsAppMediaResult> DownloadMediaAsync(
        string mediaId,
        CancellationToken cancellationToken = default);
}

public class WhatsAppMessageResult
{
    public bool Success { get; set; }
    public string? MessageId { get; set; }
    public string? ErrorMessage { get; set; }
}

public class WhatsAppButton
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class WhatsAppListSection
{
    public string Title { get; set; } = string.Empty;
    public List<WhatsAppListRow> Rows { get; set; } = new();
}

public class WhatsAppListRow
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class WhatsAppMediaResult
{
    public bool Success { get; set; }
    public byte[]? Data { get; set; }
    public string? MimeType { get; set; }
    public string? ErrorMessage { get; set; }
}
