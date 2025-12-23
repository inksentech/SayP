using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using SayP.Application.Interfaces;
using SayP.Domain.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;

namespace SayP.Application.Services;

/// <summary>
/// Service for generating JWT tokens for backend API authorization
/// </summary>
public class BackendTokenService : IBackendTokenService
{
    private readonly ISayPDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackendTokenService> _logger;

    public BackendTokenService(
        ISayPDbContext context,
        IConfiguration configuration,
        ILogger<BackendTokenService> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> GenerateBackendTokenAsync(string phoneNumber, Guid tenantId, Guid? companyId = null)
    {
        try
        {
            var resolvedTenantId = await ResolveTenantIdAsync(phoneNumber, tenantId);
            if (resolvedTenantId == Guid.Empty)
            {
                _logger.LogWarning("Unable to resolve tenant for {PhoneNumber}. Token generation aborted.", phoneNumber);
                throw new InvalidOperationException($"Tenant not found for phone number {phoneNumber}");
            }

            var hasAccess = await ValidateUserTenantAccessAsync(phoneNumber, resolvedTenantId);
            if (!hasAccess)
            {
                _logger.LogWarning("User {PhoneNumber} does not have access to tenant {TenantId}", phoneNumber, resolvedTenantId);
                throw new UnauthorizedAccessException($"User {phoneNumber} does not have access to tenant {resolvedTenantId}");
            }

            // Get JWT settings (must match backend configuration)
            var jwtKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key not configured");
            var jwtIssuer = _configuration["Jwt:Issuer"] ?? "SayP";
            var jwtAudience = _configuration["Jwt:Audience"] ?? "Backend";

            // Create claims
            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, phoneNumber),
                new Claim(ClaimTypes.NameIdentifier, phoneNumber),
                new Claim(ClaimTypes.Name, phoneNumber),
                new Claim("phone", phoneNumber),
                new Claim("tenant_id", resolvedTenantId.ToString()),
                new Claim("ServiceType", "SayP"),
                new Claim(ClaimTypes.Role, "ServiceAccount"),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            if (companyId.HasValue)
            {
                claims.Add(new Claim("company_id", companyId.Value.ToString()));
            }

            // Create signing key with KeyId
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            {
                KeyId = "SayP-Key-2024"
            };
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            // Create JWT token
            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.AddHours(24),
                signingCredentials: credentials
            );

            var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

            _logger.LogInformation("Generated backend token for {PhoneNumber} / Tenant {TenantId}", phoneNumber, resolvedTenantId);
            _logger.LogInformation("Token (first 100 chars): {Token}", tokenString.Substring(0, Math.Min(100, tokenString.Length)));
            _logger.LogInformation("Token length: {Length}, Parts: {Parts}", tokenString.Length, tokenString.Split('.').Length);

            return tokenString;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating backend token for {PhoneNumber} / Tenant {TenantId}", phoneNumber, tenantId);
            throw;
        }
    }

    public async Task<bool> ValidateUserTenantAccessAsync(string phoneNumber, Guid tenantId)
    {
        try
        {
            if (tenantId == Guid.Empty)
                return false;

            // Prefer user profiles because they store the canonical mapping
            var profileExists = await _context.UserProfiles
                .AsNoTracking()
                .AnyAsync(p => p.PhoneNumber == phoneNumber && p.TenantId == tenantId);

            if (profileExists)
                return true;

            // Fallback to tenant mappings (WhatsApp phone -> tenant records)
            var mappingExists = await _context.TenantMappings
                .AsNoTracking()
                .AnyAsync(m => m.PhoneNumber == phoneNumber && m.TenantId == tenantId && m.IsActive);

            return mappingExists;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating user tenant access for {PhoneNumber} / Tenant {TenantId}", phoneNumber, tenantId);
            return false;
        }
    }

    private async Task<Guid> ResolveTenantIdAsync(string phoneNumber, Guid tenantId)
    {
        if (tenantId != Guid.Empty)
            return tenantId;

        // Try to resolve from user profiles
        var profileTenantId = await _context.UserProfiles
            .AsNoTracking()
            .Where(p => p.PhoneNumber == phoneNumber)
            .OrderByDescending(p => p.UpdatedAt)
            .Select(p => p.TenantId)
            .FirstOrDefaultAsync();

        if (profileTenantId != Guid.Empty)
        {
            _logger.LogDebug("Resolved tenant {TenantId} for {PhoneNumber} via UserProfiles", profileTenantId, phoneNumber);
            return profileTenantId;
        }

        // Fallback to tenant mappings
        var mappingTenantId = await _context.TenantMappings
            .AsNoTracking()
            .Where(m => m.PhoneNumber == phoneNumber && m.IsActive)
            .OrderByDescending(m => m.UpdatedAt.HasValue ? m.UpdatedAt.Value : m.CreatedAt)
            .Select(m => m.TenantId)
            .FirstOrDefaultAsync();

        if (mappingTenantId != Guid.Empty)
        {
            _logger.LogDebug("Resolved tenant {TenantId} for {PhoneNumber} via TenantMappings", mappingTenantId, phoneNumber);
            return mappingTenantId;
        }

        return Guid.Empty;
    }
}
