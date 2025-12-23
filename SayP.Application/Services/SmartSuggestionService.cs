using Microsoft.Extensions.Logging;

namespace SayP.Application.Services;

/// <summary>
/// Provides intelligent suggestions when entities are not found
/// </summary>
public class SmartSuggestionService
{
    private readonly FuzzyMatchingService _fuzzyMatching;
    private readonly ILogger<SmartSuggestionService> _logger;

    public SmartSuggestionService(
        FuzzyMatchingService fuzzyMatching,
        ILogger<SmartSuggestionService> logger)
    {
        _fuzzyMatching = fuzzyMatching;
        _logger = logger;
    }

    /// <summary>
    /// Generate suggestion response for services/products
    /// </summary>
    public SuggestionResponse GenerateServiceSuggestion<T>(
        string searchTerm,
        IEnumerable<T> availableServices,
        Func<T, string> nameSelector,
        Func<T, string>? priceSelector = null,
        string language = "tr")
    {
        var matches = _fuzzyMatching.FindBestMatches(
            searchTerm, 
            availableServices, 
            nameSelector,
            threshold: 0.5,
            maxResults: 3
        );

        if (!matches.Any())
        {
            return new SuggestionResponse
            {
                Type = SuggestionType.CreateNew,
                Message = language == "tr" 
                    ? $"'{searchTerm}' servisi bulunamadı. Yeni servis olarak oluşturmak ister misiniz?\n\n✅ Evet, oluştur\n❌ Hayır, iptal et"
                    : $"Service '{searchTerm}' not found. Would you like to create it as a new service?\n\n✅ Yes, create\n❌ No, cancel",
                SearchTerm = searchTerm,
                Suggestions = new List<Suggestion>()
            };
        }

        var suggestions = matches.Select((m, index) => new Suggestion
        {
            Index = index + 1,
            Name = nameSelector(m.Item),
            Description = priceSelector != null ? priceSelector(m.Item) : null,
            Similarity = m.Similarity,
            Data = m.Item
        }).ToList();

        var message = language == "tr"
            ? BuildTurkishSuggestionMessage(searchTerm, suggestions, "servis")
            : BuildEnglishSuggestionMessage(searchTerm, suggestions, "service");

        return new SuggestionResponse
        {
            Type = SuggestionType.SelectFromList,
            Message = message,
            SearchTerm = searchTerm,
            Suggestions = suggestions
        };
    }

    /// <summary>
    /// Generate suggestion response for customers
    /// </summary>
    public SuggestionResponse GenerateCustomerSuggestion<T>(
        string searchTerm,
        IEnumerable<T> availableCustomers,
        Func<T, string> nameSelector,
        Func<T, string>? phoneSelector = null,
        string language = "tr")
    {
        var matches = _fuzzyMatching.FindBestMatches(
            searchTerm, 
            availableCustomers, 
            nameSelector,
            threshold: 0.5,
            maxResults: 3
        );

        if (!matches.Any())
        {
            return new SuggestionResponse
            {
                Type = SuggestionType.CreateNew,
                Message = language == "tr" 
                    ? $"'{searchTerm}' adında müşteri bulunamadı. Yeni müşteri olarak kaydetmek ister misiniz?\n\n✅ Evet, kaydet\n❌ Hayır, iptal et"
                    : $"Customer '{searchTerm}' not found. Would you like to register as a new customer?\n\n✅ Yes, register\n❌ No, cancel",
                SearchTerm = searchTerm,
                Suggestions = new List<Suggestion>()
            };
        }

        var suggestions = matches.Select((m, index) => new Suggestion
        {
            Index = index + 1,
            Name = nameSelector(m.Item),
            Description = phoneSelector != null ? phoneSelector(m.Item) : null,
            Similarity = m.Similarity,
            Data = m.Item
        }).ToList();

        var message = language == "tr"
            ? BuildTurkishSuggestionMessage(searchTerm, suggestions, "müşteri")
            : BuildEnglishSuggestionMessage(searchTerm, suggestions, "customer");

        return new SuggestionResponse
        {
            Type = SuggestionType.SelectFromList,
            Message = message,
            SearchTerm = searchTerm,
            Suggestions = suggestions
        };
    }

