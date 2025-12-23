# 🔧 Port 5000 Sorunu - Çözüldü!

## ❌ Problem

```
System.Net.Sockets.SocketException (10013): 
An attempt was made to access a socket in a way forbidden by its access permissions.
Failed to bind to address http://localhost:5000
```

## 🎯 Neden Oluştu?

### Windows Port Reservation
- Windows 10/11'de **port 5000** reserved olabilir
- Hyper-V veya diğer Windows servisleri kullanıyor olabilir
- .NET 6+ default port 5000 kullanır

### Kontrol
```bash
netstat -ano | findstr :5000
# Eğer boş dönerse: Windows reserved
# Eğer PID görünürse: Başka process kullanıyor
```

## ✅ Çözüm

### Seçenek 1: Port 5100 Kullan (Önerilen)
```bash
dotnet run --urls "http://localhost:5100"
```

✅ **Bu çalıştı!** Backend şimdi port 5100'de çalışıyor.

### Seçenek 2: launchSettings.json Güncelle
```json
{
  "profiles": {
    "http": {
      "applicationUrl": "http://localhost:5100"  // 5000 → 5100
    }
  }
}
```

### Seçenek 3: Windows Reserved Ports Temizle
```powershell
# Admin PowerShell
netsh int ipv4 show excludedportrange protocol=tcp

# Eğer 5000 reserved ise:
net stop winnat
net start winnat
```

## 🚀 Şu Anda Çalışan Durum

### Backend
```
✅ Port: 5100
✅ SignalR Hub: /chatHub
✅ Client bağlantıları: Başarılı
✅ CORS: Configured
```

### Web Chat
```
✅ Port: 3000
✅ SignalR: Connected
✅ Backend URL: http://localhost:5100 (otomatik proxy)
```

## 📝 Kalıcı Çözüm

### appsettings.json
```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://localhost:5100"
      }
    }
  }
}
```

### Program.cs (Alternatif)
```csharp
builder.WebHost.UseUrls("http://localhost:5100");
```

## 🎯 Test

### Backend Çalışıyor mu?
```bash
curl http://localhost:5100/health
# ✅ {"status":"healthy",...}
```

### SignalR Çalışıyor mu?
```bash
curl http://localhost:5100/chatHub/negotiate
# ✅ SignalR negotiate response
```

### Web Chat Bağlanıyor mu?
```
Browser: http://localhost:3000
✅ Connection status: Connected (yeşil nokta)
```

## 📊 Port Kullanımı

| Servis | Port | Durum |
|--------|------|-------|
| **SayP API** | 5100 | ✅ Çalışıyor |
| **Web Chat** | 3000 | ✅ Çalışıyor |
| **Backend ERP** | 5245 | Gerektiğinde |
| **Port 5000** | - | ❌ Reserved/Kullanılamaz |

## 🐛 Hala Sorun Varsa

### 1. Process'i Kontrol Et
```bash
netstat -ano | findstr :5100
# Eğer kullanımdaysa:
taskkill /PID <PID> /F
```

### 2. Farklı Port Dene
```bash
dotnet run --urls "http://localhost:5200"
```

### 3. Admin Olarak Çalıştır
```bash
# PowerShell Admin olarak aç
cd SayP.Api
dotnet run
```

## ✅ Sonuç

**Port sorunu çözüldü!** 🎉

```
✅ Backend: http://localhost:5100
✅ SignalR: Connected
✅ Web Chat: Working
✅ Ready to test!
```

**Artık mesaj gönderebilirsiniz:** "Kod için şablon oluşturalım" 🚀
