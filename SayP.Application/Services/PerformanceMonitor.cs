using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Collections.Concurrent;

namespace SayP.Application.Services;

/// <summary>
/// Performance monitoring for AI operations
/// </summary>
public class PerformanceMonitor
{
    private readonly ILogger<PerformanceMonitor> _logger;
    private readonly ConcurrentDictionary<string, OperationMetrics> _metrics = new();
    private const int SLOW_OPERATION_THRESHOLD_MS = 2000;
    private const int VERY_SLOW_OPERATION_THRESHOLD_MS = 5000;

    public PerformanceMonitor(ILogger<PerformanceMonitor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Track an operation
    /// </summary>
    public IDisposable TrackOperation(string operationName, Dictionary<string, object>? metadata = null)
    {
        return new OperationTracker(this, operationName, metadata);
    }

    /// <summary>
    /// Record operation completion
    /// </summary>
    private void RecordOperation(string operationName, long durationMs, bool success, Dictionary<string, object>? metadata)
    {
        // Get or create metrics
        var metrics = _metrics.GetOrAdd(operationName, _ => new OperationMetrics { OperationName = operationName });

        // Update metrics
        lock (metrics)
        {
            metrics.TotalCalls++;
            if (success)
                metrics.SuccessfulCalls++;
            else
                metrics.FailedCalls++;

            metrics.TotalDurationMs += durationMs;
            metrics.MinDurationMs = Math.Min(metrics.MinDurationMs, durationMs);
            metrics.MaxDurationMs = Math.Max(metrics.MaxDurationMs, durationMs);
            metrics.LastCallAt = DateTime.UtcNow;

            // Track recent durations for percentile calculation
            metrics.RecentDurations.Add(durationMs);
            if (metrics.RecentDurations.Count > 100)
            {
                metrics.RecentDurations.RemoveAt(0);
            }
        }

        // Log based on duration
        if (durationMs >= VERY_SLOW_OPERATION_THRESHOLD_MS)
        {
            _logger.LogWarning(
                "🐌 VERY SLOW: {Operation} took {Duration}ms (Threshold: {Threshold}ms) - Metadata: {Metadata}",
                operationName, durationMs, VERY_SLOW_OPERATION_THRESHOLD_MS, 
                metadata != null ? System.Text.Json.JsonSerializer.Serialize(metadata) : "none");
        }
        else if (durationMs >= SLOW_OPERATION_THRESHOLD_MS)
        {
            _logger.LogWarning(
                "🐢 SLOW: {Operation} took {Duration}ms (Threshold: {Threshold}ms)",
                operationName, durationMs, SLOW_OPERATION_THRESHOLD_MS);
        }
        else
        {
            _logger.LogDebug(
                "✅ {Operation} completed in {Duration}ms",
                operationName, durationMs);
        }
    }

    /// <summary>
    /// Get metrics for an operation
    /// </summary>
    public OperationMetrics? GetMetrics(string operationName)
    {
        return _metrics.TryGetValue(operationName, out var metrics) ? metrics : null;
    }

    /// <summary>
    /// Get all metrics
    /// </summary>
    public Dictionary<string, OperationMetrics> GetAllMetrics()
    {
        return new Dictionary<string, OperationMetrics>(_metrics);
    }

    /// <summary>
    /// Get performance summary
    /// </summary>
    public PerformanceSummary GetSummary()
    {
        var summary = new PerformanceSummary
        {
            TotalOperations = _metrics.Values.Sum(m => m.TotalCalls),
            TotalSuccessful = _metrics.Values.Sum(m => m.SuccessfulCalls),
            TotalFailed = _metrics.Values.Sum(m => m.FailedCalls),
            AverageDurationMs = _metrics.Values.Any() 
                ? _metrics.Values.Average(m => m.AverageDurationMs) 
                : 0,
            SlowestOperations = _metrics.Values
                .OrderByDescending(m => m.MaxDurationMs)
                .Take(5)
                .Select(m => new OperationSummary { OperationName = m.OperationName, Value = m.MaxDurationMs })
                .ToList<object>(),
            MostFrequentOperations = _metrics.Values
                .OrderByDescending(m => m.TotalCalls)
                .Take(5)
                .Select(m => new OperationSummary { OperationName = m.OperationName, Value = m.TotalCalls })
                .ToList<object>()
        };

        return summary;
    }

    /// <summary>
    /// Reset all metrics
    /// </summary>
    public void Reset()
    {
        _metrics.Clear();
        _logger.LogInformation("Performance metrics reset");
    }

    /// <summary>
    /// Log performance summary
    /// </summary>
    public void LogSummary()
    {
        var summary = GetSummary();
        
        _logger.LogInformation(
            "📊 Performance Summary: Total={Total}, Success={Success}, Failed={Failed}, AvgDuration={AvgDuration:F0}ms",
            summary.TotalOperations, summary.TotalSuccessful, summary.TotalFailed, summary.AverageDurationMs);

        if (summary.SlowestOperations.Any())
        {
            _logger.LogInformation("🐌 Slowest Operations:");
            foreach (var op in summary.SlowestOperations)
            {
                _logger.LogInformation("  • {Operation}: {Duration}ms", 
                    op.GetType().GetProperty("OperationName")?.GetValue(op), 
                    op.GetType().GetProperty("MaxDurationMs")?.GetValue(op));
            }
        }

        if (summary.MostFrequentOperations.Any())
        {
            _logger.LogInformation("📈 Most Frequent Operations:");
            foreach (var op in summary.MostFrequentOperations)
            {
                _logger.LogInformation("  • {Operation}: {Count} calls", 
                    op.GetType().GetProperty("OperationName")?.GetValue(op), 
                    op.GetType().GetProperty("TotalCalls")?.GetValue(op));
            }
        }
    }

    private class OperationTracker : IDisposable
    {
        private readonly PerformanceMonitor _monitor;
        private readonly string _operationName;
        private readonly Dictionary<string, object>? _metadata;
        private readonly Stopwatch _stopwatch;
        private bool _success = true;

        public OperationTracker(PerformanceMonitor monitor, string operationName, Dictionary<string, object>? metadata)
        {
            _monitor = monitor;
            _operationName = operationName;
            _metadata = metadata;
            _stopwatch = Stopwatch.StartNew();
        }

        public void MarkFailed()
        {
            _success = false;
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _monitor.RecordOperation(_operationName, _stopwatch.ElapsedMilliseconds, _success, _metadata);
        }
    }
}

public class OperationMetrics
{
    public string OperationName { get; set; } = string.Empty;
    public long TotalCalls { get; set; }
    public long SuccessfulCalls { get; set; }
    public long FailedCalls { get; set; }
    public long TotalDurationMs { get; set; }
    public long MinDurationMs { get; set; } = long.MaxValue;
    public long MaxDurationMs { get; set; }
    public DateTime? LastCallAt { get; set; }
    public List<long> RecentDurations { get; set; } = new();

