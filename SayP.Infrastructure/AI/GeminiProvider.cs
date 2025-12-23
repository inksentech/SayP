using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SayP.Domain.Interfaces;
using System.Text;
using System.Text.Json;

namespace SayP.Infrastructure.AI;

public class GeminiProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<GeminiProvider> _logger;
    private readonly string _apiKey;
    private readonly string _model;

    public GeminiProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<GeminiProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _apiKey = configuration["AI:Gemini:ApiKey"] 
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY") 
            ?? throw new InvalidOperationException("Gemini API Key not configured");
        
        _model = configuration["AI:Gemini:Model"] 
            ?? Environment.GetEnvironmentVariable("GEMINI_MODEL") 
            ?? "gemini-1.5-flash";
    }

    /// <summary>
    /// ⚠️ DEPRECATED: Use DynamicIntentMapper instead.
    /// This method uses hard-coded command types and is not dynamic.
    /// </summary>
    [Obsolete("Use DynamicIntentMapper for dynamic intent mapping. This will be removed in v3.0.0")]
    public async Task<AICommandResult> ExtractCommandAsync(
        string message,
        string? conversationContext = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            // Build context-aware prompt
            var systemPrompt = GetCommandExtractionPrompt();
            var fullPrompt = BuildContextAwarePrompt(systemPrompt, message, conversationContext);

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = fullPrompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 4096,
                    responseModalities = new[] { "TEXT" }
                }
            };

            var content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("Gemini API raw response (Status: {StatusCode}): {Response}", 
                response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error: {StatusCode} - {Response}", response.StatusCode, responseBody);
                return new AICommandResult
                {
                    Success = false,
                    ErrorMessage = $"Gemini API error: {response.StatusCode}"
                };
            }

            var result = JsonConvert.DeserializeObject<GeminiResponse>(responseBody);
            
            _logger.LogInformation("Gemini parsed - Candidates count: {Count}", result?.Candidates?.Count ?? 0);
            
            var generatedText = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "";

            if (string.IsNullOrEmpty(generatedText))
            {
                _logger.LogWarning("Gemini returned empty text. Full response: {Response}", responseBody);
                return new AICommandResult
                {
                    Success = false,
                    ErrorMessage = "Gemini returned empty response"
                };
            }

            _logger.LogInformation("Gemini response: {Response}", generatedText);

            // Parse the AI response to extract command
            return ParseCommandFromResponse(generatedText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Gemini API");
            return new AICommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// ✅ RECOMMENDED: Generic AI response generation.
    /// Use this for all AI-powered text generation needs.
    /// </summary>
    public async Task<string> GenerateResponseAsync(
        string prompt,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        return await GenerateResponseAsync(prompt, context, temperature: 0.7, maxTokens: 4096, cancellationToken);
    }

    /// <summary>
    /// ✅ RECOMMENDED: Generic AI response generation with custom parameters.
    /// </summary>
    public async Task<string> GenerateResponseAsync(
        string prompt,
        string? context = null,
        double temperature = 0.7,
        int maxTokens = 4096,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            var fullPrompt = context != null 
                ? $"{context}\n\n{prompt}" 
                : prompt;

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = fullPrompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = temperature,
                    maxOutputTokens = maxTokens,
                    responseModalities = new[] { "TEXT" }
                }
            };

            var content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error: {StatusCode} - {Response}", response.StatusCode, responseBody);
                return "Üzgünüm, şu an yanıt veremiyorum.";
            }

            var result = JsonConvert.DeserializeObject<GeminiResponse>(responseBody);
            var generatedText = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "";

            return generatedText;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Gemini API");
            return "Üzgünüm, bir hata oluştu.";
        }
    }

    private AICommandResult ParseCommandFromResponse(string response)
    {
        try
        {
            // Remove markdown code blocks if present
            var cleanResponse = response.Trim();
            if (cleanResponse.StartsWith("```json"))
            {
                cleanResponse = cleanResponse.Substring(7); // Remove ```json
            }
            if (cleanResponse.StartsWith("```"))
            {
                cleanResponse = cleanResponse.Substring(3); // Remove ```
            }
            if (cleanResponse.EndsWith("```"))
            {
                cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3); // Remove ```
            }
            cleanResponse = cleanResponse.Trim();

            // Try to parse JSON response
            var jsonDoc = JsonDocument.Parse(cleanResponse);
            var root = jsonDoc.RootElement;

            var commandResult = new AICommandResult
            {
                Success = true,
                CommandJson = cleanResponse,
                CommandType = root.TryGetProperty("commandType", out var cmdType) ? cmdType.GetString() : null,
                ConfirmationMessage = root.TryGetProperty("confirmationMessage", out var confMsg) ? confMsg.GetString() : 
                                     root.TryGetProperty("response", out var respMsg) ? respMsg.GetString() : null,
                ErrorMessage = root.TryGetProperty("errorMessage", out var errMsg) ? errMsg.GetString() : null,
                RequiresConfirmation = root.TryGetProperty("requiresConfirmation", out var reqConf) && reqConf.GetBoolean(),
                Confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.0
            };
            
            return commandResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing Gemini response");
            return new AICommandResult
            {
                Success = true,
                CommandType = Domain.Enums.CommandType.Unknown.ToString(),
                CommandJson = "{\"commandType\": \"None\", \"response\": \"Üzgünüm, bir hata oluştu.\"}"
            };
        }
    }

    // Gemini API response models
    private class GeminiResponse
    {
        [JsonProperty("candidates")]
        public List<Candidate>? Candidates { get; set; }
    }

    private class Candidate
    {
        [JsonProperty("content")]
        public Content? Content { get; set; }
    }

    private class Content
    {
        [JsonProperty("parts")]
        public List<Part>? Parts { get; set; }
    }

    private class Part
    {
        [JsonProperty("text")]
        public string? Text { get; set; }
    }

    /// <summary>
    /// Extract command from image
    /// </summary>
    public async Task<AICommandResult> ExtractCommandFromImageAsync(
        byte[] imageData,
        string mimeType,
        string? textPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var mediaParts = new List<MediaPart>
        {
            new MediaPart
            {
                Type = MediaPartType.Image,
                Data = imageData,
                MimeType = mimeType
            }
        };

        if (!string.IsNullOrEmpty(textPrompt))
        {
            mediaParts.Add(new MediaPart
            {
                Type = MediaPartType.Text,
                Text = textPrompt
            });
        }

        return await ExtractCommandFromMultimodalAsync(mediaParts, textPrompt, cancellationToken);
    }

    /// <summary>
    /// Extract command from audio
    /// </summary>
    public async Task<AICommandResult> ExtractCommandFromAudioAsync(
        byte[] audioData,
        string mimeType,
        string? textPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var mediaParts = new List<MediaPart>
        {
            new MediaPart
            {
                Type = MediaPartType.Audio,
                Data = audioData,
                MimeType = mimeType
            }
        };

        if (!string.IsNullOrEmpty(textPrompt))
        {
            mediaParts.Add(new MediaPart
            {
                Type = MediaPartType.Text,
                Text = textPrompt
            });
        }

        return await ExtractCommandFromMultimodalAsync(mediaParts, textPrompt, cancellationToken);
    }

    /// <summary>
    /// Extract command from multiple media (images, audio, text)
    /// </summary>
    public async Task<AICommandResult> ExtractCommandFromMultimodalAsync(
        List<MediaPart> mediaParts,
        string? textPrompt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            // Build parts array for Gemini API
            var parts = new List<object>();

            // Add system prompt first
            parts.Add(new { text = GetCommandExtractionPrompt() });

            // Add media parts
            foreach (var mediaPart in mediaParts)
            {
                if (mediaPart.Type == MediaPartType.Text && !string.IsNullOrEmpty(mediaPart.Text))
                {
                    parts.Add(new { text = mediaPart.Text });
                }
                else if (mediaPart.Type == MediaPartType.Image && mediaPart.Data != null)
                {
                    parts.Add(new
                    {
                        inline_data = new
                        {
                            mime_type = mediaPart.MimeType ?? "image/jpeg",
                            data = Convert.ToBase64String(mediaPart.Data)
                        }
                    });
                }
                else if (mediaPart.Type == MediaPartType.Audio && mediaPart.Data != null)
                {
                    parts.Add(new
                    {
                        inline_data = new
                        {
                            mime_type = mediaPart.MimeType ?? "audio/mp3",
                            data = Convert.ToBase64String(mediaPart.Data)
                        }
                    });
                }
            }

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = parts.ToArray()
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    maxOutputTokens = 4096,
                    responseModalities = new[] { "TEXT" }
                }
            };

            var content = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8,
                "application/json"
            );

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("Gemini multimodal API response (Status: {StatusCode}): {Response}",
                response.StatusCode, responseBody);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error: {StatusCode} - {Response}", response.StatusCode, responseBody);
                return new AICommandResult
                {
                    Success = false,
                    ErrorMessage = $"Gemini API error: {response.StatusCode}"
                };
            }

            var result = JsonConvert.DeserializeObject<GeminiResponse>(responseBody);
            var generatedText = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? "";

            if (string.IsNullOrEmpty(generatedText))
            {
                _logger.LogWarning("Gemini returned empty text for multimodal request");
                return new AICommandResult
                {
                    Success = false,
                    ErrorMessage = "Gemini returned empty response"
                };
            }

            _logger.LogInformation("Gemini multimodal response: {Response}", generatedText);

            // Parse the AI response to extract command
            return ParseCommandFromResponse(generatedText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling Gemini multimodal API");
            return new AICommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Build context-aware prompt with conversation history
    /// </summary>
    private string BuildContextAwarePrompt(string systemPrompt, string currentMessage, string? conversationContext)
    {
        var promptBuilder = new System.Text.StringBuilder();
        
        promptBuilder.AppendLine(systemPrompt);
        promptBuilder.AppendLine();
        
        // Add conversation context if available
        if (!string.IsNullOrEmpty(conversationContext))
        {
            promptBuilder.AppendLine("📜 CONVERSATION HISTORY:");
            promptBuilder.AppendLine(conversationContext);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("⚠️ IMPORTANT: Consider the conversation history above!");
            promptBuilder.AppendLine("- Remember product/customer names mentioned in previous messages");
            promptBuilder.AppendLine("- Use history to complete missing information");
            promptBuilder.AppendLine("- Resolve ambiguous references (\"it\", \"that product\") from history");
            promptBuilder.AppendLine();
        }
        
        promptBuilder.AppendLine("👤 USER MESSAGE:");
        promptBuilder.AppendLine(currentMessage);
        
        return promptBuilder.ToString();
    }

    private string GetCommandExtractionPrompt()
    {
        return @"You are an advanced multilingual business assistant. Analyze user messages and determine the most appropriate intent.

📋 AVAILABLE INTENTS:
• CreateProduct - Create product (TR: ""Çiçek ürünü ekle 50 TL"", EN: ""Add flower product 50 TL"", ""Create laptop 15000 TL"") - Auto-generates code
• UpdateProduct - Update product (TR: ""PRD-000001 fiyatını 16000 TL yap"", EN: ""Update PRD-000001 price to 16000 TL"")
• DeleteProduct - Delete product (TR: ""PRD-000001'i sil"", EN: ""Delete PRD-000001"")
• ListProducts - List products (TR: ""Ürünleri göster"", EN: ""Show products"", ""List all products"")
• GetProduct - Product details (TR: ""PRD-000001 detayları"", EN: ""PRD-000001 details"")
• GetProductByCode - Find by code (TR: ""PRD-000001 nedir"", EN: ""What is PRD-000001"")
• GetProductsByCodes - Multiple products (TR: ""PRD-000001, PRD-000002 kodlu ürünler"", EN: ""Products PRD-000001, PRD-000002"")
• GetTodaysProducts - Today's products (TR: ""Bugün oluşturulan ürünler"", EN: ""Today's products"", ""Products created today"")
• CreateCustomer - Add customer (TR: ""Ahmet Yılmaz müşteri ekle"", EN: ""Add customer John Doe"")
• ListCustomers - List customers (TR: ""Müşterileri göster"", EN: ""Show customers"", ""List all customers"")
• CreateCodeTemplate - Create code template (TR: ""Şablon oluştur"", ""Kod şablonu ekle"", ""Template yap"", EN: ""Create template"", ""Add code template"")
• CreateAppointment - Create appointment (TR: ""Ahmet için yarın 14:30'da randevu oluştur"", EN: ""Create appointment for John tomorrow at 14:30"")
• ListAppointments - List appointments (TR: ""Yarınki randevuları listele"", ""Bugünkü randevular"", EN: ""List tomorrow's appointments"", ""Today's appointments"")
• CreateInvoice - Create invoice (TR: ""Ahmet'e fatura kes"", EN: ""Create invoice for John"")
• ListInvoices - List invoices (TR: ""Faturaları göster"", EN: ""Show invoices"", ""List all invoices"")
• None - General chat (TR: ""Merhaba"", EN: ""Hello"", ""Hi"", ""Thank you"")

🎯 ANALYSIS STEPS:
1. Identify keywords in the message
2. Extract numerical values (price, quantity, stock)
3. Detect names and identifiers
4. Recognize product codes (PRD-XXXXXX format)
5. Evaluate context
6. Select the most appropriate intent

🏷️ PRODUCT CODE SYSTEM:
- Each product gets an auto-generated code (e.g., PRD-000001)
- Users can use either code or name
- Code-based operations are faster
- Code should be shown when product is created

📝 JSON OUTPUT FORMAT:
{
  ""commandType"": ""CreateProduct"",
  ""confidence"": 0.95,
  ""command"": {
    ""name"": ""Çiçek"",
    ""price"": 50,
    ""description"": ""Güzel çiçek""
  },
  ""response"": ""✅ Çiçek ürünü oluşturuldu!\n🏷️ Ürün Kodu: PRD-000001\n💰 Fiyat: 50 TL"",
  ""requiresConfirmation"": false
}

For general chat:
{
  ""commandType"": ""None"",
  ""confidence"": 0.9,
  ""response"": ""Merhaba! Size nasıl yardımcı olabilirim? Ürün, müşteri veya fatura işlemleri yapabilirim.""
}

🔍 KEY PATTERNS:
- TR: ""listele"", ""göster"", ""tüm"", ""bütün"" / EN: ""list"", ""show"", ""all"" → ListProducts/ListCustomers/ListInvoices
- TR: ""oluştur"", ""ekle"", ""yeni"" / EN: ""create"", ""add"", ""new"" + product name + price → CreateProduct
- TR: ""güncelle"", ""değiştir"" / EN: ""update"", ""change"" + code/name → UpdateProduct
- If uncertain, ask question
- If missing info, specify
- Respond in user's language (Turkish or English)
- Set realistic confidence value";
    }

    /// <summary>
    /// ⚠️ DEPRECATED: Use DynamicSlotFiller instead.
    /// This method uses hard-coded entity schemas and is not dynamic.
    /// </summary>
    [Obsolete("Use DynamicSlotFiller for dynamic entity extraction. This will be removed in v3.0.0")]
    public async Task<Dictionary<string, object>> ExtractEntitiesAsync(
        string message,
        string commandType,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{_model}:generateContent?key={_apiKey}";

            var prompt = GetEntityExtractionPrompt(commandType, message);

            var payload = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                }
            };

            var jsonPayload = System.Text.Json.JsonSerializer.Serialize(payload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Gemini API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return new Dictionary<string, object>();
            }

            var jsonResponse = JsonDocument.Parse(responseContent);
            var textResponse = jsonResponse.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            if (string.IsNullOrEmpty(textResponse))
            {
                _logger.LogWarning("Empty response from Gemini for entity extraction");
                return new Dictionary<string, object>();
            }

            // Parse JSON response - clean markdown if present
            var cleanJson = textResponse.Trim();
            if (cleanJson.StartsWith("```json"))
                cleanJson = cleanJson.Substring(7);
            if (cleanJson.StartsWith("```"))
                cleanJson = cleanJson.Substring(3);
            if (cleanJson.EndsWith("```"))
                cleanJson = cleanJson.Substring(0, cleanJson.Length - 3);
            cleanJson = cleanJson.Trim();

            var entities = JsonConvert.DeserializeObject<Dictionary<string, object>>(cleanJson) 
                ?? new Dictionary<string, object>();

            // 🔧 POST-PROCESSING: Gemini 2.5 Flash modeli prompt'u görmezden aldığı için zorunlu filtre
            if (commandType == "ListAppointments" && entities.ContainsKey("serviceName"))
            {
                var serviceName = entities["serviceName"]?.ToString()?.Trim();
                
                if (!string.IsNullOrEmpty(serviceName))
                {
                    // Yasaklı kelimeler - bunlar hizmet adı DEĞİL! (Türkçe + İngilizce)
                    var bannedWords = new[] { 
                        // Türkçe
                        "yarınki", "bugünkü", "dünkü", 
                        "tüm", "bütün", "hepsi", "hepsini",
                        "randevular", "randevu", "randevuları",
                        "listele", "göster", "bul",
                        // İngilizce
                        "tomorrow", "tomorrow's", "today", "today's", "yesterday", "yesterday's",
                        "all", "every", "entire",
                        "appointments", "appointment", "appointments'",
                        "list", "show", "find", "get"
                    };
                    
                    var lowerServiceName = serviceName.ToLowerInvariant();
                    
                    // Eğer SADECE yasaklı kelimelerden oluşuyorsa null yap
                    var words = lowerServiceName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    var allBanned = words.All(word => bannedWords.Contains(word));
                    
                    if (allBanned)
                    {
                        _logger.LogWarning("⚠️ AI incorrectly extracted serviceName='{ServiceName}' (all banned words), setting to null", serviceName);
                        entities["serviceName"] = null;
                    }
                }
            }

            _logger.LogInformation("AI extracted {Count} entities for command type {CommandType}", entities.Count, commandType);
            return entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting entities with AI");
            return new Dictionary<string, object>();
        }
    }

    private string GetEntityExtractionPrompt(string commandType, string message)
    {
        var schema = GetEntitySchemaForCommand(commandType);
        var prompt = $@"You are an entity extraction expert. Extract entities from user messages in Turkish or English.

📝 MESSAGE: ""{message}""
🎯 COMMAND TYPE: {commandType}

{schema}

🔍 RULES:
1. Extract ALL information from the message
2. Leave null if information is missing
3. Names: Full name (e.g., ""Ahmet Yılmaz"", ""John Doe"")
4. Phone: 10-11 digit number (e.g., ""05551234567"")
5. Date: YYYY-MM-DD format
6. Time: HH:mm format (e.g., ""14:30"")
7. Price/Amount: Number only (e.g., 150.50)
8. Tax rate: Decimal (e.g., 0.20 = 20%)

✅ RETURN ONLY JSON, NO EXPLANATIONS!

Example:
{{
  ""name"": ""Ahmet Yılmaz"",
  ""phone"": ""05551234567"",
  ""date"": ""2025-11-02"",
  ""time"": ""14:30""
}}";
        
        _logger.LogInformation("🔍 PROMPT for {CommandType}:\n{Prompt}", commandType, prompt);
        return prompt;
    }

    private string GetEntitySchemaForCommand(string commandType)
    {
        return commandType switch
        {
            "CreateCustomer" => @"
📋 ENTITIES TO EXTRACT:
- name: Customer full name (string)
- phone: Phone number (string)
- email: Email (string, optional)
- address: Address (string, optional)
- notes: Notes (string, optional)",

            "CreateCodeTemplate" => @"
📋 ENTITIES TO EXTRACT:
- name: Template name (string)
- entityType: Entity type (string, e.g., ""Product"", ""Invoice"", ""Customer"")
- prefix: Code prefix (string, optional)
- suffix: Code suffix (string, optional)
- numericLength: Numeric part length (int, optional, default: 6)
- includeDate: Include date in code (bool, optional, default: false)
- notes: Notes (string, optional)",

            "CreateAppointment" => @"
📋 ENTITIES TO EXTRACT:
- customerName: Customer full name (string)
- serviceName: Service name (string)
- date: Appointment date (string, YYYY-MM-DD) - Convert expressions like ""tomorrow"", ""today"" to date
- time: Appointment time (string, HH:mm)
- durationMinutes: Duration in minutes (int, optional)
- notes: Notes (string, optional)

⚠️ IMPORTANT DATE CONVERSION - Turkey Time (UTC+3):
Current: " + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time")).ToString("yyyy-MM-dd HH:mm") + @"
- TR: ""bugün"" / EN: ""today"" → " + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time")).ToString("yyyy-MM-dd") + @"
- TR: ""yarın"" / EN: ""tomorrow"" → " + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time")).AddDays(1).ToString("yyyy-MM-dd") + @"",

            "CreateProduct" => @"
