namespace SayP.Application.Interfaces;

/// <summary>
/// Service for generating JWT tokens for backend API authorization
/// </summary>
public interface IBackendTokenService
{
    /// <summary>
    /// Generate JWT token for backend API access
    /// </summary>
    /// <param name="phoneNumber">User's phone number</param>
    /// <param name="tenantId">Tenant ID</param>
    /// <param name="companyId">Company ID (optional)</param>
    /// <returns>JWT token string</returns>
    Task<string> GenerateBackendTokenAsync(string phoneNumber, Guid tenantId, Guid? companyId = null);

    /// <summary>
    /// Validate if user has access to tenant
    /// </summary>
    /// <param name="phoneNumber">User's phone number</param>
    /// <param name="tenantId">Tenant ID</param>
    /// <returns>True if authorized</returns>
    Task<bool> ValidateUserTenantAccessAsync(string phoneNumber, Guid tenantId);
}
