-- ============================================
-- SayP - Tenant Settings Seed Data
-- ============================================
-- Bu script örnek tenant ve user kayıtları oluşturur
-- Mevcut sisteminize göre GUID'leri değiştirin!
-- ============================================

-- ============================================
-- 1. ÖRNEK TENANT KAYITLARI
-- ============================================
-- Not: Eğer sisteminizde zaten tenant'lar varsa bu adımı atlayın
-- Mevcut tenant ID'lerinizi kullanın

-- Örnek Tenant 1: Acme Corp
-- INSERT INTO "Tenants" ("Id", "Name", "CreatedAt", "IsActive")
-- VALUES 
--   ('11111111-1111-1111-1111-111111111111', 'Acme Corp', NOW(), true);

-- Örnek Tenant 2: Beta Company  
-- INSERT INTO "Tenants" ("Id", "Name", "CreatedAt", "IsActive")
-- VALUES 
--   ('22222222-2222-2222-2222-222222222222', 'Beta Company', NOW(), true);


-- ============================================
-- 2. ÖRNEK USER KAYITLARI
-- ============================================
-- Not: Eğer sisteminizde zaten user'lar varsa bu adımı atlayın
-- Mevcut user ID'lerinizi kullanın

-- Örnek Admin User 1 (Acme Corp)
-- INSERT INTO "Users" ("Id", "Email", "Name", "TenantId", "Role", "CreatedAt", "IsActive")
-- VALUES 
--   ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 'admin@acme.com', 'Admin User', 
--    '11111111-1111-1111-1111-111111111111', 'Admin', NOW(), true);

-- Örnek Admin User 2 (Beta Company)
-- INSERT INTO "Users" ("Id", "Email", "Name", "TenantId", "Role", "CreatedAt", "IsActive")
-- VALUES 
--   ('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 'admin@beta.com', 'Beta Admin', 
--    '22222222-2222-2222-2222-222222222222', 'Admin', NOW(), true);


-- ============================================
-- 3. TENANT SETTINGS (ZORUNLU)
-- ============================================
-- Her tenant için default ayarlar oluşturun
-- GUID'leri kendi tenant ID'lerinizle değiştirin!

-- Tenant 1: Acme Corp - Tüm özellikler aktif
INSERT INTO "TenantSettings" (
    "Id", 
    "TenantId", 
    "TenantName",
    -- AI Özellikleri (10 adet)
    "EnableSmartIntentClassifier",
    "EnableEntityExtraction",
    "EnableMultiTurnDialogue",
    "EnableIntelligentFallback",
    "EnableUserLearning",
    "EnableIntentDiscovery",
    "EnablePatternLearning",
    "EnableContextAware",
    "EnableSlotFilling",
    "EnableAnalytics",
    -- Gelişmiş Parametreler (6 adet)
    "MinConfidenceThreshold",
    "HighConfidenceThreshold",
    "DialogueTimeoutMinutes",
    "MaxDialogueAttempts",
    "ProfileAnalysisInterval",
    "BehaviorLogRetentionDays",
    -- Metadata
    "CreatedByUserId",
    "UpdatedByUserId",
    "CreatedAt",
    "UpdatedAt",
    "IsActive",
    "Notes"
) VALUES (
    gen_random_uuid(),                                    -- Id
    '00000000-0000-0000-0000-000000000001',              -- TenantId (DEĞİŞTİRİN!)
    'Default Tenant',                                          -- TenantName
    -- AI Özellikleri - Tümü aktif
    true,  -- EnableSmartIntentClassifier
    true,  -- EnableEntityExtraction
    true,  -- EnableMultiTurnDialogue
    true,  -- EnableIntelligentFallback
    true,  -- EnableUserLearning (Premium)
    true,  -- EnableIntentDiscovery
    true,  -- EnablePatternLearning
    true,  -- EnableContextAware
    true,  -- EnableSlotFilling
    true,  -- EnableAnalytics
    -- Gelişmiş Parametreler - Default değerler
    0.5,   -- MinConfidenceThreshold (50%)
    0.8,   -- HighConfidenceThreshold (80%)
    10,    -- DialogueTimeoutMinutes
    3,     -- MaxDialogueAttempts
    10,    -- ProfileAnalysisInterval
    90,    -- BehaviorLogRetentionDays
    -- Metadata
    '0199da20-bb08-7be0-b61c-38abb9b90039',              -- CreatedByUserId (DEĞİŞTİRİN!)
    '0199da20-bb08-7be0-b61c-38abb9b90039',              -- UpdatedByUserId
    NOW(),                                                -- CreatedAt
    NOW(),                                                -- UpdatedAt
    true,                                                 -- IsActive
    'Initial setup - All features enabled'               -- Notes
);


-- ============================================
-- 4. MEVCUT SİSTEMİNİZİ KULLANMAK İÇİN
-- ============================================
-- Eğer sisteminizde zaten tenant ve user'lar varsa:

