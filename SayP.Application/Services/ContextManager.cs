using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SayP.Domain.Entities;
using SayP.Domain.Interfaces;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SayP.Application.Services;

/// <summary>
/// Context Manager - Konuşma bağlamını yönetir ve referansları çözer
/// 
/// Özellikler:
/// 1. Referans Çözme - "o müşteri", "aynısından" gibi ifadeleri çözer
/// 2. Bağlam Kaydetme - Son kullanılan entity'leri hatırlar
/// 3. Akıllı Varsayılanlar - Kullanıcının geçmiş tercihlerine göre varsayılan değerler önerir
/// 4. Proaktif Öneriler - Kullanıcı alışkanlıklarına göre önerilerde bulunur
/// </summary>
public class ContextManager
{
    private readonly ISayPDbContext _context;
    private readonly ILogger<ContextManager> _logger;
    private readonly SelfLearningService _selfLearning;

    // Referans ifadeleri (Türkçe ve İngilizce)
    private static readonly Dictionary<string, string[]> ReferencePatterns = new()
    {
        { "customer", new[] { 
            // Türkçe
            @"\b(o|bu|aynı|son|önceki)\s*(müşteri|alıcı|firma|şirket)\b",
            @"\b(müşteri|alıcı|firma|şirket)(ye|ya|den|dan|nin|nın|e|a)\b",
            @"\bona\b", @"\bonun\b", @"\bondan\b",
            // İngilizce
            @"\b(that|this|same|last|previous)\s*(customer|client|buyer|company)\b",
            @"\bto\s*(him|her|them)\b"
        }},
        { "product", new[] { 
            // Türkçe
            @"\b(o|bu|aynı|son|önceki)\s*(ürün|mal|eşya|hizmet|servis)\b",
            @"\b(aynısından|bundan|ondan)\b",
            @"\b(ürün|mal|eşya)(den|dan|e|a|i|ı)\b",
            // İngilizce
            @"\b(that|this|same|last|previous)\s*(product|item|service)\b",
            @"\b(same\s*one|another\s*one)\b"
        }},
        { "appointment", new[] { 
            // Türkçe
            @"\b(o|bu|aynı|son|önceki)\s*(randevu|seans|görüşme)\b",
            // İngilizce
            @"\b(that|this|same|last|previous)\s*(appointment|meeting|session)\b"
        }},
        { "invoice", new[] { 
            // Türkçe
            @"\b(o|bu|aynı|son|önceki)\s*(fatura|hesap)\b",
            // İngilizce
            @"\b(that|this|same|last|previous)\s*(invoice|bill)\b"
        }}
    };

    public ContextManager(
        ISayPDbContext context,
        ILogger<ContextManager> logger,
        SelfLearningService selfLearning)
    {
        _context = context;
        _logger = logger;
        _selfLearning = selfLearning;
    }

    #region Reference Detection & Resolution

