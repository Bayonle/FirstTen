# Deployment

Deploy one API image and one worker image from the same commit. PostgreSQL and private object storage are managed dependencies; Vite assets are served by the API image.

## Before rollout

1. Verify image digests, migration backup, restore-test evidence, key availability, and provider secret references.
2. Run migrations with the worker migration service before accepting traffic.
3. Confirm object versioning is off, lifecycle/backup expiry matches approved policy, and public buckets are impossible.
4. Confirm `/alive`, `/health`, queue progress, audit-chain validity, guidance coverage, and activation blockers.

## Rollout and rollback

Start the worker, then API. Keep public WhatsApp blocked until `/api/operations/activation` is open. For rollback, stop inbound traffic, deploy the previous compatible images, and do not reverse an applied data migration until its documented data impact is reviewed. Preserve queues and audit records.
