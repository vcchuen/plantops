# ADR-0010: Scheduled jobs run in Azure Functions; logic lives in the module

- **Status:** Accepted
- **Date:** 2026-10-03

## Context
SLA escalation (every 5 min) and PM generation (daily) must run exactly once per schedule tick. That has to hold even when the web app is scaled to several instances, restarting, or idle.

## Decision
- **The logic:** the runners (`ISlaEscalationRunner`, `IPreventiveMaintenanceRunner`) are public contracts in `WorkOrders.Contracts`, implemented internally by the WorkOrders module.
- **Azure:** an isolated-worker **Azure Functions** app hosts them with **timer triggers**. Timer triggers take a blob lease, so only one instance runs each occurrence.
- **Locally:** `Jobs:RunInProcess=true` runs the same runners in a `BackgroundService` inside the API, for single-instance development only.
- **Correctness doesn't depend on "exactly once":** both runners are idempotent.
  - Escalation: `EscalatedAt IS NULL` plus a domain no-op.
  - PM: a filtered unique index on `(PmScheduleId, PmDueOn)`.

  A double run is harmless.

## Consequences
- **One more deployable:** the Function app references the module projects directly, so it's a second host of the modular monolith. That's the same extraction story as ADR-0001, in practice.
- **Cost:** a Functions Consumption/Flex plan for two timers is near-free. The real monthly cost is measured in M9, not estimated here.
- **Local tooling:** running Functions locally requires Azure Functions Core Tools. That isn't installed on the dev laptop, so local development uses the in-process option.
