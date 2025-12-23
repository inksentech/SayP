using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SayP.Domain.Entities;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SayP.Application.Services;

/// <summary>
/// Self-Learning Service - Sistemin kendini iyileştirmesi için merkezi servis
/// 
/// Özellikler:
/// 1. Alias Öğrenme - Başarılı komutlardan yeni alias'lar öğrenir
/// 2. Terminoloji Öğrenme - Şirket bazlı özel terimleri öğrenir
/// 3. Bağlam Yönetimi - "o müşteri", "aynısından" gibi referansları çözer
/// 4. Feedback İşleme - Kullanıcı geri bildirimlerinden öğrenir
/// 5. Pattern Mining - Başarılı komutlardan pattern çıkarır
/// 6. Global Learning - Tüm tenant'lardan anonim öğrenme (opt-in)
/// </summary>
public class SelfLearningService
{
    private readonly ISayPDbContext _context;
    private readonly ILogger<SelfLearningService> _logger;
    private readonly LanguageDetector _languageDetector;
    
    // Standart terimler (Türkçe ve İngilizce)
    private static readonly Dictionary<string, string[]> StandardTerms = new()
    {
        { "customer", new[] { "müşteri", "customer", "alıcı", "client" } },
        { "product", new[] { "ürün", "product", "mal", "eşya", "item" } },
        { "service", new[] { "hizmet", "service", "servis" } },
        { "appointment", new[] { "randevu", "appointment", "seans", "görüşme", "meeting" } },
        { "invoice", new[] { "fatura", "invoice", "hesap", "bill" } },
        { "template", new[] { "şablon", "template", "kalıp", "pattern" } },
        { "code", new[] { "kod", "code", "numara", "number" } },
        { "list", new[] { "liste", "list", "listele", "göster", "show" } },
        { "create", new[] { "oluştur", "create", "ekle", "add", "yeni", "new", "kaydet", "save" } },
        { "update", new[] { "güncelle", "update", "değiştir", "change", "düzenle", "edit" } },
        { "delete", new[] { "sil", "delete", "kaldır", "remove", "iptal", "cancel" } }
    };

    public SelfLearningService(
        ISayPDbContext context,
        ILogger<SelfLearningService> logger)
    {
        _context = context;
        _logger = logger;
        _languageDetector = new LanguageDetector();
    }

    #region 1. Alias Öğrenme

    /// <summary>
    /// Başarılı bir komuttan alias öğren
    /// </summary>
    public async Task LearnFromSuccessfulCommandAsync(
        string userMessage,
        string intent,
        Guid? tenantId,
        double confidence,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedMessage = NormalizeText(userMessage);
            var language = _languageDetector.DetectLanguage(userMessage);

            // Zaten var mı kontrol et
            var existingAlias = await _context.LearnedAliases
                .FirstOrDefaultAsync(a => 
                    a.NormalizedAlias == normalizedMessage && 
                    a.Intent == intent &&
                    (a.TenantId == tenantId || (a.TenantId == null && tenantId == null)),
                    cancellationToken);

            if (existingAlias != null)
            {
                // Mevcut alias'ı güncelle
                existingAlias.UsageCount++;
                existingAlias.LastUsedAt = DateTime.UtcNow;
                existingAlias.UpdatedAt = DateTime.UtcNow;
                
                // Başarı oranını güncelle (moving average)
                existingAlias.SuccessRate = (existingAlias.SuccessRate * (existingAlias.UsageCount - 1) + 1.0) / existingAlias.UsageCount;
                
                // Güven skorunu güncelle
                existingAlias.ConfidenceScore = CalculateConfidenceScore(existingAlias);
                
                _logger.LogDebug("Updated existing alias: {Alias} -> {Intent} (usage: {Count})", 
                    userMessage, intent, existingAlias.UsageCount);
            }
            else
            {
                // Yeni alias oluştur
                var newAlias = new LearnedAlias
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Intent = intent,
                    Alias = userMessage.Trim(),
                    NormalizedAlias = normalizedMessage,
                    Language = language,
                    UsageCount = 1,
                    SuccessRate = 1.0,
                    ConfidenceScore = Math.Min(0.5 + (confidence * 0.3), 0.8), // Başlangıç skoru
                    IsActive = true,
                    IsManual = false,
                    Source = "user_success",
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.LearnedAliases.Add(newAlias);
                _logger.LogInformation("Learned new alias: '{Alias}' -> {Intent} (lang: {Lang})", 
                    userMessage, intent, language);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error learning from successful command");
        }
    }

