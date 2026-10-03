# ADR-0009: Transactional outbox + idempotent inbox for events between modules

- **Status:** Accepted
- **Date:** 2026-10-03

## Context
Completing a work order must change stock (Inventory) and maintenance history (Assets). The modules are separately owned and must stay extractable (ADR-0001). The reaction must survive crashes and restarts.

## Decision
- **Producing side:**
  - Producers write integration events as **outbox rows in the same transaction** as the business change. The M4 `DomainEventInterceptor` does this, via a per-module mapper from domain events to integration events.
  - A background **dispatcher** claims unprocessed rows with `UPDLOCK, READPAST`, delivers them to in-process handlers in a fresh scope, and marks them processed.
  - It retries failed messages up to 5 times, then parks them (logged).
- **Consuming side:** consumers record each handled message in an **inbox** table with a unique (MessageId, Handler) key, in the same transaction as their effect, which makes redelivery harmless.

## Consequences
- **Delivery is at-least-once**, and effects happen once (idempotent consumers). Exactly-once *delivery* is not achievable across a crash boundary; exactly-once *effect* is.
- **Eventual consistency:** a short delay (one polling interval) before consumers see the change. The UI states this.
- **Extraction path:** to extract a module, point the dispatcher at Azure Service Bus instead of in-process handlers. Event contracts and consumer code don't change. M6 does exactly this for SLA notifications.
- **Cost:** two extra tables per module and a polling loop. The polling interval is a configuration value; the default is 2 s.
- **Rejected:** in-process calls (crash window), an in-memory bus (lost on restart), and publishing to a broker after commit (the dual-write problem).