📋 ENTITIES TO EXTRACT:
- name: Product/Service name (string)
- price: Price (decimal)
- description: Description (string, optional)
- stock: Stock quantity (int, optional)
- productType: Type (""Product"" or ""Service"", optional)",

            "CreateInvoice" => @"
📋 ENTITIES TO EXTRACT:
- customerName: Customer name (string)
- items: Product/service list (array)
- totalAmount: Total amount (decimal, optional)
- taxRate: Tax rate (decimal, optional)
- notes: Notes (string, optional)",

            "ListAppointments" => @"
LOOK AT EXAMPLES FIRST:

❌ WRONG:
TR: ""Yarınki tüm randevuları listele"" → {""date"":""2025-11-03"", ""serviceName"":""Yarınki tüm""} ← WRONG!
TR: ""Bugünkü randevular"" → {""date"":""2025-11-02"", ""serviceName"":""bugünkü""} ← WRONG!
EN: ""List all appointments for tomorrow"" → {""date"":""2025-11-03"", ""serviceName"":""all appointments""} ← WRONG!
EN: ""Today's appointments"" → {""date"":""2025-11-02"", ""serviceName"":""today's""} ← WRONG!

✅ CORRECT:
TR: ""Yarınki tüm randevuları listele"" → {""date"":""2025-11-03"", ""serviceName"":null}
TR: ""Bugünkü randevular"" → {""date"":""2025-11-02"", ""serviceName"":null}
TR: ""Yarın Saç Kesimi randevuları"" → {""date"":""2025-11-03"", ""serviceName"":""Saç Kesimi""}
EN: ""List all appointments for tomorrow"" → {""date"":""2025-11-03"", ""serviceName"":null}
EN: ""Today's appointments"" → {""date"":""2025-11-02"", ""serviceName"":null}
EN: ""Tomorrow's haircut appointments"" → {""date"":""2025-11-03"", ""serviceName"":""Haircut""}

