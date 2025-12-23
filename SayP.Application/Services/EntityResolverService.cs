using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SayP.Application.Interfaces;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;
using System.Text.RegularExpressions;

namespace SayP.Application.Services;

/// <summary>
/// Resolves entity references from natural language to actual entity IDs
/// Uses TrackField attributes to find entities by name, code, etc.
/// </summary>
public class EntityResolverService
{
    private readonly IGenericCommandExecutor _executor;
    private readonly IApiDiscoveryService _discoveryService;
    private readonly IAIProvider _aiProvider;
    private readonly ICacheService _cache;
    private readonly ILogger<EntityResolverService> _logger;

    private const string ENTITY_CACHE_PREFIX = "entity_list:";
    private static readonly TimeSpan ENTITY_CACHE_DURATION = TimeSpan.FromMinutes(5);

    public EntityResolverService(
        IGenericCommandExecutor executor,
        IApiDiscoveryService discoveryService,
        IAIProvider aiProvider,
        ICacheService cache,
        ILogger<EntityResolverService> logger)
    {
        _executor = executor;
        _discoveryService = discoveryService;
        _aiProvider = aiProvider;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Resolve entity from user input using TrackFields
    /// </summary>
    public async Task<EntityResolutionResult> ResolveEntityAsync(
        string userInput,
        DiscoveredEndpoint endpoint,
        Guid tenantId,
        string backendUrl,
        string? apiKey = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Resolving entity from input: '{Input}' for endpoint: {Intent}",
                userInput, endpoint.Intent);

            // 1. Get TrackFields
            var trackFields = endpoint.Schema?.Fields
                .Where(f => f.IsTrackField)
                .OrderByDescending(f => f.TrackFieldPriority)
                .ToList();

            if (trackFields == null || !trackFields.Any())
            {
                _logger.LogWarning("No TrackFields defined for endpoint {Intent}", endpoint.Intent);
                return new EntityResolutionResult
                {
                    Success = false,
                    ErrorMessage = "No track fields defined for this entity type"
                };
            }

            _logger.LogInformation("Found {Count} TrackFields: {Fields}",
                trackFields.Count,
                string.Join(", ", trackFields.Select(f => $"{f.Name} (Priority: {f.TrackFieldPriority})")));

            // 2. Find list endpoint
            var listEndpoint = await FindListEndpointAsync(endpoint, tenantId, cancellationToken);
            if (listEndpoint == null)
            {
                _logger.LogWarning("List endpoint not found for {Intent}", endpoint.Intent);
                return new EntityResolutionResult
                {
                    Success = false,
                    ErrorMessage = "Cannot search entities - list endpoint not available"
                };
            }

            // 3. Fetch all entities (with cache)
            var entities = await FetchEntitiesWithCacheAsync(
                listEndpoint,
                tenantId,
                backendUrl,
                apiKey,
                cancellationToken);

            if (entities == null || !entities.Any())
            {
                _logger.LogInformation("No entities found");
                return new EntityResolutionResult
                {
                    Success = false,
                    ErrorMessage = "No records found"
                };
            }

            _logger.LogInformation("Fetched {Count} entities for matching", entities.Count);

            // 4. Try matching with each TrackField (by priority)
            foreach (var trackField in trackFields)
            {
                _logger.LogDebug("Trying to match with TrackField: {Field} (Priority: {Priority})",
                    trackField.Name, trackField.TrackFieldPriority);

                var matches = await FindMatchesAsync(userInput, entities, trackField, cancellationToken);

                if (matches.Any())
                {
                    var bestMatch = matches.First();

                    if (bestMatch.Confidence >= trackField.MinTrackConfidence)
                    {
                        _logger.LogInformation(
                            "Entity resolved: {Name} (ID: {Id}, Confidence: {Confidence:P0}, Field: {Field})",
                            bestMatch.EntityName,
                            bestMatch.EntityId,
                            bestMatch.Confidence,
                            trackField.Name);

                        return new EntityResolutionResult
                        {
                            Success = true,
                            EntityId = bestMatch.EntityId,
                            EntityName = bestMatch.EntityName,
                            Confidence = bestMatch.Confidence,
                            MatchedField = trackField.Name,
                            AlternativeMatches = matches.Skip(1).Take(2).ToList()
                        };
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Best match confidence {Confidence:P0} below threshold {Threshold:P0}",
                            bestMatch.Confidence,
                            trackField.MinTrackConfidence);
                    }
                }
            }

