using Microsoft.Extensions.Logging;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;

namespace SayP.Application.Services;

/// <summary>
/// Intelligent fallback system for AI providers
/// </summary>
public class IntelligentFallbackProvider
{
    private readonly ILogger<IntelligentFallbackProvider> _logger;
    private readonly EntityExtractor _entityExtractor;
    private readonly MultiLanguageTypoCorrector _typoCorrector;
    private const double CONFIDENCE_THRESHOLD_HIGH = 0.7;
    private const double CONFIDENCE_THRESHOLD_LOW = 0.3;

    public IntelligentFallbackProvider(
        ILogger<IntelligentFallbackProvider> logger,
        EntityExtractor entityExtractor,
        MultiLanguageTypoCorrector typoCorrector)
    {
        _logger = logger;
        _entityExtractor = entityExtractor;
        _typoCorrector = typoCorrector;
    }

    /// <summary>
    /// Extract command with intelligent fallback
    /// </summary>
    public async Task<AICommandResult> ExtractWithFallbackAsync(
        string message,
        IAIProvider primaryProvider,
        IAIProvider? secondaryProvider = null,
        string? conversationContext = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting intelligent fallback extraction");

        // Step 0: Auto-correct typos before processing
        var correctedMessage = _typoCorrector.AutoCorrect(message);
        if (correctedMessage != message)
        {
            _logger.LogInformation("Message auto-corrected: '{Original}' → '{Corrected}'", message, correctedMessage);
            message = correctedMessage;
        }

        // Try primary AI provider
        var result = await TryProviderAsync(
            primaryProvider,
            message,
            conversationContext,
            "Primary",
            cancellationToken);

        if (result.Success && result.Confidence >= CONFIDENCE_THRESHOLD_HIGH)
        {
            _logger.LogInformation("Primary provider succeeded with high confidence: {Confidence}", result.Confidence);
            return result;
        }

        // If confidence is low, try secondary provider
        if (secondaryProvider != null && result.Confidence < CONFIDENCE_THRESHOLD_HIGH)
        {
            _logger.LogInformation(
                "Primary provider confidence low ({Confidence}), trying secondary provider",
                result.Confidence);

            var secondaryResult = await TryProviderAsync(
                secondaryProvider,
                message,
                conversationContext,
                "Secondary",
                cancellationToken);

            // Use secondary if it has higher confidence
            if (secondaryResult.Success && secondaryResult.Confidence > result.Confidence)
            {
                _logger.LogInformation(
                    "Secondary provider has higher confidence ({Secondary} > {Primary})",
                    secondaryResult.Confidence, result.Confidence);
                result = secondaryResult;
            }
        }

        // If still low confidence, try rule-based extraction
        if (result.Confidence < CONFIDENCE_THRESHOLD_LOW)
        {
            _logger.LogInformation(
                "AI confidence very low ({Confidence}), trying rule-based extraction",
                result.Confidence);

            var ruleBasedResult = TryRuleBasedExtraction(message);
            
            if (ruleBasedResult != null && ruleBasedResult.Confidence > result.Confidence)
            {
                _logger.LogInformation("Rule-based extraction succeeded");
                result = ruleBasedResult;
            }
        }

        // If still no success, create clarification request
        if (result.Confidence < CONFIDENCE_THRESHOLD_LOW)
        {
            _logger.LogInformation("All methods failed, creating clarification request");
            result = CreateClarificationRequest(message);
        }

        return result;
    }

    /// <summary>
    /// Try a specific AI provider
    /// </summary>
    private async Task<AICommandResult> TryProviderAsync(
        IAIProvider provider,
        string message,
        string? conversationContext,
        string providerName,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Trying {Provider} provider", providerName);
            
            var result = await provider.ExtractCommandAsync(
                message,
                conversationContext,
                cancellationToken);

            _logger.LogDebug(
                "{Provider} provider result: Success={Success}, Confidence={Confidence}",
                providerName, result.Success, result.Confidence);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Provider} provider failed", providerName);
            
