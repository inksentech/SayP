using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SayP.Domain.Entities;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;
using System.Text;
using System.Text.RegularExpressions;

namespace SayP.Application.Services;

/// <summary>
/// Smart context manager with semantic compression
/// </summary>
public class SmartContextManager
{
    private readonly ISayPDbContext _context;
    private readonly ILogger<SmartContextManager> _logger;
    private const int MAX_CONTEXT_LENGTH = 2000; // characters
    private const int MAX_MESSAGES_TO_ANALYZE = 20;

    public SmartContextManager(
        ISayPDbContext context,
        ILogger<SmartContextManager> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Build optimized conversation context with semantic compression
    /// </summary>
    public async Task<string> BuildSmartContextAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        // Get recent messages
        var messages = await _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(MAX_MESSAGES_TO_ANALYZE)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Direction, m.Content, m.CreatedAt, m.Type })
            .ToListAsync(cancellationToken);

        if (!messages.Any())
            return string.Empty;

        // Extract key information
        var extractedInfo = ExtractKeyInformation(messages.Select(m => m.Content).ToList());
        
        // Get recent conversation flow (last 3 messages)
        var recentMessages = messages.TakeLast(3).ToList();
        
        // Build compressed context
        var contextBuilder = new StringBuilder();
        
        // Add extracted entities and decisions
        if (extractedInfo.Entities.Any())
        {
            contextBuilder.AppendLine("📌 Önemli Bilgiler:");
            foreach (var entity in extractedInfo.Entities.Take(5))
            {
                contextBuilder.AppendLine($"  • {entity.Key}: {entity.Value}");
            }
            contextBuilder.AppendLine();
        }

        if (extractedInfo.Decisions.Any())
        {
            contextBuilder.AppendLine("✅ Alınan Kararlar:");
            foreach (var decision in extractedInfo.Decisions.Take(3))
            {
                contextBuilder.AppendLine($"  • {decision}");
            }
            contextBuilder.AppendLine();
        }

        // Add recent conversation
        contextBuilder.AppendLine("💬 Son Mesajlar:");
        foreach (var msg in recentMessages)
        {
            var role = msg.Direction == MessageDirection.Incoming ? "Kullanıcı" : "Asistan";
            var content = msg.Type == MessageType.Text ? msg.Content : $"[{msg.Type}]";
            contextBuilder.AppendLine($"  {role}: {TruncateMessage(content, 150)}");
        }

        var context = contextBuilder.ToString();
        
        // Ensure context doesn't exceed max length
        if (context.Length > MAX_CONTEXT_LENGTH)
        {
            context = context.Substring(0, MAX_CONTEXT_LENGTH) + "...";
            _logger.LogDebug("Context truncated to {Length} characters", MAX_CONTEXT_LENGTH);
        }

        _logger.LogInformation("Built smart context: {Length} chars, {Entities} entities, {Decisions} decisions",
            context.Length, extractedInfo.Entities.Count, extractedInfo.Decisions.Count);

        return context;
    }

    /// <summary>
    /// Extract key information from messages
    /// </summary>
    private ExtractedInformation ExtractKeyInformation(List<string> messages)
    {
        var info = new ExtractedInformation();

        foreach (var message in messages)
        {
            if (string.IsNullOrWhiteSpace(message))
                continue;

            // Extract entities
            ExtractEntities(message, info.Entities);
            
            // Extract decisions
            ExtractDecisions(message, info.Decisions);
        }

        return info;
    }

    /// <summary>
    /// Extract entities (names, prices, dates, etc.)
    /// </summary>
    private void ExtractEntities(string message, Dictionary<string, string> entities)
    {
        // Extract product/customer names (quoted or capitalized)
        var nameMatches = Regex.Matches(message, @"[""']([^""']+)[""']|(?:ad[ıi]|isim[li]?)[\s:]+([A-Za-zğüşıöçĞÜŞİÖÇ\s]+?)(?:\s|,|$)");
        foreach (Match match in nameMatches)
        {
            var name = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (!string.IsNullOrWhiteSpace(name) && name.Length > 2)
            {
                entities["İsim"] = name.Trim();
            }
        }

        // Extract prices
        var priceMatch = Regex.Match(message, @"(\d+[\.,]?\d*)\s*(?:TL|tl|₺|lira)", RegexOptions.IgnoreCase);
        if (priceMatch.Success)
        {
            entities["Fiyat"] = priceMatch.Groups[1].Value + " TL";
        }

        // Extract quantities
        var quantityMatch = Regex.Match(message, @"(\d+)\s*(?:adet|tane|piece)", RegexOptions.IgnoreCase);
        if (quantityMatch.Success)
        {
            entities["Miktar"] = quantityMatch.Groups[1].Value + " adet";
        }

        // Extract dates
        if (Regex.IsMatch(message, @"\bbugün\b", RegexOptions.IgnoreCase))
        {
            entities["Tarih"] = "Bugün";
        }
        else if (Regex.IsMatch(message, @"\byarın\b", RegexOptions.IgnoreCase))
        {
            entities["Tarih"] = "Yarın";
        }

        // Extract phone numbers
        var phoneMatch = Regex.Match(message, @"(\+?\d[\d\s\-\(\)]{8,})");
        if (phoneMatch.Success)
        {
            entities["Telefon"] = phoneMatch.Groups[1].Value.Trim();
        }

        // Extract email
        var emailMatch = Regex.Match(message, @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b");
        if (emailMatch.Success)
        {
            entities["Email"] = emailMatch.Value;
        }
    }

    /// <summary>
    /// Extract decisions and confirmations
    /// </summary>
    private void ExtractDecisions(string message, List<string> decisions)
    {
        var lowerMessage = message.ToLowerInvariant();

        // Detect confirmations
        if (Regex.IsMatch(lowerMessage, @"\b(onaylandı|tamamlandı|oluşturuldu|kaydedildi|başarılı)\b"))
        {
            // Extract what was confirmed
            if (lowerMessage.Contains("ürün"))
                decisions.Add("Ürün işlemi tamamlandı");
            else if (lowerMessage.Contains("müşteri"))
                decisions.Add("Müşteri işlemi tamamlandı");
            else if (lowerMessage.Contains("fatura"))
                decisions.Add("Fatura işlemi tamamlandı");
            else
                decisions.Add("İşlem tamamlandı");
        }

        // Detect rejections
        if (Regex.IsMatch(lowerMessage, @"\b(iptal|hayır|olmaz|vazgeç)\b"))
        {
            decisions.Add("İşlem iptal edildi");
        }

        // Detect pending actions
        if (Regex.IsMatch(lowerMessage, @"\b(eklemek|oluşturmak|güncellemek|silmek)\s+(ister|istiyor)\b"))
        {
            var actionMatch = Regex.Match(lowerMessage, @"\b(eklemek|oluşturmak|güncellemek|silmek)");
            if (actionMatch.Success)
            {
                decisions.Add($"Kullanıcı {actionMatch.Value} istiyor");
            }
        }
    }

    /// <summary>
    /// Truncate message to max length
    /// </summary>
    private string TruncateMessage(string message, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        if (message.Length <= maxLength)
            return message;

        return message.Substring(0, maxLength) + "...";
    }

    /// <summary>
    /// Calculate context relevance score
    /// </summary>
    public double CalculateRelevanceScore(string message, string context)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(context))
            return 0;

        var messageWords = ExtractKeywords(message);
        var contextWords = ExtractKeywords(context);

        if (!messageWords.Any() || !contextWords.Any())
            return 0;

        // Calculate overlap
        var overlap = messageWords.Intersect(contextWords, StringComparer.OrdinalIgnoreCase).Count();
        var score = (double)overlap / Math.Max(messageWords.Count, contextWords.Count);

        return score;
    }

    /// <summary>
    /// Extract keywords from text
    /// </summary>
    private HashSet<string> ExtractKeywords(string text)
    {
        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bir", "bu", "şu", "o", "ve", "veya", "ile", "için", "mi", "mı", "mu", "mü",
            "da", "de", "ta", "te", "ki", "gibi", "kadar", "daha", "çok", "az",
            "the", "a", "an", "and", "or", "but", "in", "on", "at", "to", "for"
        };

        var words = Regex.Matches(text, @"\b\w{3,}\b")
            .Cast<Match>()
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => !stopWords.Contains(w))
            .ToHashSet();

        return words;
    }

    /// <summary>
    /// Get conversation summary
    /// </summary>
    public async Task<ConversationSummary> GetConversationSummaryAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var messages = await _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        var summary = new ConversationSummary
        {
            TotalMessages = messages.Count,
            UserMessages = messages.Count(m => m.Direction == MessageDirection.Incoming),
            AssistantMessages = messages.Count(m => m.Direction == MessageDirection.Outgoing),
            FirstMessageAt = messages.FirstOrDefault()?.CreatedAt,
            LastMessageAt = messages.LastOrDefault()?.CreatedAt
        };

        // Extract key topics
        var allContent = string.Join(" ", messages.Select(m => m.Content));
        var extractedInfo = ExtractKeyInformation(messages.Select(m => m.Content).ToList());
        
        summary.KeyEntities = extractedInfo.Entities;
        summary.Decisions = extractedInfo.Decisions;

        return summary;
    }

    private class ExtractedInformation
    {
        public Dictionary<string, string> Entities { get; set; } = new();
        public List<string> Decisions { get; set; } = new();
    }
}

public class ConversationSummary
{
    public int TotalMessages { get; set; }
    public int UserMessages { get; set; }
    public int AssistantMessages { get; set; }
    public DateTime? FirstMessageAt { get; set; }
    public DateTime? LastMessageAt { get; set; }
    public Dictionary<string, string> KeyEntities { get; set; } = new();
    public List<string> Decisions { get; set; } = new();
}
