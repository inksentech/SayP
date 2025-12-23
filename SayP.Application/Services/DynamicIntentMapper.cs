using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SayP.Application.Interfaces;
using SayP.Domain.Entities;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;

namespace SayP.Application.Services;

/// <summary>
/// AI-powered intent mapper that matches user messages to discovered endpoints
/// Enhanced with self-learning capabilities
/// </summary>
public class DynamicIntentMapper : IDynamicIntentMapper
{
    private readonly IAIProvider _aiProvider;
    private readonly ICacheService _cache;
    private readonly FuzzyMatchingService _fuzzyMatcher;
    private readonly ConfidenceCalibrator? _calibrator;
    private readonly SelfLearningService? _selfLearning; // ✅ NEW: Self-learning integration
    private readonly ILogger<DynamicIntentMapper> _logger;
    private const string LEARNING_CACHE_PREFIX = "intent_learning:";

    // ✅ NEW: Tenant context for learning
    private Guid? _currentTenantId;

    public DynamicIntentMapper(
        IAIProvider aiProvider,
        ICacheService cache,
        FuzzyMatchingService fuzzyMatcher,
        ILogger<DynamicIntentMapper> logger,
        ConfidenceCalibrator? calibrator = null,
        SelfLearningService? selfLearning = null) // ✅ NEW: Optional for backward compatibility
    {
        _aiProvider = aiProvider;
        _cache = cache;
        _fuzzyMatcher = fuzzyMatcher;
        _calibrator = calibrator;
        _selfLearning = selfLearning;
        _logger = logger;
    }

    /// <summary>
    /// Set tenant context for learning
    /// </summary>
    public void SetTenantContext(Guid? tenantId)
    {
        _currentTenantId = tenantId;
    }

