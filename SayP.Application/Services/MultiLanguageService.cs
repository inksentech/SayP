using Microsoft.Extensions.Logging;
using SayP.Domain.Models;
using System.Text.RegularExpressions;

namespace SayP.Application.Services;

/// <summary>
/// Multi-Language Service - Çoklu dil desteği sağlar
/// 
/// Özellikler:
/// 1. Otomatik Dil Algılama - Mesaj dilini algılar
/// 2. Çoklu Dil Alias'ları - Her endpoint için TR + EN alias'lar
/// 3. Dil Bazlı Yanıt - Kullanıcının dilinde yanıt verir
/// 4. Dil Tercihi Kaydetme - Kullanıcının tercih ettiği dili hatırlar
/// </summary>
public class MultiLanguageService
{
    private readonly ILogger<MultiLanguageService> _logger;

    // Türkçe karakterler ve kelimeler
    private static readonly HashSet<char> TurkishChars = new() { 'ç', 'ğ', 'ı', 'ö', 'ş', 'ü', 'Ç', 'Ğ', 'İ', 'Ö', 'Ş', 'Ü' };
    
    private static readonly HashSet<string> TurkishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Greetings
        "merhaba", "selam", "günaydın", "iyi", "günler", "akşamlar", "geceler",
        // Common words
        "evet", "hayır", "tamam", "lütfen", "teşekkür", "teşekkürler", "rica", "ederim",
        // Entities
        "müşteri", "ürün", "hizmet", "randevu", "fatura", "şablon", "kod",
        // Actions
        "oluştur", "ekle", "sil", "güncelle", "listele", "göster", "bul", "ara", "kaydet",
        // Time
        "bugün", "yarın", "dün", "şimdi", "sonra", "önce", "hafta", "ay", "yıl",
        // Question words
        "ne", "nasıl", "neden", "kim", "nerede", "ne zaman", "kaç",
        // Connectors
        "ve", "veya", "ile", "için", "den", "dan", "de", "da", "nin", "nın", "e", "a",
        // Numbers
        "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz", "on"
    };

    private static readonly HashSet<string> EnglishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Greetings
        "hello", "hi", "hey", "good", "morning", "afternoon", "evening", "night",
        // Common words
        "yes", "no", "okay", "ok", "please", "thank", "thanks", "you", "welcome",
        // Entities
        "customer", "product", "service", "appointment", "invoice", "template", "code",
        // Actions
        "create", "add", "delete", "remove", "update", "list", "show", "find", "search", "save",
        // Time
        "today", "tomorrow", "yesterday", "now", "later", "before", "week", "month", "year",
        // Question words
        "what", "how", "why", "who", "where", "when", "which",
        // Connectors
        "and", "or", "with", "for", "from", "to", "the", "a", "an", "of", "in", "on",
        // Numbers
        "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"
    };

    // Çoklu dil alias'ları
    private static readonly Dictionary<string, MultiLanguageAlias> IntentAliases = new()
    {
        // Customer
        { "create_customer", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "müşteri ekle", "müşteri oluştur", "yeni müşteri", "müşteri kaydet", "alıcı ekle" },
            EnglishAliases = new[] { "add customer", "create customer", "new customer", "save customer", "add client" }
        }},
        { "list_customers", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "müşterileri listele", "müşterileri göster", "müşteri listesi", "tüm müşteriler" },
            EnglishAliases = new[] { "list customers", "show customers", "customer list", "all customers" }
        }},
        { "update_customer", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "müşteri güncelle", "müşteri düzenle", "müşteri değiştir" },
            EnglishAliases = new[] { "update customer", "edit customer", "modify customer" }
        }},
        { "delete_customer", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "müşteri sil", "müşteriyi kaldır" },
            EnglishAliases = new[] { "delete customer", "remove customer" }
        }},

        // Product
        { "create_product", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "ürün ekle", "ürün oluştur", "yeni ürün", "ürün kaydet", "hizmet ekle", "hizmet oluştur" },
            EnglishAliases = new[] { "add product", "create product", "new product", "save product", "add service", "create service" }
        }},
        { "list_products", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "ürünleri listele", "ürünleri göster", "ürün listesi", "tüm ürünler", "hizmetleri göster" },
            EnglishAliases = new[] { "list products", "show products", "product list", "all products", "show services" }
        }},
        { "update_product", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "ürün güncelle", "ürün düzenle", "ürün değiştir", "fiyat güncelle" },
            EnglishAliases = new[] { "update product", "edit product", "modify product", "update price" }
        }},
        { "delete_product", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "ürün sil", "ürünü kaldır" },
            EnglishAliases = new[] { "delete product", "remove product" }
        }},

        // Appointment
        { "create_appointment", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "randevu oluştur", "randevu al", "randevu ekle", "randevu yap", "randevu ayarla" },
            EnglishAliases = new[] { "create appointment", "book appointment", "add appointment", "make appointment", "schedule appointment" }
        }},
        { "list_appointments", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "randevuları listele", "randevuları göster", "randevu listesi", "bugünkü randevular", "yarınki randevular" },
            EnglishAliases = new[] { "list appointments", "show appointments", "appointment list", "today's appointments", "tomorrow's appointments" }
        }},
        { "cancel_appointment", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "randevu iptal", "randevuyu iptal et", "randevu sil" },
            EnglishAliases = new[] { "cancel appointment", "delete appointment", "remove appointment" }
        }},

        // Invoice
        { "create_invoice", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "fatura oluştur", "fatura kes", "fatura ekle", "yeni fatura" },
            EnglishAliases = new[] { "create invoice", "generate invoice", "add invoice", "new invoice" }
        }},
        { "list_invoices", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "faturaları listele", "faturaları göster", "fatura listesi", "tüm faturalar" },
            EnglishAliases = new[] { "list invoices", "show invoices", "invoice list", "all invoices" }
        }},

        // Code Template
        { "create_code_template", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "şablon oluştur", "kod şablonu ekle", "yeni şablon", "template oluştur" },
            EnglishAliases = new[] { "create template", "add code template", "new template", "create code template" }
        }},
        { "list_code_templates", new MultiLanguageAlias
        {
            TurkishAliases = new[] { "şablonları listele", "şablonları göster", "şablon listesi" },
            EnglishAliases = new[] { "list templates", "show templates", "template list" }
        }}
    };

    public MultiLanguageService(ILogger<MultiLanguageService> logger)
    {
        _logger = logger;
    }

    #region Language Detection

    /// <summary>
    /// Mesajın dilini algıla
    /// </summary>
    public LanguageDetectionResult DetectLanguage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return new LanguageDetectionResult { Language = "tr", Confidence = 0.5 };
        }

        var result = new LanguageDetectionResult();
        
        // Check for Turkish characters (strong indicator)
        var turkishCharCount = message.Count(c => TurkishChars.Contains(c));
        if (turkishCharCount > 0)
        {
            result.Language = "tr";
            result.Confidence = Math.Min(0.9 + (turkishCharCount * 0.02), 1.0);
            result.Reason = "Turkish characters detected";
            return result;
        }

        // Word-based detection
        var words = Regex.Split(message.ToLower(), @"\W+")
            .Where(w => w.Length > 1)
            .ToList();

        int turkishScore = 0;
        int englishScore = 0;

        foreach (var word in words)
        {
            if (TurkishWords.Contains(word)) turkishScore++;
            if (EnglishWords.Contains(word)) englishScore++;
        }

        // Calculate confidence
        var totalMatches = turkishScore + englishScore;
        if (totalMatches == 0)
        {
            // No matches, default to Turkish
            result.Language = "tr";
            result.Confidence = 0.5;
            result.Reason = "No language indicators, defaulting to Turkish";
        }
        else if (turkishScore > englishScore)
        {
            result.Language = "tr";
            result.Confidence = (double)turkishScore / totalMatches;
            result.Reason = $"Turkish words: {turkishScore}, English words: {englishScore}";
        }
        else if (englishScore > turkishScore)
        {
            result.Language = "en";
            result.Confidence = (double)englishScore / totalMatches;
            result.Reason = $"English words: {englishScore}, Turkish words: {turkishScore}";
        }
        else
        {
            // Equal scores, default to Turkish
            result.Language = "tr";
            result.Confidence = 0.5;
            result.Reason = "Equal scores, defaulting to Turkish";
        }

        _logger.LogDebug("Language detected: {Language} (confidence: {Confidence:P0}) - {Reason}",
            result.Language, result.Confidence, result.Reason);

        return result;
    }

    #endregion

    #region Multi-Language Aliases

    /// <summary>
    /// Intent için dil bazlı alias'ları getir
    /// </summary>
    public string[] GetAliasesForIntent(string intent, string? language = null)
    {
        if (!IntentAliases.TryGetValue(intent, out var aliases))
        {
            return Array.Empty<string>();
        }

        if (language == "en")
        {
            return aliases.EnglishAliases;
        }
        else if (language == "tr")
        {
            return aliases.TurkishAliases;
        }
        else
        {
            // Return both
            return aliases.TurkishAliases.Concat(aliases.EnglishAliases).ToArray();
        }
    }

    /// <summary>
    /// Tüm intent'ler için alias'ları endpoint'lere ekle
    /// </summary>
    public void EnrichEndpointsWithAliases(List<DiscoveredEndpoint> endpoints)
    {
        foreach (var endpoint in endpoints)
        {
            if (IntentAliases.TryGetValue(endpoint.Intent, out var aliases))
            {
                var existingAliases = endpoint.Aliases.ToList();
                
                // Add Turkish aliases
                foreach (var alias in aliases.TurkishAliases)
                {
                    if (!existingAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    {
                        existingAliases.Add(alias);
                    }
                }

                // Add English aliases
                foreach (var alias in aliases.EnglishAliases)
                {
                    if (!existingAliases.Contains(alias, StringComparer.OrdinalIgnoreCase))
                    {
                        existingAliases.Add(alias);
                    }
                }

                endpoint.Aliases = existingAliases.ToArray();
            }
        }

        _logger.LogDebug("Enriched {Count} endpoints with multi-language aliases", endpoints.Count);
    }

    /// <summary>
    /// Mesaj için en iyi alias eşleşmesini bul
    /// </summary>
    public (string? Intent, double Confidence, string? Language)? FindBestAliasMatch(string message)
    {
        var lowerMessage = message.ToLower().Trim();
        var detectedLang = DetectLanguage(message);

        double bestScore = 0;
        string? bestIntent = null;
        string? matchedLanguage = null;

        foreach (var (intent, aliases) in IntentAliases)
        {
            // Check Turkish aliases
            foreach (var alias in aliases.TurkishAliases)
            {
                var score = CalculateMatchScore(lowerMessage, alias.ToLower());
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIntent = intent;
                    matchedLanguage = "tr";
                }
            }

            // Check English aliases
            foreach (var alias in aliases.EnglishAliases)
            {
                var score = CalculateMatchScore(lowerMessage, alias.ToLower());
                if (score > bestScore)
                {
                    bestScore = score;
                    bestIntent = intent;
                    matchedLanguage = "en";
                }
            }
        }

        if (bestScore >= 0.7 && bestIntent != null)
        {
            return (bestIntent, bestScore, matchedLanguage);
        }

        return null;
    }

    private double CalculateMatchScore(string message, string alias)
    {
        // Exact match
        if (message == alias) return 1.0;

        // Contains match
        if (message.Contains(alias)) return 0.9;
        if (alias.Contains(message)) return 0.8;

        // Word overlap
        var messageWords = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var aliasWords = alias.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var commonWords = messageWords.Intersect(aliasWords, StringComparer.OrdinalIgnoreCase).Count();
        var totalWords = Math.Max(messageWords.Length, aliasWords.Length);

        if (totalWords == 0) return 0;

        return (double)commonWords / totalWords;
    }

    #endregion

    #region Response Translation

    /// <summary>
    /// Dil bazlı yanıt seç
    /// </summary>
    public string GetLocalizedResponse(string turkishMessage, string englishMessage, string language)
    {
        return language == "en" ? englishMessage : turkishMessage;
    }

    /// <summary>
    /// Sistem mesajlarını dile göre getir
    /// </summary>
    public SystemMessages GetSystemMessages(string language)
    {
        return language == "en" ? SystemMessages.English : SystemMessages.Turkish;
    }

    #endregion
}

