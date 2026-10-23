# SEC-16 grant expiry and explicit revocation over real HTTP

This candidate addresses selected residual acceptance in #1328 after #1325's
bounded File grant subset. It does not complete #689/#1328, security architecture,
RLS, target qualification or pilot release.

`FileDownloadGrantLifecycleHostedHttpTests` adds four PostgreSQL facts:
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

## Local verification — 2026-10-23

The candidate built in Release with zero warnings/errors. All four owning facts
passed with zero failures/skips against a fresh container using the pinned
PostgreSQL 18.6 image. The database port was bound only to loopback, all databases
were disposable, and the owned container was removed afterward. TRX totals were
recorded for independent reconciliation. This is local HTTP/provider evidence;
hosted exact-candidate CI and resulting-Main qualification remain separate.

Remaining #1328 work includes other applicable File/sharing/lifecycle transitions,
complete actor/route/scenario traceability, PR/Main/deep coverage accounting and
the existing #582 normalized finding-policy integration. Never infer these from
four passing facts. Required checks, disclosure controls and Owner holds persist.
