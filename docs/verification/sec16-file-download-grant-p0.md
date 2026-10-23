# SEC-16 — File download-grant real-stack P0 candidate

Status: **Draft, unmerged, NOT_YET_RUNTIME_VERIFIED**. Owner issue: #689.

This patch extends the existing required `FUNC-AUTHZ-002` security-negative
test on the isolated SEC-02 Alpha/Beta fixture. No new ungated test title and
no HTTP mocks. The pre-existing FCI-07 Compose runner is the execution owner.

## Implemented route inventory

| Operation | Existing current endpoint |
| --- | --- |
| Issue FileObject grant | `POST /api/files/{fileObjectId}/download-grants` |
| Consume opaque grant | `POST /api/file-download-grants/{fileDownloadGrantId}/download` |
| Direct FileObject download | `GET /api/files/{fileObjectId}/download` |
| Attachment-specific grant (SEC-05 coverage) | `POST /api/attachments/{attachmentId}/download-grants` |
| Attachment-specific consumption | `POST /api/attachment-download-grants/{fileDownloadGrantId}/download` |

The current token contract is an opaque POST-body credential, **not** a public
signed-URL endpoint. Do not infer that a signed URL product flow exists.

## Blocking representative P0 cases

- Positive issuance and synthetic byte-identical download for Alpha member.
- Same-Tenant different actor, foreign-Tenant owner, invalid token and wrong
  grant ID are denied without exposing protected file metadata or redirects.
- Foreign actor cannot issue a grant for an Alpha FileObject.
- The existing FCI-07 test revokes the same Alpha member Workspace membership.
  Without ending the Tenant session, the previously valid grant, a newly
  requested grant and direct download all receive bounded HTTP 400 denials.
- The still-authorized Alpha owner downloads the same synthetic file after
  revocation (positive control, so absent resource/failed transport cannot
  produce a false-green negative).
- No cookie, token, file bytes, resource identity, storage key or signed URL
  is written to reporter evidence. Evidence contains boolean checks only.

## Boundaries and remaining work

Current state is source implementation only. CI and isolated PostgreSQL +
real-backend FCI-07 execution on **exact PR HEAD** are required before PASS.
Expiry/revocation-by-grant, archived/moved/versioned File lifecycle, sharing
policy transitions, and longer role matrices need independent acceptance.
SEC-17 SignalR, full SEC-ARCH/RLS/activation approval and staging/rollback
checks remain separate pilot-release blockers.

No production endpoint, grant policy, schema, migration, required CI threshold,
license, deployment or privileged credential change is authorized by this PR.
