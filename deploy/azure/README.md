# PlantOps on Azure

Bicep + a manual GitHub Actions deploy. Design and decisions: `docs/design/09-azure-deployment.md`. Costs: `docs/azure-cost.md`.

> **Nothing here has been deployed.** CI (`.github/workflows/infra.yml`) proves the Bicep compiles and lints with no Azure login. Whether Azure accepts it, and whether the runtime wiring works, is only known after a real deployment.

## What gets created (one resource group, default region southeastasia)

| Resource | Module | Notes |
|---|---|---|
| Log Analytics workspace + workspace-based Application Insights | `modules/monitoring.bicep` | 30-day retention, 1 GB/day ingestion cap |
| Key Vault (RBAC) + key `dataprotection` | `modules/keyvault.bicep` | wraps the Data Protection key ring; holds the secret `Auth--ClientSecret` |
| Storage account | `modules/storage.bicep` | shared-key off; containers `dataprotection` and `function-deployments` |
| Azure SQL server (Entra-only) + database `PlantOps` | `modules/sql.bicep` | free offer, AutoPause when the monthly grant ends |
| Service Bus Standard + queue `sla-breaches` | `modules/servicebus.bicep` | duplicate detection 10 min, max delivery 5, SAS keys disabled |
| App Service plan (B1 or F1) + web app (API and SPA) | `modules/web.bicep` | system-assigned identity, HTTPS only, TLS 1.2, FTPS off, health check `/health/ready` |
| Flex Consumption plan + function app | `modules/functions.bicep` | .NET 9 isolated, identity-based host storage |
| 3 scheduled-query alert rules (EventIds 1004, 1005, 1006) + optional email action group | `modules/alerts.bicep` | |
| All role assignments | `modules/roles.bicep` | web: Key Vault Secrets User + Crypto User, Blob Data Contributor on the `dataprotection` container, Service Bus Data Sender. Functions: Service Bus Data Sender + Receiver, Storage Blob Data Owner + Queue/Table Data Contributor |

Parameters: see `main.bicep`; placeholders live in `main.parameters.json` (no secrets).

## Settings the apps receive

Web app (app settings): `ConnectionStrings__PlantOps` (`Authentication=Active Directory Default`, no password), `KeyVault__Uri`, `DataProtection__BlobUri`, `DataProtection__KeyId`, `APPLICATIONINSIGHTS_CONNECTION_STRING`, `ServiceBus__FullyQualifiedNamespace`, `ServiceBus__SlaBreachQueue`, `Auth__Authority`, `Auth__ClientId`, `ForwardedHeaders__TrustAll=true`, `Factory__TimeZone`, `Database__ApplyMigrationsOnStartup=false`, `Seed__Demo=false`.

**The OIDC client secret is not an app setting and not a Key Vault reference.** The API's Key Vault configuration provider reads the secret named `Auth--ClientSecret` (the provider maps `--` to `:`, giving `Auth:ClientSecret`). You create it once, after the first deploy (see below).

Function app: `AzureWebJobsStorage__accountName` + `__credential=managedidentity`, `ServiceBus__fullyQualifiedNamespace`, `SlaBreachQueue` **and** `ServiceBus__SlaBreachQueue` (both are read, CLAUDE.md M6), `ConnectionStrings__PlantOps`, `APPLICATIONINSIGHTS_CONNECTION_STRING`, `Factory__TimeZone`.

## Prerequisites (one time, by a human)

1. **An Azure subscription and a resource group**, created beforehand: `az group create -n plantops-rg -l southeastasia`.
2. **An Entra app registration for the pipeline** (this is what GitHub logs in as; it is not the web sign-in app):
   - Create it and its service principal.
   - Add a **federated credential**: issuer `https://token.actions.githubusercontent.com`, subject `repo:<owner>/<repo>:environment:production`, audience `api://AzureADTokenExchange`.
   - Assign it **Contributor** and **Role Based Access Control Administrator** (or Owner) on the resource group. The Bicep creates role assignments, which Contributor alone cannot do.
   - Note its **client ID**, the tenant ID, the subscription ID, and the **object ID of its service principal** (Enterprise applications > Object ID; not the client ID). The object ID becomes the SQL admin.
3. **An Entra app registration for user sign-in** (what `Auth__ClientId` is) with redirect URI `https://<web app host>/signin-oidc` (ASP.NET's default OIDC callback path; the code does not override it) and a client secret.
4. **GitHub**: create the environment `production` (add required reviewers). Add these **secrets** (they are IDs, not credentials; there is no client secret anywhere): `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`.

## Run it

Actions > **deploy** > Run workflow, fill the inputs (resource group, SKU `B1`/`F1`, SQL admin object ID and name, authority `https://login.microsoftonline.com/<tenant-id>/v2.0`, sign-in client ID). Jobs: `build` (API zip with the SPA in `wwwroot`, Functions zip, one EF migration bundle per module) > `infra` (Bicep) > `migrate` (bundles as the SQL admin; a temporary firewall rule for the runner) > `grant` (contained users) > `deploy` (web zip, Functions zip) > `smoke` (`/health/ready` returns 200).

### After the first deploy: the sign-in secret

Give yourself `Key Vault Secrets Officer` on the vault, then:

```
az keyvault secret set --vault-name <keyVaultName> --name "Auth--ClientSecret" --value "<sign-in app client secret>"
az webapp restart -g <rg> -n <webAppName>
```

The first smoke test can pass before this; sign-in works only after it.

### The contained-user grant

`deploy/azure/sql/grant-app-identities.sql` is idempotent and run by the `grant` job. By hand, as the SQL Entra admin, against database `PlantOps`:

```
sqlcmd -S <server>.database.windows.net -d PlantOps -G -b -i deploy/azure/sql/grant-app-identities.sql \
  -v WEB_APP_NAME=<web app> WEB_OBJECT_ID=<webPrincipalId> FUNC_APP_NAME=<function app> FUNC_OBJECT_ID=<functionPrincipalId>
```

(`-G` without a user or password uses your `az login` session; `go-sqlcmd` is required.) It creates each app's managed identity as a contained user and adds `db_datareader` and `db_datawriter`. No DDL: migrations are applied by the pipeline's bundles (ADR-0004).

## Validate locally without Azure

Not possible here (Azure CLI and Bicep are intentionally not installed locally). CI does it: `.github/workflows/infra.yml` runs `az bicep build` and `az bicep lint` with no login. `deploy/azure/cost/prices.sh` needs only `curl` and `python3`.

## Tear down

```
az group delete -n plantops-rg --yes
az keyvault purge --name <keyVaultName> --location southeastasia   # soft-delete keeps the name for 7 days; purge frees it
```

Also delete the pipeline app registration and the `production` environment if you are done with the project.

## Known unknowns until a real deploy

- Flex Consumption availability in the chosen region: check `az functionapp list-flexconsumption-locations`.
- That `CREATE USER ... FROM EXTERNAL PROVIDER WITH OBJECT_ID` works as the pipeline service principal admin, and that `Authentication=Active Directory Default` resolves to each app's identity.
- That `dotnet ef migrations bundle` builds from the API startup project in CI (the Api references Microsoft.EntityFrameworkCore.Design; the placeholder connection string is the only config it needs).
- The alert queries read `Properties.EventId` from `AppTraces`; check one real row in Log Analytics.
- The free-offer `useFreeLimit` accepted with `GP_S_Gen5` capacity 2 (max 4 vCores per the free offer page).
