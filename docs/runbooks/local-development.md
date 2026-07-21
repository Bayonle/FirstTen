# Local development

## Prerequisites

- .NET SDK from `global.json`, Node 22+, Docker Desktop, and trusted local HTTPS certificates.
- User secrets for Aspire parameters. Never commit provider tokens, contact keys, media keys, or approval evidence.

## Start

1. Run `dotnet run --project src/First10.AppHost`.
2. Use the Aspire dashboard to confirm PostgreSQL, MinIO, API, worker, and Vite are healthy.
3. Bootstrap the first administrator, invite dispatcher and clinical accounts, and enroll MFA.
4. Keep `Channels__WhatsApp__SandboxMode=true` and explicitly set `Channels__WhatsApp__SandboxAllowedDestinations__0` to each test number; Telegram is test-only until activation passes.

## Deterministic checks

Run `dotnet build First10.slnx -c Release`, the unit/contract/architecture tests, integration tests with Docker, and `npm run lint && npm test && npm run build` under `web/`.

Do not use local fixtures as pilot evidence. Synthetic, sandbox, controlled-exercise, and live cohorts remain separate.
