using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SayP.Application.Interfaces;
using SayP.Domain.Models;
using SayP.Domain.Interfaces;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SayP.Application.Services;

/// <summary>
/// Discovers API endpoints from backend systems using Swagger/OpenAPI and custom attributes
/// </summary>
public class ApiDiscoveryService : IApiDiscoveryService
{
    private readonly HttpClient _httpClient;
    private readonly ICacheService _cache;
    private readonly ILogger<ApiDiscoveryService> _logger;
    private const string CACHE_KEY_PREFIX = "api_discovery:";
    private static readonly TimeSpan DEFAULT_CACHE_DURATION = TimeSpan.FromHours(24);
    private JObject? _swaggerDoc; // Store Swagger document for $ref resolution

    public ApiDiscoveryService(
        HttpClient httpClient,
        ICacheService cache,
        ILogger<ApiDiscoveryService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    public async Task<List<DiscoveredEndpoint>> DiscoverEndpointsAsync(
        string baseUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Starting API discovery for {BaseUrl}", baseUrl);

            // Try Swagger first
            var swaggerUrl = $"{baseUrl.TrimEnd('/')}/swagger/v1/swagger.json";
            var endpoints = await DiscoverFromSwaggerAsync(swaggerUrl, cancellationToken);

            if (endpoints.Any())
            {
                _logger.LogInformation("Discovered {Count} endpoints from Swagger", endpoints.Count);
                return endpoints;
            }

            // Fallback: Try custom discovery endpoint
            _logger.LogWarning("Swagger not found, trying custom discovery endpoint");
            return await DiscoverFromCustomEndpointAsync(baseUrl, apiKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error discovering endpoints from {BaseUrl}", baseUrl);
            return new List<DiscoveredEndpoint>();
        }
    }

    public async Task<List<DiscoveredEndpoint>> DiscoverFromSwaggerAsync(
        string swaggerUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Fetching Swagger spec from {SwaggerUrl}", swaggerUrl);

            var response = await _httpClient.GetAsync(swaggerUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch Swagger spec: {StatusCode}", response.StatusCode);
                return new List<DiscoveredEndpoint>();
            }

            var swaggerJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var swaggerDoc = JObject.Parse(swaggerJson);
            
            // Store for $ref resolution
            _swaggerDoc = swaggerDoc;

            var endpoints = new List<DiscoveredEndpoint>();
            var paths = swaggerDoc["paths"] as JObject;

            if (paths == null)
            {
                _logger.LogWarning("No paths found in Swagger spec");
                return endpoints;
            }

            foreach (var path in paths.Properties())
            {
                var pathValue = path.Value as JObject;
                if (pathValue == null) continue;

                foreach (var method in pathValue.Properties())
                {
                    var operation = method.Value as JObject;
                    if (operation == null) continue;

                    // Check for SayP tag or x-sayp extension
                    var tags = operation["tags"]?.ToObject<List<string>>() ?? new List<string>();
                    var xSayp = operation["x-sayp"] as JObject;

                    if (!tags.Contains("SayP") && xSayp == null)
                        continue; // Skip non-SayP endpoints

                    var endpoint = ParseSwaggerOperation(path.Name, method.Name, operation, xSayp);
                    if (endpoint != null)
                    {
                        endpoints.Add(endpoint);
                        _logger.LogDebug("Discovered endpoint: {Intent} - {Route}", endpoint.Intent, endpoint.Route);
                    }
                }
            }

            _logger.LogInformation("Discovered {Count} SayP-enabled endpoints from Swagger", endpoints.Count);
            return endpoints;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Swagger spec from {SwaggerUrl}", swaggerUrl);
            return new List<DiscoveredEndpoint>();
        }
    }

