using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayP.Application.Services;
using SayP.Domain.Enums;
using SayP.Infrastructure.Redis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SayP.Api.Controllers;

/// <summary>
/// WhatsApp Cloud API Webhook Controller
/// </summary>
[ApiController]
[Route("api/webhook")]
[AllowAnonymous]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly GenericWhatsAppHandler _genericHandler; // NEW: Generic AI handler
    private readonly RedisCacheService _redis;
    private readonly WhatsAppSignatureValidator _signatureValidator;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        GenericWhatsAppHandler genericHandler, // NEW: Generic AI handler
        RedisCacheService redis,
        WhatsAppSignatureValidator signatureValidator,
        IConfiguration configuration,
        ILogger<WhatsAppWebhookController> logger)
    {
        _genericHandler = genericHandler;
        _redis = redis;
        _signatureValidator = signatureValidator;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Webhook verification endpoint (GET)
    /// </summary>
    [HttpGet]
    public IActionResult VerifyWebhook(
        [FromQuery(Name = "hub.mode")] string mode,
        [FromQuery(Name = "hub.verify_token")] string token,
        [FromQuery(Name = "hub.challenge")] string challenge)
    {
        var verifyToken = _configuration["WHATSAPP_WEBHOOK_VERIFY_TOKEN"] 
            ?? Environment.GetEnvironmentVariable("WHATSAPP_WEBHOOK_VERIFY_TOKEN");

        _logger.LogInformation("Verifying webhook. Mode: {Mode}, Expected Token: {ExpectedToken}, Received Token: {ReceivedToken}", 
            mode, verifyToken, token);

        // Case-insensitive token comparison
        if (mode == "subscribe" && string.Equals(token, verifyToken, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Webhook verified successfully");
            return Ok(challenge);
        }

        _logger.LogWarning("Webhook verification failed. Mode: {Mode}, Token: {Token}, Expected: {ExpectedToken}", 
            mode, token, verifyToken);
        return Forbid();
    }

    /// <summary>
    /// Webhook message handler (POST)
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> HandleWebhook([FromBody] JsonElement payload)
    {
        try
        {
            // Read raw body for signature validation
            Request.EnableBuffering();
            Request.Body.Position = 0;
            using var reader = new StreamReader(Request.Body, leaveOpen: true);
            var rawBody = await reader.ReadToEndAsync();
            Request.Body.Position = 0;

            // Verify signature
            var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            if (!string.IsNullOrEmpty(signature) && !_signatureValidator.ValidateSignature(rawBody, signature))
            {
                _logger.LogWarning("Invalid webhook signature");
                return Unauthorized("Invalid signature");
            }

            _logger.LogInformation("Webhook received: {Payload}", payload.ToString());

            // Parse webhook payload
            if (!payload.TryGetProperty("entry", out var entries))
            {
                return Ok(); // No entries, acknowledge
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (!entry.TryGetProperty("changes", out var changes))
                    continue;

                foreach (var change in changes.EnumerateArray())
                {
                    if (!change.TryGetProperty("value", out var value))
                        continue;

                    // Handle messages
                    if (value.TryGetProperty("messages", out var messages))
                    {
                        foreach (var message in messages.EnumerateArray())
                        {
                            await HandleIncomingMessage(message);
                        }
                    }

                    // Handle status updates
                    if (value.TryGetProperty("statuses", out var statuses))
                    {
                        foreach (var status in statuses.EnumerateArray())
                        {
                            HandleStatusUpdate(status);
                        }
                    }
                }
            }

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook");
            return Ok(); // Always return 200 to acknowledge receipt
        }
    }

    private async Task HandleIncomingMessage(JsonElement message)
    {
        try
        {
            var messageId = message.GetProperty("id").GetString();
            var from = message.GetProperty("from").GetString();
            var timestamp = message.GetProperty("timestamp").GetString();

            if (string.IsNullOrEmpty(messageId) || string.IsNullOrEmpty(from))
            {
                _logger.LogWarning("Invalid message format");
                return;
            }

            // Check idempotency
            var idempotencyKey = $"msg:{messageId}";
            if (await _redis.GetAsync<bool>(idempotencyKey))
            {
                _logger.LogInformation("Duplicate message {MessageId}, skipping", messageId);
                return;
            }

            // Mark as processed
            await _redis.SetAsync(idempotencyKey, true, TimeSpan.FromHours(24));

            // Rate limiting
            if (!await CheckRateLimit(from))
            {
                _logger.LogWarning("Rate limit exceeded for {PhoneNumber}", from);
                return;
            }

            // Extract message content
            string? messageText = null;
            string? mediaId = null;
            string? mimeType = null;
            string? caption = null;
            MessageType messageType = MessageType.Text;

            // Interactive button reply
            if (message.TryGetProperty("interactive", out var interactiveObj))
            {
                if (interactiveObj.TryGetProperty("type", out var interactiveType) && 
                    interactiveType.GetString() == "button_reply")
                {
                    if (interactiveObj.TryGetProperty("button_reply", out var buttonReply))
                    {
                        var buttonId = buttonReply.GetProperty("id").GetString();
                        _logger.LogInformation("Button reply received: {ButtonId}", buttonId);

                        // Handle button reply - treat as text message
                        await _genericHandler.ProcessMessageAsync(
                            phoneNumber: from,
                            messageText: buttonId,
                            messageId: messageId,
                            timestamp: DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp)).UtcDateTime,
                            messageType: MessageType.Interactive
                        );
                        return;
                    }
                }
            }
            // Text message
            else if (message.TryGetProperty("text", out var textObj))
            {
                messageText = textObj.GetProperty("body").GetString();
                messageType = MessageType.Text;
            }
            // Image message
            else if (message.TryGetProperty("image", out var imageObj))
            {
                mediaId = imageObj.GetProperty("id").GetString();
                mimeType = imageObj.TryGetProperty("mime_type", out var mt) ? mt.GetString() : "image/jpeg";
                caption = imageObj.TryGetProperty("caption", out var cap) ? cap.GetString() : null;
                messageType = MessageType.Image;
                _logger.LogInformation("Image message received: {MediaId}, Caption: {Caption}", mediaId, caption);
            }
            // Audio message
            else if (message.TryGetProperty("audio", out var audioObj))
            {
                mediaId = audioObj.GetProperty("id").GetString();
                mimeType = audioObj.TryGetProperty("mime_type", out var mt) ? mt.GetString() : "audio/ogg";
                messageType = MessageType.Audio;
                _logger.LogInformation("Audio message received: {MediaId}", mediaId);
            }
            // Voice message
            else if (message.TryGetProperty("voice", out var voiceObj))
            {
                mediaId = voiceObj.GetProperty("id").GetString();
                mimeType = voiceObj.TryGetProperty("mime_type", out var mt) ? mt.GetString() : "audio/ogg";
                messageType = MessageType.Audio;
                _logger.LogInformation("Voice message received: {MediaId}", mediaId);
            }
            else
            {
                _logger.LogInformation("Unsupported message type received, skipping");
                return;
            }

            _logger.LogInformation("Processing message from {From}, Type: {Type}", from, messageType);

            // 🚀 NEW: Process message through Generic AI handler
            await _genericHandler.ProcessMessageAsync(
                phoneNumber: from,
                messageText: messageText,
                messageId: messageId,
                timestamp: DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp)).UtcDateTime,
                messageType: messageType,
                mediaId: mediaId,
                mimeType: mimeType,
                caption: caption
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling incoming message");
        }
    }

    private void HandleStatusUpdate(JsonElement status)
    {
        try
        {
            var messageId = status.GetProperty("id").GetString();
            var statusValue = status.GetProperty("status").GetString();

            _logger.LogInformation("Message {MessageId} status: {Status}", messageId, statusValue);

            // You can update message status in database here
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling status update");
        }
    }

    private async Task<bool> CheckRateLimit(string phoneNumber)
    {
        var key = $"ratelimit:{phoneNumber}";
        var count = await _redis.GetAsync<int>(key);

        if (count >= 10) // Max 10 messages per minute
        {
            return false;
        }

        await _redis.SetAsync(key, count + 1, TimeSpan.FromMinutes(1));
        return true;
    }
}
