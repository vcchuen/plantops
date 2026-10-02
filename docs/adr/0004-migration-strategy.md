# ADR-0004: How EF Core migrations reach the database

- **Status:** Accepted
- **Date:** 2026-10-03

## Context
Each module owns a schema and a migration set (ADR-0002). We need a way to apply migrations locally, in CI and in Azure.

## Options
1. **`Database.MigrateAsync()` at app startup, always.**
   - Zero setup.
   - But in production:
     - several App Service instances race to migrate (EF 9 takes a lock, but you still get slow cold starts and an app that crash-loops if a migration fails);
     - the app's runtime identity needs DDL rights (`ALTER`, `CREATE TABLE`), which breaks least privilege;
     - a bad migration takes the site down instead of failing a pipeline.
2. **Idempotent SQL scripts** (`dotnet ef migrations script --idempotent`) applied by the pipeline.
   - Reviewable SQL, a DBA-friendly artifact.
   - Needs `sqlcmd` or an equivalent in the pipeline.
3. **Migration bundles** (`dotnet ef migrations bundle`).
   - A self-contained executable per module, run as a pipeline step with a privileged identity, *before* the new app version receives traffic.

## Decision
- **Local / compose:** apply at startup, but **only** when `Database:ApplyMigrationsOnStartup=true`. Compose sets it; the default is `false`. Each module migrates its own context.
- **Integration tests:** the Testcontainers fixture calls `MigrateAsync()`, so tests run against the real migrations, not `EnsureCreated()`, which would skip them.
- **Azure (M9):** one migration bundle per module, run by GitHub Actions with a deploy identity that has DDL rights. The App Service identity gets only data read/write (`db_datareader`, `db_datawriter`).

## Consequences
- **Zero-downtime deploys:** migrations must be *backward compatible with the running version* (expand → migrate data → contract over two releases). For example, rename a column by adding the new one, dual-writing, backfilling, and then dropping the old one in a later release.
- **Developer flow:** a developer running the API outside compose runs `dotnet ef database update` once per module, or sets the flag in user-secrets.
