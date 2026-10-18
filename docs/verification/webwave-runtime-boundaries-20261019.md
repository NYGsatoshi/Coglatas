# Web wave: current HTTP authorization and Unicode boundaries

Status: **IMPLEMENTED_AND_VERIFIED locally; hosted CI and independent review pending.**
**PRE-AVALONIA SEC-ARCH: BLOCKED.** This record grants no specification,
mapping, role, policy, key-custody, migration, RLS or activation approval.

## Source and executed changes

- Product base: `9b44d631df88cdc2f15a45b6278b3e1b2552a8df`.
- Download HTTP regression: `48236eb7a24582f83a3f5692ad07840054b62aa9`.
- Failing Unicode regression: `a15e130bdae47e09d9b1b193d1d99b2c3c7b01b6`.
- Repaired and tested code: `eaf7c7a3da428d5db7306a8c4255fb43bc3fb88e`.
- Environment: Windows, .NET SDK 10.0.401, .NET runtime 10.0.12,
  EF Core InMemory 10.0.12. Npgsql is not executed in these selections.

The two new HTTP cases exercise the existing direct File download and issued
download-grant routes through current controllers, Application authorization,
concrete EF repositories, tenant middleware and synthetic authentication.
Each has a successful same-operation download, then persists Workspace-member
suspension in a separate scope. The previously issued grant and a direct
download both reauthorize current membership. Denied download responses match
the error subtree of a nonexistent target, contain no token, original name,
storage key or bytes, and have no download disposition. Metadata reads also
fail closed. The synthetic storage adapter records no additional read;
selected File/Attachment state fingerprints in both Tenants remain unchanged.
Restored membership permits the same operation again.

The authorized download asserts `Cache-Control: no-store`, `Pragma: no-cache`
and `Expires: 0`. Captured request logs are nonempty, contain no exception/error
records during these ordinary controls, and exclude the token, filename,
storage key and content. Persisted denial AuditLog and SecurityEvent records
are present and checked for the same protected values. Denial audits are
expected effects; the denied request is not described as a database no-op.

These are Kestrel/EF InMemory observations with a synthetic authentication
handler and storage adapter. They do not establish PostgreSQL transactions,
cookie-authenticated File composition, real filesystem effects, browser cache
clearing or the complete authorization/logging matrix. The separate existing
cookie suite uses actual cookie/session services with InMemory. Unhandled
exception logging across every request path remains unqualified; the existing
global exception middleware still logs exception objects for server faults.

The Unicode regression demonstrated that truncation at 260 UTF-16 code units
split a valid supplementary character at index 259. Its pre-fix run passed 12
cases and failed the boundary case. The repair backs up one code unit only
when the cutoff separates a high/low surrogate pair. The existing maximum,
path/control-character sanitization and all other behavior remain unchanged.
Tests cover BMP names, supplementary characters on both sides of the cutoff,
decomposed combining characters, mixed paths/NUL and long BMP names. This is
display-name sanitization coverage, not PostgreSQL collation or IME evidence.

## Executed results and private evidence

| Selection | Code revision | PASS | FAIL | SKIP | Result |
| --- | --- | ---: | ---: | ---: | --- |
| Initial new download HTTP cases | Working test source before `48236eb7` | 2 | 0 | 0 | PASS; an initial nullable compiler warning was repaired before the broader run |
| HTTP, grant and exception subset | `48236eb7` | 145 | 0 | 0 | PASS |
| Unicode regression before production repair | `a15e130b` | 12 | 1 | 0 | FAIL reproduced and retained |
| Final combined selection | `eaf7c7a3` | 165 | 0 | 0 | PASS; build emitted no warnings |

The final selection contains 25 `AuthSecurityHttpTests`, 99
`HttpTenantIsolationTests`, 16 fake-service `FileDownloadGrantBoundaryTests`,
5 unit `GlobalExceptionHandlingMiddlewareTests`, 13 `FileNameSanitizerTests`
and 7 fake-service `CommunicationPollingServiceTests`. Thus 124 cases are
HTTP tests; the other 41 are unit/service controls. The final 165 supersede,
and must not be added to, the overlapping 145 or initial 2. Earlier unchanged
Main observations are separate historical evidence, not additional coverage.
No PostgreSQL selection ran and no PostgreSQL SKIP is counted as a PASS.

Raw TRX and logs stay outside the Product checkout in the private wave evidence
directory. No raw bodies, identities or machine paths are published here.

| Relative private evidence path | SHA-256 |
| --- | --- |
| `e-runtime-results/runtime-http-and-file-boundaries.trx` | `955e613e3e686ce07a1d0597417439225cefbd5bccafe33efe89c099e97daffc` |
| `e-runtime-results/runtime-unicode-prefx.trx` | `100e4ea15f47ae053251d35afb6852b46c1fca8f9d7a9a255a03a74012410b48` |
| `e-runtime-results/runtime-final-boundaries.trx` | `b276d3d878076cd0e60acffc75376aea1f366db71dec865f05ddd73bebedf88b` |

