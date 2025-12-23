# 🤖 AI-Powered Smart Slot Filling

## 🎯 Problem

**Önceki Durum:**
```
User: "Kod şablonu oluştur"
System: ✅ Matched: create_code_template
        ✅ All required fields filled (Name, EntityType have defaults)
        → Direkt onay aşamasına geçiyor ❌
```

**Sorun:** Backend'de sadece `Name` ve `EntityType` required, ama bunların bile default değerleri var. Sistem tüm alanları "tamamlandı" sayıyor ve kullanıcıya hiçbir soru sormadan direkt onay istiyor.

---

## 💡 Çözüm: 3-Tier Smart Field Selection

### **Tier 1: AI-Powered Field Selection (En Akıllı)**

AI'ya sor: "Bu endpoint için kullanıcıdan hangi minimum bilgileri almalıyım?"

```csharp
var prompt = @"You are analyzing an API endpoint to determine which fields are ESSENTIAL to ask the user.

Endpoint: Kod şablonu oluştur
Intent: create_code_template

Available Fields:
[
  {""Name"": ""Name"", ""Type"": ""string"", ""IsRequired"": true, ""Description"": ""Template name""},
  {""Name"": ""EntityType"", ""Type"": ""string"", ""IsRequired"": true, ""Description"": ""Entity type""},
  {""Name"": ""Prefix"", ""Type"": ""string"", ""IsRequired"": false, ""Description"": ""Code prefix""},
  {""Name"": ""Separator"", ""Type"": ""string"", ""IsRequired"": false, ""Description"": ""Separator""},
  {""Name"": ""NumericLength"", ""Type"": ""int"", ""IsRequired"": false, ""Description"": ""Numeric length""}
]

IMPORTANT RULES:
1. Only select fields that are TRULY NECESSARY for the operation to make sense
2. Ignore technical fields (IDs, timestamps, flags with defaults)
3. Focus on business-critical information (names, descriptions, key identifiers)
4. Maximum 3-4 fields to avoid overwhelming the user
5. If a field has a sensible default value, DON'T ask for it

Respond with JSON:
{
  ""fieldsToAsk"": [""Name"", ""EntityType""],
  ""reasoning"": ""Name and EntityType are essential to identify what kind of template to create. Other fields have sensible defaults.""
}";
```

**AI Response:**
```json
{
  "fieldsToAsk": ["Name", "EntityType"],
  "reasoning": "Name and EntityType are essential to identify what kind of template to create. Prefix, Separator, and NumericLength have sensible defaults and can be configured later."
}
```

---

### **Tier 2: Required Fields Fallback**

AI başarısız olursa, sadece **required** alanları sor:

```csharp
var requiredFields = endpoint.Schema.Fields.Where(f => f.IsRequired).ToList();
```

---

### **Tier 3: Smart Heuristic Fallback**

Required field yoksa, **örnek veya açıklama içeren** alanları sor (max 3):

```csharp
var fieldsWithExamples = endpoint.Schema.Fields
    .Where(f => !string.IsNullOrEmpty(f.Example) || !string.IsNullOrEmpty(f.Description))
    .Take(3)
    .ToList();
```

---

## 🎯 Beklenen Davranış (Düzeltme Sonrası)

### **Senaryo 1: Kod Şablonu Oluştur**

```
User: "Kod şablonu oluştur"

AI Field Selection:
  → fieldsToAsk: ["Name", "EntityType"]
  → reasoning: "Essential fields for template creation"

Bot: "📋 **Kod şablonu oluştur**
     
     ⏳ Eksik bilgiler: 2 adet
     
     📝 İsim nedir?
     💡 Örnek: ProductTemplate"

User: "Ürün Kodu"

Bot: "📋 **Kod şablonu oluştur**
     
     ✅ Toplanan bilgiler:
       • İsim: Ürün Kodu
     
     ⏳ Eksik bilgiler: 1 adet
     
     📝 Entity Type nedir?"

User: "Product"

Bot: "✅ Anladım! **Kod şablonu oluştur** işlemini yapacağım.
     
     📝 **Parametreler:**
       • İsim: **Ürün Kodu**
       • Entity Type: **Product**
     
     ❓ Bu bilgilerle devam etmek istiyor musunuz?
       • \"Evet\" → İşlemi başlat
       • \"Hayır\" → İptal et"
```

