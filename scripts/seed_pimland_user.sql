-- ==============================================
-- SAYP SEED DATA FOR PIMLAND
-- ==============================================
-- Run this script in sayp_pimland_db database
-- Database: PostgreSQL (port 5437)
-- ==============================================

-- 1. TenantMapping - Maps WhatsApp phone to Pimland tenant
INSERT INTO "TenantMappings" (
    "Id",
    "PhoneNumber",
    "TenantId",
    "CompanyId",
    "WhatsAppBusinessAccountId",
    "WhatsAppPhoneNumberId",
    "IsActive",
    "Metadata",
    "CreatedAt",
    "UpdatedAt"
) VALUES (
    gen_random_uuid(),
    '905433654894',                                    -- Your WhatsApp number (without +)
    'dfd11b13-b1ae-4837-9c2f-eacf5d174772'::uuid,     -- Pimland Tenant ID
    NULL,                                              -- CompanyId (optional)
    NULL,                                              -- WhatsApp Business Account ID (set later)
    NULL,                                              -- WhatsApp Phone Number ID (set later)
    true,                                              -- IsActive
    '{"source": "manual_seed", "notes": "Pimland test user"}',
    NOW(),
    NOW()
) ON CONFLICT ("PhoneNumber") DO UPDATE SET
    "TenantId" = EXCLUDED."TenantId",
    "IsActive" = EXCLUDED."IsActive",
    "UpdatedAt" = NOW();

-- 2. UserProfile - User behavior tracking
INSERT INTO "UserProfiles" (
    "Id",
    "PhoneNumber",
    "TenantId",
    "UserName",
    "TotalMessageCount",
    "TotalCommandCount",
    "FrequentCommandsJson",
    "PreferredEntitiesJson",
    "CommunicationStyleJson",
    "LearnedPatternsJson",
    "UsageHabitsJson",
    "CustomContextJson",
    "PreferredLanguage",
    "AverageResponseTime",
    "ActiveHoursJson",
    "LastActivityAt",
    "CreatedAt",
    "UpdatedAt",
    "LearningScore",
    "SatisfactionScore"
) VALUES (
    gen_random_uuid(),
    '905433654894',                                    -- Your WhatsApp number
    'dfd11b13-b1ae-4837-9c2f-eacf5d174772'::uuid,     -- Pimland Tenant ID
    'Fareed',                                          -- Username
    0,                                                 -- TotalMessageCount
    0,                                                 -- TotalCommandCount
    '{}',                                              -- FrequentCommandsJson
    '{}',                                              -- PreferredEntitiesJson
    '{}',                                              -- CommunicationStyleJson
    '{}',                                              -- LearnedPatternsJson
    '{}',                                              -- UsageHabitsJson
    '{}',                                              -- CustomContextJson
    'tr',                                              -- PreferredLanguage
    0,                                                 -- AverageResponseTime
    '{}',                                              -- ActiveHoursJson
    NOW(),                                             -- LastActivityAt
    NOW(),                                             -- CreatedAt
    NOW(),                                             -- UpdatedAt
    0,                                                 -- LearningScore
    100                                                -- SatisfactionScore
) ON CONFLICT ("PhoneNumber") DO UPDATE SET
    "TenantId" = EXCLUDED."TenantId",
    "UpdatedAt" = NOW();

-- 3. Verify the data
SELECT 'TenantMappings' as table_name, COUNT(*) as count FROM "TenantMappings" WHERE "PhoneNumber" = '905433654894'
UNION ALL
SELECT 'UserProfiles' as table_name, COUNT(*) as count FROM "UserProfiles" WHERE "PhoneNumber" = '905433654894';

-- 4. Show the inserted data
SELECT 
    tm."PhoneNumber",
    tm."TenantId",
    tm."IsActive",
    up."UserName",
    up."PreferredLanguage"
FROM "TenantMappings" tm
LEFT JOIN "UserProfiles" up ON tm."PhoneNumber" = up."PhoneNumber"
WHERE tm."PhoneNumber" = '905433654894';
