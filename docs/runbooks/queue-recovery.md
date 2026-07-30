# Queue recovery

1. Measure queue age, not only depth. Protect fast-intake and priority-guidance deadlines first.
2. Confirm PostgreSQL health and worker lease/progress before restarting the worker once.
3. Do not delete dead letters. Inspect privacy-safe failure codes and semantic identities, fix the cause, then replay a bounded batch.
4. Never replay an outbound item in `Unknown` state automatically; the provider may already have accepted it.
5. Reconcile inbound receipts, outbox records, incident timeline events, guidance intents, and provider receipts after drainage.
6. Record the maximum age, affected semantic scopes, duplicates prevented, deadline fallbacks, and remaining manual actions.
