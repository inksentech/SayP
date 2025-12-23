using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Entities;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;

namespace SayP.Application.Services;

/// <summary>
/// ⚠️ DEPRECATED: Manages WhatsApp conversations and 24-hour window tracking
/// Use GenericConversationManager instead for modern dynamic AI pipeline.
/// This will be removed in v3.0.0
/// </summary>
[Obsolete("Use GenericConversationManager for dynamic AI processing. This will be removed in v3.0.0")]
public class ConversationManager
{
    private readonly ISayPDbContext _context;
    private readonly IWhatsAppService _whatsAppService;
    private readonly AICommandRouter _aiRouter;
    private readonly ICommandExecutor _commandExecutor;
    private readonly IAIProvider _aiProvider;
    private readonly SlotFillingManager _slotFillingManager;
    private readonly IBackendUserService _backendUserService;
    private readonly ISmartConversationManager _smartConversationManager;
    private readonly ISmartIntentClassifier _smartIntentClassifier;
    private readonly SmartMessageProcessor _smartMessageProcessor;
    private readonly UserProfileService _userProfileService;
    private readonly ILogger<ConversationManager> _logger;

    public ConversationManager(
        ISayPDbContext context,
        IWhatsAppService whatsAppService,
        AICommandRouter aiRouter,
        ICommandExecutor commandExecutor,
        IAIProvider aiProvider,
        SlotFillingManager slotFillingManager,
        IBackendUserService backendUserService,
        ISmartConversationManager smartConversationManager,
        ISmartIntentClassifier smartIntentClassifier,
        SmartMessageProcessor smartMessageProcessor,
        UserProfileService userProfileService,
        ILogger<ConversationManager> logger)
    {
        _context = context;
        _whatsAppService = whatsAppService;
        _aiRouter = aiRouter;
        _commandExecutor = commandExecutor;
        _aiProvider = aiProvider;
        _slotFillingManager = slotFillingManager;
        _backendUserService = backendUserService;
        _smartConversationManager = smartConversationManager;
        _smartIntentClassifier = smartIntentClassifier;
        _smartMessageProcessor = smartMessageProcessor;
        _userProfileService = userProfileService;
        _logger = logger;
    }

