using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayP.Application.Services;
using SayP.Domain.Entities;

namespace SayP.Api.Controllers;

/// <summary>
/// Conversations management for mobile app
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ConversationsController : ControllerBase
{
    private readonly ConversationManager _conversationManager;
    private readonly ILogger<ConversationsController> _logger;

    public ConversationsController(
        ConversationManager conversationManager,
        ILogger<ConversationsController> logger)
    {
        _conversationManager = conversationManager;
        _logger = logger;
    }

    /// <summary>
    /// Get all conversations
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        try
        {
            var conversations = await _conversationManager.GetAllConversationsAsync();
            return Ok(conversations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversations");
            return StatusCode(500, new { message = "An error occurred while retrieving conversations" });
        }
    }

    /// <summary>
    /// Get conversation by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        try
        {
            var conversation = await _conversationManager.GetConversationAsync(id);
            if (conversation == null)
            {
                return NotFound(new { message = "Conversation not found" });
            }
            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversation {ConversationId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving conversation" });
        }
    }

    /// <summary>
    /// Get messages for a conversation
    /// </summary>
    [HttpGet("{id}/messages")]
    public async Task<IActionResult> GetMessages(string id)
    {
        try
        {
            var messages = await _conversationManager.GetMessagesAsync(id);
            return Ok(messages);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for conversation {ConversationId}", id);
            return StatusCode(500, new { message = "An error occurred while retrieving messages" });
        }
    }

    /// <summary>
    /// Send a message
    /// </summary>
    [HttpPost("{id}/messages")]
    public async Task<IActionResult> SendMessage(string id, [FromBody] SendMessageRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Content))
            {
                return BadRequest(new { message = "Message content is required" });
            }

            // Get tenant ID from user claims (or use default for mobile)
            var tenantIdClaim = User.FindFirst("tenantId")?.Value;
            Guid? tenantId = null;
            if (!string.IsNullOrEmpty(tenantIdClaim) && Guid.TryParse(tenantIdClaim, out var parsedTenantId))
            {
                tenantId = parsedTenantId;
            }

            var message = await _conversationManager.SendMessageAsync(id, request.Content, request.Type ?? "text", tenantId);
            return Ok(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to conversation {ConversationId}", id);
            return StatusCode(500, new { message = "An error occurred while sending message" });
        }
    }

    /// <summary>
    /// Create a new conversation
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateConversationRequest request)
    {
        try
        {
            // Get tenant ID from user claims (or use default for mobile)
            var tenantIdClaim = User.FindFirst("tenantId")?.Value;
            Guid? tenantId = null;
            if (!string.IsNullOrEmpty(tenantIdClaim) && Guid.TryParse(tenantIdClaim, out var parsedTenantId))
            {
                tenantId = parsedTenantId;
            }

            var conversation = await _conversationManager.CreateConversationAsync(
                request.PhoneNumber ?? "mobile-app",
                request.Title ?? "New Conversation",
                tenantId
            );
            return Ok(conversation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating conversation");
            return StatusCode(500, new { message = "An error occurred while creating conversation" });
        }
    }

    /// <summary>
    /// Delete a conversation
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        try
        {
            await _conversationManager.DeleteConversationAsync(id);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting conversation {ConversationId}", id);
            return StatusCode(500, new { message = "An error occurred while deleting conversation" });
        }
    }
}

public class SendMessageRequest
{
    public string Content { get; set; } = string.Empty;
    public string? Type { get; set; }
}

public class CreateConversationRequest
{
    public string? PhoneNumber { get; set; }
    public string? Title { get; set; }
}
