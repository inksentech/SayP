using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text.RegularExpressions;
using SayP.Domain.Enums;

namespace SayP.Application.Services;

/// <summary>
/// Extracts entities from natural language text
/// </summary>
public class EntityExtractor
{
    private readonly ILogger<EntityExtractor> _logger;

    public EntityExtractor(ILogger<EntityExtractor> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Extract entities from message based on command type
    /// </summary>
    public Dictionary<string, object> ExtractEntities(string message, CommandType? commandType = null)
    {
        var entities = new Dictionary<string, object>();

        // Extract common entities
        ExtractPrices(message, entities);
        ExtractQuantities(message, entities);
        ExtractDates(message, entities);
        ExtractPhoneNumbers(message, entities);
        ExtractEmails(message, entities);
        ExtractPercentages(message, entities);
        ExtractTaxInfo(message, entities);
        ExtractProductNames(message, entities);
        ExtractCustomerNames(message, entities);
        ExtractIds(message, entities);
        ExtractTimes(message, entities);
        ExtractTimeRanges(message, entities);
        ExtractDateRanges(message, entities);

        // Command-specific extraction
        if (commandType.HasValue)
        {
            ExtractCommandSpecificEntities(message, commandType.Value, entities);
        }

        _logger.LogInformation("Extracted {Count} entities from message", entities.Count);
        return entities;
    }

    /// <summary>
    /// Extract prices (15000 TL, 15.000 TL, 15,000 TL, $100)
    /// </summary>
    private void ExtractPrices(string message, Dictionary<string, object> entities)
    {
        // Turkish Lira patterns
        var patterns = new[]
        {
            @"(\d+[\.,]?\d*)\s*(?:TL|tl|₺|lira)",
            @"(?:fiyat|fiyatı|tutar|tutarı|ücret|para)[\s:]*(\d+[\.,]?\d*)",
            @"(\d+[\.,]?\d*)\s*(?:TL|tl)",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var priceStr = match.Groups[1].Value.Replace(".", "").Replace(",", ".");
                if (decimal.TryParse(priceStr, out var price))
                {
                    entities["price"] = price;
                    _logger.LogDebug("Extracted price: {Price}", price);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Extract quantities (10 adet, 5 tane, 3x)
    /// </summary>
    private void ExtractQuantities(string message, Dictionary<string, object> entities)
    {
        var patterns = new[]
        {
            @"(\d+)\s*(?:adet|tane|adet|piece|pcs)",
            @"(?:miktar|quantity|stok)[\s:]*(\d+)",
            @"(\d+)\s*x\s*",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success && int.TryParse(match.Groups[1].Value, out var quantity))
            {
                entities["stockQuantity"] = quantity;
                _logger.LogDebug("Extracted quantity: {Quantity}", quantity);
                return;
            }
        }
    }

    /// <summary>
    /// Extract tax numbers and tax offices
    /// </summary>
    private void ExtractTaxInfo(string message, Dictionary<string, object> entities)
    {
        // Tax number pattern (10 digits)
        var taxNoPattern = @"(?:vergi\s+no|vergi\s+numarası|vkn)[\s:]*(\d{10})";
        var taxNoMatch = Regex.Match(message, taxNoPattern, RegexOptions.IgnoreCase);
        
        if (taxNoMatch.Success)
        {
            entities["taxNumber"] = taxNoMatch.Groups[1].Value;
            _logger.LogDebug("Extracted tax number: {TaxNumber}", taxNoMatch.Groups[1].Value);
        }

        // Tax office pattern
        var taxOfficePattern = @"(?:vergi\s+dairesi|vd)[\s:]*([A-ZĞÜŞİÖÇa-zğüşıöç\s]+?)(?:\s+(?:vergi|telefon|0\d{3})|$)";
        var taxOfficeMatch = Regex.Match(message, taxOfficePattern, RegexOptions.IgnoreCase);
        
        if (taxOfficeMatch.Success)
        {
            var office = taxOfficeMatch.Groups[1].Value.Trim();
            if (office.Length > 2)
            {
                entities["taxOffice"] = office;
                _logger.LogDebug("Extracted tax office: {TaxOffice}", office);
            }
        }
    }

    /// <summary>
    /// Extract dates (2024-01-15, 15/01/2024, bugün, yarın)
    /// </summary>
    private void ExtractDates(string message, Dictionary<string, object> entities)
    {
        DateTime? extractedDate = null;

        // Relative dates
        if (Regex.IsMatch(message, @"\bbugün\b", RegexOptions.IgnoreCase))
        {
            extractedDate = DateTime.Today;
        }
        else if (Regex.IsMatch(message, @"\byarın\b", RegexOptions.IgnoreCase) || 
                 Regex.IsMatch(message, @"\byarin\b", RegexOptions.IgnoreCase)) // Handle "yarin" without Turkish char
        {
            extractedDate = DateTime.Today.AddDays(1);
        }
        else if (Regex.IsMatch(message, @"\bdün\b", RegexOptions.IgnoreCase))
        {
            extractedDate = DateTime.Today.AddDays(-1);
        }
        else
        {
            // Absolute dates
            var datePatterns = new[]
            {
                @"(\d{4})-(\d{2})-(\d{2})",
                @"(\d{2})/(\d{2})/(\d{4})",
                @"(\d{2})\.(\d{2})\.(\d{4})",
            };

            foreach (var pattern in datePatterns)
            {
                var match = Regex.Match(message, pattern);
                if (match.Success)
                {
                    try
                    {
                        extractedDate = DateTime.Parse(match.Value);
                        break;
                    }
                    catch { }
                }
            }
        }

        if (extractedDate.HasValue)
        {
            // Store in multiple formats for compatibility
            entities["date"] = extractedDate.Value;
            entities["appointmentDate"] = extractedDate.Value; // For appointment slot filling
            _logger.LogDebug("Extracted date: {Date}", extractedDate.Value);
        }
    }

    /// <summary>
    /// Extract phone numbers
    /// </summary>
    private void ExtractPhoneNumbers(string message, Dictionary<string, object> entities)
    {
        var patterns = new[]
        {
            @"(?:tel|telefon|phone)[\s:]*(\+?\d[\d\s\-\(\)]{8,})",
            @"(\+90\s?\d{3}\s?\d{3}\s?\d{2}\s?\d{2})",
            @"(0\d{3}\s?\d{3}\s?\d{2}\s?\d{2})",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var phone = match.Groups[1].Value.Trim();
                entities["phone"] = phone;
                _logger.LogDebug("Extracted phone: {Phone}", phone);
                return;
            }
        }
    }

    /// <summary>
    /// Extract email addresses
    /// </summary>
    private void ExtractEmails(string message, Dictionary<string, object> entities)
    {
        var pattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b";
        var match = Regex.Match(message, pattern);
        
        if (match.Success)
        {
            entities["email"] = match.Value;
            _logger.LogDebug("Extracted email: {Email}", match.Value);
        }
    }

    /// <summary>
    /// Extract percentages (18%, %18, KDV 18)
    /// </summary>
    private void ExtractPercentages(string message, Dictionary<string, object> entities)
    {
        var patterns = new[]
        {
            @"(?:kdv|vergi|tax|vat)[\s:]*(\d+)",
            @"(\d+)\s*%",
            @"%\s*(\d+)",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success && decimal.TryParse(match.Groups[1].Value, out var percentage))
            {
                entities["taxRate"] = percentage;
                _logger.LogDebug("Extracted tax rate: {TaxRate}", percentage);
                return;
            }
        }
    }

    /// <summary>
    /// Extract product names
    /// </summary>
    private void ExtractProductNames(string message, Dictionary<string, object> entities)
    {
        // Look for quoted names
        var quotedMatch = Regex.Match(message, @"[""']([^""']+)[""']");
        if (quotedMatch.Success)
        {
            entities["name"] = quotedMatch.Groups[1].Value;
            return;
        }

        // Look for "adı/adında/isimli" patterns
        var namePatterns = new[]
        {
            @"(?:ad[ıi]|isim[li]?|name)[\s:]+([A-Za-zğüşıöçĞÜŞİÖÇ\s]+?)(?:\s+(?:ekle|oluştur|yap|sil|güncelle)|,|$)",
            @"([A-Za-zğüşıöçĞÜŞİÖÇ]+)\s+(?:ekle|oluştur|yap)",
        };

        foreach (var pattern in namePatterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var name = match.Groups[1].Value.Trim();
                if (name.Length > 2 && !IsCommonWord(name))
                {
                    entities["name"] = name;
                    _logger.LogDebug("Extracted name: {Name}", name);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Extract customer names (both person and company names)
    /// </summary>
    private void ExtractCustomerNames(string message, Dictionary<string, object> entities)
    {
        _logger.LogDebug("ExtractCustomerNames called with message: {Message}", message);
        
        // Check for "firma ekle/oluştur" pattern first
        var companyPatterns = new[]
        {
            @"(?:firma|şirket|kurum)\s+(?:ekle|oluştur|kaydet|yap)\s+([A-ZĞÜŞİÖÇa-zğüşıöç0-9\s]+?)(?:\s+(?:0\d{3}|\d{4})|$)",
            @"(?:firma|şirket|kurum)[\s:]+([A-ZĞÜŞİÖÇa-zğüşıöç0-9\s]+?)(?:\s+(?:0\d{3}|\d{4})|$)"
        };

        foreach (var pattern in companyPatterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var name = match.Groups[1].Value.Trim();
                // Remove trailing words like "ekle", "vergi", etc.
                name = Regex.Replace(name, @"\s+(ekle|oluştur|vergi|no|telefon).*$", "", RegexOptions.IgnoreCase).Trim();
                
                if (name.Length > 2 && !IsCommonWord(name))
                {
                    entities["customerName"] = name;
                    _logger.LogInformation("✅ Extracted company name: {Name}", name);
                    return;
                }
            }
        }

        // Appointment-specific patterns: "... için" or "... adına"
        // CRITICAL: Use word boundary and specific context to avoid over-matching
        var appointmentPatterns = new[]
        {
            // "Yeni müşteri oluştur Ahmet Yılmaz" - CreateCustomer pattern
            @"(?:yeni\s+)?(?:müşteri|musteri|customer)\s+(?:oluştur|olustur|ekle|kaydet)\s+([A-ZĞÜŞIÖÇ][a-zğüşıöç]+\s+[A-ZĞÜŞIÖÇ][a-zğüşıöç]+)",
            // "Ahmet Yılmaz için" at the END of sentence
            @"([A-ZĞÜŞIÖÇ][a-zğüşıöç]+\s+[A-ZĞÜŞIÖÇ][a-zğüşıöç]+)\s+için\s*$",
            // "Ahmet Yılmaz için" before punctuation
            @"([A-ZĞÜŞIÖÇ][a-zğüşıöç]+\s+[A-ZĞÜŞIÖÇ][a-zğüşıöç]+)\s+için[.,!?]",
            // "Ahmet Yılmaz adına"
            @"([A-ZĞÜŞIÖÇ][a-zğüşıöç]+\s+[A-ZĞÜŞIÖÇ][a-zğüşıöç]+)\s+(?:adına|adina)",
            // "müşteri: Ahmet Yılmaz"
            @"(?:müşteri|musteri|customer)[\s:]+([A-ZĞÜŞIÖÇ][a-zğüşıöç]+\s+[A-ZĞÜŞIÖÇ][a-zğüşıöç]+)"
        };

        foreach (var pattern in appointmentPatterns)
        {
            _logger.LogDebug("Trying appointment pattern: {Pattern}", pattern);
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                _logger.LogDebug("Pattern matched! Groups: {Groups}", match.Groups.Count);
                var name = match.Groups[1].Value.Trim();
                _logger.LogDebug("Extracted name before validation: '{Name}'", name);
                
                if (name.Length > 2 && !IsCommonWord(name))
                {
                    entities["customerName"] = name;
                    _logger.LogInformation("✅ Extracted customer name (appointment): {Name}", name);
                    return;
                }
                else
                {
                    _logger.LogDebug("Name rejected - Length: {Length}, IsCommonWord: {IsCommon}", name.Length, IsCommonWord(name));
                }
            }
        }

        // Turkish person name patterns (First Last) - fallback only
        var personPattern = @"\b([A-ZĞÜŞIÖÇ][a-zğüşıöç]+(?:\s+[A-ZĞÜŞIÖÇ][a-zğüşıöç]+)+)\b";
        var personMatches = Regex.Matches(message, personPattern);
        
        if (personMatches.Count > 0 && !entities.ContainsKey("customerName"))
        {
            // Find the first valid name (not a common word)
            foreach (Match match in personMatches)
            {
                var name = match.Groups[1].Value;
                var words = name.Split(' ');
                
                // Check if any word is a common word (verb, etc.)
                if (words.Any(w => IsCommonWord(w)))
                {
                    _logger.LogDebug("Rejected name '{Name}' - contains common word", name);
                    continue;
                }
                
                entities["customerName"] = name;
                _logger.LogInformation("✅ Extracted customer name (person): {Name}", name);
                return;
            }
            
            _logger.LogWarning("❌ No valid customer name found in message");
        }
        else if (!entities.ContainsKey("customerName"))
        {
            _logger.LogWarning("❌ No customer name found in message");
        }
    }

    /// <summary>
    /// Extract IDs (product ID, customer ID, invoice number)
    /// </summary>
    private void ExtractIds(string message, Dictionary<string, object> entities)
    {
        var patterns = new[]
        {
            @"(?:id|kod|code|numara|no)[\s:]*(\d+)",
            @"#(\d+)",
            @"\b(\d{4,})\b", // 4+ digit numbers
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var id = match.Groups[1].Value;
                entities["id"] = id;
                _logger.LogDebug("Extracted ID: {Id}", id);
                return;
            }
        }
    }

    /// <summary>
    /// Extract times (14:30, 2 buçuk, saat 3)
    /// </summary>
    private void ExtractTimes(string message, Dictionary<string, object> entities)
    {
        // HH:mm format
        var timePattern = @"(\d{1,2}):(\d{2})";
        var match = Regex.Match(message, timePattern);
        if (match.Success)
        {
            var hours = int.Parse(match.Groups[1].Value);
            var minutes = int.Parse(match.Groups[2].Value);
            entities["startTime"] = new TimeSpan(hours, minutes, 0);
            _logger.LogDebug("Extracted time: {Hours}:{Minutes}", hours, minutes);
            return;
        }

        // Turkish time expressions (2 buçuk = 14:30, 3 = 15:00)
        var turkishTimePatterns = new Dictionary<string, TimeSpan>
        {
            { @"\b(\d+)\s*buçuk\b", TimeSpan.Zero }, // Will be calculated
            { @"\bsaat\s+(\d+)\b", TimeSpan.Zero }
        };

        foreach (var pattern in turkishTimePatterns.Keys)
        {
            match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var hour = int.Parse(match.Groups[1].Value);
                var minutes = pattern.Contains("buçuk") ? 30 : 0;
                entities["startTime"] = new TimeSpan(hour, minutes, 0);
                _logger.LogDebug("Extracted Turkish time: {Hour}:{Minutes}", hour, minutes);
                return;
            }
        }
    }

