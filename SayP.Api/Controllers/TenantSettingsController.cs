using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayP.Application.Services;
using SayP.Domain.Entities;
using System.Security.Claims;

namespace SayP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class TenantSettingsController : ControllerBase
{
    private readonly TenantSettingsService _settingsService;
    private readonly ILogger<TenantSettingsController> _logger;

    public TenantSettingsController(
        TenantSettingsService settingsService,
        ILogger<TenantSettingsController> logger)
    {
        _settingsService = settingsService;
        _logger = logger;
    }

    /// <summary>
    /// Get tenant settings
    /// </summary>
    [HttpGet("{tenantId}")]
    public async Task<IActionResult> GetSettings(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check authorization
            if (!await IsAuthorizedForTenantAsync(tenantId))
                return Forbid();

            var settings = await _settingsService.GetSettingsAsync(tenantId, cancellationToken);
            return Ok(settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting settings for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "Failed to retrieve settings" });
        }
    }

    /// <summary>
    /// Get all tenant settings (Admin only)
    /// </summary>
    [HttpGet]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> GetAllSettings(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var allSettings = await _settingsService.GetAllSettingsAsync(cancellationToken);
            return Ok(allSettings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all tenant settings");
            return StatusCode(500, new { error = "Failed to retrieve all settings" });
        }
    }

    /// <summary>
    /// Toggle a specific feature
    /// </summary>
    [HttpPost("{tenantId}/toggle-feature")]
    public async Task<IActionResult> ToggleFeature(
        Guid tenantId,
        [FromBody] ToggleFeatureRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check authorization
            if (!await IsAuthorizedForTenantAsync(tenantId))
                return Forbid();

            var userId = GetCurrentUserId();
            var success = await _settingsService.ToggleFeatureAsync(
                tenantId,
                request.FeatureName,
                request.Enabled,
                userId,
                request.Reason,
                cancellationToken);

            if (!success)
                return BadRequest(new { error = "Failed to toggle feature" });

            return Ok(new
            {
                message = $"Feature {request.FeatureName} {(request.Enabled ? "enabled" : "disabled")} successfully",
                feature = request.FeatureName,
                enabled = request.Enabled
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling feature for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "Failed to toggle feature" });
        }
    }

    /// <summary>
    /// Toggle all features
    /// </summary>
    [HttpPost("{tenantId}/toggle-all")]
    public async Task<IActionResult> ToggleAllFeatures(
        Guid tenantId,
        [FromBody] ToggleAllFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check authorization
            if (!await IsAuthorizedForTenantAsync(tenantId))
                return Forbid();

            var userId = GetCurrentUserId();
            var success = await _settingsService.ToggleAllFeaturesAsync(
                tenantId,
                request.Enabled,
                userId,
                request.Reason,
                cancellationToken);

            if (!success)
                return BadRequest(new { error = "Failed to toggle all features" });

            return Ok(new
            {
                message = $"All features {(request.Enabled ? "enabled" : "disabled")} successfully",
                enabled = request.Enabled
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling all features for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "Failed to toggle all features" });
        }
    }

    /// <summary>
    /// Update advanced settings
    /// </summary>
    [HttpPut("{tenantId}/advanced")]
    public async Task<IActionResult> UpdateAdvancedSettings(
        Guid tenantId,
        [FromBody] AdvancedSettingsRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check authorization
            if (!await IsAuthorizedForTenantAsync(tenantId))
                return Forbid();

            var userId = GetCurrentUserId();
            var settings = await _settingsService.UpdateSettingsAsync(
                tenantId,
                s =>
                {
                    if (request.MinConfidenceThreshold.HasValue)
                        s.MinConfidenceThreshold = request.MinConfidenceThreshold.Value;
                    
                    if (request.HighConfidenceThreshold.HasValue)
                        s.HighConfidenceThreshold = request.HighConfidenceThreshold.Value;
                    
                    if (request.DialogueTimeoutMinutes.HasValue)
                        s.DialogueTimeoutMinutes = request.DialogueTimeoutMinutes.Value;
                    
                    if (request.MaxDialogueAttempts.HasValue)
                        s.MaxDialogueAttempts = request.MaxDialogueAttempts.Value;
                    
                    if (request.ProfileAnalysisInterval.HasValue)
                        s.ProfileAnalysisInterval = request.ProfileAnalysisInterval.Value;
                    
                    if (request.BehaviorLogRetentionDays.HasValue)
                        s.BehaviorLogRetentionDays = request.BehaviorLogRetentionDays.Value;
                },
                userId,
                "Advanced settings updated",
                cancellationToken);

            return Ok(new
            {
                message = "Advanced settings updated successfully",
                settings
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating advanced settings for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "Failed to update advanced settings" });
        }
    }

    /// <summary>
    /// Get change history
    /// </summary>
    [HttpGet("{tenantId}/history")]
    public async Task<IActionResult> GetChangeHistory(
        Guid tenantId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check authorization
            if (!await IsAuthorizedForTenantAsync(tenantId))
                return Forbid();

            var history = await _settingsService.GetChangeHistoryAsync(tenantId, limit, cancellationToken);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting change history for tenant {TenantId}", tenantId);
            return StatusCode(500, new { error = "Failed to retrieve change history" });
        }
    }

    /// <summary>
    /// Check if feature is enabled
    /// </summary>
    [HttpGet("{tenantId}/feature/{featureName}")]
    public async Task<IActionResult> IsFeatureEnabled(
        Guid tenantId,
        string featureName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var enabled = await _settingsService.IsFeatureEnabledAsync(tenantId, featureName, cancellationToken);
            return Ok(new { feature = featureName, enabled });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking feature {Feature} for tenant {TenantId}", featureName, tenantId);
            return StatusCode(500, new { error = "Failed to check feature status" });
        }
    }

    /// <summary>
    /// Bulk update features (Admin only)
    /// </summary>
    [HttpPost("bulk-update")]
    [Authorize(Roles = "Admin,SuperAdmin")]
    public async Task<IActionResult> BulkUpdateFeatures(
        [FromBody] BulkUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var userId = GetCurrentUserId();
            var results = new List<object>();

            foreach (var tenantId in request.TenantIds)
            {
                try
                {
                    var success = await _settingsService.ToggleAllFeaturesAsync(
                        tenantId,
                        request.Enabled,
                        userId,
                        request.Reason,
                        cancellationToken);

                    results.Add(new
                    {
                        tenantId,
                        success,
                        message = success ? "Updated successfully" : "Failed to update"
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in bulk update for tenant {TenantId}", tenantId);
                    results.Add(new
                    {
                        tenantId,
                        success = false,
                        message = ex.Message
                    });
                }
            }

            return Ok(new
            {
                message = "Bulk update completed",
                results
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in bulk update");
            return StatusCode(500, new { error = "Failed to perform bulk update" });
        }
    }

    // Helper methods
    private async Task<bool> IsAuthorizedForTenantAsync(Guid tenantId)
    {
        // Admin/SuperAdmin can access all tenants
        if (User.IsInRole("Admin") || User.IsInRole("SuperAdmin"))
            return true;

        // Check if user belongs to the tenant
        // JWT claim name is "tenant_id" (lowercase with underscore)
        var userTenantId = User.FindFirst("tenant_id")?.Value 
                        ?? User.FindFirst("TenantId")?.Value;  // Fallback
        
        if (string.IsNullOrEmpty(userTenantId))
        {
            _logger.LogWarning("No tenant_id claim found in JWT token");
            return false;
        }

        var matches = Guid.Parse(userTenantId) == tenantId;
        _logger.LogInformation("Authorization check: User tenant {UserTenantId} vs requested {RequestedTenantId} = {Matches}", 
            userTenantId, tenantId, matches);
        
        return matches;
    }

    private Guid GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                       ?? User.FindFirst("sub")?.Value;  // JWT standard claim
        return string.IsNullOrEmpty(userIdClaim) ? Guid.Empty : Guid.Parse(userIdClaim);
    }
}

// Request models
public class ToggleFeatureRequest
{
    public string FeatureName { get; set; } = string.Empty;
    public bool Enabled { get; set; }
    public string? Reason { get; set; }
}

public class ToggleAllFeaturesRequest
{
    public bool Enabled { get; set; }
    public string? Reason { get; set; }
}

public class AdvancedSettingsRequest
{
    public double? MinConfidenceThreshold { get; set; }
    public double? HighConfidenceThreshold { get; set; }
    public int? DialogueTimeoutMinutes { get; set; }
    public int? MaxDialogueAttempts { get; set; }
    public int? ProfileAnalysisInterval { get; set; }
    public int? BehaviorLogRetentionDays { get; set; }
}

public class BulkUpdateRequest
{
    public List<Guid> TenantIds { get; set; } = new();
    public bool Enabled { get; set; }
    public string? Reason { get; set; }
}
