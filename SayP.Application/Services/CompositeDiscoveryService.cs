using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SayP.Application.Interfaces;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;

namespace SayP.Application.Services;

/// <summary>
/// Composite Discovery Service - MCP ve Swagger discovery'yi birleştirir
/// Mevcut ApiDiscoveryService'i DEĞİŞTİRMEZ, sadece wrapper olarak çalışır
/// </summary>
public class CompositeDiscoveryService : IApiDiscoveryService
{
    private readonly ApiDiscoveryService _swaggerDiscovery;
    private readonly IMcpDiscoveryService? _mcpDiscovery;
    private readonly ICacheService _cache;
    private readonly ILogger<CompositeDiscoveryService> _logger;
    private readonly McpOptions _options;
    
    private const string CACHE_KEY_PREFIX = "composite_discovery:";
    
    public CompositeDiscoveryService(
        ApiDiscoveryService swaggerDiscovery,
        ICacheService cache,
        ILogger<CompositeDiscoveryService> logger,
        IOptions<McpOptions> options,
        IMcpDiscoveryService? mcpDiscovery = null)
    {
        _swaggerDiscovery = swaggerDiscovery;
        _mcpDiscovery = mcpDiscovery;
        _cache = cache;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task<List<DiscoveredEndpoint>> DiscoverEndpointsAsync(
        string baseUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        var endpoints = new List<DiscoveredEndpoint>();
        var mcpEndpointCount = 0;
        var swaggerEndpointCount = 0;
        
        // 1. MCP Discovery (if enabled and available)
        if (_options.Enabled && _mcpDiscovery != null)
        {
            try
            {
                var mcpUrl = GetMcpUrl(baseUrl);
                var mcpAvailable = await _mcpDiscovery.IsMcpAvailableAsync(baseUrl, cancellationToken);
                
                if (mcpAvailable)
                {
                    var mcpEndpoints = await _mcpDiscovery.DiscoverFromMcpAsync(mcpUrl, cancellationToken);
                    
                    if (mcpEndpoints.Any())
                    {
                        endpoints.AddRange(mcpEndpoints);
                        mcpEndpointCount = mcpEndpoints.Count;
                        
                        _logger.LogInformation("✅ MCP discovery successful: {Count} endpoints", mcpEndpointCount);
                    }
                }
                else
                {
                    _logger.LogDebug("MCP not available at {BaseUrl}, falling back to Swagger", baseUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "MCP discovery failed, falling back to Swagger");
            }
        }
        
        // 2. Swagger Discovery (always run to get additional endpoints)
        try
        {
            var swaggerEndpoints = await _swaggerDiscovery.DiscoverEndpointsAsync(baseUrl, apiKey, cancellationToken);
            
            // Merge: Add Swagger endpoints that don't exist in MCP
            foreach (var swaggerEndpoint in swaggerEndpoints)
            {
                // Check if this intent already exists from MCP
                var existsInMcp = endpoints.Any(e => 
                    e.Intent.Equals(swaggerEndpoint.Intent, StringComparison.OrdinalIgnoreCase));
                
                if (!existsInMcp)
                {
                    endpoints.Add(swaggerEndpoint);
                    swaggerEndpointCount++;
                }
                else
                {
                    // MCP endpoint exists, but we might want to merge additional metadata
                    var mcpEndpoint = endpoints.First(e => 
                        e.Intent.Equals(swaggerEndpoint.Intent, StringComparison.OrdinalIgnoreCase));
                    
                    MergeEndpointMetadata(mcpEndpoint, swaggerEndpoint);
                }
            }
            
            _logger.LogInformation(
                "Discovery complete: {McpCount} MCP + {SwaggerCount} Swagger = {TotalCount} total endpoints",
                mcpEndpointCount, swaggerEndpointCount, endpoints.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Swagger discovery failed");
            
            // If MCP also failed, we have no endpoints
            if (!endpoints.Any())
            {
                throw;
            }
        }
        
        return endpoints;
    }

    /// <inheritdoc />
    public async Task<List<DiscoveredEndpoint>> DiscoverFromSwaggerAsync(
        string swaggerUrl,
        CancellationToken cancellationToken = default)
    {
        // Delegate to Swagger discovery
        return await _swaggerDiscovery.DiscoverFromSwaggerAsync(swaggerUrl, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EndpointSchema?> GetEndpointSchemaAsync(
        string baseUrl,
        DiscoveredEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        // If it's an MCP endpoint, schema is already included
        if (endpoint.HttpMethod == "MCP" && endpoint.Schema != null)
        {
            return endpoint.Schema;
        }
        
        // Otherwise, delegate to Swagger
        return await _swaggerDiscovery.GetEndpointSchemaAsync(baseUrl, endpoint, cancellationToken);
    }

    /// <inheritdoc />
    public async Task CacheDiscoveryAsync(
        Guid tenantId,
        List<DiscoveredEndpoint> endpoints,
        TimeSpan? expiration = null)
    {
        await _swaggerDiscovery.CacheDiscoveryAsync(tenantId, endpoints, expiration);
    }

    /// <inheritdoc />
    public async Task<List<DiscoveredEndpoint>?> GetCachedEndpointsAsync(Guid tenantId)
    {
        return await _swaggerDiscovery.GetCachedEndpointsAsync(tenantId);
    }

    /// <inheritdoc />
    public async Task RefreshDiscoveryAsync(
        Guid tenantId,
        string baseUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Refreshing discovery cache for tenant {TenantId}", tenantId);
        
        var endpoints = await DiscoverEndpointsAsync(baseUrl, apiKey, cancellationToken);
        await CacheDiscoveryAsync(tenantId, endpoints);
        
        _logger.LogInformation("Discovery cache refreshed for tenant {TenantId}: {Count} endpoints", 
            tenantId, endpoints.Count);
    }

    /// <inheritdoc />
    public async Task InvalidateCacheAsync(Guid tenantId)
    {
        await _swaggerDiscovery.InvalidateCacheAsync(tenantId);
    }

    #region Helpers

    /// <summary>
    /// MCP endpoint'e Swagger'dan ek metadata ekle
    /// </summary>
    private void MergeEndpointMetadata(DiscoveredEndpoint mcpEndpoint, DiscoveredEndpoint swaggerEndpoint)
    {
        // MCP'de yoksa Swagger'dan al
        if (string.IsNullOrEmpty(mcpEndpoint.Description) && !string.IsNullOrEmpty(swaggerEndpoint.Description))
        {
            mcpEndpoint.Description = swaggerEndpoint.Description;
        }
        
        // Aliases'ları birleştir
        if (swaggerEndpoint.Aliases?.Any() == true)
        {
            var allAliases = (mcpEndpoint.Aliases ?? Array.Empty<string>())
                .Concat(swaggerEndpoint.Aliases)
                .Distinct()
                .ToArray();
            
            mcpEndpoint.Aliases = allAliases;
        }
        
        // Schema'yı zenginleştir
        if (mcpEndpoint.Schema == null && swaggerEndpoint.Schema != null)
        {
            mcpEndpoint.Schema = swaggerEndpoint.Schema;
        }
        else if (mcpEndpoint.Schema != null && swaggerEndpoint.Schema != null)
        {
            // Merge field descriptions from Swagger if MCP doesn't have them
            foreach (var swaggerField in swaggerEndpoint.Schema.Fields)
            {
                var mcpField = mcpEndpoint.Schema.Fields
                    .FirstOrDefault(f => f.Name.Equals(swaggerField.Name, StringComparison.OrdinalIgnoreCase));
                
                if (mcpField != null)
                {
                    if (string.IsNullOrEmpty(mcpField.Description))
                        mcpField.Description = swaggerField.Description;
                    
                    if (string.IsNullOrEmpty(mcpField.Example))
                        mcpField.Example = swaggerField.Example;
                    
                    if (mcpField.Aliases?.Any() != true && swaggerField.Aliases?.Any() == true)
                        mcpField.Aliases = swaggerField.Aliases;
                }
            }
        }
        
        // Controller/Action bilgisi
        if (string.IsNullOrEmpty(mcpEndpoint.ControllerName) || mcpEndpoint.ControllerName == "MCP")
        {
            mcpEndpoint.ControllerName = swaggerEndpoint.ControllerName;
            mcpEndpoint.ActionName = swaggerEndpoint.ActionName;
        }
    }

    /// <summary>
    /// Backend URL'den MCP URL oluştur
    /// </summary>
    private string GetMcpUrl(string baseUrl)
    {
        return baseUrl.TrimEnd('/') + "/mcp";
    }

    #endregion
}

/// <summary>
/// MCP configuration options
/// </summary>
public class McpOptions
{
    public const string SectionName = "Mcp";
    
    /// <summary>
    /// Enable MCP discovery
    /// </summary>
    public bool Enabled { get; set; } = false;
    
    /// <summary>
    /// MCP endpoint suffix (default: /mcp)
    /// </summary>
    public string EndpointSuffix { get; set; } = "/mcp";
    
    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;
    
    /// <summary>
    /// Prefer MCP over Swagger when both are available
    /// </summary>
    public bool PreferMcp { get; set; } = true;
    
    /// <summary>
    /// Cache MCP discovery results
    /// </summary>
    public bool CacheEnabled { get; set; } = true;
    
    /// <summary>
    /// Cache duration in minutes
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 60;
}