    public async Task<IntentMappingResult> MapIntentAsync(
        string message,
        List<DiscoveredEndpoint> availableEndpoints,
        string? conversationContext = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Mapping intent for message: {Message}", message);

            if (!availableEndpoints.Any())
            {
                return new IntentMappingResult
                {
                    Success = false,
                    ErrorMessage = "No available endpoints to match"
                };
            }

            // ✅ Step 0: Try learned aliases first (fastest, most accurate for known patterns)
            if (_selfLearning != null)
            {
                var aliasMatch = await TryLearnedAliasMatchingAsync(message, availableEndpoints, cancellationToken);
                if (aliasMatch.Success && aliasMatch.Confidence >= 0.8)
                {
                    _logger.LogInformation("🧠 Learned alias match found: {Intent} (confidence: {Confidence:F2})", 
                        aliasMatch.MatchedEndpoint?.Intent, aliasMatch.Confidence);
                    return aliasMatch;
                }
            }

            // Step 1: Try fuzzy matching (fast, free)
            var fuzzyResult = await TryFuzzyMatchingAsync(message, availableEndpoints);
            
            // ✅ ADDED: Apply confidence calibration to fuzzy result
            if (fuzzyResult.Success && fuzzyResult.MatchedEndpoint != null && _calibrator != null)
            {
                var rawConfidence = fuzzyResult.Confidence;
                var adjustedConfidence = await _calibrator.AdjustConfidenceAsync(
                    fuzzyResult.MatchedEndpoint.Intent,
                    rawConfidence,
                    cancellationToken);
                
                _logger.LogDebug("Fuzzy confidence calibrated: {Raw:F2} → {Adjusted:F2}", 
                    rawConfidence, adjustedConfidence);
                
                fuzzyResult.Confidence = adjustedConfidence;
            }
            
            // ✅ CRITICAL FIX: Only use fuzzy matching for EXACT matches (0.95+)
            // For anything less, ALWAYS ask AI - it understands context much better
            // Example: "müşteri ekle" vs "kod şablonu ekle" - fuzzy can't distinguish entities
            if (fuzzyResult.Success && fuzzyResult.Confidence >= 0.95)
            {
                _logger.LogInformation("Exact fuzzy match found: {Intent} (confidence: {Confidence:F2})", 
                    fuzzyResult.MatchedEndpoint?.Intent, fuzzyResult.Confidence);
                return fuzzyResult;
            }
            
            // Log that we're deferring to AI for better accuracy
            if (fuzzyResult.Success && fuzzyResult.Confidence > 0.5)
            {
                _logger.LogInformation("Fuzzy match found but deferring to AI for accuracy: {Intent} (confidence: {Confidence:F2})", 
                    fuzzyResult.MatchedEndpoint?.Intent, fuzzyResult.Confidence);
            }

            // Step 2: Use AI for more complex matching - AI is much better at understanding context
            var aiResult = await TryAIMatchingAsync(message, availableEndpoints, conversationContext, cancellationToken);
            
            // ✅ ADDED: Apply confidence calibration to AI result
            if (aiResult.Success && aiResult.MatchedEndpoint != null && _calibrator != null)
            {
                var rawConfidence = aiResult.Confidence;
                var adjustedConfidence = await _calibrator.AdjustConfidenceAsync(
                    aiResult.MatchedEndpoint.Intent,
                    rawConfidence,
                    cancellationToken);
                
                _logger.LogDebug("AI confidence calibrated: {Raw:F2} → {Adjusted:F2}", 
                    rawConfidence, adjustedConfidence);
                
                aiResult.Confidence = adjustedConfidence;
            }
            
            if (aiResult.Success)
            {
                _logger.LogInformation("AI match found: {Intent} with confidence {Confidence}", 
                    aiResult.MatchedEndpoint?.Intent, aiResult.Confidence);
                return aiResult;
            }

            // ⚠️ IMPORTANT: If AI explicitly said "none" (confidence = 0), don't use fuzzy fallback
            // This means AI determined it's not a command (casual message, question, etc.)
            if (!aiResult.Success && aiResult.Confidence == 0)
            {
                _logger.LogInformation("AI determined message is not a command, skipping fuzzy fallback");
                return new IntentMappingResult
                {
                    Success = false,
                    Confidence = 0,
                    ErrorMessage = "Not a command - casual message or question"
                };
            }

            // Step 3: Return best fuzzy match as fallback (only if AI didn't explicitly reject)
            if (fuzzyResult.MatchedEndpoint != null && fuzzyResult.Confidence >= 0.5)
            {
                _logger.LogInformation("Returning fuzzy match as fallback: {Intent} (confidence: {Confidence})", 
                    fuzzyResult.MatchedEndpoint.Intent, fuzzyResult.Confidence);
                return fuzzyResult;
            }

            return new IntentMappingResult
            {
                Success = false,
                ErrorMessage = "Could not match message to any available endpoint",
                AlternativeEndpoints = availableEndpoints.Take(3).ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mapping intent for message: {Message}", message);
            return new IntentMappingResult
            {
                Success = false,
                ErrorMessage = $"Error: {ex.Message}"
            };
        }
    }

