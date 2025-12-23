using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace SayP.Application.Services;

/// <summary>
/// Advanced English typo correction with fuzzy matching
/// </summary>
public class EnglishTypoCorrector
{
    private readonly ILogger<EnglishTypoCorrector> _logger;
    private readonly Dictionary<string, string> _commonTypos;
    private readonly HashSet<string> _englishDictionary;
    private const int MAX_LEVENSHTEIN_DISTANCE = 2;

    public EnglishTypoCorrector(ILogger<EnglishTypoCorrector> logger)
    {
        _logger = logger;
        _commonTypos = InitializeCommonTypos();
        _englishDictionary = InitializeEnglishDictionary();
    }

    /// <summary>
    /// Auto-correct English text with fuzzy matching
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
        if (_englishDictionary.Contains(cleanWord))
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
        var candidates = _englishDictionary
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
    /// Initialize common English typos dictionary
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
            { "compter", "computer" },
            { "computr", "computer" },
            
            // Business words
            { "custmer", "customer" },
            { "costumer", "customer" },
            { "cusotmer", "customer" },
            
            { "invoce", "invoice" },
            { "invioce", "invoice" },
            { "invocie", "invoice" },
            
            { "prodcut", "product" },
            { "porduct", "product" },
            { "proudct", "product" },
            
            { "contarct", "contract" },
            { "contrct", "contract" },
            
            // Common verbs
            { "creat", "create" },
            { "crate", "create" },
            { "craete", "create" },
            
            { "updat", "update" },
            { "updaet", "update" },
            { "udpate", "update" },
            
            { "delet", "delete" },
            { "deleet", "delete" },
            { "delte", "delete" },
            
            { "serach", "search" },
            { "seach", "search" },
            { "saerch", "search" },
            
            { "recieve", "receive" },
            { "recive", "receive" },
            
            // Common nouns
            { "pric", "price" },
            { "prcie", "price" },
            
            { "amout", "amount" },
            { "ammount", "amount" },
            
            { "quantiy", "quantity" },
            { "quanity", "quantity" },
            
            { "stok", "stock" },
            { "stokc", "stock" },
            
            // Common adjectives
            { "availabe", "available" },
            { "availble", "available" },
            { "avaliable", "available" },
            
            { "successfull", "successful" },
            { "succesful", "successful" },
            
            // Common mistakes
            { "teh", "the" },
            { "hte", "the" },
            { "taht", "that" },
            { "thsi", "this" },
            { "whcih", "which" },
            { "recieved", "received" },
            { "occured", "occurred" },
            { "seperate", "separate" },
            { "definately", "definitely" },
            { "begining", "beginning" }
        };
    }

    /// <summary>
    /// Initialize English dictionary with common business words
    /// </summary>
    private HashSet<string> InitializeEnglishDictionary()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Products
            "laptop", "computer", "phone", "tablet", "keyboard", "mouse",
            "monitor", "screen", "printer", "scanner", "camera", "headset",
            
            // Business entities
            "product", "customer", "invoice", "contract", "order", "payment",
            "stock", "inventory", "category", "brand", "model", "serial", "code",
            
            // Actions (verbs)
            "create", "add", "make", "save", "delete", "remove", "cancel",
            "update", "change", "edit", "modify", "list", "show", "search", "find",
            "get", "fetch", "send", "receive", "take", "give",
            
            // Attributes
            "name", "title", "price", "cost", "amount", "total",
            "quantity", "count", "unit", "piece",
            "description", "detail", "info", "information",
            "date", "time", "day", "month", "year",
            
            // Common words
            "new", "old", "last", "first", "all", "some", "any",
            "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
            "today", "yesterday", "tomorrow", "now", "later", "before", "after",
            "for", "with", "and", "or", "but", "if", "then",
            
            // Business terms
            "tax", "vat", "discount", "campaign", "installment", "cash",
            "credit", "bank", "account", "receipt", "bill",
            
            // Status
            "active", "inactive", "pending", "approved", "rejected", "cancelled",
            "completed", "ongoing", "started", "finished",
            
            // Units
            "piece", "kilogram", "gram", "liter", "meter", "centimeter",
            "box", "package", "carton", "pallet", "ton",
            
            // Common phrases
            "hello", "hi", "good", "morning", "afternoon", "evening", "thanks",
            "please", "okay", "yes", "no", "sure", "maybe"
        };
    }

    private class MatchResult
    {
        public string Word { get; set; } = string.Empty;
        public int Distance { get; set; }
    }
}
