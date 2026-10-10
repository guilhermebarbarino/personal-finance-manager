-- Apply to a Neon test branch first, then production BEFORE deploying the updated API.
-- This adds an optional display name without changing financial transactions.
ALTER TABLE "AdminAccounts" ADD COLUMN IF NOT EXISTS "DisplayName" varchar(100) NULL;

-- Existing accounts can set their name through the "Definir meu nome" button.
-- Verification:
-- SELECT column_name, data_type FROM information_schema.columns
-- WHERE table_name = 'AdminAccounts' AND column_name = 'DisplayName';