    private async Task<IntentMappingResult> TryFuzzyMatchingAsync(
        string message,
        List<DiscoveredEndpoint> endpoints)
    {
        var messageLower = message.ToLower();
        var bestMatch = endpoints
            .Select(e => new
            {
                Endpoint = e,
                Score = CalculateFuzzyScore(messageLower, e)
            })
            .OrderByDescending(x => x.Score)
            .FirstOrDefault();

        // ✅ IMPROVED: Dynamic threshold based on priority
        const double MIN_FUZZY_THRESHOLD = 0.4;
        const double HIGH_PRIORITY_THRESHOLD = 0.3; // Lower threshold for high-priority endpoints
        
        var effectiveThreshold = bestMatch?.Endpoint.Priority >= 8 
            ? HIGH_PRIORITY_THRESHOLD 
            : MIN_FUZZY_THRESHOLD;
        
        if (bestMatch == null || bestMatch.Score < effectiveThreshold)
        {
            _logger.LogDebug("Fuzzy matching failed: best score {Score} below threshold {Threshold} (priority: {Priority})", 
                bestMatch?.Score ?? 0, effectiveThreshold, bestMatch?.Endpoint.Priority ?? 0);
            
            // ✅ IMPROVED: Return top 3 alternatives even if no exact match
            var topAlternatives = endpoints
                .Select(e => new { Endpoint = e, Score = CalculateFuzzyScore(messageLower, e) })
                .OrderByDescending(x => x.Score)
                .Take(3)
                .Select(x => x.Endpoint)
                .ToList();
            
            return new IntentMappingResult 
            { 
                Success = false,
                AlternativeEndpoints = topAlternatives
            };
        }

        _logger.LogDebug("Fuzzy match: {Intent} with score {Score}", 
            bestMatch.Endpoint.Intent, bestMatch.Score);

        // Get alternative matches
        var alternatives = endpoints
            .Where(e => e.Id != bestMatch.Endpoint.Id)
            .Select(e => new { Endpoint = e, Score = CalculateFuzzyScore(messageLower, e) })
            .Where(x => x.Score > MIN_FUZZY_THRESHOLD)
            .OrderByDescending(x => x.Score)
            .Take(3)
            .Select(x => x.Endpoint)
            .ToList();

        return new IntentMappingResult
        {
            Success = true,
            MatchedEndpoint = bestMatch.Endpoint,
            Confidence = bestMatch.Score,
            AlternativeEndpoints = alternatives
        };
    }

    // ✅ SIMPLIFIED: Fuzzy matching is now just for basic text similarity
    // AI handles all the complex entity/action understanding
    // This keeps the system dynamic and not hard-coded to specific entities
    private double CalculateFuzzyScore(string message, DiscoveredEndpoint endpoint)
    {
        var scores = new List<double>();

        // Match against intent name (basic similarity)
        var intentWords = endpoint.Intent.Replace("_", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var intentScore = intentWords.Average(word => 
            _fuzzyMatcher.CalculateSimilarity(message, word));
        scores.Add(intentScore);

        // Match against description
        if (!string.IsNullOrEmpty(endpoint.Description))
        {
            var descScore = _fuzzyMatcher.CalculateSimilarity(message, endpoint.Description);
            scores.Add(descScore);
        }

        // Match against aliases (these come from the API, not hard-coded)
        foreach (var alias in endpoint.Aliases)
        {
            var aliasScore = _fuzzyMatcher.CalculateSimilarity(message, alias);
            scores.Add(aliasScore);
        }

        var baseScore = scores.Max();
        
        // Priority boost (but capped to prevent runaway scores)
        var priorityMultiplier = 1.0 + (endpoint.Priority * 0.05); // Max 50% boost at priority 10
        var finalScore = Math.Min(1.0, baseScore * priorityMultiplier);
        
        _logger.LogDebug("Fuzzy score for {Intent}: base={BaseScore:F3}, priority={Priority}, final={Final:F3}", 
            endpoint.Intent, baseScore, endpoint.Priority, finalScore);
        
        return finalScore;
    }

    private async Task<IntentMappingResult> TryAIMatchingAsync(
        string message,
        List<DiscoveredEndpoint> endpoints,
        string? conversationContext,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build prompt for AI
            var prompt = BuildAIPrompt(message, endpoints, conversationContext);

            // Call AI provider
            var aiResponse = await _aiProvider.GenerateResponseAsync(prompt, null, cancellationToken);
            
            // Parse AI response
            var result = ParseAIResponse(aiResponse, endpoints);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AI matching");
            return new IntentMappingResult { Success = false };
        }
    }