RULE: serviceName ONLY for real service names! Words like TR: ""yarınki"", ""bugünkü"", ""tüm"" / EN: ""tomorrow"", ""today"", ""all"" are NOT serviceName!

Fields to extract:
- date: Date (YYYY-MM-DD) - today=" + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time")).ToString("yyyy-MM-dd") + @", tomorrow=" + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time")).AddDays(1).ToString("yyyy-MM-dd") + @"
- serviceName: Service name (string or null) - ONLY real service names
- customerName: Customer name (string or null)
- status: Status (string or null)",

            "CheckAvailability" => @"
📋 ENTITIES TO EXTRACT:
- date: Date (string, YYYY-MM-DD) - Convert expressions like ""tomorrow"", ""today"" to date
- time: Time (string, HH:mm)
- serviceName: Service name (string, optional)
- serviceId: Service ID (string, optional)

⚠️ IMPORTANT DATE CONVERSION - Turkey Time (UTC+3):
Current: " + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time")).ToString("yyyy-MM-dd HH:mm") + @"
- TR: ""bugün"" / EN: ""today"" → " + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time")).ToString("yyyy-MM-dd") + @"
- TR: ""yarın"" / EN: ""tomorrow"" → " + TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time")).AddDays(1).ToString("yyyy-MM-dd") + @"",

            "CancelAppointment" => @"
