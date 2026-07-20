# First10

First10 is a privacy-first, bystander-initiated road-crash reporting system for the
Berger–Mowe pilot corridor. The backend is a modular monolith composed into one API and one worker;
the dispatcher console is React, TypeScript, Vite, TanStack Query, and TanStack Router.

## Local development

Prerequisites:

- .NET SDK 10.0.103 or a compatible 10.0 patch
- Node.js 22.14 or newer
- Docker-compatible container runtime

Set stable local service credentials without committing them. Keeping the PostgreSQL password
stable is required when reusing the named development volume:

```sh
dotnet user-secrets --project src/First10.AppHost set Parameters:postgres-password "choose-a-long-local-secret"
dotnet user-secrets --project src/First10.AppHost set Parameters:object-storage-access-key first10-local
dotnet user-secrets --project src/First10.AppHost set Parameters:object-storage-secret-key "choose-a-long-local-secret"
dotnet user-secrets --project src/First10.AppHost set Parameters:bootstrap-secret "choose-a-different-long-local-secret"
dotnet user-secrets --project src/First10.AppHost set Parameters:telegram-webhook-secret "choose-a-telegram-webhook-secret"
dotnet user-secrets --project src/First10.AppHost set Parameters:telegram-bot-token "paste-the-BotFather-token"
dotnet user-secrets --project src/First10.AppHost set Parameters:reporter-pseudonym-key "choose-an-independent-32-character-minimum-key"
dotnet user-secrets --project src/First10.AppHost set Parameters:media-encryption-key "paste-a-base64-encoded-32-byte-key"
dotnet user-secrets --project src/First10.AppHost set Parameters:face-redaction-model-path "/absolute/path/to/FirstTen/models/face_detection_yunet_2026may.onnx"
dotnet user-secrets --project src/First10.AppHost set Parameters:openai-api-key "paste-the-project-api-key"
dotnet user-secrets --project src/First10.AppHost set Parameters:openai-safety-identifier-key "choose-an-independent-32-character-minimum-key"
```

Generate the media key with `openssl rand -base64 32`; do not reuse the reporter pseudonym key.
The OpenAI safety-identifier key must also be independent: it HMACs the already pseudonymous
reporter key before any request leaves First10. OpenAI response storage is disabled in every triage
request. No guidance category is enabled by default; clinically approved categories must be added
to `Guidance:EnabledCategories` only after sign-off.

The checked-in corridor gazetteer is deliberately non-operational until FRSC supplies and reviews
the Berger–Mowe landmark coordinates, aliases, and directions. Until then, landmark phrases fail
closed to the existing location-pin request flow.

Fetch and checksum the pinned MIT-licensed face detector before first startup:

```bash
./scripts/fetch-face-redaction-model.sh
```

Install the frontend dependencies once, then start the complete development topology:

```sh
npm --prefix web install
dotnet run --project src/First10.AppHost
```

Aspire starts the API, worker, Vite server, PostgreSQL, S3-compatible object storage, and the local
telemetry dashboard. Aspire is not part of the production runtime.

The bootstrap secret can issue only the first Administrator invitation and becomes unusable as soon
as that provisional account exists. Remove it from the secret store after the first administrator
has enrolled. There is no public registration endpoint.

## Production shape

Only `src/First10.Api/Dockerfile` and `src/First10.Worker/Dockerfile` produce application images.
Product modules remain code boundaries inside those two workloads.

## Project documents

- Requirements: `docs/brainstorms/2026-07-20-first10-pilot-requirements.md`
- Implementation plan: `docs/plans/2026-07-20-001-feat-first10-live-pilot-plan.md`
