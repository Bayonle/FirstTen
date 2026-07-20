# Baseline and metrics protocol

## Cohorts

Report live emergencies, controlled corridor exercises, provider sandbox submissions, and synthetic
fixtures separately. Only a cohort approved in advance by FRSC and the evaluation owner may support
a live-outcome claim. A report is a source submission; an incident is the dispatcher-reviewed group
of one or more reports; a verified contribution is a source report explicitly verified by a
dispatcher.

## Clock definitions

- Intake start: first authenticated provider receipt time.
- Ticket ready: durable structured or manual-fallback incident is queryable by the dispatcher.
- Dispatch: committed dispatcher transition to `Dispatched`, using the FRSC-recorded operational
  timestamp where the downstream action occurs outside First10.
- Guidance intent: durable approved text/voice intent; provider acceptance, delivered receipt, and
  unknown outcome are reported separately.
- Arrival, transport and closure: committed dispatcher transitions backed by the agreed FRSC source.

Use occurrence and receipt clocks separately for late evidence. Never replace missing downstream
timestamps with application time or provider acceptance.

## Baseline

The Impact Lead and FRSC owner must define sampling dates, inclusion/exclusion criteria, start/end
events, missing-data handling, and outlier policy before inspecting First10 results. Store the
approved baseline outside git and record only its evidence reference and aggregate results here.

## Required denominators

Every metric records cohort, total eligible count, included count, excluded count with reasons,
missing count, ground-truth owner, and confidence/uncertainty. Report means with median and p95 where
latency is skewed. Do not combine report-level and incident-level denominators.

## Safety and integrity

False positives require independent, documented ground truth. Location completeness is measured at
the `Dispatched` transition. Guidance latency is measured from intake start to intent and separately
to provider acceptance/delivery. All figures reconcile to privacy-safe event IDs; evaluation labels
never change live operational decisions.