    private DiscoveredEndpoint? ParseSwaggerOperation(
        string route,
        string httpMethod,
        JObject operation,
        JObject? xSayp)
    {
        try
        {
            // Extract SayP metadata
            var intent = xSayp?["intent"]?.ToString() 
                ?? GenerateIntentFromRoute(route, httpMethod);
            
            var description = operation["summary"]?.ToString() 
                ?? operation["description"]?.ToString() 
                ?? xSayp?["description"]?.ToString() 
                ?? "";

            var aliases = xSayp?["aliases"]?.ToObject<string[]>() ?? Array.Empty<string>();
            var priority = xSayp?["priority"]?.ToObject<int>() ?? 0;
            var requiresConfirmation = xSayp?["requiresConfirmation"]?.ToObject<bool>() ?? true;

            // Extract schema from requestBody
            EndpointSchema? schema = null;
            var requestBody = operation["requestBody"] as JObject;
            if (requestBody != null)
            {
                schema = ExtractSchemaFromRequestBody(requestBody);
                _logger.LogInformation("🔍 Schema extracted for {Intent}: HasSchema={HasSchema}, Fields={FieldCount}", 
                    intent, schema != null, schema?.Fields?.Count ?? 0);
            }
            else
            {
                _logger.LogWarning("⚠️ No requestBody found for {Intent}", intent);
            }

            // Extract controller and action names from operationId
            var operationId = operation["operationId"]?.ToString() ?? "";
            var (controllerName, actionName) = ParseOperationId(operationId);

            return new DiscoveredEndpoint
            {
                Intent = intent,
                Description = description,
                Aliases = aliases,
                HttpMethod = httpMethod.ToUpper(),
                Route = route,
                ControllerName = controllerName,
                ActionName = actionName,
                Priority = priority,
                RequiresConfirmation = requiresConfirmation,
                Schema = schema,
                DiscoveredAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Swagger operation for route {Route}", route);
            return null;
        }
    }

    private EndpointSchema? ExtractSchemaFromRequestBody(JObject requestBody)
    {
        try
        {
            var content = requestBody["content"] as JObject;
            var jsonContent = content?["application/json"] as JObject;
            var schemaRef = jsonContent?["schema"] as JObject;

            if (schemaRef == null) return null;

            // Check if schema is an array type
            bool isArray = false;
            string? arrayItemType = null;
            
            var schemaType = schemaRef["type"]?.ToString();
            _logger.LogInformation("🔍 Schema type detected: {SchemaType}", schemaType ?? "null");
            
            if (schemaType == "array")
            {
                isArray = true;
                var itemsRef = schemaRef["items"]?["$ref"]?.ToString();
                _logger.LogInformation("🔍 Array detected! Items ref: {ItemsRef}", itemsRef ?? "null");
                
                if (!string.IsNullOrEmpty(itemsRef))
                {
                    arrayItemType = itemsRef.Split('/').LastOrDefault();
                    // Get the actual item schema from components/schemas
                    var componentsSchemas = _swaggerDoc?["components"]?["schemas"] as JObject;
                    schemaRef = componentsSchemas?[arrayItemType] as JObject;
                    
                    if (schemaRef == null)
                    {
                        _logger.LogWarning("Array item schema {SchemaName} not found in components/schemas", arrayItemType);
                        return null;
                    }
                }
            }
            // Check if schema is a $ref (reference to components/schemas)
            else
            {
                var refPath = schemaRef["$ref"]?.ToString();
                if (!string.IsNullOrEmpty(refPath))
                {
                    // Extract schema name from $ref (e.g., "#/components/schemas/CreateCodeTemplateDto")
                    var schemaName = refPath.Split('/').LastOrDefault();
                    if (string.IsNullOrEmpty(schemaName))
                    {
                        _logger.LogWarning("Could not extract schema name from $ref: {RefPath}", refPath);
                        return null;
                    }

                    // Get the schema from components/schemas
                    var componentsSchemas = _swaggerDoc?["components"]?["schemas"] as JObject;
                    schemaRef = componentsSchemas?[schemaName] as JObject;
                    
                    if (schemaRef == null)
                    {
                        _logger.LogWarning("Schema {SchemaName} not found in components/schemas", schemaName);
                        return null;
                    }
                }
            }

            var typeName = schemaRef["title"]?.ToString() ?? "Unknown";
            var properties = schemaRef["properties"] as JObject;
            var required = schemaRef["required"]?.ToObject<List<string>>() ?? new List<string>();

            if (properties == null)
            {
                _logger.LogWarning("No properties found in schema {TypeName}", typeName);
                return null;
            }

            var fields = new List<SchemaField>();
            foreach (var prop in properties.Properties())
            {
                var propSchema = prop.Value as JObject;
                if (propSchema == null) continue;

                var field = new SchemaField
                {
                    Name = prop.Name,
                    Type = propSchema["type"]?.ToString() ?? "string",
                    Description = propSchema["description"]?.ToString() ?? "",
                    Example = propSchema["example"]?.ToString() ?? "",
                    IsRequired = required.Contains(prop.Name),
                    IsOptional = !required.Contains(prop.Name)
                };

                // Check for x-sayp-field extension
                var xSaypField = propSchema["x-sayp-field"] as JObject;
                if (xSaypField != null)
                {
                    field.Aliases = xSaypField["aliases"]?.ToObject<string[]>() ?? Array.Empty<string>();
                    field.ValidationPattern = xSaypField["validationPattern"]?.ToString();
                    field.ValidationMessage = xSaypField["validationMessage"]?.ToString();
                }

                // ✅ NEW: Check for x-track-field extension
                var xTrackField = propSchema["x-track-field"] as JObject;
                if (xTrackField != null)
                {
                    field.IsTrackField = true;
                    field.TrackFieldPriority = xTrackField["priority"]?.ToObject<int>() ?? 5;
                    field.UsesFuzzyMatching = xTrackField["useFuzzyMatching"]?.ToObject<bool>() ?? true;
                    field.MinTrackConfidence = xTrackField["minConfidence"]?.ToObject<double>() ?? 0.7;
                    field.TrackAliases = xTrackField["searchAliases"]?.ToObject<string[]>();
                    field.TrackCaseSensitive = xTrackField["caseSensitive"]?.ToObject<bool>() ?? false;
                    field.AllowPartialMatch = xTrackField["allowPartialMatch"]?.ToObject<bool>() ?? true;
                    field.MinPartialMatchLength = xTrackField["minPartialMatchLength"]?.ToObject<int>() ?? 3;
                }

                fields.Add(field);
            }

            return new EndpointSchema
            {
                TypeName = typeName,
                Fields = fields,
                IsArray = isArray,
                ArrayItemType = arrayItemType
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting schema from request body");
            return null;
        }
    }

    private string GenerateIntentFromRoute(string route, string httpMethod)
    {
        // Convert route to intent name
        // Example: /api/code-templates -> create_code_template (for POST)
        var parts = route.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => !p.StartsWith("{") && p != "api")
            .ToList();

        var action = httpMethod.ToLower() switch
        {
            "post" => "create",
            "put" => "update",
            "delete" => "delete",
            "get" => "get",
            _ => "action"
        };

        var resource = string.Join("_", parts).Replace("-", "_");
        return $"{action}_{resource}";
    }

    private (string controller, string action) ParseOperationId(string operationId)
    {
        // Example: "CodeTemplates_Create" -> ("CodeTemplates", "Create")
        var parts = operationId.Split('_');
        if (parts.Length >= 2)
        {
            return (parts[0], parts[1]);
        }
        return ("Unknown", "Unknown");
    }

    private async Task<List<DiscoveredEndpoint>> DiscoverFromCustomEndpointAsync(
        string baseUrl,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        try
        {
            // Call custom discovery endpoint: GET /api/sayp/discovery
            var discoveryUrl = $"{baseUrl.TrimEnd('/')}/api/sayp/discovery";
            
            var request = new HttpRequestMessage(HttpMethod.Get, discoveryUrl);
            if (!string.IsNullOrEmpty(apiKey))
            {
                request.Headers.Add("X-SayP-Api-Key", apiKey);
            }

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Custom discovery endpoint not found or failed");
                return new List<DiscoveredEndpoint>();
            }

            var endpoints = await response.Content.ReadFromJsonAsync<List<DiscoveredEndpoint>>(cancellationToken);
            return endpoints ?? new List<DiscoveredEndpoint>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling custom discovery endpoint");
            return new List<DiscoveredEndpoint>();
        }
    }

