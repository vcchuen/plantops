# Keycloak Configuration for PlantOps

This directory contains the Keycloak realm import file for local development.

## plantops-realm.json

A Keycloak 26 realm export file that sets up the `plantops` realm with:
- Four realm roles: `operator`, `technician`, `supervisor`, `admin`
- One OIDC client: `plantops-web` (configured for the PlantOps API and Angular SPA)
- Four development users (see below)

### Development Users

**WARNING: Development only — change all passwords and credentials in production.**

| Username | Password | Email | Role |
|---|---|---|---|
| olivia | Passw0rd! | olivia.tan@plantops.local | operator |
| tom | Passw0rd! | tom.lim@plantops.local | technician |
| sam | Passw0rd! | sam.wong@plantops.local | supervisor |
| ada | Passw0rd! | ada.rahman@plantops.local | admin |

### Client Secret

The client secret is set to `plantops-dev-secret-change-me` (plaintext in the realm file for local dev).

**In real environments (staging/production):**
- Microsoft Entra ID is used instead (no code changes required; see `docs/guides/entra-id.md`).
- The client secret is stored in Azure Key Vault and injected via `Auth:ClientSecret` at runtime.
- The realm file is never used in production.

## Re-exporting from Keycloak

To export the realm after making changes in Keycloak's admin UI:

```bash
docker compose exec keycloak /opt/keycloak/bin/kc.sh export \
  --realm plantops \
  --users realm_file \
  --file /opt/keycloak/data/import/plantops-realm.json
```

Then copy the exported file back to this directory.
