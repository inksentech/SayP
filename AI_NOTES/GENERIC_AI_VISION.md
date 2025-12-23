# SayP Generic AI-Powered Integration Platform

## 🎯 Vizyon

SayP'yi **self-learning, generic, AI-first** bir entegrasyon platformuna dönüştürme.

### Hedef
Herhangi bir .NET backend API'sine bağlanıp, controller'ları otomatik keşfedip, kullanıcı niyetini anlayıp doğru endpoint'e yönlendiren akıllı sistem.

---

## 🏗️ Yeni Mimari

```
┌─────────────────────────────────────────────────────────┐
│                    SayP Generic AI                      │
│                                                         │
│  ┌──────────────────────────────────────────────────┐  │
│  │         1. API Discovery Engine                  │  │
│  │  - Swagger/OpenAPI okuma                         │  │
│  │  - [SayP] attribute tarama                       │  │
│  │  - Controller/Action keşfi                       │  │
│  └──────────────────────────────────────────────────┘  │
│                         │                               │
│                         ▼                               │
│  ┌──────────────────────────────────────────────────┐  │
│  │         2. Schema Learning System                │  │
│  │  - DTO analizi                                   │  │
│  │  - Required/Optional field detection             │  │
│  │  - Validation rules extraction                   │  │
│  │  - Redis cache                                   │  │
│  └──────────────────────────────────────────────────┘  │
│                         │                               │
│                         ▼                               │
│  ┌──────────────────────────────────────────────────┐  │
│  │         3. AI Intent Mapper                      │  │
│  │  - Natural language → Intent                     │  │
│  │  - Intent → Endpoint matching                    │  │
│  │  - Confidence scoring                            │  │
│  │  - Multi-language support                        │  │
│  └──────────────────────────────────────────────────┘  │
│                         │                               │
│                         ▼                               │
│  ┌──────────────────────────────────────────────────┐  │
│  │         4. Dynamic Slot Filler                   │  │
│  │  - Schema-based slot extraction                  │  │
│  │  - Multi-turn conversation                       │  │
│  │  - Smart defaults                                │  │
│  │  - Validation                                    │  │
│  └──────────────────────────────────────────────────┘  │
│                         │                               │
│                         ▼                               │
│  ┌──────────────────────────────────────────────────┐  │
│  │         5. Generic Executor                      │  │
│  │  - Dynamic HTTP request building                 │  │
│  │  - JSON serialization                            │  │
│  │  - Error handling                                │  │
│  │  - Response parsing                              │  │
│  └──────────────────────────────────────────────────┘  │
│                         │                               │
│                         ▼                               │
│  ┌──────────────────────────────────────────────────┐  │
│  │         6. Self-Learning Engine                  │  │
│  │  - Success/failure tracking                      │  │
│  │  - User feedback collection                      │  │
│  │  - Intent mapping improvement                    │  │
│  │  - Auto-optimization                             │  │
│  └──────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────┘
```

---

## 📝 Kullanım Senaryosu

### Backend API (Entegre edilecek sistem)
```csharp
[ApiController]
[Route("api/code-templates")]
public class CodeTemplatesController : ControllerBase
{
    [HttpPost]
    [SayP(Intent = "create_code_template", Description = "Yeni kod şablonu oluşturur")]
    public async Task<IActionResult> Create([FromBody] CodeTemplate template)
    {
        // Implementation
    }
    
    [HttpGet]
    [SayP(Intent = "list_code_templates", Description = "Kod şablonlarını listeler")]
    public async Task<IActionResult> List([FromQuery] string? entityType = null)
    {
        // Implementation
    }
}

public class CodeTemplate
{
    [Required]
    [SayPField(Description = "Şablon adı", Example = "React Component")]
    public string Name { get; set; }
    
    [Required]
    [SayPField(Description = "Entity tipi", Example = "Component")]
    public string EntityType { get; set; }
    
    [SayPField(Description = "Şablon içeriği", Optional = true)]
    public string? Content { get; set; }
}
```

### Kullanıcı Konuşması
```
Kullanıcı: "Kod şablonu oluştur"

SayP: "Kod şablonu oluşturmak için bazı bilgilere ihtiyacım var:
       1. Şablon adı nedir?
       2. Hangi entity tipi için? (örn: Component, Service, Controller)"

Kullanıcı: "React Component şablonu, Component tipi"

SayP: "Anladım! React Component adında, Component tipi için bir kod şablonu oluşturuyorum.
       Şablon içeriği eklemek ister misiniz? (Opsiyonel)"

Kullanıcı: "Hayır"

SayP: ✅ "Kod şablonu başarıyla oluşturuldu!"
```

---

## 🔧 Temel Bileşenler

### 1. SayP Attribute (Backend'de)
```csharp
[AttributeUsage(AttributeTargets.Method)]
public class SayPAttribute : Attribute
{
    public string Intent { get; set; }
    public string Description { get; set; }
    public string[] Aliases { get; set; } = Array.Empty<string>();
}

[AttributeUsage(AttributeTargets.Property)]
public class SayPFieldAttribute : Attribute
{
    public string Description { get; set; }
    public string Example { get; set; }
    public bool Optional { get; set; }
}
```

### 2. API Discovery Service (SayP'de)
```csharp
public interface IApiDiscoveryService
{
    Task<List<DiscoveredEndpoint>> DiscoverEndpointsAsync(string baseUrl);
    Task<EndpointSchema> GetEndpointSchemaAsync(DiscoveredEndpoint endpoint);
    Task CacheDiscoveryAsync(string tenantId, List<DiscoveredEndpoint> endpoints);
}
```

### 3. Dynamic Intent Mapper (SayP'de)
```csharp
public interface IDynamicIntentMapper
{
    Task<IntentMappingResult> MapIntentAsync(string message, List<DiscoveredEndpoint> endpoints);
    Task LearnFromFeedbackAsync(string message, string correctIntent, bool wasCorrect);
}
```

### 4. Generic Executor (SayP'de)
```csharp
public interface IGenericCommandExecutor
{
    Task<ExecutionResult> ExecuteAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> parameters,
        Guid tenantId);
}
```

---

## 🚀 Implementation Roadmap

### Phase 1: API Discovery (1 hafta)
- Swagger/OpenAPI parser
- [SayP] attribute scanner
- Schema extraction
- Redis caching

### Phase 2: Dynamic Intent Mapping (1 hafta)
- AI-powered intent detection
- Endpoint matching algorithm
- Confidence scoring
- Multi-language support

### Phase 3: Generic Execution (1 hafta)
- Dynamic HTTP client
- JSON serialization/deserialization
- Error handling
- Response parsing

### Phase 4: Self-Learning (1 hafta)
- Feedback collection
- Intent mapping improvement
- Success rate tracking
- Auto-optimization

### Phase 5: Cleanup & Optimization (3 gün)
- Remove hard-coded commands
- Simplify architecture
- Performance optimization
- Documentation

**TOPLAM: ~4-5 hafta**