    /// <summary>
    /// Generate suggestion response for time slots
    /// </summary>
    public SuggestionResponse GenerateTimeSlotSuggestion(
        DateTime date,
        TimeSpan requestedTime,
        List<TimeSpan> availableSlots,
        string language = "tr")
    {
        if (!availableSlots.Any())
        {
            return new SuggestionResponse
            {
                Type = SuggestionType.NoAlternatives,
                Message = language == "tr"
                    ? $"❌ {date:dd MMMM yyyy} tarihinde müsait saat bulunamadı.\n\nBaşka bir tarih deneyin."
                    : $"❌ No available time slots on {date:dd MMMM yyyy}.\n\nPlease try another date.",
                SearchTerm = requestedTime.ToString(@"hh\:mm"),
                Suggestions = new List<Suggestion>()
            };
        }

        var suggestions = availableSlots.Select((slot, index) => new Suggestion
        {
            Index = index + 1,
            Name = slot.ToString(@"hh\:mm"),
            Description = null,
            Similarity = 1.0,
            Data = slot
        }).ToList();

        var message = language == "tr"
            ? $"⏰ {date:dd MMMM yyyy} - {requestedTime:hh\\:mm} dolu.\n\n📅 Müsait saatler:\n" +
              string.Join("\n", suggestions.Select(s => $"{s.Index}. {s.Name}")) +
              "\n\nHangisini tercih edersiniz? (Numara ile cevap verin)"
            : $"⏰ {date:dd MMMM yyyy} - {requestedTime:hh\\:mm} is not available.\n\n📅 Available times:\n" +
              string.Join("\n", suggestions.Select(s => $"{s.Index}. {s.Name}")) +
              "\n\nWhich one do you prefer? (Reply with number)";

        return new SuggestionResponse
        {
            Type = SuggestionType.SelectFromList,
            Message = message,
            SearchTerm = requestedTime.ToString(@"hh\:mm"),
            Suggestions = suggestions
        };
    }

    private string BuildTurkishSuggestionMessage(string searchTerm, List<Suggestion> suggestions, string entityType)
    {
        var message = $"🔍 '{searchTerm}' bulunamadı.\n\n";
        message += $"📋 Benzer {entityType}ler:\n";
        
        foreach (var suggestion in suggestions)
        {
            message += $"{suggestion.Index}. {suggestion.Name}";
            if (!string.IsNullOrEmpty(suggestion.Description))
                message += $" - {suggestion.Description}";
            message += "\n";
        }

        message += $"\n❓ Bunlardan birini mi demek istediniz? (Numara ile cevap verin)\n";
        message += $"✨ Veya yeni {entityType} oluşturmak için 'Yeni' yazın";

        return message;
    }

    private string BuildEnglishSuggestionMessage(string searchTerm, List<Suggestion> suggestions, string entityType)
    {
        var message = $"🔍 '{searchTerm}' not found.\n\n";
        message += $"📋 Similar {entityType}s:\n";
        
        foreach (var suggestion in suggestions)
        {
            message += $"{suggestion.Index}. {suggestion.Name}";
            if (!string.IsNullOrEmpty(suggestion.Description))
                message += $" - {suggestion.Description}";
            message += "\n";
        }

        message += $"\n❓ Did you mean one of these? (Reply with number)\n";
        message += $"✨ Or type 'New' to create a new {entityType}";

        return message;
    }
}

public class SuggestionResponse
{
    public SuggestionType Type { get; set; }
    public string Message { get; set; } = string.Empty;
    public string SearchTerm { get; set; } = string.Empty;
    public List<Suggestion> Suggestions { get; set; } = new();
}

public class Suggestion
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Similarity { get; set; }
    public object? Data { get; set; }
}

public enum SuggestionType
{
    SelectFromList,
    CreateNew,
    NoAlternatives
}
