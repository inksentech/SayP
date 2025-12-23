using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;

namespace SayP.Application.Services;

/// <summary>
/// Manages conversations using generic AI-powered discovery and execution
/// </summary>
public class GenericConversationManager
{
    private readonly IApiDiscoveryService _discoveryService;
    private readonly IDynamicIntentMapper _intentMapper;
    private readonly IGenericCommandExecutor _executor;
    private readonly DynamicSlotFiller _slotFiller;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ISayPDbContext _context;
    private readonly IAIProvider _aiProvider;
    private readonly UserProfileService _userProfileService;
    private readonly ConfidenceCalibrator? _calibrator;
    private readonly ICacheService _cache;
    private readonly EntityResolverService _entityResolver;
    private readonly ILogger<GenericConversationManager> _logger;
    
    // ✅ Phase 8: Self-Learning & AI Enhancement Services
    private readonly SelfLearningService? _selfLearning;
    private readonly ContextManager? _contextManager;
    private readonly FeedbackService? _feedbackService;
    private readonly MultiLanguageService? _multiLanguage;

    private const string CONFIRMATION_TEMPLATE_CACHE_PREFIX = "confirmation_template:";
    private static readonly TimeSpan TEMPLATE_CACHE_DURATION = TimeSpan.FromHours(24);

    public GenericConversationManager(
        IApiDiscoveryService discoveryService,
        IDynamicIntentMapper intentMapper,
        IGenericCommandExecutor executor,
        DynamicSlotFiller slotFiller,
        IWhatsAppService whatsAppService,
        ISayPDbContext context,
        IAIProvider aiProvider,
        UserProfileService userProfileService,
        ICacheService cache,
        EntityResolverService entityResolver,
        ILogger<GenericConversationManager> logger,
        ConfidenceCalibrator? calibrator = null,
        // ✅ Phase 8: Self-Learning & AI Enhancement Services (optional for backward compatibility)
        SelfLearningService? selfLearning = null,
        ContextManager? contextManager = null,
        FeedbackService? feedbackService = null,
        MultiLanguageService? multiLanguage = null)
    {
        _discoveryService = discoveryService;
        _intentMapper = intentMapper;
        _executor = executor;
        _slotFiller = slotFiller;
        _whatsAppService = whatsAppService;
        _context = context;
        _aiProvider = aiProvider;
        _userProfileService = userProfileService;
        _calibrator = calibrator;
        _cache = cache;
        _entityResolver = entityResolver;
        _logger = logger;
        
        // ✅ Phase 8: Self-Learning & AI Enhancement Services
        _selfLearning = selfLearning;
        _contextManager = contextManager;
        _feedbackService = feedbackService;
        _multiLanguage = multiLanguage;
    }

    /// <summary>
    /// Process incoming message using generic AI pipeline
    /// </summary>
    public async Task<GenericProcessingResult> ProcessMessageAsync(
        string phoneNumber,
        string message,
        Guid tenantId,
        string backendUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        return await ProcessMessageAsync(phoneNumber, message, tenantId, backendUrl, apiKey, 
            mediaContext: null, cancellationToken);
    }

