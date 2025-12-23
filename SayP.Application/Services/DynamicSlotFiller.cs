using Microsoft.Extensions.Logging;
using SayP.Application.Interfaces;
using SayP.Domain.Interfaces;
using SayP.Domain.Models;
using System.Text;
using System.Text.Json;

namespace SayP.Application.Services;

/// <summary>
/// Dynamically fills missing parameters for discovered endpoints
/// </summary>
public class DynamicSlotFiller
{
    private readonly ILogger<DynamicSlotFiller> _logger;
    private readonly IAIProvider? _aiProvider;
    private readonly AINavigationResolver? _navigationResolver;
    private readonly AIMediaAttachmentService? _mediaAttachmentService;

    public DynamicSlotFiller(
        ILogger<DynamicSlotFiller> logger,
        IAIProvider? aiProvider = null,
        AINavigationResolver? navigationResolver = null,
        AIMediaAttachmentService? mediaAttachmentService = null)
    {
        _logger = logger;
        _aiProvider = aiProvider;
        _navigationResolver = navigationResolver;
        _mediaAttachmentService = mediaAttachmentService;
    }

    /// <summary>
    /// Check which parameters are missing and need to be filled
    /// Uses AI to determine which fields are truly important to ask the user
    /// </summary>
    public async Task<DynamicSlotFillingResult> AnalyzeSlotsAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> extractedParameters,
        string? personalizedContext = null,
        CancellationToken cancellationToken = default)
    {
        // Call overload without media context
        return await AnalyzeSlotsAsync(
            endpoint, 
            extractedParameters, 
            mediaContext: null, 
            backendUrl: null, 
            apiKey: null, 
            tenantId: null,
            personalizedContext, 
            cancellationToken);
    }

    /// <summary>
    /// Check which parameters are missing and need to be filled
    /// With support for media attachment and navigation property resolution
    /// </summary>
    public async Task<DynamicSlotFillingResult> AnalyzeSlotsAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> extractedParameters,
        MediaContext? mediaContext,
        string? backendUrl,
        string? apiKey,
        Guid? tenantId = null,
        string? personalizedContext = null,
        CancellationToken cancellationToken = default)
    {
        var result = new DynamicSlotFillingResult();

        if (endpoint.Schema == null)
        {
            result.IsComplete = true;
            result.FilledSlots = extractedParameters;
            return result;
        }

        var filledSlots = new Dictionary<string, object>(extractedParameters);
        var missingSlots = new List<MissingSlot>();

        // DEBUG: Log schema info
        _logger.LogInformation("📊 Endpoint Schema - Intent: {Intent}, HasSchema: {HasSchema}, FieldCount: {FieldCount}", 
            endpoint.Intent, 
            endpoint.Schema != null, 
            endpoint.Schema?.Fields?.Count ?? 0);

        // ✅ NEW: Resolve navigation properties (e.g., "Adl" → BrandId: 42)
        _logger.LogInformation("🔗 Navigation resolver check - HasResolver: {HasResolver}, BackendUrl: {BackendUrl}, TenantId: {TenantId}",
            _navigationResolver != null, backendUrl ?? "null", tenantId);
        
        if (_navigationResolver != null && !string.IsNullOrEmpty(backendUrl))
        {
            try
            {
                _logger.LogInformation("🔗 Calling navigation resolver for {Intent} with {SlotCount} slots", 
                    endpoint.Intent, filledSlots.Count);
                
                filledSlots = await _navigationResolver.ResolveNavigationPropertiesAsync(
                    endpoint,
                    filledSlots,
                    backendUrl,
                    apiKey,
                    tenantId,
                    cancellationToken);
                
                _logger.LogInformation("🔗 Navigation resolution completed for {Intent}", endpoint.Intent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Navigation resolution failed for {Intent}", endpoint.Intent);
            }
        }
        else
        {
            _logger.LogWarning("⚠️ Navigation resolution skipped - Resolver: {HasResolver}, BackendUrl: {HasUrl}",
                _navigationResolver != null, !string.IsNullOrEmpty(backendUrl));
        }

        // ✅ NEW: Attach media if available
        if (_mediaAttachmentService != null && mediaContext != null && !mediaContext.IsUsed)
        {
            try
            {
                filledSlots = await _mediaAttachmentService.AttachMediaAsync(
                    endpoint,
                    filledSlots,
                    mediaContext,
                    cancellationToken);
                
                _logger.LogDebug("Media attachment completed for {Intent}", endpoint.Intent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Media attachment failed for {Intent}", endpoint.Intent);
            }
        }
        
        // Get fields that need user input and auto-filled defaults (with personalized context)
        var analysis = await DetermineFieldsToAskAsync(endpoint, filledSlots, cancellationToken, personalizedContext);

        // Apply defaults for fields that are NOT already filled
        foreach (var defaultVal in analysis.AutoFilledDefaults)
        {
            if (!filledSlots.ContainsKey(defaultVal.Key))
            {
                // Convert JsonElement if necessary
                object value = defaultVal.Value;
                if (value is JsonElement element)
                {
                    value = ConvertJsonElement(element);
                }

                // Sanitize and ensure type safety based on schema
                if (endpoint.Schema != null)
                {
                    var field = endpoint.Schema.Fields.FirstOrDefault(f => 
                        f.Name.Equals(defaultVal.Key, StringComparison.OrdinalIgnoreCase) ||
                        (f.Aliases != null && f.Aliases.Any(a => a.Equals(defaultVal.Key, StringComparison.OrdinalIgnoreCase))));
                    
                    if (field != null)
                    {
                        value = SanitizeAndConvertValue(value, field);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Could not find schema field for default key '{Key}' - skipping sanitization", defaultVal.Key);
                    }
                }

                filledSlots[defaultVal.Key] = value;
                _logger.LogInformation("✅ Auto-filled default for {Field}: {Value}", defaultVal.Key, value);
            }
        }

        foreach (var field in analysis.FieldsToAsk)
        {
            var hasValue = filledSlots.ContainsKey(field.Name) ||
                          field.Aliases.Any(alias => filledSlots.ContainsKey(alias));

            if (!hasValue)
            {
                missingSlots.Add(new MissingSlot
                {
                    FieldName = field.Name,
                    Description = field.Description,
                    Type = field.Type,
                    Example = field.Example,
                    Aliases = field.Aliases
                });
            }
        }

        result.FilledSlots = filledSlots;
        result.MissingSlots = missingSlots;
        result.IsComplete = !missingSlots.Any();
        
        if (!result.IsComplete)
        {
            result.NextQuestion = GenerateNextQuestion(missingSlots, endpoint);
        }

        return result;
    }

    /// <summary>
    /// Synchronous version for backward compatibility
    /// </summary>
    public DynamicSlotFillingResult AnalyzeSlots(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> extractedParameters)
    {
        return AnalyzeSlotsAsync(endpoint, extractedParameters).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Determine which fields to ask the user using AI or fallback logic
    /// </summary>
    private async Task<FieldAnalysisResult> DetermineFieldsToAskAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> extractedParameters,
        CancellationToken cancellationToken,
        string? personalizedContext = null)
    {
        _logger.LogInformation("🔍 Determining fields to ask for {Intent}", endpoint.Intent);
        
        // Strategy 1: Use AI to determine important fields
        if (_aiProvider != null)
        {
            _logger.LogInformation("✅ AI Provider available, asking AI for important fields");
            try
            {
                var aiResult = await AskAIForImportantFieldsAsync(endpoint, cancellationToken, personalizedContext);
                if (aiResult.FieldsToAsk.Any())
                {
                    _logger.LogInformation("✅ AI determined {Count} important fields to ask for {Intent}: {Fields}", 
                        aiResult.FieldsToAsk.Count, endpoint.Intent, string.Join(", ", aiResult.FieldsToAsk.Select(f => f.Name)));
                    return aiResult;
                }
                else
                {
                    _logger.LogWarning("⚠️ AI returned empty field list, falling back");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ AI field determination failed, falling back to required fields");
            }
        }
        else
        {
            _logger.LogWarning("⚠️ AI Provider is NULL, using fallback strategy");
        }

        // Strategy 2: Fallback to required fields only
        var requiredFields = endpoint.Schema?.Fields.Where(f => f.IsRequired).ToList() ?? new List<SchemaField>();
        
        if (requiredFields.Any())
        {
            _logger.LogInformation("📋 Using {Count} required fields: {Fields}", 
                requiredFields.Count, string.Join(", ", requiredFields.Select(f => f.Name)));
            return new FieldAnalysisResult { FieldsToAsk = requiredFields };
        }
        
        // Strategy 3: If no required fields, ask for fields with examples (likely important)
        var fieldsWithExamples = endpoint.Schema?.Fields
            .Where(f => !string.IsNullOrEmpty(f.Example) || !string.IsNullOrEmpty(f.Description))
            .Take(3) // Limit to 3 most important
            .ToList() ?? new List<SchemaField>();
        
        _logger.LogInformation("💡 No required fields, asking for {Count} fields with examples/descriptions: {Fields}", 
            fieldsWithExamples.Count, string.Join(", ", fieldsWithExamples.Select(f => f.Name)));
        return new FieldAnalysisResult { FieldsToAsk = fieldsWithExamples };
    }

    /// <summary>
    /// Ask AI which fields are important to collect from the user
    /// </summary>
    private async Task<FieldAnalysisResult> AskAIForImportantFieldsAsync(
        DiscoveredEndpoint endpoint,
        CancellationToken cancellationToken,
        string? personalizedContext = null)
    {
        if (_aiProvider == null || endpoint.Schema == null)
            return new FieldAnalysisResult();

        var fieldsJson = System.Text.Json.JsonSerializer.Serialize(
            endpoint.Schema.Fields.Select(f => new
            {
                f.Name,
                f.Type,
                f.Description,
                f.Example,
                f.IsRequired,
                f.IsOptional
            }));

        var userContextInfo = !string.IsNullOrEmpty(personalizedContext)
            ? $"\n\nUser Profile:\n{personalizedContext}\n\nConsider user's frequent commands and preferences when deciding which fields to ask."
            : "";

        var prompt = $@"You are analyzing an API endpoint to determine which fields to ask the user and which to AUTO-FILL.

Endpoint: {endpoint.Description}
Intent: {endpoint.Intent}

Available Fields:
{fieldsJson}{userContextInfo}

IMPORTANT RULES:
1. Fields marked with 'sayp_required': true MUST be asked from the user - these are business-critical fields.
2. Fields marked with 'auto_generated': true should NEVER be asked - the system generates them automatically.
3. Fields marked with 'track_field': true are important identifiers but may be auto-generated.
4. Fields marked with 'navigation': true are entity references - ask for the name, system will resolve to ID.
5. If a field is 'Required' but is a CONFIGURATION setting (e.g. NumericLength, IncrementStep, Format), assume a default value.
6. Boolean fields should use sensible defaults (isActive: true, isBlocked: false, etc.)
7. DO NOT ask for optional fields unless they have 'sayp_required': true.
8. System fields like 'id', 'createdAt', 'updatedAt', 'createdById' are always auto-generated.

Respond with JSON:
{{
  ""fieldsToAsk"": [""FieldName1"", ""FieldName2""],
  ""defaults"": {{
    ""FieldName3"": true,
    ""FieldName4"": 0,
    ""FieldName5"": ""DefaultValue""
  }},
  ""reasoning"": ""Brief explanation""
}}";

        var response = await _aiProvider.GenerateResponseAsync(prompt, context: null, cancellationToken);
        
        try
        {
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(response, @"\{[\s\S]*\}");
            if (jsonMatch.Success)
            {
                var json = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(jsonMatch.Value);
                
                var result = new FieldAnalysisResult();

                // Parse fields to ask
                if (json.TryGetProperty("fieldsToAsk", out var fieldsProp))
                {
                    var fieldNames = fieldsProp.EnumerateArray()
                        .Select(e => e.GetString())
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToList();

                    result.FieldsToAsk = endpoint.Schema.Fields
                        .Where(f => fieldNames.Contains(f.Name, StringComparer.OrdinalIgnoreCase))
                        .ToList();
                }

                // Parse defaults
                if (json.TryGetProperty("defaults", out var defaultsProp))
                {
                    foreach (var property in defaultsProp.EnumerateObject())
                    {
                        result.AutoFilledDefaults[property.Name] = property.Value; // Store JsonElement, convert later
                    }
                }

                if (json.TryGetProperty("reasoning", out var reasoningProp))
                {
                    _logger.LogInformation("AI reasoning for fields: {Reasoning}", reasoningProp.GetString());
                }

                return result;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI field selection response");
        }

        return new FieldAnalysisResult();
    }

    private object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt32(out var i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => element.ToString()
        };
    }

    /// <summary>
    /// Update slots with new user input (supports multi-slot extraction)
    /// </summary>
    public async Task<DynamicSlotFillingResult> UpdateSlotsAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> currentSlots,
        string userInput,
        List<MissingSlot> missingSlots,
        CancellationToken cancellationToken = default)
    {
        // Strategy 1: AI Extraction for multiple slots
        if (_aiProvider != null && missingSlots.Count > 0)
        {
            try 
            {
                var extracted = await ExtractValuesWithAIAsync(userInput, missingSlots, cancellationToken);
                if (extracted.Any())
                {
                    foreach (var kvp in extracted)
                    {
                        currentSlots[kvp.Key] = kvp.Value;
                        _logger.LogInformation("✅ AI extracted value for {Field}: {Value}", kvp.Key, kvp.Value);
                    }
                    return await AnalyzeSlotsAsync(endpoint, currentSlots, null, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI extraction failed, falling back to simple extraction");
            }
        }

        // Strategy 2: Fallback (Simple mapping)
        if (missingSlots.Count == 1)
        {
            var extractedValue = ExtractValueFromInput(userInput, missingSlots[0]);
            if (extractedValue != null)
            {
                currentSlots[missingSlots[0].FieldName] = extractedValue;
            }
        }
        else
        {
            // If multiple slots and AI failed/missing, try splitting by comma
            var parts = userInput.Split(new[] { ',', '\n', ';' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < Math.Min(parts.Length, missingSlots.Count); i++)
            {
                var val = ExtractValueFromInput(parts[i], missingSlots[i]);
                if (val != null) currentSlots[missingSlots[i].FieldName] = val;
            }
        }

        return await AnalyzeSlotsAsync(endpoint, currentSlots, null, cancellationToken);
    }

    private async Task<Dictionary<string, object>> ExtractValuesWithAIAsync(
        string userInput, 
        List<MissingSlot> missingSlots,
        CancellationToken cancellationToken)
    {
        // ✅ IMPROVED: Simple cache key based on input hash (session-based caching would be better but more complex)
        var cacheKey = $"slot_extraction:{userInput.GetHashCode()}:{string.Join(",", missingSlots.Select(s => s.FieldName))}";
        
        // Note: For production, implement proper session-based cache with conversation ID
        // This is a simple optimization to avoid re-extracting identical inputs
        
        var slotsJson = JsonSerializer.Serialize(missingSlots.Select(s => new { s.FieldName, s.Description, s.Type, s.Example }));
        
        var prompt = $@"Extract values from user input for the following missing fields.

User Input: ""{userInput}""

Missing Fields:
{slotsJson}

Rules:
1. Map user input to the fields intelligently.
2. User might provide values separated by commas, spaces, or in natural language.
3. Convert values to appropriate types (int, bool, etc.).
4. If a value is not found, omit it.

Respond with JSON object:
{{
  ""extractedValues"": {{
    ""FieldName1"": ""Value1"",
    ""FieldName2"": 123
  }}
}}";

        var response = await _aiProvider!.GenerateResponseAsync(prompt, context: null, cancellationToken);
        var result = new Dictionary<string, object>();

        try
        {
            var jsonMatch = System.Text.RegularExpressions.Regex.Match(response, @"\{[\s\S]*\}");
            if (jsonMatch.Success)
            {
                var json = JsonSerializer.Deserialize<JsonElement>(jsonMatch.Value);
                if (json.TryGetProperty("extractedValues", out var values))
                {
                    foreach (var property in values.EnumerateObject())
                    {
                        result[property.Name] = ConvertJsonElement(property.Value);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI value extraction response");
        }

        return result;
    }

    private string GenerateNextQuestion(List<MissingSlot> slots, DiscoveredEndpoint endpoint)
    {
        if (slots.Count == 1) return GenerateSingleQuestion(slots[0]);

        var sb = new StringBuilder();
        sb.AppendLine("📝 **Lütfen aşağıdaki bilgileri cevaplayınız:**");
        sb.AppendLine("*(Tek bir mesajda, sırasıyla veya virgülle ayırarak yazabilirsiniz)*");
        sb.AppendLine();

        foreach (var slot in slots)
        {
            var name = TranslateFieldName(slot.FieldName);
            var desc = !string.IsNullOrEmpty(slot.Description) ? slot.Description : name;
            sb.AppendLine($"🔸 **{name}**: {desc}");
        }

        return sb.ToString();
    }

    private string GenerateSingleQuestion(MissingSlot slot)
    {
        // Build a friendly, conversational question
        var question = "📝 ";
        
        // Use Turkish field name translations for common fields
        var fieldNameTurkish = TranslateFieldName(slot.FieldName);
        
        if (!string.IsNullOrEmpty(slot.Description))
        {
            question += $"{slot.Description}";
        }
        else
        {
            question += $"{fieldNameTurkish} nedir?";
        }

        // Add example if available
        if (!string.IsNullOrEmpty(slot.Example))
        {
            question += $"\n\n💡 Örnek: {slot.Example}";
        }

        // Add type hint for clarity
        var typeHint = GetTypeHint(slot.Type);
        if (!string.IsNullOrEmpty(typeHint))
        {
            question += $"\n{typeHint}";
        }

        return question;
    }

    private string TranslateFieldName(string fieldName)
    {
        return fieldName.ToLower() switch
        {
            "name" => "İsim",
            "title" => "Başlık",
            "description" => "Açıklama",
            "price" => "Fiyat",
            "code" => "Kod",
            "template" => "Şablon",
            "content" => "İçerik",
            "date" => "Tarih",
            "time" => "Saat",
            "customer" => "Müşteri",
            "product" => "Ürün",
            "quantity" => "Miktar",
            "amount" => "Tutar",
            _ => fieldName
        };
    }

    private string GetTypeHint(string type)
    {
        return type.ToLower() switch
        {
            "int" or "integer" => "ℹ️ Sayı giriniz",
            "decimal" or "double" or "number" => "ℹ️ Sayı giriniz (örn: 99.99)",
            "date" or "datetime" => "ℹ️ Tarih giriniz (örn: 25 Kasım)",
            "bool" or "boolean" => "ℹ️ Evet veya Hayır",
            _ => ""
        };
    }

    private object? ExtractValueFromInput(string userInput, MissingSlot slot)
    {
        // Simple extraction - can be enhanced with AI
        var input = userInput.Trim();

        // Try to convert based on type
        try
        {
            return slot.Type.ToLower() switch
            {
                "string" => input,
                "int" or "integer" => int.Parse(input),
                "long" => long.Parse(input),
                "double" or "number" => double.Parse(input),
                "decimal" => decimal.Parse(input),
                "bool" or "boolean" => ParseBoolean(input),
                "datetime" or "date" => DateTime.Parse(input),
                "guid" => Guid.Parse(input),
                _ => input
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not convert input '{Input}' to type '{Type}'", input, slot.Type);
            return input; // Return as string if conversion fails
        }
    }

    private bool ParseBoolean(string input)
    {
        var lower = input.ToLowerInvariant();
        if (lower == "true" || lower == "evet" || lower == "yes" || lower == "y" || lower == "1" || lower == "onay")
            return true;
        if (lower == "false" || lower == "hayır" || lower == "no" || lower == "n" || lower == "0" || lower == "ret")
            return false;
        
        return bool.Parse(input);
    }

    private object SanitizeAndConvertValue(object value, SchemaField field)
    {
        if (value == null) return value!;

        try 
        {
            var strVal = value.ToString() ?? "";
            var type = field.Type.ToLower();

            if (type == "int" || type == "integer" || type == "long" || type == "short")
            {
                // Remove non-numeric chars (keep minus sign)
                var clean = new string(strVal.Where(c => char.IsDigit(c) || c == '-').ToArray());
                if (string.IsNullOrEmpty(clean)) return 0; 
                
                if (type == "long") return long.Parse(clean);
                return int.Parse(clean);
            }
            
            if (type == "double" || type == "decimal" || type == "float" || type == "number")
            {
                 // Keep digits, dot, comma, minus
                 var clean = new string(strVal.Where(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-').ToArray());
                 // Normalize comma to dot for parsing
                 clean = clean.Replace(",", ".");
                 if (string.IsNullOrEmpty(clean)) return 0.0;

                 return double.Parse(clean, System.Globalization.CultureInfo.InvariantCulture);
            }

            if (type == "bool" || type == "boolean")
            {
                return ParseBoolean(strVal);
            }
        }
        catch
        {
            // If cleanup fails, return original
            return value;
        }

        return value;
    }

    #region Validation

    /// <summary>
    /// Validate a value against slot rules
    /// </summary>
    public SlotValidationResult ValidateValue(object? value, MissingSlot slot, string? language = "tr")
    {
        var strVal = value?.ToString()?.Trim() ?? "";
        var isTurkish = language == "tr";

        // Required check
        if (slot.IsRequired && string.IsNullOrEmpty(strVal))
        {
            return SlotValidationResult.Error(
                $"❌ {TranslateFieldName(slot.FieldName)} alanı zorunludur.",
                $"❌ {slot.FieldName} is required.");
        }

        // If empty and not required, it's valid
        if (string.IsNullOrEmpty(strVal))
        {
            return SlotValidationResult.Success(null);
        }

        // Type-specific validation
        var type = slot.Type.ToLower();

        // String validations
        if (type == "string")
        {
            if (slot.MinLength.HasValue && strVal.Length < slot.MinLength.Value)
            {
                return SlotValidationResult.Error(
                    $"❌ {TranslateFieldName(slot.FieldName)} en az {slot.MinLength} karakter olmalıdır.",
                    $"❌ {slot.FieldName} must be at least {slot.MinLength} characters.");
            }

            if (slot.MaxLength.HasValue && strVal.Length > slot.MaxLength.Value)
            {
                return SlotValidationResult.Error(
                    $"❌ {TranslateFieldName(slot.FieldName)} en fazla {slot.MaxLength} karakter olabilir.",
                    $"❌ {slot.FieldName} must be at most {slot.MaxLength} characters.");
            }

            // Pattern validation (regex)
            if (!string.IsNullOrEmpty(slot.Pattern))
            {
                try
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(strVal, slot.Pattern))
                    {
                        var patternDesc = slot.PatternDescription ?? slot.Pattern;
                        return SlotValidationResult.Error(
                            $"❌ {TranslateFieldName(slot.FieldName)} geçersiz format. {patternDesc}",
                            $"❌ {slot.FieldName} has invalid format. {patternDesc}");
                    }
                }
                catch
                {
                    // Invalid regex, skip validation
                }
            }

            return SlotValidationResult.Success(strVal);
        }

        // Numeric validations
        if (type == "int" || type == "integer" || type == "long" || type == "double" || type == "decimal" || type == "number")
        {
            // Try to parse as number
            var clean = new string(strVal.Where(c => char.IsDigit(c) || c == '.' || c == ',' || c == '-').ToArray());
            clean = clean.Replace(",", ".");

            if (!double.TryParse(clean, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numValue))
            {
                return SlotValidationResult.Error(
                    $"❌ {TranslateFieldName(slot.FieldName)} geçerli bir sayı olmalıdır.",
                    $"❌ {slot.FieldName} must be a valid number.");
            }

            if (slot.MinValue.HasValue && numValue < slot.MinValue.Value)
            {
                return SlotValidationResult.Error(
                    $"❌ {TranslateFieldName(slot.FieldName)} en az {slot.MinValue} olmalıdır.",
                    $"❌ {slot.FieldName} must be at least {slot.MinValue}.");
            }

            if (slot.MaxValue.HasValue && numValue > slot.MaxValue.Value)
            {
                return SlotValidationResult.Error(
                    $"❌ {TranslateFieldName(slot.FieldName)} en fazla {slot.MaxValue} olabilir.",
                    $"❌ {slot.FieldName} must be at most {slot.MaxValue}.");
            }

            // Return converted value
            if (type == "int" || type == "integer")
                return SlotValidationResult.Success((int)numValue);
            if (type == "long")
                return SlotValidationResult.Success((long)numValue);
            if (type == "decimal")
                return SlotValidationResult.Success((decimal)numValue);
            
            return SlotValidationResult.Success(numValue);
        }

        // Phone number validation
        if (slot.FieldName.ToLower().Contains("phone") || slot.FieldName.ToLower().Contains("telefon"))
        {
            var phoneClean = new string(strVal.Where(c => char.IsDigit(c)).ToArray());
            
            if (phoneClean.Length < 10 || phoneClean.Length > 15)
            {
                return SlotValidationResult.Error(
                    "❌ Geçersiz telefon numarası. Lütfen geçerli bir numara girin (Örn: 05551234567)",
                    "❌ Invalid phone number. Please enter a valid number (e.g., 05551234567)");
            }

            return SlotValidationResult.Success(phoneClean);
        }

        // Email validation
        if (slot.FieldName.ToLower().Contains("email") || slot.FieldName.ToLower().Contains("eposta"))
        {
            var emailPattern = @"^[^@\s]+@[^@\s]+\.[^@\s]+$";
            if (!System.Text.RegularExpressions.Regex.IsMatch(strVal, emailPattern))
            {
                return SlotValidationResult.Error(
                    "❌ Geçersiz e-posta adresi. Lütfen geçerli bir e-posta girin (Örn: ornek@email.com)",
                    "❌ Invalid email address. Please enter a valid email (e.g., example@email.com)");
            }

            return SlotValidationResult.Success(strVal.ToLower());
        }

        // Allowed values (enum) validation
        if (slot.AllowedValues != null && slot.AllowedValues.Length > 0)
        {
            var matched = slot.AllowedValues.FirstOrDefault(v => 
                v.Equals(strVal, StringComparison.OrdinalIgnoreCase));

            if (matched == null)
            {
                var options = string.Join(", ", slot.AllowedValues);
                return SlotValidationResult.Error(
                    $"❌ Geçersiz değer. İzin verilen değerler: {options}",
                    $"❌ Invalid value. Allowed values: {options}");
            }

            return SlotValidationResult.Success(matched);
        }

        // Date validation
        if (type == "date" || type == "datetime")
        {
            if (!TryParseDate(strVal, out var dateValue))
            {
                return SlotValidationResult.Error(
                    "❌ Geçersiz tarih formatı. Lütfen geçerli bir tarih girin (Örn: 25 Aralık 2024 veya 25/12/2024)",
                    "❌ Invalid date format. Please enter a valid date (e.g., December 25, 2024 or 25/12/2024)");
            }

            return SlotValidationResult.Success(dateValue);
        }

        // Default: return as-is
        return SlotValidationResult.Success(value);
    }

    /// <summary>
    /// Try to parse various date formats including Turkish natural language
    /// </summary>
    private bool TryParseDate(string input, out DateTime result)
    {
        result = DateTime.MinValue;
        var lower = input.ToLower().Trim();

        // Turkish natural language dates
        var turkeyTime = TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, turkeyTime);

        if (lower == "bugün" || lower == "today")
        {
            result = now.Date;
            return true;
        }
        if (lower == "yarın" || lower == "tomorrow")
        {
            result = now.Date.AddDays(1);
            return true;
        }
        if (lower == "dün" || lower == "yesterday")
        {
            result = now.Date.AddDays(-1);
            return true;
        }

        // Turkish month names
        var turkishMonths = new Dictionary<string, int>
        {
            { "ocak", 1 }, { "şubat", 2 }, { "mart", 3 }, { "nisan", 4 },
            { "mayıs", 5 }, { "haziran", 6 }, { "temmuz", 7 }, { "ağustos", 8 },
            { "eylül", 9 }, { "ekim", 10 }, { "kasım", 11 }, { "aralık", 12 }
        };

        // Try "25 Aralık" or "25 Aralık 2024" format
        foreach (var month in turkishMonths)
        {
            if (lower.Contains(month.Key))
            {
                var parts = lower.Replace(month.Key, "|").Split('|');
                if (parts.Length >= 1)
                {
                    var dayStr = new string(parts[0].Where(char.IsDigit).ToArray());
                    if (int.TryParse(dayStr, out var day))
                    {
                        var year = now.Year;
                        if (parts.Length > 1)
                        {
                            var yearStr = new string(parts[1].Where(char.IsDigit).ToArray());
                            if (yearStr.Length == 4 && int.TryParse(yearStr, out var parsedYear))
                            {
                                year = parsedYear;
                            }
                        }
                        
                        try
                        {
                            result = new DateTime(year, month.Value, day);
                            return true;
                        }
                        catch { }
                    }
                }
            }
        }

        // Try standard formats
        var formats = new[]
        {
            "dd/MM/yyyy", "dd.MM.yyyy", "dd-MM-yyyy",
            "yyyy-MM-dd", "yyyy/MM/dd",
            "dd/MM/yyyy HH:mm", "dd.MM.yyyy HH:mm",
            "yyyy-MM-dd HH:mm", "yyyy-MM-ddTHH:mm:ss"
        };

        foreach (var format in formats)
        {
            if (DateTime.TryParseExact(input, format, System.Globalization.CultureInfo.InvariantCulture, 
                System.Globalization.DateTimeStyles.None, out result))
            {
                return true;
            }
        }

        // Try general parsing
        return DateTime.TryParse(input, out result);
    }

    #endregion
}

/// <summary>
/// Result of dynamic slot filling analysis
/// </summary>
public class DynamicSlotFillingResult
{
    public bool IsComplete { get; set; }
    public Dictionary<string, object> FilledSlots { get; set; } = new();
    public List<MissingSlot> MissingSlots { get; set; } = new();
    public string? NextQuestion { get; set; }
}

/// <summary>
/// Represents a missing parameter that needs to be filled
/// </summary>
public class MissingSlot
{
    public string FieldName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public string Example { get; set; } = string.Empty;
    public string[] Aliases { get; set; } = Array.Empty<string>();
    
    // ✅ NEW: Validation rules from backend schema
    public bool IsRequired { get; set; } = false;
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public double? MinValue { get; set; }
    public double? MaxValue { get; set; }
    public string? Pattern { get; set; } // Regex pattern
    public string? PatternDescription { get; set; } // Human-readable pattern description
    public string[]? AllowedValues { get; set; } // Enum values
}

/// <summary>
/// Result of field analysis including fields to ask and default values
/// </summary>
public class FieldAnalysisResult
{
    public List<SchemaField> FieldsToAsk { get; set; } = new();
    public Dictionary<string, object> AutoFilledDefaults { get; set; } = new();
}

/// <summary>
/// Result of slot value validation
/// </summary>
public class SlotValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorMessageTurkish { get; set; }
    public string? ErrorMessageEnglish { get; set; }
    public object? SanitizedValue { get; set; }
    
    public static SlotValidationResult Success(object? value = null) => new() { IsValid = true, SanitizedValue = value };
    public static SlotValidationResult Error(string messageTr, string messageEn) => new() 
    { 
        IsValid = false, 
        ErrorMessageTurkish = messageTr, 
        ErrorMessageEnglish = messageEn,
        ErrorMessage = messageTr // Default to Turkish
    };
}