    /// <summary>
    /// Başarısız bir komuttan öğren (alias'ın başarı oranını düşür)
    /// </summary>
    public async Task LearnFromFailedCommandAsync(
        string userMessage,
        string intent,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedMessage = NormalizeText(userMessage);

            var existingAlias = await _context.LearnedAliases
                .FirstOrDefaultAsync(a => 
                    a.NormalizedAlias == normalizedMessage && 
                    a.Intent == intent &&
                    (a.TenantId == tenantId || (a.TenantId == null && tenantId == null)),
                    cancellationToken);

            if (existingAlias != null)
            {
                existingAlias.UsageCount++;
                existingAlias.UpdatedAt = DateTime.UtcNow;
                
                // Başarı oranını düşür
                existingAlias.SuccessRate = (existingAlias.SuccessRate * (existingAlias.UsageCount - 1) + 0.0) / existingAlias.UsageCount;
                existingAlias.ConfidenceScore = CalculateConfidenceScore(existingAlias);
                
                // Çok düşük başarı oranı varsa devre dışı bırak
                if (existingAlias.SuccessRate < 0.3 && existingAlias.UsageCount >= 5)
                {
                    existingAlias.IsActive = false;
                    _logger.LogWarning("Deactivated low-performing alias: {Alias} -> {Intent} (success rate: {Rate:P0})", 
                        existingAlias.Alias, intent, existingAlias.SuccessRate);
                }

                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error learning from failed command");
        }
    }

