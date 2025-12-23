namespace SayP.Domain.Interfaces;

/// <summary>
/// Generic cache service interface for Clean Architecture compliance
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Get cached value by key (string)
    /// </summary>
    Task<string?> GetAsync(string key);
    
    /// <summary>
    /// Get cached value by key with deserialization
    /// </summary>
    Task<T?> GetAsync<T>(string key);
    
    /// <summary>
    /// Set cache value with expiration (string)
    /// </summary>
    Task SetAsync(string key, string value, TimeSpan? expiration = null);
    
    /// <summary>
    /// Set cache value with expiration and serialization
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    
    /// <summary>
    /// Delete cached value
    /// </summary>
    Task DeleteAsync(string key);
}
