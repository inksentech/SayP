# 🔄 SayP Generic AI'ya Geçiş Kılavuzu

## 📋 Geçiş Adımları

### Adım 1: Database Migration (5 dakika)

DialogueState tablosuna yeni field ekle:

```bash
# Migration oluştur
cd SayP.Infrastructure
dotnet ef migrations add AddPendingEndpointJsonToDialogueState --startup-project ../SayP.Api

# Migration'ı uygula
dotnet ef database update --startup-project ../SayP.Api
```

**Manuel SQL (Alternatif)**:
```sql
ALTER TABLE "DialogueStates" 
ADD COLUMN "PendingEndpointJson" TEXT NULL;
```

---

### Adım 2: Backend'e Attribute'lar Ekle (30 dakika)

#### 2.1. CodeTemplatesController
```csharp
using SayP.Domain.Attributes; // veya kendi attribute'larınız

[HttpPost]
[SayP(
    Intent = "create_code_template",
    Description = "Yeni kod şablonu oluşturur",
    Aliases = new[] { "şablon oluştur", "template ekle" }
)]
public async Task<IActionResult> Create([FromBody] CodeTemplate template)
```

#### 2.2. DTO'lar
```csharp
public class CodeTemplate
{
    [Required]
    [SayPField(
        Description = "Şablon adı",
        Example = "React Component",
        Aliases = new[] { "isim", "ad" }
    )]
    public string Name { get; set; }
}
```

**Detaylı örnekler**: `backend/SAYP_INTEGRATION_EXAMPLE.md`

---

### Adım 3: WhatsApp Webhook'u Güncelle (10 dakika)

#### Option A: Direkt GenericWhatsAppHandler Kullan (Önerilen)

```csharp
// WhatsAppWebhookController.cs - HandleIncomingMessage metodunu güncelle

private readonly GenericWhatsAppHandler _genericHandler;

public WhatsAppWebhookController(
    // ... diğer parametreler
    GenericWhatsAppHandler genericHandler)
{
    _genericHandler = genericHandler;
}

private async Task HandleIncomingMessage(JsonElement message)
{
    // ... mevcut kod (validation, rate limit vb.)
    
    // ESKİ:
    // await _conversationManager.ProcessIncomingMessageAsync(...);
    
    // YENİ:
    await _genericHandler.ProcessMessageAsync(
        phoneNumber: from,
        messageText: messageText,
        messageId: messageId,
        timestamp: DateTimeOffset.FromUnixTimeSeconds(long.Parse(timestamp)).UtcDateTime,
        messageType: messageType,
        mediaId: mediaId,
        mimeType: mimeType,
        caption: caption
    );
}
```

#### Option B: Feature Flag ile Kademeli Geçiş

```csharp
// appsettings.json
{
  "SayP": {
    "UseGenericPipeline": true  // false = eski sistem, true = yeni sistem
  }
}

// WhatsAppWebhookController.cs
private async Task HandleIncomingMessage(JsonElement message)
{
    var useGeneric = _configuration.GetValue<bool>("SayP:UseGenericPipeline");
    
    if (useGeneric)
    {
        await _genericHandler.ProcessMessageAsync(...);
    }
    else
    {
        await _conversationManager.ProcessIncomingMessageAsync(...);
    }
}
```

---

### Adım 4: Test (15 dakika)

#### 4.1. Discovery Test
```bash
curl "http://localhost:5100/api/test/discovery?url=http://localhost:5245"
```

**Beklenen sonuç**: Backend'deki SayP-enabled endpoint'ler listelenmeli.

#### 4.2. Intent Mapping Test
```bash
curl -X POST http://localhost:5100/api/test/intent \
  -H "Content-Type: application/json" \
  -d '{
    "message": "kod şablonu oluştur",
    "tenantId": "your-tenant-id",
    "backendUrl": "http://localhost:5245"
  }'
```

**Beklenen sonuç**: `create_code_template` intent'i yüksek confidence ile eşleşmeli.

#### 4.3. WhatsApp End-to-End Test
```
WhatsApp'tan mesaj: "kod şablonu oluştur"
```

**Beklenen akış**:
1. SayP: "Şablon adı nedir?"
2. Sen: "React Component"
3. SayP: "Entity tipi nedir?"
4. Sen: "Component"
5. SayP: "✅ Başarıyla oluşturuldu!"

---

### Adım 5: Monitoring (5 dakika)

#### Redis Cache Kontrolü
```bash
redis-cli
> KEYS api_discovery:*
> GET api_discovery:your-tenant-id
```

#### Log Kontrolü
```bash
# SayP logs
docker-compose logs -f sayp-api | grep "Generic"

# Beklenen loglar:
[INFO] Starting API discovery for http://localhost:5245
[INFO] Discovered 4 endpoints from Swagger
[INFO] Matched endpoint: create_code_template with confidence 0.95
[INFO] Endpoint executed successfully in 245ms
```

---

## 🔧 Troubleshooting

### Problem 1: Endpoint'ler Keşfedilmiyor

**Kontrol listesi**:
- [ ] Backend Swagger URL'i erişilebilir mi? `http://localhost:5245/swagger/v1/swagger.json`
- [ ] Controller'larda `[SayP]` attribute var mı?
- [ ] Swagger'da "SayP" tag'i görünüyor mu?

