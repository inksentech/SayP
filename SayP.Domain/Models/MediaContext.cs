namespace SayP.Domain.Models;

/// <summary>
/// Represents media (image/video/audio) received from chat
/// Used to attach media to entities during creation
/// </summary>
public class MediaContext
{
    /// <summary>
    /// WhatsApp/Platform media ID
    /// </summary>
    public string? MediaId { get; set; }
    
    /// <summary>
    /// Media MIME type (image/jpeg, image/png, video/mp4, etc.)
    /// </summary>
    public string? MimeType { get; set; }
    
    /// <summary>
    /// Media caption if provided
    /// </summary>
    public string? Caption { get; set; }
    
    /// <summary>
    /// Media binary data (downloaded from platform)
    /// </summary>
    public byte[]? MediaData { get; set; }
    
    /// <summary>
    /// Base64 encoded media data
    /// </summary>
    public string? MediaDataBase64 { get; set; }
    
    /// <summary>
    /// Media URL from platform
    /// </summary>
    public string? MediaUrl { get; set; }
    
    /// <summary>
    /// Original filename if available
    /// </summary>
    public string? FileName { get; set; }
    
    /// <summary>
    /// File size in bytes
    /// </summary>
    public long? FileSize { get; set; }
    
    /// <summary>
    /// When media was received
    /// </summary>
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Whether this media has been used/attached to an entity
    /// </summary>
    public bool IsUsed { get; set; } = false;
    
    /// <summary>
    /// Which field this media was attached to
    /// </summary>
    public string? AttachedToField { get; set; }
    
    /// <summary>
    /// Check if media is an image
    /// </summary>
    public bool IsImage => MimeType?.StartsWith("image/") == true;
    
    /// <summary>
    /// Check if media is a video
    /// </summary>
    public bool IsVideo => MimeType?.StartsWith("video/") == true;
    
    /// <summary>
    /// Check if media is audio
    /// </summary>
    public bool IsAudio => MimeType?.StartsWith("audio/") == true;
    
    /// <summary>
    /// Get file extension from MIME type
    /// </summary>
    public string? GetExtension()
    {
        return MimeType?.ToLower() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "video/mp4" => ".mp4",
            "video/quicktime" => ".mov",
            "audio/ogg" => ".ogg",
            "audio/mpeg" => ".mp3",
            "application/pdf" => ".pdf",
            _ => null
        };
    }
}

/// <summary>
/// Result of media field detection by AI
/// </summary>
public class MediaFieldDetectionResult
{
    /// <summary>
    /// Whether the endpoint accepts media
    /// </summary>
    public bool AcceptsMedia { get; set; }
    
    /// <summary>
    /// Field name that accepts media (e.g., "ProductMedias", "ImageUrl")
    /// </summary>
    public string? MediaFieldName { get; set; }
    
    /// <summary>
    /// Type of media field (array, single, url)
    /// </summary>
    public MediaFieldType FieldType { get; set; }
    
    /// <summary>
    /// Whether to set IsCreatedFromMedia flag
    /// </summary>
    public bool ShouldSetCreatedFromMedia { get; set; }
    
    /// <summary>
    /// AI reasoning for the detection
    /// </summary>
    public string? Reasoning { get; set; }
}

/// <summary>
/// Type of media field
/// </summary>
public enum MediaFieldType
{
    /// <summary>
    /// Array of media objects (e.g., List<ProductMediaDto>)
    /// </summary>
    MediaArray,
    
    /// <summary>
    /// Single media object
    /// </summary>
    SingleMedia,
    
    /// <summary>
    /// URL string field (e.g., ImageUrl)
    /// </summary>
    UrlField,
    
    /// <summary>
    /// Base64 string field
    /// </summary>
    Base64Field
}
