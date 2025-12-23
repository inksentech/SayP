using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SayP.Infrastructure.Redis;

namespace SayP.Api.Controllers;

/// <summary>
/// Health check controller
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class HealthController : ControllerBase
{
    private readonly RedisCacheService _redis;
    private readonly ILogger<HealthController> _logger;

    public HealthController(RedisCacheService redis, ILogger<HealthController> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var health = new
        {
            status = "healthy",
            timestamp = DateTime.UtcNow,
            redis = await CheckRedis()
        };

        return Ok(health);
    }

    private async Task<string> CheckRedis()
    {
        try
        {
            await _redis.SetAsync("health:check", "ok", TimeSpan.FromSeconds(10));
            var value = await _redis.GetAsync<string>("health:check");
            return value == "ok" ? "connected" : "error";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Redis health check failed");
            return "disconnected";
        }
    }
}
