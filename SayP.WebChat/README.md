# 💬 SayP Web Chat Simulator

WhatsApp akışını birebir simüle eden web tabanlı chat uygulaması.

## 🎯 Özellikler

- ✅ **WhatsApp Benzeri UI** - Tanıdık kullanıcı deneyimi
- ✅ **Real-time Messaging** - SignalR ile anlık iletişim
- ✅ **Generic AI Pipeline** - Gerçek SayP AI'sını kullanır
- ✅ **Multi-turn Conversations** - Çoklu diyalog desteği
- ✅ **Typing Indicators** - "Yazıyor..." göstergesi
- ✅ **Tenant/Company Selection** - Multi-tenant test
- ✅ **Message History** - Konuşma geçmişi
- ✅ **Settings Panel** - Kolay yapılandırma

## 🚀 Kurulum

### 1. Dependencies Yükle
```bash
cd SayP.WebChat
npm install
```

### 2. Backend'i Başlat
```bash
cd ../SayP.Api
dotnet run
```

Backend şu adreste çalışacak: `http://localhost:5100`

### 3. Web Chat'i Başlat
```bash
cd ../SayP.WebChat
npm run dev
```

Web Chat şu adreste açılacak: `http://localhost:3000`

## 📱 Kullanım

### Temel Kullanım

1. **Tarayıcıda Aç**: `http://localhost:3000`
2. **Settings'i Aç**: Sağ üstteki ⚙️ ikonuna tıkla
3. **Ayarları Yap**:
   - Phone Number: `+905551234567`
   - Backend URL: `http://localhost:5245`
   - Tenant ID: (Opsiyonel)
   - Company ID: (Opsiyonel)
4. **Mesaj Gönder**: "Kod için şablon oluşturalım"

### Örnek Konuşmalar

#### 1. Kod Şablonu Oluşturma
```
👤 "Kod için şablon oluşturalım"
🤖 "Anladım! Kod şablonu oluşturuyoruz. Şablon adı nedir?"
👤 "React Component"
🤖 "Entity tipi nedir?"
👤 "Component"
🤖 "✅ Başarıyla oluşturuldu!"
```

#### 2. Ürün Ekleme
```
👤 "Bir ürün eklemek istiyorum"
🤖 "Ürün adı nedir?"
👤 "Laptop"
🤖 "Fiyat nedir?"
👤 "15000"
🤖 "✅ Laptop ürünü eklendi!"
```

#### 3. Randevu Sorgulama
```
👤 "Bugün hangi randevularım var?"
🤖 "Bugün 3 randevunuz var:
     1. 10:00 - Ahmet Yılmaz
     2. 14:00 - Mehmet Demir
     3. 16:30 - Ayşe Kaya"
```

## 🔧 Yapılandırma

### Settings Panel

| Alan | Açıklama | Örnek |
|------|----------|-------|
| **Phone Number** | Simüle edilecek telefon numarası | +905551234567 |
| **Backend URL** | ERP backend URL'i | http://localhost:5245 |
| **Tenant ID** | Tenant kimliği (opsiyonel) | guid veya boş |
| **Company ID** | Şirket kimliği (opsiyonel) | guid veya boş |

### Environment Variables

Backend `.env` dosyasında:
```env
BACKEND_API_URL=http://localhost:5245
SAYP_API_KEY=your-api-key
REDIS_CONNECTION_STRING=localhost:6379
```

## 🎨 UI Özellikleri