    /// <summary>
    /// Tenant ve dil için aktif alias'ları getir
    /// </summary>
    public async Task<List<LearnedAlias>> GetActiveAliasesAsync(
        Guid? tenantId,
        string? language = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.LearnedAliases
            .Where(a => a.IsActive)
            .Where(a => a.TenantId == null || a.TenantId == tenantId); // Global + tenant-specific

        if (!string.IsNullOrEmpty(language))
        {
            query = query.Where(a => a.Language == language || a.Language == "multi");
        }

        return await query
            .OrderByDescending(a => a.ConfidenceScore)
            .ThenByDescending(a => a.UsageCount)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Mesaj için en iyi alias eşleşmesini bul
    /// </summary>
    public async Task<(string? Intent, double Confidence)?> FindBestAliasMatchAsync(
        string message,
        Guid? tenantId,
        CancellationToken cancellationToken = default)
    {
        var normalizedMessage = NormalizeText(message);
        var language = _languageDetector.DetectLanguage(message);

        // Exact match
        var exactMatch = await _context.LearnedAliases
            .Where(a => a.IsActive)
            .Where(a => a.NormalizedAlias == normalizedMessage)
            .Where(a => a.TenantId == null || a.TenantId == tenantId)
            .OrderByDescending(a => a.TenantId.HasValue) // Tenant-specific öncelikli
            .ThenByDescending(a => a.ConfidenceScore)
            .FirstOrDefaultAsync(cancellationToken);

        if (exactMatch != null)
        {
            return (exactMatch.Intent, exactMatch.ConfidenceScore);
        }

        return null;
    }

    #endregion

    #region 2. Terminoloji Öğrenme

    /// <summary>
    /// Şirket bazlı terminoloji öğren
    /// </summary>
    public async Task LearnTerminologyAsync(
        Guid tenantId,
        string standardTerm,
        string customTerm,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedCustom = NormalizeText(customTerm);

            var existing = await _context.TenantTerminologies
                .FirstOrDefaultAsync(t => 
                    t.TenantId == tenantId && 
                    t.StandardTerm == standardTerm &&
                    t.NormalizedCustomTerm == normalizedCustom,
                    cancellationToken);

            if (existing != null)
            {
                existing.UsageCount++;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var terminology = new TenantTerminology
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    StandardTerm = standardTerm,
                    CustomTerm = customTerm.Trim(),
                    NormalizedCustomTerm = normalizedCustom,
                    UsageCount = 1,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.TenantTerminologies.Add(terminology);
                _logger.LogInformation("Learned terminology for tenant {TenantId}: {Custom} -> {Standard}", 
                    tenantId, customTerm, standardTerm);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error learning terminology");
        }
    }

    /// <summary>
    /// Mesajdaki özel terimleri standart terimlere çevir
    /// </summary>
    public async Task<string> NormalizeTerminologyAsync(
        string message,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var terminologies = await _context.TenantTerminologies
            .Where(t => t.TenantId == tenantId && t.IsActive)
            .OrderByDescending(t => t.CustomTerm.Length) // Uzun terimler önce
            .ToListAsync(cancellationToken);

        var normalizedMessage = message;
        foreach (var term in terminologies)
        {
            // Case-insensitive replace
            normalizedMessage = Regex.Replace(
                normalizedMessage,
                Regex.Escape(term.CustomTerm),
                GetStandardTermReplacement(term.StandardTerm),
                RegexOptions.IgnoreCase);
        }

        return normalizedMessage;
    }

    private string GetStandardTermReplacement(string standardTerm)
    {
        // Standart terimi Türkçe karşılığına çevir
        return standardTerm switch
        {
            "customer" => "müşteri",
            "product" => "ürün",
            "service" => "hizmet",
            "appointment" => "randevu",
            "invoice" => "fatura",
            "template" => "şablon",
            _ => standardTerm
        };
    }

    /// <summary>
    /// Mesajdan potansiyel terminoloji öğren
    /// </summary>
    public async Task DetectAndLearnTerminologyAsync(
        string message,
        string intent,
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        // Intent'ten entity tipini çıkar
        var entityType = ExtractEntityTypeFromIntent(intent);
        if (string.IsNullOrEmpty(entityType)) return;

        // Mesajda standart olmayan terimler ara
        var words = message.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        foreach (var word in words)
        {
            // Standart terimlerden biri mi kontrol et
            if (StandardTerms.TryGetValue(entityType, out var standardTerms))
            {
                // Eğer kelime standart terimlerden biri değilse ve entity ile ilgiliyse
                if (!standardTerms.Contains(word) && IsLikelyEntityTerm(word, entityType))
                {
                    await LearnTerminologyAsync(tenantId, entityType, word, cancellationToken);
                }
            }
        }
    }

    private string? ExtractEntityTypeFromIntent(string intent)
    {
        var lowerIntent = intent.ToLower();
        if (lowerIntent.Contains("customer")) return "customer";
        if (lowerIntent.Contains("product")) return "product";
        if (lowerIntent.Contains("service")) return "service";
        if (lowerIntent.Contains("appointment")) return "appointment";
        if (lowerIntent.Contains("invoice")) return "invoice";
        if (lowerIntent.Contains("template")) return "template";
        return null;
    }

    private bool IsLikelyEntityTerm(string word, string entityType)
    {
        // Basit heuristik: 3+ karakter ve sayı içermiyor
        return word.Length >= 3 && !word.Any(char.IsDigit);
    }

    #endregion

    #region 3. Bağlam Yönetimi

    /// <summary>
    /// Bağlam referansı kaydet
    /// </summary>
    public async Task SaveContextReferenceAsync(
        Guid conversationId,
        string referenceType,
        Guid entityId,
        string? entityName,
        object? entityData = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Mevcut en yüksek sırayı bul
            var maxOrder = await _context.ContextReferences
                .Where(r => r.ConversationId == conversationId && r.ReferenceType == referenceType)
                .MaxAsync(r => (int?)r.Order, cancellationToken) ?? 0;

            var reference = new ContextReference
            {
                Id = Guid.NewGuid(),
                ConversationId = conversationId,
                ReferenceType = referenceType,
                EntityId = entityId,
                EntityName = entityName,
                EntityDataJson = entityData != null ? JsonSerializer.Serialize(entityData) : null,
                Order = maxOrder + 1,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };

            _context.ContextReferences.Add(reference);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Saved context reference: {Type} -> {Name} (ID: {Id})", 
                referenceType, entityName, entityId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving context reference");
        }
    }

