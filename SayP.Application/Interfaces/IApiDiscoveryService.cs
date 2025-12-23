using SayP.Domain.Models;

namespace SayP.Application.Interfaces;

/// <summary>
/// Service for discovering API endpoints from backend systems
/// </summary>
public interface IApiDiscoveryService
{
    /// <summary>
    /// Discover all SayP-enabled endpoints from a backend API
    /// </summary>
    /// <param name="baseUrl">Base URL of the backend API</param>
    /// <param name="apiKey">API key for authentication</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of discovered endpoints</returns>
    Task<List<DiscoveredEndpoint>> DiscoverEndpointsAsync(
        string baseUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Discover endpoints from Swagger/OpenAPI specification
    /// </summary>
    Task<List<DiscoveredEndpoint>> DiscoverFromSwaggerAsync(
        string swaggerUrl,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Get detailed schema for a specific endpoint
    /// </summary>
    Task<EndpointSchema?> GetEndpointSchemaAsync(
        string baseUrl,
        DiscoveredEndpoint endpoint,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Cache discovered endpoints for a tenant
    /// </summary>
    Task CacheDiscoveryAsync(
        Guid tenantId,
        List<DiscoveredEndpoint> endpoints,
        TimeSpan? expiration = null);
    
    /// <summary>
    /// Get cached endpoints for a tenant
    /// </summary>
    Task<List<DiscoveredEndpoint>?> GetCachedEndpointsAsync(Guid tenantId);
    
    /// <summary>
    /// Refresh discovery cache for a tenant
    /// </summary>
    Task RefreshDiscoveryAsync(
        Guid tenantId,
        string baseUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ✅ NEW: Invalidate discovery cache for a tenant (webhook support)
    /// </summary>
    Task InvalidateCacheAsync(Guid tenantId);
}