📋 ENTITIES TO EXTRACT:
- appointmentId: Appointment ID (string, optional)
- date: Date (string, YYYY-MM-DD, optional)
- startTime: Start time (string, HH:mm, optional)
- endTime: End time (string, HH:mm, optional)
- reason: Cancellation reason (string, optional)",

            "ListCustomers" => @"
📋 ENTITIES TO EXTRACT:
- name: Customer name (string, optional - for search)
- phone: Phone (string, optional - for search)
- email: Email (string, optional - for search)",

            "ListProducts" => @"
📋 ENTITIES TO EXTRACT:
- name: Product/Service name (string, optional - for search)
- productType: Type (""Product"" or ""Service"", optional)
- minPrice: Minimum price (decimal, optional)
- maxPrice: Maximum price (decimal, optional)",

            _ => @"
📋 ENTITIES TO EXTRACT:
- Extract all important information from the message
- Return as key-value pairs"
        };
    }

    // ✅ NEW: Dynamic helper methods for modern usage

    /// <summary>
    /// Generate JSON response from AI with automatic parsing
    /// </summary>
    public async Task<T?> GenerateJsonResponseAsync<T>(
        string prompt,
        string? context = null,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var jsonPrompt = $@"{prompt}

IMPORTANT: Respond with ONLY valid JSON. No explanations, no markdown code blocks.";

            var response = await GenerateResponseAsync(jsonPrompt, context, cancellationToken);
            
            // Clean response (remove markdown if present)
            var cleanResponse = response.Trim();
            if (cleanResponse.StartsWith("```json"))
                cleanResponse = cleanResponse.Substring(7);
            if (cleanResponse.StartsWith("```"))
                cleanResponse = cleanResponse.Substring(3);
            if (cleanResponse.EndsWith("```"))
                cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
            cleanResponse = cleanResponse.Trim();

            return JsonConvert.DeserializeObject<T>(cleanResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating JSON response");
            return null;
        }
    }

    /// <summary>
    /// Generate response with retry logic for reliability
    /// </summary>
    public async Task<string> GenerateResponseWithRetryAsync(
        string prompt,
        string? context = null,
        int maxRetries = 3,
        CancellationToken cancellationToken = default)
    {
        Exception? lastException = null;
        
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                return await GenerateResponseAsync(prompt, context, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "AI generation failed (attempt {Attempt}/{MaxRetries})", i + 1, maxRetries);
                
                if (i < maxRetries - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i)), cancellationToken);
                }
            }
        }
        
        _logger.LogError(lastException, "AI generation failed after {MaxRetries} attempts", maxRetries);
        throw lastException ?? new Exception("AI generation failed");
    }

    /// <summary>
    /// Generate multiple responses in parallel for comparison
    /// </summary>
    public async Task<List<string>> GenerateMultipleResponsesAsync(
        string prompt,
        string? context = null,
        int count = 3,
        double temperature = 0.9,
        CancellationToken cancellationToken = default)
    {
        var tasks = Enumerable.Range(0, count)
            .Select(_ => GenerateResponseAsync(prompt, context, temperature, 4096, cancellationToken))
            .ToList();

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    /// <summary>
    /// Stream response for long-running generations (future enhancement)
    /// </summary>
    public async IAsyncEnumerable<string> StreamResponseAsync(
        string prompt,
        string? context = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Note: Gemini API supports streaming via streamGenerateContent endpoint
        // This is a placeholder for future implementation
        var response = await GenerateResponseAsync(prompt, context, cancellationToken);
        
        // Simulate streaming by yielding chunks
        var words = response.Split(' ');
        foreach (var word in words)
        {
            yield return word + " ";
            await Task.Delay(50, cancellationToken); // Simulate streaming delay
        }
    }
}

