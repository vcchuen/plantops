# Study guide 03: Identity & authorisation

> Goal: you can draw the login flow on a whiteboard, explain why the browser never holds a token, name three distinct attacks this code defends against (and where), and explain how swapping Keycloak for Entra ID needs no code change.
>
> Everything in this milestone runs and is tested **without** Keycloak. The lab needs no servers.

---

## 1. Concepts from first principles

### 1.1 Authentication vs authorisation
- **Authentication (AuthN)** answers *who are you?* Keycloak or Entra checks your password or MFA and tells us.
- **Authorisation (AuthZ)** answers *what may you do?* Our API decides that, from the roles in your identity.
- Interviewers love asking which status code means which:
  - **401 Unauthorized** really means *unauthenticated*: "I don't know who you are".
  - **403 Forbidden** means "I know who you are, and the answer is no".

### 1.2 OAuth 2.0 vs OpenID Connect
- **OAuth 2.0** is about *delegated access*: "let this app call that API on my behalf". It produces an **access token**.
- **OpenID Connect (OIDC)** is a thin layer on top that's about *login*: "tell this app who I am". It adds an **ID token** (a signed JWT with claims like `sub`, `name`, `email`, `roles`) plus a standard **discovery document** (`/.well-known/openid-configuration`) listing every endpoint and signing key.
- We use OIDC to log people in. We don't call any other API, so we don't even need the access token.

### 1.3 Authorization Code flow with PKCE (what actually happens)
1. The API redirects the browser to the IdP's `/authorize` with `client_id`, `redirect_uri`, `scope=openid profile email`, and three security values:
   - **`state`**: a random value we check on return. It stops an attacker from injecting *their* login response into your browser (login CSRF).
   - **`nonce`**: a random value that must appear inside the ID token. It stops replay of an old token.
   - **`code_challenge`** (PKCE): `SHA256(code_verifier)`. Only we know the `code_verifier`, so a stolen authorization code is useless to anyone else.
2. The user signs in at the IdP, which redirects back to `/signin-oidc?code=…&state=…`.
3. **Back channel (server to IdP):** the API exchanges the `code` + `code_verifier` + **client secret** for tokens, then validates the ID token's signature, issuer, audience, expiry and nonce.
4. The API creates *its own* session cookie and forgets the tokens. (It keeps only the ID token, inside the encrypted cookie, for the logout hint.)

### 1.4 Where tokens live: SPA tokens vs BFF (ADR-0006)
- **The common enterprise pattern (MSAL Angular):** the SPA itself runs the flow and keeps the access token in JavaScript memory or storage. **Any XSS can read and exfiltrate the token**, and then use it from anywhere until it expires.
- **BFF (Backend-for-Frontend):** the server runs the flow and gives the browser only an **HttpOnly cookie**. JavaScript, including injected JavaScript, *cannot read* it.
  - XSS can still make requests *while the victim's tab is open*. That's bad, but strictly better than handing over a portable token.
- **The price is CSRF:** cookies are attached automatically, so we must make sure requests come from *our* page (§1.6).

### 1.5 Cookies, in the detail interviewers probe

