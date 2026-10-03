# ADR-0006: BFF-style cookie authentication; no tokens in the browser

- **Status:** Accepted
- **Date:** 2026-10-03

## Context
The Angular SPA and the API share one origin (design 01, Decision 4). Users authenticate with an external OpenID Connect provider: Keycloak locally, Entra ID in production.

## Decision
The **server** is the OIDC client: confidential, Authorization Code flow with PKCE, client secret held server-side.
- After login it issues an encrypted, `HttpOnly`, `Secure`, `SameSite=Lax` session cookie.
- The browser never sees an access, ID or refresh token.
- Unsafe API requests must also carry an `X-CSRF: 1` header.

## Consequences
- **Good:**
  - XSS can't steal tokens. It can still act *as* the user while the page is open, which is why XSS prevention (Angular's auto-escaping, plus a CSP in M8) still matters.
  - Angular needs no OIDC library.
  - Secrets never ship to the browser.
- **Cost:**
  - CSRF must be handled (SameSite + custom header).
  - The cookie is encrypted with ASP.NET Data Protection, so multiple instances need a **shared key ring** (Azure Blob + Key Vault, M9). Otherwise users get logged out whenever a request lands on a different instance or the app restarts.
- **When to choose the SPA-token model instead:**
  - when the SPA is hosted on a different site from its APIs;
  - when it calls many third-party APIs directly with user tokens;
  - when organisational standards mandate MSAL Angular.

  You then keep tokens in memory (not `localStorage`), use short access-token lifetimes, and rely on refresh-token rotation.
