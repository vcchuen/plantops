# Swapping Keycloak for Microsoft Entra ID

PlantOps reads only standard OIDC claims plus a flat `roles` claim (design 03, Decision 4). Keycloak is configured to emit exactly what Entra emits, so **the swap is configuration only; no code changes.**

## 1. Register the application
In the Microsoft Entra admin center, go to **App registrations → New registration**:
- **Name:** `PlantOps`.
- **Supported account types:** *Accounts in this organizational directory only* (single tenant).
- **Redirect URI (platform "Web"):** `https://<your-app>.azurewebsites.net/signin-oidc`. For local development also add `http://localhost:4200/signin-oidc` (`ng serve`) and `http://localhost:8080/signin-oidc` (compose).

Then:
- **Post-logout redirect URIs:** add each site root (`https://<your-app>.azurewebsites.net/`, `http://localhost:4200/`) as a redirect URI too. Entra validates `post_logout_redirect_uri` against the registered redirect URIs.
- **Record the Application (client) ID and Directory (tenant) ID.**

## 2. Create the app roles
Under **App roles → Create app role**, create four roles. Set **Allowed member types** to *Users/Groups* for each.

| Display name | Value (must match `Roles` in `PlantOps.Modules.Identity.Contracts`) |
|---|---|
| Operator | `operator` |
| Technician | `technician` |
| Supervisor | `supervisor` |
| Admin | `admin` |

Entra puts assigned app-role **values** into the `roles` claim of the ID token. That's the same claim name and the same values that the Keycloak mapper produces.

## 3. Assign users to roles
Under **Enterprise applications → PlantOps → Users and groups → Add user/group**, pick a user and a role.
- Assigning **groups** (rather than individual users) to app roles requires a Microsoft Entra ID P1 or P2 licence.
- In an enterprise you'd assign groups such as `SG-PlantOps-Supervisors`. On a free tenant, assign users.

## 4. Credentials
- **Simplest:** *Certificates & secrets → New client secret*. Store it in **Key Vault** as secret `Auth--ClientSecret`. The Key Vault configuration provider maps `--` to `:`, so the app sees `Auth:ClientSecret`.
- **Better for production:** a certificate, or a *federated identity credential* that trusts the App Service's managed identity. That way there's no secret to rotate at all. This is revisited in M9.

## 5. Optional claims
Under **Token configuration → Add optional claim → ID → `email`**. PlantOps shows the email if present and works without it.

## 6. Configure PlantOps

| Key | Keycloak (local) | Entra ID |
|---|---|---|
| `Auth:Authority` | `http://localhost:8081/realms/plantops` | `https://login.microsoftonline.com/<tenant-id>/v2.0` |
| `Auth:MetadataAddress` | `http://keycloak:8080/realms/plantops/.well-known/openid-configuration` (compose only) | *(leave empty)* |
| `Auth:ClientId` | `plantops-web` | Application (client) ID |
| `Auth:ClientSecret` | dev placeholder from `.env` | Key Vault `Auth--ClientSecret` |
| `Auth:RequireHttpsMetadata` | `false` | `true` (default) |

## 7. What differs, and why it doesn't matter
- **Claim layout:**
  - Entra's `name` claim is the user's display name, which is the same claim we already use as `NameClaimType`.
  - Keycloak's default role location (`realm_access.roles`) isn't used at all, because of the realm mapper that emits `roles`.
- **userinfo:** Entra's userinfo endpoint (served by Microsoft Graph) doesn't return `roles`. Roles come from the ID token, which is where we read them for both providers.
- **Logout:** both providers publish `end_session_endpoint` in discovery, and the logout endpoint reads it from there.

## Checklist to verify a swap
1. `GET /api/identity/me` after login returns the expected `roles` array.
2. An operator receives 403 on `POST /api/assets`, and a supervisor receives 201.
3. Sign out lands back on the site root and `GET /api/identity/me` returns 401.
