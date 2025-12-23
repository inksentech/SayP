using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SayP.Domain.Entities;
using SayP.Domain.Enums;
using SayP.Domain.Interfaces;
using System.Text.Json;

namespace SayP.Application.Services;

/// <summary>
/// Kullanıcı profili ve davranış öğrenme servisi
/// </summary>
public class UserProfileService
{
    private readonly ISayPDbContext _context;
    private readonly ILogger<UserProfileService> _logger;

    public UserProfileService(
        ISayPDbContext context,
        ILogger<UserProfileService> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Kullanıcı profilini getir veya oluştur
    /// </summary>
    public async Task<UserProfile> GetOrCreateProfileAsync(
        string phoneNumber,
        Guid tenantId,
        string? userName = null,
        CancellationToken cancellationToken = default)
    {
        var profile = await _context.UserProfiles
            .FirstOrDefaultAsync(p => p.PhoneNumber == phoneNumber && p.TenantId == tenantId, cancellationToken);

        if (profile == null)
        {
            profile = new UserProfile
            {
                Id = Guid.NewGuid(),
                PhoneNumber = phoneNumber,
                TenantId = tenantId,
                UserName = userName,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                LastActivityAt = DateTime.UtcNow,
                LearningScore = 0,
                SatisfactionScore = 50, // Başlangıç değeri
                PreferredLanguage = "tr",
                FrequentCommandsJson = "{}",
                PreferredEntitiesJson = "{}",
                CommunicationStyleJson = "{}",
                LearnedPatternsJson = "{}",
                UsageHabitsJson = "{}",
                CustomContextJson = "{}",
                ActiveHoursJson = "{}"
            };

            _context.UserProfiles.Add(profile);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created new user profile for {PhoneNumber}", phoneNumber);
        }

        return profile;
    }

    /// <summary>
    /// Kullanıcı davranışını kaydet
    /// </summary>
    public async Task LogBehaviorAsync(
        Guid userProfileId,
        string behaviorType,
        object behaviorData,
        string? messageContent = null,
        string? commandType = null,
        bool isSuccessful = true,
        double confidence = 0,
        int responseTimeMs = 0,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var log = new UserBehaviorLog
            {
                Id = Guid.NewGuid(),
                UserProfileId = userProfileId,
                BehaviorType = behaviorType,
                BehaviorDataJson = JsonSerializer.Serialize(behaviorData),
                MessageContent = messageContent,
                CommandType = commandType,
                IsSuccessful = isSuccessful,
                Confidence = confidence,
                ResponseTimeMs = responseTimeMs,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserBehaviorLogs.Add(log);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogDebug("Logged behavior {Type} for user {UserId}", behaviorType, userProfileId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error logging user behavior");
        }
    }

    /// <summary>
    /// Kullanıcı profilini analiz et ve güncelle
    /// </summary>
    public async Task AnalyzeAndUpdateProfileAsync(
        Guid userProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles.FindAsync(new object[] { userProfileId }, cancellationToken);
            if (profile == null) return;

            // Son 30 günün davranış loglarını al
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            var recentLogs = await _context.UserBehaviorLogs
                .Where(l => l.UserProfileId == userProfileId && l.CreatedAt >= thirtyDaysAgo)
                .OrderByDescending(l => l.CreatedAt)
                .Take(1000)
                .ToListAsync(cancellationToken);

            if (!recentLogs.Any()) return;

            // 1. En çok kullanılan komutları analiz et
            await UpdateFrequentCommandsAsync(profile, recentLogs);

            // 2. Tercih edilen varlıkları analiz et
            await UpdatePreferredEntitiesAsync(profile, recentLogs);

            // 3. Konuşma tarzını analiz et
            await UpdateCommunicationStyleAsync(profile, recentLogs);

            // 4. Kullanım alışkanlıklarını analiz et
            await UpdateUsageHabitsAsync(profile, recentLogs);

            // 5. Aktif saatleri analiz et
            await UpdateActiveHoursAsync(profile, recentLogs);

            // 6. Öğrenilmiş pattern'leri güncelle
            await UpdateLearnedPatternsAsync(profile, recentLogs);

            // 7. Ortalama yanıt süresini güncelle
            profile.AverageResponseTime = recentLogs.Average(l => l.ResponseTimeMs) / 1000.0;

            // 8. Öğrenme skorunu güncelle
            profile.LearningScore = CalculateLearningScore(recentLogs);

            // 9. Memnuniyet skorunu güncelle
            profile.SatisfactionScore = CalculateSatisfactionScore(recentLogs);

            profile.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated profile for user {UserId}, Learning Score: {Score}", 
                userProfileId, profile.LearningScore);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing user profile");
        }
    }

