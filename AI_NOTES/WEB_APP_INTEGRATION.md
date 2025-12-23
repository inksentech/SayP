# 🌐 Web Uygulamasına SayP Ayarları Entegrasyonu

## 📋 Genel Bakış

SayP'nin akıllı özelliklerini web uygulamanızın **Ayarlar** veya **Yönetim** bölümünden yönetebilirsiniz.

---

## 🎯 Entegrasyon Adımları

### 1. Menüye Yeni Sayfa Ekleyin

Web uygulamanızın admin/ayarlar menüsüne yeni bir menü öğesi ekleyin:

```
📁 Ayarlar
  ├── Genel Ayarlar
  ├── Kullanıcılar
  ├── Roller
  ├── 🆕 WhatsApp AI Özellikleri  ← YENİ
  └── Sistem Ayarları
```

### 2. Sayfa Rotası Ekleyin

**React Router örneği:**
```jsx
// routes.js veya App.js
import WhatsAppAISettings from './pages/WhatsAppAISettings';

<Route path="/settings/whatsapp-ai" element={<WhatsAppAISettings />} />
```

**Angular örneği:**
```typescript
// app-routing.module.ts
{
  path: 'settings/whatsapp-ai',
  component: WhatsAppAISettingsComponent,
  canActivate: [AuthGuard]
}
```

**Vue Router örneği:**
```javascript
// router/index.js
{
  path: '/settings/whatsapp-ai',
  name: 'WhatsAppAISettings',
  component: () => import('@/views/WhatsAppAISettings.vue'),
  meta: { requiresAuth: true }
}
```

### 3. API Service Oluşturun

Web uygulamanızın API servisine SayP ayarları için metodlar ekleyin:

**JavaScript/TypeScript:**
```typescript
// services/sayp-settings.service.ts
export class SayPSettingsService {
  private baseUrl = '/api/tenantsettings';

  async getSettings(tenantId: string) {
    const response = await fetch(`${this.baseUrl}/${tenantId}`, {
      headers: this.getHeaders()
    });
    return response.json();
  }

  async toggleFeature(tenantId: string, featureName: string, enabled: boolean, reason?: string) {
    const response = await fetch(`${this.baseUrl}/${tenantId}/toggle-feature`, {
      method: 'POST',
      headers: this.getHeaders(),
      body: JSON.stringify({ featureName, enabled, reason })
    });
    return response.json();
  }

  async toggleAllFeatures(tenantId: string, enabled: boolean, reason?: string) {
    const response = await fetch(`${this.baseUrl}/${tenantId}/toggle-all`, {
      method: 'POST',
      headers: this.getHeaders(),
      body: JSON.stringify({ enabled, reason })
    });
    return response.json();
  }

  async updateAdvancedSettings(tenantId: string, settings: any) {
    const response = await fetch(`${this.baseUrl}/${tenantId}/advanced`, {
      method: 'PUT',
      headers: this.getHeaders(),
      body: JSON.stringify(settings)
    });
    return response.json();
  }

  async getChangeHistory(tenantId: string, limit: number = 50) {
    const response = await fetch(`${this.baseUrl}/${tenantId}/history?limit=${limit}`, {
      headers: this.getHeaders()
    });
    return response.json();
  }

  // Admin only
  async getAllSettings() {
    const response = await fetch(this.baseUrl, {
      headers: this.getHeaders()
    });
    return response.json();
  }

  async bulkUpdate(tenantIds: string[], enabled: boolean, reason?: string) {
    const response = await fetch(`${this.baseUrl}/bulk-update`, {
      method: 'POST',
      headers: this.getHeaders(),
      body: JSON.stringify({ tenantIds, enabled, reason })
    });
    return response.json();
  }

  private getHeaders() {
    return {
      'Content-Type': 'application/json',
      'Authorization': `Bearer ${this.getAuthToken()}`
    };
  }

  private getAuthToken() {
    // Web uygulamanızın auth token'ını buradan alın
    return localStorage.getItem('authToken') || '';
  }
}
```

### 4. UI Component Ekleyin

Web uygulamanızın UI framework'üne uygun component oluşturun:

#### **Basit HTML/JavaScript Versiyonu** (Framework bağımsız)

