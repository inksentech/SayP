using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Entities;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;
using System.Text.Json;

// Resolve ambiguity between Domain.Entities.DialogueState and Domain.Models.DialogueState
using DialogueState = SayP.Domain.Entities.DialogueState;

namespace SayP.Application.Services;

/// <summary>
/// Handles WhatsApp messages using generic AI pipeline
/// </summary>
public class GenericWhatsAppHandler
{
    private readonly GenericConversationManager _genericConversationManager;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ISayPDbContext _context;
    private readonly IBackendUserService _backendUserService;
    private readonly UserProfileService _userProfileService; // ✅ ADDED: User learning
    private readonly IConfiguration _configuration;
    private readonly ILogger<GenericWhatsAppHandler> _logger;

    public GenericWhatsAppHandler(
        GenericConversationManager genericConversationManager,
        IWhatsAppService whatsAppService,
        ISayPDbContext context,
        IBackendUserService backendUserService,
        UserProfileService userProfileService, // ✅ ADDED: User learning
        IConfiguration configuration,
        ILogger<GenericWhatsAppHandler> logger)
    {
        _genericConversationManager = genericConversationManager;
        _whatsAppService = whatsAppService;
        _context = context;
        _backendUserService = backendUserService;
        _userProfileService = userProfileService; // ✅ ADDED: User learning
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Process incoming WhatsApp message using generic AI pipeline
    /// </summary>
    public async Task ProcessMessageAsync(
        string phoneNumber,
        string? messageText,
        string messageId,
        DateTime timestamp,
        MessageType messageType = MessageType.Text,
        string? mediaId = null,
        string? mimeType = null,
        string? caption = null,
        CancellationToken cancellationToken = default)
    {
        Guid? tenantId = null; // ✅ Declare outside try block for catch access
        
        try
        {
            _logger.LogInformation("Processing WhatsApp message from {PhoneNumber}: {Text}", 
                phoneNumber, messageText);

            // 1. Validate user and get tenant info
            var userAuthInfo = await _backendUserService.ValidatePhoneNumberAsync(phoneNumber);
            
            if (userAuthInfo == null || !userAuthInfo.IsAuthorized)
            {
                _logger.LogWarning("Unauthorized phone number: {PhoneNumber}", phoneNumber);
                await _whatsAppService.SendTextMessageAsync(phoneNumber, 
                    "Bu numara sistemde kayıtlı değil. Lütfen yöneticinizle iletişime geçin.");
                return;
            }

            tenantId = userAuthInfo.TenantId;
            var backendUrl = _configuration["Backend:ApiUrl"] 
                ?? Environment.GetEnvironmentVariable("BACKEND_API_URL") 
                ?? "http://localhost:5245";
            var apiKey = _configuration["SayP:ApiKey"] 
                ?? Environment.GetEnvironmentVariable("SAYP_API_KEY");

            _logger.LogInformation("Authorized user from tenant {TenantId}", tenantId);

            // 2. ✅ ADDED: Get or create user profile for learning
            var userName = $"{userAuthInfo.FirstName} {userAuthInfo.LastName}".Trim();
            if (string.IsNullOrEmpty(userName))
                userName = phoneNumber;
            
            var userProfile = await _userProfileService.GetOrCreateProfileAsync(
                phoneNumber,
                tenantId.Value,
                userName,
                cancellationToken);

            var startTime = DateTime.UtcNow;

            // 3. Get or create conversation
            var conversation = await GetOrCreateConversationAsync(
                phoneNumber, 
                tenantId.Value, 
                userAuthInfo.DefaultCompanyId,
                cancellationToken);

            // 4. Save incoming message
            await SaveMessageAsync(
                conversation.Id,
                messageId,
                messageType,
                MessageDirection.Incoming,
                messageText ?? caption ?? "",
                phoneNumber,
                cancellationToken);

            // 5. ✅ ADDED: Update user activity
            userProfile.LastActivityAt = DateTime.UtcNow;
            userProfile.TotalMessageCount++;
            await _context.SaveChangesAsync(cancellationToken);

            // 5.5 ✅ NEW: Handle media context
            MediaContext? mediaContext = null;
            if (!string.IsNullOrEmpty(mediaId))
            {
                mediaContext = new MediaContext
                {
                    MediaId = mediaId,
                    MimeType = mimeType,
                    Caption = caption,
                    ReceivedAt = DateTime.UtcNow
                };
                
                _logger.LogInformation("📷 Media received: {MediaId}, Type: {MimeType}", mediaId, mimeType);
                
                // If only media (no text), save to dialogue state for later use
                if (string.IsNullOrEmpty(messageText) && string.IsNullOrEmpty(caption))
                {
                    await SavePendingMediaAsync(conversation.Id, mediaContext, cancellationToken);
                    await _whatsAppService.SendTextMessageAsync(phoneNumber, 
                        "📷 Görsel alındı! Ne yapmak istersiniz? (örn: 'ürün oluştur', 'bu görseli kaydet')");
                    return;
                }
            }
            
            // Check for pending media from previous message
            if (mediaContext == null)
            {
                mediaContext = await GetPendingMediaAsync(conversation.Id, cancellationToken);
                if (mediaContext != null)
                {
                    _logger.LogInformation("📷 Using pending media: {MediaId}", mediaContext.MediaId);
                }
            }

            // 6. Check if continuing previous conversation
            var activeDialogue = await _context.DialogueStates
                .FirstOrDefaultAsync(d => 
                    d.ConversationId == conversation.Id && 
                    d.IsActive &&
                    d.ExpiresAt > DateTime.UtcNow,
                    cancellationToken);

            GenericProcessingResult result;

            if (activeDialogue != null && !string.IsNullOrEmpty(activeDialogue.PendingEndpointJson))
            {
                // Continue existing dialogue
                _logger.LogInformation("Continuing dialogue {DialogueId}", activeDialogue.Id);
                
                var pendingEndpoint = System.Text.Json.JsonSerializer.Deserialize<Domain.Models.DiscoveredEndpoint>(
                    activeDialogue.PendingEndpointJson);
                var currentSlots = string.IsNullOrEmpty(activeDialogue.CollectedSlotsJson)
                    ? new Dictionary<string, object>()
                    : System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(
                        activeDialogue.CollectedSlotsJson) ?? new Dictionary<string, object>();
                var missingSlots = string.IsNullOrEmpty(activeDialogue.MissingSlotsJson)
                    ? new List<MissingSlot>()
                    : System.Text.Json.JsonSerializer.Deserialize<List<MissingSlot>>(
                        activeDialogue.MissingSlotsJson) ?? new List<MissingSlot>();

                result = await _genericConversationManager.ContinueConversationAsync(
                    phoneNumber,
                    messageText ?? "",
                    pendingEndpoint!,
                    currentSlots,
                    missingSlots,
                    tenantId.Value,
                    backendUrl,
                    apiKey,
                    cancellationToken);

                // Check if user wants to cancel and start fresh
                if (result.ShouldCancelDialogue)
                {
                    _logger.LogInformation("User cancelled dialogue {DialogueId}, starting fresh", activeDialogue.Id);
                    
                    // Cancel old dialogue
                    activeDialogue.IsActive = false;
                    activeDialogue.IsComplete = false;
                    activeDialogue.UpdatedAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync(cancellationToken);
                    
                    // Process as new message
                    result = await _genericConversationManager.ProcessMessageAsync(
                        phoneNumber,
                        messageText ?? "",
                        tenantId.Value,
                        backendUrl,
                        apiKey,
                        cancellationToken);
                    
                    // Create new dialogue state if needed
                    if ((result.RequiresMoreInfo || result.RequiresConfirmation) && result.PendingEndpoint != null)
                    {
                        var newDialogueState = new DialogueState
                        {
                            ConversationId = conversation.Id,
                            PendingIntent = null,
                            PendingEndpointJson = System.Text.Json.JsonSerializer.Serialize(result.PendingEndpoint),
                            CollectedSlotsJson = System.Text.Json.JsonSerializer.Serialize(result.CurrentSlots),
                            MissingSlotsJson = System.Text.Json.JsonSerializer.Serialize(result.MissingSlots),
                            LastQuestion = result.Response,
                            IsActive = true,
                            ExpiresAt = DateTime.UtcNow.AddHours(24),
                            CreatedAt = DateTime.UtcNow
                        };

                        _context.DialogueStates.Add(newDialogueState);
                        await _context.SaveChangesAsync(cancellationToken);
                    }
                }
                else
                {
                    // Update dialogue state normally
                    if (result.RequiresMoreInfo || result.RequiresConfirmation)
                    {
                        activeDialogue.CollectedSlotsJson = System.Text.Json.JsonSerializer.Serialize(result.CurrentSlots);
                        activeDialogue.MissingSlotsJson = System.Text.Json.JsonSerializer.Serialize(result.MissingSlots);
                        activeDialogue.LastQuestion = result.Response;
                        activeDialogue.UpdatedAt = DateTime.UtcNow;
                    }
                    else
                    {
                        // Dialogue complete
                        activeDialogue.IsActive = false;
                        activeDialogue.IsComplete = true;
                        activeDialogue.UpdatedAt = DateTime.UtcNow;
                    }

                    await _context.SaveChangesAsync(cancellationToken);
                }
            }
            else
            {
                // New conversation - include media context
                result = await _genericConversationManager.ProcessMessageAsync(
                    phoneNumber,
                    messageText ?? caption ?? "",
                    tenantId.Value,
                    backendUrl,
                    apiKey,
                    mediaContext,  // ✅ NEW: Pass media context
                    cancellationToken);

                // Create dialogue state if needed (for multi-turn conversations or confirmations)
                if ((result.RequiresMoreInfo || result.RequiresConfirmation) && result.PendingEndpoint != null)
                {
                    var dialogueState = new DialogueState
                    {
                        ConversationId = conversation.Id,
                        PendingIntent = null, // Generic doesn't use CommandType enum
                        PendingEndpointJson = System.Text.Json.JsonSerializer.Serialize(result.PendingEndpoint),
                        CollectedSlotsJson = System.Text.Json.JsonSerializer.Serialize(result.CurrentSlots),
                        MissingSlotsJson = System.Text.Json.JsonSerializer.Serialize(result.MissingSlots),
                        LastQuestion = result.Response,
                        IsActive = true,
                        ExpiresAt = DateTime.UtcNow.AddHours(24),
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.DialogueStates.Add(dialogueState);
                    await _context.SaveChangesAsync(cancellationToken);
                }
            }

            // 7. Send response to user
            await _whatsAppService.SendTextMessageAsync(phoneNumber, result.Response, cancellationToken);

            // 8. Save outgoing message
            await SaveMessageAsync(
                conversation.Id,
                Guid.NewGuid().ToString(),
                MessageType.Text,
                MessageDirection.Outgoing,
                result.Response,
                Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID") ?? "",
                cancellationToken);

            // 9. ✅ ADDED: Log user behavior for learning
            var responseTime = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
            var isCommand = result.ExecutionResult != null;
            var commandSuccess = result.ExecutionResult?.Success ?? false;
            
            await _userProfileService.LogBehaviorAsync(
                userProfile.Id,
                isCommand ? "command_executed" : "casual_message",
                new
                {
                    intent = result.PendingEndpoint?.Intent ?? "none",
                    endpoint = result.PendingEndpoint?.Route,
                    success = commandSuccess,
                    requiresMoreInfo = result.RequiresMoreInfo,
                    requiresConfirmation = result.RequiresConfirmation,
                    parameters = result.CurrentSlots
                },
                messageText,
                result.PendingEndpoint?.Intent,
                commandSuccess,
                result.ExecutionResult?.Success == true ? 0.9 : 0.5, // Estimated confidence
                responseTime,
                cancellationToken);
            
            // ✅ IMPROVED: Implicit learning feedback - successful execution = correct intent
            if (isCommand && commandSuccess && result.PendingEndpoint != null)
            {
                try
                {
                    // Record successful intent mapping for learning
                    await _genericConversationManager.RecordIntentFeedbackAsync(
                        messageText,
                        result.PendingEndpoint.Intent,
                        wasCorrect: true,
                        tenantId.Value,
                        cancellationToken);
                    
                    _logger.LogDebug("Recorded positive intent feedback for {Intent}", result.PendingEndpoint.Intent);
                }
                catch (Exception feedbackEx)
                {
                    _logger.LogWarning(feedbackEx, "Failed to record intent feedback");
                }
            }

            // 10. ✅ ADDED: Update command count and trigger periodic analysis
            if (isCommand)
            {
                userProfile.TotalCommandCount++;
                await _context.SaveChangesAsync(cancellationToken);

                // Analyze profile every 10 commands
                if (userProfile.TotalCommandCount % 10 == 0)
                {
                    _ = Task.Run(async () => await _userProfileService.AnalyzeAndUpdateProfileAsync(
                        userProfile.Id,
                        CancellationToken.None));
                }
            }

            _logger.LogInformation("Message processed successfully for {PhoneNumber}", phoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WhatsApp message for {PhoneNumber}", phoneNumber);
            
            // ✅ IMPROVED: Log error to UserBehavior for learning
            try
            {
                var errorUserProfile = await _userProfileService.GetOrCreateProfileAsync(
                    phoneNumber,
                    tenantId.Value,
                    cancellationToken: cancellationToken);
                
                await _userProfileService.LogBehaviorAsync(
                    errorUserProfile.Id,
                    "error_occurred",
                    new
                    {
                        errorType = ex.GetType().Name,
                        errorMessage = ex.Message,
                        stackTrace = ex.StackTrace != null ? ex.StackTrace.Substring(0, Math.Min(500, ex.StackTrace.Length)) : null
                    },
                    messageText,
                    commandType: null,
                    isSuccessful: false,
                    confidence: 0,
                    responseTimeMs: 0,
                    cancellationToken);
                
                _logger.LogInformation("Error logged to UserBehavior for learning");
            }
            catch (Exception logEx)
            {
                _logger.LogWarning(logEx, "Failed to log error to UserBehavior, continuing");
            }
            
            try
            {
                await _whatsAppService.SendTextMessageAsync(phoneNumber, 
                    "❌ Bir hata oluştu. Lütfen tekrar deneyin veya farklı bir şekilde ifade edin.");
            }
            catch (Exception sendEx)
            {
                _logger.LogError(sendEx, "Error sending error message");
            }
        }
    }

    private async Task<Conversation> GetOrCreateConversationAsync(
        string phoneNumber,
        Guid tenantId,
        Guid? companyId,
        CancellationToken cancellationToken)
    {
        var conversation = await _context.Conversations
            .FirstOrDefaultAsync(c => 
                c.PhoneNumber == phoneNumber && 
                c.TenantId == tenantId &&
                c.Status == ConversationStatus.Active,
                cancellationToken);

        if (conversation == null)
        {
            conversation = new Conversation
            {
                PhoneNumber = phoneNumber,
                TenantId = tenantId,
                CompanyId = companyId,
                Status = ConversationStatus.Active,
                CreatedAt = DateTime.UtcNow,
                LastMessageAt = DateTime.UtcNow
            };

            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync(cancellationToken);
        }
        else
        {
            conversation.LastMessageAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return conversation;
    }

    private async Task SaveMessageAsync(
        Guid conversationId,
        string whatsAppMessageId,
        MessageType messageType,
        MessageDirection direction,
        string content,
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        var message = new Message
        {
            ConversationId = conversationId,
            WhatsAppMessageId = whatsAppMessageId,
            Type = messageType,
            Direction = direction,
            Content = content,
            From = direction == MessageDirection.Incoming ? phoneNumber : 
                Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID") ?? "",
            To = direction == MessageDirection.Outgoing ? phoneNumber : 
                Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID") ?? "",
            Status = MessageStatus.Sent,
            Timestamp = DateTime.UtcNow
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Save pending media to dialogue state for later use
    /// </summary>
    private async Task SavePendingMediaAsync(
        Guid conversationId,
        MediaContext mediaContext,
        CancellationToken cancellationToken)
    {
        // Find or create a dialogue state for pending media
        var dialogueState = await _context.DialogueStates
            .FirstOrDefaultAsync(d => 
                d.ConversationId == conversationId && 
                d.IsActive &&
                d.ExpiresAt > DateTime.UtcNow,
                cancellationToken);

        if (dialogueState == null)
        {
            dialogueState = new DialogueState
            {
                ConversationId = conversationId,
                IsActive = true,
                ExpiresAt = DateTime.UtcNow.AddHours(1), // Media expires in 1 hour
                CreatedAt = DateTime.UtcNow
            };
            _context.DialogueStates.Add(dialogueState);
        }

        dialogueState.MediaContextJson = JsonSerializer.Serialize(mediaContext);
        dialogueState.UpdatedAt = DateTime.UtcNow;
        
        await _context.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("📷 Saved pending media {MediaId} for conversation {ConversationId}", 
            mediaContext.MediaId, conversationId);
    }

    /// <summary>
    /// Get pending media from dialogue state
    /// </summary>
    private async Task<MediaContext?> GetPendingMediaAsync(
        Guid conversationId,
        CancellationToken cancellationToken)
    {
        var dialogueState = await _context.DialogueStates
            .FirstOrDefaultAsync(d => 
                d.ConversationId == conversationId && 
                d.IsActive &&
                d.ExpiresAt > DateTime.UtcNow &&
                !string.IsNullOrEmpty(d.MediaContextJson),
                cancellationToken);

        if (dialogueState?.MediaContextJson == null)
            return null;

        try
        {
            var mediaContext = JsonSerializer.Deserialize<MediaContext>(dialogueState.MediaContextJson);
            
            // Clear the pending media after retrieval
            dialogueState.MediaContextJson = null;
            dialogueState.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            
            return mediaContext;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize pending media context");
            return null;
        }
    }
}
