# Study guide 09: Azure deployment (Bicep + GitHub Actions)

> **Honest status:** written and compiled in CI, **never deployed** (owner's choice). Treat this guide as "how I would deploy it, and why". Each "unverified until deploy" item below is a good interview answer to "what could go wrong?".

## 1. Concepts from first principles
- **Infrastructure as code (Bicep):**
  - Bicep is a declarative language for Azure resources. `main.bicep` composes modules; parameters change per environment.
  - Deploying twice gives the same result (idempotent).
  - It compiles to ARM JSON, and CI runs `az bicep build` + `lint` with no Azure login, so syntax and type errors surface on every push.
- **Managed identity:** Azure gives the web app and the function app their own identities, and RBAC role assignments grant them exactly what they need:
  - Key Vault Secrets User and Crypto User;
  - Storage Blob Data Contributor;
  - Service Bus Data Sender and Receiver;
  - a SQL contained user with reader/writer.

  **The result: no passwords or connection-string secrets anywhere.** SQL uses `Authentication=Active Directory Default`, and Service Bus uses its namespace name.
- **OIDC from GitHub to Azure:** the workflow requests a short-lived token from GitHub, and Entra trusts it through a *federated credential*. GitHub stores only three **ids**, never a secret.
- **The Data Protection key ring:** ASP.NET encrypts the session cookie with keys stored on local disk by default. On App Service that means every restart or scale-out logs everyone out. The fix is to store the keys in Blob Storage and wrap them with a Key Vault key, which closes the M3 gap.
- **Least-privilege migrations:**
  - The pipeline runs EF **migration bundles** as the *deploy* identity (the SQL Entra admin).
  - The app's identity can only read and write data, so a compromised app can't drop tables (ADR-0004).

## 2. Code tour
1. **`deploy/azure/main.bicep`**: parameters, modules and outputs. *Notice* that outputs contain names, URLs and principal ids only; secrets never appear in deployment outputs.
2. **`modules/web.bicep`**. *Notice:*
   - the app settings mirror the app's config keys: `KeyVault__Uri`, `DataProtection__*`, `ServiceBus__FullyQualifiedNamespace`, `ForwardedHeaders__TrustAll=true`;
   - `httpsOnly`, TLS 1.2, FTPS disabled;
   - Always On only for B1;
   - the health check path is `/health/ready`.
3. **`modules/functions.bicep`**: Flex Consumption (`FC1`), .NET 9 isolated, identity-based `AzureWebJobsStorage`, and **both** queue settings (M6 note).
4. **`modules/sql.bicep`**: Entra-only authentication, `useFreeLimit: true`, and `freeLimitExhaustionBehavior: AutoPause`.
5. **`modules/servicebus.bicep`**: **Standard** (duplicate detection needs it; Basic is queues only), with a queue that has duplicate detection over 10 minutes and max delivery count 5.
6. **`modules/alerts.bicep`**: scheduled queries on the M8 security EventIds (1004, 1005, 1006).
7. **`modules/roles.bicep`**: every role assignment, with built-in role GUIDs checked against the docs.
8. **`sql/grant-app-identities.sql`**: `CREATE USER … FROM EXTERNAL PROVIDER WITH OBJECT_ID`, idempotent, reader/writer only.
9. **`.github/workflows/deploy.yml`**:
   - manual dispatch and a `production` environment;
   - build (zip files + one migration bundle per module) → infra → migrate (temporary firewall rule for the runner IP, removed afterwards) → grant → deploy → smoke test on `/health/ready`;
   - every action pinned to a SHA, and sqlcmd pinned by sha256.
10. **`.github/workflows/infra.yml`**: compiles and lints the Bicep on every push.
11. **`src/Host/PlantOps.Api/Hosting/AzureHosting.cs`**: everything is **opt-in by configuration** (`AzureHostingOptions.From` is a pure, tested function), so local dev, tests and compose make no Azure calls.
    - One `DefaultAzureCredential` is shared across Key Vault, Blob and key wrapping, so tokens are cached once.
    - `AddAzureHosting()` runs **first** in `Program.cs`, so Key Vault secrets are loaded before modules read their connection strings.
12. **`src/Functions/PlantOps.Functions`**: OpenTelemetry to Azure Monitor, and `"telemetryMode": "OpenTelemetry"` in `host.json`.
13. **`docs/azure-cost.md` + `deploy/azure/cost/prices.sh`**:
    - every unit price comes from the public Azure Retail Prices API, with the date and filter recorded;
    - free grants are quoted with links;
    - "actual spend: not measured".

## 3. Why this, not that

| Choice | Instead of | Why |
|---|---|---|
| App Service (one app: API + SPA) | Static Web Apps + API | Same origin, so the BFF cookie works and there's no CORS (ADR-0006) |
| B1 default, F1 allowed | F1 only | F1 has no Always On, so the in-process outbox dispatcher idles when the site sleeps |
| Flex Consumption Functions | Timers in the web app | Single-instance timers via a storage lock; scale to zero (ADR-0010) |
| Service Bus Standard | Basic | Duplicate detection is a Standard feature, and the forwarder depends on it |
| Azure SQL free offer, auto-pause | Paid serverless | 100,000 vCore-seconds + 32 GB per database per month; pauses rather than bills |
| Managed identity + RBAC | Connection strings in Key Vault | Nothing to rotate or leak |
| OIDC federated credential | A client secret in GitHub | No long-lived credential |
| Manual deploy with a protected environment | Deploy on merge | Deploying is a human decision for this project |
| List-price script | A pricing-calculator screenshot | Reproducible, dated, citable |

## 4. Unverified until a real deploy (say these in an interview)
1. Flex Consumption availability in southeastasia. Check with `az functionapp list-flexconsumption-locations`.
2. `CREATE USER … WITH OBJECT_ID` when the pipeline's service principal is the SQL admin.
3. That `dotnet ef migrations bundle` builds in CI from the API startup project.
4. That the alert KQL reads the EventId where the OpenTelemetry distro actually puts it (`AppTraces.Properties`?).
5. That `GP_S_Gen5` with `useFreeLimit` accepts the chosen capacity.
6. Whether the Functions Service Bus trigger's identity-based setting is `ServiceBus__fullyQualifiedNamespace`. It follows the documented `<prefix>__fullyQualifiedNamespace` convention, but this session didn't re-verify it.
7. Key Vault, Blob Data Protection and Azure Monitor export at runtime. Only the "off" path is tested.

## 5. Senior interview questions
<details><summary><strong>Q1. How does your pipeline authenticate to Azure, and why no secrets?</strong></summary>

GitHub Actions requests an OIDC token for the workflow run. An Entra app registration has a federated credential that trusts tokens from this repository and environment, so `azure/login` exchanges it for a short-lived Azure token.

GitHub stores only the client, tenant and subscription **ids**. There's nothing to rotate or leak. The deploy runs only on manual dispatch, through a `production` environment with required reviewers.
</details>

<details><summary><strong>Q2. How do your apps reach SQL, Key Vault and Service Bus without credentials?</strong></summary>

Each app has a system-assigned managed identity, and Bicep grants it least-privilege RBAC roles:
- Key Vault Secrets User and Crypto User;
- Blob Data Contributor on the Data Protection container;
- Service Bus Data Sender for the API, and Sender + Receiver for the Function.

For SQL, the pipeline creates each identity as a contained user with reader/writer only, and connection strings use `Authentication=Active Directory Default`. Migrations run separately as the deploy identity, which is the only principal with DDL rights.
</details>

<details><summary><strong>Q3. Why does your app need a shared Data Protection key ring in Azure?</strong></summary>

The BFF session cookie is encrypted with ASP.NET Data Protection keys. By default they live on the instance's disk. On App Service a restart, a scale-out or a new deployment slot means a different key ring, so every user is logged out, or gets random 401s behind a load balancer.

We persist the keys to Blob Storage and wrap them with a Key Vault key. Every instance then shares one key ring, and the keys are encrypted at rest under a key we control.
</details>

<details><summary><strong>Q4. What does this cost per month?</strong></summary>

I can give list price, not actual spend, because it hasn't run for a month.

From the Azure Retail Prices API on 2026-10-03, in southeastasia:
- App Service Linux B1 is USD 0.018/hour, about USD 13.14 for 730 hours;
- Service Bus Standard's base unit is USD 10/month;
- Azure SQL stays free within 100,000 vCore-seconds and 32 GB per month, and auto-pauses beyond that;
- Flex Consumption has a monthly free grant of executions and GB-seconds;
- Log Analytics' first 5 GB/month is free.

So the list-price floor is about USD 23/month on B1, or about USD 10 on F1. Variable meters are given as formulas, and the script that produced these numbers is in the repo. After a month I'd read real spend from Cost Management.
</details>

<details><summary><strong>Q5. How do you deploy a schema change without downtime?</strong></summary>

Migrations are additive and backward compatible with the running version: expand, then migrate data, then contract in a later release (ADR-0004). The pipeline runs the migration bundles *before* deploying the new app, so the old app keeps working against the expanded schema. Destructive steps (dropping a column) ship one release later.

Deployment slots would add a warm swap. This project uses zip deploy plus `/health/ready` for the smoke test.
</details>

## 6. Break-it lab (offline)
1. In `src/Host/PlantOps.Api/Hosting/AzureHosting.cs`, change:
   ```csharp
   DataProtectionBlobUri is not null && DataProtectionKeyId is not null
   ```
   so that `&&` becomes `||`.
2. Run `dotnet test tests/PlantOps.Api.Tests --filter "FullyQualifiedName~AzureHosting"`.

   **Observe** (verified on this code): `Data_protection_needs_both_the_blob_and_the_key` fails.
3. Explain the production impact. With only the Blob URI set, the app would try to protect keys with a Key Vault key it doesn't have, and **crash at startup** on every instance. Or, depending on ordering, it would store **unencrypted** keys in Blob. That's why the rule is "both or neither", and why it's a pure, tested function.
4. Clean up with `git checkout -- .`.

## 7. Numbers (sources)
- **Unit prices:** Azure Retail Prices API on 2026-10-03, in `docs/azure-cost.md`.
- **Actual spend:** not measured.
- **CI:** the Bicep compiles and lints in `infra.yml`. The status is in the PR checks.