    /// <summary>
    /// Process incoming WhatsApp message (text, image, or audio)
    /// </summary>
    public async Task ProcessIncomingMessageAsync(
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
        try
        {
            _logger.LogInformation("Processing message from {PhoneNumber}: {Text}", phoneNumber, messageText);

            // Validate phone number with backend and get tenant information
            var userAuthInfo = await _backendUserService.ValidatePhoneNumberAsync(phoneNumber);
            
            if (userAuthInfo == null || !userAuthInfo.IsAuthorized)
            {
                _logger.LogWarning("Unauthorized phone number attempted to send message: {PhoneNumber}", phoneNumber);
                await _whatsAppService.SendTextMessageAsync(phoneNumber, 
                    "Bu numara sistemde kayıtlı değil. Lütfen yöneticinizle iletişime geçin.");
                return;
            }

            var tenantId = userAuthInfo.TenantId;
            var companyId = userAuthInfo.DefaultCompanyId;
            var userCompanyId = userAuthInfo.DefaultUserCompanyId;
            
            _logger.LogInformation("Authorized user {UserId} from tenant {TenantId}, UserCompanyId: {UserCompanyId} sent message", 
                userAuthInfo.UserId, tenantId, userCompanyId);

            // Ensure tenant mapping exists in SayP database
            await EnsureTenantMappingAsync(phoneNumber, tenantId, companyId, cancellationToken);

            // Get or create conversation
            var conversation = await GetOrCreateConversationAsync(phoneNumber, tenantId, companyId, cancellationToken);

            // Save incoming message
            await AddMessageAsync(
                conversation.Id,
                messageId,
                messageType,
                MessageDirection.Incoming,
                messageText ?? caption ?? "",
                phoneNumber,
                Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID") ?? "",
                cancellationToken: cancellationToken
            );

            // Update conversation window
            await UpdateConversationWindowAsync(conversation.Id, cancellationToken);

            // Process based on message type
            AICommandResult commandResult;

            if (messageType == MessageType.Image && !string.IsNullOrEmpty(mediaId))
            {
                // Download and process image
                _logger.LogInformation("Processing image message");
                var mediaResult = await _whatsAppService.DownloadMediaAsync(mediaId, cancellationToken);
                
                if (!mediaResult.Success || mediaResult.Data == null)
                {
                    await _whatsAppService.SendTextMessageAsync(phoneNumber, "Görsel indirilemedi. Lütfen tekrar deneyin.");
                    return;
                }

                commandResult = await _aiProvider.ExtractCommandFromImageAsync(
                    mediaResult.Data,
                    mediaResult.MimeType ?? "image/jpeg",
                    caption,
                    cancellationToken);
            }
            else if (messageType == MessageType.Audio && !string.IsNullOrEmpty(mediaId))
            {
                // Download and process audio
                _logger.LogInformation("Processing audio message");
                var mediaResult = await _whatsAppService.DownloadMediaAsync(mediaId, cancellationToken);
                
                if (!mediaResult.Success || mediaResult.Data == null)
                {
                    await _whatsAppService.SendTextMessageAsync(phoneNumber, "Ses kaydı indirilemedi. Lütfen tekrar deneyin.");
                    return;
                }

                commandResult = await _aiProvider.ExtractCommandFromAudioAsync(
                    mediaResult.Data,
                    mediaResult.MimeType ?? "audio/ogg",
                    null,
                    cancellationToken);
            }
            else if (!string.IsNullOrEmpty(messageText))
            {
                // ✨ USE SMART MESSAGE PROCESSOR - Full AI Pipeline
                _logger.LogInformation("Using SmartMessageProcessor for intelligent processing");
                var smartResult = await _smartMessageProcessor.ProcessMessageAsync(messageText, conversation, cancellationToken);
                
                if (!smartResult.Success)
                {
                    await _whatsAppService.SendTextMessageAsync(phoneNumber, 
                        smartResult.ErrorMessage ?? "Bir hata oluştu. Lütfen tekrar deneyin.");
                    return;
                }

                // If not a command, send natural response
                if (!smartResult.IsCommand)
                {
                    await _whatsAppService.SendTextMessageAsync(phoneNumber, 
                        smartResult.NaturalResponse ?? "Merhaba! Size nasıl yardımcı olabilirim?");
                    
                    // Save outgoing message
                    await AddMessageAsync(
                        conversation.Id,
                        Guid.NewGuid().ToString(),
                        MessageType.Text,
                        MessageDirection.Outgoing,
                        smartResult.NaturalResponse ?? "",
                        Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID") ?? "",
                        phoneNumber,
                        cancellationToken: cancellationToken
                    );
                    return;
                }

                // Command detected - proceed with execution
                var commandType = smartResult.CommandType;
                var commandParameters = smartResult.CommandParameters;
                bool requiresConfirmation = smartResult.RequiresConfirmation;
                string? confirmationMessage = smartResult.ConfirmationMessage;
                
                // Update command JSON with filled slots
                var updatedCommandJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
                {
                    commandType = commandType.ToString(),
                    command = commandParameters,
                    confidence = smartResult.Confidence
                });
                
                // Save command
                var command = new Command
                {
                    ConversationId = conversation.Id,
                    Type = commandType,
                    Status = requiresConfirmation ? CommandStatus.Pending : CommandStatus.Executing,
                    Parameters = updatedCommandJson,
                    Confidence = smartResult.Confidence,
                    RequiresConfirmation = requiresConfirmation,
                    IsConfirmed = !requiresConfirmation
                };

                _context.Commands.Add(command);
                await _context.SaveChangesAsync(cancellationToken);

                // If requires confirmation, send confirmation buttons
                if (requiresConfirmation)
                {
                    await SendConfirmationRequestAsync(
                        phoneNumber,
                        command.Id,
                        confirmationMessage ?? "Bu işlemi onaylıyor musunuz?",
                        cancellationToken
                    );
                    return;
                }

                // Execute command immediately if no confirmation needed
                command.Status = CommandStatus.Executing;
                await _context.SaveChangesAsync(cancellationToken);

                var executionResult = await _commandExecutor.ExecuteAsync(
                    commandType,
                    updatedCommandJson,
                    tenantId,
                    companyId,
                    userCompanyId
                );

                // Update command status
                command.Status = executionResult.Success ? CommandStatus.Completed : CommandStatus.Failed;
                command.Result = executionResult.Result;
                command.ResultJson = executionResult.ResultJson;
                command.ErrorMessage = executionResult.ErrorMessage;
                await _context.SaveChangesAsync(cancellationToken);

                // Log command execution behavior
                if (smartResult.UserProfileId.HasValue)
                {
                    await _userProfileService.LogBehaviorAsync(
                        smartResult.UserProfileId.Value,
                        "command_executed",
                        new
                        {
                            commandType = commandType.ToString(),
                            success = executionResult.Success,
                            result = executionResult.Result,
                            parameters = commandParameters
                        },
                        messageText,
                        commandType.ToString(),
                        executionResult.Success,
                        smartResult.Confidence,
                        0,
                        cancellationToken);

                    // Periodically analyze and update profile (every 10 commands)
                    var profile = await _context.UserProfiles.FindAsync(new object[] { smartResult.UserProfileId.Value }, cancellationToken);
                    if (profile != null && profile.TotalCommandCount % 10 == 0)
                    {
                        _ = Task.Run(async () => await _userProfileService.AnalyzeAndUpdateProfileAsync(
                            smartResult.UserProfileId.Value, 
                            CancellationToken.None));
                    }
                }

                // Set user message for language detection
                ResponseFormatter.SetUserMessage(messageText ?? "");
                
                // Send response with detailed information
                var responseMessage = executionResult.Success
                    ? ResponseFormatter.FormatSuccessResponse(commandType, executionResult.ResultJson)
                    : ResponseFormatter.FormatErrorResponse(executionResult.ErrorMessage);

                await _whatsAppService.SendTextMessageAsync(phoneNumber, responseMessage);

                // Save outgoing message
                await AddMessageAsync(
                    conversation.Id,
                    Guid.NewGuid().ToString(),
                    MessageType.Text,
                    MessageDirection.Outgoing,
                    responseMessage,
                    Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID") ?? "",
                    phoneNumber,
                    cancellationToken: cancellationToken
                );

                _logger.LogInformation("Successfully processed smart message from {PhoneNumber}", phoneNumber);
                return;
            }
            else
            {
                await _whatsAppService.SendTextMessageAsync(phoneNumber, "Üzgünüm, bu mesaj türünü işleyemedim.");
                return;
            }

            // Media processing continues with old flow for now
            // Check if command extraction was successful
            if (!commandResult.Success)
            {
                await _whatsAppService.SendTextMessageAsync(phoneNumber, 
                    commandResult.ErrorMessage ?? "Bir hata oluştu. Lütfen tekrar deneyin.");
                return;
            }

            // Parse command JSON
            dynamic? parsedCommand = null;
            string commandTypeStr = "None";
            
            try
            {
                parsedCommand = Newtonsoft.Json.JsonConvert.DeserializeObject(commandResult.CommandJson ?? "{}")!;
                commandTypeStr = parsedCommand.commandType?.ToString() ?? "None";
            }
            catch (Newtonsoft.Json.JsonException)
            {
                _logger.LogInformation("AI returned non-JSON response");
                await _whatsAppService.SendTextMessageAsync(phoneNumber, commandResult.CommandJson ?? "Merhaba!");
                return;
            }

            if (commandTypeStr == "None")
            {
                string response = parsedCommand.response?.ToString() ?? "Merhaba!";
                await _whatsAppService.SendTextMessageAsync(phoneNumber, response);
                return;
            }

            if (!Enum.TryParse<CommandType>(commandTypeStr, out var mediaCommandType))
            {
                await _whatsAppService.SendTextMessageAsync(phoneNumber, "Komutu anlayamadım.");
                return;
            }

            var mediaExtractedEntities = new Dictionary<string, object>();
            if (parsedCommand.command != null)
            {
                foreach (var prop in parsedCommand.command)
                {
                    mediaExtractedEntities[prop.Name] = prop.Value;
                }
            }

            var mediaSlotResult = _slotFillingManager.FillSlots(mediaCommandType, mediaExtractedEntities);
            
            if (!mediaSlotResult.IsComplete)
            {
                await _whatsAppService.SendTextMessageAsync(phoneNumber, mediaSlotResult.NextQuestion ?? "Eksik bilgi var.");
                return;
            }

            var mediaCommandJson = Newtonsoft.Json.JsonConvert.SerializeObject(new
            {
                commandType = mediaCommandType.ToString(),
                command = mediaSlotResult.FilledSlots,
                confidence = mediaSlotResult.Confidence
            });

            bool mediaRequiresConfirmation = parsedCommand.requiresConfirmation ?? false;
            string? mediaConfirmationMessage = parsedCommand.confirmationMessage?.ToString();

            var mediaCommand = new Command
            {
                ConversationId = conversation.Id,
                Type = mediaCommandType,
                Status = mediaRequiresConfirmation ? CommandStatus.Pending : CommandStatus.Executing,
                Parameters = mediaCommandJson,
                Confidence = mediaSlotResult.Confidence,
                RequiresConfirmation = mediaRequiresConfirmation,
                IsConfirmed = !mediaRequiresConfirmation
            };

            _context.Commands.Add(mediaCommand);
            await _context.SaveChangesAsync(cancellationToken);

            if (mediaRequiresConfirmation)
            {
                await SendConfirmationRequestAsync(
                    phoneNumber,
                    mediaCommand.Id,
                    mediaConfirmationMessage ?? "Bu işlemi onaylıyor musunuz?",
                    cancellationToken
                );
                return;
            }

            mediaCommand.Status = CommandStatus.Executing;
            await _context.SaveChangesAsync(cancellationToken);

            var mediaExecutionResult = await _commandExecutor.ExecuteAsync(
                mediaCommandType,
                commandResult.CommandJson ?? "{}",
                tenantId,
                companyId,
                userCompanyId
            );

            // Update command status
            mediaCommand.Status = mediaExecutionResult.Success ? CommandStatus.Completed : CommandStatus.Failed;
            mediaCommand.Result = mediaExecutionResult.Result;
            mediaCommand.ResultJson = mediaExecutionResult.ResultJson;
            mediaCommand.ErrorMessage = mediaExecutionResult.ErrorMessage;
            await _context.SaveChangesAsync(cancellationToken);

            // Send response with detailed information
            var mediaResponseMessage = mediaExecutionResult.Success
                ? ResponseFormatter.FormatSuccessResponse(mediaCommandType, mediaExecutionResult.ResultJson)
                : ResponseFormatter.FormatErrorResponse(mediaExecutionResult.ErrorMessage);

            await _whatsAppService.SendTextMessageAsync(phoneNumber, mediaResponseMessage);

            // Save outgoing message
            await AddMessageAsync(
                conversation.Id,
                Guid.NewGuid().ToString(),
                MessageType.Text,
                MessageDirection.Outgoing,
                mediaResponseMessage,
                Environment.GetEnvironmentVariable("WHATSAPP_PHONE_NUMBER_ID") ?? "",
                phoneNumber,
                cancellationToken: cancellationToken
            );

            _logger.LogInformation("Successfully processed media message from {PhoneNumber}", phoneNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {PhoneNumber}", phoneNumber);
            
            // Send error message to user
            try
            {
                await _whatsAppService.SendTextMessageAsync(
                    phoneNumber,
                    "Bir hata oluştu. Lütfen daha sonra tekrar deneyin."
                );
            }
            catch (Exception sendEx)
            {
                _logger.LogError(sendEx, "Error sending error message to {PhoneNumber}", phoneNumber);
            }
        }
    }