| Attribute | Our value | Why |
|---|---|---|
| `HttpOnly` | yes | JavaScript can't read it, so XSS can't steal it |
| `Secure` | always | never sent over plain HTTP (Chrome treats `localhost` as secure; Safari doesn't, so use Chrome for local dev) |
| `SameSite` | `Lax` | not sent on cross-site POST/PUT/DELETE, but sent on top-level GET navigations (needed for the return from the IdP) |
| Name prefix `__Host-` | yes | the browser *enforces* Secure, `Path=/` and no `Domain`, so a sibling subdomain can't overwrite the cookie |
| Lifetime | 8 h sliding | one factory shift |

### 1.6 CSRF and the custom-header defence
- **The attack:** a page on `evil.example` contains `<form action="https://plantops/api/assets/…/decommission" method="post">` and auto-submits it. If the browser attaches our cookie, the request runs *as the victim*.
- **Defence 1:** `SameSite=Lax` stops browsers from attaching the cookie to that cross-site POST.
- **Defence 2 (defence in depth):** every unsafe `/api` request must carry `X-CSRF: 1`. A plain HTML form *can't* set custom headers. JavaScript on another origin can only do so after a **CORS preflight**, and our API has no CORS policy, so the preflight fails and the browser never sends the request.
- Antiforgery tokens would also work, but they need a token round-trip. The header trick is what Duende's BFF uses.

### 1.7 Open redirect
- `/api/identity/login?returnUrl=…` sends you somewhere after login. If it accepted `https://evil.example`, a phishing email could say "log in to PlantOps here" with a *genuine* PlantOps link, and land the victim on a fake site straight after a real login.
- So `returnUrl` must be **local**:
  - starts with a single `/`;
  - isn't `//host` or `/\host`, which browsers treat as *another host*;
  - contains no control characters.
- That's OWASP A01, Broken Access Control.

### 1.8 Authorisation in ASP.NET Core: policies and the fallback policy
- **A policy** is a named set of requirements: `"assets:manage"` = *has role supervisor or admin*. Endpoints ask for a **capability** (`RequireAuthorization(Policies.ManageAssets)`), and the policy decides **who** has it. When the business says "technicians can now register assets too", you change one line in one place.
- **The fallback policy** applies to every endpoint that has *no* authorisation metadata. We set it to "authenticated user". The result is **secure by default**: a forgotten annotation means "login required", not "public".
- Public endpoints must *opt out* with `.AllowAnonymous()`. That makes them easy to grep and easy to review.

### 1.9 Angular: SignalStore, functional interceptors, CanMatch
- **NgRx SignalStore** builds a store from features:
  - `withState` for the data;
  - `withComputed` for derived signals;
  - `withMethods` for the operations.
  - `patchState` is the *only* way to write.

  We use it for the **session**, the one piece of genuinely app-wide state. Page-local state stays in plain signals, which is simpler and needs no store.
- **Functional interceptors** are plain functions `(req, next) => …` registered with `withInterceptors([...])`. **Order matters:** requests flow through the array in order, responses in reverse.
- **`CanMatch` vs `CanActivate`:** `CanMatch` runs *before* the lazy route's code is downloaded. An anonymous user never even fetches the assets bundle.

---

## 2. Guided code tour (read in this order)

### Backend
1. **`src/Modules/Identity/PlantOps.Modules.Identity.Contracts/Roles.cs`, `Policies.cs`**
   - Public constants.
   - *Notice:* Assets references **only** this Contracts project, which is the first real cross-module dependency, through the sanctioned door (ADR-0002).
2. **`Identity/AuthOptions.cs`**
   - The `Auth:*` configuration contract (design 03, Decision 6), plus `IsConfigured`.
3. **`Identity/IdentityModule.cs`**, top to bottom. *Notice:*
   - Default scheme = cookie; default *challenge* scheme = OIDC.
   - Cookie options: compare them to the table in §1.5.
   - Cookie events turn redirect-to-login and access-denied into **401 / 403**. An XHR must never receive a 302 to an HTML page.
   - `SetFallbackPolicy(...)` is the secure-by-default line. The lab removes it.
   - `UseIdentityModule()`: the CSRF guard, then authentication, then authorisation. **Order matters.** The CSRF guard runs first, so forged requests are rejected without even decrypting a cookie.
4. **`Identity/OidcOptionsSetup.cs`**. The most dense file; read every comment. *Notice:*
   - It's an `IConfigureNamedOptions`, not a lambda, so configuration added later (test overrides, user-secrets) is honoured. That's the same lesson as `AssetsModule` in M2.
   - `ResponseType = code`, `UsePkce = true`, `SaveTokens = true` (only for the logout `id_token_hint`).
   - **`ResponseMode = Query`** rather than the default `form_post`. A form_post callback is a *cross-site POST*, so the correlation and nonce cookies would need `SameSite=None`, which browsers drop on plain-http localhost.
   - `MapInboundClaims = false`: keep `roles` and `name` as the IdP sent them, instead of long WS-Federation URIs.
   - `ClaimActions.MapJsonKey("roles", "roles")`: one claim per array element. **Not** `MapUniqueJsonKey` (see the lab and pitfall 1).
   - `OnRedirectToIdentityProvider`: any `/api/*` path except `/login` gets 401 instead of a redirect.
   - When `Auth:Authority` is missing, placeholder options keep the app runnable (tests, local dev), and login returns 503.
5. **`Identity/CsrfGuardMiddleware.cs`**
   - Twenty lines. *Notice:* `StartsWithSegments("/api")` is case-insensitive, so `/API/...` can't sneak past.
6. **`Identity/IdentityEndpoints.cs`**. *Notice:*
   - **`Login`:** the `IsLocalUrl` check comes *first* (§1.7). Already signed in: `LocalRedirect`. Not configured: 503. Otherwise: `Challenge`.
   - **`Me`:** it's anonymous on purpose, so it can *answer* 401. The SPA uses that answer to decide what to do.
   - **`Logout`:**
     - It's POST, protected by the fallback policy and the CSRF guard.
     - It signs out locally *first*, then looks up the IdP's `end_session_endpoint`. If the IdP is unreachable, you're still logged out locally.
     - It returns the URL instead of redirecting, because `fetch` + cross-origin 302 = CORS failure.
   - **`IsLocalUrl`:** the same rules as MVC's `Url.IsLocalUrl`, which isn't available to minimal APIs.
7. **`src/Modules/Assets/.../Endpoints/AssetEndpoints.cs`**
   - Five `.RequireAuthorization(Policies.ManageAssets)` calls. Reads have no annotation, so the fallback covers them.
8. **`src/Host/PlantOps.Api/Program.cs`**
   - Static files come *before* auth (the SPA shell and assets are public).
   - `.AllowAnonymous()` on health, the SPA fallback and the `/api/{**rest}` 404 catch-all.

### Tests
9. **`tests/PlantOps.Api.Tests/Support/TestAuth.cs`**
   - A test authentication scheme that builds a principal from `X-Test-User: name=Sam;roles=supervisor`.
   - *Notice:* it replaces **only authentication**. Policies, the fallback, the CSRF guard and the 401/403 handling all run for real.
   - It's linked into the Assets test project with `<Compile Link>` rather than a third test project.
10. **`tests/PlantOps.Api.Tests/IdentityHostTests.cs`**. Read the test names first; together they're the behaviour spec. *Notice:*
    - `Login_redirects_to_the_idp_with_pkce_state_and_nonce` runs **offline**. The OIDC options get a static `Configuration` *and* `ConfigurationManager` (setting only one of them doesn't work; see pitfall 4).
    - The `IsLocalUrl` theory includes the nasty cases: `//evil.example`, `/\evil.example`, `~//evil.example` and a newline.
    - `Operator_gets_403_…_before_the_database_is_touched`: authorisation runs before the endpoint handler, so this needs no database.
11. **`tests/PlantOps.Modules.Assets.Tests/Integration/*`**
    - The default client is now a supervisor with `X-CSRF`. New tests cover operator → 403, anonymous → 401, and missing header → 400, against real SQL Server in CI.

### Frontend (`web/src/app/core/auth/`)
12. **`browser-location.ts`**
    - An injection token over `window.location`, so tests can assert "navigated to the IdP" without jsdom choking on real navigation.
13. **`session.store.ts`**. *Notice:*
    - `withComputed` receives state signals directly (`user()`, not `store.user()`).
    - `load()` shares one in-flight promise: the shell and the guard both call it at startup, and only one `/me` request goes out.
    - **`load()` failure semantics:** 401 means `anonymous`, but any *other* failure stays `unknown`. If a broken API were treated as "signed out", the guard would bounce the browser to the IdP and back forever.
    - `login()` is a **full-page navigation**, not an `HttpClient` call. OIDC needs the browser itself to visit the IdP.
14. **`auth.guard.ts`**. *Notice:*
    - It's `CanMatchFn` (§1.9).
    - The return target comes from `router.getCurrentNavigation()?.extractedUrl`. During a guard, `router.url` is still the *previous* page.
    - When status is unknown it returns `UrlTree('/status')`, a safe place, not a loop.
15. **`csrf.interceptor.ts`**
    - It only touches relative `/api/` URLs. Adding a custom header to a third-party URL would trigger a CORS preflight against a server we don't control.
16. **`session-expiry.interceptor.ts`**. *Notice:*
    - It resolves the store **lazily** via `Injector`. `SessionStore` → `HttpClient` → this interceptor → `SessionStore` would otherwise be a circular dependency. Also, `inject()` isn't allowed inside `catchError`.
    - It excludes `/api/identity/me`, whose 401 is normal for anonymous visitors.
17. **`app.ts` / `app.html`**
    - `store.load()` runs in the constructor, not in `provideAppInitializer`, so first render isn't blocked on `/me`.
    - The toolbar shows the name and roles as text, plus Sign in / Sign out.
18. **`features/assets/assets.models.ts` → `describeError`**
    - It checks 403 *first*, because the problem body's title is just a generic "Forbidden".

### Configuration
19. **`deploy/keycloak/plantops-realm.json`**. *Notice:*
    - A confidential client (`publicClient: false`) with `pkce.code.challenge.method: S256`.
    - `directAccessGrantsEnabled: false`: the password grant is disabled.
    - The **`realm roles as roles` mapper**, which makes Keycloak emit Entra-shaped claims.
20. **`docker-compose.yml`** (keycloak service)
    - `KC_HOSTNAME` + `KC_HOSTNAME_BACKCHANNEL_DYNAMIC` solve the issuer problem (design 03, Decision 6). **Unverified until compose runs.**
21. **`docs/guides/entra-id.md`**
    - The swap, step by step. Every Microsoft-specific claim in it was checked against Microsoft Learn on 2026-10-03.

---

## 3. Why this, not that

| Choice | Instead of | Why |
|---|---|---|
| BFF cookie session | MSAL-style tokens in the SPA | XSS can't steal a portable token; secrets stay server-side; no OIDC library in Angular (ADR-0006) |
| Custom `X-CSRF` header | Antiforgery tokens | No token round-trip; same guarantee, given same-origin hosting and no CORS |
| `SameSite=Lax` | `Strict` | `Strict` drops the cookie on the top-level return from the IdP; the header covers what Lax doesn't |
| `ResponseMode=Query` | `form_post` (default) | `form_post` needs `SameSite=None` correlation cookies, which fail on http localhost; with PKCE plus a confidential client, a code in the query is standard |
| Flat `roles` claim via Keycloak mapper | Parsing `realm_access.roles` in code | Matches Entra's shape, so swapping providers is configuration only |
| `MapJsonKey` | A custom claim action | Built in and verified to split arrays (the lab proves it); a custom class was written and then deleted in review |
| Fallback policy = authenticated | `[Authorize]` on each endpoint | Secure by default; public endpoints are explicit and greppable |
| Policies (`assets:manage`) | `RequireRole(...)` in endpoints | Endpoints state capabilities; who has them changes in one place |
| Test auth scheme | A real IdP in CI | Fast and deterministic; still exercises every *authorisation* rule |
| SignalStore for the session | A service with signals / NgRx Store | App-wide state, with structure and devtools-friendly patterns, without NgRx Store's actions and reducers ceremony |
| `CanMatch` | `CanActivate` | Doesn't download code the user can't use |

---

## 4. Common pitfalls and how this code avoids them

1. **`MapUniqueJsonKey` on an array claim.** You get *one* claim whose value is `["supervisor","admin"]`, so `IsInRole("admin")` is false and your admins are locked out. `MapJsonKey` splits arrays. **Caught in review:** the implementing agent claimed `MapJsonKey` *also* fails and wrote a custom class. A 20-line probe proved otherwise, and the class was deleted. *Verify claims about framework behaviour with a probe, not from memory.*
2. **XHR receives a 302 to the login page.** By default the OIDC handler redirects any unauthenticated request. The SPA would then parse Keycloak's HTML as JSON. Fixed in both `OnRedirectToIdentityProvider` and the cookie events.
3. **Redirect loop when the API is down.** Treating "couldn't reach `/me`" as "signed out" sends the browser to the IdP and back endlessly. Only a real 401 means anonymous.
4. **Static OIDC configuration in tests.** The framework's own post-configure builds a `ConfigurationManager` from the Authority, and the handler uses *that*. Setting `Configuration` alone is ignored, so set both.
5. **A POST without `Content-Type: application/json` returns 404, not 415.** Minimal APIs put content-type metadata on the endpoint, routing skips it, and our `/api/{**rest}` catch-all answers. It's surprising when debugging with curl. Always send the header.
6. **`post_logout_redirect_uri` built from the request host.** Behind Azure's front end the scheme and host come from forwarded headers. That's deferred to M9 (`UseForwardedHeaders`), and noted in the code.
7. **Data protection keys.** The cookie is encrypted with keys that, by default, live in the container's filesystem. Restart the container or scale out and everyone is logged out. A shared key ring (Blob + Key Vault) is in M9.
8. **`__Host-` cookies on Safari over http://localhost.** Safari refuses them. Use Chrome or Edge for local development. Production is HTTPS, so it doesn't matter there.
9. **Hiding buttons ≠ security.** The UI may hide "Decommission" from operators, but the API's policy is what actually stops them. Tests assert the 403 at the API.

---

## 5. Senior interview questions

<details>
<summary><strong>Q1. Walk me through your login flow. Why doesn't your Angular app use MSAL or any OIDC library?</strong></summary>

**Model answer:** We use the Backend-for-Frontend pattern. The ASP.NET Core server is a confidential OIDC client.

When the SPA finds it's anonymous (`/me` returns 401), it does a full-page navigation to `/api/identity/login`. The server redirects to the IdP with the authorization code flow, PKCE, `state` and `nonce`. After sign-in, the IdP redirects back with a code. The server redeems it over the back channel using the code verifier and the client secret, validates the ID token, and issues an encrypted HttpOnly `__Host-` cookie.

The browser never holds a token, so an XSS bug can't exfiltrate one. The SPA needs no OIDC library, and secrets never ship to the browser.

The trade-off is CSRF, which we handle with `SameSite=Lax` plus a mandatory custom header on unsafe requests. I'd choose the SPA-token model with MSAL if the SPA had to call many APIs on other origins with user tokens. Then I'd keep tokens in memory, use short lifetimes, and enable refresh-token rotation.
</details>

<details>
<summary><strong>Q2. Your API uses cookies. How do you prevent CSRF?</strong></summary>

**Model answer:** There are two layers.
1. **SameSite=Lax:** modern browsers don't attach the cookie to cross-site POST, PUT or DELETE requests.
2. **The custom header:** every unsafe request under `/api` must carry `X-CSRF: 1`, checked by middleware that runs before authentication. A cross-site HTML form can't set custom headers. Cross-origin JavaScript can only do so after a CORS preflight, and our API has no CORS policy, so the browser refuses to send it.

This is the approach Duende's BFF uses. It's simpler than antiforgery tokens because there's no token to fetch, and it's correct *because* the SPA and API share an origin. If we ever add CORS for another origin, this assumption has to be revisited. That's the kind of coupling I'd write in an ADR, and we did (ADR-0006).
</details>

<details>
<summary><strong>Q3. What's a fallback policy, and how does it differ from a default policy?</strong></summary>

**Model answer:**
- The **default policy** is what a bare `[Authorize]` or `RequireAuthorization()` means when you don't name a policy.
- The **fallback policy** applies to endpoints with **no authorisation metadata at all**.

Setting the fallback to "authenticated user" makes the app secure by default: a developer who forgets to annotate a new endpoint gets "login required", not "public". Anonymous endpoints must opt out with `AllowAnonymous()`, which is explicit and easy to review.

Our logout endpoint has no annotation and relies on the fallback. In the lab, removing the fallback makes three tests fail, including the logout one. That's the safety net catching exactly the mistake it exists for.
</details>

<details>
<summary><strong>Q4. How would you switch from Keycloak to Microsoft Entra ID?</strong></summary>

**Model answer:** It's configuration only.
- **Claims:** the API reads standard OIDC claims plus a flat `roles` claim. Entra emits app roles there by default. For Keycloak, we added a realm-role mapper so it emits the same shape, instead of its default nested `realm_access.roles`.
- **Entra setup:** register an app with redirect URIs (including the post-logout ones; Entra validates them against the registered list), create app roles whose values match our role constants, and assign users or groups to them. Group assignment needs Entra ID P1 or P2.
- **App config:** set `Auth:Authority` to `https://login.microsoftonline.com/{tenant}/v2.0`, plus ClientId, and put the secret in Key Vault. Better still, use a certificate or a managed-identity federated credential so there's no secret to rotate.

The guide in `docs/guides/entra-id.md` has the checklist, and every Entra-specific detail in it was checked against Microsoft Learn.
</details>

<details>
<summary><strong>Q5. In Angular, how do you avoid a 401 redirect loop, and why is the session in a SignalStore?</strong></summary>

**Model answer:**
1. **`/me` is excluded from the session-expiry interceptor.** It's *expected* to return 401 for anonymous visitors; the store reads that as "anonymous".
2. **Only a real 401 means anonymous.** A network error or a 5xx leaves the status "unknown", and the guard sends the user to a public status page instead of to the IdP. Otherwise a broken API would bounce the browser between the IdP and the app forever.
3. **Login is a full-page navigation.** It's never retried by HttpClient.

The session is the one piece of genuinely global state: the shell, the guard and the interceptors all read it. So it lives in an NgRx SignalStore, with state, computed values like `isSupervisorOrAdmin`, and methods. `patchState` is the only writer.

The interceptor resolves the store lazily through the `Injector`. The store depends on HttpClient, which depends on the interceptors, so injecting the store eagerly would be a circular dependency.
</details>

---

## 6. Break-it lab (no servers needed)

**Goal:** watch two security properties fail, and see which tests catch them. Both parts were run against this code before writing this guide.

1. Run `git checkout -b lab-03`.

**Part A: lock out every admin with one word.**

2. In `src/Modules/Identity/PlantOps.Modules.Identity/OidcOptionsSetup.cs`, change:
   ```csharp
   options.ClaimActions.MapJsonKey(Claims.Roles, Claims.Roles);
   ```
   to:
   ```csharp
   options.ClaimActions.MapUniqueJsonKey(Claims.Roles, Claims.Roles);
   ```
3. Run `dotnet test tests/PlantOps.Api.Tests`.

   **Observe:** `Roles_json_array_maps_to_one_claim_per_role` fails with `Assert.Equal() Failure: Collections differ at index 0`. The identity now holds one claim whose value is the literal text `["supervisor","admin"]`. In production, every supervisor and admin would get 403 on every asset command. It compiles, it looks reasonable in review, and only a test catches it.
4. Undo the change: `git checkout -- src/Modules/Identity/PlantOps.Modules.Identity/OidcOptionsSetup.cs`.

**Part B: remove "secure by default".**

5. In `src/Modules/Identity/PlantOps.Modules.Identity/IdentityModule.cs`, comment out the `.SetFallbackPolicy(...)` line. Keep `services.AddAuthorizationBuilder()` and `.AddPolicy(...)`.
6. Run `dotnet test tests/PlantOps.Api.Tests`.

   **Observe:** 3 tests fail:
   - `Anonymous_api_request_gets_401_problem_not_a_redirect`
   - `Anonymous_api_request_gets_401_even_with_the_real_oidc_scheme_configured`
   - `Logout_requires_a_session_and_returns_the_idp_logout_url`
7. Ask yourself:
   - Why did `GET /api/assets` stop returning 401? (It has no annotation; the fallback was its only protection.)
   - Why did **logout** fail too? (Same reason: nobody annotated it.)
   - Which endpoint is *still* protected, and why? (The five asset commands, which name `ManageAssets` explicitly.)
8. Clean up:
   ```bash
   git checkout -- . && git checkout m3-identity && git branch -D lab-03
   ```

**What you should be able to say afterwards:** "Two one-line changes, both compile, both pass code review at a glance. One locks out every admin, the other makes every unannotated endpoint public. Tests are the only thing that catches them, which is why the authorisation rules have explicit tests rather than being trusted to the framework."

---

## 7. Measured numbers from this milestone
- **.NET tests:** 34 host tests plus 54 domain tests pass locally. In CI, 113 .NET tests ran with 0 skipped, including 25 SQL Server integration tests (4 of them new, role-aware).
- **Vitest:** 37 tests.
- **Angular production build:** initial bundle 542.21 kB raw / 129.48 kB estimated transfer (it was 536.67 / 127.7 after M2). `@ngrx/signals` and the auth code add about 5.5 kB raw to the initial bundle.
- **Not measured / not run:** a real login against Keycloak. Compose isn't started, by your choice.
