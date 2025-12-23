using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Entities;
using SayP.Domain.Interfaces;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace SayP.Application.Services;

/// <summary>
/// Akıllı intent sınıflandırma servisi - context-aware ve machine learning destekli
/// </summary>
public class SmartIntentClassifier : ISmartIntentClassifier
{
    private readonly IIntentDiscoveryService _intentDiscovery;
    private readonly IAIProvider _aiProvider;
    private readonly ISayPDbContext _context;
    private readonly ILogger<SmartIntentClassifier> _logger;
    private readonly IntentDisambiguator _disambiguator;
    private readonly Dictionary<string, float> _intentScores = new();
    private readonly Dictionary<string, List<string>> _contextPatterns = new();

    public SmartIntentClassifier(
        IIntentDiscoveryService intentDiscovery,
        IAIProvider aiProvider,
        ISayPDbContext context,
        ILogger<SmartIntentClassifier> logger,
        IntentDisambiguator disambiguator)
    {
        _intentDiscovery = intentDiscovery;
        _aiProvider = aiProvider;
        _context = context;
        _logger = logger;
        _disambiguator = disambiguator;
    }

    public async Task<IntentClassificationResult> ClassifyIntentAsync(
        string message, 
        Conversation conversation, 
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Classifying intent for message: {Message}", message);

            // 1. Discover available intents
            var availableIntents = await _intentDiscovery.DiscoverIntentsAsync();

            // 2. Get conversation context
            var context = await GetConversationContextAsync(conversation);

            // 3. Multi-stage classification
            var result = await PerformMultiStageClassificationAsync(message, context, availableIntents);

            // 4. Learn from classification
            await LearnFromClassificationAsync(message, result, conversation);

            _logger.LogDebug("Intent classified as: {Intent} with confidence: {Confidence}", 
                result.IntentName, result.Confidence);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error classifying intent for message: {Message}", message);
            return new IntentClassificationResult
            {
                IntentName = "None",
                Confidence = 0.1f,
                Response = "Üzgünüm, mesajınızı anlayamadım. Lütfen daha açık bir şekilde belirtir misiniz?"
            };
        }
    }

    private async Task<IntentClassificationResult> PerformMultiStageClassificationAsync(
        string message, 
        ConversationContext context, 
        Dictionary<string, IntentDefinition> availableIntents)
    {
        var results = new List<IntentClassificationResult>();

        // Stage 1: Rule-based classification
        var ruleBasedResult = await RuleBasedClassification(message, availableIntents);
        if (ruleBasedResult.Confidence > 0.8f)
        {
            results.Add(ruleBasedResult);
        }

        // Stage 2: Pattern matching
        var patternResult = await PatternBasedClassification(message, availableIntents);
        if (patternResult.Confidence > 0.7f)
        {
            results.Add(patternResult);
        }

        // Stage 3: AI-powered classification
        var aiResult = await AIBasedClassification(message, context, availableIntents);
        results.Add(aiResult);

        // Stage 4: Context-aware refinement
        var contextRefinedResult = await ContextAwareRefinement(results, context);

        // Stage 5: Disambiguation if needed
        if (_disambiguator.NeedsDisambiguation(contextRefinedResult, results))
        {
            var disambiguationMessage = await _disambiguator.GenerateDisambiguationMessageAsync(
                message, 
                contextRefinedResult, 
                results.Where(r => r.IntentName != contextRefinedResult.IntentName).ToList());

            contextRefinedResult.Response = disambiguationMessage;
            contextRefinedResult.RequiresConfirmation = true;
            
            _logger.LogInformation("Disambiguation required for message: {Message}", message);
        }

        return contextRefinedResult;
    }

    private Task<IntentClassificationResult> RuleBasedClassification(
        string message, 
        Dictionary<string, IntentDefinition> availableIntents)
    {
        var normalizedMessage = message.ToLowerInvariant().Trim();
        var bestMatch = new IntentClassificationResult { IntentName = "None", Confidence = 0f };

        foreach (var intent in availableIntents.Values)
        {
            var score = CalculateRuleBasedScore(normalizedMessage, intent);
            if (score > bestMatch.Confidence)
            {
                bestMatch = new IntentClassificationResult
                {
                    IntentName = intent.Name,
                    Confidence = score,
                    ExtractedEntities = ExtractEntitiesFromMessage(normalizedMessage, intent),
                    RequiresConfirmation = intent.RequiresConfirmation
                };
            }
        }

        return Task.FromResult(bestMatch);
    }

    private async Task<IntentClassificationResult> PatternBasedClassification(
        string message, 
        Dictionary<string, IntentDefinition> availableIntents)
    {
        var bestMatch = new IntentClassificationResult { IntentName = "None", Confidence = 0f };

        foreach (var intent in availableIntents.Values)
        {
            var patterns = await _intentDiscovery.GetPatternsForIntentAsync(intent.Name);
            var score = CalculatePatternScore(message, patterns);
            
            if (score > bestMatch.Confidence)
            {
                bestMatch = new IntentClassificationResult
                {
                    IntentName = intent.Name,
                    Confidence = score,
                    ExtractedEntities = ExtractEntitiesFromMessage(message, intent),
                    RequiresConfirmation = intent.RequiresConfirmation
                };
            }
        }

        return bestMatch;
    }

    private async Task<IntentClassificationResult> AIBasedClassification(
        string message, 
        ConversationContext context, 
        Dictionary<string, IntentDefinition> availableIntents)
    {
        try
        {
            var prompt = BuildAIClassificationPrompt(message, context, availableIntents);
            var aiResponse = await _aiProvider.GenerateResponseAsync(prompt);
            
            return ParseAIClassificationResponse(aiResponse, availableIntents);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI classification failed, falling back to rule-based");
            return new IntentClassificationResult { IntentName = "None", Confidence = 0.3f };
        }
    }

    private string BuildAIClassificationPrompt(
        string message, 
        ConversationContext context, 
        Dictionary<string, IntentDefinition> availableIntents)
    {
        var intentList = string.Join("\n", availableIntents.Values.Select(i => 
            $"• {i.Name}: {i.Description} (örnek: {string.Join(", ", i.Examples.Take(2))})"));

        return $@"Sen akıllı bir intent sınıflandırıcısın. Kullanıcı mesajını analiz et ve en uygun intent'i belirle.

MEVCUT İNTENT'LER:
{intentList}

ÖNEMLİ NOTLAR:
- ""randevu"" kelimesi varsa → CreateAppointment kullan (CreateOrder DEĞİL!)
- ""sipariş"" kelimesi varsa → CreateOrder kullan
- ""ürün"" kelimesi varsa → CreateProduct kullan
- ""müşteri"" veya ""firma"" varsa → CreateCustomer kullan

KONUŞMA CONTEXT'İ:
- Son mesajlar: {string.Join(" → ", context.RecentMessages.Take(3))}
- Bekleyen işlem: {context.PendingAction ?? "Yok"}
- Kullanıcı profili: {context.UserProfile}

KULLANICI MESAJI: ""{message}""

ÖRNEKLER:
• ""Yarın saat 14:00 için saç kesimi randevusu oluştur"" → CreateAppointment
• ""Laptop ürünü oluştur 15000 TL"" → CreateProduct
• ""Müşteri ekle Ahmet Yılmaz 0532 123 4567"" → CreateCustomer
• ""Bugünün randevularını göster"" → ListAppointments

JSON formatında yanıt ver:
{{
  ""intentName"": ""CreateProduct"",
  ""confidence"": 0.95,
  ""extractedEntities"": {{
    ""name"": ""Çiçek"",
    ""price"": 50
  }},
  ""reasoning"": ""Kullanıcı yeni bir ürün oluşturmak istiyor""
}}";
    }

    private IntentClassificationResult ParseAIClassificationResponse(
        string aiResponse, 
        Dictionary<string, IntentDefinition> availableIntents)
    {
        try
        {
            // JSON'dan parse et
            var jsonStart = aiResponse.IndexOf('{');
            var jsonEnd = aiResponse.LastIndexOf('}') + 1;
            
            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonContent = aiResponse.Substring(jsonStart, jsonEnd - jsonStart);
                var parsed = JsonSerializer.Deserialize<JsonElement>(jsonContent);
                
                var intentName = parsed.GetProperty("intentName").GetString() ?? "None";
                var confidence = parsed.GetProperty("confidence").GetSingle();
                
                var entities = new Dictionary<string, object>();
                if (parsed.TryGetProperty("extractedEntities", out var entitiesElement))
                {
                    foreach (var prop in entitiesElement.EnumerateObject())
                    {
                        entities[prop.Name] = prop.Value.ToString() ?? "";
                    }
                }

                return new IntentClassificationResult
                {
                    IntentName = intentName,
                    Confidence = confidence,
                    ExtractedEntities = entities,
                    RequiresConfirmation = availableIntents.GetValueOrDefault(intentName)?.RequiresConfirmation ?? false
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI classification response: {Response}", aiResponse);
        }

        return new IntentClassificationResult { IntentName = "None", Confidence = 0.2f };
    }

    private Task<IntentClassificationResult> ContextAwareRefinement(
        List<IntentClassificationResult> results, 
        ConversationContext context)
    {
        if (!results.Any())
        {
            return Task.FromResult(new IntentClassificationResult { IntentName = "None", Confidence = 0f });
        }

        // En yüksek confidence'a sahip sonucu al
        var bestResult = results.OrderByDescending(r => r.Confidence).First();

        // Context ile refine et
        if (!string.IsNullOrEmpty(context.PendingAction))
        {
            // Bekleyen işlem varsa confidence'ı artır
            if (bestResult.IntentName.Contains(context.PendingAction, StringComparison.OrdinalIgnoreCase))
            {
                bestResult.Confidence = Math.Min(1.0f, bestResult.Confidence + 0.2f);
            }
        }

        // Son mesajlarla tutarlılık kontrolü
        if (context.RecentIntents.Any())
        {
            var recentSimilar = context.RecentIntents
                .Count(ri => ri.Contains(bestResult.IntentName.Split(new[] { "Create", "Update", "Delete", "Get", "List" }, 
                    StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "", StringComparison.OrdinalIgnoreCase));
            
            if (recentSimilar > 0)
            {
                bestResult.Confidence = Math.Min(1.0f, bestResult.Confidence + (recentSimilar * 0.1f));
            }
        }

        return Task.FromResult(bestResult);
    }

    private float CalculateRuleBasedScore(string message, IntentDefinition intent)
    {
        var score = 0f;
        var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Intent name'deki anahtar kelimeler
        var intentKeywords = ExtractKeywordsFromIntentName(intent.Name);
        foreach (var keyword in intentKeywords)
        {
            if (message.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                score += 0.3f;
            }
        }

        // Required field'lar için kontrol
        foreach (var field in intent.RequiredFields)
        {
            if (ContainsFieldReference(message, field))
            {
                score += 0.2f;
            }
        }

        return Math.Min(1.0f, score);
    }

    private float CalculatePatternScore(string message, List<string> patterns)
    {
        if (!patterns.Any()) return 0f;

        var maxScore = 0f;
        foreach (var pattern in patterns)
        {
            var similarity = CalculateStringSimilarity(message, pattern);
            maxScore = Math.Max(maxScore, similarity);
        }

        return maxScore;
    }

    private List<string> ExtractKeywordsFromIntentName(string intentName)
    {
        var keywords = new List<string>();
        
        // CRUD operations
        if (intentName.StartsWith("Create")) keywords.AddRange(new[] { "oluştur", "ekle", "yarat", "kaydet" });
        if (intentName.StartsWith("Get")) keywords.AddRange(new[] { "göster", "getir", "detay", "bilgi" });
        if (intentName.StartsWith("Update")) keywords.AddRange(new[] { "güncelle", "değiştir", "düzenle" });
        if (intentName.StartsWith("Delete")) keywords.AddRange(new[] { "sil", "kaldır", "iptal" });
        if (intentName.StartsWith("List")) keywords.AddRange(new[] { "listele", "göster", "tümü" });

        return keywords;
    }

    private bool ContainsFieldReference(string message, string field)
    {
        var fieldPatterns = field.ToLower() switch
        {
            "name" => new[] { "ad", "isim", "adı" },
            "price" => new[] { "fiyat", "ücret", "tutar", "tl", "₺" },
            "description" => new[] { "açıklama", "detay", "bilgi" },
            "email" => new[] { "email", "eposta", "mail" },
            "phone" => new[] { "telefon", "tel", "numara" },
            _ => new[] { field }
        };

        return fieldPatterns.Any(pattern => message.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private Dictionary<string, object> ExtractEntitiesFromMessage(string message, IntentDefinition intent)
    {
        var entities = new Dictionary<string, object>();

        // Price extraction
        var priceMatch = Regex.Match(message, @"(\d+(?:[.,]\d+)?)\s*(?:tl|₺|lira)", RegexOptions.IgnoreCase);
        if (priceMatch.Success && intent.RequiredFields.Contains("price"))
        {
            if (decimal.TryParse(priceMatch.Groups[1].Value.Replace(',', '.'), out var price))
            {
                entities["price"] = price;
            }
        }

        // Name extraction (simple approach - can be enhanced)
        if (intent.RequiredFields.Contains("name"))
        {
            var namePatterns = new[] { @"adı\s+([^\s,]+)", @"ismi\s+([^\s,]+)", @"([^\s,]+)\s+(?:oluştur|ekle)" };
            foreach (var pattern in namePatterns)
            {
                var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    entities["name"] = match.Groups[1].Value;
                    break;
                }
            }
        }

        return entities;
    }

    private async Task<ConversationContext> GetConversationContextAsync(Conversation conversation)
    {
        try
        {
            var recentMessages = await _context.Messages
                .Where(m => m.ConversationId == conversation.Id)
                .OrderByDescending(m => m.CreatedAt)
                .Take(10)
                .Select(m => m.Content)
                .ToListAsync();

            var contextDict = new Dictionary<string, object>();
            try
            {
                if (!string.IsNullOrEmpty(conversation.Context))
                {
                    contextDict = JsonSerializer.Deserialize<Dictionary<string, object>>(conversation.Context) ?? new Dictionary<string, object>();
                }
            }
            catch
            {
                contextDict = new Dictionary<string, object>();
            }

            return new ConversationContext
            {
                RecentMessages = recentMessages,
                PendingAction = contextDict.GetValueOrDefault("pendingAction")?.ToString(),
                UserProfile = "Standard User", // This could be enhanced
                RecentIntents = ExtractRecentIntents(recentMessages)
            };
        }
        catch
        {
            return new ConversationContext
            {
                RecentMessages = new List<string>(),
                UserProfile = "Standard User"
            };
        }
    }

    private List<string> ExtractRecentIntents(List<string> recentMessages)
    {
        // Bu basit bir implementasyon - gerçekte daha sofistike olabilir
        var intents = new List<string>();
        foreach (var message in recentMessages)
        {
            if (message.Contains("oluştur", StringComparison.OrdinalIgnoreCase)) intents.Add("Create");
            if (message.Contains("listele", StringComparison.OrdinalIgnoreCase)) intents.Add("List");
            if (message.Contains("güncelle", StringComparison.OrdinalIgnoreCase)) intents.Add("Update");
            if (message.Contains("sil", StringComparison.OrdinalIgnoreCase)) intents.Add("Delete");
        }
        return intents;
    }

    private async Task LearnFromClassificationAsync(
        string message, 
        IntentClassificationResult result, 
        Conversation conversation)
    {
        try
        {
            // High confidence classifications için pattern'leri kaydet
            if (result.Confidence > 0.8f && !string.IsNullOrEmpty(result.IntentName))
            {
                // Pattern'i hafızaya al
                if (!_contextPatterns.ContainsKey(result.IntentName))
                {
                    _contextPatterns[result.IntentName] = new List<string>();
                }
                
                var normalizedMessage = message.ToLowerInvariant().Trim();
                if (!_contextPatterns[result.IntentName].Contains(normalizedMessage))
                {
                    _contextPatterns[result.IntentName].Add(normalizedMessage);
                    
                    // En fazla 50 pattern tut (memory management)
                    if (_contextPatterns[result.IntentName].Count > 50)
                    {
                        _contextPatterns[result.IntentName].RemoveAt(0);
                    }
                }
                
                // Intent score'u güncelle
                if (!_intentScores.ContainsKey(result.IntentName))
                {
                    _intentScores[result.IntentName] = 0;
                }
                
                // Başarılı classification için score artır
                _intentScores[result.IntentName] += 0.1f;
                
                _logger.LogDebug("Learned pattern for intent {Intent}, Score: {Score}", 
                    result.IntentName, _intentScores[result.IntentName]);
            }
            
            // Düşük confidence için negatif feedback
            if (result.Confidence < 0.3f && !string.IsNullOrEmpty(result.IntentName))
            {
                if (_intentScores.ContainsKey(result.IntentName))
                {
                    _intentScores[result.IntentName] = Math.Max(0, _intentScores[result.IntentName] - 0.05f);
                }
            }
            
            // Database'e classification history kaydet (analytics için)
            await SaveClassificationHistoryAsync(message, result, conversation);
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error learning from classification");
        }
    }

    private async Task SaveClassificationHistoryAsync(
        string message,
        IntentClassificationResult result,
        Conversation conversation)
    {
        try
        {
            // Classification history'yi database'e kaydet
            // Bu analytics ve model improvement için kullanılabilir
            var history = new
            {
                ConversationId = conversation.Id,
                Message = message,
                IntentName = result.IntentName,
                Confidence = result.Confidence,
                ExtractedEntities = result.ExtractedEntities,
                Timestamp = DateTime.UtcNow
            };
            
            // Context'e ekle (JSON olarak)
            var contextData = new Dictionary<string, object>
            {
                { "lastIntent", result.IntentName },
                { "lastConfidence", result.Confidence },
                { "timestamp", DateTime.UtcNow }
            };
            
            conversation.Context = JsonSerializer.Serialize(contextData);
            await _context.SaveChangesAsync();
            
            _logger.LogDebug("Saved classification history for conversation {ConversationId}", conversation.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not save classification history");
        }
    }

    private float CalculateStringSimilarity(string str1, string str2)
    {
        if (string.IsNullOrEmpty(str1) || string.IsNullOrEmpty(str2))
            return 0;

        var longer = str1.Length > str2.Length ? str1 : str2;
        var shorter = str1.Length > str2.Length ? str2 : str1;

        if (longer.Length == 0)
            return 1.0f;

        return (longer.Length - ComputeLevenshteinDistance(longer, shorter)) / (float)longer.Length;
    }

    private int ComputeLevenshteinDistance(string str1, string str2)
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

public class IntentClassificationResult
{
    public string IntentName { get; set; } = "None";
    public float Confidence { get; set; }
    public Dictionary<string, object> ExtractedEntities { get; set; } = new();
    public bool RequiresConfirmation { get; set; }
    public string? Response { get; set; }
    public string? ConfirmationMessage { get; set; }
    public List<string> MissingFields { get; set; } = new();
    public List<string> Suggestions { get; set; } = new();
}

public class ConversationContext
{
    public List<string> RecentMessages { get; set; } = new();
    public string? PendingAction { get; set; }
    public string UserProfile { get; set; } = string.Empty;
    public List<string> RecentIntents { get; set; } = new();
}

public interface ISmartIntentClassifier
{
    Task<IntentClassificationResult> ClassifyIntentAsync(
        string message, 
        Conversation conversation, 
        CancellationToken cancellationToken = default);
}