    /// <summary>
    /// "O müşteri", "aynısından" gibi referansları çöz
    /// </summary>
    public async Task<ContextReference?> ResolveReferenceAsync(
        Guid conversationId,
        string referenceType,
        CancellationToken cancellationToken = default)
    {
        var reference = await _context.ContextReferences
            .Where(r => r.ConversationId == conversationId && r.ReferenceType == referenceType)
            .OrderByDescending(r => r.Order)
            .FirstOrDefaultAsync(cancellationToken);

        if (reference != null)
        {
            reference.LastAccessedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }

        return reference;
    }

    /// <summary>
    /// Mesajda referans ifadeleri tespit et
    /// </summary>
    public (bool HasReference, string? ReferenceType) DetectReferenceInMessage(string message)
    {
        var lowerMessage = message.ToLower();

        // Türkçe referans ifadeleri
        var customerRefs = new[] { "o müşteri", "bu müşteri", "aynı müşteri", "müşteriye", "ona" };
        var productRefs = new[] { "o ürün", "bu ürün", "aynı ürün", "aynısından", "ondan" };
        var appointmentRefs = new[] { "o randevu", "bu randevu", "aynı randevu" };
        var invoiceRefs = new[] { "o fatura", "bu fatura", "aynı fatura" };

        // İngilizce referans ifadeleri
        var customerRefsEn = new[] { "that customer", "this customer", "same customer", "to them", "to him", "to her" };
        var productRefsEn = new[] { "that product", "this product", "same product", "same one", "another one" };
        var appointmentRefsEn = new[] { "that appointment", "this appointment", "same appointment" };
        var invoiceRefsEn = new[] { "that invoice", "this invoice", "same invoice" };

        if (customerRefs.Any(r => lowerMessage.Contains(r)) || customerRefsEn.Any(r => lowerMessage.Contains(r)))
            return (true, "customer");
        if (productRefs.Any(r => lowerMessage.Contains(r)) || productRefsEn.Any(r => lowerMessage.Contains(r)))
            return (true, "product");
        if (appointmentRefs.Any(r => lowerMessage.Contains(r)) || appointmentRefsEn.Any(r => lowerMessage.Contains(r)))
            return (true, "appointment");
        if (invoiceRefs.Any(r => lowerMessage.Contains(r)) || invoiceRefsEn.Any(r => lowerMessage.Contains(r)))
            return (true, "invoice");

        return (false, null);
    }

    /// <summary>
    /// Eski bağlam referanslarını temizle
    /// </summary>
    public async Task CleanupOldReferencesAsync(
        TimeSpan maxAge,
        CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow - maxAge;
        
        var oldReferences = await _context.ContextReferences
            .Where(r => r.LastAccessedAt < cutoffDate)
            .ToListAsync(cancellationToken);

        if (oldReferences.Any())
        {
            _context.ContextReferences.RemoveRange(oldReferences);
            await _context.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Cleaned up {Count} old context references", oldReferences.Count);
        }
    }

    #endregion

    #region 4. Feedback İşleme

