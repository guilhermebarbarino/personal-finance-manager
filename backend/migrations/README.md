# Manual production schema changes (MVP)

The database was initialized with EF Core `EnsureCreated`, not migrations.
**Before merging the payment-status PR**, take a Neon backup or create a
Neon database branch and run `20261009_add_expense_payment_status.sql`
on your production database using the Neon SQL Editor.

Validate without exposing private transaction details:

```sql
SELECT column_name, data_type, is_nullable
FROM information_schema.columns
WHERE table_name = 'Transactions' AND column_name = 'IsPaid';
```

Expect `IsPaid | boolean | YES`.

This additive change preserves historical transaction rows and uses NULL
for their previously unknown status. Avoid updating past rows automatically.
Then merge the PR, let CI complete, and redeploy the Render backend from main.
The frontend is deployed independently via GitHub Actions.

For future work replace `EnsureCreated` with a baseline and proper EF Core
Migrations after a planned transition and verified backup.
