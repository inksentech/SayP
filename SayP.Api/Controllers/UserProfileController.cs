using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SayP.Application.Services;
using SayP.Domain.Interfaces;

namespace SayP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UserProfileController : ControllerBase
{
    private readonly UserProfileService _userProfileService;
    private readonly ISayPDbContext _context;
    private readonly ILogger<UserProfileController> _logger;

    public UserProfileController(
        UserProfileService userProfileService,
        ISayPDbContext context,
        ILogger<UserProfileController> logger)
    {
        _userProfileService = userProfileService;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get user profile by phone number
    /// </summary>
    [HttpGet("{phoneNumber}")]
    public async Task<IActionResult> GetProfile(
        string phoneNumber,
        [FromQuery] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.PhoneNumber == phoneNumber && p.TenantId == tenantId, cancellationToken);

            if (profile == null)
                return NotFound(new { message = "User profile not found" });

            return Ok(profile);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user profile");
            return StatusCode(500, new { error = "Failed to retrieve user profile" });
        }
    }

    /// <summary>
    /// Get personalized context for user
    /// </summary>
    [HttpGet("{phoneNumber}/context")]
    public async Task<IActionResult> GetPersonalizedContext(
        string phoneNumber,
        [FromQuery] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.PhoneNumber == phoneNumber && p.TenantId == tenantId, cancellationToken);

            if (profile == null)
                return NotFound(new { message = "User profile not found" });

            var context = await _userProfileService.BuildPersonalizedContextAsync(profile.Id, cancellationToken);

            return Ok(new { context });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting personalized context");
            return StatusCode(500, new { error = "Failed to retrieve personalized context" });
        }
    }

    /// <summary>
    /// Get user behavior logs
    /// </summary>
    [HttpGet("{phoneNumber}/behaviors")]
    public async Task<IActionResult> GetBehaviors(
        string phoneNumber,
        [FromQuery] Guid tenantId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.PhoneNumber == phoneNumber && p.TenantId == tenantId, cancellationToken);

            if (profile == null)
                return NotFound(new { message = "User profile not found" });

            var behaviors = await _context.UserBehaviorLogs
                .Where(b => b.UserProfileId == profile.Id)
                .OrderByDescending(b => b.CreatedAt)
                .Take(limit)
                .ToListAsync(cancellationToken);

            return Ok(behaviors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user behaviors");
            return StatusCode(500, new { error = "Failed to retrieve user behaviors" });
        }
    }

    /// <summary>
    /// Manually trigger profile analysis
    /// </summary>
    [HttpPost("{phoneNumber}/analyze")]
    public async Task<IActionResult> AnalyzeProfile(
        string phoneNumber,
        [FromQuery] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.PhoneNumber == phoneNumber && p.TenantId == tenantId, cancellationToken);

            if (profile == null)
                return NotFound(new { message = "User profile not found" });

            await _userProfileService.AnalyzeAndUpdateProfileAsync(profile.Id, cancellationToken);

            // Reload profile
            profile = await _context.UserProfiles.FindAsync(new object[] { profile.Id }, cancellationToken);

            return Ok(new
            {
                message = "Profile analyzed successfully",
                learningScore = profile?.LearningScore ?? 0,
                satisfactionScore = profile?.SatisfactionScore ?? 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing user profile");
            return StatusCode(500, new { error = "Failed to analyze user profile" });
        }
    }

    /// <summary>
    /// Update custom context for user
    /// </summary>
    [HttpPut("{phoneNumber}/custom-context")]
    public async Task<IActionResult> UpdateCustomContext(
        string phoneNumber,
        [FromQuery] Guid tenantId,
        [FromBody] Dictionary<string, string> customContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.PhoneNumber == phoneNumber && p.TenantId == tenantId, cancellationToken);

            if (profile == null)
                return NotFound(new { message = "User profile not found" });

            profile.CustomContextJson = System.Text.Json.JsonSerializer.Serialize(customContext);
            profile.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Custom context updated successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating custom context");
            return StatusCode(500, new { error = "Failed to update custom context" });
        }
    }

    /// <summary>
    /// Get all user profiles (paginated)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAllProfiles(
        [FromQuery] Guid? tenantId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var query = _context.UserProfiles.AsQueryable();

            if (tenantId.HasValue)
                query = query.Where(p => p.TenantId == tenantId.Value);

            var total = await query.CountAsync(cancellationToken);
            var profiles = await query
                .OrderByDescending(p => p.LastActivityAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Ok(new
            {
                total,
                page,
                pageSize,
                profiles
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user profiles");
            return StatusCode(500, new { error = "Failed to retrieve user profiles" });
        }
    }

    /// <summary>
    /// Get user learning statistics
    /// </summary>
    [HttpGet("{phoneNumber}/stats")]
    public async Task<IActionResult> GetUserStats(
        string phoneNumber,
        [FromQuery] Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.PhoneNumber == phoneNumber && p.TenantId == tenantId, cancellationToken);

            if (profile == null)
                return NotFound(new { message = "User profile not found" });

            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            var recentBehaviors = await _context.UserBehaviorLogs
                .Where(b => b.UserProfileId == profile.Id && b.CreatedAt >= thirtyDaysAgo)
                .ToListAsync(cancellationToken);

            var stats = new
            {
                profile = new
                {
                    profile.PhoneNumber,
                    profile.UserName,
                    profile.LearningScore,
                    profile.SatisfactionScore,
                    profile.TotalMessageCount,
                    profile.TotalCommandCount,
                    profile.LastActivityAt
                },
                last30Days = new
                {
                    totalInteractions = recentBehaviors.Count,
                    successRate = recentBehaviors.Any() ? recentBehaviors.Average(b => b.IsSuccessful ? 1.0 : 0.0) * 100 : 0,
                    avgConfidence = recentBehaviors.Any() ? recentBehaviors.Average(b => b.Confidence) * 100 : 0,
                    avgResponseTime = recentBehaviors.Any() ? recentBehaviors.Average(b => b.ResponseTimeMs) : 0,
                    commandDistribution = recentBehaviors
                        .Where(b => !string.IsNullOrEmpty(b.CommandType))
                        .GroupBy(b => b.CommandType)
                        .Select(g => new { command = g.Key, count = g.Count() })
                        .OrderByDescending(x => x.count)
                        .Take(5)
                }
            };

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user stats");
            return StatusCode(500, new { error = "Failed to retrieve user stats" });
        }
    }
}
