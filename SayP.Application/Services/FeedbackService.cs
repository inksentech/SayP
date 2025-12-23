using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SayP.Domain.Entities;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;
using System.Text.Json;

namespace SayP.Application.Services;

/// <summary>
/// Feedback Service - Kullanıcı geri bildirimlerini yönetir ve öğrenme sistemine aktarır
/// 
/// Özellikler:
/// 1. Hata Düzeltme - Yanlış intent algılandığında düzeltme seçenekleri sunar
/// 2. Feedback Loop - Kullanıcı iptal ettiğinde neyin yanlış gittiğini öğrenir
/// 3. Rating System - Kullanıcı memnuniyetini ölçer
/// 4. Correction Learning - Düzeltmelerden öğrenir
/// </summary>
public class FeedbackService
{
    private readonly ISayPDbContext _context;
    private readonly ILogger<FeedbackService> _logger;
    private readonly SelfLearningService _selfLearning;

    public FeedbackService(
        ISayPDbContext context,
        ILogger<FeedbackService> logger,
        SelfLearningService selfLearning)
    {
        _context = context;
        _logger = logger;
        _selfLearning = selfLearning;
    }

    #region Error Correction

    /// <summary>
    /// Yanlış intent algılandığında düzeltme seçenekleri oluştur
    /// </summary>
    public async Task<CorrectionOptions> GenerateCorrectionOptionsAsync(
        string originalMessage,
        string detectedIntent,
        List<DiscoveredEndpoint> availableEndpoints,
        string? language = "tr",
        CancellationToken cancellationToken = default)
    {
        var isTurkish = language == "tr";
        var options = new CorrectionOptions
        {
            OriginalMessage = originalMessage,
            DetectedIntent = detectedIntent,
            Language = language ?? "tr"
        };

        // Get similar intents based on the message
        var similarIntents = GetSimilarIntents(originalMessage, detectedIntent, availableEndpoints);

        // Build correction message
        var messageBuilder = new System.Text.StringBuilder();
        
        if (isTurkish)
        {
            messageBuilder.AppendLine("🤔 Yanlış anlamış olabilirim. Aşağıdakilerden birini mi demek istediniz?");
            messageBuilder.AppendLine();
        }
        else
        {
            messageBuilder.AppendLine("🤔 I might have misunderstood. Did you mean one of these?");
            messageBuilder.AppendLine();
        }

        int index = 1;
        foreach (var intent in similarIntents.Take(3))
        {
            var description = isTurkish 
                ? GetTurkishDescription(intent.Intent) 
                : intent.Description;
            
            messageBuilder.AppendLine($"{index}. {description}");
            options.Alternatives.Add(new CorrectionAlternative
            {
                Index = index,
                Intent = intent.Intent,
                Description = description,
                Endpoint = intent
            });
            index++;
        }

        if (isTurkish)
        {
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("📝 Numara yazarak seçebilir veya 'iptal' diyebilirsiniz.");
        }
        else
        {
            messageBuilder.AppendLine();
            messageBuilder.AppendLine("📝 Type a number to select or say 'cancel'.");
        }

        options.CorrectionMessage = messageBuilder.ToString();
        options.HasAlternatives = options.Alternatives.Any();

        return options;
    }

    /// <summary>
    /// Kullanıcının düzeltme seçimini işle
    /// </summary>
    public async Task<CorrectionResult> ProcessCorrectionAsync(
        Guid userProfileId,
        string originalMessage,
        string detectedIntent,
        string userResponse,
        List<CorrectionAlternative> alternatives,
        CancellationToken cancellationToken = default)
    {
        var result = new CorrectionResult();
        var lowerResponse = userResponse.ToLower().Trim();

        // Check for cancellation
        if (lowerResponse == "iptal" || lowerResponse == "cancel" || lowerResponse == "hayır" || lowerResponse == "no")
        {
            result.IsCancelled = true;
            result.MessageTurkish = "❌ İşlem iptal edildi.";
            result.MessageEnglish = "❌ Operation cancelled.";

            // Record cancellation feedback
            await _selfLearning.SaveFeedbackAsync(
                userProfileId,
                "cancellation",
                originalMessage,
                detectedIntent,
                cancellationToken: cancellationToken);

            return result;
        }

        // Try to parse selection number
        if (int.TryParse(lowerResponse, out var selection) && selection >= 1 && selection <= alternatives.Count)
        {
            var selectedAlternative = alternatives[selection - 1];
            result.IsSuccess = true;
            result.SelectedIntent = selectedAlternative.Intent;
            result.SelectedEndpoint = selectedAlternative.Endpoint;
            result.MessageTurkish = $"✅ Anladım! '{selectedAlternative.Description}' işlemi için devam ediyorum.";
            result.MessageEnglish = $"✅ Got it! Proceeding with '{selectedAlternative.Description}'.";

            // Record correction feedback for learning
            await _selfLearning.SaveFeedbackAsync(
                userProfileId,
                "correction",
                originalMessage,
                detectedIntent,
                selectedAlternative.Intent,
                cancellationToken: cancellationToken);

            return result;
        }

        // Invalid response
        result.IsInvalid = true;
        result.MessageTurkish = "❓ Geçersiz seçim. Lütfen 1-" + alternatives.Count + " arası bir numara girin veya 'iptal' yazın.";
        result.MessageEnglish = "❓ Invalid selection. Please enter a number between 1-" + alternatives.Count + " or type 'cancel'.";

        return result;
    }