### WhatsApp Benzeri Tasarım
- ✅ Yeşil renk teması (#25D366)
- ✅ Mesaj balonları (sağ: kullanıcı, sol: bot)
- ✅ Zaman damgaları
- ✅ Typing indicators (3 nokta animasyonu)
- ✅ Connection status (bağlı/bağlı değil)

### Responsive Design
- ✅ Desktop optimized
- ✅ Mobile friendly
- ✅ Tablet support

## 🔌 SignalR Integration

### Hub Endpoint
```
http://localhost:5100/chatHub
```

### Events

#### Client → Server
```javascript
connection.invoke('SendMessage', {
  phoneNumber: '+905551234567',
  message: 'Kod için şablon oluşturalım',
  tenantId: 'guid-or-null',
  companyId: 'guid-or-null',
  backendUrl: 'http://localhost:5245'
})
```

#### Server → Client
```javascript
// Mesaj alındı
connection.on('ReceiveMessage', (message) => {
  console.log(message.text)
})

// Yazıyor göstergesi
connection.on('TypingIndicator', (isTyping) => {
  console.log(isTyping ? 'Bot yazıyor...' : 'Bot durdu')
})
```

## 🧪 Test Senaryoları

### 1. Discovery Test
```
Mesaj: "Hangi komutları kullanabilirim?"
Beklenen: Backend endpoint'lerinin listesi
```

### 2. Natural Language Test
```
Mesaj: "Kod için şablon oluşturalım"
Beklenen: Intent: create_code_template, Confidence: >0.85
```

### 3. Multi-turn Test
```
1. "Kod şablonu oluştur"
2. "React Component" (ad)
3. "Component" (entity type)
Beklenen: Başarılı oluşturma
```

### 4. Error Handling Test
```
Mesaj: "asdfghjkl" (anlamsız)
Beklenen: Fallback response veya clarification
```

## 🐛 Troubleshooting

### Problem: "Not connected to chat hub"
**Çözüm**: Backend'in çalıştığından emin olun
```bash
curl http://localhost:5100/health
```

### Problem: "CORS error"
**Çözüm**: Program.cs'de CORS policy kontrol edin
```csharp
policy.WithOrigins("http://localhost:3000")
```

### Problem: Mesaj gönderilmiyor
**Çözüm**: 
1. Browser console'u kontrol edin
2. SignalR connection durumunu kontrol edin
3. Backend logs'u kontrol edin

### Problem: Backend bulunamıyor
**Çözüm**: Settings'de Backend URL'i kontrol edin
```
✅ http://localhost:5245
❌ https://localhost:5245 (SSL varsa)
```

## 📊 Performans

| Metrik | Değer |
|--------|-------|
| **Connection Time** | <500ms |
| **Message Send** | <100ms |
| **AI Response** | 500-2000ms |
| **UI Update** | <50ms |

## 🎯 WhatsApp vs Web Chat

| Özellik | WhatsApp | Web Chat |
|---------|----------|----------|
| **UI** | WhatsApp Cloud API | React UI |
| **Transport** | Webhook | SignalR |
| **Auth** | Phone Number | Settings Panel |
| **Pipeline** | GenericWhatsAppHandler | ✅ Aynı |
| **AI** | DynamicIntentMapper | ✅ Aynı |
| **Execution** | GenericCommandExecutor | ✅ Aynı |

**Sonuç**: %100 aynı AI pipeline, farklı UI!

## 🚀 Production Deployment

### Build
```bash
npm run build
```

### Preview
```bash
npm run preview
```

### Deploy
```bash
# Netlify, Vercel, veya static hosting
# dist/ klasörünü deploy edin
```

## 📝 Geliştirme Notları

### Teknolojiler
- **React 18** - UI framework
- **Vite** - Build tool
- **TailwindCSS** - Styling
- **SignalR** - Real-time communication
- **Lucide React** - Icons

### Dosya Yapısı
```
SayP.WebChat/
├── src/
│   ├── App.jsx           # Ana component
│   ├── App.css           # Animasyonlar
│   ├── index.css         # TailwindCSS
│   ├── main.jsx          # Entry point
│   └── services/
│       └── ChatService.js # SignalR client
├── index.html            # HTML template
├── package.json          # Dependencies
├── vite.config.js        # Vite config
├── tailwind.config.js    # Tailwind config
└── README.md             # Bu dosya
```

## 🎉 Sonuç

Web Chat Simulator ile:
- ✅ WhatsApp olmadan test edebilirsiniz
- ✅ Gerçek AI pipeline'ı kullanırsınız
- ✅ Hızlı iterasyon yapabilirsiniz
- ✅ Debug kolaydır
- ✅ Demo için mükemmeldir

**WhatsApp'ın tüm gücü, web'in rahatlığıyla! 🚀**

---

**Version**: 1.0.0  
**Last Updated**: 2025-01-18  
**Status**: ✅ Production Ready