**Çözüm**:
```bash
# Swagger'ı manuel kontrol et
curl http://localhost:5245/swagger/v1/swagger.json | jq '.paths'

# SayP tag'i arayın
curl http://localhost:5245/swagger/v1/swagger.json | grep "SayP"
```

### Problem 2: Intent Eşleşmiyor

**Kontrol listesi**:
- [ ] Alias'lar Türkçe mi?
- [ ] Description açıklayıcı mı?
- [ ] Fuzzy matching score'u düşük mü?

**Çözüm**:
```csharp
// Daha fazla alias ekle
[SayP(
    Intent = "create_code_template",
    Aliases = new[] {
        "şablon oluştur",
        "template ekle",
        "yeni şablon",
        "kod şablonu yap",
        "şablon yarat"  // Daha fazla!
    }
)]
```

### Problem 3: Parametreler Eksik

**Kontrol listesi**:
- [ ] DTO'larda `[SayPField]` var mı?
- [ ] Required field'lar işaretli mi?
- [ ] Example değerler verilmiş mi?

**Çözüm**:
```csharp
[Required]
[SayPField(
    Description = "Çok açık ve detaylı açıklama",
    Example = "Somut bir örnek değer",
    Aliases = new[] { "alternatif1", "alternatif2" }
)]
public string FieldName { get; set; }
```

### Problem 4: Migration Hatası

**Hata**: `Column already exists`

**Çözüm**:
```bash
# Migration'ı geri al
dotnet ef migrations remove --startup-project ../SayP.Api

# Veya manuel SQL
ALTER TABLE "DialogueStates" DROP COLUMN IF EXISTS "PendingEndpointJson";
```

---

## 📊 Karşılaştırma: Eski vs Yeni

### Eski Sistem (Hard-Coded)
```csharp
// 1. CommandType enum'a ekle
public enum CommandType {
    CreateCodeTemplate  // Manuel ekleme
}

// 2. Command schema yaz
public class CreateCodeTemplateCommand {
    public string Name { get; set; }
    // Manuel tanımlama
}

// 3. Executor'a case ekle
switch (commandType) {
    case CommandType.CreateCodeTemplate:
        await ExecuteCreateCodeTemplate(...);
        break;
}

// 4. Intent mapping ekle
if (message.Contains("şablon")) {
    return CommandType.CreateCodeTemplate;
}
```

**Toplam**: ~200 satır kod, her yeni komut için tekrar

### Yeni Sistem (Generic AI)
```csharp
// Backend'de sadece:
[HttpPost]
[SayP(Intent = "create_code_template", Description = "...")]
public async Task<IActionResult> Create([FromBody] CodeTemplate template)
```

**Toplam**: 3 satır attribute, SayP otomatik halleder!

---

## 🎯 Geçiş Sonrası Cleanup (Opsiyonel)

### Silinebilecek Dosyalar
```
❌ SayP.Domain/Enums/CommandType.cs (artık gerekli değil)
❌ SayP.Application/CommandSchemas/*.cs (artık gerekli değil)
❌ SayP.Infrastructure/CommandExecution/BackendCommandExecutor.cs (GenericCommandExecutor kullan)
❌ SayP.Application/Services/AICommandRouter.cs (DynamicIntentMapper kullan)
```

### Deprecate Edilebilecek Servisler
```csharp
// Eski servisleri @Obsolete ile işaretle
[Obsolete("Use GenericConversationManager instead")]
public class ConversationManager { }

[Obsolete("Use DynamicIntentMapper instead")]
public class AICommandRouter { }
```

### Temizlik Adımları
```bash
# 1. Eski servisleri kaldır
git rm SayP.Domain/Enums/CommandType.cs
git rm -r SayP.Application/CommandSchemas/

# 2. Program.cs'den eski servisleri çıkar
# builder.Services.AddScoped<AICommandRouter>(); // Kaldır
# builder.Services.AddScoped<SlotFillingManager>(); // Kaldır (DynamicSlotFiller kullan)

# 3. Commit
git commit -m "Migrate to Generic AI - Remove hard-coded commands"
```

---

## 📈 Beklenen İyileştirmeler

### Geliştirme Hızı
- **Önce**: Yeni komut eklemek ~2 saat
- **Sonra**: Yeni komut eklemek ~5 dakika (sadece attribute)

### Maintenance
- **Önce**: Her komut için kod değişikliği
- **Sonra**: Backend'de attribute değiştir, SayP otomatik adapte olur

### Esneklik
- **Önce**: Tek backend'e bağlı
- **Sonra**: Birden fazla backend'e bağlanabilir

### Öğrenme
- **Önce**: Statik, öğrenme yok
- **Sonra**: User feedback ile iyileşir

---

## ✅ Geçiş Checklist

- [ ] Database migration yapıldı
- [ ] Backend'e attribute'lar eklendi
- [ ] WhatsApp webhook güncellendi
- [ ] Discovery test başarılı
- [ ] Intent mapping test başarılı
- [ ] WhatsApp end-to-end test başarılı
- [ ] Redis cache çalışıyor
- [ ] Loglar düzgün
- [ ] Production'a deploy edildi
- [ ] Eski kod temizlendi (opsiyonel)

---

## 🎉 Tebrikler!

SayP artık **generic, AI-powered, self-learning** bir platform! 🚀

**Yeni özellik eklemek için**: Backend'e sadece `[SayP]` attribute ekle!
