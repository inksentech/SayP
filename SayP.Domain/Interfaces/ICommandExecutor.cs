using SayP.Domain.Enums;

namespace SayP.Domain.Interfaces;

/// <summary>
/// Executes commands on the main backend API
/// </summary>
public interface ICommandExecutor
{
    /// <summary>
    /// Execute a command
    /// </summary>
    Task<CommandExecutionResult> ExecuteAsync(
        CommandType commandType,
        string commandJson,
        Guid tenantId,
        Guid? companyId = null,
        Guid? userCompanyId = null,
        CancellationToken cancellationToken = default);
}

public class CommandExecutionResult
{
    public bool Success { get; set; }
    public string? Result { get; set; }
    public string? ResultJson { get; set; }
    public string? Message { get; set; }
    public string? ErrorMessage { get; set; }
    public int StatusCode { get; set; }
}
