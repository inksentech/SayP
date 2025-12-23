using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace SayP.Application.Services;

/// <summary>
/// Advanced Turkish typo correction with fuzzy matching
/// </summary>
public class TurkishTypoCorrector
{
    private readonly ILogger<TurkishTypoCorrector> _logger;
    private readonly Dictionary<string, string> _commonTypos;
    private readonly HashSet<string> _turkishDictionary;
    private const int MAX_LEVENSHTEIN_DISTANCE = 2;

    public TurkishTypoCorrector(ILogger<TurkishTypoCorrector> logger)
    {
        _logger = logger;
        _commonTypos = InitializeCommonTypos();
        _turkishDictionary = InitializeTurkishDictionary();
    }

    /// <summary>
    /// Auto-correct Turkish text with fuzzy matching
    /// </summary>
    public string AutoCorrect(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        var words = message.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var correctedWords = new List<string>();
        var correctionsMade = false;

        foreach (var word in words)
        {
            var corrected = CorrectWord(word);
            correctedWords.Add(corrected);
            
            if (corrected != word)
            {
                correctionsMade = true;
                _logger.LogDebug("Corrected: '{Original}' → '{Corrected}'", word, corrected);
            }
        }

        if (correctionsMade)
        {
            var correctedMessage = string.Join(" ", correctedWords);
            _logger.LogInformation("Message corrected: '{Original}' → '{Corrected}'", message, correctedMessage);
            return correctedMessage;
        }

        return message;
    }

    /// <summary>
    /// Correct a single word
    /// </summary>
    private string CorrectWord(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return word;

        var cleanWord = word.ToLowerInvariant();
        
        // Check if it's a number or special character
        if (Regex.IsMatch(cleanWord, @"^\d+$|^[^\w]+$"))
            return word;

        // Step 1: Check common typos dictionary
        if (_commonTypos.TryGetValue(cleanWord, out var commonCorrection))
        {
            return PreserveCase(word, commonCorrection);
        }

        // Step 2: Check if word is in dictionary (correct)
        if (_turkishDictionary.Contains(cleanWord))
        {
            return word;
        }

        // Step 3: Find closest match using Levenshtein distance
        var bestMatch = FindClosestMatch(cleanWord);
        if (bestMatch != null && bestMatch.Distance <= MAX_LEVENSHTEIN_DISTANCE)
        {
            return PreserveCase(word, bestMatch.Word);
        }

        // Step 4: No correction found, return original
        return word;
    }

    /// <summary>
    /// Find closest matching word in dictionary
    /// </summary>
    private MatchResult? FindClosestMatch(string word)
    {
        MatchResult? bestMatch = null;
        var minDistance = int.MaxValue;

        // Only check words with similar length (±2 characters)
        var candidates = _turkishDictionary
            .Where(w => Math.Abs(w.Length - word.Length) <= 2)
            .ToList();

        foreach (var candidate in candidates)
        {
            var distance = ComputeLevenshteinDistance(word, candidate);
            
            if (distance < minDistance)
            {
                minDistance = distance;
                bestMatch = new MatchResult { Word = candidate, Distance = distance };
            }

            // Early exit if perfect match found
            if (distance == 0)
                break;
        }

        return bestMatch;
    }

