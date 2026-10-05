# Issue #481 public HTTPS production Golden Path

Status: repeatable deployment-gate implementation; target-environment execution evidence is still required.

## Purpose

`/health/ready` only confirms a limited application readiness condition. It is
not release success. The Issue #481 gate runs a single non-mocked Chromium
journey through the configured public HTTPS origin after deployment:

```text
Browser -> public TLS/CDN/tunnel -> forwarded headers -> ASP.NET Core
        -> cookie/CSRF -> Workspace -> Project -> Task -> Project File runtime
        -> durable result -> reload -> logout/re-login -> result reread
```

The runner rejects localhost, private addresses, HTTP, path-prefixed URLs, and
non-standard ports. It first requires a real `http` to `https` redirect, then
requires HSTS on the browser's HTTPS login response. A successful readiness
probe permits the browser test to start; it is never a pass condition by
itself. An unavailable endpoint fails as `BLOCKED` rather than producing a
synthetic pass.

## Fixture contract

The protected `public-https-gate` GitHub environment supplies the following
values as secrets. The repository does not store their values.

| Value | Required fixture role |
| --- | --- |
| `COGLATAS_PUBLIC_SMOKE_URL` | root public HTTPS origin, reached through the real deployment route |
| `COGLATAS_PUBLIC_SMOKE_EMAIL`, `COGLATAS_PUBLIC_SMOKE_PASSWORD` | dedicated `@example.test` account only; never a staff, student, or production-user account |
| `COGLATAS_PUBLIC_SMOKE_WORKSPACE_ID`, `COGLATAS_PUBLIC_SMOKE_PROJECT_ID`, `COGLATAS_PUBLIC_SMOKE_TASK_ID` | current authorized synthetic Workspace, Project, and Task; its Task has a current clean Project File and can run `FirstPartyProjectFilesRuntimeV1` |
| `COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_WORKSPACE_ID`, `COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_PROJECT_ID`, `COGLATAS_PUBLIC_SMOKE_UNAUTHORIZED_TASK_ID` | synthetic records denied to the test account |
| `COGLATAS_PUBLIC_SMOKE_REVOKED_FILE_ID` | a synthetic Project File unavailable to the test account |

The fixture must remain isolated from real-school data and may accumulate
durable execution results. The test does not upload, delete, change execution
scope, or try to clean up its public fixture. Recreate or rotate only the
dedicated fixture outside the gate when necessary.

## Assertions

The public browser gate verifies all of the following without request mocking
or API interception:

- public HTTP redirects to HTTPS, the HTTPS login response has HSTS, and both
  `.Coglatas.Auth` and `.Coglatas.Csrf` are Secure/HttpOnly cookies;
- invalid login, a mutation without CSRF, malformed JSON, inaccessible
  Workspace/Project/Task/File reads, and logged-out/cleared-session result
  access deny without raw exception or protected identifier disclosure;
- the browser successfully enters the configured Workspace, Project, and Task
  routes, starts the server-authorized Task execution, and obtains a succeeded
  `Project Files Analysis Report` confirming at least one authorized source;
- the same durable result remains available after reload and after re-login,
  while it is inaccessible after logout; and
- the runner never logs fixture IDs, credentials, CSRF tokens, cookies, raw
  responses, or report bodies.

The public mode uses the Playwright list reporter only and disables trace,
screenshots, video, HTML report, JUnit output, and test attachments. The
workflow intentionally uploads no Playwright artifact.

## Release invocation

After deploying the candidate to the public route, run **Public HTTPS
Production Golden Path** (`.github/workflows/public-https-golden-path.yml`).
It is a protected, manually dispatched release gate rather than a pull-request
check because it needs a real external endpoint and synthetic deployment
fixture. Its `public-https-gate` environment must approve access to the
secrets. A missing value, browser setup failure, unreachable endpoint, failed
health probe, or failed journey blocks the release.

For controlled operator use outside GitHub Actions, configure the same values,
set `COGLATAS_PUBLIC_HTTPS_SMOKE=1` and
`COGLATAS_PUBLIC_SMOKE_SYNTHETIC_FIXTURE=1`, then run:

```bash
npm run test:ui:public-https
```

Run `npm run test:ui:public-https:runner` to verify the local configuration
parser without contacting a deployment.