---

### **Senaryo 2: Ürün Oluştur (Çok Fazla Alan)**

```
User: "Ürün ekle"

AI Field Selection:
  → fieldsToAsk: ["Name", "Price", "Code"]
  → reasoning: "Name and Price are essential. Code is important for inventory. Other fields like IsActive, CreatedAt have defaults."

Bot: "📋 **Ürün ekle**
     
     ⏳ Eksik bilgiler: 3 adet
     
     📝 İsim nedir?"

User: "Laptop"

Bot: "📋 **Ürün ekle**
     
     ✅ Toplanan bilgiler:
       • İsim: Laptop
     
     ⏳ Eksik bilgiler: 2 adet
     
     📝 Fiyat nedir?
     ℹ️ Sayı giriniz (örn: 99.99)"

User: "15000"

Bot: "📋 **Ürün ekle**
     
     ✅ Toplanan bilgiler:
       • İsim: Laptop
       • Fiyat: 15000
     
     ⏳ Eksik bilgiler: 1 adet
     
     📝 Kod nedir?"

User: "LAP-001"

Bot: "✅ Anladım! **Ürün ekle** işlemini yapacağım.
     
     📝 **Parametreler:**
       • İsim: **Laptop**
       • Fiyat: **15000**
       • Kod: **LAP-001**
     
     ❓ Bu bilgilerle devam etmek istiyor musunuz?"
```

---

## 🔧 Teknik Implementasyon

### **DynamicSlotFiller.cs**

```csharp
public class DynamicSlotFiller
{
    private readonly IAIProvider? _aiProvider;

    // 🎯 Main method: AI-powered smart field selection
    public async Task<DynamicSlotFillingResult> AnalyzeSlotsAsync(
        DiscoveredEndpoint endpoint,
        Dictionary<string, object> extractedParameters,
        CancellationToken cancellationToken = default)
    {
        // Get fields that need user input (AI-powered or fallback)
        var fieldsToAsk = await DetermineFieldsToAskAsync(endpoint, extractedParameters, cancellationToken);

        // Check which fields are missing
        foreach (var field in fieldsToAsk)
        {
            if (!HasValue(field, extractedParameters))
            {
                missingSlots.Add(field);
            }
        }

        return result;
    }

    // 🤖 Tier 1: AI-powered field selection
    private async Task<List<SchemaField>> AskAIForImportantFieldsAsync(
        DiscoveredEndpoint endpoint,
        CancellationToken cancellationToken)
    {
        var prompt = BuildFieldSelectionPrompt(endpoint);
        var response = await _aiProvider.GenerateResponseAsync(prompt, null, cancellationToken);
        
        // Parse AI response
        var json = ParseJSON(response);
        var fieldNames = json["fieldsToAsk"];
        var reasoning = json["reasoning"];
        
        _logger.LogInformation("AI reasoning: {Reasoning}", reasoning);
        
        return MapFieldNames(fieldNames, endpoint.Schema.Fields);
    }

    // 📋 Tier 2: Required fields fallback
    // 💡 Tier 3: Smart heuristic fallback
    private async Task<List<SchemaField>> DetermineFieldsToAskAsync(...)
    {
        // Try AI first
        if (_aiProvider != null)
        {
            var aiFields = await AskAIForImportantFieldsAsync(endpoint, cancellationToken);
            if (aiFields.Any()) return aiFields;
        }

        // Fallback to required fields
        var requiredFields = endpoint.Schema.Fields.Where(f => f.IsRequired).ToList();
        if (requiredFields.Any()) return requiredFields;

        // Last resort: fields with examples/descriptions
        return endpoint.Schema.Fields
            .Where(f => !string.IsNullOrEmpty(f.Example) || !string.IsNullOrEmpty(f.Description))
            .Take(3)
            .ToList();
    }
}
```

---

## 📊 Performans & Maliyet

