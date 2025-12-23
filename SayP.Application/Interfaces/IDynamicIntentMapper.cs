using SayP.Domain.Models;

namespace SayP.Application.Interfaces;

/// <summary>
/// Maps natural language messages to discovered endpoints using AI
/// </summary>
public interface IDynamicIntentMapper
{
    /// <summary>
    /// Map a user message to the most appropriate endpoint
    /// </summary>
    Task<IntentMappingResult> MapIntentAsync(
        string message,
        List<DiscoveredEndpoint> availableEndpoints,
        string? conversationContext = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Learn from user feedback to improve future mappings
    /// </summary>
    Task LearnFromFeedbackAsync(
        string message,
        string correctIntent,
        bool wasCorrect,
        Guid tenantId);
    
    /// <summary>
    /// Get mapping confidence for a message and endpoint
    /// </summary>
    Task<double> GetMappingConfidenceAsync(
        string message,
        DiscoveredEndpoint endpoint,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of intent mapping
/// </summary>
public class IntentMappingResult
{
    public bool Success { get; set; }
    public DiscoveredEndpoint? MatchedEndpoint { get; set; }
    public double Confidence { get; set; }
    public List<DiscoveredEndpoint> AlternativeEndpoints { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public Dictionary<string, object> ExtractedParameters { get; set; } = new();
    
    /// <summary>
    /// Source of the match: "learned_alias", "fuzzy", "ai", "fallback"
    /// </summary>
    public string? MatchSource { get; set; }
    
    /// <summary>
    /// Detected language of the message
    /// </summary>
    public string? DetectedLanguage { get; set; }
}
