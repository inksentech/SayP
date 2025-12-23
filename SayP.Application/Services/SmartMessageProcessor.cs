using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Entities;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;

namespace SayP.Application.Services;

/// <summary>
/// ⚠️ DEPRECATED: Akıllı mesaj işleme servisi - tüm AI servislerini koordine eder
/// Use GenericConversationManager instead for modern dynamic AI pipeline.
/// This will be removed in v3.0.0
/// </summary>
[Obsolete("Use GenericConversationManager for dynamic AI processing. This will be removed in v3.0.0")]
public class SmartMessageProcessor
{
    private readonly ISmartIntentClassifier _intentClassifier;
    private readonly EntityExtractor _entityExtractor;
    private readonly IntelligentFallbackProvider _fallbackProvider;
    private readonly DialogueStateManager _dialogueStateManager;
    private readonly SlotFillingManager _slotFillingManager;
    private readonly IAIProvider _aiProvider;
    private readonly ISayPDbContext _context;
    private readonly UserProfileService _userProfileService;
    private readonly TenantSettingsService _tenantSettingsService;
    private readonly ILogger<SmartMessageProcessor> _logger;

    public SmartMessageProcessor(
        ISmartIntentClassifier intentClassifier,
        EntityExtractor entityExtractor,
        IntelligentFallbackProvider fallbackProvider,
        DialogueStateManager dialogueStateManager,
        SlotFillingManager slotFillingManager,
        IAIProvider aiProvider,
        ISayPDbContext context,
        UserProfileService userProfileService,
        TenantSettingsService tenantSettingsService,
        ILogger<SmartMessageProcessor> logger)
    {
        _intentClassifier = intentClassifier;
        _entityExtractor = entityExtractor;
        _fallbackProvider = fallbackProvider;
        _dialogueStateManager = dialogueStateManager;
        _slotFillingManager = slotFillingManager;
        _aiProvider = aiProvider;
        _context = context;
        _userProfileService = userProfileService;
        _tenantSettingsService = tenantSettingsService;
        _logger = logger;
    }

