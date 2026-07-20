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
```

Install the frontend dependencies once, then start the complete development topology:

```sh
npm --prefix web install
dotnet run --project src/First10.AppHost
```

Aspire starts the API, worker, Vite server, PostgreSQL, S3-compatible object storage, and the local
telemetry dashboard. Aspire is not part of the production runtime.

## Production shape

Only `src/First10.Api/Dockerfile` and `src/First10.Worker/Dockerfile` produce application images.
Product modules remain code boundaries inside those two workloads.

## Project documents

- Requirements: `docs/brainstorms/2026-07-20-first10-pilot-requirements.md`
- Implementation plan: `docs/plans/2026-07-20-001-feat-first10-live-pilot-plan.md`