            return new AICommandResult
            {
                Success = false,
                Confidence = 0,
                ErrorMessage = $"{providerName} provider error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Try rule-based command extraction
    /// </summary>
    private AICommandResult? TryRuleBasedExtraction(string message)
    {
        try
        {
            var messageLower = message.ToLower();

            // Product commands
            if (messageLower.Contains("ürün") || messageLower.Contains("product"))
            {
                if (messageLower.Contains("ekle") || messageLower.Contains("oluştur") || messageLower.Contains("yap"))
                {
                    return CreateRuleBasedResult("CreateProduct", message);
                }
                if (messageLower.Contains("güncelle") || messageLower.Contains("değiştir"))
                {
                    return CreateRuleBasedResult("UpdateProduct", message);
                }
                if (messageLower.Contains("sil") || messageLower.Contains("iptal"))
                {
                    return CreateRuleBasedResult("DeleteProduct", message);
                }
                if (messageLower.Contains("listele") || messageLower.Contains("göster"))
                {
                    return CreateRuleBasedResult("ListProducts", message);
                }
                if (messageLower.Contains("ara") || messageLower.Contains("bul"))
                {
                    return CreateRuleBasedResult("SearchProducts", message);
                }
            }

            // Customer commands
            if (messageLower.Contains("müşteri") || messageLower.Contains("customer"))
            {
                if (messageLower.Contains("ekle") || messageLower.Contains("oluştur"))
                {
                    return CreateRuleBasedResult("CreateCustomer", message);
                }
                if (messageLower.Contains("listele") || messageLower.Contains("göster"))
                {
                    return CreateRuleBasedResult("ListCustomers", message);
                }
            }

            // Invoice commands
            if (messageLower.Contains("fatura") || messageLower.Contains("invoice"))
            {
                if (messageLower.Contains("kes") || messageLower.Contains("oluştur"))
                {
                    return CreateRuleBasedResult("CreateInvoice", message);
                }
                if (messageLower.Contains("listele") || messageLower.Contains("göster"))
                {
                    return CreateRuleBasedResult("ListInvoices", message);
                }
            }

            // Generic list/search
            if (messageLower.Contains("listele") || messageLower.Contains("list"))
            {
                return CreateRuleBasedResult("ListProducts", message);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rule-based extraction failed");
            return null;
        }
    }

    /// <summary>
    /// Create rule-based result
    /// </summary>
    private AICommandResult CreateRuleBasedResult(string commandType, string message)
    {
        // Extract entities using rule-based extractor
        var entities = _entityExtractor.ExtractEntities(message);
        var normalizedEntities = _entityExtractor.NormalizeEntities(entities);

        var commandJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            commandType,
            requiresConfirmation = false,
            confidence = 0.6, // Medium confidence for rule-based
            command = normalizedEntities
        });

        return new AICommandResult
        {
            Success = true,
            Confidence = 0.6,
            CommandType = commandType,
            CommandJson = commandJson
        };
    }

    /// <summary>
    /// Create clarification request
    /// </summary>
    private AICommandResult CreateClarificationRequest(string message)
    {
        var clarificationQuestions = new List<string>
        {
            "Üzgünüm, tam olarak anlayamadım. Ne yapmak istediğinizi daha açık belirtir misiniz?",
            "Şunlardan birini mi demek istediniz?",
            "• Ürün eklemek",
            "• Ürün güncellemek",
            "• Ürün aramak",
            "• Müşteri eklemek",
            "• Fatura kesmek"
        };

        var response = string.Join("\n", clarificationQuestions);

        var commandJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
        {
            commandType = "None",
            response
        });

        return new AICommandResult
        {
            Success = true,
            Confidence = 0.1,
            CommandType = "None",
            CommandJson = commandJson
        };
    }

    /// <summary>
    /// Suggest corrections for common typos (Deprecated - use MultiLanguageTypoCorrector.AutoCorrect instead)
    /// </summary>
    [Obsolete("Use MultiLanguageTypoCorrector.AutoCorrect instead")]
    public string SuggestCorrections(string message)
    {
        return _typoCorrector.AutoCorrect(message);
    }

    /// <summary>
    /// Detect ambiguity in message
    /// </summary>
    public bool IsAmbiguous(string message)
    {
        var ambiguousPatterns = new[]
        {
            @"\b(onu|onu|bunu|şunu)\b", // Pronouns without context
            @"\b(o|bu|şu)\s+(ürün|müşteri|fatura)\b",
            @"^(ekle|sil|güncelle|değiştir)$", // Action without object
        };

        foreach (var pattern in ambiguousPatterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(
                message,
                pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                _logger.LogInformation("Detected ambiguous message: {Message}", message);
                return true;
            }
        }

        return false;
    }
}