    /// <summary>
    /// Process message with full AI pipeline
    /// </summary>
    public async Task<SmartProcessingResult> ProcessMessageAsync(
        string message,
        Conversation conversation,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            _logger.LogInformation("Smart processing message for conversation {ConversationId}", conversation.Id);

            // 0. Get tenant settings
            var tenantSettings = await _tenantSettingsService.GetSettingsAsync(
                conversation.TenantId,
                cancellationToken);

            _logger.LogDebug("Tenant settings loaded for {TenantId}: SmartIntent={SmartIntent}, UserLearning={UserLearning}", 
                conversation.TenantId, 
                tenantSettings.EnableSmartIntentClassifier,
                tenantSettings.EnableUserLearning);

            // 0.1. Get or create user profile (if enabled)
            UserProfile? userProfile = null;
            string? personalizedContext = null;

            if (tenantSettings.EnableUserLearning)
            {
                userProfile = await _userProfileService.GetOrCreateProfileAsync(
                    conversation.PhoneNumber,
                    conversation.TenantId,
                    cancellationToken: cancellationToken);

                // Build personalized context
                personalizedContext = await _userProfileService.BuildPersonalizedContextAsync(
                    userProfile.Id,
                    cancellationToken);

                _logger.LogDebug("Personalized context for user {UserId}: {Context}", 
                    userProfile.Id, personalizedContext);
            }

            // 1. Check for active dialogue state (multi-turn conversation) (if enabled)
            DialogueState? activeDialogue = null;
            
            if (tenantSettings.EnableMultiTurnDialogue)
            {
                activeDialogue = await _dialogueStateManager.GetActiveStateAsync(conversation.Id, cancellationToken);
            
                if (activeDialogue != null)
                {
                    _logger.LogInformation("Continuing multi-turn dialogue {DialogueId}", activeDialogue.Id);
                    return await ContinueDialogueAsync(message, activeDialogue, conversation, cancellationToken);
                }
            }

            // 2. Classify intent using smart classifier
            var intentResult = await _intentClassifier.ClassifyIntentAsync(message, conversation, cancellationToken);
            
            _logger.LogInformation("Intent classified: {Intent} with confidence {Confidence}", 
                intentResult.IntentName, intentResult.Confidence);

            // 3. If confidence is too low, use fallback
            if (intentResult.Confidence < 0.5f)
            {
                _logger.LogInformation("Low confidence, using intelligent fallback");
                return await UseIntelligentFallbackAsync(message, conversation, cancellationToken);
            }

            // 4. Check if it's a command or just conversation
            if (intentResult.IntentName == "None" || string.IsNullOrEmpty(intentResult.IntentName))
            {
                return new SmartProcessingResult
                {
                    IsCommand = false,
                    NaturalResponse = intentResult.Response ?? "Merhaba! Size nasıl yardımcı olabilirim?",
                    Success = true
                };
            }

            // 5. Parse intent to command type
            if (!Enum.TryParse<CommandType>(intentResult.IntentName, out var commandType))
            {
                _logger.LogWarning("Could not parse intent {Intent} to CommandType", intentResult.IntentName);
                return new SmartProcessingResult
                {
                    IsCommand = false,
                    NaturalResponse = "Üzgünüm, bu komutu anlayamadım.",
                    Success = true
                };
            }

            // 6. Extract entities using AI (primary) + regex fallback
            Dictionary<string, object> extractedEntities;
            
            try
            {
                // Try AI-powered extraction first (more flexible)
                _logger.LogDebug("Attempting AI-powered entity extraction");
                extractedEntities = await _aiProvider.ExtractEntitiesAsync(
                    message, 
                    commandType.ToString(), 
                    cancellationToken);
                
                _logger.LogInformation("AI extracted {Count} entities", extractedEntities.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI entity extraction failed, falling back to regex");
                extractedEntities = new Dictionary<string, object>();
            }
            
            // Fallback: Use regex-based extraction for missing entities
            var regexEntities = _entityExtractor.ExtractEntities(message, commandType);
            foreach (var entity in regexEntities)
            {
                if (!extractedEntities.ContainsKey(entity.Key) || extractedEntities[entity.Key] == null)
                {
                    extractedEntities[entity.Key] = entity.Value;
                    _logger.LogDebug("Filled missing entity '{Key}' from regex", entity.Key);
                }
            }
            
            // Merge with intent's extracted entities
            foreach (var entity in intentResult.ExtractedEntities)
            {
                if (!extractedEntities.ContainsKey(entity.Key))
                {
                    extractedEntities[entity.Key] = entity.Value;
                }
            }

            // Normalize entities
            var normalizedEntities = _entityExtractor.NormalizeEntities(extractedEntities);

            _logger.LogInformation("Total extracted entities: {Count}", normalizedEntities.Count);

            // 7. Fill slots
            var slotFillingResult = _slotFillingManager.FillSlots(commandType, normalizedEntities);

            // 8. If slots are incomplete, start dialogue state
            if (!slotFillingResult.IsComplete && slotFillingResult.MissingSlots.Any())
            {
                _logger.LogInformation("Slots incomplete, starting dialogue state. Missing: {Missing}", 
                    string.Join(", ", slotFillingResult.MissingSlots));

                var dialogueState = await _dialogueStateManager.CreateStateAsync(
                    conversation.Id,
                    commandType,
                    slotFillingResult.FilledSlots,
                    slotFillingResult.MissingSlots,
                    cancellationToken);

                return new SmartProcessingResult
                {
                    IsCommand = false,
                    NaturalResponse = slotFillingResult.NextQuestion ?? "Lütfen eksik bilgileri tamamlayın.",
                    Success = true,
                    RequiresMoreInfo = true,
                    DialogueStateId = dialogueState.Id
                };
            }

            // 9. All slots filled, return command
            var result = new SmartProcessingResult
            {
                IsCommand = true,
                CommandType = commandType,
                CommandParameters = slotFillingResult?.FilledSlots ?? new Dictionary<string, object>(),
                Confidence = intentResult.Confidence,
                RequiresConfirmation = intentResult.RequiresConfirmation,
                ConfirmationMessage = intentResult.ConfirmationMessage,
                Success = true,
                UserProfileId = userProfile.Id
            };

            // 10. Log user behavior
            var responseTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            await _userProfileService.LogBehaviorAsync(
                userProfile.Id,
                "command_detected",
                new
                {
                    intent = intentResult.IntentName,
                    commandType = commandType.ToString(),
                    entities = normalizedEntities,
                    personalizedContext = !string.IsNullOrEmpty(personalizedContext)
                },
                message,
                commandType.ToString(),
                true,
                intentResult.Confidence,
                responseTime,
                cancellationToken);

            // Update profile activity
            userProfile.LastActivityAt = DateTime.UtcNow;
            userProfile.TotalMessageCount++;
            userProfile.TotalCommandCount++;
            await _context.SaveChangesAsync(cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in smart message processing");
            return new SmartProcessingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                NaturalResponse = "Bir hata oluştu. Lütfen tekrar deneyin."
            };
        }
    }