            // 5. No good match found - try AI-powered resolution
            _logger.LogInformation("No direct match found, trying AI-powered resolution");
            return await ResolveWithAIAsync(userInput, entities, trackFields, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving entity from input: {Input}", userInput);
            return new EntityResolutionResult
            {
                Success = false,
                ErrorMessage = $"Error resolving entity: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Find list endpoint for the same entity type
    /// </summary>
    private async Task<DiscoveredEndpoint?> FindListEndpointAsync(
        DiscoveredEndpoint endpoint,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var allEndpoints = await _discoveryService.GetCachedEndpointsAsync(tenantId);
        if (allEndpoints == null) return null;

        // Extract entity name from intent
        // Example: "update_code_template" -> "code_template"
        var intentParts = endpoint.Intent.Split('_');
        var entityName = string.Join("_", intentParts.Skip(1));

        // Look for list endpoint
        var listIntent = $"list_{entityName}s"; // Try plural first
        var listEndpoint = allEndpoints.FirstOrDefault(e => e.Intent == listIntent);

        if (listEndpoint == null)
        {
            listIntent = $"list_{entityName}"; // Try singular
            listEndpoint = allEndpoints.FirstOrDefault(e => e.Intent == listIntent);
        }

        if (listEndpoint == null)
        {
            // Try alternative patterns
            listIntent = $"get_{entityName}s";
            listEndpoint = allEndpoints.FirstOrDefault(e => e.Intent == listIntent);
        }

        return listEndpoint;
    }

    /// <summary>
    /// Fetch entities with caching
    /// </summary>
    private async Task<List<JObject>?> FetchEntitiesWithCacheAsync(
        DiscoveredEndpoint listEndpoint,
        Guid tenantId,
        string backendUrl,
        string? apiKey,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"{ENTITY_CACHE_PREFIX}{listEndpoint.Intent}:{tenantId}";

        try
        {
            var cached = await _cache.GetAsync<List<JObject>>(cacheKey);
            if (cached != null)
            {
                _logger.LogDebug("Using cached entities for {Intent}", listEndpoint.Intent);
                return cached;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache read failed, fetching fresh data");
        }

        // Fetch from API
        var result = await _executor.ExecuteAsync(
            listEndpoint,
            new Dictionary<string, object>(), // No parameters for list
            tenantId,
            backendUrl,
            apiKey: apiKey,
            cancellationToken: cancellationToken);

        if (!result.Success || result.ParsedResponse == null)
        {
            _logger.LogWarning("Failed to fetch entities: {Error}", result.ErrorMessage);
            return null;
        }

        // Parse response
        var entities = new List<JObject>();
        var response = result.ParsedResponse;

        if (response is JArray array)
        {
            entities = array.OfType<JObject>().ToList();
        }
        else if (response is JObject obj)
        {
            // Check for common pagination patterns
            if (obj["data"] is JArray dataArray)
            {
                entities = dataArray.OfType<JObject>().ToList();
            }
            else if (obj["items"] is JArray itemsArray)
            {
                entities = itemsArray.OfType<JObject>().ToList();
            }
            else if (obj["results"] is JArray resultsArray)
            {
                entities = resultsArray.OfType<JObject>().ToList();
            }
            else
            {
                // Single entity response
                entities.Add(obj);
            }
        }

        // Cache the result
        try
        {
            await _cache.SetAsync(cacheKey, entities, ENTITY_CACHE_DURATION);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache entities");
        }

        return entities;
    }

    /// <summary>
    /// Find matches for user input against entities using a TrackField
    /// </summary>
    private async Task<List<EntityMatch>> FindMatchesAsync(
        string userInput,
        List<JObject> entities,
        SchemaField trackField,
        CancellationToken cancellationToken)
    {
        var matches = new List<EntityMatch>();
        var inputLower = trackField.TrackCaseSensitive ? userInput : userInput.ToLower();

        foreach (var entity in entities)
        {
            var fieldValue = GetFieldValue(entity, trackField.Name);
            if (string.IsNullOrEmpty(fieldValue)) continue;

            var valueLower = trackField.TrackCaseSensitive ? fieldValue : fieldValue.ToLower();
            double confidence = 0;

            // 1. Exact match
            if (string.Equals(inputLower, valueLower, StringComparison.Ordinal))
            {
                confidence = 1.0;
            }
            // 2. Partial match (if allowed)
            else if (trackField.AllowPartialMatch &&
                     inputLower.Length >= trackField.MinPartialMatchLength)
            {
                if (valueLower.Contains(inputLower))
                {
                    // Calculate confidence based on match length ratio
                    confidence = (double)inputLower.Length / valueLower.Length;
                    confidence = Math.Min(0.95, confidence); // Cap at 95% for partial matches
                }
                else if (inputLower.Contains(valueLower))
                {
                    confidence = (double)valueLower.Length / inputLower.Length * 0.9;
                }
            }

            // 3. Fuzzy matching (if enabled)
            if (confidence < 0.5 && trackField.UsesFuzzyMatching)
            {
                confidence = CalculateFuzzyScore(inputLower, valueLower);
            }

            // 4. Check aliases
            if (confidence < 0.5 && trackField.TrackAliases != null)
            {
                foreach (var alias in trackField.TrackAliases)
                {
                    var aliasLower = trackField.TrackCaseSensitive ? alias : alias.ToLower();
                    if (inputLower.Contains(aliasLower) || aliasLower.Contains(inputLower))
                    {
                        confidence = Math.Max(confidence, 0.8);
                        break;
                    }
                }
            }

            if (confidence > 0)
            {
                var entityId = GetFieldValue(entity, "id") ?? GetFieldValue(entity, "Id");
                if (!string.IsNullOrEmpty(entityId))
                {
                    matches.Add(new EntityMatch
                    {
                        EntityId = entityId,
                        EntityName = fieldValue,
                        Confidence = confidence,
                        Entity = entity
                    });
                }
            }
        }

        return matches.OrderByDescending(m => m.Confidence).ToList();
    }

    /// <summary>
    /// Calculate fuzzy matching score using Levenshtein distance
    /// </summary>
    private double CalculateFuzzyScore(string input, string target)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(target))
            return 0;

        var distance = LevenshteinDistance(input, target);
        var maxLength = Math.Max(input.Length, target.Length);
        var similarity = 1.0 - ((double)distance / maxLength);

        return Math.Max(0, similarity);
    }

    /// <summary>
    /// Calculate Levenshtein distance between two strings
    /// </summary>
    private int LevenshteinDistance(string source, string target)
    {
        if (string.IsNullOrEmpty(source))
            return target?.Length ?? 0;

        if (string.IsNullOrEmpty(target))
            return source.Length;

        var sourceLength = source.Length;
        var targetLength = target.Length;
        var distance = new int[sourceLength + 1, targetLength + 1];

        for (int i = 0; i <= sourceLength; distance[i, 0] = i++) ;
        for (int j = 0; j <= targetLength; distance[0, j] = j++) ;

        for (int i = 1; i <= sourceLength; i++)
        {
            for (int j = 1; j <= targetLength; j++)
            {
                var cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                distance[i, j] = Math.Min(
                    Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                    distance[i - 1, j - 1] + cost);
            }
        }

        return distance[sourceLength, targetLength];
    }

    /// <summary>
    /// AI-powered entity resolution when fuzzy matching fails
    /// </summary>
    private async Task<EntityResolutionResult> ResolveWithAIAsync(
        string userInput,
        List<JObject> entities,
        List<SchemaField> trackFields,
        CancellationToken cancellationToken)
    {
        try
        {
            // Build entity list for AI
            var entityList = entities.Take(20).Select(e => // Limit to 20 for AI context
            {
                var dict = new Dictionary<string, string>();
                foreach (var trackField in trackFields)
                {
                    var value = GetFieldValue(e, trackField.Name);
                    if (!string.IsNullOrEmpty(value))
                    {
                        dict[trackField.Name] = value;
                    }
                }
                dict["Id"] = GetFieldValue(e, "id") ?? GetFieldValue(e, "Id") ?? "";
                return dict;
            }).ToList();

            var entitiesJson = System.Text.Json.JsonSerializer.Serialize(entityList);

            var prompt = $@"User is trying to reference an entity with: ""{userInput}""

Available entities:
{entitiesJson}

Which entity is the user most likely referring to? Consider typos, abbreviations, and partial matches.

Respond with JSON:
{{
  ""entityId"": ""the ID of the matched entity"",
  ""confidence"": 0.0-1.0,
  ""reasoning"": ""brief explanation""
}}

If no good match, set confidence to 0.";

            var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);

            // Parse AI response
            var jsonMatch = Regex.Match(response, @"\{[\s\S]*\}");
            if (jsonMatch.Success)
            {
                var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(jsonMatch.Value);

                var entityId = json.GetProperty("entityId").GetString();
                var confidence = json.GetProperty("confidence").GetDouble();

                if (confidence >= 0.6 && !string.IsNullOrEmpty(entityId))
                {
                    var entity = entities.FirstOrDefault(e =>
                        GetFieldValue(e, "id") == entityId || GetFieldValue(e, "Id") == entityId);

                    if (entity != null)
                    {
                        var primaryTrackField = trackFields.First();
                        var entityName = GetFieldValue(entity, primaryTrackField.Name);

                        _logger.LogInformation("AI resolved entity: {Name} (ID: {Id}, Confidence: {Confidence:P0})",
                            entityName, entityId, confidence);

                        return new EntityResolutionResult
                        {
                            Success = true,
                            EntityId = entityId,
                            EntityName = entityName,
                            Confidence = confidence,
                            MatchedField = "AI",
                            ResolvedByAI = true
                        };
                    }
                }
            }

            return new EntityResolutionResult
            {
                Success = false,
                ErrorMessage = $"Could not find entity matching '{userInput}'"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI resolution failed");
            return new EntityResolutionResult
            {
                Success = false,
                ErrorMessage = "AI resolution failed"
            };
        }
    }

    /// <summary>
    /// Get field value from JObject (case-insensitive)
    /// </summary>
    private string? GetFieldValue(JObject obj, string fieldName)
    {
        var property = obj.Properties()
            .FirstOrDefault(p => string.Equals(p.Name, fieldName, StringComparison.OrdinalIgnoreCase));

        return property?.Value?.ToString();
    }
}

/// <summary>
/// Result of entity resolution
/// </summary>
public class EntityResolutionResult
{
    public bool Success { get; set; }
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }
    public double Confidence { get; set; }
    public string? MatchedField { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ResolvedByAI { get; set; }
    public List<EntityMatch> AlternativeMatches { get; set; } = new();
}

/// <summary>
/// Single entity match
/// </summary>
public class EntityMatch
{
    public string EntityId { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public JObject? Entity { get; set; }
}