```html
<!-- pages/whatsapp-ai-settings.html -->
<div class="settings-container">
  <div class="settings-header">
    <h1>WhatsApp AI Özellikleri</h1>
    <p>Akıllı mesajlaşma özelliklerini yönetin</p>
    <div class="actions">
      <button onclick="toggleAll(false)" class="btn-secondary">Tümünü Kapat</button>
      <button onclick="toggleAll(true)" class="btn-primary">Tümünü Aç</button>
    </div>
  </div>

  <div id="alert" class="alert hidden"></div>

  <!-- Özellikler Listesi -->
  <div class="settings-section">
    <h2>🧠 Temel AI Özellikleri</h2>
    <div id="coreFeatures"></div>
  </div>

  <div class="settings-section">
    <h2>💬 Konuşma Özellikleri</h2>
    <div id="conversationFeatures"></div>
  </div>

  <div class="settings-section">
    <h2>📈 Öğrenme Özellikleri</h2>
    <div id="learningFeatures"></div>
  </div>

  <div class="settings-section">
    <h2>⚙️ Gelişmiş Ayarlar</h2>
    <div id="advancedSettings"></div>
    <button onclick="saveAdvanced()" class="btn-primary">Kaydet</button>
  </div>
</div>

<script>
// Web uygulamanızın mevcut API service'ini kullanın
const settingsService = new SayPSettingsService();
let currentSettings = null;

async function loadSettings() {
  const tenantId = getCurrentTenantId(); // Mevcut sisteminizden
  currentSettings = await settingsService.getSettings(tenantId);
  renderFeatures();
}

function renderFeatures() {
  // Özellik listesini render et
  const features = [
    { key: 'enableSmartIntentClassifier', label: 'Smart Intent Classifier', desc: 'Akıllı intent sınıflandırma' },
    { key: 'enableEntityExtraction', label: 'Entity Extraction', desc: 'Otomatik varlık çıkarma' },
    { key: 'enableUserLearning', label: 'User Learning', desc: 'Kullanıcı bazlı öğrenme', premium: true },
    // ... diğer özellikler
  ];

  features.forEach(feature => {
    const html = `
      <div class="feature-item">
        <div class="feature-info">
          <strong>${feature.label}</strong>
          ${feature.premium ? '<span class="badge-premium">Premium</span>' : ''}
          <p>${feature.desc}</p>
        </div>
        <label class="switch">
          <input type="checkbox" 
                 ${currentSettings[feature.key] ? 'checked' : ''}
                 onchange="toggleFeature('${feature.key}', this.checked)">
          <span class="slider"></span>
        </label>
      </div>
    `;
    // Uygun container'a ekle
  });
}

async function toggleFeature(featureName, enabled) {
  const tenantId = getCurrentTenantId();
  await settingsService.toggleFeature(tenantId, featureName, enabled);
  showNotification('Ayar güncellendi', 'success');
  loadSettings();
}

async function toggleAll(enabled) {
  const tenantId = getCurrentTenantId();
  await settingsService.toggleAllFeatures(tenantId, enabled);
  showNotification('Tüm ayarlar güncellendi', 'success');
  loadSettings();
}

// Sayfa yüklendiğinde
document.addEventListener('DOMContentLoaded', loadSettings);
</script>
```

#### **React Component Versiyonu**

```jsx
// pages/WhatsAppAISettings.jsx
import React, { useState, useEffect } from 'react';
import { useAuth } from '../hooks/useAuth'; // Mevcut auth hook'unuz
import { SayPSettingsService } from '../services/sayp-settings.service';

export default function WhatsAppAISettings() {
  const { tenantId } = useAuth();
  const [settings, setSettings] = useState(null);
  const [loading, setLoading] = useState(true);
  const settingsService = new SayPSettingsService();

  useEffect(() => {
    loadSettings();
  }, []);

  const loadSettings = async () => {
    const data = await settingsService.getSettings(tenantId);
    setSettings(data);
    setLoading(false);
  };

  const toggleFeature = async (featureName, enabled) => {
    await settingsService.toggleFeature(tenantId, featureName, enabled);
    loadSettings();
  };

  if (loading) return <div>Yükleniyor...</div>;

  return (
    <div className="settings-page">
      <h1>WhatsApp AI Özellikleri</h1>
      
      <FeatureSection title="Temel AI Özellikleri">
        <FeatureToggle
          label="Smart Intent Classifier"
          description="Akıllı intent sınıflandırma"
          enabled={settings.enableSmartIntentClassifier}
          onToggle={(enabled) => toggleFeature('EnableSmartIntentClassifier', enabled)}
        />
        {/* Diğer özellikler */}
      </FeatureSection>
    </div>
  );
}
```

