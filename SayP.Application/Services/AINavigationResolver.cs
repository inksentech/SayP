using Microsoft.Extensions.Logging;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace SayP.Application.Services;

/// <summary>
/// AI-driven navigation property resolver
/// Resolves entity names to IDs (e.g., "Adl" → BrandId: 42)
/// </summary>
public class AINavigationResolver
{
    private readonly IAIProvider _aiProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<AINavigationResolver> _logger;

    public AINavigationResolver(
        IAIProvider aiProvider,
        IHttpClientFactory httpClientFactory,
        ILogger<AINavigationResolver> logger)
    {
        _aiProvider = aiProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Analyze extracted parameters and resolve navigation properties
    /// </summary>
    public async Task<Dictionary<string, object>> ResolveNavigationPropertiesAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> extractedParameters,
        string backendUrl,
        string? apiKey,
        Guid? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (extractedParameters.Count == 0)
            return extractedParameters;

        try
        {
            // 1. Ask AI which parameters need resolution
            var resolutions = await AnalyzeParametersForResolutionAsync(
                endpoint, 
                extractedParameters, 
                cancellationToken);

            if (resolutions.Count == 0)
            {
                _logger.LogDebug("No navigation properties to resolve for {Intent}", endpoint.Intent);
                return extractedParameters;
            }

            _logger.LogInformation("Found {Count} navigation properties to resolve", resolutions.Count);

            // 2. Resolve each navigation property
            var httpClient = _httpClientFactory.CreateClient("BackendApi");
            
            // Add SayP API Key for authentication (bypasses IdentityServer)
            var effectiveApiKey = apiKey ?? Environment.GetEnvironmentVariable("SAYP_API_KEY");
            if (!string.IsNullOrEmpty(effectiveApiKey))
            {
                httpClient.DefaultRequestHeaders.Add("X-SayP-Api-Key", effectiveApiKey);
                _logger.LogDebug("Added X-SayP-Api-Key header for navigation resolution");
            }
            
            // Add Tenant ID header (required for multi-tenant backends like Pimland)
            if (tenantId.HasValue && tenantId.Value != Guid.Empty)
            {
                httpClient.DefaultRequestHeaders.Add("X-Tenant-Id", tenantId.Value.ToString());
                _logger.LogDebug("Added X-Tenant-Id header: {TenantId}", tenantId.Value);
            }

            foreach (var resolution in resolutions)
            {
                try
                {
                    var resolvedId = await ResolveEntityAsync(
                        httpClient,
                        backendUrl,
                        resolution,
                        cancellationToken);

                    if (resolvedId != null)
                    {
                        // Set the ID field
                        extractedParameters[resolution.TargetField] = resolvedId;
                        
                        // Remove the original string parameter if different
                        if (resolution.ParamName != resolution.TargetField)
                        {
                            extractedParameters.Remove(resolution.ParamName);
                        }

                        _logger.LogInformation(
                            "✅ Resolved {Entity}: '{Name}' → {TargetField}={Id}",
                            resolution.EntityType, resolution.SearchValue, resolution.TargetField, resolvedId);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "⚠️ Could not resolve {Entity}: '{Name}'",
                            resolution.EntityType, resolution.SearchValue);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Failed to resolve {Entity}={Value}",
                        resolution.EntityType, resolution.SearchValue);
                }
            }

            return extractedParameters;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in navigation property resolution");
            return extractedParameters;
        }
    }

    /// <summary>
    /// Ask AI to analyze which parameters need resolution
    /// </summary>
    private async Task<List<NavigationResolution>> AnalyzeParametersForResolutionAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> extractedParameters,
        CancellationToken cancellationToken)
    {
        var schemaFields = endpoint.Schema?.Fields?
            .Select(f => new { f.Name, f.Type, f.Description })
            .ToList();

        var prompt = $@"Analyze these API parameters and identify which ones are entity NAMES that need to be resolved to IDs.

Endpoint: {endpoint.Intent} - {endpoint.Description}

Schema Fields:
{JsonSerializer.Serialize(schemaFields, new JsonSerializerOptions { WriteIndented = true })}

Extracted Parameters:
{JsonSerializer.Serialize(extractedParameters, new JsonSerializerOptions { WriteIndented = true })}

RULES:
1. If a parameter value is a STRING that looks like an entity NAME (not an ID), it needs resolution
2. Common patterns:
   - ""brand"": ""Adl"" → needs resolution to BrandId
   - ""season"": ""2024 Yaz"" → needs resolution to SeasonId
   - ""brandId"": 42 → already an ID, no resolution needed
   - ""name"": ""Ürün Adı"" → this is the entity's own name, not a reference
3. Look for fields ending with ""Id"" in schema - their corresponding name values need resolution
4. Entity types: Brand, Season, ProductGroup, Designer, Supplier, ProductTheme, etc.

Respond with JSON only:
{{
  ""resolutions"": [
    {{
      ""paramName"": ""brand"",
      ""entityType"": ""Brand"",
      ""searchValue"": ""Adl"",
      ""targetField"": ""BrandId""
    }}
  ]
}}

If no resolution needed, return: {{ ""resolutions"": [] }}";

        var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);

        try
        {
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(response, @"\{[\s\S]*\}");
            if (jsonMatch.Success)
            {
                var json = JsonSerializer.Deserialize<JsonElement>(jsonMatch.Value);
                if (json.TryGetProperty("resolutions", out var resolutionsArray))
                {
                    var resolutions = new List<NavigationResolution>();
                    foreach (var item in resolutionsArray.EnumerateArray())
                    {
                        resolutions.Add(new NavigationResolution
                        {
                            ParamName = item.GetProperty("paramName").GetString() ?? "",
                            EntityType = item.GetProperty("entityType").GetString() ?? "",
                            SearchValue = item.GetProperty("searchValue").GetString() ?? "",
                            TargetField = item.GetProperty("targetField").GetString() ?? ""
                        });
                    }
                    return resolutions;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI resolution analysis");
        }

        return new List<NavigationResolution>();
    }

    /// <summary>
    /// Resolve a single entity by searching the backend
    /// </summary>
    private async Task<object?> ResolveEntityAsync(
        HttpClient httpClient,
        string backendUrl,
        NavigationResolution resolution,
        CancellationToken cancellationToken)
    {
        // Try multiple search endpoint patterns
        var searchEndpoints = GetSearchEndpoints(resolution.EntityType);

        foreach (var (endpoint, method, useBody) in searchEndpoints)
        {
            try
            {
                var searchUrl = $"{backendUrl.TrimEnd('/')}{endpoint}";
                HttpResponseMessage response;

                if (method == "POST" && useBody)
                {
                    // Pimland style: POST with JSON body containing search criteria
                    var searchBody = new Dictionary<string, object>
                    {
                        { "name", resolution.SearchValue }
                    };
                    var jsonContent = new StringContent(
                        JsonSerializer.Serialize(searchBody),
                        System.Text.Encoding.UTF8,
                        "application/json");
                    
                    _logger.LogDebug("Trying POST search: {Url} with body", searchUrl);
                    response = await httpClient.PostAsync(searchUrl, jsonContent, cancellationToken);
                }
                else if (method == "POST")
                {
                    // POST without body (get_all style)
                    _logger.LogDebug("Trying POST: {Url}", searchUrl);
                    response = await httpClient.PostAsync(searchUrl, null, cancellationToken);
                }
                else
                {
                    // GET with query parameter
                    var urlWithQuery = searchUrl.Contains("?")
                        ? $"{searchUrl}&search={Uri.EscapeDataString(resolution.SearchValue)}"
                        : $"{searchUrl}?search={Uri.EscapeDataString(resolution.SearchValue)}";

                    _logger.LogDebug("Trying GET search: {Url}", urlWithQuery);
                    response = await httpClient.GetAsync(urlWithQuery, cancellationToken);
                    
                    if (!response.IsSuccessStatusCode)
                    {
                        // Try with name parameter
                        urlWithQuery = searchUrl.Contains("?")
                            ? $"{searchUrl}&name={Uri.EscapeDataString(resolution.SearchValue)}"
                            : $"{searchUrl}?name={Uri.EscapeDataString(resolution.SearchValue)}";
                        
                        response = await httpClient.GetAsync(urlWithQuery, cancellationToken);
                    }
                }

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    _logger.LogDebug("Search response: {Content}", content.Length > 500 ? content.Substring(0, 500) : content);
                    
                    var results = JsonSerializer.Deserialize<JsonElement>(content);
                    var id = ExtractIdFromResponse(results, resolution.SearchValue);
                    
                    if (id != null)
                    {
                        _logger.LogInformation("✅ Found {Entity} ID: {Id} for '{Name}'", 
                            resolution.EntityType, id, resolution.SearchValue);
                        return id;
                    }
                }
                else
                {
                    _logger.LogDebug("Search endpoint {Endpoint} returned {StatusCode}", endpoint, response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Search endpoint {Endpoint} failed", endpoint);
            }
        }

        // If exact match fails, try fuzzy search with AI
        return await TryFuzzyMatchAsync(httpClient, backendUrl, resolution, cancellationToken);
    }

    /// <summary>
    /// Get possible search endpoints for an entity type
    /// Returns tuples of (endpoint, httpMethod, useBodyForSearch)
    /// </summary>
    private List<(string Endpoint, string Method, bool UseBody)> GetSearchEndpoints(string entityType)
    {
        var baseName = entityType.ToLower();
        var pluralName = GetPluralName(baseName);

        return new List<(string, string, bool)>
        {
            // Pimland uses POST endpoints with body for search/filter
            ($"/api/{baseName}/filter", "POST", true),        // /api/brand/filter (POST with body)
            ($"/api/{baseName}/search_by_id", "POST", true),  // /api/brand/search_by_id (POST with body)
            ($"/api/{baseName}/get_all", "POST", false),      // /api/brand/get_all (POST, then filter client-side)
            // Standard REST patterns (GET)
            ($"/api/{baseName}", "GET", false),               // /api/brand (GET)
            ($"/api/{pluralName}", "GET", false),
            ($"/api/{pluralName}/search", "GET", false),
            ($"/api/{baseName}/search", "GET", false),
            ($"/api/{baseName}s", "GET", false),
            // Additional patterns
            ($"/api/{pluralName}/getall", "GET", false),
            ($"/api/{pluralName}/list", "GET", false)
        };
    }
    
    /// <summary>
    /// Extract ID from response, searching for matching entity by name
    /// </summary>
    private object? ExtractIdFromResponse(JsonElement results, string searchValue)
    {
        // Handle array response
        if (results.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in results.EnumerateArray())
            {
                var id = FindMatchingEntityId(item, searchValue);
                if (id != null) return id;
            }
            // If no exact match, return first item's ID
            if (results.GetArrayLength() > 0)
            {
                return ExtractIdFromResult(results[0]);
            }
        }
        // Handle object response (single result or paginated)
        else if (results.ValueKind == JsonValueKind.Object)
        {
            // Check if it's a paginated response
            if (results.TryGetProperty("data", out var dataArray) && 
                dataArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in dataArray.EnumerateArray())
                {
                    var id = FindMatchingEntityId(item, searchValue);
                    if (id != null) return id;
                }
                if (dataArray.GetArrayLength() > 0)
                    return ExtractIdFromResult(dataArray[0]);
            }
            else if (results.TryGetProperty("items", out var itemsArray) && 
                     itemsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsArray.EnumerateArray())
                {
                    var id = FindMatchingEntityId(item, searchValue);
                    if (id != null) return id;
                }
                if (itemsArray.GetArrayLength() > 0)
                    return ExtractIdFromResult(itemsArray[0]);
            }
            // Direct object result
            else if (results.TryGetProperty("id", out _) || results.TryGetProperty("Id", out _))
            {
                return ExtractIdFromResult(results);
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// Find entity matching the search value and return its ID
    /// </summary>
    private object? FindMatchingEntityId(JsonElement item, string searchValue)
    {
        // Check common name fields
        string[] nameFields = { "name", "Name", "title", "Title", "description", "Description" };
        
        foreach (var field in nameFields)
        {
            if (item.TryGetProperty(field, out var nameValue) && 
                nameValue.ValueKind == JsonValueKind.String)
            {
                var name = nameValue.GetString();
                if (name != null && name.Equals(searchValue, StringComparison.OrdinalIgnoreCase))
                {
                    return ExtractIdFromResult(item);
                }
                // Also check if name contains the search value (partial match)
                if (name != null && name.Contains(searchValue, StringComparison.OrdinalIgnoreCase))
                {
                    return ExtractIdFromResult(item);
                }
            }
        }
        
        return null;
    }

    /// <summary>
    /// Get plural form of entity name
    /// </summary>
    private string GetPluralName(string name)
    {
        if (name.EndsWith("y"))
            return name[..^1] + "ies";
        if (name.EndsWith("s") || name.EndsWith("x") || name.EndsWith("ch") || name.EndsWith("sh"))
            return name + "es";
        return name + "s";
    }

    /// <summary>
    /// Extract ID from a JSON result
    /// </summary>
    private object? ExtractIdFromResult(JsonElement element)
    {
        // Try common ID field names
        string[] idFields = { "id", "Id", "ID", "entityId", "EntityId" };

        foreach (var field in idFields)
        {
            if (element.TryGetProperty(field, out var idValue))
            {
                return idValue.ValueKind switch
                {
                    JsonValueKind.Number => idValue.TryGetInt64(out var longVal) ? longVal : idValue.GetDouble(),
                    JsonValueKind.String => idValue.GetString(),
                    _ => null
                };
            }
        }

        return null;
    }

    /// <summary>
    /// Try fuzzy matching when exact match fails
    /// </summary>
    private async Task<object?> TryFuzzyMatchAsync(
        HttpClient httpClient,
        string backendUrl,
        NavigationResolution resolution,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Trying fuzzy match for {Entity}={Value}", 
            resolution.EntityType, resolution.SearchValue);

        // Get all entities and let AI find the best match
        var searchEndpoints = GetSearchEndpoints(resolution.EntityType);

        foreach (var endpoint in searchEndpoints)
        {
            try
            {
                var url = $"{backendUrl.TrimEnd('/')}{endpoint}";
                var response = await httpClient.GetAsync(url, cancellationToken);

                if (!response.IsSuccessStatusCode)
                    continue;

                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var results = JsonSerializer.Deserialize<JsonElement>(content);

                // Extract items from response
                var items = ExtractItemsFromResponse(results);
                if (items.Count == 0)
                    continue;

                // Limit items for AI analysis
                var limitedItems = items.Take(50).ToList();

                // Ask AI to find best match
                var prompt = $@"Find the best matching item for ""{resolution.SearchValue}"" from this list.
Consider typos, case differences, and partial matches.

Items:
{JsonSerializer.Serialize(limitedItems, new JsonSerializerOptions { WriteIndented = true })}

Respond with JSON only:
{{
  ""found"": true,
  ""matchedId"": 42,
  ""matchedName"": ""ADL"",
  ""confidence"": 0.95
}}

Or if no good match: {{ ""found"": false }}";

                var aiResponse = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
                
                var jsonMatch = System.Text.RegularExpressions.Regex.Match(aiResponse, @"\{[\s\S]*\}");
                if (jsonMatch.Success)
                {
                    var json = JsonSerializer.Deserialize<JsonElement>(jsonMatch.Value);
                    if (json.TryGetProperty("found", out var found) && found.GetBoolean())
                    {
                        if (json.TryGetProperty("matchedId", out var matchedId))
                        {
                            var confidence = json.TryGetProperty("confidence", out var conf) 
                                ? conf.GetDouble() 
                                : 0.8;

                            if (confidence >= 0.7)
                            {
                                _logger.LogInformation(
                                    "🔍 Fuzzy matched '{Search}' → '{Matched}' (confidence: {Confidence:P0})",
                                    resolution.SearchValue,
                                    json.TryGetProperty("matchedName", out var name) ? name.GetString() : "?",
                                    confidence);

                                return matchedId.ValueKind == JsonValueKind.Number
                                    ? matchedId.GetInt64()
                                    : matchedId.GetString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Fuzzy match attempt failed for endpoint {Endpoint}", endpoint);
            }
        }

        return null;
    }

    /// <summary>
    /// Extract items array from various response formats
    /// </summary>
    private List<JsonElement> ExtractItemsFromResponse(JsonElement response)
    {
        var items = new List<JsonElement>();

        if (response.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in response.EnumerateArray())
            {
                items.Add(item);
            }
        }
        else if (response.ValueKind == JsonValueKind.Object)
        {
            // Try common wrapper properties
            string[] wrapperProps = { "data", "items", "results", "value", "content" };
            foreach (var prop in wrapperProps)
            {
                if (response.TryGetProperty(prop, out var array) && array.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in array.EnumerateArray())
                    {
                        items.Add(item);
                    }
                    break;
                }
            }
        }

        return items;
    }
}

/// <summary>
/// Represents a navigation property that needs resolution
/// </summary>
public class NavigationResolution
{
    /// <summary>
    /// Original parameter name (e.g., "brand")
    /// </summary>
    public string ParamName { get; set; } = string.Empty;

    /// <summary>
    /// Entity type to search (e.g., "Brand")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Value to search for (e.g., "Adl")
    /// </summary>
    public string SearchValue { get; set; } = string.Empty;

    /// <summary>
    /// Target field to set with resolved ID (e.g., "BrandId")
    /// </summary>
    public string TargetField { get; set; } = string.Empty;
}