    /// <summary>
    /// Extract time ranges (3 ile 5 arası, 14:00-16:00)
    /// </summary>
    private void ExtractTimeRanges(string message, Dictionary<string, object> entities)
    {
        // HH:mm - HH:mm format
        var rangePattern = @"(\d{1,2}):(\d{2})\s*-\s*(\d{1,2}):(\d{2})";
        var match = Regex.Match(message, rangePattern);
        if (match.Success)
        {
            var startHour = int.Parse(match.Groups[1].Value);
            var startMin = int.Parse(match.Groups[2].Value);
            var endHour = int.Parse(match.Groups[3].Value);
            var endMin = int.Parse(match.Groups[4].Value);
            
            entities["startTime"] = new TimeSpan(startHour, startMin, 0);
            entities["endTime"] = new TimeSpan(endHour, endMin, 0);
            _logger.LogDebug("Extracted time range: {Start} - {End}", entities["startTime"], entities["endTime"]);
            return;
        }

        // Turkish format (3 ile 5 arası, 14 ile 16 arası)
        var turkishRangePattern = @"(\d+)\s+(?:ile|to)\s+(\d+)\s+(?:arası|between)";
        match = Regex.Match(message, turkishRangePattern, RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var startHour = int.Parse(match.Groups[1].Value);
            var endHour = int.Parse(match.Groups[2].Value);
            
            entities["startTime"] = new TimeSpan(startHour, 0, 0);
            entities["endTime"] = new TimeSpan(endHour, 0, 0);
            _logger.LogDebug("Extracted Turkish time range: {Start} - {End}", entities["startTime"], entities["endTime"]);
            return;
        }
    }