    /// <summary>
    /// En çok kullanılan komutları güncelle
    /// </summary>
    private async Task UpdateFrequentCommandsAsync(UserProfile profile, List<UserBehaviorLog> logs)
    {
        var commandCounts = logs
            .Where(l => !string.IsNullOrEmpty(l.CommandType))
            .GroupBy(l => l.CommandType)
            .Select(g => new { Command = g.Key!, Count = g.Count(), SuccessRate = g.Average(x => x.IsSuccessful ? 1.0 : 0.0) })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToDictionary(x => x.Command, x => new { x.Count, x.SuccessRate });

        profile.FrequentCommandsJson = JsonSerializer.Serialize(commandCounts);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Tercih edilen varlıkları güncelle (ürünler, müşteriler vb.)
    /// </summary>
    private async Task UpdatePreferredEntitiesAsync(UserProfile profile, List<UserBehaviorLog> logs)
    {
        var preferredEntities = new Dictionary<string, Dictionary<string, int>>();

        foreach (var log in logs.Where(l => !string.IsNullOrEmpty(l.BehaviorDataJson)))
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(log.BehaviorDataJson!);
                if (data == null) continue;

                // Ürün adlarını çıkar
                if (data.ContainsKey("productName"))
                {
                    var productName = data["productName"].GetString();
                    if (!string.IsNullOrEmpty(productName))
                    {
                        if (!preferredEntities.ContainsKey("products"))
                            preferredEntities["products"] = new Dictionary<string, int>();
                        
                        if (!preferredEntities["products"].ContainsKey(productName))
                            preferredEntities["products"][productName] = 0;
                        
                        preferredEntities["products"][productName]++;
                    }
                }

                // Müşteri adlarını çıkar
                if (data.ContainsKey("customerName"))
                {
                    var customerName = data["customerName"].GetString();
                    if (!string.IsNullOrEmpty(customerName))
                    {
                        if (!preferredEntities.ContainsKey("customers"))
                            preferredEntities["customers"] = new Dictionary<string, int>();
                        
                        if (!preferredEntities["customers"].ContainsKey(customerName))
                            preferredEntities["customers"][customerName] = 0;
                        
                        preferredEntities["customers"][customerName]++;
                    }
                }
            }
            catch { }
        }

        // En çok kullanılan 20 varlığı tut
        foreach (var entityType in preferredEntities.Keys.ToList())
        {
            preferredEntities[entityType] = preferredEntities[entityType]
                .OrderByDescending(x => x.Value)
                .Take(20)
                .ToDictionary(x => x.Key, x => x.Value);
        }

