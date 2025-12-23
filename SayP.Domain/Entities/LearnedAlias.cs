using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SayP.Domain.Entities;

/// <summary>
/// Öğrenilmiş alias - Kullanıcıların kullandığı alternatif ifadeler
/// Sistem başarılı komutlardan yeni alias'lar öğrenir
/// </summary>
[Table("LearnedAliases")]
public class LearnedAlias
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant ID (null ise global alias)
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Hedef intent (örn: "create_customer", "list_products")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// Öğrenilen alias/ifade (örn: "yeni müşteri kaydet", "mal listesi")
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Alias { get; set; } = string.Empty;

    /// <summary>
    /// Normalize edilmiş alias (küçük harf, trim)
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string NormalizedAlias { get; set; } = string.Empty;

    /// <summary>
    /// Dil kodu (tr, en, vb.)
    /// </summary>
    [MaxLength(10)]
    public string Language { get; set; } = "tr";

    /// <summary>
    /// Bu alias kaç kez başarıyla kullanıldı
    /// </summary>
    public int UsageCount { get; set; } = 1;

    /// <summary>
    /// Başarı oranı (0-1)
    /// </summary>
    public double SuccessRate { get; set; } = 1.0;

    /// <summary>
    /// Güven skoru (0-1) - Yüksek skor = daha güvenilir alias
    /// </summary>
    public double ConfidenceScore { get; set; } = 0.5;

    /// <summary>
    /// Alias aktif mi? (Düşük başarı oranı olanlar devre dışı bırakılır)
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Manuel olarak mı eklendi? (false = otomatik öğrenildi)
    /// </summary>
    public bool IsManual { get; set; } = false;

    /// <summary>
    /// Kaynak: "user_success", "admin_manual", "pattern_mining", "global_learning"
    /// </summary>
    [MaxLength(50)]
    public string Source { get; set; } = "user_success";

    /// <summary>
    /// İlk oluşturulma tarihi
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Son kullanım tarihi
    /// </summary>
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Son güncelleme tarihi
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Şirket/Tenant bazlı terminoloji
/// Her şirketin kendi özel terimleri olabilir
/// </summary>
[Table("TenantTerminology")]
public class TenantTerminology
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Tenant ID
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Standart terim (örn: "ürün", "müşteri", "randevu")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string StandardTerm { get; set; } = string.Empty;

    /// <summary>
    /// Şirketin kullandığı terim (örn: "mal", "alıcı", "seans")
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string CustomTerm { get; set; } = string.Empty;

    /// <summary>
    /// Normalize edilmiş custom term
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string NormalizedCustomTerm { get; set; } = string.Empty;

    /// <summary>
    /// Kullanım sayısı
    /// </summary>
    public int UsageCount { get; set; } = 1;

    /// <summary>
    /// Aktif mi?
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Oluşturulma tarihi
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Son güncelleme tarihi
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Bağlam referansları - "o müşteri", "aynısından" gibi ifadeleri çözmek için
/// </summary>
[Table("ContextReferences")]
public class ContextReference
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Conversation ID
    /// </summary>
    public Guid ConversationId { get; set; }

    /// <summary>
    /// Referans tipi: "customer", "product", "appointment", "invoice"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string ReferenceType { get; set; } = string.Empty;

    /// <summary>
    /// Referans edilen entity ID
    /// </summary>
    public Guid EntityId { get; set; }

    /// <summary>
    /// Entity adı (hızlı erişim için)
    /// </summary>
    [MaxLength(200)]
    public string? EntityName { get; set; }

    /// <summary>
    /// Ek bilgiler (JSON)
    /// </summary>
    [Column(TypeName = "text")]
    public string? EntityDataJson { get; set; }

    /// <summary>
    /// Referans sırası (en son = en yüksek öncelik)
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Oluşturulma tarihi
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Son erişim tarihi
    /// </summary>
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(ConversationId))]
    public Conversation? Conversation { get; set; }
}

/// <summary>
/// Feedback kaydı - Kullanıcı geri bildirimleri
/// </summary>
[Table("UserFeedback")]
public class UserFeedback
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// User Profile ID
    /// </summary>
    public Guid UserProfileId { get; set; }

    /// <summary>
    /// Conversation ID
    /// </summary>
    public Guid? ConversationId { get; set; }

    /// <summary>
    /// İlgili Command ID
    /// </summary>
    public Guid? CommandId { get; set; }

    /// <summary>
    /// Feedback tipi: "correction", "cancellation", "confirmation", "rating"
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string FeedbackType { get; set; } = string.Empty;

    /// <summary>
    /// Orijinal mesaj
    /// </summary>
    [Column(TypeName = "text")]
    public string? OriginalMessage { get; set; }

    /// <summary>
    /// Algılanan intent
    /// </summary>
    [MaxLength(100)]
    public string? DetectedIntent { get; set; }

    /// <summary>
    /// Doğru intent (kullanıcı düzeltmesi varsa)
    /// </summary>
    [MaxLength(100)]
    public string? CorrectIntent { get; set; }

    /// <summary>
    /// Kullanıcı yorumu
    /// </summary>
    [Column(TypeName = "text")]
    public string? UserComment { get; set; }

    /// <summary>
    /// Rating (1-5, opsiyonel)
    /// </summary>
    public int? Rating { get; set; }

    /// <summary>
    /// İşlendi mi? (Öğrenme sistemine aktarıldı mı)
    /// </summary>
    public bool IsProcessed { get; set; } = false;

    /// <summary>
    /// Oluşturulma tarihi
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    [ForeignKey(nameof(UserProfileId))]
    public UserProfile? UserProfile { get; set; }
}

/// <summary>
/// Global öğrenme havuzu - Tüm tenant'lardan anonim öğrenme
/// </summary>
[Table("GlobalLearningPool")]
public class GlobalLearningEntry
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>
    /// Pattern/Alias
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string Pattern { get; set; } = string.Empty;

    /// <summary>
    /// Normalize edilmiş pattern
    /// </summary>
    [Required]
    [MaxLength(500)]
    public string NormalizedPattern { get; set; } = string.Empty;

    /// <summary>
    /// Hedef intent
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string Intent { get; set; } = string.Empty;

    /// <summary>
    /// Dil
    /// </summary>
    [MaxLength(10)]
    public string Language { get; set; } = "tr";

    /// <summary>
    /// Kaç farklı tenant'ta kullanıldı
    /// </summary>
    public int TenantCount { get; set; } = 1;

    /// <summary>
    /// Toplam kullanım sayısı
    /// </summary>
    public int TotalUsageCount { get; set; } = 1;

    /// <summary>
    /// Ortalama başarı oranı
    /// </summary>
    public double AverageSuccessRate { get; set; } = 1.0;

    /// <summary>
    /// Global güven skoru
    /// </summary>
    public double GlobalConfidenceScore { get; set; } = 0.5;

    /// <summary>
    /// Aktif mi?
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Oluşturulma tarihi
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Son güncelleme tarihi
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