    /// <summary>
    /// Extract date ranges (bu hafta, gelecek hafta, 1-5 Kasım)
    /// </summary>
    private void ExtractDateRanges(string message, Dictionary<string, object> entities)
    {
        // "Bu hafta" = this week
        if (Regex.IsMatch(message, @"\bbu\s+hafta\b", RegexOptions.IgnoreCase))
        {
            var today = DateTime.Today;
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek + 1); // Monday
            var endOfWeek = startOfWeek.AddDays(6); // Sunday
            
            entities["startDate"] = startOfWeek;
            entities["endDate"] = endOfWeek;
            _logger.LogDebug("Extracted date range: This week {Start} - {End}", startOfWeek, endOfWeek);
            return;
        }

        // "Gelecek hafta" = next week
        if (Regex.IsMatch(message, @"\bgelecek\s+hafta\b", RegexOptions.IgnoreCase))
        {
            var today = DateTime.Today;
            var startOfNextWeek = today.AddDays(-(int)today.DayOfWeek + 8); // Next Monday
            var endOfNextWeek = startOfNextWeek.AddDays(6); // Next Sunday
            
            entities["startDate"] = startOfNextWeek;
            entities["endDate"] = endOfNextWeek;
            _logger.LogDebug("Extracted date range: Next week {Start} - {End}", startOfNextWeek, endOfNextWeek);
            return;
        }
    }

    /// <summary>
    /// Extract command-specific entities
    /// </summary>
    private void ExtractCommandSpecificEntities(string message, CommandType commandType, Dictionary<string, object> entities)
    {
        switch (commandType)
        {
            case CommandType.SearchProducts:
            case CommandType.ListProducts:
                ExtractSearchQuery(message, entities);
                ExtractPriceRange(message, entities);
                break;

            case CommandType.CreateInvoice:
                ExtractInvoiceItems(message, entities);
                break;

            case CommandType.CreateContract:
                ExtractContractDetails(message, entities);
                break;

            case CommandType.CreateAppointment:
            case CommandType.ListAppointments:
            case CommandType.CheckAvailability:
                ExtractAppointmentEntities(message, entities);
                break;
        }
    }

    /// <summary>
    /// Extract appointment-specific entities (service name, duration)
    /// </summary>
    private void ExtractAppointmentEntities(string message, Dictionary<string, object> entities)
    {
        // Extract service name from patterns like "Saç Kesimi randevusu", "Boyama için randevu"
        var servicePatterns = new[]
        {
            // "için saç kesimi randevusu" → "saç kesimi"
            @"için\s+([A-ZĞÜŞİÖÇa-zğüşıöç\s]+?)\s+randevu",
            // "saç kesimi randevusu" → "saç kesimi"
            @"([A-ZĞÜŞİÖÇa-zğüşıöç\s]+?)\s+randevu",
            // "randevu için saç kesimi" → "saç kesimi"
            @"randevu\s+için\s+([A-ZĞÜŞİÖÇa-zğüşıöç\s]+)",
            // "saç kesimi için randevu" → "saç kesimi"
            @"([A-ZĞÜŞİÖÇa-zğüşıöç\s]+?)\s+için\s+randevu"
        };

        foreach (var pattern in servicePatterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var serviceName = match.Groups[1].Value.Trim();
                // Remove common words from the beginning
                serviceName = Regex.Replace(serviceName, @"^(saat|için|adına|adina)\s+", "", RegexOptions.IgnoreCase).Trim();
                
                if (!IsCommonWord(serviceName) && serviceName.Length > 2)
                {
                    entities["serviceName"] = serviceName;
                    _logger.LogDebug("Extracted service name: {ServiceName}", serviceName);
                    break;
                }
            }
        }

        // Extract duration (30 dakika, 1 saat)
        var durationPattern = @"(\d+)\s*(?:dakika|minute|min|saat|hour|hr)";
        var durationMatch = Regex.Match(message, durationPattern, RegexOptions.IgnoreCase);
        if (durationMatch.Success)
        {
            var value = int.Parse(durationMatch.Groups[1].Value);
            var unit = durationMatch.Groups[0].Value.ToLower();
            
            var minutes = unit.Contains("saat") || unit.Contains("hour") ? value * 60 : value;
            entities["durationMinutes"] = minutes;
            _logger.LogDebug("Extracted duration: {Minutes} minutes", minutes);
        }
    }

    /// <summary>
    /// Extract search query
    /// </summary>
    private void ExtractSearchQuery(string message, Dictionary<string, object> entities)
    {
        var patterns = new[]
        {
            @"(?:ara|search|bul)[\s:]+([A-Za-zğüşıöçĞÜŞİÖÇ0-9\s]+?)(?:\s+(?:için|ile)|$)",
            @"([A-Za-zğüşıöçĞÜŞİÖÇ0-9]+)\s+ara",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                entities["searchQuery"] = match.Groups[1].Value.Trim();
                return;
            }
        }
    }

    /// <summary>
    /// Extract price range (10000-20000 TL, 10000 ile 20000 arası)
    /// </summary>
    private void ExtractPriceRange(string message, Dictionary<string, object> entities)
    {
        var patterns = new[]
        {
            @"(\d+[\.,]?\d*)\s*-\s*(\d+[\.,]?\d*)\s*(?:TL|tl)",
            @"(\d+[\.,]?\d*)\s+(?:ile|to)\s+(\d+[\.,]?\d*)\s+(?:arası|between)",
        };

        foreach (var pattern in patterns)
        {
            var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var min = decimal.Parse(match.Groups[1].Value.Replace(".", "").Replace(",", "."));
                var max = decimal.Parse(match.Groups[2].Value.Replace(".", "").Replace(",", "."));
                
                entities["minPrice"] = min;
                entities["maxPrice"] = max;
                _logger.LogDebug("Extracted price range: {Min}-{Max}", min, max);
                return;
            }
        }
    }

    /// <summary>
    /// Extract invoice items
    /// </summary>
    private void ExtractInvoiceItems(string message, Dictionary<string, object> entities)
    {
        // Simple pattern: "2 laptop, 3 mouse"
        var pattern = @"(\d+)\s+([A-Za-zğüşıöçĞÜŞİÖÇ]+)";
        var matches = Regex.Matches(message, pattern, RegexOptions.IgnoreCase);

        if (matches.Count > 0)
        {
            var items = new List<Dictionary<string, object>>();
            foreach (Match match in matches)
            {
                items.Add(new Dictionary<string, object>
                {
                    { "quantity", int.Parse(match.Groups[1].Value) },
                    { "productName", match.Groups[2].Value }
                });
            }
            entities["items"] = items;
        }
    }

    /// <summary>
    /// Extract contract details
    /// </summary>
    private void ExtractContractDetails(string message, Dictionary<string, object> entities)
    {
        // Extract duration (1 yıl, 6 ay, 12 month)
        var durationPattern = @"(\d+)\s*(yıl|ay|month|year)";
        var match = Regex.Match(message, durationPattern, RegexOptions.IgnoreCase);
        
        if (match.Success)
        {
            var value = int.Parse(match.Groups[1].Value);
            var unit = match.Groups[2].Value.ToLower();
            
            var months = unit.Contains("yıl") || unit.Contains("year") ? value * 12 : value;
            entities["durationMonths"] = months;
        }
    }

    /// <summary>
    /// Check if word is a common word (to avoid false positives)
    /// </summary>
    private bool IsCommonWord(string word)
    {
        var commonWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ekle", "oluştur", "yap", "sil", "güncelle", "listele", "ara",
            "fiyat", "fiyatı", "tutar", "miktar", "adet", "tane",
            "ürün", "müşteri", "fatura", "sözleşme",
            "bir", "iki", "üç", "dört", "beş"
        };

        return commonWords.Contains(word.Trim());
    }

    /// <summary>
    /// Normalize extracted entities
    /// </summary>
    public Dictionary<string, object> NormalizeEntities(Dictionary<string, object> entities)
    {
        var normalized = new Dictionary<string, object>();

        foreach (var entity in entities)
        {
            var key = entity.Key;
            var value = entity.Value;

            // Normalize tax rate (convert percentage to decimal if needed)
            if (key == "taxRate" && value is decimal taxRate)
            {
                normalized[key] = taxRate > 1 ? taxRate / 100 : taxRate;
            }
            // Normalize phone (remove spaces and dashes)
            else if (key == "phone" && value is string phone)
            {
                normalized[key] = phone.Replace(" ", "").Replace("-", "").Replace("(", "").Replace(")", "");
            }
            // Normalize name (capitalize first letter)
            else if ((key == "name" || key == "customerName") && value is string name)
            {
                normalized[key] = CapitalizeWords(name);
            }
            else
            {
                normalized[key] = value;
            }
        }

        return normalized;
    }

    private string CapitalizeWords(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        
        var words = text.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
            }
        }
        return string.Join(" ", words);
    }
}