    public double AverageDurationMs => TotalCalls > 0 ? (double)TotalDurationMs / TotalCalls : 0;
    public double SuccessRate => TotalCalls > 0 ? (double)SuccessfulCalls / TotalCalls : 0;
    
    public double P50DurationMs
    {
        get
        {
            if (!RecentDurations.Any()) return 0;
            var sorted = RecentDurations.OrderBy(d => d).ToList();
            return sorted[sorted.Count / 2];
        }
    }

    public double P95DurationMs
    {
        get
        {
            if (!RecentDurations.Any()) return 0;
            var sorted = RecentDurations.OrderBy(d => d).ToList();
            var index = (int)(sorted.Count * 0.95);
            return sorted[Math.Min(index, sorted.Count - 1)];
        }
    }

    public double P99DurationMs
    {
        get
        {
            if (!RecentDurations.Any()) return 0;
            var sorted = RecentDurations.OrderBy(d => d).ToList();
            var index = (int)(sorted.Count * 0.99);
            return sorted[Math.Min(index, sorted.Count - 1)];
        }
    }
}

public class PerformanceSummary
{
    public long TotalOperations { get; set; }
    public long TotalSuccessful { get; set; }
    public long TotalFailed { get; set; }
    public double AverageDurationMs { get; set; }
    public List<object> SlowestOperations { get; set; } = new();
    public List<object> MostFrequentOperations { get; set; } = new();
}

public class OperationSummary
{
    public string OperationName { get; set; } = string.Empty;
    public long Value { get; set; }
}
