using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SayP.Domain.Interfaces;
using System.Text.Json;

namespace SayP.Application.Services;

/// <summary>
/// Analytics service for intent classification and command execution
/// </summary>
public class IntentAnalyticsService
{
    private readonly ISayPDbContext _context;
    private readonly ILogger<IntentAnalyticsService> _logger;

    public IntentAnalyticsService(
        ISayPDbContext context,
        ILogger<IntentAnalyticsService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get intent classification accuracy metrics
    /// </summary>
    public async Task<IntentAccuracyMetrics> GetIntentAccuracyAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            startDate ??= DateTime.UtcNow.AddDays(-30);
            endDate ??= DateTime.UtcNow;

            // Get all conversations in date range
            var conversations = await _context.Conversations
                .Where(c => c.CreatedAt >= startDate && c.CreatedAt <= endDate)
                .ToListAsync(cancellationToken);

            var totalClassifications = 0;
            var highConfidenceCount = 0;
            var mediumConfidenceCount = 0;
            var lowConfidenceCount = 0;
            var intentDistribution = new Dictionary<string, int>();

            foreach (var conversation in conversations)
            {
                if (string.IsNullOrEmpty(conversation.Context))
                    continue;

                try
                {
                    var context = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(conversation.Context);
                    if (context != null && context.ContainsKey("lastIntent"))
                    {
                        totalClassifications++;
                        
                        var intent = context["lastIntent"].GetString() ?? "Unknown";
                        var confidence = context.ContainsKey("lastConfidence") 
                            ? context["lastConfidence"].GetDouble() 
                            : 0;

                        // Track confidence distribution
                        if (confidence >= 0.8)
                            highConfidenceCount++;
                        else if (confidence >= 0.5)
                            mediumConfidenceCount++;
                        else
                            lowConfidenceCount++;

                        // Track intent distribution
                        if (!intentDistribution.ContainsKey(intent))
                            intentDistribution[intent] = 0;
                        intentDistribution[intent]++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing conversation context for analytics");
                }
            }

            return new IntentAccuracyMetrics
            {
                TotalClassifications = totalClassifications,
                HighConfidenceCount = highConfidenceCount,
                MediumConfidenceCount = mediumConfidenceCount,
                LowConfidenceCount = lowConfidenceCount,
                HighConfidencePercentage = totalClassifications > 0 
                    ? (double)highConfidenceCount / totalClassifications * 100 
                    : 0,
                IntentDistribution = intentDistribution,
                StartDate = startDate.Value,
                EndDate = endDate.Value
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating intent accuracy metrics");
            return new IntentAccuracyMetrics();
        }
    }

    /// <summary>
    /// Get command execution success rate
    /// </summary>
    public async Task<CommandExecutionMetrics> GetCommandExecutionMetricsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            startDate ??= DateTime.UtcNow.AddDays(-30);
            endDate ??= DateTime.UtcNow;

            var commands = await _context.Commands
                .Where(c => c.CreatedAt >= startDate && c.CreatedAt <= endDate)
                .ToListAsync(cancellationToken);

            var totalCommands = commands.Count;
            var successfulCommands = commands.Count(c => c.Status == Domain.Enums.CommandStatus.Completed);
            var failedCommands = commands.Count(c => c.Status == Domain.Enums.CommandStatus.Failed);
            var pendingCommands = commands.Count(c => c.Status == Domain.Enums.CommandStatus.Pending);

            var commandTypeDistribution = commands
                .GroupBy(c => c.Type)
                .ToDictionary(g => g.Key.ToString(), g => g.Count());

            var avgConfidence = commands.Any() ? commands.Average(c => c.Confidence) : 0;

            return new CommandExecutionMetrics
            {
                TotalCommands = totalCommands,
                SuccessfulCommands = successfulCommands,
                FailedCommands = failedCommands,
                PendingCommands = pendingCommands,
                SuccessRate = totalCommands > 0 ? (double)successfulCommands / totalCommands * 100 : 0,
                AverageConfidence = avgConfidence,
                CommandTypeDistribution = commandTypeDistribution,
                StartDate = startDate.Value,
                EndDate = endDate.Value
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating command execution metrics");
            return new CommandExecutionMetrics();
        }
    }

    /// <summary>
    /// Get entity extraction success rate
    /// </summary>
    public async Task<EntityExtractionMetrics> GetEntityExtractionMetricsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            startDate ??= DateTime.UtcNow.AddDays(-30);
            endDate ??= DateTime.UtcNow;

            var commands = await _context.Commands
                .Where(c => c.CreatedAt >= startDate && c.CreatedAt <= endDate)
                .ToListAsync(cancellationToken);

            var totalExtractions = 0;
            var successfulExtractions = 0;
            var entityTypeDistribution = new Dictionary<string, int>();

