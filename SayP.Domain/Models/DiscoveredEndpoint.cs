namespace SayP.Domain.Models;

/// <summary>
/// Represents a discovered API endpoint
/// </summary>
public class DiscoveredEndpoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Intent { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public string HttpMethod { get; set; } = "POST";
    public string Route { get; set; } = string.Empty;
    public string ControllerName { get; set; } = string.Empty;
    public string ActionName { get; set; } = string.Empty;
    public int Priority { get; set; } = 0;
    public bool RequiresConfirmation { get; set; } = true;
    
    /// <summary>
    /// Schema for request body/parameters
    /// </summary>
    public EndpointSchema? Schema { get; set; }
    
    /// <summary>
    /// When this endpoint was discovered
    /// </summary>
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Success rate (0-1) based on execution history
    /// </summary>
    public double SuccessRate { get; set; } = 1.0;
    
    /// <summary>
    /// Number of times this endpoint has been called
    /// </summary>
    public int CallCount { get; set; } = 0;
}

/// <summary>
/// Schema for an endpoint's request parameters
/// </summary>
public class EndpointSchema
{
    public string TypeName { get; set; } = string.Empty;
    public List<SchemaField> Fields { get; set; } = new();
    public string? Example { get; set; }
    
    /// <summary>
    /// Whether the request body expects an array of items
    /// </summary>
    public bool IsArray { get; set; } = false;
    
    /// <summary>
    /// For array types, the item type name
    /// </summary>
    public string? ArrayItemType { get; set; }
}

/// <summary>
/// Represents a field in a schema
/// </summary>
public class SchemaField
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Example { get; set; } = string.Empty;
    public bool IsRequired { get; set; } = false;
    public bool IsOptional { get; set; } = false;
    public string[] Aliases { get; set; } = Array.Empty<string>();
    public string? ValidationPattern { get; set; }
    public string? ValidationMessage { get; set; }
    public object? DefaultValue { get; set; }
    
    /// <summary>
    /// For nested objects
    /// </summary>
    public List<SchemaField>? NestedFields { get; set; }
    
    /// <summary>
    /// For arrays, the type of items
    /// </summary>
    public string? ItemType { get; set; }
    
    // ✅ NEW: TrackField properties for entity resolution
    /// <summary>
    /// Is this field used for tracking/identifying entities?
    /// </summary>
    public bool IsTrackField { get; set; } = false;
    
    /// <summary>
    /// Priority of this track field (higher = more important)
    /// </summary>
    public int TrackFieldPriority { get; set; } = 5;
    
    /// <summary>
    /// Use fuzzy matching for this track field
    /// </summary>
    public bool UsesFuzzyMatching { get; set; } = true;
    
    /// <summary>
    /// Minimum confidence for fuzzy matching
    /// </summary>
    public double MinTrackConfidence { get; set; } = 0.7;
    
    /// <summary>
    /// Alternative search terms for tracking
    /// </summary>
    public string[]? TrackAliases { get; set; }
    
    /// <summary>
    /// Case-sensitive tracking
    /// </summary>
    public bool TrackCaseSensitive { get; set; } = false;
    
    /// <summary>
    /// Allow partial matches
    /// </summary>
    public bool AllowPartialMatch { get; set; } = true;
    
    /// <summary>
    /// Minimum length for partial match
    /// </summary>
    public int MinPartialMatchLength { get; set; } = 3;
}