    /// <summary>
    /// Kullanıcı feedback'i kaydet
    /// </summary>
    public async Task SaveFeedbackAsync(
        Guid userProfileId,
        string feedbackType,
        string? originalMessage,
        string? detectedIntent,
        string? correctIntent = null,
        string? userComment = null,
        int? rating = null,
        Guid? conversationId = null,
        Guid? commandId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var feedback = new UserFeedback
            {
                Id = Guid.NewGuid(),
                UserProfileId = userProfileId,
                ConversationId = conversationId,
                CommandId = commandId,
                FeedbackType = feedbackType,
                OriginalMessage = originalMessage,
                DetectedIntent = detectedIntent,
                CorrectIntent = correctIntent,
                UserComment = userComment,
                Rating = rating,
                IsProcessed = false,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserFeedbacks.Add(feedback);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Saved feedback: {Type} for intent {Intent}", feedbackType, detectedIntent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving feedback");
        }
    }

    /// <summary>
    /// İşlenmemiş feedback'leri işle ve öğren
    /// </summary>
    public async Task ProcessPendingFeedbackAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pendingFeedbacks = await _context.UserFeedbacks
                .Where(f => !f.IsProcessed)
                .OrderBy(f => f.CreatedAt)
                .Take(100)
                .ToListAsync(cancellationToken);

            foreach (var feedback in pendingFeedbacks)
            {
                await ProcessSingleFeedbackAsync(feedback, cancellationToken);
                feedback.IsProcessed = true;
            }

            await _context.SaveChangesAsync(cancellationToken);
            
            if (pendingFeedbacks.Any())
            {
                _logger.LogInformation("Processed {Count} pending feedbacks", pendingFeedbacks.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pending feedback");
        }
    }

    private async Task ProcessSingleFeedbackAsync(UserFeedback feedback, CancellationToken cancellationToken)
    {
        switch (feedback.FeedbackType)
        {
            case "correction":
                // Kullanıcı yanlış intent'i düzeltti
                if (!string.IsNullOrEmpty(feedback.OriginalMessage) && 
                    !string.IsNullOrEmpty(feedback.CorrectIntent))
                {
                    // Yanlış alias'ın başarı oranını düşür
                    if (!string.IsNullOrEmpty(feedback.DetectedIntent))
                    {
                        await LearnFromFailedCommandAsync(
                            feedback.OriginalMessage, 
                            feedback.DetectedIntent, 
                            null, // Global
                            cancellationToken);
                    }

                    // Doğru alias'ı öğren
                    await LearnFromSuccessfulCommandAsync(
                        feedback.OriginalMessage,
                        feedback.CorrectIntent,
                        null, // Global
                        0.9, // Yüksek güven - kullanıcı düzeltmesi
                        cancellationToken);
                }
                break;

            case "cancellation":
                // Kullanıcı işlemi iptal etti - muhtemelen yanlış intent
                if (!string.IsNullOrEmpty(feedback.OriginalMessage) && 
                    !string.IsNullOrEmpty(feedback.DetectedIntent))
                {
                    await LearnFromFailedCommandAsync(
                        feedback.OriginalMessage,
                        feedback.DetectedIntent,
                        null,
                        cancellationToken);
                }
                break;

            case "confirmation":
                // Kullanıcı onayladı - doğru intent
                if (!string.IsNullOrEmpty(feedback.OriginalMessage) && 
                    !string.IsNullOrEmpty(feedback.DetectedIntent))
                {
                    await LearnFromSuccessfulCommandAsync(
                        feedback.OriginalMessage,
                        feedback.DetectedIntent,
                        null,
                        0.95, // Çok yüksek güven - kullanıcı onayı
                        cancellationToken);
                }
                break;
        }
    }

    #endregion

    #region 5. Pattern Mining

    /// <summary>
    /// Başarılı komutlardan pattern çıkar
    /// </summary>
    public async Task MinePatternFromSuccessfulCommandsAsync(
        int minOccurrences = 3,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Son 30 günün başarılı komutlarını al
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            
            var successfulCommands = await _context.Commands
                .Where(c => c.CreatedAt >= thirtyDaysAgo)
                .Where(c => c.Status == Domain.Enums.CommandStatus.Completed)
                .Select(c => new { c.Type, c.OriginalMessage })
                .ToListAsync(cancellationToken);

            // Mesajları intent'e göre grupla
            var groupedByIntent = successfulCommands
                .Where(c => !string.IsNullOrEmpty(c.OriginalMessage))
                .GroupBy(c => c.Type.ToString())
                .Where(g => g.Count() >= minOccurrences);

            foreach (var group in groupedByIntent)
            {
                var intent = group.Key;
                var messages = group.Select(g => g.OriginalMessage!).ToList();

                // Ortak pattern'leri bul
                var patterns = ExtractCommonPatterns(messages);

                foreach (var pattern in patterns)
                {
                    // Pattern'i alias olarak kaydet
                    await LearnFromSuccessfulCommandAsync(
                        pattern,
                        intent,
                        null, // Global
                        0.7, // Orta güven - pattern mining
                        cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error mining patterns");
        }
    }

    private List<string> ExtractCommonPatterns(List<string> messages)
    {
        var patterns = new List<string>();
        
        // Basit pattern extraction: Ortak kelime kombinasyonları
        var wordFrequency = new Dictionary<string, int>();
        
        foreach (var message in messages)
        {
            var words = NormalizeText(message).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            // 2-gram ve 3-gram'ları say
            for (int i = 0; i < words.Length - 1; i++)
            {
                var bigram = $"{words[i]} {words[i + 1]}";
                wordFrequency[bigram] = wordFrequency.GetValueOrDefault(bigram, 0) + 1;

                if (i < words.Length - 2)
                {
                    var trigram = $"{words[i]} {words[i + 1]} {words[i + 2]}";
                    wordFrequency[trigram] = wordFrequency.GetValueOrDefault(trigram, 0) + 1;
                }
            }
        }

        // En sık kullanılan pattern'leri al
        var minCount = Math.Max(2, messages.Count / 3);
        patterns = wordFrequency
            .Where(kv => kv.Value >= minCount)
            .OrderByDescending(kv => kv.Value)
            .Take(5)
            .Select(kv => kv.Key)
            .ToList();

        return patterns;
    }

    #endregion

    #region 6. Global Learning

    /// <summary>
    /// Global öğrenme havuzuna katkıda bulun (anonim)
    /// </summary>
    public async Task ContributeToGlobalLearningAsync(
        string pattern,
        string intent,
        string language,
        double successRate,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var normalizedPattern = NormalizeText(pattern);

            var existing = await _context.GlobalLearningPool
                .FirstOrDefaultAsync(g => 
                    g.NormalizedPattern == normalizedPattern && 
                    g.Intent == intent,
                    cancellationToken);

            if (existing != null)
            {
                existing.TenantCount++; // Farklı tenant'tan geldiğini varsay
                existing.TotalUsageCount++;
                existing.AverageSuccessRate = (existing.AverageSuccessRate * (existing.TotalUsageCount - 1) + successRate) / existing.TotalUsageCount;
                existing.GlobalConfidenceScore = CalculateGlobalConfidenceScore(existing);
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var entry = new GlobalLearningEntry
                {
                    Id = Guid.NewGuid(),
                    Pattern = pattern.Trim(),
                    NormalizedPattern = normalizedPattern,
                    Intent = intent,
                    Language = language,
                    TenantCount = 1,
                    TotalUsageCount = 1,
                    AverageSuccessRate = successRate,
                    GlobalConfidenceScore = 0.3, // Düşük başlangıç skoru
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _context.GlobalLearningPool.Add(entry);
            }

            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error contributing to global learning");
        }
    }

    /// <summary>
    /// Global öğrenme havuzundan alias'ları al
    /// </summary>
    public async Task<List<GlobalLearningEntry>> GetGlobalPatternsAsync(
        string? language = null,
        double minConfidence = 0.5,
        CancellationToken cancellationToken = default)
    {
        var query = _context.GlobalLearningPool
            .Where(g => g.IsActive)
            .Where(g => g.GlobalConfidenceScore >= minConfidence)
            .Where(g => g.TenantCount >= 2); // En az 2 farklı tenant'ta kullanılmış

        if (!string.IsNullOrEmpty(language))
        {
            query = query.Where(g => g.Language == language);
        }

        return await query
            .OrderByDescending(g => g.GlobalConfidenceScore)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    private double CalculateGlobalConfidenceScore(GlobalLearningEntry entry)
    {
        // Faktörler:
        // 1. Tenant sayısı (daha fazla = daha güvenilir)
        // 2. Toplam kullanım (daha fazla = daha güvenilir)
        // 3. Başarı oranı
        
        var tenantFactor = Math.Min(entry.TenantCount / 10.0, 1.0); // Max 10 tenant
        var usageFactor = Math.Min(entry.TotalUsageCount / 50.0, 1.0); // Max 50 kullanım
        var successFactor = entry.AverageSuccessRate;

        return (tenantFactor * 0.3 + usageFactor * 0.3 + successFactor * 0.4);
    }

    #endregion

    #region Helper Methods

    private string NormalizeText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        // Küçük harf, trim, fazla boşlukları kaldır
        var normalized = text.ToLowerInvariant().Trim();
        normalized = Regex.Replace(normalized, @"\s+", " ");

        // Türkçe karakterleri normalize et (opsiyonel - fuzzy matching için)
        // normalized = NormalizeTurkishChars(normalized);

        return normalized;
    }

    private double CalculateConfidenceScore(LearnedAlias alias)
    {
        // Faktörler:
        // 1. Kullanım sayısı (daha fazla = daha güvenilir)
        // 2. Başarı oranı
        // 3. Yaş (eski alias'lar daha güvenilir)
        
        var usageFactor = Math.Min(alias.UsageCount / 20.0, 1.0); // Max 20 kullanım
        var successFactor = alias.SuccessRate;
        var ageDays = (DateTime.UtcNow - alias.CreatedAt).TotalDays;
        var ageFactor = Math.Min(ageDays / 30.0, 1.0); // Max 30 gün

        return (usageFactor * 0.3 + successFactor * 0.5 + ageFactor * 0.2);
    }

    #endregion
}

/// <summary>
/// Basit dil algılama servisi
/// </summary>
public class LanguageDetector
{
    private static readonly HashSet<string> TurkishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "merhaba", "selam", "evet", "hayır", "tamam", "lütfen", "teşekkür", "teşekkürler",
        "müşteri", "ürün", "hizmet", "randevu", "fatura", "şablon", "kod",
        "oluştur", "ekle", "sil", "güncelle", "listele", "göster", "bul",
        "bugün", "yarın", "dün", "şimdi", "sonra", "önce",
        "ve", "veya", "ile", "için", "den", "dan", "de", "da", "ne", "nasıl", "neden", "kim", "nerede"
    };

    private static readonly HashSet<string> EnglishWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "hello", "hi", "yes", "no", "okay", "please", "thank", "thanks",
        "customer", "product", "service", "appointment", "invoice", "template", "code",
        "create", "add", "delete", "update", "list", "show", "find",
        "today", "tomorrow", "yesterday", "now", "later", "before",
        "and", "or", "with", "for", "from", "to", "the", "a", "an", "what", "how", "why", "who", "where"
    };

    public string DetectLanguage(string text)
    {
        if (string.IsNullOrEmpty(text)) return "tr"; // Default Türkçe

        var words = text.ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        int turkishCount = 0;
        int englishCount = 0;

        foreach (var word in words)
        {
            if (TurkishWords.Contains(word)) turkishCount++;
            if (EnglishWords.Contains(word)) englishCount++;
        }

        // Türkçe karakterler varsa Türkçe
        if (text.Any(c => "çğıöşüÇĞİÖŞÜ".Contains(c)))
        {
            turkishCount += 3;
        }

        return turkishCount >= englishCount ? "tr" : "en";
    }
}
