using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SayP.Domain.Interfaces;
using System.Text.Json;

namespace SayP.Application.Services;

/// <summary>
/// Calibrates confidence thresholds based on user feedback
/// </summary>
public class ConfidenceCalibrator
{
    private readonly ISayPDbContext _context;
    private readonly ILogger<ConfidenceCalibrator> _logger;
    private readonly Dictionary<string, IntentMetrics> _intentMetrics = new();
    private const int LEARNING_WINDOW_DAYS = 30;

    public ConfidenceCalibrator(
        ISayPDbContext context,
        ILogger<ConfidenceCalibrator> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Learn from user feedback
    /// </summary>
    public async Task LearnFromFeedbackAsync(
        string intentName,
        double predictedConfidence,
        bool wasCorrect,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get or create metrics for this intent
            if (!_intentMetrics.TryGetValue(intentName, out var metrics))
            {
                metrics = await LoadMetricsAsync(intentName, cancellationToken);
                _intentMetrics[intentName] = metrics;
            }

            // Update metrics
            metrics.TotalPredictions++;
            
            if (wasCorrect)
            {
                metrics.CorrectPredictions++;
                metrics.ConfidenceWhenCorrect.Add(predictedConfidence);
            }
            else
            {
                metrics.IncorrectPredictions++;
                metrics.ConfidenceWhenIncorrect.Add(predictedConfidence);
            }

            // Recalculate optimal threshold
            metrics.OptimalThreshold = CalculateOptimalThreshold(metrics);
            metrics.LastUpdated = DateTime.UtcNow;

            // Save metrics
            await SaveMetricsAsync(intentName, metrics, cancellationToken);

            _logger.LogInformation(
                "Updated metrics for {Intent}: Accuracy={Accuracy:P2}, OptimalThreshold={Threshold:F2}",
                intentName, metrics.Accuracy, metrics.OptimalThreshold);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error learning from feedback for intent {Intent}", intentName);
        }
    }

    /// <summary>
    /// Get optimal confidence threshold for an intent
    /// </summary>
    public async Task<double> GetOptimalThresholdAsync(
        string intentName,
        CancellationToken cancellationToken = default)
    {
        if (_intentMetrics.TryGetValue(intentName, out var metrics))
        {
            return metrics.OptimalThreshold;
        }

        // Load from database
        metrics = await LoadMetricsAsync(intentName, cancellationToken);
        _intentMetrics[intentName] = metrics;

        return metrics.OptimalThreshold;
    }

    /// <summary>
    /// Adjust confidence score based on historical performance
    /// </summary>
    public async Task<double> AdjustConfidenceAsync(
        string intentName,
        double rawConfidence,
        CancellationToken cancellationToken = default)
    {
        var metrics = await GetMetricsAsync(intentName, cancellationToken);

        if (metrics.TotalPredictions < 10)
        {
            // Not enough data, return raw confidence
            return rawConfidence;
        }

        // Adjust based on accuracy
        var accuracyFactor = metrics.Accuracy;
        var adjustedConfidence = rawConfidence * accuracyFactor;

        // Apply calibration curve
        adjustedConfidence = ApplyCalibrationCurve(adjustedConfidence, metrics);

        _logger.LogDebug(
            "Adjusted confidence for {Intent}: {Raw:F2} → {Adjusted:F2} (Accuracy: {Accuracy:P2})",
            intentName, rawConfidence, adjustedConfidence, accuracyFactor);

        return Math.Clamp(adjustedConfidence, 0, 1);
    }

    /// <summary>
    /// Calculate optimal threshold using ROC analysis
    /// </summary>
    private double CalculateOptimalThreshold(IntentMetrics metrics)
    {
        if (metrics.TotalPredictions < 10)
        {
            return 0.6; // Default threshold
        }

        // Calculate mean confidence for correct and incorrect predictions
        var meanCorrect = metrics.ConfidenceWhenCorrect.Any() 
            ? metrics.ConfidenceWhenCorrect.Average() 
            : 0.8;
        
        var meanIncorrect = metrics.ConfidenceWhenIncorrect.Any() 
            ? metrics.ConfidenceWhenIncorrect.Average() 
            : 0.4;

        // Optimal threshold is between the two means
        var optimalThreshold = (meanCorrect + meanIncorrect) / 2;

        // Ensure it's within reasonable bounds
        return Math.Clamp(optimalThreshold, 0.3, 0.9);
    }

