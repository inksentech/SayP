using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Infrastructure.Persistence;
using System.Text.Json;

namespace SayP.Infrastructure.Services;

/// <summary>
/// Service for communicating with the main backend to validate users and tenants
/// Phone validation is done locally from SayP's TenantMappings table
/// </summary>
public class BackendUserService : IBackendUserService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackendUserService> _logger;
    private readonly SayPDbContext _dbContext;
    private readonly string _backendBaseUrl;
    private string? _currentTenantId;

    public BackendUserService(
        HttpClient httpClient, 
        IConfiguration configuration, 
        ILogger<BackendUserService> logger,
        SayPDbContext dbContext)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        _dbContext = dbContext;
        
        _backendBaseUrl = _configuration["Backend:BaseUrl"] 
            ?? Environment.GetEnvironmentVariable("BACKEND_BASE_URL") 
            ?? Environment.GetEnvironmentVariable("BACKEND_API_URL")
            ?? "http://localhost:5245";
            
        _logger.LogInformation("BackendUserService initialized with base URL: {BaseUrl}", _backendBaseUrl);
    }

    /// <summary>
    /// Validate phone number from SayP's local TenantMappings table
    /// No need to call backend - SayP is the source of truth for phone-tenant mapping
    /// </summary>
    public async Task<UserAuthInfo?> ValidatePhoneNumberAsync(string phoneNumber)
    {
        try
        {
            _logger.LogInformation("Validating phone number from SayP DB: {PhoneNumber}", phoneNumber);
            
            // Normalize phone number
            var normalizedPhone = phoneNumber.Replace("+", "").Replace(" ", "").Trim();
            
            // Find in TenantMappings
            var mapping = await _dbContext.TenantMappings
                .Where(tm => tm.IsActive && 
                            (tm.PhoneNumber == normalizedPhone || 
                             tm.PhoneNumber == phoneNumber))
                .FirstOrDefaultAsync();

            if (mapping == null)
            {
                _logger.LogWarning("Phone number {PhoneNumber} not found in SayP TenantMappings", phoneNumber);
                return null;
            }

            // Get user profile if exists
            var userProfile = await _dbContext.UserProfiles
                .Where(up => up.PhoneNumber == normalizedPhone || up.PhoneNumber == phoneNumber)
                .FirstOrDefaultAsync();

            _logger.LogInformation("Phone {PhoneNumber} validated successfully. Tenant: {TenantId}, Company: {CompanyId}", 
                phoneNumber, mapping.TenantId, mapping.CompanyId);

            // Store tenant ID for subsequent requests
            _currentTenantId = mapping.TenantId.ToString();

            return new UserAuthInfo
            {
                UserId = Guid.Empty, // Not needed for WhatsApp - we use phone number
                TenantId = mapping.TenantId,
                DefaultCompanyId = mapping.CompanyId,
                PhoneNumber = normalizedPhone,
                FirstName = userProfile?.UserName ?? "WhatsApp User",
                LastName = "",
                IsAuthorized = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating phone number {PhoneNumber}", phoneNumber);
            return null;
        }
    }

    // Product operations
    public Task<IEnumerable<ProductDto>?> SearchProductsAsync(string searchTerm, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        // Not implemented yet - will be added when needed
        _logger.LogWarning("SearchProductsAsync not implemented");
        return Task.FromResult<IEnumerable<ProductDto>?>(null);
    }

    public Task<IEnumerable<ProductDto>?> GetAllProductsAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        // Not implemented yet - will be added when needed
        _logger.LogWarning("GetAllProductsAsync not implemented");
        return Task.FromResult<IEnumerable<ProductDto>?>(null);
    }

    public Task<ProductDto?> GetProductByIdAsync(Guid productId, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        // Not implemented yet - will be added when needed
        _logger.LogWarning("GetProductByIdAsync not implemented");
        return Task.FromResult<ProductDto?>(null);
    }

    // Customer operations
    public Task<IEnumerable<CustomerDto>?> SearchCustomersAsync(string searchTerm, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        // Not implemented yet - will be added when needed
        _logger.LogWarning("SearchCustomersAsync not implemented");
        return Task.FromResult<IEnumerable<CustomerDto>?>(null);
    }

    public Task<IEnumerable<CustomerDto>?> GetAllCustomersAsync(Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        // Not implemented yet - will be added when needed
        _logger.LogWarning("GetAllCustomersAsync not implemented");
        return Task.FromResult<IEnumerable<CustomerDto>?>(null);
    }

    public Task<CustomerDto?> GetCustomerByIdAsync(Guid customerId, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        // Not implemented yet - will be added when needed
        _logger.LogWarning("GetCustomerByIdAsync not implemented");
        return Task.FromResult<CustomerDto?>(null);
    }

    public Task<CustomerDto?> GetCustomerByPhoneAsync(string phone, Guid tenantId, Guid? companyId, CancellationToken cancellationToken = default)
    {
        // Not implemented yet - will be added when needed
        _logger.LogWarning("GetCustomerByPhoneAsync not implemented");
        return Task.FromResult<CustomerDto?>(null);
    }

    private void AddSayPAuthHeaders(HttpRequestMessage request, string? tenantId = null)
    {
        // Add SayP API Key
        var apiKey = _configuration["SayP:ApiKey"] 
            ?? Environment.GetEnvironmentVariable("SAYP_API_KEY")
            ?? "SayP-Secret-Key-2024"; // Default key for development

        request.Headers.Add("X-SayP-Api-Key", apiKey);

        // Add tenant ID if provided
        if (!string.IsNullOrEmpty(tenantId))
        {
            request.Headers.Add("X-Tenant-Id", tenantId);
        }

        _logger.LogInformation("Added SayP auth headers - API Key: {ApiKey}, Tenant: {TenantId}", 
            apiKey?.Substring(0, Math.Min(10, apiKey.Length)) + "...", tenantId ?? "null");
    }
}
