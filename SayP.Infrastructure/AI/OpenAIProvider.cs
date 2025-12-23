using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using SayP.Domain.Interfaces;
using System.Net.Http.Headers;
using System.Text;

namespace SayP.Infrastructure.AI;

/// <summary>
/// OpenAI/ChatGPT provider for AI command extraction
/// Supports text, image (GPT-4 Vision), and audio (Whisper)
/// </summary>
public class OpenAIProvider : IAIProvider
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OpenAIProvider> _logger;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _whisperModel;

    public OpenAIProvider(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<OpenAIProvider> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;

        _apiKey = configuration["AI:OpenAI:ApiKey"] 
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") 
            ?? throw new InvalidOperationException("OpenAI API Key not configured");
        
        _model = configuration["AI:OpenAI:Model"] 
            ?? Environment.GetEnvironmentVariable("OPENAI_MODEL") 
            ?? "gpt-4o"; // GPT-4 Omni supports vision
        
        _whisperModel = configuration["AI:OpenAI:WhisperModel"] 
            ?? Environment.GetEnvironmentVariable("OPENAI_WHISPER_MODEL") 
            ?? "whisper-1";

        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
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
            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = message // This contains the full prompt with schemas
                }
            };

            var payload = new
            {
                model = _model,
                messages,
                temperature = 0.7,
                max_tokens = 1000,
                response_format = new { type = "json_object" }
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                content,
                cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return new AICommandResult
                {
                    Success = false,
                    ErrorMessage = $"OpenAI API error: {response.StatusCode}"
                };
            }

            dynamic result = JsonConvert.DeserializeObject(responseContent)!;
            string commandJson = result.choices[0].message.content.ToString();

            _logger.LogInformation("OpenAI command extraction successful");

            return new AICommandResult
            {
                Success = true,
                CommandJson = commandJson
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting command with OpenAI");
            return new AICommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<string> GenerateResponseAsync(
        string prompt,
        string? context = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<object>();

            if (!string.IsNullOrEmpty(context))
            {
                messages.Add(new { role = "system", content = context });
            }

            messages.Add(new { role = "user", content = prompt });

            var payload = new
            {
                model = _model,
                messages,
                temperature = 0.7,
                max_tokens = 500
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                content,
                cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return "Üzgünüm, şu anda bir hata oluştu. Lütfen daha sonra tekrar deneyin.";
            }

            dynamic result = JsonConvert.DeserializeObject(responseContent)!;
            string generatedResponse = result.choices[0].message.content.ToString();

            return generatedResponse;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating response with OpenAI");
            return "Üzgünüm, şu anda bir hata oluştu.";
        }
    }

    /// <summary>
    /// Extract command from image using GPT-4 Vision
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
    /// Extract command from audio using Whisper + GPT
    /// </summary>
    public async Task<AICommandResult> ExtractCommandFromAudioAsync(
        byte[] audioData,
        string mimeType,
        string? textPrompt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Step 1: Transcribe audio using Whisper
            _logger.LogInformation("Transcribing audio with Whisper");

            using var audioContent = new ByteArrayContent(audioData);
            using var formData = new MultipartFormDataContent();
            
            formData.Add(audioContent, "file", "audio.mp3");
            formData.Add(new StringContent(_whisperModel), "model");
            formData.Add(new StringContent("text"), "response_format");

            var whisperResponse = await _httpClient.PostAsync(
                "https://api.openai.com/v1/audio/transcriptions",
                formData,
                cancellationToken);

            var whisperResult = await whisperResponse.Content.ReadAsStringAsync(cancellationToken);

            if (!whisperResponse.IsSuccessStatusCode)
            {
                _logger.LogError("Whisper API error: {StatusCode} - {Content}", whisperResponse.StatusCode, whisperResult);
                return new AICommandResult
                {
                    Success = false,
                    ErrorMessage = "Ses dosyası işlenemedi"
                };
            }

            dynamic transcription = JsonConvert.DeserializeObject(whisperResult)!;
            string transcribedText = transcription.text.ToString();

            _logger.LogInformation("Audio transcribed: {Text}", transcribedText);

            // Step 2: Extract command from transcribed text
            return await ExtractCommandAsync(transcribedText, null, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing audio with OpenAI");
            return new AICommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Extract command from multiple media using GPT-4 Vision
    /// </summary>
    public async Task<AICommandResult> ExtractCommandFromMultimodalAsync(
        List<MediaPart> mediaParts,
        string? textPrompt = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = GetCommandExtractionPrompt()
                }
            };

            // Build content array for user message
            var contentParts = new List<object>();

            foreach (var mediaPart in mediaParts)
            {
                if (mediaPart.Type == MediaPartType.Text && !string.IsNullOrEmpty(mediaPart.Text))
                {
                    contentParts.Add(new
                    {
                        type = "text",
                        text = mediaPart.Text
                    });
                }
                else if (mediaPart.Type == MediaPartType.Image && mediaPart.Data != null)
                {
                    contentParts.Add(new
                    {
                        type = "image_url",
                        image_url = new
                        {
                            url = $"data:{mediaPart.MimeType ?? "image/jpeg"};base64,{Convert.ToBase64String(mediaPart.Data)}"
                        }
                    });
                }
            }

            messages.Add(new
            {
                role = "user",
                content = contentParts.ToArray()
            });

            var payload = new
            {
                model = _model,
                messages,
                temperature = 0.7,
                max_tokens = 2000
            };

            var json = JsonConvert.SerializeObject(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                content,
                cancellationToken);

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            _logger.LogInformation("OpenAI multimodal response (Status: {StatusCode}): {Response}",
                response.StatusCode, responseContent);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("OpenAI API error: {StatusCode} - {Content}", response.StatusCode, responseContent);
                return new AICommandResult
                {
                    Success = false,
                    ErrorMessage = $"OpenAI API error: {response.StatusCode}"
                };
            }

            dynamic result = JsonConvert.DeserializeObject(responseContent)!;
            string generatedText = result.choices[0].message.content.ToString();

            _logger.LogInformation("OpenAI multimodal response: {Response}", generatedText);

            // Parse command from response
            return ParseCommandFromResponse(generatedText);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling OpenAI multimodal API");
            return new AICommandResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string GetCommandExtractionPrompt()
    {
        return @"You are an intelligent business assistant. Extract commands from natural language, voice, and visuals.

📦 PRODUCT COMMANDS:
• CreateProduct - Create new product (TR: ""Laptop ekle, 15000 TL"", EN: ""Add laptop, 15000 TL"")
• UpdateProduct - Update product (TR: ""Laptop fiyatını 16000 TL yap"", EN: ""Set laptop price to 16000 TL"")
• DeleteProduct - Delete product (TR: ""Laptop'ı sil"", EN: ""Delete laptop"")
• GetProduct - Product details (TR: ""Laptop'ın detaylarını göster"", EN: ""Show laptop details"")
• ListProducts - List products (TR: ""Ürünleri listele"", EN: ""List products"", ""10000-20000 TL range"")
• SearchProducts - Search products (TR: ""Laptop ara"", EN: ""Search laptop"", ""Product code 23537"")

👥 CUSTOMER COMMANDS:
• CreateCustomer - Add customer (TR: ""Ahmet Yılmaz müşteri ekle, tel: 555-1234"", EN: ""Add customer John Doe, phone: 555-1234"")
• UpdateCustomer - Update customer (TR: ""Ahmet'in telefonunu değiştir"", EN: ""Change John's phone"")
• DeleteCustomer - Delete customer (TR: ""Ahmet Yılmaz'ı sil"", EN: ""Delete John Doe"")
• GetCustomer - Customer details (TR: ""Ahmet'in bilgileri"", EN: ""John's information"")
• ListCustomers - List customers (TR: ""Müşterileri listele"", EN: ""List customers"")
• SearchCustomers - Search customers (TR: ""Ahmet ara"", EN: ""Search John"")

🧾 INVOICE COMMANDS:
• CreateInvoice - Create invoice (TR: ""Ahmet'e fatura kes, 2 laptop"", EN: ""Create invoice for John, 2 laptops"")
• UpdateInvoice - Update invoice (TR: ""123 nolu faturayı güncelle"", EN: ""Update invoice #123"")
• DeleteInvoice - Delete invoice (TR: ""123 nolu faturayı iptal et"", EN: ""Cancel invoice #123"")
• GetInvoice - Invoice details (TR: ""123 nolu faturayı göster"", EN: ""Show invoice #123"")
• ListInvoices - List invoices (TR: ""Bu ayki faturaları listele"", EN: ""List this month's invoices"")
• GetInvoiceStatus - Invoice status (TR: ""123 nolu fatura ödendi mi?"", EN: ""Is invoice #123 paid?"")
• SendInvoice - Send invoice (TR: ""123 nolu faturayı Ahmet'e gönder"", EN: ""Send invoice #123 to John"")

📄 CONTRACT COMMANDS:
• CreateContract - Create contract (TR: ""Ahmet ile 1 yıllık sözleşme"", EN: ""1-year contract with John"")
• UpdateContract - Update contract
• DeleteContract - Cancel contract
• GetContract - Contract details
• ListContracts - List contracts

📊 ANALYSIS & REPORTS:
• GetSalesReport - Sales report (TR: ""Bu ayın satış raporu"", EN: ""This month's sales report"")
• GetCustomerReport - Customer report (TR: ""En çok alışveriş yapan müşteriler"", EN: ""Top customers"")
• GetProductReport - Product report (TR: ""En çok satan ürünler"", EN: ""Best selling products"")
• GetFinancialSummary - Financial summary (TR: ""Bu ayın kazançı ne kadar?"", EN: ""This month's revenue?"")

⚡ BULK OPERATIONS:
• BulkCreateProducts - Bulk create products (TR: ""10 adet laptop ekle"", EN: ""Add 10 laptops"")
• BulkUpdateProducts - Bulk update (TR: ""Tüm ürünlere %10 zam"", EN: ""10% increase on all products"")
• BulkDeleteProducts - Bulk delete (TR: ""Stokta olmayan ürünleri sil"", EN: ""Delete out-of-stock products"")

🤖 SMART OPERATIONS:
• SuggestProducts - Suggest products (TR: ""Ahmet'e ne önerebilirim?"", EN: ""What can I suggest to John?"")
• PriceOptimization - Price optimization (TR: ""Laptop fiyatını optimize et"", EN: ""Optimize laptop price"")
• StockAlert - Stock alert (TR: ""Hangi ürünler bitmek üzere?"", EN: ""Which products are running low?"")

🎯 SPECIAL RULES:
1. Price/info queries → Use Get/List/Search commands
2. Delete/update operations → requiresConfirmation: true
3. If information is missing → Ask questions, don't assume
4. If uncertain → Return None and explain
5. Use context (remember previous messages)

📝 JSON FORMAT:
{
  ""commandType"": ""CreateProduct"",
  ""requiresConfirmation"": true,
  ""confidence"": 0.95,
  ""command"": {
    ""name"": ""Laptop"",
    ""price"": 15000,
    ""taxRate"": 18,
    ""description"": ""Gaming laptop""
  },
  ""confirmationMessage"": ""Laptop (15000 TL, KDV %18) oluşturmak istediğinizi anladım. Onaylıyor musunuz?"",
  ""missingFields"": [""stockQuantity""],
  ""suggestions"": [""Stok miktarını belirtir misiniz?""]
}

If not a command:
{
  ""commandType"": ""None"",
  ""response"": ""Merhaba! Size nasıl yardımcı olabilirim?""
}

💡 BE SMART:
- If information is missing, ask
- Suggest alternatives
- Fix typos (""laptp"" → ""laptop"")
- Use context
- Respond in user's language (Turkish or English)";
    }

    private AICommandResult ParseCommandFromResponse(string response)
    {
        try
        {
            // Remove markdown code blocks if present
            var cleanResponse = response.Trim();
            if (cleanResponse.StartsWith("```json"))
            {
                cleanResponse = cleanResponse.Substring(7);
            }
            if (cleanResponse.StartsWith("```"))
            {
                cleanResponse = cleanResponse.Substring(3);
            }
            if (cleanResponse.EndsWith("```"))
            {
                cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
            }
            cleanResponse = cleanResponse.Trim();

            dynamic parsed = JsonConvert.DeserializeObject(cleanResponse)!;
            string commandType = parsed.commandType?.ToString() ?? "Unknown";

            var commandResult = new AICommandResult
            {
                Success = true,
                CommandType = commandType,
                RequiresConfirmation = parsed.requiresConfirmation ?? false,
                ConfirmationMessage = parsed.confirmationMessage?.ToString(),
                CommandJson = cleanResponse
            };

            return commandResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing OpenAI response");
            return new AICommandResult
            {
                Success = true,
                CommandType = Domain.Enums.CommandType.Unknown.ToString(),
                CommandJson = "{\"commandType\": \"None\", \"response\": \"Üzgünüm, bir hata oluştu.\"}"
            };
        }
    }

    /// <summary>
    /// ⚠️ DEPRECATED: Use DynamicSlotFiller instead.
    /// This method uses hard-coded entity schemas and is not dynamic.
    /// </summary>
    [Obsolete("Use DynamicSlotFiller for dynamic entity extraction. This will be removed in v3.0.0")]
    public Task<Dictionary<string, object>> ExtractEntitiesAsync(
        string message,
        string commandType,
        CancellationToken cancellationToken = default)
    {
        // TODO: Implement OpenAI entity extraction if needed
        _logger.LogWarning("OpenAI entity extraction not implemented, returning empty");
        return Task.FromResult(new Dictionary<string, object>());
    }
}

