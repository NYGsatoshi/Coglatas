# SEC-16 server-owned selection scope and retained-handle control

This bounded follow-up addresses the current
[file batch selection contract](../contracts/file-batch-selection-v1.md) and
the residual obligations recorded under #614, #1328 and #1349. Closed issue
lifecycle states do not replace this exact-source runtime evidence.

The new PostgreSQL fact is
`FileDownloadGrantLifecycleHostedHttpTests.ServerOwnedBatchSelectionRejectsActorAndTenantTransferAndPreservesOriginalOneUse`.
It extends the 23 existing real HTTP lifecycle facts through a new partial file.
All original tests, helpers, product APIs, authorization, policies, migrations,
roles/grants, scanner thresholds and Owner HOLDs remain unchanged.

## Bounded contract

- Real HTTP creates a fresh Alpha Workspace, admits the existing active Member,
  and uploads two Member Files beside one Owner File. Separate Alpha Workspace
  and Beta Workspace Files are also created through the actual API. The Beta
  content differs; authenticated physical-byte positives and persisted
  Workspace-owner/date-facet arrangements establish live controls.
- Capture receives only the current Workspace and an implemented date facet;
  it receives no client File identities. The persisted snapshot must contain
  exactly the three intended Workspace Files, current Tenant/actor/scope/facets
  and an unconsumed version 0. Other Workspace/Tenant resources stay excluded.
  Same-Tenant wrong-Workspace and foreign-Tenant Workspace captures are
  independently denied with confirmed absent active memberships.
- A missing-CSRF 403 is a separate control. With valid credentials/CSRF, an
  authorized Alpha Owner cannot consume the Member's handle; a separate Beta
  Owner is likewise denied through its own Tenant session. Both return the
  established 400 canonical route code without protected resource/credential
  disclosure. The Beta control combines actor/Tenant exclusion; it is not a
  same-user multi-Tenant isolation matrix.
- Denials preserve complete scalar FileObject/Attachment/sharing/download-grant
  state, complete snapshot/items state and the identity inventory of relevant
  successful audit records. Snapshots are serialized and hashed in memory.
  Assertions print only fixed diagnostics, never rows, tokens, keys or bodies.
- The original Member then consumes the same snapshot once. Its two uploads are
  actually soft-deleted with two exact business audit identities; the Owner's
  upload returns the existing bounded `NotDeleted` outcome and remains intact.
  This verifies current per-item authorization and best-effort, non-atomic
  behavior. Replay is denied without further state changes. Owner/other-scope
  authorized physical-byte controls remain usable.

Source: Main `19c36836fbfde46cba4d566a4c471cf8dd973c31`.
The default-branch search index still referenced 7499 during inspection; the
7499→19c delta is exclusively four frontend package/lock files. The checked
backend/service/fixture/contract blobs are identical.

## Routing and limits

The fact retains `PostgreSqlFact`, `PostgreSQLIntegration` and `Scope=SEC16`.
The existing `mark_backend_test_file` router selects the PostgreSql namespace
and migrations for this folder; its filter includes the compiled partial-class
method regardless of the new filename.
Its namespace is `Coglatas.Tests.PostgreSql`. The closed SEC-ARCH collector only
registers its architecture/replay/explicit reused catalog; this File method is
outside that catalog and is ignored, so no collector registration or parser
expansion is needed. The actual named fact and zero-skip execution must still be
reconciled in native hosted PostgreSQL logs/TRX verification on the exact head
and actual tested merge; source inclusion and a green unrelated check are
insufficient. No local execution is claimed in the unavailable executor.

This is not the full role/scope/route matrix, concurrent revocation proof,
all-table RLS, a common SEC-08 finding adapter, Medium approval, upload-intake
activation or release acceptance. No new mixed-ID capture input, atomic batch,
public signed URL or one-use download-token rule is introduced. The selection
handle itself is already single-use; download credentials retain their existing
contract. Hosted qualification and independent review remain pending.

## Retained first-source quality outcome

Source `2bbee2d047d848ad8f58323bccde069c9bf51707` has successful shared
compilation but a retained ReSharper failure in
[CI40445975364/1, job121998021652](https://github.com/NYGsatoshi/Coglatas/actions/runs/40445975364/job/121998021652):
one changed-file warning, “Captured variable is disposed in the outer scope.”
SARIF artifact12707988726 has archive SHA-256
`4b5c3b3431068412097a1af54c905fed830baf8ffd18ba6de625c28b965e2f6b`.
The native annotation location is unavailable through the current connector;
source inspection identifies the nested audit subquery closing over the
await-using context. The forward test-only repair materializes its bounded
matching grant-ID set before filtering successful audit identities. Complete
entity hashes and all authorization assertions are preserved; no inspection is
suppressed and the first whole CI is not relabeled PASS. Actual named runtime
and new exact-head quality qualification must be recorded separately.

The same first source later completed its actual backend execution successfully:
[backend job121998021544](https://github.com/NYGsatoshi/Coglatas/actions/runs/40445975364/job/121998021544)
checks out merge `d4e81dc33273b7840ec257d179a05f099ae4cc8c`, parents
`19c36836fbfde46cba4d566a4c471cf8dd973c31` and
`2bbee2d047d848ad8f58323bccde069c9bf51707`, with head-equivalent tree
`c0de631422a2276aea40f154f15da2883946f26a`. PostgreSQL 18.6 is configured;
the exact new fact passed in the native console rounded 12 seconds, all 24 File
lifecycle facts appear Passed, and 467 selected backend tests plus the native
TRX verifier passed with no failed/skipped cases. This does not mean every
selected test uses PostgreSQL, nor does it qualify architecture execution.
Successful raw TRX was not published. The closed architecture collector remains
Advisory/UNVERIFIED with pre-Avalonia BLOCKED. The whole first run remains
FAILURE because of its independent ReSharper warning. This predecessor runtime
cannot qualify the forward quality-repair head; that source needs fresh checks.
