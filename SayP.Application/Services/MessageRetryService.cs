using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SayP.Domain.Entities;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;

namespace SayP.Application.Services;

/// <summary>
/// Handles retry logic for failed messages
/// </summary>
public class MessageRetryService
{
    private readonly ISayPDbContext _context;
    private readonly IWhatsAppService _whatsAppService;
    private readonly ILogger<MessageRetryService> _logger;
    private const int MAX_RETRY_ATTEMPTS = 3;
    private static readonly TimeSpan[] RetryDelays = 
    {
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15)
    };

    public MessageRetryService(
        ISayPDbContext context,
        IWhatsAppService whatsAppService,
        ILogger<MessageRetryService> logger)
    {
        _context = context;
        _whatsAppService = whatsAppService;
        _logger = logger;
    }

    /// <summary>
    /// Retry failed outgoing messages
    /// </summary>
    public async Task RetryFailedMessagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var failedMessages = await _context.Messages
                .Where(m => m.Direction == MessageDirection.Outgoing 
                    && m.Status == MessageStatus.Failed
                    && m.RetryCount < MAX_RETRY_ATTEMPTS)
                .OrderBy(m => m.CreatedAt)
                .Take(10) // Process 10 at a time
                .ToListAsync(cancellationToken);

            if (!failedMessages.Any())
            {
                _logger.LogDebug("No failed messages to retry");
                return;
            }

            _logger.LogInformation("Retrying {Count} failed messages", failedMessages.Count);

            foreach (var message in failedMessages)
            {
                await RetryMessageAsync(message, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrying failed messages");
        }
    }

    /// <summary>
    /// Retry a specific message
    /// </summary>
    private async Task RetryMessageAsync(Message message, CancellationToken cancellationToken)
    {
        try
        {
            // Check if enough time has passed since last retry
            var retryDelay = RetryDelays[Math.Min(message.RetryCount, RetryDelays.Length - 1)];
            var nextRetryTime = (message.UpdatedAt ?? message.CreatedAt).Add(retryDelay);

            if (DateTime.UtcNow < nextRetryTime)
            {
                _logger.LogDebug(
                    "Message {MessageId} not ready for retry. Next retry at {NextRetry}",
                    message.Id, nextRetryTime);
                return;
            }

            _logger.LogInformation(
                "Retrying message {MessageId} (Attempt {Attempt}/{Max})",
                message.Id, message.RetryCount + 1, MAX_RETRY_ATTEMPTS);

            // Attempt to resend
            bool success = false;
            string? errorMessage = null;

            try
            {
                if (!string.IsNullOrEmpty(message.Content))
                {
                    await _whatsAppService.SendTextMessageAsync(message.To, message.Content);
                    success = true;
                }
                else if (!string.IsNullOrEmpty(message.MediaUrl))
                {
                    // Handle media retry if needed
                    _logger.LogWarning("Media message retry not yet implemented for message {MessageId}", message.Id);
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                _logger.LogError(ex, "Failed to retry message {MessageId}", message.Id);
            }

            // Update message status
            message.RetryCount++;
            message.UpdatedAt = DateTime.UtcNow;

            if (success)
            {
                message.Status = MessageStatus.Sent;
                message.ErrorMessage = null;
                _logger.LogInformation("Successfully retried message {MessageId}", message.Id);
            }
            else
            {
                message.ErrorMessage = errorMessage;
                
                if (message.RetryCount >= MAX_RETRY_ATTEMPTS)
                {
                    message.Status = MessageStatus.Failed;
                    _logger.LogWarning(
                        "Message {MessageId} exceeded max retry attempts ({Max})",
                        message.Id, MAX_RETRY_ATTEMPTS);
                }
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing retry for message {MessageId}", message.Id);
        }
    }

    /// <summary>
    /// Get retry statistics
    /// </summary>
    public async Task<RetryStatistics> GetRetryStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var failedCount = await _context.Messages
            .CountAsync(m => m.Status == MessageStatus.Failed, cancellationToken);

        var pendingRetryCount = await _context.Messages
            .CountAsync(m => m.Status == MessageStatus.Failed 
                && m.RetryCount < MAX_RETRY_ATTEMPTS, cancellationToken);

        var exceededRetriesCount = await _context.Messages
            .CountAsync(m => m.Status == MessageStatus.Failed 
                && m.RetryCount >= MAX_RETRY_ATTEMPTS, cancellationToken);

        return new RetryStatistics
        {
            TotalFailed = failedCount,
            PendingRetry = pendingRetryCount,
            ExceededRetries = exceededRetriesCount
        };
    }
}

public class RetryStatistics
{
    public int TotalFailed { get; set; }
    public int PendingRetry { get; set; }
    public int ExceededRetries { get; set; }
}