#### **Angular Component Versiyonu**

```typescript
// whatsapp-ai-settings.component.ts
import { Component, OnInit } from '@angular/core';
import { SayPSettingsService } from '../services/sayp-settings.service';
import { AuthService } from '../services/auth.service';

@Component({
  selector: 'app-whatsapp-ai-settings',
  templateUrl: './whatsapp-ai-settings.component.html'
})
export class WhatsAppAISettingsComponent implements OnInit {
  settings: any;
  loading = true;

  constructor(
    private settingsService: SayPSettingsService,
    private authService: AuthService
  ) {}

  ngOnInit() {
    this.loadSettings();
  }

  async loadSettings() {
    const tenantId = this.authService.getTenantId();
    this.settings = await this.settingsService.getSettings(tenantId);
    this.loading = false;
  }

  async toggleFeature(featureName: string, enabled: boolean) {
    const tenantId = this.authService.getTenantId();
    await this.settingsService.toggleFeature(tenantId, featureName, enabled);
    this.loadSettings();
  }
}
```

### 5. CSS Stilleri Ekleyin

Web uygulamanızın mevcut stil sistemine uygun şekilde:

```css
/* whatsapp-ai-settings.css */
.settings-container {
  max-width: 1200px;
  margin: 0 auto;
  padding: 2rem;
}

.settings-header {
  margin-bottom: 2rem;
}

.settings-section {
  background: white;
  border-radius: 8px;
  padding: 1.5rem;
  margin-bottom: 1.5rem;
  box-shadow: 0 1px 3px rgba(0,0,0,0.1);
}

.feature-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem;
  border: 1px solid #e5e7eb;
  border-radius: 8px;
  margin-bottom: 1rem;
}

.feature-info {
  flex: 1;
}

.badge-premium {
  display: inline-block;
  padding: 0.25rem 0.5rem;
  background: #3b82f6;
  color: white;
  border-radius: 4px;
  font-size: 0.75rem;
  margin-left: 0.5rem;
}

/* Toggle Switch */
.switch {
  position: relative;
  display: inline-block;
  width: 48px;
  height: 24px;
}

.switch input {
  opacity: 0;
  width: 0;
  height: 0;
}

.slider {
  position: absolute;
  cursor: pointer;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: #ccc;
  transition: .4s;
  border-radius: 24px;
}

.slider:before {
  position: absolute;
  content: "";
  height: 18px;
  width: 18px;
  left: 3px;
  bottom: 3px;
  background-color: white;
  transition: .4s;
  border-radius: 50%;
}

input:checked + .slider {
  background-color: #3b82f6;
}

input:checked + .slider:before {
  transform: translateX(24px);
}
```

---

## 🎨 UI Tasarım Önerileri

### Sayfa Yapısı

```
┌─────────────────────────────────────────────────────┐
│  WhatsApp AI Özellikleri                            │
│  Akıllı mesajlaşma özelliklerini yönetin            │
│                                                      │
│  [Tümünü Kapat]  [Tümünü Aç]                       │
├─────────────────────────────────────────────────────┤
│                                                      │
│  🧠 Temel AI Özellikleri                            │
│  ┌─────────────────────────────────────────────┐   │
│  │ Smart Intent Classifier              [ON]   │   │
│  │ Akıllı intent sınıflandırma                 │   │
│  └─────────────────────────────────────────────┘   │
│  ┌─────────────────────────────────────────────┐   │
│  │ Entity Extraction                    [ON]   │   │
│  │ Otomatik varlık çıkarma                     │   │
│  └─────────────────────────────────────────────┘   │
│                                                      │
│  📈 Öğrenme Özellikleri                             │
│  ┌─────────────────────────────────────────────┐   │
│  │ User Learning [Premium]              [ON]   │   │
│  │ Kullanıcı bazlı öğrenme                     │   │
│  └─────────────────────────────────────────────┘   │
│                                                      │
│  ⚙️ Gelişmiş Ayarlar                                │
│  ┌─────────────────────────────────────────────┐   │
│  │ Min Confidence: [0.5]                       │   │
│  │ High Confidence: [0.8]                      │   │
│  │ [Kaydet]                                    │   │
│  └─────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────┘
```

---

## 🔐 Yetkilendirme

Web uygulamanızın mevcut yetkilendirme sistemini kullanın:

