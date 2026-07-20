# Pilot approval register

No row may become `Approved` without a dated evidence reference stored in the approved project
records. Do not put signatures, tokens, participant data, or confidential agreements in git.

| Gate | Approver | Required evidence | Status | Evidence reference | Expiry/review |
|---|---|---|---|---|---|
| FRSC pilot authority | Named FRSC authority | Signed LOI and corridor scope | Missing | — | Before activation |
| Operational workflow | Named FRSC dispatcher lead | Queue, timer, status, briefing-handoff sign-off | Missing | — | Before exercise |
| Clinical library | Named Clinical Advisor | Exact text/audio checksums for all enabled sets | Missing | — | Every asset change |
| Legal/DPIA | Named legal/privacy advisor | DPIA, lawful basis/notices, retention, rights and transfers | Missing | — | On processor/scope change |
| OpenAI processing | Legal/privacy + tech lead | Approved project, models, region/profile, retention and subprocessors | Missing | — | On configuration change |
| Hosting/processors | Legal/privacy + tech lead | Region, DPA, access, backup and deletion controls | Missing | — | On host change |
| Privacy benchmark | Tech lead + legal/privacy | Licensed model checksum and approved recall report | Missing | — | On model/threshold change |
| AI benchmark | Tech lead + FRSC/clinical reviewers | Language accuracy, overreach, latency, cost and abstention report | Missing | — | On model/prompt change |
| Meta production access | Meta + partner owner | Production WABA/number/templates/webhook access | Missing | — | Provider expiry |
| Reporter materials | FRSC + legal/privacy | Exact localized onboarding and first-contact materials | Missing | — | On wording change |
| Recovery and restore | Tech lead + FRSC operator | Dated game-day and restore evidence | Missing | — | Before each activation |
| Corridor exercise | FRSC + Team Lead | Dated exercise results and stop/go decision | Missing | — | Before live pilot |

Public inbound and outbound WhatsApp configuration must read this fail-closed projection; a single
missing, expired, or checksum-drifted mandatory gate keeps production intake disabled.
