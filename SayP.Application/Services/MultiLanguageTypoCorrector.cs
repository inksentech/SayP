using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace SayP.Application.Services;

/// <summary>
/// Multi-language typo corrector supporting Turkish and English
/// Automatically detects language and applies appropriate corrections
/// </summary>
public class MultiLanguageTypoCorrector
{
    private readonly TurkishTypoCorrector _turkishCorrector;
    private readonly EnglishTypoCorrector _englishCorrector;
    private readonly ILogger<MultiLanguageTypoCorrector> _logger;

    public MultiLanguageTypoCorrector(
        TurkishTypoCorrector turkishCorrector,
        EnglishTypoCorrector englishCorrector,
        ILogger<MultiLanguageTypoCorrector> logger)
    {
        _turkishCorrector = turkishCorrector;
        _englishCorrector = englishCorrector;
        _logger = logger;
    }

    /// <summary>
    /// Auto-correct text with automatic language detection
    /// </summary>
    public string AutoCorrect(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return message;

        var language = DetectLanguage(message);
        
        _logger.LogDebug("Detected language: {Language} for message: {Message}", language, message);

        return language switch
        {
            Language.Turkish => _turkishCorrector.AutoCorrect(message),
            Language.English => _englishCorrector.AutoCorrect(message),
            Language.Mixed => CorrectMixedLanguage(message),
            _ => message
        };
    }

    /// <summary>
    /// Detect the primary language of the message
    /// </summary>
    private Language DetectLanguage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return Language.Unknown;

        var words = message.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        
        int turkishScore = 0;
        int englishScore = 0;

        foreach (var word in words)
        {
            var cleanWord = word.ToLowerInvariant();
            
            // Skip numbers and special characters
            if (Regex.IsMatch(cleanWord, @"^\d+$|^[^\w]+$"))
                continue;

            // Turkish-specific characters
            if (ContainsTurkishCharacters(cleanWord))
            {
                turkishScore += 3; // Strong indicator
            }

            // Turkish-specific words
            if (IsTurkishWord(cleanWord))
            {
                turkishScore += 2;
            }

            // English-specific words
            if (IsEnglishWord(cleanWord))
            {
                englishScore += 2;
            }
        }

        _logger.LogDebug("Language detection scores - Turkish: {Turkish}, English: {English}", 
            turkishScore, englishScore);

        // If both languages detected, it's mixed
        if (turkishScore > 0 && englishScore > 0 && Math.Abs(turkishScore - englishScore) < 3)
        {
            return Language.Mixed;
        }

        // Determine primary language
        if (turkishScore > englishScore)
            return Language.Turkish;
        else if (englishScore > turkishScore)
            return Language.English;
        
        // Default to Turkish for business context
        return Language.Turkish;
    }

    /// <summary>
    /// Correct mixed language text (word by word)
    /// </summary>
    private string CorrectMixedLanguage(string message)
    {
        var words = message.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var correctedWords = new List<string>();

        foreach (var word in words)
        {
            var cleanWord = word.ToLowerInvariant();
            
            // Skip numbers and special characters
            if (Regex.IsMatch(cleanWord, @"^\d+$|^[^\w]+$"))
            {
                correctedWords.Add(word);
                continue;
            }

            // Try Turkish first if it contains Turkish characters
            if (ContainsTurkishCharacters(cleanWord))
            {
                correctedWords.Add(_turkishCorrector.AutoCorrect(word));
            }
            // Otherwise try English
            else
            {
                correctedWords.Add(_englishCorrector.AutoCorrect(word));
            }
        }

        return string.Join(" ", correctedWords);
    }

    /// <summary>
    /// Check if word contains Turkish-specific characters
    /// </summary>
    private bool ContainsTurkishCharacters(string word)
    {
        return word.Any(c => "ğüşıöçĞÜŞİÖÇ".Contains(c));
    }

    /// <summary>
    /// Check if word is a common Turkish word
    /// </summary>
    private bool IsTurkishWord(string word)
    {
        var turkishWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ürün", "müşteri", "fatura", "sözleşme", "sipariş", "ödeme",
            "ekle", "oluştur", "güncelle", "sil", "listele", "göster",
            "bugün", "yarın", "dün", "için", "ile", "ve", "veya",
            "bir", "iki", "üç", "dört", "beş"
        };

        return turkishWords.Contains(word);
    }

    /// <summary>
    /// Check if word is a common English word
    /// </summary>
    private bool IsEnglishWord(string word)
    {
        var englishWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "product", "customer", "invoice", "contract", "order", "payment",
            "create", "update", "delete", "list", "show", "search",
            "today", "tomorrow", "yesterday", "for", "with", "and", "or",
            "one", "two", "three", "four", "five"
        };

        return englishWords.Contains(word);
    }

    /// <summary>
    /// Get language statistics for a message
    /// </summary>
    public LanguageStats GetLanguageStats(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new LanguageStats();

        var words = message.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        var stats = new LanguageStats
        {
            TotalWords = words.Length,
            DetectedLanguage = DetectLanguage(message)
        };

        foreach (var word in words)
        {
            var cleanWord = word.ToLowerInvariant();
            
            if (ContainsTurkishCharacters(cleanWord))
                stats.TurkishWords++;
            else if (IsEnglishWord(cleanWord))
                stats.EnglishWords++;
        }

        return stats;
    }

    private enum Language
    {
        Unknown,
        Turkish,
        English,
        Mixed
    }
}

/// <summary>
/// Language detection statistics
/// </summary>
public class LanguageStats
{
    public int TotalWords { get; set; }
    public int TurkishWords { get; set; }
    public int EnglishWords { get; set; }
    public object DetectedLanguage { get; set; } = "Unknown";
    
    public double TurkishPercentage => TotalWords > 0 ? (double)TurkishWords / TotalWords * 100 : 0;
    public double EnglishPercentage => TotalWords > 0 ? (double)EnglishWords / TotalWords * 100 : 0;
}
