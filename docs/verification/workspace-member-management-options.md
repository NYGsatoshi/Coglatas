# Workspace member management reads

The backend adds two management previews beside the ordinary member directory:

- `GET /api/workspaces/{workspaceId}/members/management/options?userId={optionalGuid}`
- `GET /api/workspaces/{workspaceId}/members/management/candidates?q={prefix}&take=20&afterUserId={optionalGuid}`

Successful responses are plain camelCase DTOs, without a success envelope.
Options contain `workspaceId`, `canAddMember` and an optional target. The target
contains its opaque `userId`, numeric `currentRole`, `membershipStatus`,
`canChangeRole`, `allowedRoles` and `canRemove`. Candidate pages contain
`workspaceId`, `items`, `hasMore` and `nextAfterUserId`; each item contains only
`userId`, `displayName` and `allowedRoles`. The next position is the last returned
ID only when more rows exist; otherwise it is null. Neither read returns email,
account/Tenant roles, joined dates or a total directory count.

Both reads require a current active account/Tenant identity and the existing
Workspace management authority. A missing or invisible Workspace is masked as
404; a visible Workspace without management authority returns 403. Archived
Workspaces have no mutation preview. Active system administrators (`SystemRole.SystemAdmin`) retain the
existing active-Workspace exception while still needing normal Tenant admission.
The existing five Workspace roles remain Owner0, Admin1, Adviser2, Member3 and
ReadOnly4. No assignment hierarchy, last-Owner restriction or ordinary member
self-leave command is introduced.

Candidates must have active, undeleted accounts and Tenant records and active
Tenant membership. Any existing Workspace relationship excludes a candidate,
including Pending or Suspended membership. Search uses a literal trimmed
display-name prefix of 2–100 Unicode scalars, default20/max50 rows, stable UUID
ordering and a `take+1` lookahead. NUL and malformed UTF-16 input are rejected.
Every continuation request reapplies the complete Tenant/Workspace/eligibility
predicate; a cursor is a position, never authorization.

Target options preserve current role-only PATCH semantics: omitted status stays
unchanged. Active targets with inactive account/Tenant admission cannot receive
an active-role change, but readable targets can still be removed. Unknown
persisted role/status values produce an unavailable result instead of invented
roles. Existing POST/PATCH commands reject undefined role/status numbers before
mutating state. Valid Owner changes and manager self-removal remain supported.

Options are advisory snapshots. Existing commands independently reauthorize and
stage canonical General membership, audit and authorization outbox changes in
their existing unit of work. A preview reserves neither identity eligibility nor
write permission, and does not promise linearizable revocation.

## Private caching and operational logs

The exact new GET routes return `Cache-Control: private, no-store`, including
denied responses. Their completion logger records only a finite operation token,
status and trace ID. It records no query, display name, candidate ID or response
body. The canonical configuration keeps `Microsoft.AspNetCore` at Warning and
does not enable sensitive-data SQL logging. Actual Kestrel log capture verified
safe 200/403 operation events with synthetic query/name/identity values absent.

Hosting RequestStarting can run before application middleware and include the
full query when its category is raised. This qualification therefore requires
the canonical logging settings; custom framework category overrides, body-log
providers and reverse-proxy access logs require separate review. Operators must
exclude raw queries and directory response data from those logs. The application
does not claim to redact arbitrary debug logging or external proxy output.

## Local evidence and remaining qualification

On source `9243f7baf8fe28460e13063a0f390c6d670b9b09`, native .NET SDK10.0.402
and isolated PostgreSQL18.6 ran 17/17 owning Kestrel/cookie/CSRF cases, 84/84
existing Workspace regressions and 18/18 HTTP security-policy tests, all with
zero skips. The 84-case selection contains15 PostgreSQL cases; the remaining
counts are mixed application/controller/policy tests. All seven actual DLLs
have the tested commit's embedded version. Raw TRX, source guards and local
receipts are retained privately.

The original test-only baseline had2 actual failures: the absent read route and
role999 accepted/persisted by the existing command. The first implementation
trial retained1pass/1private-cache assertion failure. Two subsequent attempts
failed compilation and executed no tests. These records remain separate.

These local results qualify the backend for independent review. They establish
no new hosted CI, full-suite, combined frontend/backend browser, production
logging, target deployment or school-user/public release outcome.