    public async Task<EndpointSchema?> GetEndpointSchemaAsync(
        string baseUrl,
        DiscoveredEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        // Schema is already extracted during discovery
        if (endpoint.Schema != null)
            return endpoint.Schema;

        // Try to fetch schema separately if needed
        try
        {
            var schemaUrl = $"{baseUrl.TrimEnd('/')}/api/sayp/schema/{endpoint.Intent}";
            var response = await _httpClient.GetAsync(schemaUrl, cancellationToken);
            
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<EndpointSchema>(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching schema for endpoint {Intent}", endpoint.Intent);
        }

        return null;
    }

    public async Task CacheDiscoveryAsync(
        Guid tenantId,
        List<DiscoveredEndpoint> endpoints,
        TimeSpan? expiration = null)
    {
        try
        {
            var cacheKey = $"{CACHE_KEY_PREFIX}{tenantId}";
            var duration = expiration ?? DEFAULT_CACHE_DURATION;
            
            await _cache.DeleteAsync(cacheKey);
            await _cache.SetAsync(cacheKey, endpoints, duration);
            _logger.LogInformation("Cached {Count} endpoints for tenant {TenantId}", endpoints.Count, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error caching discovery for tenant {TenantId}", tenantId);
        }
    }

    public async Task<List<DiscoveredEndpoint>?> GetCachedEndpointsAsync(Guid tenantId)
    {
        try
        {
            var cacheKey = $"{CACHE_KEY_PREFIX}{tenantId}";
            var cached = await _cache.GetAsync<List<DiscoveredEndpoint>>(cacheKey);
            
            if (cached != null)
            {
                _logger.LogDebug("Retrieved {Count} cached endpoints for tenant {TenantId}", cached.Count, tenantId);
            }
            
            return cached;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving cached endpoints for tenant {TenantId}", tenantId);
            return null;
        }
    }

    public async Task RefreshDiscoveryAsync(
        Guid tenantId,
        string baseUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Refreshing discovery cache for tenant {TenantId}", tenantId);
            
            var endpoints = await DiscoverEndpointsAsync(baseUrl, apiKey, cancellationToken);
            await CacheDiscoveryAsync(tenantId, endpoints);
            
            _logger.LogInformation("Discovery cache refreshed for tenant {TenantId}: {Count} endpoints", 
                tenantId, endpoints.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing discovery for tenant {TenantId}", tenantId);
        }
    }

    /// <summary>
    /// ✅ NEW: Invalidate discovery cache for a tenant (webhook support)
    /// </summary>
    public async Task InvalidateCacheAsync(Guid tenantId)
    {
        try
        {
            var cacheKey = $"{CACHE_KEY_PREFIX}{tenantId}";
            await _cache.DeleteAsync(cacheKey);
            
            _logger.LogInformation("Discovery cache invalidated for tenant {TenantId}", tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error invalidating cache for tenant {TenantId}", tenantId);
            throw;
        }
    }
}