#region Models

/// <summary>
/// Dil algılama sonucu
/// </summary>
public class LanguageDetectionResult
{
    public string Language { get; set; } = "tr";
    public double Confidence { get; set; }
    public string? Reason { get; set; }
}

/// <summary>
/// Çoklu dil alias'ları
/// </summary>
public class MultiLanguageAlias
{
    public string[] TurkishAliases { get; set; } = Array.Empty<string>();
    public string[] EnglishAliases { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Sistem mesajları
/// </summary>
public class SystemMessages
{
    public string WelcomeMessage { get; set; } = string.Empty;
    public string ProcessingMessage { get; set; } = string.Empty;
    public string SuccessMessage { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public string ConfirmationPrompt { get; set; } = string.Empty;
    public string CancellationMessage { get; set; } = string.Empty;
    public string InvalidInputMessage { get; set; } = string.Empty;
    public string HelpMessage { get; set; } = string.Empty;

    public static SystemMessages Turkish => new()
    {
        WelcomeMessage = "👋 Merhaba! Size nasıl yardımcı olabilirim?",
        ProcessingMessage = "⏳ İşleminiz yapılıyor, lütfen bekleyin...",
        SuccessMessage = "✅ İşlem başarıyla tamamlandı!",
        ErrorMessage = "❌ Bir hata oluştu. Lütfen tekrar deneyin.",
        ConfirmationPrompt = "Bu işlemi onaylıyor musunuz? (Evet/Hayır)",
        CancellationMessage = "❌ İşlem iptal edildi.",
        InvalidInputMessage = "❓ Geçersiz giriş. Lütfen tekrar deneyin.",
        HelpMessage = "📚 Yardım için 'yardım' yazabilirsiniz."
    };

    public static SystemMessages English => new()
    {
        WelcomeMessage = "👋 Hello! How can I help you?",
        ProcessingMessage = "⏳ Processing your request, please wait...",
        SuccessMessage = "✅ Operation completed successfully!",
        ErrorMessage = "❌ An error occurred. Please try again.",
        ConfirmationPrompt = "Do you confirm this operation? (Yes/No)",
        CancellationMessage = "❌ Operation cancelled.",
        InvalidInputMessage = "❓ Invalid input. Please try again.",
        HelpMessage = "📚 Type 'help' for assistance."
    };
}

#endregion
