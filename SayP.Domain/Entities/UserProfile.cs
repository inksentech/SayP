using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SayP.Domain.Entities;

/// <summary>
/// Kullanıcı profili - davranış analizi ve öğrenme için
/// </summary>
[Table("UserProfiles")]
public class UserProfile
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Telefon numarası (WhatsApp)
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>
    /// Tenant ID
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Kullanıcı adı (backend'den)
    /// </summary>
    [MaxLength(200)]
    public string? UserName { get; set; }

    /// <summary>
    /// Toplam mesaj sayısı
    /// </summary>
    public int TotalMessageCount { get; set; }

    /// <summary>
    /// Toplam komut sayısı
    /// </summary>
    public int TotalCommandCount { get; set; }

    /// <summary>
    /// En çok kullanılan komutlar (JSON)
    /// </summary>
    [Column(TypeName = "text")]
    public string? FrequentCommandsJson { get; set; }

    /// <summary>
    /// Tercih edilen varlıklar (ürünler, müşteriler vb.) (JSON)
    /// </summary>
    [Column(TypeName = "text")]
    public string? PreferredEntitiesJson { get; set; }

    /// <summary>
    /// Konuşma tarzı analizi (JSON)
    /// </summary>
    [Column(TypeName = "text")]
    public string? CommunicationStyleJson { get; set; }

    /// <summary>
    /// Öğrenilmiş pattern'ler (JSON)
    /// </summary>
    [Column(TypeName = "text")]
    public string? LearnedPatternsJson { get; set; }

    /// <summary>
    /// Kullanım alışkanlıkları (JSON)
    /// </summary>
    [Column(TypeName = "text")]
    public string? UsageHabitsJson { get; set; }

    /// <summary>
    /// Kullanıcıya özel notlar/durumlar (JSON)
    /// </summary>
    [Column(TypeName = "text")]
    public string? CustomContextJson { get; set; }

    /// <summary>
    /// Tercih edilen dil/lehçe
    /// </summary>
    [MaxLength(10)]
    public string? PreferredLanguage { get; set; }

    /// <summary>
    /// Ortalama yanıt süresi (saniye)
    /// </summary>
    public double AverageResponseTime { get; set; }

    /// <summary>
    /// Aktif saatler (JSON) - Kullanıcının en aktif olduğu saatler
    /// </summary>
    [Column(TypeName = "text")]
    public string? ActiveHoursJson { get; set; }

    /// <summary>
    /// Son aktivite tarihi
    /// </summary>
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// Profil oluşturulma tarihi
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Son güncelleme tarihi
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Öğrenme seviyesi (0-100)
    /// </summary>
    public int LearningScore { get; set; }

    /// <summary>
    /// Kullanıcı memnuniyet skoru (0-100)
    /// </summary>
    public int SatisfactionScore { get; set; }
}

/// <summary>
/// Kullanıcı davranış logu
/// </summary>
[Table("UserBehaviorLogs")]
public class UserBehaviorLog
{
    [Key]
    public Guid Id { get; set; }

    public Guid UserProfileId { get; set; }

    /// <summary>
    /// Davranış tipi (command_executed, pattern_used, preference_detected, vb.)
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string BehaviorType { get; set; } = string.Empty;

    /// <summary>
    /// Davranış detayları (JSON)
    /// </summary>
    [Column(TypeName = "text")]
    public string? BehaviorDataJson { get; set; }

    /// <summary>
    /// Mesaj içeriği (opsiyonel)
    /// </summary>
    [Column(TypeName = "text")]
    public string? MessageContent { get; set; }

    /// <summary>
    /// Komut tipi (varsa)
    /// </summary>
    [MaxLength(50)]
    public string? CommandType { get; set; }

    /// <summary>
    /// Başarı durumu
    /// </summary>
    public bool IsSuccessful { get; set; }

    /// <summary>
    /// Confidence skoru
    /// </summary>
    public double Confidence { get; set; }

    /// <summary>
    /// Yanıt süresi (ms)
    /// </summary>
    public int ResponseTimeMs { get; set; }

    /// <summary>
    /// Oluşturulma tarihi
    /// </summary>
    public DateTime CreatedAt { get; set; }

    // Navigation
    [ForeignKey(nameof(UserProfileId))]
    public UserProfile? UserProfile { get; set; }
}
