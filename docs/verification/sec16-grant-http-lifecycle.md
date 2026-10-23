# SEC-16 selected download-grant lifecycles over real HTTP

This candidate addresses selected residual acceptance in #1328 after #1325's
bounded File grant subset. It does not complete #689/#1328, security architecture,
RLS, target qualification or pilot release.

`FileDownloadGrantLifecycleHostedHttpTests` includes four terminal-grant PostgreSQL facts:
FileObject and Attachment routes, each with expired and explicitly revoked grants.
They reuse the actual Web entry point, isolated SEC-02 physical files, production
cookie/session authentication, CSRF middleware and real PostgreSQL. No service or
HTTP response is mocked and no SignalR socket is needed for these HTTP cases.

Each fact requires:

- Missing-CSRF issuance rejection, followed by successful authenticated issuance.
- Byte-identical member and independent-owner downloads before the transition.
- A persisted terminal transition on only the synthetic member grant.
- Two retained-token requests receiving the existing HTTP 400 denial, with bounded
  bodies, no redirects/filename disclosure and no protected content/credential markers.
- Unchanged selected File/Attachment/share/grant state and successful-use audit
  count after each denial, compared without printing protected snapshots.
- Two audit entries with the actual expiry/revocation decision reason and no
  token, File content or storage detail.
- A still-valid member session, unaffected owner download and a new current member
  grant successfully downloading the same physical bytes afterward.

There is no public grant-expiry/revocation writer. Test-only persisted arrangement
sets `ExpiresAt` or `RevokedAt`; real HTTP consumption remains the authorization
oracle. These credentials are opaque POST-body tokens, not signed URLs or one-use
tokens. No product policy, role, endpoint, schema or migration changes.

## Sharing recipient revocation and regrant

Two additional PostgreSQL facts cover FileObject and Attachment download routes
across actual sharing API transitions. Each uploads a new synthetic private
Workspace attachment through HTTP rather than changing a seeded sharing row.
The owner grants the current member through the CSRF-protected recipient API;
the member issues and consumes a real opaque download grant before revocation.
Missing-CSRF sharing is rejected without changing the selected protected state.

The owner then revokes that recipient through HTTP using the current
`SharingVersion`. The retained download credential, fresh grant issuance and
direct download are denied. Grant use and direct download retain HTTP 400;
fresh FileObject issuance returns 400, while Attachment issuance deliberately
uses the existing masked 404 `FILE_DOWNLOAD_GRANT_NOT_FOUND` contract. The
generic 400 proposal was corrected against the actual controller; no API
behavior was changed to fit the test.

Each denial preserves the selected File, Attachment, share and download-grant
state and successful-use audit count, compared as a digest without printing
protected values. Responses remain bounded and contain no content, credential,
storage detail, resource identity, redirect or download filename. The member's
session remains valid, and a newly issued owner grant still reads the physical
bytes after revocation. A fresh owner grant is necessary because sharing-version
changes invalidate every old policy stamp, including the owner's.

After a real HTTP regrant advances the sharing version again, the original
member credential still receives 400 with the persisted `policy_changed` audit
reason. The earlier denial records `current_authorization_failed`. Both audit
records exclude credentials, content and storage detail. A newly issued member
grant downloads the same bytes, proving restored authority without reviving the
old credential. These cases neither arrange nor activate any RLS policy.

## Local verification — 2026-10-23

The candidate built in Release with zero warnings/errors. All four owning facts
passed with zero failures/skips against a fresh container using the pinned
PostgreSQL 18.6 image. The database port was bound only to loopback, all databases
were disposable, and the owned container was removed afterward. TRX totals were
recorded for independent reconciliation. This is local HTTP/provider evidence;
hosted exact-candidate CI and resulting-Main qualification remain separate.

The two sharing-transition facts also passed locally with zero failures/skips
on a newly owned, loopback-only PostgreSQL 18.6 container. An initial Unix-socket
readiness probe observed PostgreSQL's temporary initialization server and a
separate version probe failed during its restart; both HTTP facts subsequently
passed. That readiness-only diagnostic is retained and is not counted as a
test pass. Subsequent qualification requires final TCP readiness and an explicit
fixture-database/version query before running tests. No deadline or assertion
was relaxed.

The initial PR #1334 [CI run 39943790004](https://github.com/NYGsatoshi/Coglatas/actions/runs/39943790004)
failed ReSharper's changed-file check: the optional expected-status parameter
was used only by a precondition assertion. The repair restores the exact-400
helper, keeps Attachment issuance's exact-404/code assertions explicit, and
shares only the unchanged bounded/redacted envelope checks. No suppression,
annotation dependency or assertion relaxation is introduced. Prior local
receipts and this CI failure remain distinct from the repaired commit's
postcommit build and six-fact qualification.

Remaining #1328 work includes other applicable File/sharing/lifecycle transitions,
complete actor/route/scenario traceability, PR/Main/deep coverage accounting and
the existing #582 normalized finding-policy integration. Never infer these from
these selected passing facts. Required checks, disclosure controls and Owner holds persist.
