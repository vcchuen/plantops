# 03 — Identity & authorisation

## Problem
Everything is anonymous today. We need to:
- sign people in through an external identity provider (IdP), Keycloak locally and Microsoft Entra ID in production, **with no code change to swap**;
- give the four roles different powers (operator, technician, supervisor, admin);
- be secure by default: a new endpoint that someone forgets to annotate must *not* be public.

## Decision 1 — Where do the tokens live? (see ADR-0006)

| Option | How it works | Pros | Cons |
|---|---|---|---|
| A. SPA as public OIDC client (Auth Code + PKCE), bearer token to API | Angular library (`angular-auth-oidc-client` / MSAL Angular) gets tokens; interceptor adds `Authorization: Bearer` | Very common in MY/SG enterprise (MSAL + Entra); API is stateless | **Tokens are in the browser.** Any XSS can read and exfiltrate them; refresh tokens in the browser are a long-lived prize |
| **B. Backend-for-Frontend (BFF): server is a confidential OIDC client, browser gets an HttpOnly cookie** | ASP.NET Core `AddOpenIdConnect` + cookie; Angular just calls `/api/...` | No tokens in JavaScript; client secret stays on the server; simplest Angular code | Cookies need CSRF protection; works best when the SPA and API share a site (ours do: ADR/design 01, Decision 4) |
| C. Implicit flow | Tokens in the URL fragment | — | Deprecated by OAuth 2.0 Security BCP. Not an option |

**Decision: B (BFF).**
- The IETF's current guidance for browser apps ("OAuth 2.0 for Browser-Based Applications") ranks a BFF as the most secure architecture.
- Our same-origin hosting makes it nearly free.
- Our API has no downstream APIs to call, so the "BFF" collapses into a classic OIDC-login-plus-cookie web app. The server doesn't even need to keep the access token.
- The study guide covers option A in depth, because you'll meet MSAL Angular in enterprise codebases.

## Decision 2 — Flow details
```
Browser (Angular)                 PlantOps API                       Keycloak / Entra
  GET /api/identity/me  ───────▶  401 (no cookie)
  location = /api/identity/login?returnUrl=/assets
                         ───────▶ Challenge(OIDC) ── 302 ──────────▶ /authorize (code + PKCE + state + nonce)
                                                                      user signs in
                         ◀─────── /signin-oidc?code=… ◀── 302 ─────── 
                                  code → tokens (back-channel, with client secret)
                                  validate id_token, map roles, issue cookie
                         ◀─────── 302 → /assets  (Set-Cookie: __Host-plantops; HttpOnly; Secure; SameSite=Lax)
  GET /api/assets (cookie) ─────▶ 200
```
- **`/api/identity/login?returnUrl=…`:** `returnUrl` must be a **local** URL (`Url.IsLocalUrl` / `LocalRedirect` semantics). Otherwise it's an *open redirect* (OWASP A01), which turns our login page into a phishing helper.
- **API requests without a cookie get 401, never a 302.** By default the OIDC handler answers unauthenticated requests with a redirect to Keycloak. For an XHR that means an HTML login page arrives where JSON was expected. Any `/api/*` path except `/api/identity/login` is short-circuited to 401, and forbidden is 403.
- **Logout:** `POST /api/identity/logout` clears the cookie and returns `{ logoutUrl }`, the IdP's `end_session_endpoint` with `id_token_hint` and `post_logout_redirect_uri`. The SPA navigates there.
  - *Why not let the handler redirect?* A `fetch` that follows a cross-origin 302 hits CORS.
  - *Why POST?* A GET logout can be triggered by any `<img src>` on another site.
- **Cookie:**
  - `HttpOnly` and `Secure`.
  - `SameSite=Lax`, so the top-level redirect back from the IdP carries it.
  - 8-hour lifetime with sliding expiration, matching a factory shift.
  - Session only; no refresh tokens are stored.

## Decision 3 — CSRF protection
- **The threat:** cookies are sent automatically, so a malicious site could submit a cross-site POST to `/api/assets/{id}/decommission`.
- **Defence 1:** `SameSite=Lax` already blocks cross-site POSTs carrying the cookie in modern browsers.
- **Defence 2 (defence in depth):** every **unsafe** request (POST/PUT/PATCH/DELETE) under `/api` must carry the header `X-CSRF: 1`, or it's rejected with 400.
  - A cross-site page can't add a custom header without a CORS preflight, and we have no CORS policy, so the preflight fails.
  - This is the same mechanism Duende's BFF uses. It's simpler than antiforgery tokens, because there's no token to fetch.
  - The Angular interceptor adds the header.

## Decision 4 — Roles: provider-neutral claims