    /// <summary>
    /// Process incoming message using generic AI pipeline with media context support
    /// </summary>
    public async Task<GenericProcessingResult> ProcessMessageAsync(
        string phoneNumber,
        string message,
        Guid tenantId,
        string backendUrl,
        string? apiKey,
        MediaContext? mediaContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing message for tenant {TenantId}: {Message}", tenantId, message);

            // ✅ Phase 8: Detect language for localized responses
            string detectedLanguage = "tr";
            if (_multiLanguage != null)
            {
                var langResult = _multiLanguage.DetectLanguage(message);
                detectedLanguage = langResult.Language;
                _logger.LogDebug("Detected language: {Language} (confidence: {Confidence:P0})", 
                    langResult.Language, langResult.Confidence);
            }

            // Step 1: Get or discover available endpoints
            var endpoints = await _discoveryService.GetCachedEndpointsAsync(tenantId);
            if (endpoints == null || !endpoints.Any())
            {
                _logger.LogInformation("No cached endpoints, discovering from {BackendUrl}", backendUrl);
                endpoints = await _discoveryService.DiscoverEndpointsAsync(backendUrl, apiKey, cancellationToken);
                await _discoveryService.CacheDiscoveryAsync(tenantId, endpoints);
            }

            if (!endpoints.Any())
            {
                var noEndpointMsg = detectedLanguage == "en" 
                    ? "Sorry, no available commands found. Please check your backend API's Swagger documentation."
                    : "Üzgünüm, kullanılabilir komut bulunamadı. Lütfen backend API'nizin Swagger dokümantasyonunu kontrol edin.";
                return new GenericProcessingResult
                {
                    Success = false,
                    Response = noEndpointMsg
                };
            }

            // ✅ Phase 8: Enrich endpoints with multi-language aliases
            if (_multiLanguage != null)
            {
                _multiLanguage.EnrichEndpointsWithAliases(endpoints);
            }

            _logger.LogInformation("Found {Count} available endpoints", endpoints.Count);

            // Step 2: Get user profile and build personalized context
            string? personalizedContext = null;
            Guid? userProfileId = null;
            Guid? conversationId = null;
            try
            {
                var userProfile = await _userProfileService.GetOrCreateProfileAsync(
                    phoneNumber,
                    tenantId,
                    cancellationToken: cancellationToken);
                
                userProfileId = userProfile.Id;
                
                personalizedContext = await _userProfileService.BuildPersonalizedContextAsync(
                    userProfile.Id,
                    cancellationToken);
                
                if (!string.IsNullOrEmpty(personalizedContext))
                {
                    _logger.LogDebug("Using personalized context for user {PhoneNumber}", phoneNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get personalized context, continuing without it");
            }

            // ✅ Phase 8: Resolve context references ("o müşteri", "aynısından")
            string processedMessage = message;
            if (_contextManager != null && conversationId.HasValue)
            {
                var detectedRefs = _contextManager.DetectReferences(message);
                if (detectedRefs.Any())
                {
                    _logger.LogDebug("Detected {Count} context references in message", detectedRefs.Count);
                    var resolvedRefs = await _contextManager.ResolveReferencesAsync(
                        conversationId.Value, detectedRefs, cancellationToken);
                    
                    if (resolvedRefs.Any())
                    {
                        processedMessage = _contextManager.ReplaceReferencesInMessage(message, resolvedRefs);
                        _logger.LogInformation("Resolved references: {Original} -> {Processed}", message, processedMessage);
                    }
                }
            }

            // ✅ Phase 8: Set tenant context for self-learning
            if (_intentMapper is DynamicIntentMapper dynamicMapper)
            {
                dynamicMapper.SetTenantContext(tenantId);
            }

            // Step 3: Map user intent to endpoint (with personalized context)
            var mappingResult = await _intentMapper.MapIntentAsync(
                message,
                endpoints,
                conversationContext: personalizedContext, // ✅ IMPROVED: Using personalized context
                cancellationToken);

            if (!mappingResult.Success || mappingResult.MatchedEndpoint == null)
            {
                // ✅ IMPROVED: Show alternative endpoint suggestions if available
                if (mappingResult.AlternativeEndpoints != null && mappingResult.AlternativeEndpoints.Any())
                {
                    var suggestions = await BuildSuggestionMessageAsync(
                        message, 
                        mappingResult.AlternativeEndpoints,
                        personalizedContext,
                        cancellationToken);
                    
                    return new GenericProcessingResult
                    {
                        Success = true,
                        Response = suggestions,
                        RequiresMoreInfo = true,
                        SuggestedEndpoints = mappingResult.AlternativeEndpoints
                    };
                }
                
                // No alternatives, use casual response
                var casualResponse = await GetCasualResponseAsync(message, endpoints, personalizedContext, cancellationToken);
                
                return new GenericProcessingResult
                {
                    Success = true,
                    Response = casualResponse,
                    SuggestedEndpoints = mappingResult.AlternativeEndpoints
                };
            }

            // ✅ IMPROVED: Priority-based confidence threshold
            const double MIN_CONFIDENCE_THRESHOLD = 0.50; // 50% minimum confidence
            const double HIGH_PRIORITY_THRESHOLD = 0.35; // 35% for high-priority endpoints (priority >= 8)
            
            var effectiveThreshold = mappingResult.MatchedEndpoint?.Priority >= 8 
                ? HIGH_PRIORITY_THRESHOLD 
                : MIN_CONFIDENCE_THRESHOLD;
            
            if (mappingResult.Confidence < effectiveThreshold)
            {
                _logger.LogInformation("Intent confidence too low ({Confidence}) for threshold {Threshold} (priority: {Priority})", 
                    mappingResult.Confidence, effectiveThreshold, mappingResult.MatchedEndpoint?.Priority ?? 0);
                
                // ✅ IMPROVED: Show suggestions if we have alternative endpoints
                if (mappingResult.AlternativeEndpoints != null && mappingResult.AlternativeEndpoints.Any())
                {
                    var suggestions = await BuildSuggestionMessageAsync(
                        message, 
                        mappingResult.AlternativeEndpoints,
                        personalizedContext,
                        cancellationToken);
                    
                    return new GenericProcessingResult
                    {
                        Success = true,
                        Response = suggestions,
                        RequiresMoreInfo = true,
                        SuggestedEndpoints = mappingResult.AlternativeEndpoints
                    };
                }
                
                // No alternatives, use casual response
                var casualResponse = await GetCasualResponseAsync(message, endpoints, personalizedContext, cancellationToken);
                
                return new GenericProcessingResult
                {
                    Success = true,
                    Response = casualResponse,
                    RequiresMoreInfo = false
                };
            }

            var endpoint = mappingResult.MatchedEndpoint;
            _logger.LogInformation("Matched endpoint: {Intent} with confidence {Confidence}", 
                endpoint.Intent, mappingResult.Confidence);

            // ✅ NEW: Step 2.5: Entity Resolution for update/delete/get operations
            if (endpoint.Intent.StartsWith("update_") || 
                endpoint.Intent.StartsWith("delete_") || 
                endpoint.Intent.StartsWith("get_") && endpoint.Intent.Contains("_detail"))
            {
                var hasTrackFields = endpoint.Schema?.Fields?.Any(f => f.IsTrackField) ?? false;
                
                if (hasTrackFields && !mappingResult.ExtractedParameters.ContainsKey("id"))
                {
                    _logger.LogInformation("Attempting entity resolution for {Intent}", endpoint.Intent);
                    
                    var resolution = await _entityResolver.ResolveEntityAsync(
                        message,
                        endpoint,
                        tenantId,
                        backendUrl,
                        apiKey,
                        cancellationToken);
                    
                    if (resolution.Success)
                    {
                        // Entity found! Add ID to parameters
                        mappingResult.ExtractedParameters["id"] = resolution.EntityId!;
                        
                        _logger.LogInformation(
                            "✅ Entity resolved: {EntityName} (ID: {EntityId}, Confidence: {Confidence:P0}, Field: {Field})",
                            resolution.EntityName,
                            resolution.EntityId,
                            resolution.Confidence,
                            resolution.MatchedField);
                        
                        // If there are alternative matches, inform user
                        if (resolution.AlternativeMatches.Any() && resolution.Confidence < 0.95)
                        {
                            var alternatives = string.Join(", ", 
                                resolution.AlternativeMatches.Take(2).Select(m => m.EntityName));
                            
                            _logger.LogInformation("Alternative matches found: {Alternatives}", alternatives);
                        }
                    }
                    else if (resolution.Confidence > 0 && resolution.Confidence < 0.7)
                    {
                        // Low confidence - ask for clarification
                        var clarificationMsg = $"'{message}' için birden fazla kayıt bulundu veya emin olamadım. ";
                        
                        if (resolution.AlternativeMatches.Any())
                        {
                            clarificationMsg += "Şunlardan birini mi demek istediniz?\n\n";
                            clarificationMsg += string.Join("\n", 
                                resolution.AlternativeMatches.Take(3)
                                    .Select((m, i) => $"{i + 1}. {m.EntityName}"));
                            clarificationMsg += "\n\nLütfen tam adını söyler misiniz?";
                        }
                        else
                        {
                            clarificationMsg += "Lütfen tam adını söyler misiniz?";
                        }
                        
                        return new GenericProcessingResult
                        {
                            Success = false,
                            Response = clarificationMsg
                        };
                    }
                    else
                    {
                        // Entity not found
                        return new GenericProcessingResult
                        {
                            Success = false,
                            Response = resolution.ErrorMessage ?? 
                                      $"'{message}' için kayıt bulunamadı. Lütfen tam adını söyler misiniz?"
                        };
                    }
                }
            }

            // Step 3: Fill slots (parameters) - AI-powered smart field selection with personalized context
            // ✅ NEW: Include media context and backend URL for navigation resolution
            _logger.LogInformation("🎯 Calling AnalyzeSlotsAsync for {Intent} with {ParamCount} extracted params", 
                endpoint.Intent, mappingResult.ExtractedParameters?.Count ?? 0);
            var slotResult = await _slotFiller.AnalyzeSlotsAsync(
                endpoint, 
                mappingResult.ExtractedParameters, 
                mediaContext,      // ✅ NEW: Media context for attachment
                backendUrl,        // ✅ NEW: Backend URL for navigation resolution
                apiKey,            // ✅ NEW: API key for backend calls
                tenantId,          // ✅ NEW: Tenant ID for multi-tenant backends
                personalizedContext, 
                cancellationToken);
            _logger.LogInformation("✅ AnalyzeSlotsAsync returned: IsComplete={IsComplete}, MissingSlots={MissingCount}", 
                slotResult.IsComplete, slotResult.MissingSlots?.Count ?? 0);

            if (!slotResult.IsComplete)
            {
                // Build AI-powered helpful response showing what we have and what we need
                var progressMessage = await BuildProgressMessageAsync(endpoint, slotResult.FilledSlots, slotResult.MissingSlots, personalizedContext, cancellationToken);
                var fullResponse = progressMessage + "\n\n" + slotResult.NextQuestion;
                
                // Need more information from user
                return new GenericProcessingResult
                {
                    Success = true,
                    RequiresMoreInfo = true,
                    Response = fullResponse,
                    PendingEndpoint = endpoint,
                    CurrentSlots = slotResult.FilledSlots,
                    MissingSlots = slotResult.MissingSlots
                };
            }

            // Step 4: Check if confirmation is needed
            // Skip confirmation for GET requests (read operations) - they're safe and fast
            bool isReadOperation = endpoint.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase);
            bool needsConfirmation = !isReadOperation && endpoint.RequiresConfirmation;
            
            if (needsConfirmation)
            {
                // Show user what will be done with collected parameters (AI-powered)
                var confirmationMessage = await BuildConfirmationMessageAsync(endpoint, slotResult.FilledSlots, personalizedContext, cancellationToken);
                
                return new GenericProcessingResult
                {
                    Success = true,
                    RequiresConfirmation = true,
                    Response = confirmationMessage,
                    PendingEndpoint = endpoint,
                    CurrentSlots = slotResult.FilledSlots
                };
            }

            // Step 5: Execute command immediately (no confirmation needed for read operations)
            var executionResult = await _executor.ExecuteAsync(
                endpoint,
                slotResult.FilledSlots,
                tenantId,
                backendUrl,
                phoneNumber: phoneNumber,
                apiKey: apiKey,
                cancellationToken: cancellationToken);

            if (!executionResult.Success)
            {
                // ✅ Phase 8: Report failure for learning
                if (_intentMapper is DynamicIntentMapper dm)
                {
                    await dm.ReportFailureAsync(processedMessage, endpoint.Intent, cancellationToken);
                }
                
                var errorMsg = detectedLanguage == "en"
                    ? $"An error occurred while executing the command: {executionResult.ErrorMessage}"
                    : $"Komut çalıştırılırken hata oluştu: {executionResult.ErrorMessage}";
                    
                return new GenericProcessingResult
                {
                    Success = false,
                    Response = errorMsg
                };
            }

            // ✅ Phase 8: Report success for learning
            if (_intentMapper is DynamicIntentMapper dynamicMapper2)
            {
                await dynamicMapper2.ReportSuccessAsync(
                    processedMessage, 
                    endpoint.Intent, 
                    mappingResult.Confidence, 
                    cancellationToken);
            }

            // ✅ Phase 8: Save context for future reference resolution
            if (_contextManager != null && conversationId.HasValue)
            {
                await _contextManager.SaveContextFromExecutionAsync(
                    conversationId.Value,
                    endpoint.Intent,
                    slotResult.FilledSlots,
                    executionResult.ParsedResponse,
                    cancellationToken);
            }

            // Step 6: Format success response (AI-powered)
            var successMessage = await BuildSuccessMessageAsync(endpoint, executionResult, cancellationToken);

            return new GenericProcessingResult
            {
                Success = true,
                Response = successMessage,
                ExecutionResult = executionResult
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            return new GenericProcessingResult
            {
                Success = false,
                Response = "Bir hata oluştu. Lütfen tekrar deneyin."
            };
        }
    }

    /// <summary>
    /// Continue a conversation that requires more information
    /// </summary>
    public async Task<GenericProcessingResult> ContinueConversationAsync(
        string phoneNumber,
        string message,
        DiscoveredEndpoint pendingEndpoint,
        Dictionary<string, object> currentSlots,
        List<MissingSlot> missingSlots,
        Guid tenantId,
        string backendUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // ✅ IMPROVED: Get user profile and personalized context
            string? personalizedContext = null;
            Guid? userProfileId = null;
            try
            {
                var userProfile = await _userProfileService.GetOrCreateProfileAsync(
                    phoneNumber,
                    tenantId,
                    cancellationToken: cancellationToken);
                
                userProfileId = userProfile.Id;
                
                personalizedContext = await _userProfileService.BuildPersonalizedContextAsync(
                    userProfile.Id,
                    cancellationToken);
                
                if (!string.IsNullOrEmpty(personalizedContext))
                {
                    _logger.LogDebug("Using personalized context in continuation for user {PhoneNumber}", phoneNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get personalized context in continuation, continuing without it");
            }

            var messageLower = message.ToLower().Trim();
            
            // Check if this is a confirmation response (Evet/Hayır)
            if (!missingSlots.Any()) // All slots filled, waiting for confirmation
            {
                if (messageLower.Contains("evet") || messageLower.Contains("yes") || 
                    messageLower.Contains("tamam") || messageLower.Contains("onay"))
                {
                    // User confirmed, execute the command
                    _logger.LogInformation("User confirmed execution for {Intent}", pendingEndpoint.Intent);
                    
                    // Send "processing" message
                    await _whatsAppService.SendTextMessageAsync(phoneNumber, 
                        "⏳ İşleminiz yapılıyor, lütfen bekleyin...", cancellationToken);
                    
                    var confirmedExecutionResult = await _executor.ExecuteAsync(
                        pendingEndpoint,
                        currentSlots,
                        tenantId,
                        backendUrl,
                        phoneNumber: phoneNumber,
                        apiKey: apiKey,
                        cancellationToken: cancellationToken);

                    if (!confirmedExecutionResult.Success)
                    {
                        var errorMessage = await BuildErrorMessageAsync(
                            confirmedExecutionResult.ErrorMessage ?? "Unknown error",
                            pendingEndpoint.Description,
                            cancellationToken);
                        
                        return new GenericProcessingResult
                        {
                            Success = false,
                            Response = errorMessage
                        };
                    }

                    // ✅ Phase 8: Report success for learning
                    if (_intentMapper is DynamicIntentMapper dynamicMapper)
                    {
                        await dynamicMapper.ReportSuccessAsync(
                            message, 
                            pendingEndpoint.Intent, 
                            0.95, // High confidence from user confirmation
                            cancellationToken);
                    }
                    
                    // ✅ Phase 8: Record confirmation feedback
                    if (_feedbackService != null && userProfileId.HasValue)
                    {
                        await _feedbackService.RecordConfirmationAsync(
                            userProfileId.Value,
                            null, // conversationId
                            null, // commandId
                            message,
                            pendingEndpoint.Intent,
                            cancellationToken);
                    }
                    
                    var confirmedSuccessMessage = await BuildSuccessMessageAsync(pendingEndpoint, confirmedExecutionResult, cancellationToken);
                    return new GenericProcessingResult
                    {
                        Success = true,
                        Response = confirmedSuccessMessage,
                        ExecutionResult = confirmedExecutionResult
                    };
                }
                else if (messageLower.Contains("hayır") || messageLower.Contains("no") || 
                         messageLower.Contains("iptal") || messageLower.Contains("cancel"))
                {
                    // User cancelled
                    _logger.LogInformation("User cancelled execution for {Intent}", pendingEndpoint.Intent);
                    
                    // ✅ Phase 8: Record cancellation for learning
                    if (_feedbackService != null && userProfileId.HasValue)
                    {
                        await _feedbackService.RecordCancellationAsync(
                            userProfileId.Value,
                            null, // conversationId
                            null, // commandId
                            message,
                            pendingEndpoint.Intent,
                            reason: "User cancelled before execution",
                            cancellationToken);
                    }
                    
                    // ✅ Phase 8: Report failure for learning
                    if (_intentMapper is DynamicIntentMapper dm)
                    {
                        await dm.ReportFailureAsync(message, pendingEndpoint.Intent, cancellationToken);
                    }
                    
                    return new GenericProcessingResult
                    {
                        Success = true,
                        Response = "❌ İşlem iptal edildi. Başka bir şey yapmak ister misiniz?"
                    };
                }
            }
            
            // Check if there are missing slots to fill
            if (!missingSlots.Any())
            {
                // No missing slots, but user sent a message that's not confirmation
                // Check if user wants to cancel or start something new
                var shouldCancel = await ShouldCancelDialogueAsync(message, pendingEndpoint, cancellationToken);
                
                if (shouldCancel)
                {
                    _logger.LogInformation("User wants to cancel/change. Treating as new conversation: {Message}", message);
                    
                    // Return a special result that signals to cancel the old dialogue
                    return new GenericProcessingResult
                    {
                        Success = false,
                        ShouldCancelDialogue = true,
                        Response = "" // Will be processed as new message
                    };
                }
                
                // User's message is unclear, ask for clarification
                _logger.LogWarning("User sent unclear message while waiting for confirmation: {Message}", message);
                return new GenericProcessingResult
                {
                    Success = true,
                    Response = "🤔 Üzgünüm, anlamadım. Lütfen 'Evet' veya 'Hayır' ile yanıt verin.\n\n" +
                               "Veya vazgeçmek isterseniz 'iptal' yazabilirsiniz."
                };
            }
            
            // Check for cancellation during slot filling
            var isCancellation = await ShouldCancelDialogueAsync(message, pendingEndpoint, cancellationToken);
            if (isCancellation)
            {
                 _logger.LogInformation("User cancelled during slot filling: {Message}", message);
                 return new GenericProcessingResult
                 {
                     Success = false,
                     ShouldCancelDialogue = true,
                     Response = ""
                 };
            }

            // Update slots with new user input
            var slotResult = await _slotFiller.UpdateSlotsAsync(
                pendingEndpoint,
                currentSlots,
                message,
                missingSlots,
                cancellationToken);

            if (!slotResult.IsComplete)
            {
                // Still need more information (AI-powered progress message with personalized context)
                var progressMessage = await BuildProgressMessageAsync(pendingEndpoint, slotResult.FilledSlots, slotResult.MissingSlots, personalizedContext, cancellationToken);
                var fullResponse = progressMessage + "\n\n" + slotResult.NextQuestion;
                
                return new GenericProcessingResult
                {
                    Success = true,
                    RequiresMoreInfo = true,
                    Response = fullResponse,
                    PendingEndpoint = pendingEndpoint,
                    CurrentSlots = slotResult.FilledSlots,
                    MissingSlots = slotResult.MissingSlots
                };
            }

            // All slots filled, ask for confirmation (AI-powered with personalized context)
            var confirmationMessage = await BuildConfirmationMessageAsync(pendingEndpoint, slotResult.FilledSlots, personalizedContext, cancellationToken);
            return new GenericProcessingResult
            {
                Success = true,
                RequiresConfirmation = true,
                Response = confirmationMessage,
                PendingEndpoint = pendingEndpoint,
                CurrentSlots = slotResult.FilledSlots
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error continuing conversation");
            return new GenericProcessingResult
            {
                Success = false,
                Response = "Bir hata oluştu. Lütfen tekrar deneyin."
            };
        }
    }

    /// <summary>
    /// AI-powered intent detection: Does user want to cancel current dialogue?
    /// </summary>
    private async Task<bool> ShouldCancelDialogueAsync(
        string message,
        DiscoveredEndpoint currentEndpoint,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = $@"You are analyzing user intent in a conversation where they were asked for INPUT or CONFIRMATION.

Current operation: {currentEndpoint.Description}
User's message: ""{message}""

Determine if the user wants to:
1. CANCEL the current operation and do something else
2. Provide input or confirm (Continue)
3. Just chatting/unclear (not cancelling)

Examples of CANCEL intent:
- ""vazgeçtim"" (I gave up)
- ""iptal"" (cancel)
- ""başka şey yapalım"" (let's do something else)
- ""hayır, başka bir şey"" (no, something else)
- ""kod şablonu oluştur"" (new command)
- ""ürün ekle"" (new command)
- ""selam"" (greeting - likely wants to start fresh)

Respond with ONLY ""true"" or ""false"". Do NOT add any explanation.";

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
            var result = response.Trim().ToLower();
            
            _logger.LogInformation("AI cancel intent detection: {Message} -> {Result}", message, result);
            
            return result.Contains("true");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI cancel intent detection failed, using fallback");
            
            // Fallback: Check for explicit cancel keywords
            var messageLower = message.ToLower().Trim();
            return messageLower.Contains("vazgeç") ||
                   messageLower.Contains("iptal") ||
                   messageLower.Contains("cancel") ||
                   messageLower.Contains("başka") ||
                   messageLower.Contains("hayır") && messageLower.Contains("başka");
        }
    }

    /// <summary>
    /// AI-powered progress message generation
    /// </summary>
    private async Task<string> BuildProgressMessageAsync(
        DiscoveredEndpoint endpoint, 
        Dictionary<string, object> filledSlots, 
        List<MissingSlot> missingSlots,
        string? personalizedContext = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var filledSlotsJson = filledSlots.Any() 
                ? System.Text.Json.JsonSerializer.Serialize(filledSlots) 
                : "none";
            
            var contextInfo = !string.IsNullOrEmpty(personalizedContext) 
                ? $"\n\nUser Context:\n{personalizedContext}" 
                : "";
            
            var prompt = $@"Generate a brief progress update message in Turkish for a multi-step operation.

Operation: {endpoint.Description}
Collected parameters: {filledSlotsJson}
Missing parameters: {missingSlots.Count}{contextInfo}

Create a message that:
1. Shows the operation name
2. Lists collected parameters naturally (if any)
3. Shows how many parameters are still needed
4. Is brief (2-3 lines max)
5. Uses appropriate emojis (📋 for operation, ✅ for collected, ⏳ for pending)
6. Is encouraging and helpful

Format naturally in Turkish.
IMPORTANT: Respond with ONLY the message content. Do not add any intro/outro like 'Here is the message:'.";

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI progress message generation failed, using fallback");
            
            // Fallback
            var message = $"📋 **{endpoint.Description}**\n\n";
            if (filledSlots.Any())
            {
                message += $"✅ {filledSlots.Count} bilgi toplandı\n";
            }
            if (missingSlots.Any())
            {
                message += $"⏳ {missingSlots.Count} bilgi daha gerekli\n";
            }
            return message.TrimEnd();
        }
    }

    /// <summary>
    /// AI-powered confirmation message generation with template caching
    /// </summary>
    private async Task<string> BuildConfirmationMessageAsync(
        DiscoveredEndpoint endpoint, 
        Dictionary<string, object> slots,
        string? personalizedContext = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // ✅ IMPROVED: Check cache for confirmation template
            var cacheKey = $"{CONFIRMATION_TEMPLATE_CACHE_PREFIX}{endpoint.Intent}";
            var cachedTemplate = await _cache.GetAsync<string>(cacheKey);
            
            if (!string.IsNullOrEmpty(cachedTemplate))
            {
                _logger.LogDebug("Using cached confirmation template for {Intent}", endpoint.Intent);
                
                // Replace placeholders with actual values
                var message = cachedTemplate;
                foreach (var slot in slots)
                {
                    message = message.Replace($"{{{slot.Key}}}", slot.Value?.ToString() ?? "");
                }
                return message;
            }
            
            var slotsJson = System.Text.Json.JsonSerializer.Serialize(slots);
            
            var contextInfo = !string.IsNullOrEmpty(personalizedContext) 
                ? $"\n\nUser Context:\n{personalizedContext}" 
                : "";
            
            var prompt = $@"Generate a natural confirmation message in Turkish before executing an operation.

Operation: {endpoint.Description}
Parameters: {slotsJson}{contextInfo}

Create a message that:
1. Confirms what will be done
2. Lists all parameters clearly and naturally
3. Asks for confirmation in a friendly way
4. Provides clear Yes/No options
5. Uses appropriate emojis (✅ for confirmation, 📝 for parameters, ❓ for question)
6. Is professional but warm
7. Keep it concise (4-5 lines max)
8. ✅ NEW: Use placeholders like {{ParameterName}} for dynamic values so we can cache the template

Format naturally in Turkish.
IMPORTANT: Respond with ONLY the message content. Do not add any intro/outro like 'Here is the message:'.";

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
            var template = response.Trim();
            
            // ✅ IMPROVED: Cache the template for future use
            try
            {
                await _cache.SetAsync(cacheKey, template, TEMPLATE_CACHE_DURATION);
                _logger.LogDebug("Cached confirmation template for {Intent}", endpoint.Intent);
            }
            catch (Exception cacheEx)
            {
                _logger.LogWarning(cacheEx, "Failed to cache confirmation template");
            }
            
            // Replace placeholders with actual values
            var finalMessage = template;
            foreach (var slot in slots)
            {
                finalMessage = finalMessage.Replace($"{{{slot.Key}}}", slot.Value?.ToString() ?? "");
            }
            
            return finalMessage;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI confirmation message generation failed, using fallback");
            
            // Fallback
            var message = $"✅ Anladım! **{endpoint.Description}** işlemini yapacağım.\n\n";
            message += "📝 **Parametreler:**\n";
            foreach (var slot in slots)
            {
                message += $"  • {slot.Key}: **{slot.Value}**\n";
            }
            message += "\n❓ Devam etmek istiyor musunuz? (Evet/Hayır)";
            return message;
        }
    }

    /// <summary>
    /// AI-powered success message generation
    /// </summary>
    private async Task<string> BuildSuccessMessageAsync(
        DiscoveredEndpoint endpoint, 
        GenericExecutionResult executionResult,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // ✅ FIX: Use ResponseBody directly instead of re-serializing ParsedResponse
            // ParsedResponse is a Newtonsoft JObject, System.Text.Json can't serialize it properly
            var resultJson = !string.IsNullOrEmpty(executionResult.ResponseBody) 
                ? executionResult.ResponseBody 
                : "no details";
            
            // 🐛 DEBUG: Log what we're sending to AI
            _logger.LogInformation("🤖 Sending to AI - Intent: {Intent}, IsListOp: {IsListOp}, DataLength: {Length}", 
                endpoint.Intent, 
                endpoint.Intent.StartsWith("list_") || endpoint.Intent.StartsWith("get_"),
                resultJson?.Length ?? 0);
            _logger.LogInformation("📊 Data preview (first 300 chars): {Data}", 
                resultJson?.Length > 300 ? resultJson.Substring(0, 300) + "..." : resultJson);
            
            // Check if this is a list/read operation - display actual data instead of just celebrating
            bool isListOperation = endpoint.Intent.StartsWith("list_") || endpoint.Intent.StartsWith("get_");
            
            string prompt;
            if (isListOperation)
            {
                prompt = $@"Format the API response data in a user-friendly way in Turkish.

Operation: {endpoint.Description}
Data: {resultJson}
Duration: {executionResult.ExecutionTime.TotalMilliseconds:F0}ms

Create a message that:
1. Shows the ACTUAL DATA from the response (names, IDs, details, etc.)
2. Formats it as a clear, readable list or summary
3. If it's a list, show each item with key details
4. If it's empty, say so clearly
5. Keep it concise but informative
6. Use appropriate emojis (📋 for lists, 👤 for users, 📦 for products, etc.)
7. NO generic celebration messages - show the actual data!

Format naturally in Turkish.
IMPORTANT: Respond with ONLY the formatted data. Do not add any intro/outro like 'Here is the data:'.

Example for customer list:
📋 Müşteri Listesi (5 kayıt):

1. 👤 Ahmet Yılmaz - 0555 123 4567
2. 👤 Mehmet Demir - 0555 234 5678
...

Example for empty list:
📋 Henüz kayıtlı müşteri bulunmuyor.";
            }
            else
            {
                prompt = $@"Generate a celebratory success message in Turkish after completing an operation.

Operation: {endpoint.Description}
Result: {resultJson}
Duration: {executionResult.ExecutionTime.TotalMilliseconds:F0}ms

Create a message that:
1. Celebrates the success enthusiastically
2. Summarizes key results naturally (ID, name, code, etc. if available)
3. Shows execution time
4. Is brief (3-4 lines max)
5. Uses appropriate emojis (✅ for success, 🎉 for celebration, 📊 for details, ⏱️ for time)
6. Is warm and encouraging

Format naturally in Turkish.
IMPORTANT: Respond with ONLY the message content. Do not add any intro/outro like 'Here is the message:'.";
            }

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI success message generation failed, using fallback");
            
            // Fallback
            var message = $"✅ **Başarılı!**\n\n";
            message += $"🎉 {endpoint.Description} işlemi tamamlandı.\n";
            message += $"⏱️ İşlem süresi: {executionResult.ExecutionTime.TotalMilliseconds:F0}ms";
            return message;
        }
    }