    private string BuildAIPrompt(
        string message,
        List<DiscoveredEndpoint> endpoints,
        string? conversationContext)
    {
        // ✅ IMPROVED: Include priority in AI prompt
        var endpointsJson = JsonConvert.SerializeObject(endpoints.Select(e => new
        {
            e.Intent,
            e.Description,
            e.Aliases,
            e.HttpMethod,
            e.Route,
            e.Priority // AI will consider priority when matching
        }), Formatting.Indented);

        var prompt = $@"You are an intelligent API intent mapper. Your task is to match a user's natural language message to the most appropriate API endpoint.

User Message: ""{message}""

{(string.IsNullOrEmpty(conversationContext) ? "" : $"Conversation Context: {conversationContext}")}

Available Endpoints:
{endpointsJson}

IMPORTANT RULES:
1. If the message is a casual greeting (like ""hello"", ""hi"", ""selam"", ""merhaba""), small talk, or general question, set matchedIntent to ""none"" and confidence to 0
2. Only match to an endpoint if the user clearly wants to perform an action (create, update, delete, list, etc.)
3. Keywords in the message should match intent names, descriptions, or aliases
4. Consider the conversation context if provided
5. ✅ PRIORITY MATTERS: Higher priority endpoints (priority >= 8) should be preferred when there's ambiguity. If multiple endpoints match, choose the one with higher priority.
6. For high-priority endpoints, you can be slightly more lenient with confidence (0.6+ is acceptable vs 0.8+ for low priority)

Respond with JSON in this format:
{{
  ""matchedIntent"": ""intent_name"",
  ""confidence"": 0.95,
  ""reasoning"": ""Brief explanation of why this endpoint matches"",
  ""extractedParameters"": {{
    ""paramName"": ""paramValue""
  }}
}}

Examples:
- ""Selam"" → {{""matchedIntent"": ""none"", ""confidence"": 0, ""reasoning"": ""Casual greeting, not a command""}}
- ""Kod şablonu oluştur"" → {{""matchedIntent"": ""create_code_template"", ""confidence"": 0.95, ""reasoning"": ""User wants to create a code template""}}

If no good match is found or it's casual conversation, set matchedIntent to ""none"" and confidence to 0.";

        return prompt;
    }