### **AI Call Frequency:**
- ✅ **Sadece 1 kere** (ilk intent mapping'den sonra)
- ✅ Cache edilebilir (aynı endpoint için tekrar sorma)

### **Latency:**
```
Without AI Field Selection:
  Intent Mapping: 1.5s
  → Direkt onay (parametreler sorulmadı) ❌
  Total: ~1.5s

With AI Field Selection:
  Intent Mapping: 1.5s
  Field Selection: 1.2s (paralel çalıştırılabilir)
  → Doğru parametreler soruluyor ✅
  Total: ~2.7s (ilk mesaj için)
  Subsequent messages: ~0s (cache)
```

### **Token Usage:**
```
Field Selection Prompt: ~500 tokens
AI Response: ~100 tokens
Total: ~600 tokens per endpoint (~$0.001)
```

---

## 🎨 UX İyileştirmeleri

### **Öncesi:**
```
User: "Kod şablonu oluştur"
Bot: "✅ Anladım! Kod şablonu oluştur işlemini yapacağım.
     
     📝 Parametreler:
       • İsim: (default)
       • EntityType: (default)
       • Prefix: (default)
       • Separator: -
       • NumericLength: 6
       ... (20+ alan)
     
     ❓ Bu bilgilerle devam etmek istiyor musunuz?"

User: "Ama ben hiçbir şey söylemedim ki?" 😕
```

### **Sonrası:**
```
User: "Kod şablonu oluştur"
Bot: "📋 Kod şablonu oluştur
     
     📝 İsim nedir?
     💡 Örnek: ProductTemplate"

User: "Ürün Kodu"
Bot: "📝 Entity Type nedir?"

User: "Product"
Bot: "✅ Anladım! Kod şablonu oluştur işlemini yapacağım.
     
     📝 Parametreler:
       • İsim: **Ürün Kodu**
       • Entity Type: **Product**
     
     ❓ Bu bilgilerle devam etmek istiyor musunuz?"

User: "Evet" ✅
```

---

## 🚀 Avantajlar

### **1. Akıllı Alan Seçimi**
- ✅ AI sadece **gerekli** alanları sorar
- ✅ Default değeri olan alanları atlar
- ✅ Teknik alanları (ID, timestamp) görmezden gelir

### **2. Kullanıcı Dostu**
- ✅ Maksimum 3-4 soru
- ✅ Her soru için açıklama ve örnek
- ✅ İlerleme göstergesi

### **3. Esnek & Robust**
- ✅ AI başarısız olursa fallback
- ✅ Required field yoksa heuristic
- ✅ Her endpoint için özelleştirilebilir

### **4. Performanslı**
- ✅ AI sonuçları cache edilebilir
- ✅ Paralel çalıştırılabilir
- ✅ Düşük token maliyeti

---

## 📝 Örnek AI Responses

### **create_code_template:**
```json
{
  "fieldsToAsk": ["Name", "EntityType"],
  "reasoning": "Name identifies the template, EntityType specifies what it's for. Other fields have sensible defaults."
}
```

### **create_product:**
```json
{
  "fieldsToAsk": ["Name", "Price", "Code"],
  "reasoning": "Name and Price are essential for any product. Code is important for inventory tracking. Fields like IsActive, CreatedAt have defaults."
}
```

### **create_appointment:**
```json
{
  "fieldsToAsk": ["CustomerName", "Date", "Time"],
  "reasoning": "Customer, date, and time are the minimum required to schedule an appointment. Duration and notes can have defaults."
}
```

---

## 🧪 Test Senaryoları

### **Test 1: Kod Şablonu (2 alan)**
```
1. "Kod şablonu oluştur"
2. "Ürün Kodu"
3. "Product"
4. "Evet"
→ ✅ Başarı
```

### **Test 2: Ürün (3 alan)**
```
1. "Ürün ekle"
2. "Laptop"
3. "15000"
4. "LAP-001"
5. "Evet"
→ ✅ Başarı
```

### **Test 3: AI Fallback (Required Fields)**
```
AI unavailable → Use required fields
→ ✅ Fallback çalışıyor
```

### **Test 4: No Required Fields (Heuristic)**
```
No required fields → Use fields with examples
→ ✅ Heuristic çalışıyor
```

---

## 🎯 Sonuç

✅ **Akıllı:** AI sadece gerekli alanları sorar
✅ **Esnek:** 3-tier fallback sistemi
✅ **Hızlı:** Cache + paralel çalışma
✅ **Kullanıcı Dostu:** Maksimum 3-4 soru
✅ **Robust:** AI başarısız olsa bile çalışır

**Kullanıcı deneyimi artık çok daha doğal ve akıllı!** 🎉
