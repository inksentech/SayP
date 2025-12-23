using Microsoft.Extensions.Logging;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;
using System.Text.Json;

namespace SayP.Application.Services;

/// <summary>
/// AI-driven media attachment service
/// Detects which fields accept media and attaches media to entities
/// </summary>
public class AIMediaAttachmentService
{
    private readonly IAIProvider _aiProvider;
    private readonly ILogger<AIMediaAttachmentService> _logger;

    // Cache for media field detection results
    private readonly Dictionary<string, MediaFieldDetectionResult> _detectionCache = new();

    public AIMediaAttachmentService(
        IAIProvider aiProvider,
        ILogger<AIMediaAttachmentService> logger)
    {
        _aiProvider = aiProvider;
        _logger = logger;
    }

    /// <summary>
    /// Detect if endpoint accepts media and which field to use
    /// </summary>
    public async Task<MediaFieldDetectionResult> DetectMediaFieldAsync(
        DiscoveredEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        // Check cache first
        var cacheKey = $"{endpoint.Intent}:{endpoint.Route}";
        if (_detectionCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var schemaFields = endpoint.Schema?.Fields?
                .Select(f => new { f.Name, f.Type, f.Description, f.ItemType })
                .ToList();

            var prompt = $@"Analyze this API endpoint to determine if it accepts media/image attachments.

Endpoint: {endpoint.Intent}
Description: {endpoint.Description}
HTTP Method: {endpoint.HttpMethod}

Schema Fields:
{JsonSerializer.Serialize(schemaFields, new JsonSerializerOptions { WriteIndented = true })}

RULES:
1. Look for fields that typically hold media:
   - ProductMedias, Images, Photos, Attachments (array types)
   - ImageUrl, PhotoUrl, MediaUrl, CoverImage (URL string types)
   - ImageData, MediaData, PhotoData (base64 string types)
   - Media, Image, Photo (object types)

2. Check if there's an ""IsCreatedFromMedia"" or similar flag field

3. Field types that accept media:
   - List<*Media*>, IList<*Media*>, *Media[] → MediaArray
   - *Url, *Image (string) → UrlField
   - *Data (string, base64) → Base64Field

Respond with JSON only:
{{
  ""acceptsMedia"": true,
  ""mediaFieldName"": ""ProductMedias"",
  ""fieldType"": ""MediaArray"",
  ""shouldSetCreatedFromMedia"": true,
  ""reasoning"": ""ProductMedias is a list of media objects, and IsCreatedFromMedia flag exists""
}}

Or if no media field: 
{{
  ""acceptsMedia"": false,
  ""reasoning"": ""No media-related fields found in schema""
}}

fieldType options: ""MediaArray"", ""SingleMedia"", ""UrlField"", ""Base64Field""";

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);

            var result = ParseDetectionResponse(response);
            
            // Cache the result
            _detectionCache[cacheKey] = result;
            
            _logger.LogInformation(
                "Media detection for {Intent}: AcceptsMedia={Accepts}, Field={Field}, Type={Type}",
                endpoint.Intent, result.AcceptsMedia, result.MediaFieldName, result.FieldType);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to detect media field for {Intent}", endpoint.Intent);
            return new MediaFieldDetectionResult { AcceptsMedia = false };
        }
    }

    /// <summary>
    /// Attach media to extracted parameters
    /// </summary>
    public async Task<Dictionary<string, object>> AttachMediaAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> extractedParameters,
        MediaContext mediaContext,
        CancellationToken cancellationToken = default)
    {
        if (mediaContext.IsUsed)
        {
            _logger.LogDebug("Media already used, skipping attachment");
            return extractedParameters;
        }

        var detection = await DetectMediaFieldAsync(endpoint, cancellationToken);
        
        if (!detection.AcceptsMedia || string.IsNullOrEmpty(detection.MediaFieldName))
        {
            _logger.LogDebug("Endpoint {Intent} does not accept media", endpoint.Intent);
            return extractedParameters;
        }

        try
        {
            // Create media object based on field type
            var mediaValue = CreateMediaValue(mediaContext, detection);
            
            if (mediaValue != null)
            {
                extractedParameters[detection.MediaFieldName] = mediaValue;
                
                // Set IsCreatedFromMedia flag if applicable
                if (detection.ShouldSetCreatedFromMedia)
                {
                    extractedParameters["IsCreatedFromMedia"] = true;
                }

                // Mark media as used
                mediaContext.IsUsed = true;
                mediaContext.AttachedToField = detection.MediaFieldName;

                _logger.LogInformation(
                    "✅ Attached media to {Field} (type: {Type}), IsCreatedFromMedia={Flag}",
                    detection.MediaFieldName, detection.FieldType, detection.ShouldSetCreatedFromMedia);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to attach media to {Field}", detection.MediaFieldName);
        }

        return extractedParameters;
    }

    /// <summary>
    /// Create appropriate media value based on field type
    /// </summary>
    private object? CreateMediaValue(MediaContext mediaContext, MediaFieldDetectionResult detection)
    {
        switch (detection.FieldType)
        {
            case MediaFieldType.MediaArray:
                // Create array with single media object
                return new List<object>
                {
                    new
                    {
                        Name = mediaContext.FileName ?? $"media{mediaContext.GetExtension()}",
                        ContentType = mediaContext.MimeType,
                        MediaData = mediaContext.MediaDataBase64 ?? 
                                   (mediaContext.MediaData != null ? Convert.ToBase64String(mediaContext.MediaData) : null),
                        IsCoverMedia = true,
                        Extension = mediaContext.GetExtension()?.TrimStart('.'),
                        IsVideo = mediaContext.IsVideo,
                        FileSize = mediaContext.FileSize
                    }
                };

            case MediaFieldType.SingleMedia:
                return new
                {
                    Name = mediaContext.FileName ?? $"media{mediaContext.GetExtension()}",
                    ContentType = mediaContext.MimeType,
                    MediaData = mediaContext.MediaDataBase64 ?? 
                               (mediaContext.MediaData != null ? Convert.ToBase64String(mediaContext.MediaData) : null),
                    Extension = mediaContext.GetExtension()?.TrimStart('.')
                };

            case MediaFieldType.UrlField:
                // Return URL if available, otherwise base64 data URL
                if (!string.IsNullOrEmpty(mediaContext.MediaUrl))
                    return mediaContext.MediaUrl;
                
                if (!string.IsNullOrEmpty(mediaContext.MediaDataBase64))
                    return $"data:{mediaContext.MimeType};base64,{mediaContext.MediaDataBase64}";
                
                if (mediaContext.MediaData != null)
                    return $"data:{mediaContext.MimeType};base64,{Convert.ToBase64String(mediaContext.MediaData)}";
                
                return null;

            case MediaFieldType.Base64Field:
                return mediaContext.MediaDataBase64 ?? 
                       (mediaContext.MediaData != null ? Convert.ToBase64String(mediaContext.MediaData) : null);

            default:
                return null;
        }
    }

    /// <summary>
    /// Parse AI detection response
    /// </summary>
    private MediaFieldDetectionResult ParseDetectionResponse(string response)
    {
        try
        {
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(response, @"\{[\s\S]*\}");
            if (jsonMatch.Success)
            {
                var json = JsonSerializer.Deserialize<JsonElement>(jsonMatch.Value);
                
                var result = new MediaFieldDetectionResult
                {
                    AcceptsMedia = json.TryGetProperty("acceptsMedia", out var accepts) && accepts.GetBoolean(),
                    MediaFieldName = json.TryGetProperty("mediaFieldName", out var field) ? field.GetString() : null,
                    ShouldSetCreatedFromMedia = json.TryGetProperty("shouldSetCreatedFromMedia", out var flag) && flag.GetBoolean(),
                    Reasoning = json.TryGetProperty("reasoning", out var reason) ? reason.GetString() : null
                };

                if (json.TryGetProperty("fieldType", out var fieldType))
                {
                    result.FieldType = fieldType.GetString()?.ToLower() switch
                    {
                        "mediaarray" => MediaFieldType.MediaArray,
                        "singlemedia" => MediaFieldType.SingleMedia,
                        "urlfield" => MediaFieldType.UrlField,
                        "base64field" => MediaFieldType.Base64Field,
                        _ => MediaFieldType.MediaArray
                    };
                }

                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse media detection response");
        }

        return new MediaFieldDetectionResult { AcceptsMedia = false };
    }

    /// <summary>
    /// Clear detection cache (useful when schema changes)
    /// </summary>
    public void ClearCache()
    {
        _detectionCache.Clear();
    }
}