    /// <summary>
    /// Mesajda referans ifadeleri tespit et
    /// </summary>
    public List<DetectedReference> DetectReferences(string message)
    {
        var references = new List<DetectedReference>();
        var lowerMessage = message.ToLower();

        foreach (var (entityType, patterns) in ReferencePatterns)
        {
            foreach (var pattern in patterns)
            {
                try
                {
                    var matches = Regex.Matches(lowerMessage, pattern, RegexOptions.IgnoreCase);
                    foreach (Match match in matches)
                    {
                        references.Add(new DetectedReference
                        {
                            EntityType = entityType,
                            MatchedText = match.Value,
                            Position = match.Index,
                            Pattern = pattern
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error matching pattern {Pattern}", pattern);
                }
            }
        }

        // Remove duplicates (same entity type)
        return references
            .GroupBy(r => r.EntityType)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Referansları çöz ve gerçek entity'lere dönüştür
    /// </summary>
    public async Task<Dictionary<string, ResolvedReference>> ResolveReferencesAsync(
        Guid conversationId,
        List<DetectedReference> detectedReferences,
        CancellationToken cancellationToken = default)
    {
        var resolved = new Dictionary<string, ResolvedReference>();

        foreach (var reference in detectedReferences)
        {
            var contextRef = await _selfLearning.ResolveReferenceAsync(
                conversationId, 
                reference.EntityType, 
                cancellationToken);

            if (contextRef != null)
            {
                resolved[reference.EntityType] = new ResolvedReference
                {
                    EntityType = reference.EntityType,
                    EntityId = contextRef.EntityId,
                    EntityName = contextRef.EntityName,
                    EntityData = !string.IsNullOrEmpty(contextRef.EntityDataJson) 
                        ? JsonSerializer.Deserialize<Dictionary<string, object>>(contextRef.EntityDataJson)
                        : null,
                    OriginalText = reference.MatchedText
                };

                _logger.LogInformation("Resolved reference: '{Text}' -> {Type} '{Name}' (ID: {Id})",
                    reference.MatchedText, reference.EntityType, contextRef.EntityName, contextRef.EntityId);
            }
            else
            {
                _logger.LogDebug("Could not resolve reference: '{Text}' for type {Type}",
                    reference.MatchedText, reference.EntityType);
            }
        }

        return resolved;
    }

    /// <summary>
    /// Mesajdaki referansları çözülmüş değerlerle değiştir
    /// </summary>
    public string ReplaceReferencesInMessage(
        string message,
        Dictionary<string, ResolvedReference> resolvedReferences)
    {
        var result = message;

        foreach (var (entityType, resolved) in resolvedReferences)
        {
            if (!string.IsNullOrEmpty(resolved.EntityName))
            {
                // Replace the reference with the actual entity name
                foreach (var pattern in ReferencePatterns[entityType])
                {
                    try
                    {
                        result = Regex.Replace(result, pattern, resolved.EntityName, RegexOptions.IgnoreCase);
                    }
                    catch { }
                }
            }
        }

        return result;
    }

    #endregion

    #region Context Saving

    /// <summary>
    /// Başarılı bir işlemden sonra bağlamı kaydet
    /// </summary>
    public async Task SaveContextFromExecutionAsync(
        Guid conversationId,
        string intent,
        Dictionary<string, object> parameters,
        object? result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Intent'ten entity tipini çıkar
            var entityType = ExtractEntityTypeFromIntent(intent);
            if (string.IsNullOrEmpty(entityType)) return;

            // Result'tan entity bilgilerini çıkar
            var entityInfo = ExtractEntityInfoFromResult(result, entityType);
            if (entityInfo == null) return;

            // Bağlamı kaydet
            await _selfLearning.SaveContextReferenceAsync(
                conversationId,
                entityType,
                entityInfo.Value.Id,
                entityInfo.Value.Name,
                entityInfo.Value.Data,
                cancellationToken);

            _logger.LogInformation("Saved context: {Type} '{Name}' (ID: {Id})",
                entityType, entityInfo.Value.Name, entityInfo.Value.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving context from execution");
        }
    }

    private string? ExtractEntityTypeFromIntent(string intent)
    {
        var lowerIntent = intent.ToLower();
        
        if (lowerIntent.Contains("customer")) return "customer";
        if (lowerIntent.Contains("product")) return "product";
        if (lowerIntent.Contains("service")) return "product"; // Services are also products
        if (lowerIntent.Contains("appointment")) return "appointment";
        if (lowerIntent.Contains("invoice")) return "invoice";
        
        return null;
    }

    private (Guid Id, string Name, object? Data)? ExtractEntityInfoFromResult(object? result, string entityType)
    {
        if (result == null) return null;

        try
        {
            // Try to extract from JSON
            var json = result is string str ? str : JsonSerializer.Serialize(result);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Try common ID field names
            Guid? id = null;
            string? name = null;

            // ID extraction
            if (root.TryGetProperty("id", out var idProp) || 
                root.TryGetProperty("Id", out idProp) ||
                root.TryGetProperty("ID", out idProp))
            {
                if (idProp.ValueKind == JsonValueKind.String && Guid.TryParse(idProp.GetString(), out var parsedId))
                {
                    id = parsedId;
                }
            }

            // Name extraction based on entity type
            var nameFields = entityType switch
            {
                "customer" => new[] { "name", "Name", "customerName", "CustomerName", "companyName", "CompanyName" },
                "product" => new[] { "name", "Name", "productName", "ProductName", "serviceName", "ServiceName" },
                "appointment" => new[] { "customerName", "CustomerName", "serviceName", "ServiceName" },
                "invoice" => new[] { "invoiceNumber", "InvoiceNumber", "number", "Number" },
                _ => new[] { "name", "Name" }
            };

            foreach (var field in nameFields)
            {
                if (root.TryGetProperty(field, out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                {
                    name = nameProp.GetString();
                    if (!string.IsNullOrEmpty(name)) break;
                }
            }

            if (id.HasValue && !string.IsNullOrEmpty(name))
            {
                return (id.Value, name, result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error extracting entity info from result");
        }

        return null;
    }

    #endregion

    #region Smart Defaults

    /// <summary>
    /// Kullanıcının geçmiş tercihlerine göre akıllı varsayılanlar öner
    /// </summary>
    public async Task<Dictionary<string, object>> GetSmartDefaultsAsync(
        Guid userProfileId,
        string intent,
        CancellationToken cancellationToken = default)
    {
        var defaults = new Dictionary<string, object>();

        try
        {
            // Get user profile
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.Id == userProfileId, cancellationToken);

            if (profile == null) return defaults;

            // Parse preferred entities
            if (!string.IsNullOrEmpty(profile.PreferredEntitiesJson))
            {
                var preferredEntities = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(
                    profile.PreferredEntitiesJson);

                if (preferredEntities != null)
                {
                    // Get entity type from intent
                    var entityType = ExtractEntityTypeFromIntent(intent);
                    
                    if (entityType != null && preferredEntities.TryGetValue(entityType, out var entities))
                    {
                        // Get most used entity
                        var mostUsed = entities.OrderByDescending(e => e.Value).FirstOrDefault();
                        if (!string.IsNullOrEmpty(mostUsed.Key))
                        {
                            defaults[$"preferred_{entityType}"] = mostUsed.Key;
                        }
                    }
                }
            }

            // Parse usage habits for time-based defaults
            if (!string.IsNullOrEmpty(profile.UsageHabitsJson))
            {
                var habits = JsonSerializer.Deserialize<Dictionary<string, object>>(profile.UsageHabitsJson);
                
                if (habits != null)
                {
                    // Add relevant habits as defaults
                    foreach (var (key, value) in habits)
                    {
                        if (key.StartsWith("default_"))
                        {
                            defaults[key.Replace("default_", "")] = value;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting smart defaults");
        }

        return defaults;
    }

    /// <summary>
    /// Kullanıcının tercihlerini kaydet
    /// </summary>
    public async Task SaveUserPreferenceAsync(
        Guid userProfileId,
        string preferenceKey,
        object preferenceValue,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.Id == userProfileId, cancellationToken);

            if (profile == null) return;

            // Parse existing habits
            var habits = !string.IsNullOrEmpty(profile.UsageHabitsJson)
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(profile.UsageHabitsJson)
                : new Dictionary<string, object>();

            habits ??= new Dictionary<string, object>();
            habits[$"default_{preferenceKey}"] = preferenceValue;

            profile.UsageHabitsJson = JsonSerializer.Serialize(habits);
            profile.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving user preference");
        }
    }

    #endregion

    #region Proactive Suggestions

    /// <summary>
    /// Kullanıcı alışkanlıklarına göre proaktif öneriler oluştur
    /// </summary>
    public async Task<List<ProactiveSuggestion>> GetProactiveSuggestionsAsync(
        Guid userProfileId,
        CancellationToken cancellationToken = default)
    {
        var suggestions = new List<ProactiveSuggestion>();

        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.Id == userProfileId, cancellationToken);

            if (profile == null) return suggestions;

            // Parse frequent commands
            if (!string.IsNullOrEmpty(profile.FrequentCommandsJson))
            {
                var frequentCommands = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                    profile.FrequentCommandsJson);

                if (frequentCommands != null)
                {
                    // Get current time in Turkey
                    var turkeyTime = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
                    var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, turkeyTime);
                    var dayOfWeek = now.DayOfWeek;
                    var hour = now.Hour;

                    // Check for time-based patterns
                    foreach (var (command, data) in frequentCommands)
                    {
                        // If user frequently lists appointments on Monday mornings
                        if (command.Contains("list_appointment") && dayOfWeek == DayOfWeek.Monday && hour >= 8 && hour <= 10)
                        {
                            suggestions.Add(new ProactiveSuggestion
                            {
                                Type = "time_based",
                                MessageTurkish = "📅 Her Pazartesi sabahı randevuları kontrol ediyorsunuz. Bugünkü randevuları göstereyim mi?",
                                MessageEnglish = "📅 You usually check appointments on Monday mornings. Would you like to see today's appointments?",
                                SuggestedIntent = "list_appointments",
                                SuggestedParameters = new Dictionary<string, object> { { "date", now.ToString("yyyy-MM-dd") } },
                                Confidence = 0.8
                            });
                        }

                        // If user frequently creates invoices at end of month
                        if (command.Contains("create_invoice") && now.Day >= 25)
                        {
                            suggestions.Add(new ProactiveSuggestion
                            {
                                Type = "time_based",
                                MessageTurkish = "📄 Ay sonu yaklaşıyor. Fatura oluşturmak ister misiniz?",
                                MessageEnglish = "📄 End of month is approaching. Would you like to create an invoice?",
                                SuggestedIntent = "create_invoice",
                                Confidence = 0.6
                            });
                        }
                    }
                }
            }

            // Parse active hours for greeting suggestions
            if (!string.IsNullOrEmpty(profile.ActiveHoursJson))
            {
                var activeHours = JsonSerializer.Deserialize<Dictionary<int, int>>(profile.ActiveHoursJson);
                
                if (activeHours != null)
                {
                    var turkeyTime = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
                    var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, turkeyTime);
                    
                    // If this is their first message of the day
                    if (profile.LastActivityAt.Date < now.Date)
                    {
                        var greeting = now.Hour switch
                        {
                            < 12 => ("Günaydın", "Good morning"),
                            < 18 => ("İyi günler", "Good afternoon"),
                            _ => ("İyi akşamlar", "Good evening")
                        };

                        suggestions.Add(new ProactiveSuggestion
                        {
                            Type = "greeting",
                            MessageTurkish = $"👋 {greeting.Item1}! Size nasıl yardımcı olabilirim?",
                            MessageEnglish = $"👋 {greeting.Item2}! How can I help you?",
                            Confidence = 0.9
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error getting proactive suggestions");
        }

        return suggestions.OrderByDescending(s => s.Confidence).Take(3).ToList();
    }

    #endregion

    #region Cleanup

    /// <summary>
    /// Eski bağlam verilerini temizle
    /// </summary>
    public async Task CleanupOldContextAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        await _selfLearning.CleanupOldReferencesAsync(maxAge, cancellationToken);
    }

    #endregion
}

#region Models

/// <summary>
/// Tespit edilen referans
/// </summary>
public class DetectedReference
{
    public string EntityType { get; set; } = string.Empty;
    public string MatchedText { get; set; } = string.Empty;
    public int Position { get; set; }
    public string Pattern { get; set; } = string.Empty;
}

/// <summary>
/// Çözülmüş referans
/// </summary>
public class ResolvedReference
{
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? EntityName { get; set; }
    public Dictionary<string, object>? EntityData { get; set; }
    public string OriginalText { get; set; } = string.Empty;
}

/// <summary>
/// Proaktif öneri
/// </summary>
public class ProactiveSuggestion
{
    public string Type { get; set; } = string.Empty; // "time_based", "pattern_based", "greeting"
    public string MessageTurkish { get; set; } = string.Empty;
    public string MessageEnglish { get; set; } = string.Empty;
    public string? SuggestedIntent { get; set; }
    public Dictionary<string, object>? SuggestedParameters { get; set; }
    public double Confidence { get; set; }
}

#endregion
