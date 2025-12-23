using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using System.Text.Json;
using SayP.Domain.Interfaces;
using SayP.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace SayP.Application.Services;

/// <summary>
/// Dinamik olarak backend API'lerini tarayarak intent'leri keşfeder
/// </summary>
public class IntentDiscoveryService : IIntentDiscoveryService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IntentDiscoveryService> _logger;
    private readonly ISayPDbContext _context;
    private readonly Dictionary<string, IntentDefinition> _discoveredIntents = new();
    private readonly Dictionary<string, List<string>> _intentPatterns = new();
    private DateTime _lastScanTime = DateTime.MinValue;

    public IntentDiscoveryService(
        IServiceProvider serviceProvider,
        ILogger<IntentDiscoveryService> logger,
        ISayPDbContext context)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _context = context;
    }

    public async Task<Dictionary<string, IntentDefinition>> DiscoverIntentsAsync()
    {
        if (DateTime.UtcNow - _lastScanTime < TimeSpan.FromMinutes(5))
        {
            return _discoveredIntents; // Cache for 5 minutes
        }

        _logger.LogInformation("Starting dynamic intent discovery...");

        try
        {
            // 1. Scan backend controllers
            await ScanBackendControllersAsync();
            
            // 2. Scan database entities
            await ScanDatabaseEntitiesAsync();
            
            // 3. Learn from conversation history
            await LearnFromConversationHistoryAsync();
            
            // 4. Generate natural language patterns
            await GenerateNaturalLanguagePatternsAsync();

            _lastScanTime = DateTime.UtcNow;
            
            _logger.LogInformation("Discovered {Count} intents", _discoveredIntents.Count);
            
            return _discoveredIntents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during intent discovery");
            return _discoveredIntents;
        }
    }

    private async Task ScanBackendControllersAsync()
    {
        _logger.LogDebug("Scanning backend controllers for API endpoints...");

        // Backend API'den endpoint'leri al
        var backendUserService = _serviceProvider.GetService<IBackendUserService>();
        if (backendUserService != null)
        {
            try
            {
                // Backend'den API metadata'sını al (swagger/openapi)
                var apiEndpoints = await GetBackendApiEndpointsAsync();
                
                foreach (var endpoint in apiEndpoints)
                {
                    var intent = CreateIntentFromEndpoint(endpoint);
                    if (intent != null)
                    {
                        _discoveredIntents[intent.Name] = intent;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not scan backend controllers");
            }
        }
    }

    private async Task ScanDatabaseEntitiesAsync()
    {
        _logger.LogDebug("Scanning database entities...");

        // Entity'leri tara ve CRUD operasyonları oluştur
        var entityTypes = new[]
        {
            "Product", "Customer", "Invoice", "Contract", "Order", "Payment", "Appointment"
        };

        foreach (var entityType in entityTypes)
        {
            // Create intents
            _discoveredIntents[$"Create{entityType}"] = new IntentDefinition
            {
                Name = $"Create{entityType}",
                Category = "CRUD",
                Description = $"Create a new {entityType.ToLower()}",
                RequiredFields = GetRequiredFieldsForEntity(entityType),
                OptionalFields = GetOptionalFieldsForEntity(entityType),
                Confidence = 0.9f,
                Examples = GenerateExamplesForEntity("Create", entityType)
            };

            // Read intents
            _discoveredIntents[$"Get{entityType}"] = new IntentDefinition
            {
                Name = $"Get{entityType}",
                Category = "CRUD",
                Description = $"Get {entityType.ToLower()} details",
                RequiredFields = new[] { "id" },
                OptionalFields = Array.Empty<string>(),
                Confidence = 0.95f,
                Examples = GenerateExamplesForEntity("Get", entityType)
            };

            // Update intents
            _discoveredIntents[$"Update{entityType}"] = new IntentDefinition
            {
                Name = $"Update{entityType}",
                Category = "CRUD",
                Description = $"Update {entityType.ToLower()} information",
                RequiredFields = new[] { "id" },
                OptionalFields = GetOptionalFieldsForEntity(entityType),
                Confidence = 0.85f,
                Examples = GenerateExamplesForEntity("Update", entityType)
            };

            // Delete intents
            _discoveredIntents[$"Delete{entityType}"] = new IntentDefinition
            {
                Name = $"Delete{entityType}",
                Category = "CRUD",
                Description = $"Delete {entityType.ToLower()}",
                RequiredFields = new[] { "id" },
                OptionalFields = Array.Empty<string>(),
                Confidence = 0.8f,
                Examples = GenerateExamplesForEntity("Delete", entityType),
                RequiresConfirmation = true
            };

            // List intents
            _discoveredIntents[$"List{entityType}s"] = new IntentDefinition
            {
                Name = $"List{entityType}s",
                Category = "CRUD",
                Description = $"List all {entityType.ToLower()}s",
                RequiredFields = Array.Empty<string>(),
                OptionalFields = new[] { "filter", "sort", "limit" },
                Confidence = 0.9f,
                Examples = GenerateExamplesForEntity("List", entityType)
            };
        }
    }

    private async Task LearnFromConversationHistoryAsync()
    {
        _logger.LogDebug("Learning from conversation history...");

        try
        {
            // Son 1000 mesajı analiz et
            var recentMessages = await _context.Messages
                .Where(m => m.CreatedAt > DateTime.UtcNow.AddDays(-30))
                .OrderByDescending(m => m.CreatedAt)
                .Take(1000)
                .Select(m => new { m.Content, m.Direction })
                .ToListAsync();

            var userMessages = recentMessages
                .Where(m => m.Direction == SayP.Domain.Enums.MessageDirection.Incoming)
                .Select(m => m.Content)
                .ToList();

            // Pattern'leri çıkar
            await ExtractPatternsFromMessages(userMessages);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error learning from conversation history");
        }
    }

    private Task GenerateNaturalLanguagePatternsAsync()
    {
        _logger.LogDebug("Generating natural language patterns...");

        foreach (var intent in _discoveredIntents.Values)
        {
            if (!_intentPatterns.ContainsKey(intent.Name))
            {
                _intentPatterns[intent.Name] = new List<string>();
            }

            // Türkçe pattern'ler oluştur
            var patterns = GenerateTurkishPatterns(intent);
            _intentPatterns[intent.Name].AddRange(patterns);
        }
        
        return Task.CompletedTask;
    }

    private List<string> GenerateTurkishPatterns(IntentDefinition intent)
    {
        var patterns = new List<string>();
        var entityName = ExtractEntityName(intent.Name);

        // Special handling for Appointment (Randevu)
        if (intent.Name.Contains("Appointment"))
        {
            if (intent.Name.StartsWith("Create"))
            {
                patterns.AddRange(new[]
                {
                    "randevu oluştur",
                    "randevu al",
                    "randevu ver",
                    "randevu ekle",
                    "randevu kaydet",
                    "randevu yap",
                    "randevu ayarla"
                });
            }
            else if (intent.Name.StartsWith("List"))
            {
                patterns.AddRange(new[]
                {
                    "randevuları listele",
                    "randevuları göster",
                    "randevu listesi",
                    "randevularım",
                    "bugünün randevuları"
                });
            }
            else if (intent.Name.StartsWith("Cancel") || intent.Name.StartsWith("Delete"))
            {
                patterns.AddRange(new[]
                {
                    "randevu iptal",
                    "randevuyu iptal et",
                    "randevu sil"
                });
            }
            return patterns;
        }

        switch (intent.Category)
        {
            case "CRUD":
                if (intent.Name.StartsWith("Create"))
                {
                    patterns.AddRange(new[]
                    {
                        $"{entityName} oluştur",
                        $"{entityName} ekle",
                        $"Yeni {entityName}",
                        $"{entityName} yarat",
                        $"{entityName} kaydet"
                    });
                }
                else if (intent.Name.StartsWith("Get"))
                {
                    patterns.AddRange(new[]
                    {
                        $"{entityName} göster",
                        $"{entityName} detayları",
                        $"{entityName} bilgileri",
                        $"{entityName} getir"
                    });
                }
                else if (intent.Name.StartsWith("Update"))
                {
                    patterns.AddRange(new[]
                    {
                        $"{entityName} güncelle",
                        $"{entityName} değiştir",
                        $"{entityName} düzenle"
                    });
                }
                else if (intent.Name.StartsWith("Delete"))
                {
                    patterns.AddRange(new[]
                    {
                        $"{entityName} sil",
                        $"{entityName} kaldır",
                        $"{entityName} iptal et"
                    });
                }
                else if (intent.Name.StartsWith("List"))
                {
                    patterns.AddRange(new[]
                    {
                        $"{entityName}ları listele",
                        $"{entityName}ları göster",
                        $"Tüm {entityName}lar",
                        $"{entityName} listesi"
                    });
                }
                break;
        }

        return patterns;
    }

    private string ExtractEntityName(string intentName)
    {
        var prefixes = new[] { "Create", "Get", "Update", "Delete", "List" };
        foreach (var prefix in prefixes)
        {
            if (intentName.StartsWith(prefix))
            {
                var entityName = intentName.Substring(prefix.Length);
                return entityName.EndsWith("s") ? entityName.Substring(0, entityName.Length - 1) : entityName;
            }
        }
        return intentName;
    }

    private async Task<List<ApiEndpoint>> GetBackendApiEndpointsAsync()
    {
        try
        {
            var endpoints = new List<ApiEndpoint>();
            
            // Backend API'den swagger/openapi metadata'sını al
            var backendUrl = Environment.GetEnvironmentVariable("BACKEND_API_URL") ?? "http://localhost:5000";
            var swaggerUrl = $"{backendUrl}/swagger/v1/swagger.json";
            
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            
            try
            {
                var response = await httpClient.GetStringAsync(swaggerUrl);
                var swaggerDoc = JsonSerializer.Deserialize<JsonElement>(response);
                
                if (swaggerDoc.TryGetProperty("paths", out var paths))
                {
                    foreach (var path in paths.EnumerateObject())
                    {
                        foreach (var method in path.Value.EnumerateObject())
                        {
                            var endpoint = new ApiEndpoint
                            {
                                Path = path.Name,
                                Method = method.Name.ToUpper(),
                                Description = method.Value.TryGetProperty("summary", out var summary) 
                                    ? summary.GetString() ?? "" 
                                    : "",
                                Parameters = ExtractParameters(method.Value)
                            };
                            endpoints.Add(endpoint);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not fetch swagger documentation from {Url}", swaggerUrl);
            }
            
            return endpoints;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting backend API endpoints");
            return new List<ApiEndpoint>();
        }
    }

    private List<string> ExtractParameters(JsonElement methodElement)
    {
        var parameters = new List<string>();
        
        if (methodElement.TryGetProperty("parameters", out var paramsArray))
        {
            foreach (var param in paramsArray.EnumerateArray())
            {
                if (param.TryGetProperty("name", out var name))
                {
                    parameters.Add(name.GetString() ?? "");
                }
            }
        }
        
        return parameters;
    }

    private IntentDefinition? CreateIntentFromEndpoint(ApiEndpoint endpoint)
    {
        try
        {
            // API endpoint'inden intent oluştur
            var intentName = DeriveIntentName(endpoint.Path, endpoint.Method);
            if (string.IsNullOrEmpty(intentName))
                return null;

            var intent = new IntentDefinition
            {
                Name = intentName,
                Category = "API",
                Description = endpoint.Description,
                RequiredFields = endpoint.Parameters.ToArray(),
                OptionalFields = Array.Empty<string>(),
                Confidence = 0.85f,
                Examples = GenerateExamplesFromEndpoint(endpoint),
                Metadata = new Dictionary<string, object>
                {
                    { "apiPath", endpoint.Path },
                    { "apiMethod", endpoint.Method }
                }
            };

            return intent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating intent from endpoint {Path}", endpoint.Path);
            return null;
        }
    }

    private string DeriveIntentName(string path, string method)
    {
        // /api/products -> Products
        // /api/customers/{id} -> Customer
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var resource = segments.LastOrDefault(s => !s.StartsWith("{"));
        
        if (string.IsNullOrEmpty(resource))
            return string.Empty;

        // Capitalize and singularize
        var entityName = char.ToUpper(resource[0]) + resource.Substring(1).TrimEnd('s');

        return method.ToUpper() switch
        {
            "POST" => $"Create{entityName}",
            "GET" => path.Contains("{") ? $"Get{entityName}" : $"List{entityName}s",
            "PUT" or "PATCH" => $"Update{entityName}",
            "DELETE" => $"Delete{entityName}",
            _ => string.Empty
        };
    }

    private List<string> GenerateExamplesFromEndpoint(ApiEndpoint endpoint)
    {
        var examples = new List<string>();
        var intentName = DeriveIntentName(endpoint.Path, endpoint.Method);
        
        if (intentName.StartsWith("Create"))
        {
            examples.Add($"{endpoint.Path.Split('/').Last()} oluştur");
            examples.Add($"Yeni {endpoint.Path.Split('/').Last()} ekle");
        }
        else if (intentName.StartsWith("List"))
        {
            examples.Add($"{endpoint.Path.Split('/').Last()} listele");
            examples.Add($"Tüm {endpoint.Path.Split('/').Last()}");
        }
        
        return examples;
    }

    private string[] GetRequiredFieldsForEntity(string entityType)
    {
        return entityType.ToLower() switch
        {
            "product" => new[] { "name", "price" },
            "customer" => new[] { "name", "email" },
            "invoice" => new[] { "customerId", "amount" },
            "contract" => new[] { "customerId", "startDate" },
            "order" => new[] { "customerId", "productId" },
            "payment" => new[] { "invoiceId", "amount" },
            "appointment" => new[] { "customerName", "serviceName", "date", "time" },
            _ => Array.Empty<string>()
        };
    }

    private string[] GetOptionalFieldsForEntity(string entityType)
    {
        return entityType.ToLower() switch
        {
            "product" => new[] { "description", "category", "stockQuantity", "taxRate" },
            "customer" => new[] { "phone", "address", "company" },
            "invoice" => new[] { "description", "dueDate", "taxRate" },
            "contract" => new[] { "endDate", "terms", "value" },
            "order" => new[] { "quantity", "notes", "deliveryDate" },
            "payment" => new[] { "paymentMethod", "notes" },
            "appointment" => new[] { "durationMinutes", "notes", "status" },
            _ => Array.Empty<string>()
        };
    }

    private List<string> GenerateExamplesForEntity(string action, string entityType)
    {
        var examples = new List<string>();
        var entityLower = entityType.ToLower();

        // Appointment için özel pattern'ler
        if (entityType == "Appointment")
        {
            switch (action)
            {
                case "Create":
                    examples.AddRange(new[]
                    {
                        "Randevu oluştur",
                        "Randevu al",
                        "Randevu kaydet",
                        "Yeni randevu",
                        "Randevu ver"
                    });
                    break;
                case "Get":
                    examples.AddRange(new[]
                    {
                        "Randevu detayları",
                        "Randevu bilgilerini göster",
                        "Randevu getir"
                    });
                    break;
                case "Update":
                    examples.AddRange(new[]
                    {
                        "Randevu güncelle",
                        "Randevu değiştir",
                        "Randevu saatini değiştir"
                    });
                    break;
                case "Delete":
                    examples.AddRange(new[]
                    {
                        "Randevu sil",
                        "Randevu iptal et",
                        "Randevu kaldır",
                        "Randevuyu iptal et"
                    });
                    break;
                case "List":
                    examples.AddRange(new[]
                    {
                        "Randevuları listele",
                        "Tüm randevular",
                        "Randevuları göster",
                        "Bugünkü randevular",
                        "Yarınki randevular",
                        "Bu haftaki randevular",
                        "Randevu listesi"
                    });
                    break;
            }
            return examples;
        }

        // Diğer entity'ler için genel pattern'ler
        switch (action)
        {
            case "Create":
                examples.AddRange(new[]
                {
                    $"{entityType} oluştur",
                    $"Yeni {entityLower} ekle",
                    $"{entityType} kaydet"
                });
                break;
            case "Get":
                examples.AddRange(new[]
                {
                    $"{entityType} detayları",
                    $"{entityType} bilgilerini göster",
                    $"{entityType} getir"
                });
                break;
            case "Update":
                examples.AddRange(new[]
                {
                    $"{entityType} güncelle",
                    $"{entityType} bilgilerini değiştir"
                });
                break;
            case "Delete":
                examples.AddRange(new[]
                {
                    $"{entityType} sil",
                    $"{entityType} kaldır"
                });
                break;
            case "List":
                examples.AddRange(new[]
                {
                    $"{entityType}ları listele",
                    $"Tüm {entityLower}lar"
                });
                break;
        }

        return examples;
    }

    private async Task ExtractPatternsFromMessages(List<string> messages)
    {
        try
        {
            // Mesajlardan pattern'leri çıkar ve öğren
            var patternFrequency = new Dictionary<string, int>();
            
            foreach (var message in messages)
            {
                var normalized = message.ToLowerInvariant().Trim();
                
                // Extract common patterns
                var patterns = ExtractCommonPatterns(normalized);
                
                foreach (var pattern in patterns)
                {
                    if (!patternFrequency.ContainsKey(pattern))
                        patternFrequency[pattern] = 0;
                    
                    patternFrequency[pattern]++;
                }
            }
            
            // Store frequently used patterns (threshold: 3+ occurrences)
            foreach (var pattern in patternFrequency.Where(p => p.Value >= 3))
            {
                // Try to match pattern to existing intents
                var matchedIntent = FindIntentForPattern(pattern.Key);
                
                if (matchedIntent != null)
                {
                    // Ensure the intent exists in the dictionary
                    if (!_intentPatterns.ContainsKey(matchedIntent))
                    {
                        _intentPatterns[matchedIntent] = new List<string>();
                    }
                    
                    if (!_intentPatterns[matchedIntent].Contains(pattern.Key))
                    {
                        _intentPatterns[matchedIntent].Add(pattern.Key);
                        _logger.LogInformation("Learned new pattern '{Pattern}' for intent {Intent}", 
                            pattern.Key, matchedIntent);
                    }
                }
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting patterns from messages");
        }
    }

    private List<string> ExtractCommonPatterns(string message)
    {
        var patterns = new List<string>();
        
        // Extract verb + noun patterns
        var verbNounPatterns = new[]
        {
            @"(ekle|oluştur|yap|kaydet)\s+(\w+)",
            @"(sil|kaldır|iptal)\s+(\w+)",
            @"(güncelle|değiştir|düzenle)\s+(\w+)",
            @"(listele|göster|getir)\s+(\w+)",
            @"(\w+)\s+(ekle|oluştur|yap)",
            @"(\w+)\s+(sil|kaldır)",
        };
        
        foreach (var patternRegex in verbNounPatterns)
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(message, patternRegex);
            foreach (System.Text.RegularExpressions.Match match in matches)
            {
                patterns.Add(match.Value);
            }
        }
        
        return patterns;
    }

    private string? FindIntentForPattern(string pattern)
    {
        var lowerPattern = pattern.ToLowerInvariant();
        
        // Match to intent categories
        if (lowerPattern.Contains("oluştur") || lowerPattern.Contains("ekle") || lowerPattern.Contains("yap"))
        {
            if (lowerPattern.Contains("ürün") || lowerPattern.Contains("product"))
                return "CreateProduct";
            if (lowerPattern.Contains("müşteri") || lowerPattern.Contains("customer"))
                return "CreateCustomer";
            if (lowerPattern.Contains("fatura") || lowerPattern.Contains("invoice"))
                return "CreateInvoice";
        }
        else if (lowerPattern.Contains("listele") || lowerPattern.Contains("göster"))
        {
            if (lowerPattern.Contains("ürün") || lowerPattern.Contains("product"))
                return "ListProducts";
            if (lowerPattern.Contains("müşteri") || lowerPattern.Contains("customer"))
                return "ListCustomers";
        }
        
        return null;
    }

    public async Task<List<string>> GetPatternsForIntentAsync(string intentName)
    {
        await DiscoverIntentsAsync(); // Ensure intents are discovered
        return _intentPatterns.GetValueOrDefault(intentName, new List<string>());
    }

    public async Task<IntentDefinition?> GetIntentDefinitionAsync(string intentName)
    {
        await DiscoverIntentsAsync();
        return _discoveredIntents.GetValueOrDefault(intentName);
    }
}

public class IntentDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string[] RequiredFields { get; set; } = Array.Empty<string>();
    public string[] OptionalFields { get; set; } = Array.Empty<string>();
    public float Confidence { get; set; }
    public List<string> Examples { get; set; } = new();
    public bool RequiresConfirmation { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class ApiEndpoint
{
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public List<string> Parameters { get; set; } = new();
}

public interface IIntentDiscoveryService
{
    Task<Dictionary<string, IntentDefinition>> DiscoverIntentsAsync();
    Task<List<string>> GetPatternsForIntentAsync(string intentName);
    Task<IntentDefinition?> GetIntentDefinitionAsync(string intentName);
}
