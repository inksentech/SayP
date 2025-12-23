using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;

namespace SayP.Api.Controllers;

/// <summary>
/// ✅ NEW: Webhook controller for API discovery cache invalidation
/// Backend can call this webhook when new endpoints are deployed
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ApiDiscoveryWebhookController : ControllerBase
{
    private readonly IApiDiscoveryService _discoveryService;
    private readonly ILogger<ApiDiscoveryWebhookController> _logger;

    public ApiDiscoveryWebhookController(
        IApiDiscoveryService discoveryService,
        ILogger<ApiDiscoveryWebhookController> logger)
    {
        _discoveryService = discoveryService;
        _logger = logger;
    }

    /// <summary>
    /// Invalidate API discovery cache for a specific tenant
    /// </summary>
    /// <param name="request">Webhook request with tenant ID</param>
    [HttpPost("invalidate")]
    public async Task<IActionResult> InvalidateCache([FromBody] InvalidateCacheRequest request)
    {
        try
        {
            if (request.TenantId == Guid.Empty)
            {
                return BadRequest(new { error = "TenantId is required" });
            }

            // Validate webhook secret if provided
            if (!string.IsNullOrEmpty(request.Secret))
            {
                var expectedSecret = Environment.GetEnvironmentVariable("API_DISCOVERY_WEBHOOK_SECRET");
                if (request.Secret != expectedSecret)
                {
                    _logger.LogWarning("Invalid webhook secret for tenant {TenantId}", request.TenantId);
                    return Unauthorized(new { error = "Invalid secret" });
                }
            }

            _logger.LogInformation("Invalidating API discovery cache for tenant {TenantId}", request.TenantId);

            // Invalidate cache
            await _discoveryService.InvalidateCacheAsync(request.TenantId);

            // Optionally trigger immediate re-discovery
            if (request.RediscoverImmediately && !string.IsNullOrEmpty(request.BackendUrl))
            {
                _logger.LogInformation("Triggering immediate re-discovery for tenant {TenantId}", request.TenantId);
                var endpoints = await _discoveryService.DiscoverEndpointsAsync(
                    request.BackendUrl,
                    request.ApiKey,
                    CancellationToken.None);
                
                await _discoveryService.CacheDiscoveryAsync(request.TenantId, endpoints);
                
                return Ok(new 
                { 
                    message = "Cache invalidated and re-discovered",
                    endpointCount = endpoints.Count,
                    tenantId = request.TenantId
                });
            }

            return Ok(new 
            { 
                message = "Cache invalidated successfully",
                tenantId = request.TenantId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating API discovery cache");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Invalidate cache for all tenants (use with caution)
    /// </summary>
    [HttpPost("invalidate-all")]
    public IActionResult InvalidateAllCaches([FromBody] InvalidateAllRequest request)
    {
        try
        {
            // Validate admin secret
            var adminSecret = Environment.GetEnvironmentVariable("API_DISCOVERY_ADMIN_SECRET");
            if (string.IsNullOrEmpty(adminSecret) || request.AdminSecret != adminSecret)
            {
                _logger.LogWarning("Invalid admin secret for invalidate-all request");
                return Unauthorized(new { error = "Invalid admin secret" });
            }

            _logger.LogWarning("Invalidating ALL API discovery caches (admin action)");

            // Note: This is a simplified version. In production, you'd need to:
            // 1. Get all tenant IDs from database
            // 2. Invalidate cache for each tenant
            // 3. Or use a cache pattern that allows wildcard deletion

            return Ok(new { message = "All caches invalidated (implementation needed)" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating all caches");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

/// <summary>
/// Request model for cache invalidation
/// </summary>
public class InvalidateCacheRequest
{
    public Guid TenantId { get; set; }
    public string? Secret { get; set; }
    public bool RediscoverImmediately { get; set; } = false;
    public string? BackendUrl { get; set; }
    public string? ApiKey { get; set; }
}

/// <summary>
/// Request model for invalidating all caches
/// </summary>
public class InvalidateAllRequest
{
    public string AdminSecret { get; set; } = string.Empty;
}