    /// <summary>
    /// Get or create a conversation for a phone number
    /// </summary>
    public async Task<Conversation> GetOrCreateConversationAsync(
        string phoneNumber,
        Guid tenantId,
        Guid? companyId = null,
        CancellationToken cancellationToken = default)
    {
        // Try to find active conversation
        var conversation = await _context.Conversations
            .Include(c => c.Messages.OrderByDescending(m => m.CreatedAt).Take(10))
            .Include(c => c.Commands.OrderByDescending(c => c.CreatedAt).Take(5))
            .FirstOrDefaultAsync(c => 
                c.PhoneNumber == phoneNumber && 
                c.TenantId == tenantId &&
                c.Status == ConversationStatus.Active,
                cancellationToken);

        if (conversation != null)
        {
            // Check if 24h window expired
            if (!conversation.IsWithinWindow)
            {
                _logger.LogInformation("Conversation {ConversationId} window expired, creating new", conversation.Id);
                
                // Archive old conversation
                conversation.Status = ConversationStatus.Archived;
                await _context.SaveChangesAsync(cancellationToken);

                // Create new conversation
                conversation = null;
            }
        }

        // Create new conversation if needed
        if (conversation == null)
        {
            _logger.LogInformation("Creating new conversation for {PhoneNumber}", phoneNumber);
            
            conversation = new Conversation
            {
                PhoneNumber = phoneNumber,
                TenantId = tenantId,
                CompanyId = companyId,
                Status = ConversationStatus.Active,
                LastMessageAt = DateTime.UtcNow,
                WindowExpiresAt = DateTime.UtcNow.AddHours(24)
            };

            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return conversation;
    }

    /// <summary>
    /// Update conversation window (when user sends a message)
    /// </summary>
    public async Task UpdateConversationWindowAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        var conversation = await _context.Conversations.FindAsync(new object[] { conversationId }, cancellationToken);
        if (conversation == null)
        {
            _logger.LogWarning("Conversation {ConversationId} not found", conversationId);
            return;
        }

        conversation.LastMessageAt = DateTime.UtcNow;
        conversation.WindowExpiresAt = DateTime.UtcNow.AddHours(24);
        conversation.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation("Updated conversation {ConversationId} window until {ExpiresAt}", 
            conversationId, conversation.WindowExpiresAt);
    }