Logs are `e-runtime-http-and-file-boundaries.log`,
`e-runtime-unicode-prefx.log` and `e-runtime-final-boundaries.log` in the same
private directory. Local artifact hashes preserve identity; they do not provide
trusted hosted execution attestation or owner approval.

Reproduce the final selection from the exact candidate:

```powershell
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj --filter 'FullyQualifiedName~Coglatas.Tests.Auth.AuthSecurityHttpTests|FullyQualifiedName~Coglatas.Tests.Tenancy.HttpTenantIsolationTests|FullyQualifiedName~Coglatas.Tests.Auth.GlobalExceptionHandlingMiddlewareTests|FullyQualifiedName~Coglatas.Tests.Files.FileDownloadGrantBoundaryTests|FullyQualifiedName~Coglatas.Tests.Files.FileNameSanitizerTests|FullyQualifiedName~Coglatas.Tests.Communication.CommunicationPollingServiceTests' --logger trx
```

## Coverage and retained holds

| Area | Disposition | Evidence or remaining acceptance |
| --- | --- | --- |
| Existing selected HTTP authorization, scoped data and safe errors | VERIFIED | Final 124 HTTP cases; InMemory/authentication qualifications above apply |
| Ordinary denied File operational/audit/security records and storage-read boundary | VERIFIED | Two new theory cases, with live positive controls and restored access |
| Existing optimistic conflict envelopes | VERIFIED | Current notification-preference and Project-lifecycle HTTP cases in the selected suite; no concurrent PostgreSQL lock claim |
| Filename Unicode scalar boundary | VERIFIED | Genuine pre-fix failure and 13 final sanitizer cases |
| Existing communication cursor actor/resource scope | VERIFIED | Existing service/HTTP tests only; neither a new Files cursor nor signing-key evidence |
| SQL plans, cardinality/statistics and live PostgreSQL conflicts | BLOCKED_BY_ENVIRONMENT | No disposable connection; Docker Linux engine pipe unavailable. No plan, statistics, data volume, planner setting or timing was observed |
| Existing isolated PostgreSQL/RLS fixtures | BLOCKED_BY_ENVIRONMENT | `POSTGRES_TEST_CONNECTION_STRING` absent; fixtures not selected. Do not reuse historical Spec #290 limited cases as current runtime qualification |
| Mapping, all-table policy/role/GRANT design and product RLS activation | BLOCKED_BY_APPROVAL | Existing SEC-ARCH owner gates remain; environment availability alone cannot clear them |
| Files cursor API and operational cursor keys | BLOCKED_BY_APPROVAL | Current File list/search contracts use page/pageSize. No approved Files cursor/key contract is inferred or introduced |
| Operational key continuity/restart/rotation | NOT_EXECUTED | No production key/configuration changes or persisted-key restart qualification; existing cookie tests do not establish this claim |
| Cookie-authenticated File workflow on actual PostgreSQL | BLOCKED_BY_ENVIRONMENT | Requires an approved disposable real-stack fixture with synthetic data |
| Public HTTPS deployment gate | BLOCKED_BY_ENVIRONMENT | Target fixture not configured; no exposure/deployment attempted; local work continues |
| Hardware latency/throughput assurance | BLOCKED_BY_APPROVAL | Existing indefinite #1128 hold; no numerical assurance resumed |
| Complete SEC-ARCH acceptance and Avalonia start | BLOCKED_BY_APPROVAL | All fifteen [preflight exit conditions](security-architecture-preflight.md) remain independently required |

No DB migration, DDL, role, GRANT, Issuer, key-custody, product RLS, Draft/Links
endpoint, dependency, runtime enforcement or deployment configuration changes
are included. Private Spec content is not copied into this Product evidence.
Draft/Links, Shared, Filters and Artifacts owner/publication gates remain separate.
See the [current fixture limitations](security-architecture-fixtures.md).

## Limited independent frontend boundary review

Source inspection of A `c3dab4d7` covered new routing/navigation and Messaging
API/facade/page changes. B `d2c82ce9` inspection covered the memory-only Draft
repository, NgRx ComponentStore scope/generation clearing, unavailable remote
seam and opt-in synthetic Links. C `7f66888e` inspection covered opaque scoped
Starred IDs and fresh existing File GET reauthorization. No new formal API,
DB/RLS activation or private Spec material was found in these inspected paths.
The Coordinator separately repairs C's exact-key ordering; this old-head
inspection does not certify that later repair. This is limited source review,
not browser acceptance, hosted CI, CODEOWNERS approval or specification approval.
