using SayP.Domain.Models;

namespace SayP.Application.Interfaces;

/// <summary>
/// Executes commands on discovered endpoints dynamically
/// </summary>
public interface IGenericCommandExecutor
{
    /// <summary>
    /// Execute a command on a discovered endpoint
    /// </summary>
    Task<GenericExecutionResult> ExecuteAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> parameters,
        Guid tenantId,
        string baseUrl,
        string? phoneNumber = null,
        string? apiKey = null,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Validate parameters against endpoint schema
    /// </summary>
    Task<ValidationResult> ValidateParametersAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> parameters);
    
    /// <summary>
    /// Build request body from parameters and schema
    /// </summary>
    Task<string> BuildRequestBodyAsync(
        EndpointSchema schema,
        Dictionary<string, object> parameters);
}

/// <summary>
/// Result of generic command execution
/// </summary>
public class GenericExecutionResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public object? ParsedResponse { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan ExecutionTime { get; set; }
}

/// <summary>
/// Result of parameter validation
/// </summary>
public class ValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> MissingRequired { get; set; } = new();
    public Dictionary<string, object> ValidatedParameters { get; set; } = new();
}
