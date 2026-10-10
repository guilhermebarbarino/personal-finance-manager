-- Additive: existing accounts remain accessible but are not marked as verified.
ALTER TABLE "AdminAccounts" ADD COLUMN IF NOT EXISTS "EmailVerifiedAt" timestamp with time zone NULL;
ALTER TABLE "AdminAccounts" ADD COLUMN IF NOT EXISTS "RequiresEmailVerification" boolean NOT NULL DEFAULT FALSE;
CREATE TABLE IF NOT EXISTS "EmailVerificationTokens" (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES "AdminAccounts" ("Id") ON DELETE CASCADE,
    "TokenHash" varchar(64) NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "UsedAt" timestamp with time zone NULL
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_EmailVerificationTokens_TokenHash" ON "EmailVerificationTokens" ("TokenHash");
CREATE INDEX IF NOT EXISTS "IX_EmailVerificationTokens_ExpiresAt" ON "EmailVerificationTokens" ("ExpiresAt");
CREATE INDEX IF NOT EXISTS "IX_EmailVerificationTokens_UserId" ON "EmailVerificationTokens" ("UserId");
