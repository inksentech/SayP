using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Entities;
using System.Text;

namespace SayP.Application.Services;

/// <summary>
/// Handles intent disambiguation when multiple intents are possible
/// </summary>
public class IntentDisambiguator
{
    private readonly ILogger<IntentDisambiguator> _logger;
    private readonly IIntentDiscoveryService _intentDiscovery;
    private const float DISAMBIGUATION_THRESHOLD = 0.6f;
    private const int MAX_DISAMBIGUATION_OPTIONS = 5;

    public IntentDisambiguator(
        ILogger<IntentDisambiguator> logger,
        IIntentDiscoveryService intentDiscovery)
    {
        _logger = logger;
        _intentDiscovery = intentDiscovery;
    }

    /// <summary>
    /// Check if disambiguation is needed
    /// </summary>
    public bool NeedsDisambiguation(IntentClassificationResult primaryResult, List<IntentClassificationResult>? alternativeResults = null)
    {
        // If confidence is very low, we need disambiguation
        if (primaryResult.Confidence < DISAMBIGUATION_THRESHOLD)
        {
            _logger.LogDebug("Low confidence ({Confidence}), disambiguation needed", primaryResult.Confidence);
            return true;
        }

        // If we have multiple high-confidence alternatives
        if (alternativeResults != null && alternativeResults.Any())
        {
            var closeAlternatives = alternativeResults
                .Where(r => r.Confidence >= DISAMBIGUATION_THRESHOLD - 0.1f)
                .ToList();

            if (closeAlternatives.Count > 0)
            {
                _logger.LogDebug("Found {Count} close alternatives, disambiguation needed", closeAlternatives.Count);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Generate disambiguation message with options
    /// </summary>
    public async Task<string> GenerateDisambiguationMessageAsync(
        string userMessage,
        IntentClassificationResult primaryResult,
        List<IntentClassificationResult>? alternativeResults = null)
    {
        var options = new List<DisambiguationOption>();

        // Add primary result if it has reasonable confidence
        if (primaryResult.Confidence >= 0.3f)
        {
            var intentDef = await _intentDiscovery.GetIntentDefinitionAsync(primaryResult.IntentName);
            if (intentDef != null)
            {
                options.Add(new DisambiguationOption
                {
                    IntentName = primaryResult.IntentName,
                    Description = intentDef.Description,
                    Confidence = primaryResult.Confidence,
                    Example = intentDef.Examples.FirstOrDefault() ?? ""
                });
            }
        }

        // Add alternatives
        if (alternativeResults != null)
        {
            foreach (var alt in alternativeResults.Take(MAX_DISAMBIGUATION_OPTIONS - 1))
            {
                if (alt.Confidence >= 0.3f && alt.IntentName != primaryResult.IntentName)
                {
                    var intentDef = await _intentDiscovery.GetIntentDefinitionAsync(alt.IntentName);
                    if (intentDef != null)
                    {
                        options.Add(new DisambiguationOption
                        {
                            IntentName = alt.IntentName,
                            Description = intentDef.Description,
                            Confidence = alt.Confidence,
                            Example = intentDef.Examples.FirstOrDefault() ?? ""
                        });
                    }
                }
            }
        }

        // Sort by confidence
        options = options.OrderByDescending(o => o.Confidence).Take(MAX_DISAMBIGUATION_OPTIONS).ToList();

        if (!options.Any())
        {
            return "Üzgünüm, ne yapmak istediğinizi anlayamadım. Lütfen daha açık bir şekilde belirtir misiniz?";
        }

        // Build message
        var message = new StringBuilder();
        message.AppendLine("🤔 Tam olarak anlayamadım. Şunlardan hangisini demek istediniz?\n");

        for (int i = 0; i < options.Count; i++)
        {
            var option = options[i];
            var emoji = GetEmojiForIntent(option.IntentName);
            var confidenceBar = GenerateConfidenceBar(option.Confidence);
            
            message.AppendLine($"{i + 1}. {emoji} **{GetTurkishDescription(option.IntentName)}**");
            message.AppendLine($"   {confidenceBar} {option.Confidence:P0}");
            
            if (!string.IsNullOrEmpty(option.Example))
            {
                message.AppendLine($"   _Örnek: {option.Example}_");
            }
            message.AppendLine();
        }

        message.AppendLine("Lütfen numarasını yazarak seçin (1-" + options.Count + ")");
        message.AppendLine("veya daha açık bir şekilde belirtin.");

        _logger.LogInformation("Generated disambiguation message with {Count} options", options.Count);

        return message.ToString();
    }

    /// <summary>
    /// Parse user's disambiguation choice
    /// </summary>
    public (bool IsValid, string? SelectedIntent) ParseDisambiguationChoice(string userInput, List<string> availableIntents)
    {
        if (string.IsNullOrWhiteSpace(userInput))
            return (false, null);

        // Try to parse as number
        if (int.TryParse(userInput.Trim(), out int choice))
        {
            if (choice >= 1 && choice <= availableIntents.Count)
            {
                var selectedIntent = availableIntents[choice - 1];
                _logger.LogInformation("User selected option {Choice}: {Intent}", choice, selectedIntent);
                return (true, selectedIntent);
            }
        }

        // Try to match by keyword
        var lowerInput = userInput.ToLowerInvariant();
        foreach (var intent in availableIntents)
        {
            var keywords = ExtractKeywordsFromIntent(intent);
            if (keywords.Any(k => lowerInput.Contains(k)))
            {
                _logger.LogInformation("User input matched intent by keyword: {Intent}", intent);
                return (true, intent);
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Get emoji for intent type
    /// </summary>
    private string GetEmojiForIntent(string intentName)
    {
        if (intentName.StartsWith("Create")) return "➕";
        if (intentName.StartsWith("Update")) return "✏️";
        if (intentName.StartsWith("Delete")) return "🗑️";
        if (intentName.StartsWith("Get")) return "🔍";
        if (intentName.StartsWith("List")) return "📋";
        if (intentName.StartsWith("Search")) return "🔎";
        return "📌";
    }

    /// <summary>
    /// Get Turkish description for intent
    /// </summary>
    private string GetTurkishDescription(string intentName)
    {
        // Extract entity name
        var entity = ExtractEntityFromIntent(intentName);
        
        if (intentName.StartsWith("Create")) return $"{entity} Oluştur";
        if (intentName.StartsWith("Update")) return $"{entity} Güncelle";
        if (intentName.StartsWith("Delete")) return $"{entity} Sil";
        if (intentName.StartsWith("Get")) return $"{entity} Detayları";
        if (intentName.StartsWith("List")) return $"{entity} Listele";
        if (intentName.StartsWith("Search")) return $"{entity} Ara";
        
        return intentName;
    }

    /// <summary>
    /// Extract entity name from intent
    /// </summary>
    private string ExtractEntityFromIntent(string intentName)
    {
        var prefixes = new[] { "Create", "Update", "Delete", "Get", "List", "Search" };
        foreach (var prefix in prefixes)
        {
            if (intentName.StartsWith(prefix))
            {
                var entity = intentName.Substring(prefix.Length);
                return entity.EndsWith("s") ? entity.Substring(0, entity.Length - 1) : entity;
            }
        }
        return intentName;
    }

    /// <summary>
    /// Extract keywords from intent name
    /// </summary>
    private List<string> ExtractKeywordsFromIntent(string intentName)
    {
        var keywords = new List<string>();
        
        if (intentName.Contains("Product")) keywords.AddRange(new[] { "ürün", "product" });
        if (intentName.Contains("Customer")) keywords.AddRange(new[] { "müşteri", "customer" });
        if (intentName.Contains("Invoice")) keywords.AddRange(new[] { "fatura", "invoice" });
        if (intentName.Contains("Contract")) keywords.AddRange(new[] { "sözleşme", "contract" });
        if (intentName.Contains("Order")) keywords.AddRange(new[] { "sipariş", "order" });
        
        if (intentName.StartsWith("Create")) keywords.AddRange(new[] { "oluştur", "ekle", "yarat" });
        if (intentName.StartsWith("Update")) keywords.AddRange(new[] { "güncelle", "değiştir" });
        if (intentName.StartsWith("Delete")) keywords.AddRange(new[] { "sil", "kaldır" });
        if (intentName.StartsWith("List")) keywords.AddRange(new[] { "listele", "göster" });
        if (intentName.StartsWith("Search")) keywords.AddRange(new[] { "ara", "bul" });
        
        return keywords;
    }

    /// <summary>
    /// Generate visual confidence bar
    /// </summary>
    private string GenerateConfidenceBar(float confidence)
    {
        var barLength = 10;
        var filledLength = (int)(confidence * barLength);
        var bar = new string('█', filledLength) + new string('░', barLength - filledLength);
        return $"[{bar}]";
    }

    private class DisambiguationOption
    {
        public string IntentName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public float Confidence { get; set; }
        public string Example { get; set; } = string.Empty;
    }
}
