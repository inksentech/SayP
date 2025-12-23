using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Entities;
using SayP.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace SayP.Application.Services;

public class SmartConversationManager : ISmartConversationManager
{
    private readonly ISayPDbContext _context;
    private readonly IAIProvider _aiProvider;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<SmartConversationManager> _logger;
    private readonly Dictionary<string, DateTime> _lastResponseTime = new();
    private readonly Dictionary<string, string> _lastResponse = new();

    public SmartConversationManager(
        ISayPDbContext context,
        IAIProvider aiProvider,
        IWhatsAppService whatsAppService,
        ILogger<SmartConversationManager> logger)
    {
        _context = context;
        _aiProvider = aiProvider;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    public async Task<bool> ShouldProcessMessageAsync(string phoneNumber, string messageText, string messageId)
    {
        // Duplicate message check
        var existingMessage = await _context.Messages
            .Where(m => m.WhatsAppMessageId == messageId)
            .FirstOrDefaultAsync();

        if (existingMessage != null)
        {
            _logger.LogInformation("Duplicate message detected: {MessageId}", messageId);
            return false;
        }

        // Rate limiting - prevent spam
        var key = $"rate:{phoneNumber}";
        if (_lastResponseTime.ContainsKey(key))
        {
            var timeSinceLastMessage = DateTime.UtcNow - _lastResponseTime[key];
            if (timeSinceLastMessage.TotalSeconds < 2) // 2 second cooldown
            {
                _logger.LogWarning("Rate limit exceeded for {PhoneNumber}", phoneNumber);
                return false;
            }
        }

        // Context-aware duplicate detection
        if (_lastResponse.ContainsKey(phoneNumber))
        {
            var similarity = CalculateStringSimilarity(messageText, _lastResponse[phoneNumber]);
            if (similarity > 0.8) // 80% similarity threshold
            {
                _logger.LogInformation("Similar message detected, skipping: {PhoneNumber}", phoneNumber);
                return false;
            }
        }

        _lastResponseTime[key] = DateTime.UtcNow;
        _lastResponse[phoneNumber] = messageText;
        return true;
    }

    public async Task<string> GenerateSmartResponseAsync(Conversation conversation, string userMessage)
    {
        try
        {
            // Get recent conversation history (last 5 messages)
            var recentMessages = await _context.Messages
                .Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(5)
                .Select(m => new { m.Content, m.Direction, m.CreatedAt })
                .ToListAsync();

            // Build optimized context
            var contextBuilder = new List<string>();
            
            // Add system context
            contextBuilder.Add("Sen Türkçe konuşan bir iş asistanısın. Kısa, net ve yardımcı yanıtlar ver.");
            
            // Add conversation history (reversed to chronological order)
            foreach (var msg in recentMessages.AsEnumerable().Reverse())
            {
                var role = msg.Direction == SayP.Domain.Enums.MessageDirection.Incoming ? "Kullanıcı" : "Asistan";
                contextBuilder.Add($"{role}: {msg.Content}");
            }
            
            // Add current message
            contextBuilder.Add($"Kullanıcı: {userMessage}");
            contextBuilder.Add("Asistan:");

            var prompt = string.Join("\n", contextBuilder);

            // Limit prompt length to prevent token overflow
            if (prompt.Length > 3000)
            {
                prompt = prompt.Substring(prompt.Length - 3000);
            }

            _logger.LogDebug("Sending optimized prompt to AI (length: {Length})", prompt.Length);

            var response = await _aiProvider.GenerateResponseAsync(prompt);
            
            if (string.IsNullOrEmpty(response))
            {
                return "Üzgünüm, şu anda size yardımcı olamıyorum. Lütfen daha sonra tekrar deneyin.";
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating smart response for conversation {ConversationId}", conversation.Id);
            return "Bir hata oluştu. Lütfen tekrar deneyin.";
        }
    }

    public async Task<bool> DetectIntentAsync(string message, Conversation conversation)
    {
        // Simple intent detection patterns
        var productIntents = new[] { "ürün", "product", "ekle", "oluştur", "create", "add" };
        var greetingIntents = new[] { "merhaba", "selam", "hello", "hi", "hey" };
        var helpIntents = new[] { "yardım", "help", "nasıl", "how" };

        var lowerMessage = message.ToLowerInvariant();

        // Product creation intent
        if (productIntents.Any(intent => lowerMessage.Contains(intent)))
        {
            conversation.Context = JsonSerializer.Serialize(new { intent = "product_creation", confidence = 0.9 });
            await _context.SaveChangesAsync();
            return true;
        }

        // Greeting intent
        if (greetingIntents.Any(intent => lowerMessage.Contains(intent)))
        {
            conversation.Context = JsonSerializer.Serialize(new { intent = "greeting", confidence = 0.8 });
            await _context.SaveChangesAsync();
            return true;
        }

        return false;
    }

    private static double CalculateStringSimilarity(string str1, string str2)
    {
        if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
            return 0;

        var longer = str1.Length > str2.Length ? str1 : str2;
        var shorter = str1.Length > str2.Length ? str2 : str1;

        if (longer.Length == 0)
            return 1.0;

        return (longer.Length - ComputeLevenshteinDistance(longer, shorter)) / (double)longer.Length;
    }

    private static int ComputeLevenshteinDistance(string str1, string str2)
    {
        var matrix = new int[str1.Length + 1, str2.Length + 1];

        for (int i = 0; i <= str1.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= str2.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= str1.Length; i++)
        {
            for (int j = 1; j <= str2.Length; j++)
            {
                var cost = str1[i - 1] == str2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[str1.Length, str2.Length];
    }
}

public interface ISmartConversationManager
{
    Task<bool> ShouldProcessMessageAsync(string phoneNumber, string messageText, string messageId);
    Task<string> GenerateSmartResponseAsync(Conversation conversation, string userMessage);
    Task<bool> DetectIntentAsync(string message, Conversation conversation);
}
