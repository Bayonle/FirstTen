# ADR 0001: Modular monolith with two deployed workloads

Status: Accepted — 2026-07-20

First10 is organized by product module (intake, incidents, dispatch, guidance, recognition, identity/audit, operations) in one codebase. Production deploys one API and one worker, backed by PostgreSQL/Wolverine durable messaging. Aspire orchestrates API, worker, PostgreSQL, object storage, and Vite locally.

This preserves module ownership and durable asynchronous boundaries without imposing a service-per-module operational burden on a two-person pilot team. Module architecture tests prevent domain implementation coupling. A future split requires measured scaling, isolation, or ownership evidence.
