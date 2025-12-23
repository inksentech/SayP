namespace SayP.Domain.Interfaces;

/// <summary>
/// Provider-agnostic AI interface for command extraction
/// </summary>
public interface IAIProvider
{
    /// <summary>
    /// ⚠️ DEPRECATED: Extract structured command from natural language
    /// Use DynamicIntentMapper instead for dynamic intent mapping.
    /// This will be removed in v3.0.0
    /// </summary>
    /// <param name="message">User message</param>
    /// <param name="conversationContext">Previous conversation context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted command as JSON</returns>
    [Obsolete("Use DynamicIntentMapper for dynamic intent mapping. This will be removed in v3.0.0")]
    Task<AICommandResult> ExtractCommandAsync(
        string message, 
        string? conversationContext = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a natural language response
    /// </summary>
    Task<string> GenerateResponseAsync(
        string prompt, 
        string? context = null, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract command from image with optional text prompt
    /// </summary>
    Task<AICommandResult> ExtractCommandFromImageAsync(
        byte[] imageData,
        string mimeType,
        string? textPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract command from audio with optional text prompt
    /// </summary>
    Task<AICommandResult> ExtractCommandFromAudioAsync(
        byte[] audioData,
        string mimeType,
        string? textPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extract command from multiple media (images, audio, text)
    /// </summary>
    Task<AICommandResult> ExtractCommandFromMultimodalAsync(
        List<MediaPart> mediaParts,
        string? textPrompt = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ⚠️ DEPRECATED: Extract entities from message using AI
    /// Use DynamicSlotFiller instead for schema-based entity extraction.
    /// This will be removed in v3.0.0
    /// </summary>
    /// <param name="message">User message</param>
    /// <param name="commandType">The command type to extract entities for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extracted entities as dictionary</returns>
    [Obsolete("Use DynamicSlotFiller for dynamic entity extraction. This will be removed in v3.0.0")]
    Task<Dictionary<string, object>> ExtractEntitiesAsync(
        string message,
        string commandType,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a media part (image, audio, or text)
/// </summary>
public class MediaPart
{
    public MediaPartType Type { get; set; }
    public byte[]? Data { get; set; }
    public string? MimeType { get; set; }
    public string? Text { get; set; }
}

public enum MediaPartType
{
    Text,
    Image,
    Audio
}

public class AICommandResult
{
    public bool Success { get; set; }
    public string? CommandType { get; set; }
    public string? CommandJson { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string? ConfirmationMessage { get; set; }
    public double Confidence { get; set; } = 0.0;
}