    /// <summary>
    /// Compute Levenshtein distance between two strings
    /// </summary>
    private int ComputeLevenshteinDistance(string str1, string str2)
    {
        var matrix = new int[str1.Length + 1, str2.Length + 1];

        for (int i = 0; i <= str1.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= str2.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= str1.Length; i++)
        {
            for (int j = 1; j <= str2.Length; j++)
            {
                var cost = str1[i - 1] == str2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[str1.Length, str2.Length];
    }

    /// <summary>
    /// Preserve original case (uppercase/lowercase)
    /// </summary>
    private string PreserveCase(string original, string corrected)
    {
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(corrected))
            return corrected;

        // All uppercase
        if (original.All(char.IsUpper))
            return corrected.ToUpperInvariant();

        // First letter uppercase
        if (char.IsUpper(original[0]))
            return char.ToUpper(corrected[0]) + corrected.Substring(1).ToLower();

        // All lowercase
        return corrected.ToLower();
    }

    /// <summary>
    /// Initialize common Turkish typos dictionary
    /// </summary>
    private Dictionary<string, string> InitializeCommonTypos()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Common product/business typos
            { "laptp", "laptop" },
            { "loptop", "laptop" },
            { "laptob", "laptop" },
            { "labtop", "laptop" },
            
            // Turkish business words
            { "müşeri", "müşteri" },
            { "musteri", "müşteri" },
            { "müsteri", "müşteri" },
            { "musterı", "müşteri" },
            
            { "fatira", "fatura" },
            { "fatura", "fatura" },
            { "faturaa", "fatura" },
            
            { "urun", "ürün" },
            { "urün", "ürün" },
            { "ürün", "ürün" },
            
            { "sozlesme", "sözleşme" },
            { "sözleşme", "sözleşme" },
            { "sozlesmee", "sözleşme" },
            
            // Common verbs
            { "ekl", "ekle" },
            { "eklee", "ekle" },
            { "eklle", "ekle" },
            
            { "olustur", "oluştur" },
            { "olusturr", "oluştur" },
            { "oluşturr", "oluştur" },
            
            { "güncele", "güncelle" },
            { "guncelle", "güncelle" },
            { "güncellee", "güncelle" },
            { "guncellee", "güncelle" },
            
            { "sill", "sil" },
            { "siil", "sil" },
            
            { "listele", "listele" },
            { "listle", "listele" },
            { "listelee", "listele" },
            
            { "göster", "göster" },
            { "goster", "göster" },
            { "gösterr", "göster" },
            
            // Common nouns
            { "fiyat", "fiyat" },
            { "fiyatt", "fiyat" },
            { "fiyaat", "fiyat" },
            
            { "tutar", "tutar" },
            { "tutarr", "tutar" },
            
            { "miktar", "miktar" },
            { "miktarr", "miktar" },
            
            { "adet", "adet" },
            { "adett", "adet" },
            
            { "stok", "stok" },
            { "stokk", "stok" },
            
            // Common adjectives
            { "yeni", "yeni" },
            { "yenii", "yeni" },
            
            { "eski", "eski" },
            { "eskii", "eski" },
            
            // Numbers and units
            { "tl", "TL" },
            { "tll", "TL" },
            
            { "kdv", "KDV" },
            { "kdvv", "KDV" },
            
            // Common mistakes
            { "birr", "bir" },
            { "ikii", "iki" },
            { "üçç", "üç" },
            { "dörtt", "dört" },
            { "beşş", "beş" }
        };
    }

    /// <summary>
    /// Initialize Turkish dictionary with common business words
    /// </summary>
    private HashSet<string> InitializeTurkishDictionary()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Products
            "laptop", "bilgisayar", "telefon", "tablet", "klavye", "mouse", "fare",
            "monitör", "ekran", "yazıcı", "tarayıcı", "kamera", "kulaklık",
            
            // Business entities
            "ürün", "müşteri", "fatura", "sözleşme", "sipariş", "ödeme", "tahsilat",
            "stok", "envanter", "kategori", "marka", "model", "seri", "kod",
            
            // Actions (verbs)
            "ekle", "oluştur", "yarat", "kaydet", "sil", "kaldır", "iptal",
            "güncelle", "değiştir", "düzenle", "listele", "göster", "ara", "bul",
            "getir", "kes", "gönder", "al", "ver", "yap", "et",
            
            // Attributes
            "ad", "adı", "isim", "ismi", "fiyat", "fiyatı", "tutar", "tutarı",
            "miktar", "miktarı", "adet", "adedi", "birim", "birimi",
            "açıklama", "açıklaması", "detay", "detayı", "bilgi", "bilgisi",
            "tarih", "tarihi", "saat", "saati", "gün", "günü", "ay", "ayı", "yıl", "yılı",
            
            // Common words
            "yeni", "eski", "son", "ilk", "tüm", "tümü", "hepsi", "bazı",
            "bir", "iki", "üç", "dört", "beş", "altı", "yedi", "sekiz", "dokuz", "on",
            "bugün", "dün", "yarın", "şimdi", "sonra", "önce",
            "için", "ile", "ve", "veya", "ama", "ancak", "fakat",
            
            // Business terms
            "kdv", "vergi", "indirim", "kampanya", "taksit", "peşin", "nakit",
            "kredi", "banka", "hesap", "dekont", "makbuz", "fiş",
            
            // Status
            "aktif", "pasif", "beklemede", "onaylandı", "reddedildi", "iptal",
            "tamamlandı", "devam", "ediyor", "başladı", "bitti",
            
            // Units
            "adet", "kilo", "gram", "litre", "metre", "santimetre",
            "kutu", "paket", "koli", "palet", "ton",
            
            // Common phrases
            "merhaba", "selam", "günaydın", "iyi", "günler", "teşekkür",
            "ederim", "lütfen", "tamam", "evet", "hayır", "olur", "olmaz"
        };
    }

    private class MatchResult
    {
        public string Word { get; set; } = string.Empty;
        public int Distance { get; set; }
    }
}
