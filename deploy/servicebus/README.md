# Service Bus Emulator

The Azure Service Bus emulator provides local messaging for SLA breach notifications (Decision 2 in docs/design/06-sla-and-preventive-maintenance.md). It decouples escalation detection in the API from notification delivery via Azure Functions.

## Usage

Enable the emulator with the `messaging` profile:

```bash
docker compose --profile messaging up
```

This runs the emulator container and exposes:
- **Port 5672**: AMQP messaging (queue: `sla-breaches`)
- **Port 5300**: Management and health check APIs

The queue is configured for duplicate detection (10-minute window), 5 delivery attempts, and auto-dead-lettering on expiration.

This is a **development-only, unverified** emulator. It does not persist after container restart and is not suitable for production.
