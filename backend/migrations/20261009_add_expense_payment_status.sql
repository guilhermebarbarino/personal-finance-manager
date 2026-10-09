-- Apply manually in Neon SQL Editor BEFORE deploying the updated API.
-- Nullable: existing expenses have an unknown payment status; no status is fabricated.
-- New expenses will default to unpaid at the application layer.
ALTER TABLE "Transactions"
ADD COLUMN IF NOT EXISTS "IsPaid" boolean NULL;

-- Keep income transactions without payment status.
-- Existing rows are not changed.
