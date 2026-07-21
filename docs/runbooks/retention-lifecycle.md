# Retention lifecycle matrix

No structured-record cutoff becomes production policy until legal/DPO evidence is recorded. Media uses the currently approved 30-day default; changing it requires a reviewed migration/config decision and updated evidence.

| Data class | Trigger | Current action | Approval state |
|---|---|---|---|
| Raw image/audio bytes | End of in-memory privacy processing | Zero memory; never persist | Enforced |
| Face-blurred image / sanitized audio | `ExpiresAtUtc` | Delete private object, then append minimized deletion audit | 30-day policy pending final legal confirmation |
| Provider media handle | Privacy processing completion/session expiry | Remove with intake input retention job | Cutoff blocked on legal approval |
| Protected reporter contact | Withdrawal or approved inactivity cutoff | Crypto-delete destination; retain non-reversible aggregate key only if approved | Cutoff blocked |
| Intake sessions and structured triage | Closure plus approved operational cutoff | Delete or irreversibly anonymize | Cutoff blocked |
| Queues, outbox, dead letters | Terminal processing plus approved troubleshooting window | Purge payload; keep counts/hash evidence | Cutoff blocked |
| Delivery records | Approved dispute window | Delete provider identifiers and payload | Cutoff blocked |
| Incident operational record | Closure plus statutory/partner cutoff | Minimize/anonymize; preserve approved aggregate | Cutoff blocked |
| Recognition consent/awards | Withdrawal affects publication immediately | Keep append-only private ledger under approved accountability cutoff | Cutoff blocked |
| Audit epochs | Epoch closure and external anchor | Retain hash chain under approved accountability cutoff | Cutoff blocked |
| Exports | Purpose completion | Delete immediately and audit | Operator enforced |
| Backups | Backup expiry | Provider deletion or approved crypto-erasure | Hosting evidence required |

Legal holds suspend deletion only for explicitly scoped records and are themselves audited. They do not authorize broader collection.
