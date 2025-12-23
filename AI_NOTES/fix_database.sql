-- Fix SayP Database Schema for Mobile API
-- Run this SQL on your PostgreSQL database

-- Add MediaUrl column
ALTER TABLE "Messages" 
ADD COLUMN IF NOT EXISTS "MediaUrl" text NULL;

-- Add RetryCount column
ALTER TABLE "Messages" 
ADD COLUMN IF NOT EXISTS "RetryCount" integer NOT NULL DEFAULT 0;

-- Fix Status column type (if needed)
-- ALTER TABLE "Messages" 
-- ALTER COLUMN "Status" TYPE integer USING "Status"::integer;

-- Verify
SELECT column_name, data_type, is_nullable 
FROM information_schema.columns 
WHERE table_name = 'Messages' 
ORDER BY ordinal_position;
