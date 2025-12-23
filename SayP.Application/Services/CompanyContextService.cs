using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace SayP.Application.Services;

/// <summary>
/// Service for managing company context and smart product type detection
/// </summary>
public class CompanyContextService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CompanyContextService> _logger;
    private readonly string _backendBaseUrl;

    public CompanyContextService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<CompanyContextService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _backendBaseUrl = _configuration["Backend:BaseUrl"]
            ?? Environment.GetEnvironmentVariable("BACKEND_BASE_URL")
            ?? Environment.GetEnvironmentVariable("BACKEND_API_URL")
            ?? "http://localhost:5245";
    }

    /// <summary>
    /// Get user's default company ID from UserCompanies table
    /// </summary>
    public async Task<Guid?> GetDefaultCompanyIdAsync(Guid userId, Guid tenantId)
    {
        try
        {
            _logger.LogInformation("Getting default company for user {UserId}", userId);

            var requestUrl = $"{_backendBaseUrl}/api/internal/user/{userId}/default-company";

            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            AddAuthHeaders(request, tenantId);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<DefaultCompanyResponse>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (result?.CompanyId != null)
                {
                    _logger.LogInformation("Default company found: {CompanyId}", result.CompanyId);
                    return result.CompanyId;
                }
            }
            else
            {
                _logger.LogWarning("Failed to get default company. Status: {StatusCode}", response.StatusCode);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting default company for user {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// Get company settings to determine if it's service-only
    /// </summary>
    public async Task<CompanySettings?> GetCompanySettingsAsync(Guid companyId, Guid tenantId)
    {
        try
        {
            _logger.LogInformation("Getting settings for company {CompanyId}", companyId);

            var requestUrl = $"{_backendBaseUrl}/api/internal/company/{companyId}/settings";

            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            AddAuthHeaders(request, tenantId);

            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                var settings = JsonSerializer.Deserialize<CompanySettings>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                _logger.LogInformation("Company settings loaded: IsServiceOnly={IsServiceOnly}", settings?.IsServiceOnly);
                return settings;
            }
            else
            {
                _logger.LogWarning("Failed to get company settings. Status: {StatusCode}", response.StatusCode);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting company settings for {CompanyId}", companyId);
            return null;
        }
    }

    /// <summary>
    /// Smart product type detection: If company is service-only, always create as service
    /// </summary>
    public async Task<string> DetermineProductTypeAsync(
        string userIntent,
        Guid companyId,
        Guid tenantId)
    {
        try
        {
            var settings = await GetCompanySettingsAsync(companyId, tenantId);

            // If company is service-only, always return "Service"
            if (settings?.IsServiceOnly == true)
            {
                _logger.LogInformation("Company is service-only, forcing product type to Service");
                return "Service";
            }

            // Otherwise, determine from user intent
            var lowerIntent = userIntent.ToLowerInvariant();
            if (lowerIntent.Contains("hizmet") || lowerIntent.Contains("service") ||
                lowerIntent.Contains("randevu") || lowerIntent.Contains("appointment"))
            {
                return "Service";
            }

            return "Physical";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error determining product type");
            return "Physical"; // Default fallback
        }
    }

    private void AddAuthHeaders(HttpRequestMessage request, Guid tenantId)
    {
        var apiKey = _configuration["SayP:ApiKey"]
            ?? Environment.GetEnvironmentVariable("SAYP_API_KEY")
            ?? "SayP-Secret-Key-2024";

        request.Headers.Add("X-SayP-Api-Key", apiKey);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
    }
}

public class DefaultCompanyResponse
{
    public Guid? CompanyId { get; set; }
    public string? CompanyName { get; set; }
}

public class CompanySettings
{
    public Guid CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsServiceOnly { get; set; }
    public string? BusinessType { get; set; }
}