-- 4.1. Mevcut Tenant ID'leri bulun:
SELECT "Id", "Name" FROM "Tenants" WHERE "IsActive" = true;

-- 4.2. Mevcut User ID'leri bulun (Admin/SuperAdmin):
SELECT "Id", "Email", "TenantId", "Role" FROM "Users" 
WHERE "Role" IN ('Admin', 'SuperAdmin') AND "IsActive" = true;

-- 4.3. Yukarıdaki INSERT statement'lardaki GUID'leri değiştirin:
--   - TenantId: Kendi tenant ID'niz
--   - CreatedByUserId: Kendi admin user ID'niz
--   - UpdatedByUserId: Kendi admin user ID'niz


-- ============================================
-- 5. DOĞRULAMA SORULARI
-- ============================================

-- Oluşturulan ayarları kontrol et
SELECT 
    "Id",
    "TenantId",
    "TenantName",
    "EnableSmartIntentClassifier",
    "EnableUserLearning",
    "MinConfidenceThreshold",
    "CreatedAt",
    "IsActive"
FROM "TenantSettings"
ORDER BY "CreatedAt" DESC;

-- Aktif tenant sayısı
SELECT COUNT(*) as "ActiveTenantSettings" 
FROM "TenantSettings" 
WHERE "IsActive" = true;

-- Tenant başına özellik durumu
SELECT 
    "TenantName",
    CASE 
        WHEN "EnableUserLearning" = true THEN 'Premium'
        ELSE 'Free'
    END as "Plan",
    "EnableSmartIntentClassifier" + 
    "EnableEntityExtraction" + 
    "EnableMultiTurnDialogue" + 
    "EnableIntelligentFallback" + 
    "EnableUserLearning" + 
    "EnableIntentDiscovery" + 
    "EnablePatternLearning" + 
    "EnableContextAware" + 
    "EnableSlotFilling" + 
    "EnableAnalytics" as "ActiveFeatures"
FROM "TenantSettings"
WHERE "IsActive" = true;


-- ============================================
-- 6. ÖRNEK UPDATE SORULARI
-- ============================================

-- Bir tenant'ın tüm özelliklerini aç
-- UPDATE "TenantSettings"
-- SET 
--     "EnableSmartIntentClassifier" = true,
--     "EnableEntityExtraction" = true,
--     "EnableMultiTurnDialogue" = true,
--     "EnableIntelligentFallback" = true,
--     "EnableUserLearning" = true,
--     "EnableIntentDiscovery" = true,
--     "EnablePatternLearning" = true,
--     "EnableContextAware" = true,
--     "EnableSlotFilling" = true,
--     "EnableAnalytics" = true,
--     "UpdatedAt" = NOW()
-- WHERE "TenantId" = 'YOUR_TENANT_ID';

-- Premium özellikleri kapat (Free plan'a geç)
-- UPDATE "TenantSettings"
-- SET 
--     "EnableUserLearning" = false,
--     "EnableIntentDiscovery" = false,
--     "EnablePatternLearning" = false,
--     "UpdatedAt" = NOW()
-- WHERE "TenantId" = 'YOUR_TENANT_ID';


-- ============================================
-- 7. TEMİZLEME (GEREKİRSE)
-- ============================================

-- Tüm tenant settings'leri sil (DİKKAT!)
-- DELETE FROM "TenantSettingsLogs";
-- DELETE FROM "TenantSettings";

-- Belirli bir tenant'ın ayarlarını sil
-- DELETE FROM "TenantSettings" WHERE "TenantId" = 'YOUR_TENANT_ID';


-- ============================================
-- 8. NOTLAR
-- ============================================
/*
1. GUID'leri mutlaka değiştirin!
   - TenantId: Kendi tenant ID'niz
   - CreatedByUserId: Kendi user ID'niz

2. TenantName'i güncelleyin

3. Özellik durumlarını ihtiyacınıza göre ayarlayın:
   - Premium plan: Tüm özellikler true
   - Free plan: User Learning, Intent Discovery, Pattern Learning false

4. Parametreleri ayarlayın:
   - MinConfidenceThreshold: 0.5-0.7 arası önerilir
   - HighConfidenceThreshold: 0.8-0.9 arası önerilir
   - DialogueTimeoutMinutes: 5-15 dakika
   - MaxDialogueAttempts: 2-5 arası
   - ProfileAnalysisInterval: 10-50 komut
   - BehaviorLogRetentionDays: 30-365 gün

5. Backend otomatik default settings oluşturur:
   - Eğer tenant için settings yoksa
   - TenantSettingsService.GetSettingsAsync çağrıldığında
   - Otomatik olarak default değerlerle oluşturulur

6. Authorization:
   - User kendi tenant'ının ayarlarını görebilir/değiştirebilir
   - Admin/SuperAdmin tüm tenant'ların ayarlarını görebilir
   - JWT token'da tenant_id olmalı
*/