            foreach (var command in commands)
            {
                if (string.IsNullOrEmpty(command.Parameters))
                    continue;

                try
                {
                    var parameters = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(command.Parameters);
                    if (parameters != null && parameters.ContainsKey("command"))
                    {
                        var commandData = parameters["command"];
                        if (commandData.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in commandData.EnumerateObject())
                            {
                                totalExtractions++;
                                
                                // Check if entity has value
                                if (prop.Value.ValueKind != JsonValueKind.Null && 
                                    prop.Value.ToString() != "")
                                {
                                    successfulExtractions++;
                                }

                                // Track entity type distribution
                                if (!entityTypeDistribution.ContainsKey(prop.Name))
                                    entityTypeDistribution[prop.Name] = 0;
                                entityTypeDistribution[prop.Name]++;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing command parameters for analytics");
                }
            }

            return new EntityExtractionMetrics
            {
                TotalExtractions = totalExtractions,
                SuccessfulExtractions = successfulExtractions,
                ExtractionSuccessRate = totalExtractions > 0 
                    ? (double)successfulExtractions / totalExtractions * 100 
                    : 0,
                EntityTypeDistribution = entityTypeDistribution,
                StartDate = startDate.Value,
                EndDate = endDate.Value
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating entity extraction metrics");
            return new EntityExtractionMetrics();
        }
    }

    /// <summary>
    /// Get dialogue state completion metrics
    /// </summary>
    public async Task<DialogueMetrics> GetDialogueMetricsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            startDate ??= DateTime.UtcNow.AddDays(-30);
            endDate ??= DateTime.UtcNow;

            var dialogueStates = await _context.DialogueStates
                .Where(d => d.CreatedAt >= startDate && d.CreatedAt <= endDate)
                .ToListAsync(cancellationToken);

            var totalDialogues = dialogueStates.Count;
            var completedDialogues = dialogueStates.Count(d => d.IsComplete);
            var abandonedDialogues = dialogueStates.Count(d => !d.IsActive && !d.IsComplete);
            var avgTurns = dialogueStates.Any() ? dialogueStates.Average(d => d.TurnCount) : 0;

            return new DialogueMetrics
            {
                TotalDialogues = totalDialogues,
                CompletedDialogues = completedDialogues,
                AbandonedDialogues = abandonedDialogues,
                CompletionRate = totalDialogues > 0 
                    ? (double)completedDialogues / totalDialogues * 100 
                    : 0,
                AverageTurns = avgTurns,
                StartDate = startDate.Value,
                EndDate = endDate.Value
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calculating dialogue metrics");
            return new DialogueMetrics();
        }
    }

    /// <summary>
    /// Get comprehensive dashboard metrics
    /// </summary>
    public async Task<DashboardMetrics> GetDashboardMetricsAsync(
        DateTime? startDate = null,
        DateTime? endDate = null,
        CancellationToken cancellationToken = default)
    {
        var intentMetrics = await GetIntentAccuracyAsync(startDate, endDate, cancellationToken);
        var commandMetrics = await GetCommandExecutionMetricsAsync(startDate, endDate, cancellationToken);
        var entityMetrics = await GetEntityExtractionMetricsAsync(startDate, endDate, cancellationToken);
        var dialogueMetrics = await GetDialogueMetricsAsync(startDate, endDate, cancellationToken);

        return new DashboardMetrics
        {
            IntentAccuracy = intentMetrics,
            CommandExecution = commandMetrics,
            EntityExtraction = entityMetrics,
            Dialogue = dialogueMetrics,
            GeneratedAt = DateTime.UtcNow
        };
    }
}

// Metrics Models
public class IntentAccuracyMetrics
{
    public int TotalClassifications { get; set; }
    public int HighConfidenceCount { get; set; }
    public int MediumConfidenceCount { get; set; }
    public int LowConfidenceCount { get; set; }
    public double HighConfidencePercentage { get; set; }
    public Dictionary<string, int> IntentDistribution { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class CommandExecutionMetrics
{
    public int TotalCommands { get; set; }
    public int SuccessfulCommands { get; set; }
    public int FailedCommands { get; set; }
    public int PendingCommands { get; set; }
    public double SuccessRate { get; set; }
    public double AverageConfidence { get; set; }
    public Dictionary<string, int> CommandTypeDistribution { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class EntityExtractionMetrics
{
    public int TotalExtractions { get; set; }
    public int SuccessfulExtractions { get; set; }
    public double ExtractionSuccessRate { get; set; }
    public Dictionary<string, int> EntityTypeDistribution { get; set; } = new();
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class DialogueMetrics
{
    public int TotalDialogues { get; set; }
    public int CompletedDialogues { get; set; }
    public int AbandonedDialogues { get; set; }
    public double CompletionRate { get; set; }
    public double AverageTurns { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class DashboardMetrics
{
    public IntentAccuracyMetrics IntentAccuracy { get; set; } = new();
    public CommandExecutionMetrics CommandExecution { get; set; } = new();
    public EntityExtractionMetrics EntityExtraction { get; set; } = new();
    public DialogueMetrics Dialogue { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}