    /// <summary>
    /// AI-powered error message generation
    /// </summary>
    private async Task<string> BuildErrorMessageAsync(
        string errorMessage,
        string context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = $@"Generate a helpful, empathetic error message in Turkish.

Error: {errorMessage}
Context: {context}

Create a message that:
1. Explains what went wrong in simple terms
2. Suggests what the user can try
3. Is empathetic and supportive (not blaming)
4. Is brief (2-3 lines max)
5. Uses appropriate emojis (❌ for error, 💡 for suggestion)
6. Offers to help further

Format naturally in Turkish.
IMPORTANT: Respond with ONLY the message content. Do not add any intro/outro like 'Here is the message:'.";

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI error message generation failed, using fallback");
            return $"❌ Hata oluştu: {errorMessage}\n\nLütfen tekrar deneyin veya farklı bir şekilde ifade edin.";
        }
    }

    /// <summary>
    /// AI-powered field name translation
    /// </summary>
    private async Task<string> TranslateFieldNameAsync(
        string fieldName, 
        string context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var prompt = $@"Translate this API field name to natural Turkish that a non-technical user would understand.

Field: {fieldName}
Context: {context}

Provide ONLY the Turkish translation (1-2 words max), nothing else.
Examples:
- name → İsim
- description → Açıklama
- price → Fiyat
- createdAt → Oluşturulma Tarihi
IMPORTANT: Output ONLY the translated word.";

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI field translation failed, using fallback");
            
            // Fallback to simple translation
            return fieldName.ToLower() switch
            {
                "name" => "İsim",
                "title" => "Başlık",
                "description" => "Açıklama",
                "price" => "Fiyat",
                "code" => "Kod",
                _ => fieldName
            };
        }
    }

    /// <summary>
    /// Generate AI-powered casual response for non-command messages
    /// </summary>
    private async Task<string> GetCasualResponseAsync(
        string message, 
        List<DiscoveredEndpoint>? availableEndpoints = null,
        string? personalizedContext = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Build context about available capabilities
            var capabilitiesContext = "";
            if (availableEndpoints != null && availableEndpoints.Any())
            {
                var capabilities = availableEndpoints
                    .Take(10)
                    .Select(e => $"- {e.Description}")
                    .ToList();
                capabilitiesContext = $"\n\nAvailable capabilities:\n{string.Join("\n", capabilities)}";
            }

            var userContextInfo = !string.IsNullOrEmpty(personalizedContext)
                ? $"\n\nUser Profile:\n{personalizedContext}"
                : "";

            var prompt = $@"You are a friendly, helpful Turkish-speaking AI assistant for a business management system.

User said: ""{message}""
{capabilitiesContext}{userContextInfo}

Generate a natural, conversational Turkish response. Guidelines:
1. If greeting → Greet warmly and offer help
2. If thanks → Acknowledge gracefully
3. If goodbye → Say goodbye warmly
4. If asking what you can do → List 3-4 key capabilities naturally
5. If unclear → Ask for clarification politely
6. Keep it brief (2-3 sentences max)
7. Use appropriate emojis sparingly
8. Be professional but friendly

Respond in Turkish naturally, as if you're a real person having a conversation.
IMPORTANT: Respond with ONLY the message content. Do not add any intro/outro like 'Here is the message:'.";

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI casual response generation failed, using fallback");
        }

        // Fallback to simple response if AI fails
        return "Merhaba! 👋 Size nasıl yardımcı olabilirim?";
    }

    /// <summary>
    /// ✅ NEW: Build AI-powered suggestion message with alternative endpoints
    /// </summary>
    private async Task<string> BuildSuggestionMessageAsync(
        string userMessage,
        List<DiscoveredEndpoint> alternatives,
        string? personalizedContext = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var alternativesJson = System.Text.Json.JsonSerializer.Serialize(
                alternatives.Take(3).Select(e => new
                {
                    e.Intent,
                    e.Description,
                    e.Priority
                }));

            var userContextInfo = !string.IsNullOrEmpty(personalizedContext)
                ? $"\n\nUser Profile:\n{personalizedContext}"
                : "";

            var prompt = $@"User said: ""{userMessage}""

We couldn't find an exact match, but here are some similar options:
{alternativesJson}{userContextInfo}

Generate a helpful Turkish message that:
1. Acknowledges we didn't fully understand
2. Lists the alternative options in a friendly, numbered format
3. Asks the user to clarify which one they meant
4. Keeps it brief and conversational

Example format:
""Tam olarak anlayamadım. Şunlardan birini mi demek istediniz?

1. Ürün oluştur
2. Müşteri ekle
3. Kod şablonu oluştur

Lütfen hangisini yapmak istediğinizi belirtir misiniz?""

IMPORTANT: Respond with ONLY the message content in Turkish. Use emojis sparingly (max 1-2).";

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
            return response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI suggestion message generation failed, using fallback");
            
            // Fallback: Simple numbered list
            var suggestions = alternatives
                .Take(3)
                .Select((e, i) => $"{i + 1}. {e.Description}")
                .ToList();
            
            return $"Tam olarak anlayamadım. 🤔 Şunlardan birini mi demek istediniz?\n\n{string.Join("\n", suggestions)}\n\nLütfen hangisini yapmak istediğinizi belirtir misiniz?";
        }
    }

    /// <summary>
    /// ✅ NEW: Record intent mapping feedback for learning
    /// </summary>
    public async Task RecordIntentFeedbackAsync(
        string message,
        string intent,
        bool wasCorrect,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Record in intent mapper (for pattern learning)
            await _intentMapper.LearnFromFeedbackAsync(message, intent, wasCorrect, tenantId);
            
            // ✅ ADDED: Record in confidence calibrator (for threshold optimization)
            if (_calibrator != null)
            {
                // Get the confidence that was used for this intent
                // In a real scenario, we'd store this with the command, but for now we'll estimate
                var confidence = wasCorrect ? 0.85 : 0.45; // Rough estimate
                
                await _calibrator.LearnFromFeedbackAsync(
                    intent,
                    confidence,
                    wasCorrect,
                    cancellationToken);
                
                _logger.LogDebug("Recorded confidence calibration feedback for {Intent}", intent);
            }
            
            _logger.LogInformation("Recorded intent feedback: {Message} -> {Intent} (Correct: {WasCorrect})", 
                message, intent, wasCorrect);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record intent feedback");
        }
    }
}

/// <summary>
/// Result of generic message processing
/// </summary>
public class GenericProcessingResult
{
    public bool Success { get; set; }
    public string Response { get; set; } = string.Empty;
    public bool RequiresMoreInfo { get; set; }
    public bool RequiresConfirmation { get; set; }
    public bool ShouldCancelDialogue { get; set; }
    public DiscoveredEndpoint? PendingEndpoint { get; set; }
    public Dictionary<string, object> CurrentSlots { get; set; } = new();
    public List<MissingSlot> MissingSlots { get; set; } = new();
    public GenericExecutionResult? ExecutionResult { get; set; }
    public List<DiscoveredEndpoint> SuggestedEndpoints { get; set; } = new();
}
