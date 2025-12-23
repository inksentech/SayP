using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SayP.Domain.Interfaces;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

namespace SayP.Infrastructure.WhatsApp;

public class WhatsAppService : IWhatsAppService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppService> _logger;
    private readonly string _apiUrl;
    private readonly string _phoneNumberId;
    private readonly string _accessToken;

    public WhatsAppService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<WhatsAppService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _apiUrl = configuration["WhatsApp:ApiUrl"] 
            ?? Environment.GetEnvironmentVariable("WHATSAPP_API_URL") 
            ?? "https://graph.facebook.com/v18.0";
        
        _phoneNumberId = configuration["WhatsApp:PhoneNumberId"] 
            ?? Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID") 
            ?? throw new InvalidOperationException("WhatsApp PhoneNumberId not configured");
        
        _accessToken = configuration["WhatsApp:AccessToken"] 
            ?? Environment.GetEnvironmentVariable("WHATSAPP_ACCESS_TOKEN") 
            ?? throw new InvalidOperationException("WhatsApp AccessToken not configured");

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
    }

    public async Task<WhatsAppMessageResult> SendTextMessageAsync(
        string to,
        string message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                to,
                type = "text",
                text = new { body = message }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_apiUrl}/{_phoneNumberId}/messages",
                payload,
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("WhatsApp API error: {StatusCode} - {Content}", response.StatusCode, content);
                return new WhatsAppMessageResult
                {
                    Success = false,
                    ErrorMessage = $"API error: {response.StatusCode}"
                };
            }

            dynamic result = JsonConvert.DeserializeObject(content)!;
            string messageId = result.messages[0].id;

            _logger.LogInformation("Message sent successfully to {To}, MessageId: {MessageId}", to, messageId);

            return new WhatsAppMessageResult
            {
                Success = true,
                MessageId = messageId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending text message to {To}", to);
            return new WhatsAppMessageResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WhatsAppMessageResult> SendInteractiveButtonsAsync(
        string to,
        string bodyText,
        List<WhatsAppButton> buttons,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                to,
                type = "interactive",
                interactive = new
                {
                    type = "button",
                    body = new { text = bodyText },
                    action = new
                    {
                        buttons = buttons.Select(b => new
                        {
                            type = "reply",
                            reply = new { id = b.Id, title = b.Title }
                        }).ToList()
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_apiUrl}/{_phoneNumberId}/messages",
                payload,
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("WhatsApp API error: {StatusCode} - {Content}", response.StatusCode, content);
                return new WhatsAppMessageResult
                {
                    Success = false,
                    ErrorMessage = $"API error: {response.StatusCode}"
                };
            }

            dynamic result = JsonConvert.DeserializeObject(content)!;
            string messageId = result.messages[0].id;

            return new WhatsAppMessageResult
            {
                Success = true,
                MessageId = messageId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending interactive buttons to {To}", to);
            return new WhatsAppMessageResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WhatsAppMessageResult> SendInteractiveListAsync(
        string to,
        string bodyText,
        string buttonText,
        List<WhatsAppListSection> sections,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                to,
                type = "interactive",
                interactive = new
                {
                    type = "list",
                    body = new { text = bodyText },
                    action = new
                    {
                        button = buttonText,
                        sections = sections.Select(s => new
                        {
                            title = s.Title,
                            rows = s.Rows.Select(r => new
                            {
                                id = r.Id,
                                title = r.Title,
                                description = r.Description
                            }).ToList()
                        }).ToList()
                    }
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_apiUrl}/{_phoneNumberId}/messages",
                payload,
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("WhatsApp API error: {StatusCode} - {Content}", response.StatusCode, content);
                return new WhatsAppMessageResult
                {
                    Success = false,
                    ErrorMessage = $"API error: {response.StatusCode}"
                };
            }

            dynamic result = JsonConvert.DeserializeObject(content)!;
            string messageId = result.messages[0].id;

            return new WhatsAppMessageResult
            {
                Success = true,
                MessageId = messageId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending interactive list to {To}", to);
            return new WhatsAppMessageResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<WhatsAppMessageResult> SendTemplateMessageAsync(
        string to,
        string templateName,
        string languageCode,
        Dictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var components = new List<object>();

            if (parameters != null && parameters.Any())
            {
                components.Add(new
                {
                    type = "body",
                    parameters = parameters.Select(p => new
                    {
                        type = "text",
                        text = p.Value
                    }).ToList()
                });
            }

            var payload = new
            {
                messaging_product = "whatsapp",
                to,
                type = "template",
                template = new
                {
                    name = templateName,
                    language = new { code = languageCode },
                    components
                }
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_apiUrl}/{_phoneNumberId}/messages",
                payload,
                cancellationToken);

            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("WhatsApp API error: {StatusCode} - {Content}", response.StatusCode, content);
                return new WhatsAppMessageResult
                {
                    Success = false,
                    ErrorMessage = $"API error: {response.StatusCode}"
                };
            }

            dynamic result = JsonConvert.DeserializeObject(content)!;
            string messageId = result.messages[0].id;

            return new WhatsAppMessageResult
            {
                Success = true,
                MessageId = messageId
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending template message to {To}", to);
            return new WhatsAppMessageResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> MarkMessageAsReadAsync(
        string messageId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                messaging_product = "whatsapp",
                status = "read",
                message_id = messageId
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{_apiUrl}/{_phoneNumberId}/messages",
                payload,
                cancellationToken);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking message {MessageId} as read", messageId);
            return false;
        }
    }

    public bool VerifyWebhookSignature(string payload, string signature, string appSecret)
    {
        try
        {
            // Remove "sha256=" prefix if present
            if (signature.StartsWith("sha256="))
            {
                signature = signature.Substring(7);
            }

            // Calculate HMAC-SHA256
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(appSecret));
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var calculatedSignature = BitConverter.ToString(hash).Replace("-", "").ToLower();

            // Compare signatures
            return signature.Equals(calculatedSignature, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying webhook signature");
            return false;
        }
    }

    public async Task<WhatsAppMediaResult> DownloadMediaAsync(
        string mediaId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Downloading media {MediaId}", mediaId);

            // Step 1: Get media URL
            var mediaInfoResponse = await _httpClient.GetAsync(
                $"https://graph.facebook.com/v18.0/{mediaId}",
                cancellationToken);

            if (!mediaInfoResponse.IsSuccessStatusCode)
            {
                var error = await mediaInfoResponse.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to get media info: {StatusCode} - {Error}", 
                    mediaInfoResponse.StatusCode, error);
                return new WhatsAppMediaResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to get media info: {mediaInfoResponse.StatusCode}"
                };
            }

            var mediaInfo = await mediaInfoResponse.Content.ReadAsStringAsync(cancellationToken);
            dynamic mediaData = JsonConvert.DeserializeObject(mediaInfo)!;
            
            string mediaUrl = mediaData.url;
            string mimeType = mediaData.mime_type;

            _logger.LogInformation("Media URL: {Url}, MimeType: {MimeType}", mediaUrl, mimeType);

            // Step 2: Download media file
            var mediaResponse = await _httpClient.GetAsync(mediaUrl, cancellationToken);

            if (!mediaResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Failed to download media: {StatusCode}", mediaResponse.StatusCode);
                return new WhatsAppMediaResult
                {
                    Success = false,
                    ErrorMessage = $"Failed to download media: {mediaResponse.StatusCode}"
                };
            }

            var mediaBytes = await mediaResponse.Content.ReadAsByteArrayAsync(cancellationToken);

            _logger.LogInformation("Media downloaded successfully: {Size} bytes", mediaBytes.Length);

            return new WhatsAppMediaResult
            {
                Success = true,
                Data = mediaBytes,
                MimeType = mimeType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading media {MediaId}", mediaId);
            return new WhatsAppMediaResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}