    private IntentMappingResult ParseAIResponse(string aiResponse, List<DiscoveredEndpoint> endpoints)
    {
        try
        {
            _logger.LogDebug("AI Response: {Response}", aiResponse);

            // Extract JSON from AI response (might have markdown code blocks)
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(
                aiResponse, 
                @"\{[\s\S]*\}", 
                System.Text.RegularExpressions.RegexOptions.Multiline);

            if (!jsonMatch.Success)
            {
                _logger.LogWarning("Could not extract JSON from AI response");
                return new IntentMappingResult { Success = false };
            }

            var json = jsonMatch.Value;
            dynamic response = JsonConvert.DeserializeObject(json)!;

            string? matchedIntent = response.matchedIntent?.ToString();
            double confidence = (double)(response.confidence ?? 0.0);

            // Cast to object to avoid dynamic dispatch issue with extension methods
            _logger.LogInformation("AI matched intent: {Intent} with confidence {Confidence}", 
                (object)(matchedIntent ?? "none"), (object)confidence);

            // Check for "none" or null intent (casual messages)
            if (string.IsNullOrEmpty(matchedIntent) || 
                matchedIntent.Equals("none", StringComparison.OrdinalIgnoreCase) ||
                confidence < 0.5)
            {
                _logger.LogInformation("AI determined this is not a command (confidence: {Confidence})", confidence);
                return new IntentMappingResult
                {
                    Success = false,
                    Confidence = confidence
                };
            }

            var matchedEndpoint = endpoints.FirstOrDefault(e => e.Intent == matchedIntent);
            if (matchedEndpoint == null)
            {
                _logger.LogWarning("AI matched intent {Intent} but endpoint not found", (object)matchedIntent);
                return new IntentMappingResult { Success = false };
            }

            var extractedParams = new Dictionary<string, object>();
            if (response.extractedParameters != null)
            {
                foreach (var prop in response.extractedParameters)
                {
                    extractedParams[prop.Name] = prop.Value?.ToString() ?? "";
                }
            }

            return new IntentMappingResult
            {
                Success = true,
                MatchedEndpoint = matchedEndpoint,
                Confidence = confidence,
                ExtractedParameters = extractedParams
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing AI response");
            return new IntentMappingResult { Success = false };
        }
    }

    public async Task LearnFromFeedbackAsync(
        string message,
        string correctIntent,
        bool wasCorrect,
        Guid tenantId)
    {
        try
        {
            var learningKey = $"{LEARNING_CACHE_PREFIX}{tenantId}:{message.GetHashCode()}";
            var learningData = new
            {
                Message = message,
                CorrectIntent = correctIntent,
                WasCorrect = wasCorrect,
                Timestamp = DateTime.UtcNow
            };

            var json = JsonConvert.SerializeObject(learningData);
            await _cache.SetAsync(learningKey, json, TimeSpan.FromDays(30));
            _logger.LogInformation("Recorded learning feedback for tenant {TenantId}: {Message} -> {Intent} (Correct: {WasCorrect})",
                tenantId, message, correctIntent, wasCorrect);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording learning feedback");
        }
    }

    public async Task<double> GetMappingConfidenceAsync(
        string message,
        DiscoveredEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var messageLower = message.ToLower();
        return CalculateFuzzyScore(messageLower, endpoint);
    }

    #region Self-Learning Integration

    /// <summary>
    /// Try to match using learned aliases
    /// </summary>
    private async Task<IntentMappingResult> TryLearnedAliasMatchingAsync(
        string message,
        List<DiscoveredEndpoint> endpoints,
        CancellationToken cancellationToken)
    {
        if (_selfLearning == null)
        {
            return new IntentMappingResult { Success = false };
        }

        try
        {
            // Find best alias match
            var aliasMatch = await _selfLearning.FindBestAliasMatchAsync(
                message, 
                _currentTenantId, 
                cancellationToken);

            if (aliasMatch.HasValue && !string.IsNullOrEmpty(aliasMatch.Value.Intent))
            {
                var matchedEndpoint = endpoints.FirstOrDefault(e => 
                    e.Intent.Equals(aliasMatch.Value.Intent, StringComparison.OrdinalIgnoreCase));

                if (matchedEndpoint != null)
                {
                    return new IntentMappingResult
                    {
                        Success = true,
                        MatchedEndpoint = matchedEndpoint,
                        Confidence = aliasMatch.Value.Confidence,
                        MatchSource = "learned_alias"
                    };
                }
            }

            return new IntentMappingResult { Success = false };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error in learned alias matching");
            return new IntentMappingResult { Success = false };
        }
    }

    /// <summary>
    /// Report successful command execution for learning
    /// </summary>
    public async Task ReportSuccessAsync(
        string message,
        string intent,
        double confidence,
        CancellationToken cancellationToken = default)
    {
        if (_selfLearning != null)
        {
            await _selfLearning.LearnFromSuccessfulCommandAsync(
                message, 
                intent, 
                _currentTenantId, 
                confidence, 
                cancellationToken);
        }

        // Also update confidence calibrator
        if (_calibrator != null)
        {
            await _calibrator.LearnFromFeedbackAsync(intent, confidence, true, cancellationToken);
        }
    }

    /// <summary>
    /// Report failed/cancelled command for learning
    /// </summary>
    public async Task ReportFailureAsync(
        string message,
        string intent,
        CancellationToken cancellationToken = default)
    {
        if (_selfLearning != null)
        {
            await _selfLearning.LearnFromFailedCommandAsync(
                message, 
                intent, 
                _currentTenantId, 
                cancellationToken);
        }

        // Also update confidence calibrator
        if (_calibrator != null)
        {
            await _calibrator.LearnFromFeedbackAsync(intent, 0.5, false, cancellationToken);
        }
    }

    #endregion
}
