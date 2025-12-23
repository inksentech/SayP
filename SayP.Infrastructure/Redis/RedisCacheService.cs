using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using SayP.Domain.Interfaces;

namespace SayP.Infrastructure.Redis;

public class RedisCacheService : ICacheService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisCacheService> _logger;

    public RedisCacheService(
        string connectionString,
        ILogger<RedisCacheService> logger)
    {
        _logger = logger;
        try
        {
            _redis = ConnectionMultiplexer.Connect(connectionString + ",abortConnect=false");
            _database = _redis.GetDatabase();
            _logger.LogInformation("Redis connected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Redis connection failed. Running without cache.");
            _redis = null!;
            _database = null!;
        }
    }

    /// <summary>
    /// Check if message was already processed (idempotency)
    /// </summary>
    public async Task<bool> IsMessageProcessedAsync(string messageId)
    {
        if (_database == null) return false;
        
        try
        {
            var key = $"msg:processed:{messageId}";
            return await _database.KeyExistsAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if message {MessageId} was processed", messageId);
            return false;
        }
    }

    /// <summary>
    /// Mark message as processed (with 24h expiry)
    /// </summary>
    public async Task MarkMessageAsProcessedAsync(string messageId)
    {
        if (_database == null) return;
        
        try
        {
            var key = $"msg:processed:{messageId}";
            await _database.StringSetAsync(key, DateTime.UtcNow.ToString(), TimeSpan.FromHours(24));
            _logger.LogInformation("Marked message {MessageId} as processed", messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking message {MessageId} as processed", messageId);
        }
    }

    /// <summary>
    /// Check rate limit for a phone number
    /// </summary>
    public async Task<bool> IsRateLimitExceededAsync(string phoneNumber, int maxRequests, TimeSpan window)
    {
        if (_database == null) return false;
        
        try
        {
            var key = $"ratelimit:{phoneNumber}";
            var count = await _database.StringIncrementAsync(key);

            if (count == 1)
            {
                await _database.KeyExpireAsync(key, window);
            }

            return count > maxRequests;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking rate limit for {PhoneNumber}", phoneNumber);
            return false; // Allow on error
        }
    }

    /// <summary>
    /// Cache conversation context
    /// </summary>
    public async Task SetConversationContextAsync(Guid conversationId, string context, TimeSpan? expiry = null)
    {
        try
        {
            var key = $"conversation:context:{conversationId}";
            await _database.StringSetAsync(key, context, expiry ?? TimeSpan.FromHours(24));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching conversation context for {ConversationId}", conversationId);
        }
    }

    /// <summary>
    /// Get cached conversation context
    /// </summary>
    public async Task<string?> GetConversationContextAsync(Guid conversationId)
    {
        try
        {
            var key = $"conversation:context:{conversationId}";
            var value = await _database.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversation context for {ConversationId}", conversationId);
            return null;
        }
    }

    /// <summary>
    /// Generic cache set
    /// </summary>
    public async Task SetAsync(string key, string value, TimeSpan? expiry = null)
    {
        try
        {
            await _database.StringSetAsync(key, value, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache key {Key}", key);
        }
    }

    /// <summary>
    /// Generic cache set with object serialization
    /// </summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null)
    {
        if (_database == null) return;
        
        try
        {
            var json = JsonSerializer.Serialize(value);
            await _database.StringSetAsync(key, json, expiry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting cache key {Key}", key);
        }
    }

    /// <summary>
    /// Generic cache get
    /// </summary>
    public async Task<string?> GetAsync(string key)
    {
        try
        {
            var value = await _database.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache key {Key}", key);
            return null;
        }
    }

    /// <summary>
    /// Generic cache get with object deserialization
    /// </summary>
    public async Task<T?> GetAsync<T>(string key)
    {
        if (_database == null) return default;
        
        try
        {
            var value = await _database.StringGetAsync(key);
            if (!value.HasValue)
                return default;

            return JsonSerializer.Deserialize<T>(value.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cache key {Key}", key);
            return default;
        }
    }

    /// <summary>
    /// Delete cache key
    /// </summary>
    public async Task DeleteAsync(string key)
    {
        try
        {
            await _database.KeyDeleteAsync(key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting cache key {Key}", key);
        }
    }

    /// <summary>
    /// Health check for Redis connection
    /// </summary>
    public async Task<bool> HealthCheckAsync()
    {
        if (_database == null || _redis == null)
            return false;

        try
        {
            await _database.PingAsync();
            return _redis.IsConnected;
        }
        catch
        {
            return false;
        }
    }
}
