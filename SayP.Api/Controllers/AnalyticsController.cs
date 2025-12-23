using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayP.Application.Services;
using SayP.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SayP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly IntentAnalyticsService _analyticsService;
    private readonly UserProfileService _userProfileService; // ✅ ADDED
    private readonly ConfidenceCalibrator _calibrator; // ✅ ADDED
    private readonly ISayPDbContext _context; // ✅ ADDED
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(
        IntentAnalyticsService analyticsService,
        UserProfileService userProfileService, // ✅ ADDED
        ConfidenceCalibrator calibrator, // ✅ ADDED
        ISayPDbContext context, // ✅ ADDED
        ILogger<AnalyticsController> logger)
    {
        _analyticsService = analyticsService;
        _userProfileService = userProfileService;
        _calibrator = calibrator;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get comprehensive dashboard metrics
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _analyticsService.GetDashboardMetricsAsync(
                startDate, 
                endDate, 
                cancellationToken);

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dashboard metrics");
            return StatusCode(500, new { error = "Failed to retrieve dashboard metrics" });
        }
    }

    /// <summary>
    /// Get intent classification accuracy metrics
    /// </summary>
    [HttpGet("intent-accuracy")]
    public async Task<IActionResult> GetIntentAccuracy(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _analyticsService.GetIntentAccuracyAsync(
                startDate, 
                endDate, 
                cancellationToken);

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting intent accuracy metrics");
            return StatusCode(500, new { error = "Failed to retrieve intent accuracy metrics" });
        }
    }

    /// <summary>
    /// Get command execution metrics
    /// </summary>
    [HttpGet("command-execution")]
    public async Task<IActionResult> GetCommandExecution(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _analyticsService.GetCommandExecutionMetricsAsync(
                startDate, 
                endDate, 
                cancellationToken);

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting command execution metrics");
            return StatusCode(500, new { error = "Failed to retrieve command execution metrics" });
        }
    }

    /// <summary>
    /// Get entity extraction metrics
    /// </summary>
    [HttpGet("entity-extraction")]
    public async Task<IActionResult> GetEntityExtraction(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _analyticsService.GetEntityExtractionMetricsAsync(
                startDate, 
                endDate, 
                cancellationToken);

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting entity extraction metrics");
            return StatusCode(500, new { error = "Failed to retrieve entity extraction metrics" });
        }
    }

    /// <summary>
    /// Get dialogue completion metrics
    /// </summary>
    [HttpGet("dialogue")]
    public async Task<IActionResult> GetDialogue(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _analyticsService.GetDialogueMetricsAsync(
                startDate, 
                endDate, 
                cancellationToken);

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting dialogue metrics");
            return StatusCode(500, new { error = "Failed to retrieve dialogue metrics" });
        }
    }

    /// <summary>
    /// ✅ NEW: Get user profile analytics
    /// </summary>
    [HttpGet("user-profiles")]
    public async Task<IActionResult> GetUserProfiles(
        [FromQuery] Guid? tenantId = null,
        [FromQuery] int top = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _context.UserProfiles.AsQueryable();

            if (tenantId.HasValue)
                query = query.Where(p => p.TenantId == tenantId.Value);

            var profiles = await query
                .OrderByDescending(p => p.LearningScore)
                .Take(top)
                .Select(p => new
                {
                    p.PhoneNumber,
                    p.UserName,
                    p.TotalMessageCount,
                    p.TotalCommandCount,
                    p.LearningScore,
                    p.SatisfactionScore,
                    p.PreferredLanguage,
                    p.AverageResponseTime,
                    p.LastActivityAt
                })
                .ToListAsync(cancellationToken);

            return Ok(profiles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user profiles");
            return StatusCode(500, new { error = "Failed to retrieve user profiles" });
        }
    }

    /// <summary>
    /// ✅ NEW: Get specific user profile details
    /// </summary>
    [HttpGet("user-profiles/{phoneNumber}")]
    public async Task<IActionResult> GetUserProfile(
        string phoneNumber,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.PhoneNumber == phoneNumber, cancellationToken);

            if (profile == null)
                return NotFound(new { error = "User profile not found" });

            // Get recent behavior logs
            var recentLogs = await _context.UserBehaviorLogs
                .Where(l => l.UserProfileId == profile.Id)
                .OrderByDescending(l => l.CreatedAt)
                .Take(20)
                .Select(l => new
                {
                    l.BehaviorType,
                    l.CommandType,
                    l.IsSuccessful,
                    l.Confidence,
                    l.ResponseTimeMs,
                    l.CreatedAt
                })
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                Profile = profile,
                RecentBehaviors = recentLogs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user profile for {PhoneNumber}", phoneNumber);
            return StatusCode(500, new { error = "Failed to retrieve user profile" });
        }
    }

    /// <summary>
    /// ✅ NEW: Get confidence calibration metrics for all intents
    /// </summary>
    [HttpGet("confidence-calibration")]
    public async Task<IActionResult> GetConfidenceCalibration(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _calibrator.GetAllMetricsAsync(cancellationToken);

            var result = metrics.Select(kvp => new
            {
                Intent = kvp.Key,
                TotalPredictions = kvp.Value.TotalPredictions,
                CorrectPredictions = kvp.Value.CorrectPredictions,
                IncorrectPredictions = kvp.Value.IncorrectPredictions,
                Accuracy = kvp.Value.Accuracy,
                OptimalThreshold = kvp.Value.OptimalThreshold,
                LastUpdated = kvp.Value.LastUpdated
            });

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting confidence calibration metrics");
            return StatusCode(500, new { error = "Failed to retrieve confidence calibration metrics" });
        }
    }

    /// <summary>
    /// ✅ NEW: Get learning statistics overview
    /// </summary>
    [HttpGet("learning-stats")]
    public async Task<IActionResult> GetLearningStats(
        [FromQuery] Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profilesQuery = _context.UserProfiles.AsQueryable();
            if (tenantId.HasValue)
                profilesQuery = profilesQuery.Where(p => p.TenantId == tenantId.Value);

            var totalUsers = await profilesQuery.CountAsync(cancellationToken);
            var avgLearningScore = await profilesQuery.AverageAsync(p => p.LearningScore, cancellationToken);
            var avgSatisfactionScore = await profilesQuery.AverageAsync(p => p.SatisfactionScore, cancellationToken);
            var totalCommands = await profilesQuery.SumAsync(p => p.TotalCommandCount, cancellationToken);
            var totalMessages = await profilesQuery.SumAsync(p => p.TotalMessageCount, cancellationToken);

            var behaviorLogsQuery = _context.UserBehaviorLogs.AsQueryable();
            if (tenantId.HasValue)
            {
                var userIds = await profilesQuery.Select(p => p.Id).ToListAsync(cancellationToken);
                behaviorLogsQuery = behaviorLogsQuery.Where(l => userIds.Contains(l.UserProfileId));
            }

            var totalBehaviorLogs = await behaviorLogsQuery.CountAsync(cancellationToken);
            var successRate = await behaviorLogsQuery.AverageAsync(l => l.IsSuccessful ? 1.0 : 0.0, cancellationToken);

            return Ok(new
            {
                TotalUsers = totalUsers,
                AverageLearningScore = Math.Round(avgLearningScore, 2),
                AverageSatisfactionScore = Math.Round(avgSatisfactionScore, 2),
                TotalCommands = totalCommands,
                TotalMessages = totalMessages,
                TotalBehaviorLogs = totalBehaviorLogs,
                SuccessRate = Math.Round(successRate * 100, 2)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting learning stats");
            return StatusCode(500, new { error = "Failed to retrieve learning statistics" });
        }
    }
}
