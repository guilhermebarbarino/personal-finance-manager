-- APPLY ON A NEON BACKUP/BRANCH FIRST. Run BEFORE deploying multiuser API.
-- Existing production is expected to have exactly ONE AdminAccounts row.
-- All existing financial records will be assigned to that account.
BEGIN;
DO $$
BEGIN
 IF (SELECT count(*) FROM "AdminAccounts") <> 1 THEN
   RAISE EXCEPTION 'Migration requires exactly one existing AdminAccounts user. Stop and inspect data.';
 END IF;
END $$;
ALTER TABLE "Transactions" ADD COLUMN IF NOT EXISTS "UserId" uuid NULL;
UPDATE "Transactions"
SET "UserId" = (SELECT "Id" FROM "AdminAccounts" LIMIT 1)
WHERE "UserId" IS NULL;
ALTER TABLE "Transactions" ALTER COLUMN "UserId" SET NOT NULL;
CREATE INDEX IF NOT EXISTS "IX_Transactions_UserId_Date" ON "Transactions" ("UserId","Date");
DO $$
BEGIN
 IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname='FK_Transactions_AdminAccounts_UserId' AND conrelid='"Transactions"'::regclass) THEN
  ALTER TABLE "Transactions" ADD CONSTRAINT "FK_Transactions_AdminAccounts_UserId"
    FOREIGN KEY ("UserId") REFERENCES "AdminAccounts" ("Id") ON DELETE RESTRICT;
 END IF;
END $$;
COMMIT;

-- Verify WITHOUT disclosing transaction details:
-- SELECT "UserId", count(*) FROM "Transactions" GROUP BY "UserId";
-- SELECT count(*) FROM "Transactions" WHERE "UserId" IS NULL;
