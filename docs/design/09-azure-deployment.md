# 09 — Azure deployment (Bicep + GitHub Actions)

> **Status:** the code and infrastructure are written but **not deployed**. The owner deferred all deployment, so nothing here has run against a real subscription. Every Azure fact below was checked against Microsoft Learn on 2026-10-03, and every price comes from the public **Azure Retail Prices API**.

## Target architecture (region `southeastasia`, Singapore, the closest broadly-featured region to Penang)
```
GitHub Actions (OIDC, no secrets) ──► Bicep ──► Resource group
  App Service plan (Linux)  ── Web App: API + Angular SPA (one app, same origin)   system-assigned identity
  Function App (Flex Consumption, Linux) ── SLA/PM timers + Service Bus notifier    system-assigned identity
  Azure SQL logical server (Entra-only auth) ── database "PlantOps" (free offer: auto-pause at limit)
  Service Bus namespace (Standard) ── queue "sla-breaches" (duplicate detection on)
  Key Vault (RBAC) ── Data Protection key, Keycloak/Entra client secret
  Storage account ── Data Protection key ring blob, Functions deployment + host storage
  Log Analytics workspace + Application Insights (workspace-based) ── OpenTelemetry from both apps; alert rules
```

## Decisions

| Concern | Choice | Why (and the verified fact behind it) |
|---|---|---|
| Web hosting | **App Service Linux**. Parameter `appServiceSku` defaults to **B1**; **F1** (free) is allowed for demos | One app serves the API and the SPA (same origin, ADR-0006). F1 has no Always On, so the in-process outbox dispatcher sleeps when the app idles; B1 avoids that |
| Jobs | **Functions Flex Consumption** | Linux only; supports .NET 9 isolated; `WEBSITE_TIME_ZONE`/`TZ` unsupported, hence UTC cron (M6). Timer triggers run single-instance via a storage lock (ADR-0010) |
| Database | **Azure SQL free offer**, `useFreeLimit: true`, `freeLimitExhaustionBehavior: AutoPause` | 100,000 vCore-seconds + 32 GB per database per month, up to 10 databases per subscription; auto-pause until next month when exhausted |
| Messaging | **Service Bus Standard** | Basic is queues only; **duplicate detection needs Standard**, and M6's forwarder relies on it |
| Secrets | **Key Vault (RBAC) + managed identities everywhere** | No connection-string secrets: SQL via Entra auth, Service Bus via `FullyQualifiedNamespace`, Key Vault via the Configuration provider |
| Session keys | **Data Protection key ring in Blob, wrapped by a Key Vault key** | Without a shared ring, every restart or scale-out logs everyone out (M3 gap) |
| Telemetry | **Azure Monitor OpenTelemetry distro** (`UseAzureMonitor()`) when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set | Traces, metrics and logs, including the M8 security EventIds |
| Alerts | Scheduled-query alert rules on EventIds **1004** (CSRF rejected, > 20 per 5 min), **1005** (rate limited, > 100 per 5 min), **1006** (outbox message parked, ≥ 1) | Turn the M8 events into pages |
| Migrations | **EF migration bundles** per module, run by the pipeline **as the deploy identity** before the app swap (ADR-0004) | The app's identity gets only `db_datareader`/`db_datawriter` (least privilege) |
| CI/CD auth | **GitHub OIDC federated credential** (`azure/login` with client-id, tenant-id, subscription-id) | No long-lived Azure secret in GitHub |
| Deploy trigger | **`workflow_dispatch` only**, GitHub environment `production` with required reviewers | Deploying is a human decision |

## Pipeline (`.github/workflows/deploy.yml`, manual)
1. `build`:
   - `dotnet publish` the API, with the Angular build copied into `wwwroot`, then zip it;
   - `dotnet publish` the Functions app, then zip it;
   - build `efbundle` executables for the Assets, Identity, WorkOrders, Inventory and Reporting contexts.
2. `infra`: `azure/login` (OIDC), then `az deployment group create -f deploy/azure/main.bicep -p @deploy/azure/main.parameters.json`.
3. `migrate`: run each bundle with `--connection "Server=tcp:<server>.database.windows.net;Database=PlantOps;Authentication=Active Directory Default"`. The deploy identity is the SQL Entra admin.
4. `grant`: one idempotent T-SQL script creates the web and function identities as contained users with reader/writer roles, run with `sqlcmd` and the same Entra token.
5. `deploy`: zip-deploy the API (`azure/webapps-deploy`) and the Functions package (`azure/functions-action`, Flex).
6. `smoke`: `curl /health/ready` until 200 (it's anonymous by design).

## Monthly cost
- `deploy/azure/cost/prices.sh` queries the **Azure Retail Prices API** for the exact SKUs and region and writes `docs/azure-cost.md` with the date, the API filter used, each unit price, and the assumptions (hours per month, expected operations).
- **Nothing is estimated beyond list price × stated assumption.** Real spend has to be read from Cost Management after a month of running. That isn't possible until someone deploys, and the doc says so.
- **Unit prices observed on 2026-10-03** (southeastasia, USD, list):

| Resource | Unit price |
|---|---|
| App Service Linux B1 | 0.018 / hour |
| App Service Linux F1 | 0.00 |
| Service Bus Standard base unit | 10.0 / month |
| Key Vault Standard operations | 0.03 per 10 K |

  Azure SQL (free offer) and Flex Consumption free grants are quoted from their docs and pricing pages by the script.

## What the code needs (not just Bicep)
- **API:**
  - `KeyVault:Uri`: add the Key Vault configuration provider, with `DefaultAzureCredential`.
  - `DataProtection:BlobUri` + `DataProtection:KeyId`: `PersistKeysToAzureBlobStorage` + `ProtectKeysWithAzureKeyVault`.
  - `UseAzureMonitor()`.
  - `ForwardedHeaders:TrustAll=true` is set by Bicep, because App Service terminates TLS.
- **SQL connection strings** use `Authentication=Active Directory Default`. There's no password anywhere.
- **Functions:** the same OpenTelemetry setup; identity-based `ServiceBus__fullyQualifiedNamespace`; both queue settings (M6 note).

## Out of scope
- Custom domain and certificate.
- Front Door / WAF.
- Private endpoints (they need a VNet, and Service Bus private link is Premium only).
- Multi-region.

These are all documented as next steps.
