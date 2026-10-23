# SEC-16 selected Attachment grant actor and scope controls

Tracks [#1349](https://github.com/NYGsatoshi/Coglatas/issues/1349).
Source baseline: Main `fe60f94197296fcee064ba0272429429d8a6cdc2`.
Status: **candidate verification in progress; full SEC-16 incomplete; release NO_GO**.
This slice neither closes the umbrella nor changes the closed history of
#689/#1328. Formal mapping/RLS/staging and the #1320/#1326 merge holds remain.

## Inventory before implementation

The nine existing `FileDownloadGrantLifecycleHostedHttpTests` facts cover grant
expiry/revocation, sharing-recipient revocation/regrant and File/Attachment DELETE
writers. Their fact bodies and assertions are unchanged. A partial test-class
declaration permits the new file to reuse their real Web/PostgreSQL fixture,
physical-byte positives, bounded denial envelopes and protected-state digests.

`FileDownloadGrantBoundaryTests` provides unit actor/token/scope controls.
FCI-07's `createSec16GrantJourney` exercises the FileObject credential route;
its wrong-actor/random-token controls do not establish Attachment-route proof.
The Direct/Group hosted tests cover participant removal and stale Attachment
authority, rather than the static actor/scope transfer cells below. No existing
real HTTP Attachment case was found pairing two live same-actor credentials for
cross-resource substitution. These distinctions prevent duplicate coverage claims.

## Four current-contract cells

All methods below belong to `FileDownloadGrantLifecycleHostedHttpTests` in
`PostgreSql/FileDownloadGrantActorScopeHostedHttpTests.cs`.

| Cell / exact method | Arrangement and observed boundary required |
| --- | --- |
| `AttachmentGrantRejectsAnotherAuthorizedActorWithoutRevokingTheOriginalCredential` | Alpha member issues a grant; an independently authenticated Alpha owner who can read the same File cannot consume that member-bound credential: two HTTP 400 denials and exact `actor_mismatch` audits. The owner's own issuance/direct access remain 200; they are not incorrectly treated as forbidden. |
| `AttachmentGrantRejectsSameTenantOtherWorkspaceActorWithoutProtectedEffects` | Existing HTTP APIs create a new owner-only Alpha Workspace and upload its private Attachment. Persisted membership proves that the Alpha member belongs to its original Workspace, not the target. Owner-bound credential transfer receives 400/`actor_mismatch`; fresh Attachment issuance is masked 404 and direct download is 400. This transfer assertion alone does not isolate Workspace authorization: the separate fresh/direct assertions establish that boundary. |
| `AttachmentGrantRejectsForeignTenantOwnerWithoutProtectedEffects` | Independent Beta owner belongs to its Beta Workspace and retains access to its own physical File. Alpha credential transfer/direct access receive 400; fresh issuance receives masked 404. The tenant-filtered grant lookup returns missing before target grant-use auditing, so no invented `tenant_mismatch` audit is required. HTTP assertions and authorized positives, not absent audits, establish the control. |
| `AttachmentGrantRejectsAnotherResourceCredentialForTheSameAuthorizedActor` | Alpha member is authorized for two actual Attachments with distinct valid grants/tokens. Token A on grant B's route and token B on grant A's route each receive 400 and exact `invalid_grant_token` audit. Both legitimate credentials still download their original physical bytes afterward. No persisted credential/resource row is tampered with. |

The current `AttachmentsController` contract is authoritative:

- `POST /api/attachments/{attachmentId}/download-grants`: 200 on allowed
  issuance; denied issuance is 404 `FILE_DOWNLOAD_GRANT_NOT_FOUND`.
- `POST /api/attachment-download-grants/{grantId}/download`: denied use is 400.
- `GET /api/attachments/{attachmentId}/download`: denied direct access is 400.

Each actor-transfer scenario proves the denied actor's current `auth/me` identity
and valid independent cookie/CSRF context. A separate missing-CSRF 403 control is
followed by restored-CSRF 400 authorization requests; it supplies no authorization
denial credit. The resource-substitution scenario uses the same valid member
cookie/CSRF for successful and denied requests.

Before and after denials, the original actor downloads through the same retained
credential. Another current authorized credential also reads physical bytes;
Beta uses its own distinct fixture bytes. This preserves the current reusable
grant contract and rules out missing-resource, invalid-session or failed-storage
false positives. Both selected File/Attachment/share/grant snapshots and
successful-use counts remain unchanged across denied operations. Denial envelopes
exclude credentials, IDs, filenames, content, storage markers and redirect headers;
audits check exact current reasons without printing protected metadata.

## Verification and limits

The initial four-fact diagnostic built in Release with zero warnings/errors and
executed four passes, zero failures/not-executed, against a newly owned pinned
PostgreSQL 18.6 container. Final TCP/database/version/privacy probes and cleanup
were captured. The working source was intentionally marked dirty and its assembly
revision still identified baseline Main; this is focused diagnostic evidence,
not frozen-source qualification. No failed test or preparation was relabeled PASS.

Local results require actual ASP.NET Core, migrated PostgreSQL and physical files,
not InMemory, HTTP mocks or unconfigured PostgreSQL early returns. Retain the exact
source, postcommit build, canonical/test-loaded assembly bytes and PE version,
database/version/final TCP/privacy checks, explicit commands, named TRX outcomes
and owned-container cleanup. Precommit diagnostic runs do not establish immutable
candidate qualification. Hosted routing, independent review and resulting-Main
execution remain separate, and no provider operation is performed by this slice.

Remaining #1349 cells include broader Owner/Member/ReadOnly/removed/unrelated
roles, parent Conversation/Project/Task combinations, grant purpose and allowed
operation boundaries, general sharing-policy PUT/recipient kinds, logical
placement, historical content and mixed-scope batch deletion. These four facts do
not qualify those cells, the full PR/Main/deep matrix, #582's common finding and
Medium-ratchet model, product RLS, target HTTPS/recovery or release readiness.
No new endpoint, writer, one-use token, public signed URL, scanner policy,
authorization rule, schema, deadline, retry or required check is introduced.
