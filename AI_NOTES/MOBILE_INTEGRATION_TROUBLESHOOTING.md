# SayP Mobile Entegrasyonu: Sorunlar ve Çözümler

## 📌 Genel Bakış

Bu doküman, SayP AI sisteminin mobile uygulamaya entegrasyonu sırasında karşılaşılan sorunları ve çözümlerini içerir. WhatsApp entegrasyonunda benzer sorunlarla karşılaşıldığında referans olarak kullanılabilir.

---

## 🔴 Sorun 1: Network Error - Backend Bağlantı Hatası

### **Belirti:**
```
Network Error
ERR_CONNECTION_REFUSED on http://localhost:5246
```

### **Kök Neden:**
- SayP backend çalışmıyor
- Yanlış port numarası kullanılıyor
- API URL'i yanlış yapılandırılmış

### **Çözüm:**
```typescript
// mobile/src/services/saypApi.ts
const SAYP_API_URL = 'http://localhost:5000/api'; // Doğru port
```

**Kontrol Listesi:**
- [ ] Backend çalışıyor mu? (`dotnet run`)
- [ ] Port numarası doğru mu? (launchSettings.json'da kontrol et)
- [ ] API URL mobil tarafta doğru mu?

---

## 🔴 Sorun 2: Database Schema Mismatch

### **Belirti:**
```
Npgsql.PostgresException: 42703: column m.MediaUrl does not exist
Npgsql.PostgresException: 42804: column "Status" cannot be cast automatically to type integer
```

### **Kök Neden:**
- Entity Framework model'i ile database schema'sı uyumsuz
- Eksik migration veya migration uygulanmamış
- Column type mismatch (text → integer)

### **Çözüm:**

**1. Migration Oluştur:**
```bash
cd sayp/SayP.Infrastructure
dotnet ef migrations add AddMediaUrlToMessages --startup-project ../SayP.Api
```

**2. Type Casting Sorunu İçin:**
```csharp
// Migration dosyasında USING clause kullan
migrationBuilder.Sql(@"
    ALTER TABLE ""Messages"" 
    ALTER COLUMN ""Status"" TYPE integer 
    USING CASE 
        WHEN ""Status"" IS NULL THEN 0
        WHEN ""Status"" ~ '^[0-9]+$' THEN ""Status""::integer
        ELSE 0
    END;
");
```

**3. Migration Uygula:**
```bash
dotnet ef database update --project ../SayP.Infrastructure --startup-project .
```

**Önemli Notlar:**
- ❌ Manuel SQL yerine EF migrations kullan
- ✅ Type conversion için `USING` clause kullan
- ✅ Migration'dan önce backend'i durdur (DLL lock sorunu)

---

## 🔴 Sorun 3: JSON Circular Reference

### **Belirti:**
```json
{
  "type": "https://httpstatuses.com/500",
  "title": "Internal Server Error",
  "detail": "A possible object cycle was detected. Path: $.Conversation.Messages.Conversation.Messages..."
}
```

### **Kök Neden:**
- Navigation property'ler sonsuz döngü oluşturuyor
- `Message` → `Conversation` → `Messages` → `Conversation` → ...

### **Çözüm:**

**1. GetMessagesAsync'de Navigation Property Dahil Etme:**
```csharp
public async Task<List<Message>> GetMessagesAsync(string conversationId)
{
    return await _context.Messages
        .AsNoTracking()
        .Where(m => m.ConversationId == convId)
        .Select(m => new Message
        {
            // Tüm property'leri manuel map et
            // Conversation = null (dahil etme!)
        })
        .ToListAsync();
}
```

**2. SendMessageAsync'de Conversation'ı Temizle:**
```csharp
_context.Messages.Add(aiMessage);
await _context.SaveChangesAsync();

// Return message without Conversation
aiMessage.Conversation = null;
return aiMessage;
```

**3. GetAllConversationsAsync'de Messages Dahil Etme:**
```csharp
return await _context.Conversations
    .AsNoTracking()
    .Select(c => new Conversation
    {
        // Messages = null (dahil etme!)
    })
    .ToListAsync();
```

**Best Practices:**
- ✅ API response'larda navigation property'leri temizle
- ✅ `.AsNoTracking()` kullan
- ✅ `.Select()` ile manuel projection yap
- ❌ `.Include()` kullanma (circular reference riski)

---

## 🔴 Sorun 4: Gemini API MAX_TOKENS Hatası

### **Belirti:**
```json
{
  "finishReason": "MAX_TOKENS",
  "thoughtsTokenCount": 2047
}
```

### **Kök Neden:**
- Gemini "thinking mode" çok fazla token kullanıyor
- `maxOutputTokens` limiti aşılıyor

### **Çözüm:**
```csharp
// GeminiProvider.cs
generationConfig = new
{
    temperature = 0.7,
    maxOutputTokens = 1024,  // 2048'den düşürüldü
    responseModalities = new[] { "TEXT" }  // Thinking mode kapalı
}
```

**Öneriler:**
- ✅ `maxOutputTokens` 1024 veya daha düşük
- ✅ `responseModalities: ["TEXT"]` ekle
- ✅ Context'i çok uzun tutma

---

## 🔴 Sorun 5: AI Response Field Mismatch

### **Belirti:**
```json
// Gemini döndürüyor:
{
  "commandType": "None",
  "response": "Merhaba! Size nasıl yardımcı olabilirim?"
}

// Ama kod arıyor:
aiResponse.ConfirmationMessage  // null!
```

### **Kök Neden:**
- Gemini farklı field adı kullanıyor (`response` vs `confirmationMessage`)
- JSON parse ediliyor ama field'lar map edilmiyor

### **Çözüm:**
```csharp
// GeminiProvider.cs - ParseCommandFromResponse
var jsonDoc = JsonDocument.Parse(cleanResponse);
var root = jsonDoc.RootElement;

var commandResult = new AICommandResult
{
    Success = true,
    CommandJson = cleanResponse,
    CommandType = root.TryGetProperty("commandType", out var cmdType) ? cmdType.GetString() : null,
    ConfirmationMessage = root.TryGetProperty("confirmationMessage", out var confMsg) 
        ? confMsg.GetString() 
        : root.TryGetProperty("response", out var respMsg)  // Fallback!
            ? respMsg.GetString() 
            : null,
    ErrorMessage = root.TryGetProperty("errorMessage", out var errMsg) ? errMsg.GetString() : null,
    RequiresConfirmation = root.TryGetProperty("requiresConfirmation", out var reqConf) && reqConf.GetBoolean(),
    Confidence = root.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.0
};
```

**Best Practices:**
- ✅ Fallback field'lar kullan
- ✅ `TryGetProperty` ile güvenli parse
- ✅ Tüm olası field adlarını kontrol et

---

## 🔴 Sorun 6: UI Message Direction Hatası

### **Belirti:**
- Tüm mesajlar sol tarafta (AI tarafı) görünüyor
- Kullanıcı mesajları sağda olmalı

### **Kök Neden:**
- Yanlış field kontrol ediliyor (`item.sender` vs `item.direction`)
- Backend farklı field kullanıyor

### **Çözüm:**
```typescript
// SayPScreen.tsx
const renderMessageItem = ({ item }: any) => {
  // Backend'den gelen field'ı kullan
  const isUser = item.direction === 1 || item.from === 'mobile-user';
  
  return (
    <View style={[
      styles.messageContainer, 
      isUser ? styles.messageUser : styles.messageAI
    ]}>
      {/* ... */}
    </View>
  );
};
```

**CSS Düzeltmesi:**
```typescript
messageUser: {
  justifyContent: 'flex-end',
  alignSelf: 'flex-end',  // ← Önemli!
},
messageAI: {
  justifyContent: 'flex-start',
  alignSelf: 'flex-start',  // ← Önemli!
},
```

**Enum Değerleri:**
- `direction: 0` = Incoming (AI)
- `direction: 1` = Outgoing (User)

---

## 🔴 Sorun 7: Real-time Message Update Yok

### **Belirti:**
- Mesaj gönderdikten sonra sayfa yenilenene kadar görünmüyor
- AI yanıtı manuel refresh gerektiriyor

### **Kök Neden:**
- `sendMessage` sonrası messages yeniden yüklenmiyor

### **Çözüm:**
```typescript
// SayPScreen.tsx - handleSendMessage
await dispatch(sendMessage({
  conversationId: currentConversationId,
  content: message,
  type: 'text',
}));

// Mesajları yeniden yükle
await loadMessages(currentConversationId);

// Scroll to bottom
setTimeout(() => {
  flatListRef.current?.scrollToEnd({ animated: true });
}, 200);
```

**Alternatif:** WebSocket kullan (gerçek zamanlı)

---

## 🔴 Sorun 8: AI Context Kaybı

### **Belirti:**
```
User: "Kitap 100 TL"
AI: "Kitap oluşturmak istediğinizi anladım. Onaylıyor musunuz?"
User: "Evet"
AI: "Onayınızı anladım. Ancak bekleyen bir işlem bulunmuyor."  ❌
```

### **Kök Neden:**
- AI'ya conversation context gönderilmiyor
- Her mesaj bağımsız işleniyor

### **Çözüm:**
```csharp
// ConversationManager.cs - SendMessageAsync
_context.Messages.Add(userMessage);
await _context.SaveChangesAsync();

// Build conversation context for AI
var contextString = await BuildConversationContextAsync(convId, CancellationToken.None);

// Generate AI response WITH context
var aiResponse = await _aiProvider.ExtractCommandAsync(content, contextString, CancellationToken.None);
```

**BuildConversationContextAsync:**
```csharp
private async Task<string> BuildConversationContextAsync(Guid conversationId, CancellationToken cancellationToken)
{
    var messages = await _context.Messages
        .Where(m => m.ConversationId == conversationId)
        .OrderBy(m => m.Timestamp)
        .Take(10)  // Son 10 mesaj
        .ToListAsync(cancellationToken);

    var contextBuilder = new StringBuilder();
    foreach (var msg in messages)
    {
        var role = msg.Direction == MessageDirection.Incoming ? "AI" : "User";
        contextBuilder.AppendLine($"{role}: {msg.Content}");
    }
    return contextBuilder.ToString();
}
```

---

## 🔴 Sorun 9: Command Execution Eksik

### **Belirti:**
```json
// AI doğru komut çıkarıyor:
{
  "commandType": "CreateProduct",
  "requiresConfirmation": false,
  "command": { "name": "Kitap", "price": 100 }
}

// Ama ürün oluşturulmuyor! ❌
```

### **Kök Neden:**
- Mobile API'de command execution logic'i yok
- Sadece AI yanıtı gösteriliyor, komut çalıştırılmıyor

### **Çözüm:**
```csharp
// ConversationManager.cs - SendMessageAsync
var aiResponse = await _aiProvider.ExtractCommandAsync(content, contextString, CancellationToken.None);

// If command doesn't require confirmation, execute immediately
if (aiResponse.Success && !string.IsNullOrEmpty(aiResponse.CommandType) && 
    aiResponse.CommandType != "None" && !aiResponse.RequiresConfirmation)
{
    try
    {
        var commandType = Enum.Parse<Domain.Enums.CommandType>(aiResponse.CommandType);
        var executionResult = await _commandExecutor.ExecuteAsync(
            commandType,
            aiResponse.CommandJson ?? "{}",
            conversation.TenantId,
            conversation.CompanyId,
            CancellationToken.None
        );

        if (executionResult.Success)
        {
            aiContent = executionResult.Message ?? "İşlem başarıyla tamamlandı.";
        }
        else
        {
            aiContent = executionResult.Message ?? "İşlem başarısız oldu.";
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error executing command");
        aiContent = "Komut çalıştırılırken bir hata oluştu.";
    }
}
```

**Önemli:**
- ✅ `requiresConfirmation: false` ise hemen çalıştır
- ✅ `requiresConfirmation: true` ise onay bekle
- ✅ Execution result'ı kullanıcıya göster

---

## 🔴 Sorun 10: Tenant ID Yönetimi

### **Belirti:**
- Tüm kullanıcılar default tenant'ta ürün oluşturuyor
- Multi-tenancy çalışmıyor

### **Kök Neden:**
- Conversation oluşturulurken hardcoded tenant ID kullanılıyor
- User'ın tenant'ı kullanılmıyor

### **Çözüm:**

**1. Controller'da Tenant Al:**
```csharp
// ConversationsController.cs
var tenantIdClaim = User.FindFirst("tenantId")?.Value;
Guid? tenantId = null;
if (!string.IsNullOrEmpty(tenantIdClaim) && Guid.TryParse(tenantIdClaim, out var parsedTenantId))
{
    tenantId = parsedTenantId;
}

var message = await _conversationManager.SendMessageAsync(id, request.Content, request.Type ?? "text", tenantId);
```

**2. ConversationManager'da Kullan:**
```csharp
public async Task<Message> SendMessageAsync(string conversationId, string content, string type, Guid? tenantId = null)
{
    var conversation = await _context.Conversations.FindAsync(convId);
    
    // Update tenant if provided
    if (tenantId.HasValue && conversation.TenantId != tenantId.Value)
    {
        conversation.TenantId = tenantId.Value;
    }
    
    // Command execution'da kullan
    var executionResult = await _commandExecutor.ExecuteAsync(
        commandType,
        aiResponse.CommandJson ?? "{}",
        conversation.TenantId,  // ← User'ın tenant'ı
        conversation.CompanyId,
        CancellationToken.None
    );
}
```

**3. CreateConversation'da Default:**
```csharp
public async Task<Conversation> CreateConversationAsync(string phoneNumber, string title, Guid? tenantId = null)
{
    // Use provided tenant or default
    var effectiveTenantId = tenantId ?? Guid.Parse("00000000-0000-0000-0000-000000000001");
    
    var conversation = new Conversation
    {
        TenantId = effectiveTenantId,
        // ...
    };
}
```

---

## 📊 Hata Ayıklama Checklist

### **Backend Sorunları:**
- [ ] Backend çalışıyor mu? (`dotnet run`)
- [ ] Database migration'ları uygulandı mı?
- [ ] Gemini API key doğru mu?
- [ ] Log'larda hata var mı?

### **API İletişimi:**
- [ ] API URL doğru mu?
- [ ] Port numarası doğru mu?
- [ ] CORS ayarları doğru mu?
- [ ] Authentication token geçerli mi?

### **Database:**
- [ ] Schema güncel mi?
- [ ] Migration'lar uygulandı mı?
- [ ] Column type'lar doğru mu?
- [ ] Tenant ID doğru mu?

### **AI Response:**
- [ ] Gemini API yanıt veriyor mu?
- [ ] JSON parse ediliyor mu?
- [ ] Field'lar map ediliyor mu?
- [ ] Context gönderiliyor mu?

### **UI:**
- [ ] Message direction doğru mu?
- [ ] Real-time update çalışıyor mu?
- [ ] Scroll to bottom çalışıyor mu?
- [ ] Loading state gösteriliyor mu?

---

## 🎯 WhatsApp Entegrasyonu İçin Öneriler

WhatsApp entegrasyonunda benzer sorunlarla karşılaşabilirsiniz:

### **1. Webhook Sorunları:**
- Network error → Webhook URL'i kontrol et
- Message not received → Webhook verification kontrol et

### **2. Message Format:**
- Circular reference → WhatsApp message object'i temizle
- Field mismatch → WhatsApp API field'larını map et

### **3. Context Management:**
- Context loss → WhatsApp conversation ID ile context yönet
- Token limit → Message history'yi sınırla

### **4. Command Execution:**
- Commands not running → Webhook handler'da execution ekle
- Tenant issues → WhatsApp phone number'dan tenant belirle

### **5. Rate Limiting:**
- API limits → Rate limiting middleware ekle
- Queue system → Background job kullan

---

## 📚 Referanslar

- **Entity Framework Migrations:** https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/
- **Gemini API Docs:** https://ai.google.dev/docs
- **ASP.NET Core Multi-tenancy:** https://learn.microsoft.com/en-us/aspnet/core/fundamentals/multi-tenancy
- **React Native Best Practices:** https://reactnative.dev/docs/performance

---

## 🔄 Versiyon Geçmişi

| Tarih | Versiyon | Değişiklikler |
|-------|----------|---------------|
| 2025-10-20 | 1.0 | İlk versiyon - Mobile entegrasyon sorunları |

---

**Not:** Bu doküman sürekli güncellenmektedir. Yeni sorunlar ve çözümler eklenmeye devam edecektir.
