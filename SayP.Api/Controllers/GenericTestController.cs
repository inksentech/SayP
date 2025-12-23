using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayP.Application.Interfaces;
using SayP.Application.Services;

namespace SayP.Api.Controllers;

/// <summary>
/// Test endpoints for Generic AI functionality
/// </summary>
[ApiController]
[Route("api/test")]
[AllowAnonymous]
public class GenericTestController : ControllerBase
{
    private readonly IApiDiscoveryService _discoveryService;
    private readonly IDynamicIntentMapper _intentMapper;
    private readonly IGenericCommandExecutor _executor;
    private readonly GenericConversationManager _conversationManager;
    private readonly ILogger<GenericTestController> _logger;

    public GenericTestController(
        IApiDiscoveryService discoveryService,
        IDynamicIntentMapper intentMapper,
        IGenericCommandExecutor executor,
        GenericConversationManager conversationManager,
        ILogger<GenericTestController> logger)
    {
        _discoveryService = discoveryService;
        _intentMapper = intentMapper;
        _executor = executor;
        _conversationManager = conversationManager;
        _logger = logger;
    }

    /// <summary>
    /// Test API discovery from a backend URL
    /// </summary>
    [HttpGet("discovery")]
    public async Task<IActionResult> TestDiscovery(
        [FromQuery] string url,
        [FromQuery] string? apiKey = null)
    {
        try
        {
            _logger.LogInformation("Testing discovery for URL: {Url}", url);

            var endpoints = await _discoveryService.DiscoverEndpointsAsync(url, apiKey);

            return Ok(new
            {
                success = true,
                url = url,
                endpointCount = endpoints.Count,
                endpoints = endpoints.Select(e => new
                {
                    e.Intent,
                    e.Description,
                    e.HttpMethod,
                    e.Route,
                    e.ControllerName,
                    e.ActionName,
                    e.RequiresConfirmation,
                    schemaFields = e.Schema?.Fields.Select(f => new
                    {
                        f.Name,
                        f.Type,
                        f.Description,
                        f.IsRequired,
                        f.Example
                    })
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing discovery");
            return BadRequest(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Test intent mapping for a message
    /// </summary>
    [HttpPost("intent")]
    public async Task<IActionResult> TestIntentMapping([FromBody] TestIntentRequest request)
    {
        try
        {
            _logger.LogInformation("Testing intent mapping for message: {Message}", request.Message);

            // Get cached endpoints or discover
            var endpoints = await _discoveryService.GetCachedEndpointsAsync(request.TenantId);
            if (endpoints == null || !endpoints.Any())
            {
                if (string.IsNullOrEmpty(request.BackendUrl))
                {
                    return BadRequest(new
                    {
                        success = false,
                        error = "No cached endpoints found. Please provide backendUrl to discover."
                    });
                }

                endpoints = await _discoveryService.DiscoverEndpointsAsync(request.BackendUrl, request.ApiKey);
                await _discoveryService.CacheDiscoveryAsync(request.TenantId, endpoints);
            }

            var result = await _intentMapper.MapIntentAsync(
                request.Message,
                endpoints,
                request.ConversationContext);

            return Ok(new
            {
                success = result.Success,
                matchedIntent = result.MatchedEndpoint?.Intent,
                confidence = result.Confidence,
                description = result.MatchedEndpoint?.Description,
                extractedParameters = result.ExtractedParameters,
                alternatives = result.AlternativeEndpoints.Select(e => new
                {
                    e.Intent,
                    e.Description
                }),
                errorMessage = result.ErrorMessage
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing intent mapping");
            return BadRequest(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Test command execution
    /// </summary>
    [HttpPost("execute")]
    public async Task<IActionResult> TestExecution([FromBody] TestExecutionRequest request)
    {
        try
        {
            _logger.LogInformation("Testing execution for intent: {Intent}", request.Intent);

            // Get cached endpoints
            var endpoints = await _discoveryService.GetCachedEndpointsAsync(request.TenantId);
            if (endpoints == null || !endpoints.Any())
            {
                return BadRequest(new
                {
                    success = false,
                    error = "No cached endpoints found. Run discovery first."
                });
            }

            var endpoint = endpoints.FirstOrDefault(e => e.Intent == request.Intent);
            if (endpoint == null)
            {
                return NotFound(new
                {
                    success = false,
                    error = $"Endpoint with intent '{request.Intent}' not found"
                });
            }

            var result = await _executor.ExecuteAsync(
                endpoint,
                request.Parameters,
                request.TenantId,
                request.BackendUrl,
                request.ApiKey);

            return Ok(new
            {
                result.Success,
                result.StatusCode,
                result.ResponseBody,
                result.ParsedResponse,
                result.ErrorMessage,
                executionTimeMs = result.ExecutionTime.TotalMilliseconds
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing execution");
            return BadRequest(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Test complete conversation flow
    /// </summary>
    [HttpPost("conversation")]
    public async Task<IActionResult> TestConversation([FromBody] TestConversationRequest request)
    {
        try
        {
            _logger.LogInformation("Testing conversation for message: {Message}", request.Message);

            var result = await _conversationManager.ProcessMessageAsync(
                request.PhoneNumber,
                request.Message,
                request.TenantId,
                request.BackendUrl,
                request.ApiKey);

            return Ok(new
            {
                result.Success,
                result.Response,
                result.RequiresMoreInfo,
                result.RequiresConfirmation,
                pendingIntent = result.PendingEndpoint?.Intent,
                currentSlots = result.CurrentSlots,
                missingSlots = result.MissingSlots.Select(s => new
                {
                    s.FieldName,
                    s.Description,
                    s.Example
                }),
                executionResult = result.ExecutionResult != null ? new
                {
                    result.ExecutionResult.Success,
                    result.ExecutionResult.StatusCode,
                    executionTimeMs = result.ExecutionResult.ExecutionTime.TotalMilliseconds
                } : null
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error testing conversation");
            return BadRequest(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Refresh discovery cache for a tenant
    /// </summary>
    [HttpPost("refresh-discovery")]
    public async Task<IActionResult> RefreshDiscovery([FromBody] RefreshDiscoveryRequest request)
    {
        try
        {
            await _discoveryService.RefreshDiscoveryAsync(
                request.TenantId,
                request.BackendUrl,
                request.ApiKey);

            var endpoints = await _discoveryService.GetCachedEndpointsAsync(request.TenantId);

            return Ok(new
            {
                success = true,
                message = "Discovery cache refreshed",
                endpointCount = endpoints?.Count ?? 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing discovery");
            return BadRequest(new
            {
                success = false,
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Get cached endpoints for a tenant
    /// </summary>
    [HttpGet("cached-endpoints/{tenantId}")]
    public async Task<IActionResult> GetCachedEndpoints(Guid tenantId)
    {
        try
        {
            var endpoints = await _discoveryService.GetCachedEndpointsAsync(tenantId);

            if (endpoints == null || !endpoints.Any())
            {
                return NotFound(new
                {
                    success = false,
                    message = "No cached endpoints found for this tenant"
                });
            }

            return Ok(new
            {
                success = true,
                tenantId,
                endpointCount = endpoints.Count,
                endpoints = endpoints.Select(e => new
                {
                    e.Intent,
                    e.Description,
                    e.HttpMethod,
                    e.Route,
                    e.SuccessRate,
                    e.CallCount
                })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting cached endpoints");
            return BadRequest(new
            {
                success = false,
                error = ex.Message
            });
        }
    }
}

// Request DTOs
public class TestIntentRequest
{
    public string Message { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string? BackendUrl { get; set; }
    public string? ApiKey { get; set; }
    public string? ConversationContext { get; set; }
}

public class TestExecutionRequest
{
    public string Intent { get; set; } = string.Empty;
    public Dictionary<string, object> Parameters { get; set; } = new();
    public Guid TenantId { get; set; }
    public string BackendUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
}

public class TestConversationRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public string BackendUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
}

public class RefreshDiscoveryRequest
{
    public Guid TenantId { get; set; }
    public string BackendUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
}