    /// <summary>
    /// Continue multi-turn dialogue
    /// </summary>
    private async Task<SmartProcessingResult> ContinueDialogueAsync(
        string message,
        DialogueState dialogueState,
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        try
        {
            // Extract entities from user's response
            var extractedEntities = _entityExtractor.ExtractEntities(message, dialogueState.PendingIntent);
            var normalizedEntities = _entityExtractor.NormalizeEntities(extractedEntities);

            // Get current collected slots
            var collectedSlots = _dialogueStateManager.GetCollectedSlots(dialogueState);
            
            // Fill slots with new information
            var slotFillingResult = _slotFillingManager.FillSlots(
                dialogueState.PendingIntent ?? CommandType.Unknown,
                normalizedEntities,
                collectedSlots);

            // Update dialogue state
            await _dialogueStateManager.UpdateStateAsync(
                dialogueState,
                normalizedEntities,
                slotFillingResult.MissingSlots,
                slotFillingResult.NextQuestion,
                cancellationToken);

            // Check if complete
            if (slotFillingResult.IsComplete)
            {
                _logger.LogInformation("Dialogue {DialogueId} completed", dialogueState.Id);
                
                await _dialogueStateManager.CompleteStateAsync(dialogueState, cancellationToken);

                return new SmartProcessingResult
                {
                    IsCommand = true,
                    CommandType = dialogueState.PendingIntent ?? CommandType.Unknown,
                    CommandParameters = slotFillingResult.FilledSlots,
                    Confidence = slotFillingResult.Confidence,
                    Success = true
                };
            }

            // Still missing slots
            return new SmartProcessingResult
            {
                IsCommand = false,
                NaturalResponse = slotFillingResult.NextQuestion ?? "Lütfen eksik bilgileri tamamlayın.",
                Success = true,
                RequiresMoreInfo = true,
                DialogueStateId = dialogueState.Id
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error continuing dialogue {DialogueId}", dialogueState.Id);
            
            await _dialogueStateManager.CancelStateAsync(
                dialogueState, 
                $"Error: {ex.Message}", 
                cancellationToken);

            return new SmartProcessingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                NaturalResponse = "Bir hata oluştu. Lütfen baştan başlayın."
            };
        }
    }

    /// <summary>
    /// Use intelligent fallback when confidence is low
    /// </summary>
    private async Task<SmartProcessingResult> UseIntelligentFallbackAsync(
        string message,
        Conversation conversation,
        CancellationToken cancellationToken)
    {
        try
        {
            // Try fallback extraction with multiple strategies
            var fallbackResult = await _fallbackProvider.ExtractWithFallbackAsync(
                message,
                _aiProvider,
                null, // No secondary provider for now
                null,
                cancellationToken);

            if (fallbackResult.Success && fallbackResult.CommandType != "None")
            {
                // Fallback succeeded, parse command
                if (Enum.TryParse<CommandType>(fallbackResult.CommandType, out var commandType))
                {
                    var extractedEntities = _entityExtractor.ExtractEntities(message, commandType);
                    var normalizedEntities = _entityExtractor.NormalizeEntities(extractedEntities);
                    var slotFillingResult = _slotFillingManager.FillSlots(commandType, normalizedEntities);

                    if (slotFillingResult.IsComplete)
                    {
                        return new SmartProcessingResult
                        {
                            IsCommand = true,
                            CommandType = commandType,
                            CommandParameters = slotFillingResult.FilledSlots,
                            Confidence = fallbackResult.Confidence,
                            Success = true
                        };
                    }
                }
            }

            // Fallback also failed, return natural response
            return new SmartProcessingResult
            {
                IsCommand = false,
                NaturalResponse = fallbackResult.ConfirmationMessage ?? "Üzgünüm, tam olarak anlayamadım. Lütfen daha açık belirtir misiniz?",
                Success = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in intelligent fallback");
            return new SmartProcessingResult
            {
                Success = false,
                ErrorMessage = ex.Message,
                NaturalResponse = "Bir hata oluştu. Lütfen tekrar deneyin."
            };
        }
    }
}

/// <summary>
/// Result of smart message processing
/// </summary>
public class SmartProcessingResult
{
    public bool Success { get; set; }
    public bool IsCommand { get; set; }
    public CommandType CommandType { get; set; }
    public Dictionary<string, object> CommandParameters { get; set; } = new();
    public double Confidence { get; set; }
    public bool RequiresConfirmation { get; set; }
    public string? ConfirmationMessage { get; set; }
    public string? NaturalResponse { get; set; }
    public string? ErrorMessage { get; set; }
    public bool RequiresMoreInfo { get; set; }
    public Guid? DialogueStateId { get; set; }
    public Guid? UserProfileId { get; set; }
}
