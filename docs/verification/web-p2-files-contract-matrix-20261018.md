# Web Files v2 — verified API/interaction contract matrix

Evidence date: 2026-10-18. Baseline Main `cb2805bca02cfcf75f4370d167baeb41fc024a9c`.
Design reference: Penpot `CoglatasUI`, revision **339** (file ID
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`).
This document supplements [the original Files audit](web-penpot-files-audit.md)
and the approved [Web/Desktop platform policy](../frontend/platform-policy.md).

**Scope:** existing server-authoritative Angular Web functionality and a
non-authorizing contract review. This document approves **no** API, schema,
permission, role grant, RLS policy, data-model mapping, or release gate.

## Source-confirmed contract matrix

| Design / interaction | Current UI and route | Verified server read/write | Disposition |
| --- | --- | --- | --- |
| FHV01 — File Hub | `FilesPageComponent`, `/files` and `/workspaces/:workspaceId/files` | `GET /api/files?workspaceId&page&pageSize`, `POST /api/files`, `GET /api/search` | **Existing** Workspace-scoped list/upload/search only |
| FHV01 — safe preview / sharing | `FilesPageComponent`, `FilePreviewService`, File Activity | `POST /api/files/{id}/download-grants`, `POST /api/file-download-grants/{grantId}/download`, `GET /api/files/{id}/activity`, version content and sharing endpoints | **Existing**, server reauthorizes, not a global file browser grant |
| FHV01 — folder tree / move | `FileBrowserSidebarComponent`, `FileFolderStore` | `GET/POST /api/file-folders`, `GET /api/files/{id}/location`, `POST /api/files/{id}/move`, `POST /api/file-folders/{id}/move` | **Partial**: folder metadata and moving exist, but no verified folder-filtered File-list endpoint |
| FHV01 — Recent | `FilesFacade`, Workspace Files | `GET /api/files` paged | **Existing**, a bounded Workspace inventory—not a globally ranked timeline |
| FHV01 — Starred / Shared shortcuts | `FilesPageComponent.browserShortcut` | No inspected authoritative Starred/Shared query, persisted star command, or cross-scope listing | **Unavailable**; do not convert absence of an API into a zero-result claim. P2 fix supplies a labeled unavailable state, clear-search handoff, and navigation back to Recent |
| FHV02 — Project Files | No dedicated current Project Files route | Existing Workspace list, File metadata/move; Task execution ProjectFile materialization is **not** general list API | **Held**: new exact Project-scoped paging/filter/read projection requires explicit business/API review |
| FHV03 — Artifact Repository | `/artifacts/:artifactId` narrow detail; Report Reader in Project/Task | `GET /api/projects/{projectId}/artifacts`, `GET /api/artifacts/{id}`, `GET /api/artifacts/{id}/versions` and authorized version download | **Partial**: per-Project Artifacts API is approved/existing; a global repository, provenance index, faceted search or cross-Project aggregation is **not** |
| FHV04 — File linking target picker | Existing Task attachment picker is scope-specific | Existing Task/File association endpoints do **not** establish an arbitrary Project/Artifact/Task link/unlink contract | **Held**: target identity, grants, idempotency and relation lifecycle approval required |

### Verified server-side ownership

- `FilesController.List` takes **Workspace ID**, page and pageSize. Its DTO does not
  accept `folderId` or `projectId` for an authoritative filtered list.
- `FileFoldersController.List` returns folder metadata; folder-content listing
  must not be simulated by locally filtering a single page of Workspace files.
- `ArtifactsController.List` returns Project-scoped `ArtifactListItemResponse`.
  `ArtifactService.ListAsync` calls `CanViewProject` before reading and
  excludes soft-deleted artifacts. This does not authorize a cross-Project
  repository, bulk linking, or display of inaccessible counts.
- Existing File download grants and Activity are purpose/scope-bound. They are
  not reusable as general-purpose authority for arbitrary target association.
- Closed [#462](https://github.com/NYGsatoshi/Coglatas/issues/462) concerns
  **server-owned immutable Task execution input materialization**, not a File
  browser list, free-form linking API, or approval to expose raw File paths.

## Strict acceptance constraints for future proposals

| Proposal | Required contract to approve | Required negative tests | State |
| --- | --- | --- | --- |
| Folder-scoped file listing | Server-owned folder/Workspace association, zero-count semantics, pagination, stable order, redacted metadata, current session grant | Unknown folder, mismatched Workspace, cross-Tenant, stale folder move, revoked File, incomplete partial page | **HOLD** |
| Project Files listing | Project authority, optional folder facet, File–Project relation identity, explicit membership/visibility, page cursor/count policy | Cross-Project substitution, Project Draft/suspended, revoked File, attacker-supplied File ID, disclosure through aggregate counts | **HOLD** |
| Starred / Shared | Canonical per-user preference and sharing projection, scope/expiry policy, authorized list filtering, revocation/cancel | Cross-user preference, tenant switch, no-grant listing, removed recipient, expired shared grant, stale cache | **HOLD** |
| Artifact Repository | Decide Project-only vs tenant/workspace-wide, version/provenance keys, high-volume pagination, RLS/API boundaries | Cross-Tenant, cross-Project, deleted/revoked versions, hidden names/counts, historical access changes | **HOLD** |
| Link File picker / command | Typed target and relation ID, current capability grant, optimistic concurrency, idempotency, link/unlink lifecycle and audit | Duplicate link, stale version, wrong Task/Project, revoked sharing, rejected file scan status, replay | **HOLD** |

No proposed endpoint path in this matrix is an approved API. Source of Truth
remains the current SPEC + server implementation, with separate Owner decision
for any new command/data model/security mapping.

## Permitted implementation order

1. **Current PR:** remove false `0 files` from unsupported Starred/Shared views;
   preserve actual Workspace search, Recent, uploads, and permissions.
2. **Next API-neutral slice:** improve Project-scoped Artifact presentation by
   using only `GET /api/projects/{projectId}/artifacts`, with strict Project
   identity equality, fail-closed response validation and explicit list limits.
   This is not a global Artifact Repository.
3. **Only after Owner authorization:** implement new Files/Folder/Project/File
   linking contracts, immutable compatibility tests, and authorized Angular UI.
4. Complete [#482](https://github.com/NYGsatoshi/Coglatas/issues/482) real
   browser/HTTPS regression with both themes, 320px, keyboard, status and
   denied API transitions. Do not claim live production PASS from a mocked test.

Related: [#1238](https://github.com/NYGsatoshi/Coglatas/issues/1238),
[#817](https://github.com/NYGsatoshi/Coglatas/issues/817),
[#689](https://github.com/NYGsatoshi/Coglatas/issues/689).