    /// <summary>
    /// Add message to conversation
    /// </summary>
    public async Task<Message> AddMessageAsync(
        Guid conversationId,
        string whatsAppMessageId,
        MessageType type,
        MessageDirection direction,
        string content,
        string from,
        string to,
        string? rawPayload = null,
        CancellationToken cancellationToken = default)
    {
        // Check for duplicate (idempotency)
        var existing = await _context.Messages
            .FirstOrDefaultAsync(m => m.WhatsAppMessageId == whatsAppMessageId, cancellationToken);

        if (existing != null)
        {
            _logger.LogInformation("Message {MessageId} already exists (idempotent)", whatsAppMessageId);
            return existing;
        }

        var message = new Message
        {
            ConversationId = conversationId,
            WhatsAppMessageId = whatsAppMessageId,
            Type = type,
            Direction = direction,
            Content = content,
            From = from,
            To = to,
            RawPayload = rawPayload,
            Timestamp = DateTime.UtcNow
        };

        _context.Messages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Added message {MessageId} to conversation {ConversationId}", 
            message.Id, conversationId);

        return message;
    }

    /// <summary>
    /// Get conversation context for AI (last N messages)
    /// </summary>
    public async Task<string> GetConversationContextAsync(
        Guid conversationId,
        int messageCount = 10,
        CancellationToken cancellationToken = default)
    {
        var messages = await _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(messageCount)
            .OrderBy(m => m.CreatedAt)
            .Select(m => new { m.Direction, m.Content, m.CreatedAt })
            .ToListAsync(cancellationToken);

        if (!messages.Any())
            return string.Empty;

        var context = string.Join("\n", messages.Select(m => 
            $"[{m.CreatedAt:HH:mm}] {(m.Direction == MessageDirection.Incoming ? "User" : "Assistant")}: {m.Content}"));

        return context;
    }

    /// <summary>
    /// Send confirmation request with interactive buttons
    /// </summary>
    private async Task SendConfirmationRequestAsync(
        string phoneNumber,
        Guid commandId,
        string confirmationMessage,
        CancellationToken cancellationToken = default)
    {
        var buttons = new List<WhatsAppButton>
        {
            new WhatsAppButton
            {
                Id = $"confirm_{commandId}",
                Title = "✅ Onayla"
            },
            new WhatsAppButton
            {
                Id = $"reject_{commandId}",
                Title = "❌ İptal"
            }
        };

        await _whatsAppService.SendInteractiveButtonsAsync(
            phoneNumber,
            confirmationMessage,
            buttons,
            cancellationToken
        );

        _logger.LogInformation("Sent confirmation request for command {CommandId} to {PhoneNumber}", 
            commandId, phoneNumber);
    }

    /// <summary>
    /// Handle button reply (confirmation/rejection)
    /// </summary>
    public async Task HandleButtonReplyAsync(
        string phoneNumber,
        string buttonId,
        string messageId,
        DateTime timestamp,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Handling button reply from {PhoneNumber}: {ButtonId}", phoneNumber, buttonId);

            // Parse button ID (format: "confirm_{commandId}" or "reject_{commandId}")
            var parts = buttonId.Split('_');
            if (parts.Length != 2 || !Guid.TryParse(parts[1], out var commandId))
            {
                _logger.LogWarning("Invalid button ID format: {ButtonId}", buttonId);
                await _whatsAppService.SendTextMessageAsync(phoneNumber, "Geçersiz işlem.");
                return;
            }

            var action = parts[0]; // "confirm" or "reject"

            // Get command
            var command = await _context.Commands
                .Include(c => c.Conversation)
                .FirstOrDefaultAsync(c => c.Id == commandId, cancellationToken);

            if (command == null)
            {
                _logger.LogWarning("Command {CommandId} not found", commandId);
                await _whatsAppService.SendTextMessageAsync(phoneNumber, "İşlem bulunamadı.");
                return;
            }

            // Check if already processed
            if (command.IsConfirmed || command.Status != CommandStatus.Pending)
            {
                _logger.LogWarning("Command {CommandId} already processed", commandId);
                await _whatsAppService.SendTextMessageAsync(phoneNumber, "Bu işlem zaten işlendi.");
                return;
            }

            // Check timeout (5 minutes)
            if ((DateTime.UtcNow - command.CreatedAt).TotalMinutes > 5)
            {
                command.Status = CommandStatus.Failed;
                command.ErrorMessage = "Onay süresi doldu";
                await _context.SaveChangesAsync(cancellationToken);

                await _whatsAppService.SendTextMessageAsync(phoneNumber, 
                    "⏱️ Onay süresi doldu. Lütfen komutu tekrar gönderin.");
                return;
            }

            if (action == "confirm")
            {
                // Confirm and execute
                command.IsConfirmed = true;
                command.Status = CommandStatus.Executing;
                await _context.SaveChangesAsync(cancellationToken);

                await _whatsAppService.SendTextMessageAsync(phoneNumber, "✅ İşlem onaylandı, işleniyor...");

                // Execute command
                // TODO: Get userCompanyId from command context
                var executionResult = await _commandExecutor.ExecuteAsync(
                    command.Type,
                    command.Parameters,
                    command.Conversation?.TenantId ?? Guid.Empty,
                    command.Conversation?.CompanyId,
                    null, // userCompanyId - not available in confirmation context
                    cancellationToken
                );

                // Update command status
                command.Status = executionResult.Success ? CommandStatus.Completed : CommandStatus.Failed;
                command.Result = executionResult.Result;
                command.ResultJson = executionResult.ResultJson;
                command.ErrorMessage = executionResult.ErrorMessage;
                command.ExecutedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);

                // Send result
                var responseMessage = executionResult.Success
                    ? ResponseFormatter.FormatSuccessResponse(command.Type, executionResult.ResultJson)
                    : ResponseFormatter.FormatErrorResponse(executionResult.ErrorMessage);

                await _whatsAppService.SendTextMessageAsync(phoneNumber, responseMessage);

                _logger.LogInformation("Command {CommandId} confirmed and executed successfully", commandId);
            }
            else if (action == "reject")
            {
                // Reject
                command.Status = CommandStatus.Failed;
                command.ErrorMessage = "Kullanıcı tarafından iptal edildi";
                await _context.SaveChangesAsync(cancellationToken);

                await _whatsAppService.SendTextMessageAsync(phoneNumber, "❌ İşlem iptal edildi.");

                _logger.LogInformation("Command {CommandId} rejected by user", commandId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling button reply from {PhoneNumber}", phoneNumber);
            await _whatsAppService.SendTextMessageAsync(phoneNumber, "Bir hata oluştu. Lütfen tekrar deneyin.");
        }
    }

    /// <summary>
    /// Build conversation context string from recent messages
    /// </summary>
    private async Task<string> BuildConversationContextAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
    {
        // Get last 5 messages
        var recentMessages = await _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.CreatedAt)
            .Take(5)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);

        if (!recentMessages.Any())
            return string.Empty;

        var contextBuilder = new System.Text.StringBuilder();
        
        foreach (var msg in recentMessages)
        {
            var role = msg.Direction == MessageDirection.Incoming ? "Kullanıcı" : "Asistan";
            var content = msg.Content ?? "(medya)";
            contextBuilder.AppendLine($"{role}: {content}");
        }

        return contextBuilder.ToString();
    }

    // Mobile API Methods
    public async Task<List<Conversation>> GetAllConversationsAsync()
    {
        // Don't include Messages to avoid circular reference
        return await _context.Conversations
            .AsNoTracking()
            .OrderByDescending(c => c.UpdatedAt)
            .Select(c => new Conversation
            {
                Id = c.Id,
                PhoneNumber = c.PhoneNumber,
                TenantId = c.TenantId,
                CompanyId = c.CompanyId,
                Status = c.Status,
                LastMessageAt = c.LastMessageAt,
                WindowExpiresAt = c.WindowExpiresAt,
                Context = c.Context,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt
                // Messages = null (don't include to avoid circular reference)
            })
            .ToListAsync();
    }

    public async Task<Conversation?> GetConversationAsync(string id)
    {
        if (!Guid.TryParse(id, out var conversationId))
            return null;

        return await _context.Conversations
            .FirstOrDefaultAsync(c => c.Id == conversationId);
    }

    public async Task<List<Message>> GetMessagesAsync(string conversationId)
    {
        if (!Guid.TryParse(conversationId, out var convId))
            return new List<Message>();

        // Don't include Conversation to avoid circular reference
        return await _context.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == convId)
            .OrderBy(m => m.Timestamp)
            .Select(m => new Message
            {
                Id = m.Id,
                ConversationId = m.ConversationId,
                WhatsAppMessageId = m.WhatsAppMessageId,
                Type = m.Type,
                Direction = m.Direction,
                Content = m.Content,
                From = m.From,
                To = m.To,
                Timestamp = m.Timestamp,
                Status = m.Status,
                ErrorMessage = m.ErrorMessage,
                MediaUrl = m.MediaUrl,
                RetryCount = m.RetryCount,
                RawPayload = m.RawPayload,
                CreatedAt = m.CreatedAt,
                UpdatedAt = m.UpdatedAt
                // Conversation = null (don't include to avoid circular reference)
            })
            .ToListAsync();
    }

    public async Task<Message> SendMessageAsync(string conversationId, string content, string type, Guid? tenantId = null)
    {
        if (!Guid.TryParse(conversationId, out var convId))
            throw new ArgumentException("Invalid conversation ID");

        var conversation = await _context.Conversations.FindAsync(convId);
        if (conversation == null)
            throw new ArgumentException("Conversation not found");

        // Validate tenant access if provided
        if (tenantId.HasValue)
        {
            // Validate that the phone number belongs to the specified tenant
            var userAuthInfo = await _backendUserService.ValidatePhoneNumberAsync(conversation.PhoneNumber);
            
            if (userAuthInfo == null || !userAuthInfo.IsAuthorized || userAuthInfo.TenantId != tenantId.Value)
            {
                _logger.LogWarning("Unauthorized tenant access attempt for conversation {ConversationId}. Phone: {PhoneNumber}, Requested Tenant: {RequestedTenant}, Actual Tenant: {ActualTenant}", 
                    conversationId, conversation.PhoneNumber, tenantId.Value, userAuthInfo?.TenantId);
                throw new UnauthorizedAccessException("Access denied: Invalid tenant for this conversation");
            }

            // Update conversation tenant and company if needed
            if (conversation.TenantId != userAuthInfo.TenantId)
            {
                conversation.TenantId = userAuthInfo.TenantId;
                conversation.CompanyId = userAuthInfo.DefaultCompanyId;
                _logger.LogInformation("Updated conversation {ConversationId} tenant to {TenantId}", convId, userAuthInfo.TenantId);
            }
        }

        // Create user message
        var userMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = convId,
            WhatsAppMessageId = Guid.NewGuid().ToString(),
            Type = MessageType.Text,
            Direction = MessageDirection.Outgoing,
            Content = content,
            From = "mobile-user",
            To = "sayp-ai",
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Sent
        };

        _context.Messages.Add(userMessage);
        await _context.SaveChangesAsync();

        // Build conversation context for AI
        var contextString = await BuildConversationContextAsync(convId, CancellationToken.None);

        // Generate AI response with context
        var aiResponse = await _aiProvider.ExtractCommandAsync(content, contextString, CancellationToken.None);
        
        // Build AI response message
        string aiContent;
        
        // If command doesn't require confirmation, execute it immediately
        if (aiResponse.Success && !string.IsNullOrEmpty(aiResponse.CommandType) && 
            aiResponse.CommandType != "None" && !aiResponse.RequiresConfirmation)
        {
            try
            {
                var commandType = Enum.Parse<Domain.Enums.CommandType>(aiResponse.CommandType);
                // TODO: Get userCompanyId from conversation context
                var executionResult = await _commandExecutor.ExecuteAsync(
                    commandType,
                    aiResponse.CommandJson ?? "{}",
                    conversation.TenantId,
                    conversation.CompanyId,
                    null, // userCompanyId - not available in legacy AI response context
                    CancellationToken.None
                );

                if (executionResult.Success)
                {
                    aiContent = executionResult.Message ?? aiResponse.ConfirmationMessage ?? "İşlem başarıyla tamamlandı.";
                }
                else
                {
                    aiContent = executionResult.Message ?? "İşlem başarısız oldu.";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing command for mobile");
                aiContent = "Komut çalıştırılırken bir hata oluştu.";
            }
        }
        else if (aiResponse.Success && !string.IsNullOrEmpty(aiResponse.ConfirmationMessage))
        {
            aiContent = aiResponse.ConfirmationMessage;
        }
        else if (!string.IsNullOrEmpty(aiResponse.ErrorMessage))
        {
            aiContent = aiResponse.ErrorMessage;
        }
        else
        {
            // Log for debugging
            _logger.LogWarning("AI response has no confirmation or error message. Success: {Success}, CommandType: {CommandType}", 
                aiResponse.Success, aiResponse.CommandType);
            aiContent = "Üzgünüm, yanıt oluşturamadım. Lütfen tekrar deneyin.";
        }
        
        var aiMessage = new Message
        {
            Id = Guid.NewGuid(),
            ConversationId = convId,
            WhatsAppMessageId = Guid.NewGuid().ToString(),
            Type = MessageType.Text,
            Direction = MessageDirection.Incoming,
            Content = aiContent,
            From = "sayp-ai",
            To = "mobile-user",
            Timestamp = DateTime.UtcNow,
            Status = MessageStatus.Delivered
        };

        _context.Messages.Add(aiMessage);
        conversation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // Return message without Conversation to avoid circular reference
        aiMessage.Conversation = null;
        return aiMessage;
    }

    public async Task<Conversation> CreateConversationAsync(string phoneNumber, string title, Guid? tenantId = null)
    {
        // Use provided tenant ID or default for mobile app
        var effectiveTenantId = tenantId ?? Guid.Parse("00000000-0000-0000-0000-000000000001");
        
        var conversation = new Conversation
        {
            Id = Guid.NewGuid(),
            PhoneNumber = phoneNumber,
            TenantId = effectiveTenantId,
            Status = ConversationStatus.Active,
            LastMessageAt = DateTime.UtcNow,
            WindowExpiresAt = DateTime.UtcNow.AddHours(24),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.Conversations.Add(conversation);
        await _context.SaveChangesAsync();

        return conversation;
    }

    /// <summary>
    /// Ensure tenant mapping exists in SayP database for the phone number
    /// </summary>
    private async Task EnsureTenantMappingAsync(
        string phoneNumber, 
        Guid tenantId, 
        Guid? companyId, 
        CancellationToken cancellationToken = default)
    {
        var existingMapping = await _context.TenantMappings
            .FirstOrDefaultAsync(tm => tm.PhoneNumber == phoneNumber, cancellationToken);

        if (existingMapping == null)
        {
            _logger.LogInformation("Creating new tenant mapping for {PhoneNumber} -> Tenant {TenantId}", 
                phoneNumber, tenantId);

            var newMapping = new TenantMapping
            {
                PhoneNumber = phoneNumber,
                TenantId = tenantId,
                CompanyId = companyId,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.TenantMappings.Add(newMapping);
            await _context.SaveChangesAsync(cancellationToken);
        }
        else if (existingMapping.TenantId != tenantId || existingMapping.CompanyId != companyId)
        {
            _logger.LogInformation("Updating tenant mapping for {PhoneNumber}: Tenant {OldTenantId} -> {NewTenantId}, Company {OldCompanyId} -> {NewCompanyId}", 
                phoneNumber, existingMapping.TenantId, tenantId, existingMapping.CompanyId, companyId);

            existingMapping.TenantId = tenantId;
            existingMapping.CompanyId = companyId;
            existingMapping.UpdatedAt = DateTime.UtcNow;
            existingMapping.IsActive = true;

            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteConversationAsync(string id)
    {
        if (!Guid.TryParse(id, out var conversationId))
            return;

        var conversation = await _context.Conversations.FindAsync(conversationId);
        if (conversation != null)
        {
            _context.Conversations.Remove(conversation);
            await _context.SaveChangesAsync();
        }
    }
}