    private List<DiscoveredEndpoint> GetSimilarIntents(
        string message,
        string excludeIntent,
        List<DiscoveredEndpoint> endpoints)
    {
        var lowerMessage = message.ToLower();
        var keywords = lowerMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return endpoints
            .Where(e => e.Intent != excludeIntent)
            .Select(e => new
            {
                Endpoint = e,
                Score = CalculateSimilarityScore(keywords, e)
            })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Endpoint.Priority)
            .Take(5)
            .Select(x => x.Endpoint)
            .ToList();
    }

    private double CalculateSimilarityScore(string[] keywords, DiscoveredEndpoint endpoint)
    {
        double score = 0;

        var intentWords = endpoint.Intent.Replace("_", " ").ToLower().Split(' ');
        var descWords = endpoint.Description?.ToLower().Split(' ') ?? Array.Empty<string>();
        var aliasWords = endpoint.Aliases.SelectMany(a => a.ToLower().Split(' ')).ToArray();

        foreach (var keyword in keywords)
        {
            if (intentWords.Any(w => w.Contains(keyword) || keyword.Contains(w)))
                score += 2;
            if (descWords.Any(w => w.Contains(keyword) || keyword.Contains(w)))
                score += 1;
            if (aliasWords.Any(w => w.Contains(keyword) || keyword.Contains(w)))
                score += 1.5;
        }

        return score;
    }

    private string GetTurkishDescription(string intent)
    {
        return intent.ToLower() switch
        {
            "create_customer" => "Yeni müşteri oluştur",
            "list_customers" => "Müşterileri listele",
            "update_customer" => "Müşteri güncelle",
            "delete_customer" => "Müşteri sil",
            "create_product" => "Yeni ürün/hizmet oluştur",
            "list_products" => "Ürünleri listele",
            "update_product" => "Ürün güncelle",
            "delete_product" => "Ürün sil",
            "create_appointment" => "Yeni randevu oluştur",
            "list_appointments" => "Randevuları listele",
            "update_appointment" => "Randevu güncelle",
            "cancel_appointment" => "Randevu iptal et",
            "create_invoice" => "Yeni fatura oluştur",
            "list_invoices" => "Faturaları listele",
            "create_code_template" => "Kod şablonu oluştur",
            "list_code_templates" => "Kod şablonlarını listele",
            _ => intent.Replace("_", " ")
        };
    }

    #endregion

    #region Feedback Loop

    /// <summary>
    /// Kullanıcı iptal ettiğinde feedback kaydet
    /// </summary>
    public async Task RecordCancellationAsync(
        Guid userProfileId,
        Guid? conversationId,
        Guid? commandId,
        string originalMessage,
        string detectedIntent,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        await _selfLearning.SaveFeedbackAsync(
            userProfileId,
            "cancellation",
            originalMessage,
            detectedIntent,
            userComment: reason,
            conversationId: conversationId,
            commandId: commandId,
            cancellationToken: cancellationToken);

        // Also learn from the failure
        await _selfLearning.LearnFromFailedCommandAsync(
            originalMessage,
            detectedIntent,
            null, // Global learning
            cancellationToken);

        _logger.LogInformation("Recorded cancellation feedback for intent {Intent}", detectedIntent);
    }

    /// <summary>
    /// Kullanıcı onayladığında feedback kaydet
    /// </summary>
    public async Task RecordConfirmationAsync(
        Guid userProfileId,
        Guid? conversationId,
        Guid? commandId,
        string originalMessage,
        string detectedIntent,
        CancellationToken cancellationToken = default)
    {
        await _selfLearning.SaveFeedbackAsync(
            userProfileId,
            "confirmation",
            originalMessage,
            detectedIntent,
            conversationId: conversationId,
            commandId: commandId,
            cancellationToken: cancellationToken);

        // Learn from success
        await _selfLearning.LearnFromSuccessfulCommandAsync(
            originalMessage,
            detectedIntent,
            null, // Global learning
            0.95, // High confidence from user confirmation
            cancellationToken);

        _logger.LogInformation("Recorded confirmation feedback for intent {Intent}", detectedIntent);
    }

    /// <summary>
    /// Kullanıcı rating verdiğinde kaydet
    /// </summary>
    public async Task RecordRatingAsync(
        Guid userProfileId,
        Guid? conversationId,
        int rating,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        await _selfLearning.SaveFeedbackAsync(
            userProfileId,
            "rating",
            null,
            null,
            userComment: comment,
            rating: rating,
            conversationId: conversationId,
            cancellationToken: cancellationToken);

        // Update user satisfaction score
        await UpdateSatisfactionScoreAsync(userProfileId, rating, cancellationToken);

        _logger.LogInformation("Recorded rating {Rating} for user {UserId}", rating, userProfileId);
    }

    private async Task UpdateSatisfactionScoreAsync(
        Guid userProfileId,
        int rating,
        CancellationToken cancellationToken)
    {
        try
        {
            var profile = await _context.UserProfiles
                .FirstOrDefaultAsync(p => p.Id == userProfileId, cancellationToken);

            if (profile != null)
            {
                // Moving average of satisfaction score
                var weight = 0.3; // New rating weight
                profile.SatisfactionScore = (int)((1 - weight) * profile.SatisfactionScore + weight * (rating * 20)); // Convert 1-5 to 0-100
                profile.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating satisfaction score");
        }
    }

    #endregion

    #region Feedback Processing

    /// <summary>
    /// Bekleyen feedback'leri işle (background job olarak çalıştırılabilir)
    /// </summary>
    public async Task ProcessPendingFeedbacksAsync(CancellationToken cancellationToken = default)
    {
        await _selfLearning.ProcessPendingFeedbackAsync(cancellationToken);
    }

    /// <summary>
    /// Feedback istatistiklerini getir
    /// </summary>
    public async Task<FeedbackStats> GetFeedbackStatsAsync(
        Guid? tenantId = null,
        int days = 30,
        CancellationToken cancellationToken = default)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-days);

        var feedbacks = await _context.UserFeedbacks
            .Where(f => f.CreatedAt >= cutoffDate)
            .ToListAsync(cancellationToken);

        return new FeedbackStats
        {
            TotalFeedbacks = feedbacks.Count,
            Confirmations = feedbacks.Count(f => f.FeedbackType == "confirmation"),
            Cancellations = feedbacks.Count(f => f.FeedbackType == "cancellation"),
            Corrections = feedbacks.Count(f => f.FeedbackType == "correction"),
            Ratings = feedbacks.Count(f => f.FeedbackType == "rating"),
            AverageRating = feedbacks.Where(f => f.Rating.HasValue).Select(f => f.Rating!.Value).DefaultIfEmpty(0).Average(),
            ConfirmationRate = feedbacks.Count > 0 
                ? (double)feedbacks.Count(f => f.FeedbackType == "confirmation") / feedbacks.Count 
                : 0,
            TopMisunderstoodIntents = feedbacks
                .Where(f => f.FeedbackType == "cancellation" || f.FeedbackType == "correction")
                .Where(f => !string.IsNullOrEmpty(f.DetectedIntent))
                .GroupBy(f => f.DetectedIntent!)
                .OrderByDescending(g => g.Count())
                .Take(5)
                .ToDictionary(g => g.Key, g => g.Count())
        };
    }

    #endregion
}

