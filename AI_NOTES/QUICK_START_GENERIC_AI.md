# 🚀 SayP Generic AI - Hızlı Başlangıç Kılavuzu

## 🎯 Ne Değişti?

### Önce (Hard-Coded)
```csharp
// Her komut için manuel tanım
public enum CommandType {
    CreateProduct,
    CreateInvoice,
    CreateContract
}

// Her komut için özel executor
if (commandType == CommandType.CreateProduct) {
    await CreateProductAsync(...);
}
```

### Şimdi (Generic AI)
```csharp
// Backend'de sadece attribute ekle
[HttpPost]
[SayP(Intent = "create_code_template", Description = "Kod şablonu oluşturur")]
public async Task<IActionResult> Create([FromBody] CodeTemplate template)

// SayP otomatik keşfeder ve çalıştırır!
var result = await _genericConversationManager.ProcessMessageAsync(
    message: "Kod şablonu oluştur",
    ...
);
```

---

## 📦 Kurulum (3 Adım)

### 1. Backend'e Attribute Ekle

```csharp
// Backend projesine SayP.Domain.dll referansı ekle
// veya attribute'ları kopyala

[ApiController]
[Route("api/[controller]")]
public class YourController : ControllerBase
{
    [HttpPost]
    [SayP(
        Intent = "your_action",
        Description = "Ne yaptığını açıkla",
        Aliases = new[] { "alternatif isimler" }
    )]
    public async Task<IActionResult> YourAction([FromBody] YourDto dto)
    {
        // Your code
    }
}
```

### 2. DTO'lara Field Attribute Ekle

```csharp
public class YourDto
{
    [Required]
    [SayPField(
        Description = "Alan açıklaması",
        Example = "Örnek değer",
        Aliases = new[] { "alternatif adlar" }
    )]
    public string FieldName { get; set; }
}
```

### 3. SayP'yi Başlat

```bash
cd sayp
dotnet run --project SayP.Api
```

---

## 🧪 Test Senaryoları

### Test 1: Discovery
```bash
# Backend'deki endpoint'leri keşfet
curl "http://localhost:5100/api/test/discovery?url=https://your-backend.com"

# Response:
# [
#   {
#     "intent": "create_code_template",
#     "description": "Kod şablonu oluşturur",
#     "route": "/api/code-templates",
#     "httpMethod": "POST",
#     "schema": { ... }
#   }
# ]
```

### Test 2: Intent Mapping
```bash
# Kullanıcı mesajını intent'e eşleştir
curl -X POST http://localhost:5100/api/test/intent \
  -H "Content-Type: application/json" \
  -d '{
    "message": "kod şablonu oluştur",
    "tenantId": "your-tenant-id"
  }'

# Response:
# {
#   "success": true,
#   "matchedIntent": "create_code_template",
#   "confidence": 0.95
# }
```

### Test 3: WhatsApp End-to-End
```
WhatsApp'tan mesaj gönder: "kod şablonu oluştur"

SayP: "Şablon adı nedir? (Örnek: React Component)"
Sen: "React Component"

SayP: "Entity tipi nedir? (Örnek: Component)"
Sen: "Component"

SayP: "Anladım! Yeni kod şablonu oluşturur
       
       Parametreler:
       • Name: React Component
       • EntityType: Component
       
       Devam etmek istiyor musunuz? (Evet/Hayır)"
Sen: "Evet"

SayP: "✅ Yeni kod şablonu oluşturur başarıyla tamamlandı!"
```

---

## 🔧 Configuration

### appsettings.json
```json
{
  "SayP": {
    "BackendUrl": "https://your-backend.com",
    "ApiKey": "your-api-key",
    "DiscoveryCacheDuration": "24:00:00",
    "EnableAutoDiscovery": true
  }
}
```

### Environment Variables
```env
BACKEND_API_URL=https://your-backend.com
SAYP_API_KEY=your-api-key
```

---

## 📊 Monitoring

