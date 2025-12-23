using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SayP.Domain.Entities;
using SayP.Domain.Interfaces;

namespace SayP.Application.Services;

/// <summary>
/// Tenant ayarları yönetim servisi
/// </summary>
public class TenantSettingsService
{
    private readonly ISayPDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TenantSettingsService> _logger;
    private const string CACHE_KEY_PREFIX = "TenantSettings_";
    private const int CACHE_DURATION_MINUTES = 10;

    public TenantSettingsService(
        ISayPDbContext context,
        IMemoryCache cache,
        ILogger<TenantSettingsService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Tenant ayarlarını getir (cache'li)
    /// </summary>
    public async Task<TenantSettings> GetSettingsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CACHE_KEY_PREFIX}{tenantId}";

        if (_cache.TryGetValue(cacheKey, out TenantSettings? cachedSettings) && cachedSettings != null)
        {
            return cachedSettings;
        }

        var settings = await _context.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.IsActive, cancellationToken);

        if (settings == null)
        {
            // Default ayarlarla oluştur
            settings = await CreateDefaultSettingsAsync(tenantId, null, null, cancellationToken);
        }

        // Cache'e ekle
        _cache.Set(cacheKey, settings, TimeSpan.FromMinutes(CACHE_DURATION_MINUTES));

        return settings;
    }

    /// <summary>
    /// Default ayarlarla tenant settings oluştur
    /// </summary>
    public async Task<TenantSettings> CreateDefaultSettingsAsync(
        Guid tenantId,
        string? tenantName = null,
        Guid? createdByUserId = null,
        CancellationToken cancellationToken = default)
    {
        var settings = new TenantSettings
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            TenantName = tenantName,
            EnableSmartIntentClassifier = true,
            EnableEntityExtraction = true,
            EnableMultiTurnDialogue = true,
            EnableIntelligentFallback = true,
            EnableUserLearning = true,
            EnableIntentDiscovery = true,
            EnablePatternLearning = true,
            EnableContextAware = true,
            EnableSlotFilling = true,
            EnableAnalytics = true,
            MinConfidenceThreshold = 0.5,
            HighConfidenceThreshold = 0.8,
            DialogueTimeoutMinutes = 10,
            MaxDialogueAttempts = 3,
            ProfileAnalysisInterval = 10,
            BehaviorLogRetentionDays = 90,
            CreatedByUserId = createdByUserId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true
        };

        _context.TenantSettings.Add(settings);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created default settings for tenant {TenantId}", tenantId);

        return settings;
    }

    /// <summary>
    /// Ayarları güncelle
    /// </summary>
    public async Task<TenantSettings> UpdateSettingsAsync(
        Guid tenantId,
        Action<TenantSettings> updateAction,
        Guid updatedByUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        // Cache'i atla, direkt database'den al
        var settings = await _context.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.IsActive, cancellationToken);
        
        if (settings == null)
        {
            throw new InvalidOperationException($"Settings not found for tenant {tenantId}");
        }
        
        // Eski değerleri kaydet (log için)
        var oldSettings = CloneSettings(settings);

        // Güncelleme yap
        updateAction(settings);
        settings.UpdatedByUserId = updatedByUserId;
        settings.UpdatedAt = DateTime.UtcNow;

        // Entity'yi update olarak işaretle
        _context.TenantSettings.Update(settings);
        await _context.SaveChangesAsync(cancellationToken);

        // Değişiklikleri logla
        await LogChangesAsync(oldSettings, settings, updatedByUserId, reason, cancellationToken);

        // Cache'i temizle
        _cache.Remove($"{CACHE_KEY_PREFIX}{tenantId}");

        _logger.LogInformation("Updated settings for tenant {TenantId} by user {UserId}", 
            tenantId, updatedByUserId);

        return settings;
    }

    /// <summary>
    /// Belirli bir özelliği aç/kapa
    /// </summary>
    public async Task<bool> ToggleFeatureAsync(
        Guid tenantId,
        string featureName,
        bool enabled,
        Guid updatedByUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Toggle feature: {FeatureName} = {Enabled} for tenant {TenantId}", 
                featureName, enabled, tenantId);

            await UpdateSettingsAsync(
                tenantId,
                settings =>
                {
                    // Case-insensitive property lookup
                    var property = typeof(TenantSettings).GetProperty(
                        featureName, 
                        System.Reflection.BindingFlags.IgnoreCase | 
                        System.Reflection.BindingFlags.Public | 
                        System.Reflection.BindingFlags.Instance);
                    
                    if (property != null && property.PropertyType == typeof(bool))
                    {
                        var oldValue = property.GetValue(settings);
                        property.SetValue(settings, enabled);
                        var newValue = property.GetValue(settings);
                        _logger.LogInformation("Property {PropertyName}: {OldValue} -> {NewValue}", 
                            property.Name, oldValue, newValue);
                    }
                    else
                    {
                        _logger.LogWarning("Property {PropertyName} not found or not boolean type. Available properties: {Properties}", 
                            featureName, 
                            string.Join(", ", typeof(TenantSettings).GetProperties().Where(p => p.PropertyType == typeof(bool)).Select(p => p.Name)));
                    }
                },
                updatedByUserId,
                reason,
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling feature {Feature} for tenant {TenantId}", 
                featureName, tenantId);
            return false;
        }
    }

    /// <summary>
    /// Tüm özellikleri aç/kapa
    /// </summary>
    public async Task<bool> ToggleAllFeaturesAsync(
        Guid tenantId,
        bool enabled,
        Guid updatedByUserId,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await UpdateSettingsAsync(
                tenantId,
                settings =>
                {
                    settings.EnableSmartIntentClassifier = enabled;
                    settings.EnableEntityExtraction = enabled;
                    settings.EnableMultiTurnDialogue = enabled;
                    settings.EnableIntelligentFallback = enabled;
                    settings.EnableUserLearning = enabled;
                    settings.EnableIntentDiscovery = enabled;
                    settings.EnablePatternLearning = enabled;
                    settings.EnableContextAware = enabled;
                    settings.EnableSlotFilling = enabled;
                    settings.EnableAnalytics = enabled;
                },
                updatedByUserId,
                reason ?? $"All features {(enabled ? "enabled" : "disabled")}",
                cancellationToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error toggling all features for tenant {TenantId}", tenantId);
            return false;
        }
    }

    /// <summary>
    /// Özellik aktif mi kontrol et
    /// </summary>
    public async Task<bool> IsFeatureEnabledAsync(
        Guid tenantId,
        string featureName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var settings = await GetSettingsAsync(tenantId, cancellationToken);
            var property = typeof(TenantSettings).GetProperty(featureName);
            
            if (property != null && property.PropertyType == typeof(bool))
            {
                return (bool)(property.GetValue(settings) ?? false);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking feature {Feature} for tenant {TenantId}", 
                featureName, tenantId);
            return false;
        }
    }

    /// <summary>
    /// Tüm tenant ayarlarını getir (Admin için)
    /// </summary>
    public async Task<List<TenantSettings>> GetAllSettingsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.TenantSettings
            .Where(s => s.IsActive)
            .OrderBy(s => s.TenantName)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Ayar değişiklik geçmişini getir
    /// </summary>
    public async Task<List<TenantSettingsLog>> GetChangeHistoryAsync(
        Guid tenantId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var settings = await _context.TenantSettings
            .FirstOrDefaultAsync(s => s.TenantId == tenantId, cancellationToken);

        if (settings == null)
            return new List<TenantSettingsLog>();

        return await _context.TenantSettingsLogs
            .Where(l => l.TenantSettingsId == settings.Id)
            .OrderByDescending(l => l.ChangedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Ayar değişikliklerini logla
    /// </summary>
    private async Task LogChangesAsync(
        TenantSettings oldSettings,
        TenantSettings newSettings,
        Guid changedByUserId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var changes = new List<TenantSettingsLog>();

        // Boolean özellikleri kontrol et
        var boolProperties = typeof(TenantSettings)
            .GetProperties()
            .Where(p => p.PropertyType == typeof(bool) && p.Name.StartsWith("Enable"));

        foreach (var prop in boolProperties)
        {
            var oldValue = (bool?)prop.GetValue(oldSettings);
            var newValue = (bool?)prop.GetValue(newSettings);

            if (oldValue != newValue)
            {
                changes.Add(new TenantSettingsLog
                {
                    Id = Guid.NewGuid(),
                    TenantSettingsId = newSettings.Id,
                    SettingName = prop.Name,
                    OldValue = oldValue?.ToString(),
                    NewValue = newValue?.ToString(),
                    ChangedByUserId = changedByUserId,
                    Reason = reason,
                    ChangedAt = DateTime.UtcNow
                });
            }
        }

        // Numeric özellikleri kontrol et
        var numericProperties = new[] 
        { 
            "MinConfidenceThreshold", "HighConfidenceThreshold", 
            "DialogueTimeoutMinutes", "MaxDialogueAttempts",
            "ProfileAnalysisInterval", "BehaviorLogRetentionDays"
        };

        foreach (var propName in numericProperties)
        {
            var prop = typeof(TenantSettings).GetProperty(propName);
            if (prop != null)
            {
                var oldValue = prop.GetValue(oldSettings)?.ToString();
                var newValue = prop.GetValue(newSettings)?.ToString();

                if (oldValue != newValue)
                {
                    changes.Add(new TenantSettingsLog
                    {
                        Id = Guid.NewGuid(),
                        TenantSettingsId = newSettings.Id,
                        SettingName = propName,
                        OldValue = oldValue,
                        NewValue = newValue,
                        ChangedByUserId = changedByUserId,
                        Reason = reason,
                        ChangedAt = DateTime.UtcNow
                    });
                }
            }
        }

        if (changes.Any())
        {
            _context.TenantSettingsLogs.AddRange(changes);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Logged {Count} setting changes for tenant {TenantId}", 
                changes.Count, newSettings.TenantId);
        }
    }

    /// <summary>
    /// Settings'i klonla (log için)
    /// </summary>
    private TenantSettings CloneSettings(TenantSettings settings)
    {
        return new TenantSettings
        {
            Id = settings.Id,
            TenantId = settings.TenantId,
            EnableSmartIntentClassifier = settings.EnableSmartIntentClassifier,
            EnableEntityExtraction = settings.EnableEntityExtraction,
            EnableMultiTurnDialogue = settings.EnableMultiTurnDialogue,
            EnableIntelligentFallback = settings.EnableIntelligentFallback,
            EnableUserLearning = settings.EnableUserLearning,
            EnableIntentDiscovery = settings.EnableIntentDiscovery,
            EnablePatternLearning = settings.EnablePatternLearning,
            EnableContextAware = settings.EnableContextAware,
            EnableSlotFilling = settings.EnableSlotFilling,
            EnableAnalytics = settings.EnableAnalytics,
            MinConfidenceThreshold = settings.MinConfidenceThreshold,
            HighConfidenceThreshold = settings.HighConfidenceThreshold,
            DialogueTimeoutMinutes = settings.DialogueTimeoutMinutes,
            MaxDialogueAttempts = settings.MaxDialogueAttempts,
            ProfileAnalysisInterval = settings.ProfileAnalysisInterval,
            BehaviorLogRetentionDays = settings.BehaviorLogRetentionDays
        };
    }
}