```typescript
// Örnek: Role-based access
if (user.role === 'Admin' || user.role === 'SuperAdmin') {
  // Tüm tenant'ları göster
  showAllTenants();
} else {
  // Sadece kendi tenant'ını göster
  showOwnTenant(user.tenantId);
}
```

---

## 📱 Responsive Tasarım

Mobil uyumlu olması için:

```css
@media (max-width: 768px) {
  .feature-item {
    flex-direction: column;
    align-items: flex-start;
  }
  
  .switch {
    margin-top: 1rem;
  }
}
```

---

## 🔔 Bildirimler

Web uygulamanızın mevcut bildirim sistemini kullanın:

```javascript
// Başarılı güncelleme
showNotification('Ayar güncellendi', 'success');

// Hata durumu
showNotification('Bir hata oluştu', 'error');

// Bilgi
showNotification('Değişiklikler kaydedildi', 'info');
```

---

## 📊 Özellik Listesi

### Yönetilebilir Özellikler

```javascript
const features = {
  core: [
    { key: 'enableSmartIntentClassifier', label: 'Smart Intent Classifier', icon: '🧠' },
    { key: 'enableEntityExtraction', label: 'Entity Extraction', icon: '🔍' },
    { key: 'enableContextAware', label: 'Context-Aware Processing', icon: '🎯' },
    { key: 'enableSlotFilling', label: 'Slot Filling', icon: '📝' }
  ],
  conversation: [
    { key: 'enableMultiTurnDialogue', label: 'Multi-Turn Dialogue', icon: '💬' },
    { key: 'enableIntelligentFallback', label: 'Intelligent Fallback', icon: '🔄' }
  ],
  learning: [
    { key: 'enableUserLearning', label: 'User Learning', icon: '📈', premium: true },
    { key: 'enableIntentDiscovery', label: 'Intent Discovery', icon: '🔎' },
    { key: 'enablePatternLearning', label: 'Pattern Learning', icon: '🧩' }
  ],
  analytics: [
    { key: 'enableAnalytics', label: 'Analytics & Monitoring', icon: '📊' }
  ]
};
```

---

## 🎯 Kullanım Örnekleri

### Örnek 1: Özellik Açma
```javascript
// Kullanıcı "User Learning" özelliğini açtı
await settingsService.toggleFeature(
  tenantId,
  'EnableUserLearning',
  true,
  'Premium plan upgrade'
);
```

### Örnek 2: Toplu Kapatma
```javascript
// Bakım modu için tüm özellikleri kapat
await settingsService.toggleAllFeatures(
  tenantId,
  false,
  'Sistem bakımı - 30 dakika'
);
```

### Örnek 3: Gelişmiş Ayarlar
```javascript
// Confidence threshold'u artır
await settingsService.updateAdvancedSettings(tenantId, {
  minConfidenceThreshold: 0.7,
  highConfidenceThreshold: 0.9
});
```

---

## ✅ Checklist

- [ ] Menüye "WhatsApp AI Özellikleri" sayfası ekle
- [ ] Rota tanımla
- [ ] API service oluştur
- [ ] UI component ekle
- [ ] CSS stilleri ekle
- [ ] Yetkilendirme kontrolü ekle
- [ ] Bildirim sistemi entegre et
- [ ] Test et

---

## 🚀 Deployment

1. **Backend zaten hazır** - Migration çalıştırın:
```bash
dotnet ef database update
```

2. **Frontend'i web uygulamanıza ekleyin** - Yukarıdaki adımları takip edin

3. **Test edin**:
- Ayarlar sayfasına gidin
- Özellikleri aç/kapa yapın
- Değişikliklerin loglandığını kontrol edin

---

## 💡 Öneriler

1. **Mevcut UI framework'ünüzü kullanın** - Bootstrap, Material UI, Ant Design vb.
2. **Mevcut state management'ı kullanın** - Redux, Vuex, NgRx vb.
3. **Mevcut notification sistemini kullanın** - Toastr, Snackbar vb.
4. **Mevcut form validation'ı kullanın** - Formik, React Hook Form vb.

---

## 🎉 Sonuç

Bu şekilde:
- ✅ SayP'ye özel frontend yok
- ✅ Mevcut web uygulamanıza entegre
- ✅ Tek bir yerden yönetim
- ✅ Mevcut auth/UI sisteminizi kullanıyor
- ✅ Backend API'ler hazır ve çalışıyor

**Web uygulamanızın "Ayarlar" bölümüne yeni bir sayfa ekleyerek tüm AI özelliklerini yönetebilirsiniz!** 🚀