### Discovery Cache
```bash
# Redis'te cached endpoint'leri gör
redis-cli
> KEYS api_discovery:*
> GET api_discovery:your-tenant-id
```

### Logs
```bash
# Discovery logs
[INFO] Starting API discovery for https://backend.com
[INFO] Discovered 15 endpoints from Swagger
[INFO] Cached 15 endpoints for tenant abc-123

# Intent mapping logs
[INFO] Mapping intent for message: kod şablonu oluştur
[INFO] High confidence fuzzy match found: create_code_template
[INFO] Matched endpoint: create_code_template with confidence 0.95

# Execution logs
[INFO] Executing endpoint create_code_template for tenant abc-123
[INFO] Endpoint executed successfully in 245ms
```

---

## 🎓 Best Practices

### 1. Attribute Kullanımı
✅ **DO**: Açıklayıcı description yaz
✅ **DO**: Türkçe alias'lar ekle
✅ **DO**: Example değerler ver
❌ **DON'T**: Intent name'leri çok karmaşık yapma

### 2. Schema Design
✅ **DO**: Required field'ları işaretle
✅ **DO**: Validation pattern ekle
✅ **DO**: Default value'lar ver
❌ **DON'T**: Çok fazla nested object kullanma

### 3. Error Handling
✅ **DO**: Anlamlı error message'lar dön
✅ **DO**: HTTP status code'ları doğru kullan
✅ **DO**: Validation error'ları detaylı açıkla

---

## 🐛 Troubleshooting

### Problem: Endpoint'ler keşfedilmiyor
```bash
# Swagger URL'i kontrol et
curl https://your-backend.com/swagger/v1/swagger.json

# SayP tag'i var mı?
# "tags": ["SayP"]

# x-sayp extension var mı?
# "x-sayp": { "intent": "..." }
```

### Problem: Intent eşleşmiyor
```bash
# Fuzzy matching score'u düşük olabilir
# Daha açıklayıcı alias'lar ekle

[SayP(
    Intent = "create_template",
    Aliases = new[] {
        "şablon oluştur",
        "template ekle",
        "yeni şablon",
        "kod şablonu yap"
    }
)]
```

### Problem: Parametreler eksik
```bash
# Schema extraction başarısız olabilir
# Manuel schema tanımla veya
# SayPField attribute'larını ekle

[SayPField(
    Description = "Çok açık açıklama",
    Example = "Somut örnek",
    Aliases = new[] { "alternatif adlar" }
)]
```

---

## 📈 Performance Tips

### 1. Cache Optimization
```csharp
// Discovery cache duration'ı artır
await _discoveryService.CacheDiscoveryAsync(
    tenantId,
    endpoints,
    expiration: TimeSpan.FromHours(48) // 24 saat yerine 48
);
```

### 2. Fuzzy Matching First
```csharp
// AI matching pahalı, fuzzy matching önce dene
var fuzzyResult = await TryFuzzyMatchingAsync(...);
if (fuzzyResult.Confidence > 0.85) {
    return fuzzyResult; // AI'yi çağırma
}
```

### 3. Parallel Discovery
```csharp
// Birden fazla backend varsa parallel keşfet
var tasks = backends.Select(b => 
    _discoveryService.DiscoverEndpointsAsync(b.Url, b.ApiKey)
);
var results = await Task.WhenAll(tasks);
```

---

## 🎉 Sonuç

SayP artık **tamamen generic**! 

- ✅ Zero configuration
- ✅ Auto-discovery
- ✅ AI-powered
- ✅ Self-learning
- ✅ Multi-backend

**Backend'e sadece attribute ekle, SayP gerisini halleder! 🚀**

---

## 📞 Destek

- **Dokümantasyon**: GENERIC_AI_IMPLEMENTATION_COMPLETE.md
- **Mimari**: GENERIC_AI_VISION.md
- **Entegrasyon**: SAYP_INTEGRATION_ANALYSIS_*.md
