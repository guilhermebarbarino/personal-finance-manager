-- Run on a Neon test branch before production. Backup production first.
-- Apply BEFORE deploying this backend release. No financial data is modified.
BEGIN;
ALTER TABLE "AdminAccounts" ADD COLUMN IF NOT EXISTS "SessionVersion" uuid;
UPDATE "AdminAccounts" SET "SessionVersion" = gen_random_uuid() WHERE "SessionVersion" IS NULL;
ALTER TABLE "AdminAccounts" ALTER COLUMN "SessionVersion" SET NOT NULL;
CREATE TABLE IF NOT EXISTS "PasswordResetTokens" (
    "Id" uuid NOT NULL PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES "AdminAccounts"("Id") ON DELETE CASCADE,
    "TokenHash" varchar(64) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "UsedAt" timestamp with time zone NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_PasswordResetTokens_TokenHash" ON "PasswordResetTokens" ("TokenHash");
CREATE INDEX IF NOT EXISTS "IX_PasswordResetTokens_ExpiresAt" ON "PasswordResetTokens" ("ExpiresAt");
COMMIT;
-- Verify:
-- SELECT count(*) FROM "AdminAccounts" WHERE "SessionVersion" IS NULL; -- 0
-- SELECT to_regclass('public."PasswordResetTokens"'); -- table must exist