#region Models

/// <summary>
/// Düzeltme seçenekleri
/// </summary>
public class CorrectionOptions
{
    public string OriginalMessage { get; set; } = string.Empty;
    public string DetectedIntent { get; set; } = string.Empty;
    public string Language { get; set; } = "tr";
    public string CorrectionMessage { get; set; } = string.Empty;
    public bool HasAlternatives { get; set; }
    public List<CorrectionAlternative> Alternatives { get; set; } = new();
}

/// <summary>
/// Düzeltme alternatifi
/// </summary>
public class CorrectionAlternative
{
    public int Index { get; set; }
    public string Intent { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public DiscoveredEndpoint? Endpoint { get; set; }
}

/// <summary>
/// Düzeltme sonucu
/// </summary>
public class CorrectionResult
{
    public bool IsSuccess { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsInvalid { get; set; }
    public string? SelectedIntent { get; set; }
    public DiscoveredEndpoint? SelectedEndpoint { get; set; }
    public string MessageTurkish { get; set; } = string.Empty;
    public string MessageEnglish { get; set; } = string.Empty;
}

/// <summary>
/// Feedback istatistikleri
/// </summary>
public class FeedbackStats
{
    public int TotalFeedbacks { get; set; }
    public int Confirmations { get; set; }
    public int Cancellations { get; set; }
    public int Corrections { get; set; }
    public int Ratings { get; set; }
    public double AverageRating { get; set; }
    public double ConfirmationRate { get; set; }
    public Dictionary<string, int> TopMisunderstoodIntents { get; set; } = new();
}

#endregion