| Provider | Where roles appear by default |
|---|---|
| Keycloak | `realm_access.roles`, a nested JSON object in the access token |
| Entra ID | `roles`, a flat string array (app roles) |

- **Decision:** configure Keycloak to emit a flat **`roles`** claim (a realm-roles protocol mapper on the client, added to the ID token and userinfo).
- With that, the API reads the same claim (`RoleClaimType = "roles"`) from both providers. **Swapping to Entra is configuration only:** Authority, ClientId, ClientSecret. See `docs/guides/entra-id.md`.
- Role values are lower-case, `operator | technician | supervisor | admin`, and match Entra app-role *values*.

## Decision 5 — Authorisation model
- **Secure by default:** a `FallbackPolicy` that requires an authenticated user applies to *every* endpoint that doesn't say otherwise. Forgetting `[Authorize]` can no longer make something public.
  - Explicitly anonymous: `/health/*`, `/api/identity/login`, `/api/identity/me` (it must be able to answer 401), and the SPA files.
- **Policies, not role checks scattered in endpoints.** Endpoints say *what they need*, `RequireAuthorization(Policies.ManageAssets)`, and the policy decides *who* has it:
  - `ManageAssets`: supervisor or admin (register, edit, relocate, change criticality, decommission).
  - Reads: any authenticated user (the fallback policy).
- **Where the policy names live:** `PlantOps.Modules.Identity.Contracts` defines the `Policies` and `Roles` constants. The Identity module registers the policies, and other modules reference only the *Contracts*. This is the first real cross-module dependency, and it goes through the sanctioned door.
- **Not yet:** resource-based authorisation ("a technician may only update *their own* work orders"). That arrives with work orders in M4, via `IAuthorizationService.AuthorizeAsync(user, workOrder, requirement)`.

## Decision 6 — Configuration contract (shared by API, compose and Keycloak)
```
Auth:Authority        e.g. http://localhost:8081/realms/plantops   (browser-facing issuer)
Auth:MetadataAddress  optional; e.g. http://keycloak:8080/realms/plantops/.well-known/openid-configuration
                      (used inside compose, where the API reaches Keycloak by service name)
Auth:ClientId         plantops-web
Auth:ClientSecret     from user-secrets / env / Key Vault, never appsettings
Auth:RequireHttpsMetadata  false only for local Keycloak
```
- **Compose issuer gotcha:**
  - The browser reaches Keycloak at `localhost:8081`, but the API container reaches it at `keycloak:8080`. Tokens carry `iss=http://localhost:8081/...`, and the API must agree.
  - Keycloak 26's hostname v2 option `KC_HOSTNAME=http://localhost:8081` fixes the issuer.
  - `KC_HOSTNAME_BACKCHANNEL_DYNAMIC=true` lets the API's back-channel calls (token and JWKS endpoints) use `keycloak:8080`.
  - **Unverified until we actually run compose.** The user has deferred running servers.

## Decision 7 — Tests without an identity provider
- **The problem:** a real Keycloak in CI would be slow and brittle, and the user has deferred servers anyway.
- **Test authentication scheme:** integration tests register a test auth scheme that builds a principal from a request header (`X-Test-User: roles=supervisor`). It replaces only the *authentication* step; every policy, the fallback policy, the CSRF check and the 401/403 behaviour run exactly as in production.
- **Login endpoint test:** the OIDC handler gets a static `OpenIdConnectConfiguration`, so its redirect to `/authorize` can be asserted offline. The assertions cover `code_challenge` (PKCE), `state` and `nonce`, and that an external `returnUrl` is refused.

## Frontend
- **Session store (NgRx SignalStore):** `SessionStore` (`@ngrx/signals`) holds `user`, `status` (`unknown | anonymous | authenticated`), and computed values like `isSupervisorOrAdmin`. Login state is *shared app-wide state*, which is exactly what the spec reserves SignalStore for. Page-local state stays in plain signals.
- **`authGuard` (`CanMatchFn`):** loads the session once. If anonymous, it sends the browser to `/api/identity/login?returnUrl=<current>`.
- **`csrfInterceptor`:** adds `X-CSRF: 1` to unsafe requests.
- **Toolbar:** shows the user's name and roles, plus a "Sign out" button.
- **Dev proxy:** `/signin-oidc` and `/signout-callback-oidc` are added to `proxy.conf.json`, so the IdP callback reaches the API through `ng serve`.
- **Hiding buttons is UX, not security.** The API enforces. A UI that hides "Decommission" from operators only spares them a 403.

## Out of scope
- User directory and JIT provisioning (M4 needs a technician list).
- Rate limiting the login endpoint (M8).
- The shared data-protection key ring for multiple instances (M9).