        profile.PreferredEntitiesJson = JsonSerializer.Serialize(preferredEntities);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Konuşma tarzını analiz et
    /// </summary>
    private async Task UpdateCommunicationStyleAsync(UserProfile profile, List<UserBehaviorLog> logs)
    {
        var messagesWithContent = logs.Where(l => !string.IsNullOrEmpty(l.MessageContent)).ToList();
        if (!messagesWithContent.Any())
        {
            await Task.CompletedTask;
            return;
        }

        var style = new Dictionary<string, object>
        {
            // Ortalama mesaj uzunluğu
            ["avgMessageLength"] = messagesWithContent.Average(l => l.MessageContent!.Length),
            
            // Kısa mesaj tercihi (< 20 karakter)
            ["prefersShortMessages"] = messagesWithContent.Count(l => l.MessageContent!.Length < 20) > messagesWithContent.Count / 2,
            
            // Emoji kullanımı
            ["usesEmojis"] = messagesWithContent.Any(l => ContainsEmoji(l.MessageContent!)),
            
            // Resmi/gayri resmi dil
            ["formalLanguage"] = AnalyzeFormalityLevel(messagesWithContent.Select(l => l.MessageContent!).ToList()),
            
            // Soru sorma sıklığı
            ["asksQuestions"] = messagesWithContent.Count(l => l.MessageContent!.Contains('?')) > messagesWithContent.Count / 4,
            
            // Komut tarzı (direkt/kibarca)
            ["commandStyle"] = AnalyzeCommandStyle(messagesWithContent.Select(l => l.MessageContent!).ToList())
        };

        profile.CommunicationStyleJson = JsonSerializer.Serialize(style);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Kullanım alışkanlıklarını güncelle
    /// </summary>
    private async Task UpdateUsageHabitsAsync(UserProfile profile, List<UserBehaviorLog> logs)
    {
        var habits = new Dictionary<string, object>
        {
            // Günlük ortalama mesaj sayısı
            ["avgMessagesPerDay"] = logs.Count / 30.0,
            
            // En aktif gün
            ["mostActiveDay"] = logs.GroupBy(l => l.CreatedAt.DayOfWeek)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault()?.Key.ToString() ?? "Unknown",
            
            // Hızlı yanıt veren mi (< 30 saniye)
            ["quickResponder"] = logs.Average(l => l.ResponseTimeMs) < 30000,
            
            // Multi-turn conversation kullanımı
            ["usesMultiTurn"] = logs.Count(l => l.BehaviorType == "multi_turn_dialogue") > logs.Count / 10,
            
            // Hata toleransı (başarısız komutlardan sonra tekrar deneme)
            ["errorTolerance"] = CalculateErrorTolerance(logs),
            
            // Tercih edilen komut kategorisi
            ["preferredCategory"] = DeterminePreferredCategory(logs)
        };

        profile.UsageHabitsJson = JsonSerializer.Serialize(habits);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Aktif saatleri güncelle
    /// </summary>
    private async Task UpdateActiveHoursAsync(UserProfile profile, List<UserBehaviorLog> logs)
    {
        var hourlyActivity = logs
            .GroupBy(l => l.CreatedAt.Hour)
            .Select(g => new { Hour = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToDictionary(x => x.Hour.ToString(), x => x.Count);

        var activeHours = new Dictionary<string, object>
        {
            ["hourlyDistribution"] = hourlyActivity,
            ["peakHour"] = hourlyActivity.OrderByDescending(x => x.Value).FirstOrDefault().Key ?? "Unknown",
            ["isNightUser"] = hourlyActivity.Where(x => int.Parse(x.Key) >= 22 || int.Parse(x.Key) <= 6).Sum(x => x.Value) > logs.Count / 3
        };

        profile.ActiveHoursJson = JsonSerializer.Serialize(activeHours);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Öğrenilmiş pattern'leri güncelle
    /// </summary>
    private async Task UpdateLearnedPatternsAsync(UserProfile profile, List<UserBehaviorLog> logs)
    {
        var patterns = new Dictionary<string, List<string>>();

        // High confidence mesajlardan pattern'leri çıkar
        var highConfidenceLogs = logs.Where(l => l.Confidence > 0.8 && !string.IsNullOrEmpty(l.MessageContent)).ToList();

        foreach (var log in highConfidenceLogs)
        {
            if (string.IsNullOrEmpty(log.CommandType)) continue;

            if (!patterns.ContainsKey(log.CommandType))
                patterns[log.CommandType] = new List<string>();

            var normalizedMessage = log.MessageContent!.ToLowerInvariant().Trim();
            if (!patterns[log.CommandType].Contains(normalizedMessage) && patterns[log.CommandType].Count < 10)
            {
                patterns[log.CommandType].Add(normalizedMessage);
            }
        }

        profile.LearnedPatternsJson = JsonSerializer.Serialize(patterns);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Öğrenme skorunu hesapla (0-100)
    /// </summary>
    private int CalculateLearningScore(List<UserBehaviorLog> logs)
    {
        if (!logs.Any()) return 0;

        var factors = new Dictionary<string, double>
        {
            // Toplam etkileşim sayısı (max 30 puan)
            ["interaction"] = Math.Min(logs.Count / 100.0 * 30, 30),
            
            // Başarı oranı (max 30 puan)
            ["success"] = logs.Average(l => l.IsSuccessful ? 1.0 : 0.0) * 30,
            
            // Ortalama confidence (max 20 puan)
            ["confidence"] = logs.Average(l => l.Confidence) * 20,
            
            // Çeşitlilik (farklı komut tipleri) (max 20 puan)
            ["diversity"] = Math.Min(logs.Select(l => l.CommandType).Distinct().Count() / 10.0 * 20, 20)
        };

        return (int)factors.Values.Sum();
    }

    /// <summary>
    /// Memnuniyet skorunu hesapla (0-100)
    /// </summary>
    private int CalculateSatisfactionScore(List<UserBehaviorLog> logs)
    {
        if (!logs.Any()) return 50;

        var successRate = logs.Average(l => l.IsSuccessful ? 1.0 : 0.0);
        var avgConfidence = logs.Average(l => l.Confidence);
        var avgResponseTime = logs.Average(l => l.ResponseTimeMs);

        // Hızlı yanıt = yüksek memnuniyet
        var responseScore = avgResponseTime < 5000 ? 1.0 : avgResponseTime < 15000 ? 0.7 : 0.4;

        var score = (successRate * 40) + (avgConfidence * 30) + (responseScore * 30);
        return (int)Math.Min(Math.Max(score, 0), 100);
    }

    /// <summary>
    /// Kullanıcıya özel context oluştur
    /// </summary>
    public async Task<string> BuildPersonalizedContextAsync(
        Guid userProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var profile = await _context.UserProfiles.FindAsync(new object[] { userProfileId }, cancellationToken);
            if (profile == null) return string.Empty;

            var contextParts = new List<string>();

            // Kullanıcı bilgisi
            contextParts.Add($"Kullanıcı: {profile.UserName ?? profile.PhoneNumber}");

            // Öğrenme seviyesi
            if (profile.LearningScore > 70)
                contextParts.Add("Bu kullanıcı deneyimli, kısa ve net yanıtlar tercih eder.");
            else if (profile.LearningScore < 30)
                contextParts.Add("Bu kullanıcı yeni, detaylı açıklamalar yapılmalı.");

            // En çok kullanılan komutlar
            if (!string.IsNullOrEmpty(profile.FrequentCommandsJson))
            {
                try
                {
                    var commands = JsonSerializer.Deserialize<Dictionary<string, object>>(profile.FrequentCommandsJson);
                    if (commands != null && commands.Any())
                    {
                        contextParts.Add($"Sık kullanılan işlemler: {string.Join(", ", commands.Keys.Take(3))}");
                    }
                }
                catch { }
            }

            // Konuşma tarzı
            if (!string.IsNullOrEmpty(profile.CommunicationStyleJson))
            {
                try
                {
                    var style = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(profile.CommunicationStyleJson);
                    if (style != null)
                    {
                        if (style.ContainsKey("prefersShortMessages") && style["prefersShortMessages"].GetBoolean())
                            contextParts.Add("Kısa mesajlar tercih eder.");
                        
                        if (style.ContainsKey("formalLanguage"))
                        {
                            var formality = style["formalLanguage"].GetString();
                            if (formality == "formal")
                                contextParts.Add("Resmi dil kullanılmalı.");
                            else if (formality == "casual")
                                contextParts.Add("Samimi dil kullanılabilir.");
                        }
                    }
                }
                catch { }
            }

            // Tercih edilen varlıklar
            if (!string.IsNullOrEmpty(profile.PreferredEntitiesJson))
            {
                try
                {
                    var entities = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(profile.PreferredEntitiesJson);
                    if (entities != null && entities.ContainsKey("products"))
                    {
                        var topProducts = entities["products"].OrderByDescending(x => x.Value).Take(3).Select(x => x.Key);
                        contextParts.Add($"Sık kullandığı ürünler: {string.Join(", ", topProducts)}");
                    }
                }
                catch { }
            }

            // Custom context
            if (!string.IsNullOrEmpty(profile.CustomContextJson) && profile.CustomContextJson != "{}")
            {
                try
                {
                    var custom = JsonSerializer.Deserialize<Dictionary<string, string>>(profile.CustomContextJson);
                    if (custom != null && custom.Any())
                    {
                        foreach (var item in custom)
                        {
                            contextParts.Add($"{item.Key}: {item.Value}");
                        }
                    }
                }
                catch { }
            }

            return string.Join("\n", contextParts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building personalized context");
            return string.Empty;
        }
    }

    // Helper methods
    private bool ContainsEmoji(string text)
    {
        return text.Any(c => c >= 0x1F600 && c <= 0x1F64F);
    }

    private string AnalyzeFormalityLevel(List<string> messages)
    {
        var formalWords = new[] { "lütfen", "rica ederim", "teşekkür ederim", "sayın", "efendim" };
        var casualWords = new[] { "slm", "naber", "tmm", "ok", "eyw" };

        var formalCount = messages.Count(m => formalWords.Any(w => m.ToLowerInvariant().Contains(w)));
        var casualCount = messages.Count(m => casualWords.Any(w => m.ToLowerInvariant().Contains(w)));

        if (formalCount > casualCount * 2) return "formal";
        if (casualCount > formalCount * 2) return "casual";
        return "neutral";
    }

    private string AnalyzeCommandStyle(List<string> messages)
    {
        var politeIndicators = messages.Count(m => 
            m.Contains("lütfen") || m.Contains("rica") || m.Contains("?"));
        
        return politeIndicators > messages.Count / 2 ? "polite" : "direct";
    }

    private double CalculateErrorTolerance(List<UserBehaviorLog> logs)
    {
        var failedLogs = logs.Where(l => !l.IsSuccessful).ToList();
        if (!failedLogs.Any()) return 1.0;

        var retriedAfterFailure = 0;
        foreach (var failed in failedLogs)
        {
            var nextLog = logs.FirstOrDefault(l => 
                l.CreatedAt > failed.CreatedAt && 
                l.CommandType == failed.CommandType &&
                (l.CreatedAt - failed.CreatedAt).TotalMinutes < 5);
            
            if (nextLog != null) retriedAfterFailure++;
        }

        return failedLogs.Any() ? (double)retriedAfterFailure / failedLogs.Count : 1.0;
    }

    private string DeterminePreferredCategory(List<UserBehaviorLog> logs)
    {
        var commandTypes = logs
            .Where(l => !string.IsNullOrEmpty(l.CommandType))
            .GroupBy(l => GetCommandCategory(l.CommandType!))
            .OrderByDescending(g => g.Count())
            .FirstOrDefault();

        return commandTypes?.Key ?? "general";
    }

    private string GetCommandCategory(string commandType)
    {
        if (commandType.Contains("Product")) return "product_management";
        if (commandType.Contains("Customer")) return "customer_management";
        if (commandType.Contains("Invoice")) return "invoicing";
        if (commandType.Contains("Contract")) return "contracts";
        return "other";
    }
}
