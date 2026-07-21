# Deployment

Deploy one API image and one worker image from the same commit. PostgreSQL and private object storage are managed dependencies; Vite assets are served by the API image.

## Before rollout

1. Verify image digests, migration backup, restore-test evidence, key availability, and provider secret references.
2. Run `dotnet run --project tools/First10.Migrator -c Release` with the release migration identity. This applies EF migrations and provisions Wolverine storage once; normal API and worker identities must have `Infrastructure__AutoProvision=false` and `Infrastructure__ApplyMigrations=false`.
3. Supply `Security__DataProtectionWrappingCertificateBase64` (PKCS#12) and its password from the secret manager. The wrapping private key must not be stored in PostgreSQL.
4. Confirm object versioning is off, lifecycle/backup expiry matches approved policy, and public buckets are impossible.
5. Confirm API and worker `/alive` and `/health`, queue progress, audit-chain validity, exact guidance transition coverage, and activation blockers.

## Rollout and rollback

Start the worker, then API. Keep public WhatsApp blocked until `/api/operations/activation` is open. For rollback, stop inbound traffic, deploy the previous compatible images, and do not reverse an applied data migration until its documented data impact is reviewed. Preserve queues and audit records.