    /// <summary>
    /// Apply calibration curve (Platt scaling)
    /// </summary>
    private double ApplyCalibrationCurve(double confidence, IntentMetrics metrics)
    {
        // Simple sigmoid calibration
        // calibrated = 1 / (1 + exp(-(a * confidence + b)))
        
        var a = 2.0; // Slope parameter
        var b = -1.0; // Bias parameter

        // Adjust parameters based on metrics
        if (metrics.Accuracy < 0.7)
        {
            // Low accuracy: be more conservative
            b = -1.5;
        }
        else if (metrics.Accuracy > 0.9)
        {
            // High accuracy: be more confident
            b = -0.5;
        }

        var calibrated = 1.0 / (1.0 + Math.Exp(-(a * confidence + b)));
        return calibrated;
    }

    /// <summary>
    /// Get metrics for an intent
    /// </summary>
    private async Task<IntentMetrics> GetMetricsAsync(
        string intentName,
        CancellationToken cancellationToken)
    {
        if (_intentMetrics.TryGetValue(intentName, out var metrics))
        {
            return metrics;
        }

        metrics = await LoadMetricsAsync(intentName, cancellationToken);
        _intentMetrics[intentName] = metrics;
        return metrics;
    }

    /// <summary>
    /// Load metrics from database
    /// </summary>
    private async Task<IntentMetrics> LoadMetricsAsync(
        string intentName,
        CancellationToken cancellationToken)
    {
        // In a real implementation, this would load from a dedicated metrics table
        // For now, we'll analyze recent commands
        
        var recentCommands = await _context.Commands
            .Where(c => c.CreatedAt > DateTime.UtcNow.AddDays(-LEARNING_WINDOW_DAYS))
            .Where(c => c.Type.ToString() == intentName)
            .Select(c => new { c.Confidence, c.Status })
            .ToListAsync(cancellationToken);

        var metrics = new IntentMetrics
        {
            IntentName = intentName,
            TotalPredictions = recentCommands.Count,
            CorrectPredictions = recentCommands.Count(c => c.Status == Domain.Enums.CommandStatus.Completed),
            IncorrectPredictions = recentCommands.Count(c => c.Status == Domain.Enums.CommandStatus.Failed),
            ConfidenceWhenCorrect = recentCommands
                .Where(c => c.Status == Domain.Enums.CommandStatus.Completed)
                .Select(c => c.Confidence)
                .ToList(),
            ConfidenceWhenIncorrect = recentCommands
                .Where(c => c.Status == Domain.Enums.CommandStatus.Failed)
                .Select(c => c.Confidence)
                .ToList(),
            LastUpdated = DateTime.UtcNow
        };

        metrics.OptimalThreshold = CalculateOptimalThreshold(metrics);

        return metrics;
    }

    /// <summary>
    /// Save metrics to database
    /// </summary>
    private async Task SaveMetricsAsync(
        string intentName,
        IntentMetrics metrics,
        CancellationToken cancellationToken)
    {
        // In a real implementation, this would save to a dedicated metrics table
        // For now, we'll just log it
        
        _logger.LogDebug(
            "Metrics for {Intent}: Total={Total}, Correct={Correct}, Incorrect={Incorrect}, Threshold={Threshold:F2}",
            intentName, metrics.TotalPredictions, metrics.CorrectPredictions, 
            metrics.IncorrectPredictions, metrics.OptimalThreshold);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Get all intent metrics
    /// </summary>
    public async Task<Dictionary<string, IntentMetrics>> GetAllMetricsAsync(
        CancellationToken cancellationToken = default)
    {
        // Load metrics for all known intents
        var allIntents = await _context.Commands
            .Select(c => c.Type.ToString())
            .Distinct()
            .ToListAsync(cancellationToken);

        var allMetrics = new Dictionary<string, IntentMetrics>();

        foreach (var intent in allIntents)
        {
            var metrics = await GetMetricsAsync(intent, cancellationToken);
            allMetrics[intent] = metrics;
        }

        return allMetrics;
    }
}

public class IntentMetrics
{
    public string IntentName { get; set; } = string.Empty;
    public int TotalPredictions { get; set; }
    public int CorrectPredictions { get; set; }
    public int IncorrectPredictions { get; set; }
    public List<double> ConfidenceWhenCorrect { get; set; } = new();
    public List<double> ConfidenceWhenIncorrect { get; set; } = new();
    public double OptimalThreshold { get; set; } = 0.6;
    public DateTime LastUpdated { get; set; }

    public double Accuracy => TotalPredictions > 0 
        ? (double)CorrectPredictions / TotalPredictions 
        : 0;
}
