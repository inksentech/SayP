using Microsoft.AspNetCore.Mvc;
using SayP.Application.Interfaces;
using SayP.Application.Services;
using SayP.Domain.Interfaces;
using SayP.Infrastructure.Persistence;

namespace SayP.Api.Controllers;

/// <summary>
/// Chat API for Web Chat Simulator
/// Provides a simple REST interface for testing the Generic AI pipeline
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ChatController : ControllerBase
{
    private readonly IApiDiscoveryService _discoveryService;
    private readonly IDynamicIntentMapper _intentMapper;
    private readonly IGenericCommandExecutor _executor;
    private readonly ILogger<ChatController> _logger;

    public ChatController(
        IApiDiscoveryService discoveryService,
        IDynamicIntentMapper intentMapper,
        IGenericCommandExecutor executor,
        ILogger<ChatController> logger)
    {
        _discoveryService = discoveryService;
        _intentMapper = intentMapper;
        _executor = executor;
        _logger = logger;
    }

    /// <summary>
    /// Send a message and get AI response
    /// </summary>
    [HttpPost("message")]
    public async Task<IActionResult> SendMessage([FromBody] ChatMessageRequest request)
    {
        try
        {
            _logger.LogInformation("📨 Chat message from {Phone}: {Message}", 
                request.PhoneNumber, request.Message);

            // Parse tenant/company IDs
            Guid? tenantId = string.IsNullOrEmpty(request.TenantId) ? null : Guid.Parse(request.TenantId);
            Guid? companyId = string.IsNullOrEmpty(request.CompanyId) ? null : Guid.Parse(request.CompanyId);

            // Step 1: Discover endpoints
            var endpoints = await _discoveryService.DiscoverEndpointsAsync(
                request.BackendUrl,
                apiKey: null
            );

            if (endpoints == null || endpoints?.Count == 0)
            {
                return Ok(new ChatMessageResponse
                {
                    Text = "Sorry, I couldn't discover any endpoints from the backend. Please check the backend URL in settings.",
                    Success = false
                });
            }

            // Step 2: Map intent
            var intentResult = await _intentMapper.MapIntentAsync(
                request.Message,
                endpoints,
                tenantId?.ToString()
            );

            if (!intentResult.Success || intentResult.MatchedEndpoint == null)
            {
                return Ok(new ChatMessageResponse
                {
                    Text = intentResult.ErrorMessage ?? "I didn't understand that. Can you rephrase?",
                    Success = false,
                    Confidence = intentResult.Confidence
                });
            }

            // Step 3: Execute command (simplified - no slot filling for now)
            var executionResult = await _executor.ExecuteAsync(
                intentResult.MatchedEndpoint,
                new Dictionary<string, object>(),
                tenantId ?? Guid.Empty,
                request.BackendUrl
            );

            if (!executionResult.Success)
            {
                return Ok(new ChatMessageResponse
                {
                    Text = $"Execution failed: {executionResult.ErrorMessage}",
                    Success = false
                });
            }

            // Step 5: Return success response
            return Ok(new ChatMessageResponse
            {
                Text = $"✅ Command executed successfully!\n\nResponse: {executionResult.ResponseBody}",
                Success = true,
                Data = executionResult.ParsedResponse,
                Confidence = intentResult.Confidence
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error processing chat message");
            return Ok(new ChatMessageResponse
            {
                Text = $"Sorry, an error occurred: {ex.Message}",
                Success = false
            });
        }
    }
}

/// <summary>
/// Chat message request
/// </summary>
public class ChatMessageRequest
{
    public string PhoneNumber { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? TenantId { get; set; }
    public string? CompanyId { get; set; }
    public string BackendUrl { get; set; } = string.Empty;
}

/// <summary>
/// Chat message response
/// </summary>
public class ChatMessageResponse
{
    public string Text { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool RequiresMoreInfo { get; set; }
    public List<string>? MissingSlots { get; set; }
    public double Confidence { get; set; }
    public object? Data { get; set; }
}
