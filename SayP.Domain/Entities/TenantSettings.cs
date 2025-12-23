using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SayP.Domain.Entities;

/// <summary>
/// Tenant bazlı akıllı özellik ayarları
/// </summary>
[Table("TenantSettings")]
public class TenantSettings
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant ID
    /// </summary>
    [Required]
    public Guid TenantId { get; set; }

    /// <summary>
    /// Tenant adı (cache için)
    /// </summary>
    [MaxLength(200)]
    public string? TenantName { get; set; }

    // ============ AKILLI ÖZELLİKLER ============

    /// <summary>
    /// Smart Intent Classifier aktif mi?
    /// </summary>
    public bool EnableSmartIntentClassifier { get; set; } = true;

    /// <summary>
    /// Entity Extraction aktif mi?
    /// </summary>
    public bool EnableEntityExtraction { get; set; } = true;

    /// <summary>
    /// Multi-turn Dialogue aktif mi?
    /// </summary>
    public bool EnableMultiTurnDialogue { get; set; } = true;

    /// <summary>
    /// Intelligent Fallback aktif mi?
    /// </summary>
    public bool EnableIntelligentFallback { get; set; } = true;

    /// <summary>
    /// User Learning (Kullanıcı bazlı öğrenme) aktif mi?
    /// </summary>
    public bool EnableUserLearning { get; set; } = true;

    /// <summary>
    /// Intent Discovery (Dinamik intent keşfi) aktif mi?
    /// </summary>
    public bool EnableIntentDiscovery { get; set; } = true;

    /// <summary>
    /// Pattern Learning (Pattern öğrenme) aktif mi?
    /// </summary>
    public bool EnablePatternLearning { get; set; } = true;

    /// <summary>
    /// Context-Aware Processing aktif mi?
    /// </summary>
    public bool EnableContextAware { get; set; } = true;

    /// <summary>
    /// Slot Filling aktif mi?
    /// </summary>
    public bool EnableSlotFilling { get; set; } = true;

    /// <summary>
    /// Analytics & Monitoring aktif mi?
    /// </summary>
    public bool EnableAnalytics { get; set; } = true;

    // ============ GELİŞMİŞ AYARLAR ============

    /// <summary>
    /// Minimum confidence threshold (0-1)
    /// </summary>
    public double MinConfidenceThreshold { get; set; } = 0.5;

    /// <summary>
    /// High confidence threshold (0-1)
    /// </summary>
    public double HighConfidenceThreshold { get; set; } = 0.8;

    /// <summary>
    /// Dialogue timeout (dakika)
    /// </summary>
    public int DialogueTimeoutMinutes { get; set; } = 10;

    /// <summary>
    /// Max dialogue attempts
    /// </summary>
    public int MaxDialogueAttempts { get; set; } = 3;

    /// <summary>
    /// Profile analysis interval (kaç komutta bir)
    /// </summary>
    public int ProfileAnalysisInterval { get; set; } = 10;

    /// <summary>
    /// Behavior log retention (gün)
    /// </summary>
    public int BehaviorLogRetentionDays { get; set; } = 90;

    // ============ METADATA ============

    /// <summary>
    /// Ayarları oluşturan kullanıcı
    /// </summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>
    /// Son güncelleyen kullanıcı
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>
    /// Oluşturulma tarihi
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Son güncelleme tarihi
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Aktif mi?
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Notlar
    /// </summary>
    [Column(TypeName = "text")]
    public string? Notes { get; set; }
}

/// <summary>
/// Tenant ayar değişiklik logu
/// </summary>
[Table("TenantSettingsLogs")]
public class TenantSettingsLog
{
    [Key]
    public Guid Id { get; set; }

    public Guid TenantSettingsId { get; set; }

    /// <summary>
    /// Değiştirilen ayar
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string SettingName { get; set; } = string.Empty;

    /// <summary>
    /// Eski değer
    /// </summary>
    [MaxLength(500)]
    public string? OldValue { get; set; }

    /// <summary>
    /// Yeni değer
    /// </summary>
    [MaxLength(500)]
    public string? NewValue { get; set; }

    /// <summary>
    /// Değişikliği yapan kullanıcı
    /// </summary>
    public Guid ChangedByUserId { get; set; }

    /// <summary>
    /// Değişiklik nedeni
    /// </summary>
    [MaxLength(500)]
    public string? Reason { get; set; }

    /// <summary>
    /// Değişiklik tarihi
    /// </summary>
    public DateTime ChangedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(TenantSettingsId))]
    public TenantSettings? TenantSettings { get; set; }
}
