# Pilot activation

The executable gate is `/api/operations/activation`. A single environment switch cannot open it.

## Required evidence

An administrator records append-only approval or revocation evidence for FRSC, clinical library, Meta production access, legal/DPIA, data protection, retention, privacy benchmark, AI benchmark, recovery drill, restore test, and staged corridor exercise. The latest record for every type must be `Approved`.

The gate also requires the effective OpenAI project, region, retention mode, transcription model, triage model, and crew-briefing model to exactly match the approved profile. It requires a conservative enabled guidance set for initial safety and every dispatch trigger (`verified`, `dispatched`, `arrived`, `transported`, `closed`, and `reopened`), plus WhatsApp credentials. Record references, not personal data or signed-document contents.

## Sequence

1. Reconcile the approval register and record accepted owners/evidence references.
2. Run privacy, AI, restore, recovery, load, accessibility, and controlled corridor exercises.
3. Confirm public inbound and outbound both return gate-closed behavior before approval.
4. Record evidence through the authenticated, antiforgery-protected endpoint.
5. Have a second operator verify the status and exact deployed commit.
6. Open provider routing. Monitor queue age, manual fallback, guidance delivery, and privacy signals.

Any revocation closes production WhatsApp on the next inbound/outbound decision. Sandbox WhatsApp is refused in Production and, elsewhere, accepts only `Channels__WhatsApp__SandboxAllowedDestinations`; labelled Telegram tests remain a separate controlled cohort.
