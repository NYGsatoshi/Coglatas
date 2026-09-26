# HTTP / browser security baseline (SEC-13)

This document is an operator and reviewer map for the server-owned policy in
`src/Coglatas.Web/Configuration/HttpSecurityPolicy.cs`. It is not a second
source of truth. Runtime behavior must be changed in that policy (and its typed
`SecurityOptions`) first, then this map and the SEC-13 tests must be updated.

## Canonical runtime boundaries

| Boundary | Canonical implementation | Policy |
| --- | --- | --- |
| Browser response headers | `HttpSecurityPolicy` + `SecurityHeadersMiddleware` | CSP, `X-Frame-Options: DENY`, `X-Content-Type-Options: nosniff`, `Referrer-Policy`, `Permissions-Policy`; unsafe external redirects are rejected before headers are sent. |
| HSTS / HTTPS | `HttpSecurityPolicy.ConfigureHsts` + `Program.cs` | HSTS is enabled outside Development only when `Security:EnableHsts=true`; HTTPS redirection is controlled by `Security:RequireHttps`. HSTS is 180 days without `includeSubDomains` or preload because on-prem/school deployments do not own sibling applications. |
| Auth cookie | `HttpSecurityPolicy.ConfigureAuthenticationCookie` | `.Coglatas.Auth`, `HttpOnly`, `SameSite=Lax`, configured Secure policy, eight-hour bounded ticket lifetime. Logout/session revocation remains server-side in the existing auth/session service. |
| CSRF | `HttpSecurityPolicy.ConfigureAntiforgery` + `CsrfProtectionMiddleware` | `.Coglatas.Csrf`, `HttpOnly`, `SameSite=Lax`, configured Secure policy, `X-CSRF-Token`; unsafe cookie-authenticated routes fail closed on missing/invalid tokens. Production startup rejects `EnableCsrfProtection=false`. |
| CORS | `HttpSecurityPolicy.ConfigureCors` | Empty allowlist means same-origin only. Wildcards are invalid. Credentialed CORS is possible only with explicit origins. Production origins must be HTTPS and non-loopback. |
| Sensitive caching | `HttpSecurityPolicy.ShouldPreventCaching` + `SecurityHeadersMiddleware` | Authenticated responses, auth/security API responses, and responses setting cookies receive `Cache-Control: no-store, no-cache, max-age=0`, `Pragma: no-cache`, and `Expires: 0`. |
| HTTP body / multipart | `SecurityOptions`, `RequestBodyLimitMiddleware`, `FormOptions` | Default HTTP body and multipart envelope is 64 MiB. Multipart must fit inside the global envelope and must be larger than `FileStorage:MaxFileSizeBytes` so framing overhead is bounded. Known oversize requests return generic 413 rather than 500. |
| Abuse control | `HttpSecurityPolicy.ConfigureRateLimiting` | Named fixed-window policies are partitioned by authenticated `NameIdentifier`; anonymous traffic is partitioned by `RemoteIpAddress`. `X-Forwarded-For` is never read directly. Rejections are generic 429 with `Retry-After`. |
| Proxy trust | `ForwardedHeadersConfiguration` | Forwarded headers are opt-in and accepted only from explicit IP/CIDR proxy boundaries. `RemoteIpAddress` is authoritative for anonymous limiter identity only after this boundary runs. |
| Host trust | ASP.NET Host Filtering + SEC-13 startup validation | Production rejects `AllowedHosts=*`. This also protects absolute URLs constructed from `Request.Host` (for example administrator invite URLs). |

## CSP reviewed exception

`script-src` is self-only and does not allow `unsafe-inline` or `unsafe-eval`.
`connect-src` is self plus same-host `ws://` / `wss://` for SignalR and no longer
contains a scheme-wide `https:` escape hatch.

Angular runtime styling currently requires `style-src 'unsafe-inline'`. This is
an explicit reviewed compatibility exception, not a wildcard policy. Replacing
it with nonce/hash-based styling is a separate hardening task and must not
silently broaden script execution.

## Abuse-control policy inventory

| Policy | Limit | Protected surface |
| --- | ---: | --- |
| `login` | 10 / minute | Login |
| `invite` | 10 / minute | Invite acceptance / invite registration |
| `auth-mutation` | 20 / minute | Password change and logout/session mutation |
| `file-upload` | 20 / minute | File / attachment / artifact-version uploads |
| `api-token` | 30 / minute | API token creation |
| `search` | 60 / minute | Search and existing search-classified selection work |
| `tenant-export` | 6 / 10 minutes | Tenant export generation |
| `admin-security` | 120 / minute | Platform/system administration and tenant administration surfaces |

The limiter middleware runs after authentication so authenticated requests can
use the server-authenticated user ID. Anonymous requests use the connection
remote IP. A directly supplied `X-Forwarded-For` value cannot change the key;
only `UseForwardedHeaders` can update `RemoteIpAddress`, and that middleware is
configured by the explicit trusted-proxy allowlist.

## Request and collection bounds audit

SEC-13 adds a global body envelope instead of replacing domain-specific bounds.
The following existing limits remain authoritative below that envelope:

- `FileStorage:MaxFileSizeBytes` is the per-file upload ceiling (50 MiB in the
  production example). Extension/content-type allowlists and local path
  traversal rejection remain in the file-storage boundary.
- Administrator paging is normalized in `AdminService` to a maximum page size
  of 200.
- Project list/query DTOs normalize page sizes to a maximum of 100.
- Communication polling clamps page sizes before repository reads.
- Gantt has its separate bounded full-snapshot contract (work items/milestones
  and dependency ceilings) and is not widened by SEC-13.

Future endpoints that accept unbounded collections or strings must define a
controller/DTO/service bound and, where persisted, a matching database/schema
constraint. The 64 MiB envelope is a last-resort transport ceiling, not a
replacement for domain-level validation.

## Production fail-closed checks

`HttpSecurityConfigurationValidator` prevents startup when any of these HTTP
security invariants are violated:

- non-positive or inconsistent request/multipart/upload limits;
- invalid CORS origins (including wildcard, credential-bearing URL, path,
  query, or fragment);
- production HTTP or loopback CORS origins;
- production CSRF or rate limiting disabled;
- production `AllowedHosts` missing or equal to `*`.

The existing `StartupConfigurationValidator` continues to enforce production
Secure cookies, HTTPS, HSTS, proxy, tenancy, storage, data-protection, and
secret requirements.

## Regression evidence

SEC-13-specific unit/hosted tests are in:

- `tests/Coglatas.Tests/Auth/HttpSecurityPolicyTests.cs`
- `tests/Coglatas.Tests/Auth/SecurityHeadersMiddlewareTests.cs`

Existing security suites remain part of the acceptance evidence:

- `AuthSecurityHttpTests` covers missing/valid CSRF, secure CSRF behavior behind
  a trusted forwarded HTTPS boundary, logout, revocation, expiry, and disabled
  users.
- `ForwardedHeadersConfigurationTests` covers explicit trusted proxy/network
  parsing and fail-closed proxy configuration.
- `tests/ui/public-https-golden-path.spec.ts` exercises the real public HTTPS
  path, verifies HTTP-to-HTTPS redirect and HSTS, validates Secure/HttpOnly auth
  and CSRF cookies, and verifies missing-CSRF denial.

The deterministic SEC-13 limiter test starts a real Kestrel endpoint and proves
that the login policy accepts the first 10 anonymous requests, rejects the 11th
with 429 plus `Retry-After`, and does not return internal partition metadata.
