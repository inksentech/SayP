using Microsoft.AspNetCore.SignalR;
using SayP.Application.Interfaces;
using SayP.Application.Services;
using SayP.Infrastructure.Persistence;

namespace SayP.Api.Hubs;

/// <summary>
/// SignalR Hub for real-time chat communication
/// Simulates WhatsApp messaging flow
/// </summary>
public class ChatHub : Hub
{
    private readonly GenericConversationManager _conversationManager;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        GenericConversationManager conversationManager,
        ILogger<ChatHub> logger)
    {
        _conversationManager = conversationManager;
        _logger = logger;
    }

    /// <summary>
    /// Send a message and get AI response
    /// </summary>
    public async Task SendMessage(ChatMessageRequest request)
    {
        try
        {
            _logger.LogInformation("📨 Received message from {Phone}: {Message}", 
                request.PhoneNumber, request.Message);

            // Send typing indicator
            await Clients.Caller.SendAsync("TypingIndicator", true);

            // Parse IDs
            Guid? tenantId = string.IsNullOrEmpty(request.TenantId) ? null : Guid.Parse(request.TenantId);
            Guid? companyId = string.IsNullOrEmpty(request.CompanyId) ? null : Guid.Parse(request.CompanyId);

            string responseText;

            // Check if this is a simple greeting or question (no backend needed)
            if (IsSimpleMessage(request.Message))
            {
                responseText = GetSimpleResponse(request.Message);
            }
            else
            {
                // Use GenericConversationManager for full AI pipeline with slot filling
                var processingResult = await _conversationManager.ProcessMessageAsync(
                    phoneNumber: request.PhoneNumber,
                    message: request.Message,
                    tenantId: tenantId ?? Guid.Empty,
                    backendUrl: request.BackendUrl,
                    apiKey: null
                );

                if (processingResult.RequiresMoreInfo)
                {
                    // Need more information from user (slot filling)
                    responseText = $"❓ {processingResult.Response}";
                }
                else if (processingResult.RequiresConfirmation)
                {
                    // Need user confirmation before executing
                    responseText = $"⚠️ {processingResult.Response}";
                }
                else if (processingResult.Success)
                {
                    // Successfully executed
                    responseText = processingResult.Response;
                }
                else
                {
                    // Error or not understood
                    responseText = processingResult.Response;
                    
                    // Show suggested endpoints if available
                    if (processingResult.SuggestedEndpoints?.Any() == true)
                    {
                        responseText += "\n\n💡 Belki şunlardan birini mi demek istediniz?\n";
                        foreach (var endpoint in processingResult.SuggestedEndpoints.Take(3))
                        {
                            responseText += $"• {endpoint.Description}\n";
                        }
                    }
                }
            }

            // Stop typing indicator
            await Clients.Caller.SendAsync("TypingIndicator", false);

            // Send response
            await Clients.Caller.SendAsync("ReceiveMessage", new
            {
                text = responseText,
                timestamp = DateTime.UtcNow,
                sender = "bot"
            });

            _logger.LogInformation("✅ Sent response to {Phone}", request.PhoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing message from {Phone}", request.PhoneNumber);
            
            await Clients.Caller.SendAsync("TypingIndicator", false);
            await Clients.Caller.SendAsync("ReceiveMessage", new
            {
                text = $"Sorry, an error occurred: {ex.Message}",
                timestamp = DateTime.UtcNow,
                sender = "system"
            });
        }
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("🔌 Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("🔌 Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Check if message is a simple greeting/question that doesn't need backend
    /// </summary>
    private bool IsSimpleMessage(string message)
    {
        // Clean message: remove special chars, extra spaces
        var cleaned = System.Text.RegularExpressions.Regex.Replace(message, @"[^\w\s]", " ");
        var lower = cleaned.ToLowerInvariant().Trim();
        
        var simplePatterns = new[]
        {
            "merhaba", "selam", "hey", "hi", "hello",
            "nasılsın", "nasıl gidiyor", "naber", "ne haber",
            "ne yapabilirsin", "neler yapabilirsin", "yardım", "help",
            "kim", "kimsin", "ne", "nedir", "tanış",
            "günaydın", "iyi günler", "iyi akşamlar", "iyi geceler"
        };

        return simplePatterns.Any(p => lower.Contains(p));
    }

    /// <summary>
    /// Get response for simple messages
    /// </summary>
    private string GetSimpleResponse(string message)
    {
        var lower = message.ToLowerInvariant().Trim();

        if (lower.Contains("merhaba") || lower.Contains("selam") || lower.Contains("hey") || 
            lower.Contains("hi") || lower.Contains("hello"))
        {
            return "👋 Merhaba! Ben SayP AI asistanınızım. Size nasıl yardımcı olabilirim?";
        }

        if (lower.Contains("nasılsın") || lower.Contains("naber") || lower.Contains("ne haber"))
        {
            return "😊 İyiyim, teşekkür ederim! Size nasıl yardımcı olabilirim?";
        }

        if (lower.Contains("ne yapabilirsin") || lower.Contains("neler yapabilirsin") || 
            lower.Contains("yardım") || lower.Contains("help"))
        {
            return @"🤖 Ben SayP AI asistanıyım. Şunları yapabilirim:

📋 **Komutlar** (Backend gerekli):
• Kod şablonu oluştur
• Müşteri ekle/listele
• Ürün oluştur
• Randevu al
• Fatura oluştur

💬 **Basit Sohbet**:
• Selamlaşma
• Genel sorular
• Yardım

Backend bağlantısı için Settings'den Backend URL'i ayarlayın.";
        }

        if (lower.Contains("kim") || lower.Contains("kimsin") || lower.Contains("tanış"))
        {
            return "🤖 Ben SayP AI asistanıyım. Doğal dil ile komutlarınızı anlayıp işlemlerinizi gerçekleştirebilirim.";
        }

        if (lower.Contains("günaydın"))
        {
            return "🌅 Günaydın! Güzel bir gün olsun. Size nasıl yardımcı olabilirim?";
        }

        if (lower.Contains("iyi günler"))
        {
            return "☀️ İyi günler! Size nasıl yardımcı olabilirim?";
        }

        if (lower.Contains("iyi akşamlar"))
        {
            return "🌆 İyi akşamlar! Size nasıl yardımcı olabilirim?";
        }

        if (lower.Contains("iyi geceler"))
        {
            return "🌙 İyi geceler! İyi dinlenmeler.";
        }

        return "👋 Merhaba! Size nasıl yardımcı olabilirim?";
    }
}

/// <summary>
/// Chat message request model
/// </summary>
public class ChatMessageRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string? CompanyId { get; set; }
    public string BackendUrl { get; set; } = string.Empty;
}
