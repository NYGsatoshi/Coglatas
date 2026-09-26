# Backend Application Logic Audit

Audit date: **2026-06-19**.

Repository: `NYGsatoshi/Coglatas`.

## Scope and constraints

This was a read-only audit of:

- controllers;
- Application services;
- request validation;
- error handling;
- null-reference risks;
- async/await usage;
- file upload and download behavior;
- project, chat, announcement, event, form, and related business logic;
- dependency-injection configuration;
- HTTP status-code consistency.

The audit did not modify authentication logic, database schema, application UI, C# source, or runtime behavior. It did not create a pull request. Recommendations below are patch suggestions, not implemented feature or architecture changes.

## Verification

- `dotnet build Coglatas.slnx --configuration Release --no-restore --disable-build-servers -m:1` completed with 0 warnings and 0 errors.
- The same-turn `dotnet test` attempt built the solution, but the test runner was prevented from opening its local IPC socket by the audit sandbox. No fresh test-pass claim is made for this audit.
- Existing test files and their coverage were inspected directly.
- The worktree remained clean after the audit.

## Severity summary

| Severity | Count | Main areas |
| --- | ---: | --- |
| Critical | 2 open + 2 resolved | conversation persistence and chat attachments remain open; Announcement visibility is resolved by the current WS-01-BE candidate and Project-derived Search authorization is resolved by PR #281 |
| High | 8 | detached EF mutations, concurrent EF use, file consistency, read-state integrity, notification targets/limits, duplicate task rows, HTTP contracts |
| Medium | 7 | route-parent validation, PATCH clearing, concurrency, membership semantics, listing filters, DI registration, query efficiency |

## 1. Critical backend bugs

### BE-001: Scoped announcements can be disclosed to the whole workspace

- Priority: critical.
- Status: resolved by the current WS-01-BE candidate.
- Affected pages: Announcements and Search.
- Exact files and methods:
  - `src/Coglatas.Application/Announcements/AnnouncementService.cs`
    - `ResolveCreateScopeAsync`
  - `src/Coglatas.Infrastructure/Persistence/AnnouncementReadScope.cs`
    - `VisibleAnnouncementsFor`
  - `src/Coglatas.Infrastructure/Persistence/AnnouncementRepository.cs`
    - `ListVisibleAsync`, `IsVisibleToUserAsync`
  - `src/Coglatas.Infrastructure/Persistence/DbSearchService.cs`
    - `SearchAnnouncementsAsync`
- Historical evidence:
  - Group and channel announcements store the owning `WorkspaceId` in addition to their narrower scope IDs.
  - Visibility predicates combine workspace, group, and channel checks with OR conditions.
  - An active workspace membership therefore satisfies the workspace branch even when the announcement belongs to a group or private/confidential channel.
- Impact:
  - Users outside a target group or private/confidential channel can receive announcement metadata and body content through list, detail, or search APIs.
- Implemented resolution:
  - Make visibility branches mutually exclusive.
  - If `ChannelId` is set, authorize only against the stored channel and its channel-type rules.
  - Else if `GroupId` is set, authorize against the stored group.
  - Else if `WorkspaceId` is set, authorize against the workspace.
  - The Workspace dashboard unread aggregate composes the same predicate and returns only a grouped numeric count.
  - List, detail/read-status, Search, and dashboard integration tests cover Group and private-Channel non-disclosure on PostgreSQL.
- Suggested tests:
  - Workspace member outside a group cannot list or get a group announcement.
  - Group member outside a private channel cannot list, get, or search its announcement.
  - Public/announcement channel members retain expected access.
  - Platform/system-admin behavior remains explicit and tenant-scoped.
- Suggested issue: **Prevent workspace-wide disclosure of scoped announcements**.

### BE-002: Project-derived Search authorization parity

- Priority: critical.
- Status: resolved under the current canonical Project read policy. WPC-02A
  persists and enforces canonical Project Visibility; legacy `NULL` remains an
  explicit compatibility state rather than an inferred classification.
- Affected pages: Search, Projects, Tasks, Artifacts, and project activity/comment results.
- Exact files and methods:
  - Historical implementation:
    - `src/Coglatas.Infrastructure/Persistence/DbSearchService.cs`
    - removed private `VisibleProjects`
    - `SearchProjectsAsync`
    - `SearchTasksAsync`
    - `SearchArtifactsAsync`
    - `SearchActivityLogsAsync`
    - `SearchCommentsAsync`
  - Canonical comparison:
    - `src/Coglatas.Application/Projects/ProjectAuthorizationService.cs`
    - `CanViewProject`
- Historical evidence:
  - the removed Search-only predicate granted every active Workspace member
    operational Project visibility even when `CanViewProject` denied a grouped
    Project;
  - Project, Task, Artifact, ActivityLog, Comment, and project-bound Message
    results inherited that wider scope.
- Resolution:
  - `ProjectReadScope.VisibleProjectsFor` is the reusable EF/PostgreSQL-
    translatable form of the current `CanViewProject` predicate;
  - the non-Archived Project list and every Project-derived Search query use
    it; `ListableProjectsFor` alone adds non-deleted Archived history for current Workspace
    members with explicit Project membership;
  - Messaging retains readable Conversation membership and then applies the
    same Project scope;
  - production PostgreSQL Conversation detail/list/count, polling, and Message
    Search use one set-based recursive Thread ancestry boundary, reject missing,
    inconsistent, cyclic, and deeper-than-32 scope, and never persist a child
    beyond the readable limit;
  - Message Search resolves the complete shared readable-Conversation ID set,
    then intersects it with all matching Messages before `CreatedAt DESC, Id ASC`
    and the final result limit; the former arbitrary first-100 Conversation
    authorization cutoff is removed;
  - My Tasks applies the same Project scope plus its existing active Workspace
    membership and task-relationship fences, removing the wider Adviser and
    `Project.OwnerUserId` shortcuts;
  - SystemAdmin behavior matches current detail authorization rather than
    becoming a global all-status bypass.
- Regression evidence:
  - real-PostgreSQL WPC coverage compares detail, list, Project/Task/Artifact/
    ActivityLog/Comment/Message Search, Messaging list, and My Tasks;
  - it denies an ordinary active Workspace member outside a grouped Active
    Project and Group even when readable Conversation membership exists;
  - it preserves explicit ProjectMember, GroupMember, Workspace Owner/Admin,
    ungrouped ordinary-member, and current SystemAdmin access, and denies a
    revoked Workspace member with stale subordinate memberships;
  - a depth-three revoked-ancestor case proves no item/count/body disclosure,
    and an over-depth case proves bounded fail-closed search authorization;
  - a real-PostgreSQL 125-authorized-Conversation regression proves the newest
    authorized Message is retained even when its Conversation is outside the
    former first 100, tied timestamps use Message ID order, and a recursively
    unauthorized Thread contributes neither title nor body.
- Suggested issue: **Align search authorization with project and comment access rules**.

### BE-003: New conversations use an invalid required workspace foreign key

- Priority: critical.
- Status: confirmed persistence defect for relational databases.
- Affected page: Messaging/DM conversation creation.
- Exact files and methods:
  - `src/Coglatas.Application/Messaging/ConversationService.cs`
    - `CreateAsync`
  - `src/Coglatas.Infrastructure/Persistence/Configurations/MessagingConfigurations.cs`
    - `ConversationConfiguration.Configure`
- Evidence:
  - `CreateAsync` sets `Conversation.WorkspaceId = Guid.Empty`.
  - `ConversationConfiguration` configures `WorkspaceId` as a required foreign key to `Workspace`.
- Impact:
  - Conversation creation can appear valid at the service level but fail during PostgreSQL `SaveChangesAsync`.
  - Current in-memory/fake tests do not exercise this FK constraint.
- Patch suggestion:
  - Require or derive one authorized workspace for the conversation.
  - Verify every selected user is an active member of that workspace.
  - Store the real workspace ID before adding the conversation.
- Suggested tests:
  - PostgreSQL-backed direct and group conversation creation.
  - Rejection when members do not share the selected workspace.
  - Rejection for an empty or inaccessible workspace.
- Suggested issue: **Persist conversations with an authorized workspace**.

### BE-004: Message attachment requests cannot produce valid canonical file records

- Priority: critical.
- Status: confirmed persistence and trust-boundary defect.
- Affected page: Messaging/DM attachments.
- Exact files and methods:
  - `src/Coglatas.Application/Messaging/ConversationService.cs`
    - `SendMessageAsync`
  - `src/Coglatas.Application/Messaging/MessagingDtos.cs`
    - `AttachmentMetadataRequest`
    - `SendMessageRequest`
  - `src/Coglatas.Infrastructure/Persistence/Configurations/SystemConfigurations.cs`
    - `AttachmentConfiguration.Configure`
- Evidence:
  - The request accepts client-provided stored filename, file path, and storage key metadata.
  - The service creates `Attachment` with `WorkspaceId = Guid.Empty` and no `FileObjectId`.
  - `AttachmentConfiguration` requires both workspace and file-object relationships.
  - No canonical uploaded file is resolved or authorized.
- Impact:
  - Attachment-bearing messages can fail during relational persistence.
  - Client-supplied storage metadata is treated as authoritative without proving that bytes exist or belong to the caller.
- Patch suggestion:
  - Accept canonical attachment or file-object IDs produced by `IFileService`.
  - Load and authorize each referenced file against the current user and conversation.
  - Derive workspace, storage, content type, filename, and size exclusively from stored server metadata.
- Suggested tests:
  - Successful message attachment using an authorized canonical file ID.
  - Rejection for nonexistent, deleted, cross-conversation, or cross-tenant file IDs.
  - PostgreSQL FK persistence test.
- Suggested issue: **Replace client-supplied chat attachment metadata with canonical file IDs**.

## 2. High-priority backend bugs

### BE-005: Channel post updates, deletes, and pin changes mutate detached entities

- Priority: high.
- Status: confirmed EF tracking defect.
- Affected page: Channels/posts.
- Exact files and methods:
  - `src/Coglatas.Infrastructure/Persistence/OrganizationRepositories.cs`
    - `ChannelRepository.GetPostByIdAsync`
  - `src/Coglatas.Application/Channels/ChannelService.cs`
    - `UpdatePostAsync`
    - `DeletePostAsync`
    - `SetPinnedAsync`
- Evidence:
  - `GetPostByIdAsync` uses `AsNoTracking`.
  - The service mutates the returned entity and calls `SaveChangesAsync` without attaching or explicitly updating it.
- Impact:
  - APIs can return success and mutated DTOs while the database row remains unchanged.
- Patch suggestion:
  - Return a tracked entity for mutation methods, or add explicit repository mutation methods.
- Suggested issue: **Persist channel post edit, delete, and pin mutations**.

### BE-006: Assignee task filtering performs concurrent operations on one scoped DbContext

- Priority: high.
- Status: confirmed async/EF misuse.
- Affected page: Project task lists filtered by assignee.
- Exact file and method:
  - `src/Coglatas.Application/Projects/ProjectService.cs`
  - `ListTasksAsync`
- Evidence:
  - The service calls `Task.WhenAll` over repository queries.
  - The repository instances share one scoped EF `DbContext`, which does not support parallel operations.
- Impact:
  - Requests can fail with the EF “second operation was started on this context” exception.
- Patch suggestion:
  - Query assignments once and build a task-ID set, or implement a repository query that applies the assignee filter in SQL.
- Suggested issue: **Remove parallel EF queries from assignee task filtering**.

### BE-007: File bytes can become orphaned when metadata persistence fails

- Priority: high.
- Status: confirmed consistency risk.
- Affected pages: Files, Attachments, and Artifact version uploads.
- Exact files and methods:
  - `src/Coglatas.Application/Files/FileService.cs`
    - `UploadAsync`
  - `src/Coglatas.Application/Artifacts/ArtifactService.cs`
    - `UploadVersionAsync`
  - `src/Coglatas.Infrastructure/Files/LocalFileStorageService.cs`
    - `SaveAsync`
- Evidence:
  - File bytes are written before metadata, attachment, audit, and notification changes are committed.
  - There is no compensating delete if a later database operation fails.
  - Local storage writes directly to the final path.
- Impact:
  - Database failures or cancellations can leave unreferenced or partial files that still consume quota/storage.
- Patch suggestion:
  - Write local files to a temporary path and atomically rename after a complete copy.
  - On metadata/audit commit failure, delete the newly stored object.
  - Record cleanup failure without hiding the original exception.
- Suggested issue: **Make file and artifact uploads storage/database-safe**.

### BE-008: Conversation read state accepts message IDs from other conversations

- Priority: high.
- Status: confirmed integrity-validation gap.
- Affected page: Messaging/DM unread state.
- Exact file and method:
  - `src/Coglatas.Application/Messaging/ConversationService.cs`
  - `MarkReadAsync`
- Evidence:
  - `LastReadMessageId` is copied directly from the request after conversation access is checked.
  - The service does not load the message or verify `Message.ConversationId == conversationId`.
- Impact:
  - Read state can reference an unrelated message and produce invalid unread calculations or misleading state.
- Patch suggestion:
  - If a message ID is supplied, load it and verify it is active and belongs to the target conversation.
- Suggested issue: **Validate conversation read-state message ownership**.

### BE-009: Task-comment notifications use the project ID as a task ID

- Priority: high.
- Status: confirmed business-logic defect.
- Affected pages: Notifications and Tasks.
- Exact file and method:
  - `src/Coglatas.Application/Projects/ProjectService.cs`
  - `NotifyCommentAsync`
- Evidence:
  - The notification source type is `TaskItem`.
  - The source ID supplied is the owning project ID instead of `comment.TargetId`.
- Impact:
  - Notification routing generates `/tasks/{projectId}`, which targets the wrong or nonexistent task.
- Patch suggestion:
  - Use the task target ID when the target type is `TaskItem`.
- Suggested issue: **Correct task-comment notification target IDs**.

### BE-010: My Tasks returns duplicate rows for multi-role assignments

- Priority: high.
- Status: confirmed query-shape defect.
- Affected pages: Dashboard and My Tasks.
- Exact file and method:
  - `src/Coglatas.Infrastructure/Persistence/PlanningRepository.cs`
  - `ListMyTasksAsync`
- Evidence:
  - The query starts from `TaskAssignments`.
  - A user may have more than one assignment role for the same task.
  - The query does not group or select distinct task IDs.
- Impact:
  - The same task can appear multiple times, inflate total counts, and distort pagination.
- Patch suggestion:
  - Select distinct task IDs first or group assignments by task before paging.
- Suggested issue: **Return distinct tasks from My Tasks**.

### BE-011: Notification generation can exceed notification column limits

- Priority: high.
- Status: confirmed persistence-validation gap.
- Affected pages: Notifications after event/form operations.
- Exact files and methods:
  - `src/Coglatas.Infrastructure/Persistence/DbNotificationService.cs`
    - `CreateAsync`
  - `src/Coglatas.Application/Events/EventService.cs`
    - `NotifyEventChangeAsync`
  - `src/Coglatas.Application/Forms/FormService.cs`
    - `NotifyFormOpenedAsync`
- Evidence:
  - Event/form titles permit up to 240 characters.
  - Notification titles permit 200 characters.
  - Event/form descriptions permit 4,000 characters, while notification bodies permit 2,000.
- Impact:
  - A valid event/form mutation can fail because its derived notification exceeds the notification schema limits.
- Patch suggestion:
  - Centralize safe notification title/body truncation in `DbNotificationService`.
- Suggested issue: **Enforce notification persistence limits**.

### BE-012: Controllers collapse unrelated failures into HTTP 400

- Priority: high.
- Status: confirmed API-contract defect.
- Affected pages: all browser pages using the REST APIs.
- Exact locations:
  - controller-local `ToActionResult` and `OkOrBad` methods under `src/Coglatas.Web/Controllers/`;
  - `src/Coglatas.Application/Common/Result.cs`;
  - `src/Coglatas.Web/Models/ErrorResponse.cs`.
- Evidence:
  - Authentication, authorization, missing resources, conflicts, disabled features, quota failures, and validation failures are commonly returned as 400.
  - Create operations usually return 200 instead of 201.
  - Delete/archive operations usually return 200 instead of 204.
  - Error shapes vary between `{ error }`, `ErrorResponse`, MVC validation problems, and middleware-specific payloads.
- Patch suggestion:
  - Add typed failure categories or a small shared mapping helper.
  - Use 400 for malformed requests, 401 for unauthenticated requests, 403 for known forbidden operations, 404 for safe not-found responses, 409 for state/uniqueness conflicts, 422 for semantic validation where adopted, 201 for creation, and 204 for successful no-body deletes.
- Suggested issue: **Standardize API status codes and error responses**.

## 3. Medium-priority issues

### BE-013: Nested route parent IDs are ignored for child mutations

- Priority: medium.
- Exact file and methods:
  - `src/Coglatas.Web/Controllers/ProjectsController.cs`
  - `UpdateAssignment`
  - `DeleteAssignment`
  - `DeleteDependency`
- Evidence:
  - Routes include `taskItemId`, but actions pass only the child ID to the service.
- Impact:
  - A valid child ID can be mutated through a misleading parent URL.
- Patch suggestion:
  - Pass both IDs and verify the stored child belongs to the route task.
- Suggested issue: **Validate nested route parent-child relationships**.

### BE-014: PATCH requests cannot explicitly clear nullable values

- Priority: medium.
- Affected modules: projects, tasks, milestones, announcements, events, forms, integrations, workspace/group/channel descriptions.
- Evidence:
  - Nullable request values are generally interpreted as “leave unchanged.”
  - The same representation cannot express “set this field to null.”
- Impact:
  - Dates, descriptions, links, expiration times, form windows, and optional associations may become impossible to clear.
- Patch suggestion:
  - Use explicit field-presence wrappers, JSON Patch, or module-specific clear flags without changing broader architecture.
- Suggested issue: **Define explicit nullable-field clearing for PATCH requests**.

### BE-015: Artifact version numbers are allocated with MAX plus one

- Priority: medium.
- Exact files and methods:
  - `src/Coglatas.Infrastructure/Persistence/ArtifactRepository.cs`
    - `GetNextVersionNumberAsync`
  - `src/Coglatas.Application/Artifacts/ArtifactService.cs`
    - `UploadVersionAsync`
- Impact:
  - Concurrent uploads can select the same version number and collide with the unique index after bytes are already stored.
- Patch suggestion:
  - Serialize version allocation per artifact or catch/retry the unique conflict while cleaning up stored bytes.
- Suggested issue: **Handle concurrent artifact version allocation safely**.

### BE-016: Event capacity enforcement is race-prone

- Priority: medium.
- Exact file and method:
  - `src/Coglatas.Application/Events/EventService.cs`
  - `UpsertAttendanceCoreAsync`
- Evidence:
  - Capacity is checked with a count before the attendance write.
- Impact:
  - Concurrent requests can both pass and overbook the event.
- Patch suggestion:
  - Use a transaction with appropriate locking/isolation or a concurrency-safe update strategy.
- Suggested issue: **Make event capacity enforcement concurrency-safe**.

### BE-017: Group removal retains membership and public-channel access

- Priority: medium.
- Exact file and method:
  - `src/Coglatas.Application/Groups/GroupService.cs`
  - `RemoveMemberAsync`
- Evidence:
  - “Removal” changes the role to `ReadOnly` rather than deleting/deactivating the membership.
  - Public-channel visibility checks only for existence of group membership.
- Impact:
  - Removed users remain visible as group members and retain read access.
- Patch suggestion:
  - Make the operation’s semantics explicit: actually remove/deactivate membership, or rename it to a read-only downgrade.
- Suggested issue: **Align group member removal behavior with its API name**.

### BE-018: Workspace listing does not require active membership

- Priority: medium.
- Exact file and method:
  - `src/Coglatas.Infrastructure/Persistence/OrganizationRepositories.cs`
  - `WorkspaceRepository.ListForUserAsync`
- Evidence:
  - The list predicate checks membership existence but not `MembershipStatus.Active`.
- Impact:
  - Suspended members may continue to see workspace summaries.
- Patch suggestion:
  - Match `WorkspaceAuthorizationService.CanViewWorkspace` by requiring active membership.
- Suggested issue: **Exclude suspended memberships from workspace lists**.

### BE-019: IFileObjectService registration relies on a runtime cast

- Priority: medium.
- Status: DI robustness issue; no missing controller dependency was found.
- Exact file and method:
  - `src/Coglatas.Application/DependencyInjection.cs`
  - `AddApplication`
- Evidence:
  - `IFileObjectService` resolves `IFileService` and casts it at runtime.
- Impact:
  - A future replacement of `IFileService` can break startup at resolution time.
- Patch suggestion:
  - Register `FileService` concretely and map both interfaces to the same scoped instance.
- Suggested issue: **Register file service interfaces through one concrete scoped service**.

## 4. Validation gaps

### Request-model validation

- Request DTOs generally have no MVC validation attributes and there is no centralized request validator.
- Required-field checks exist inconsistently in Application services.
- Database maximum lengths are rarely checked before persistence.
- Empty GUIDs are not consistently rejected.
- Enum validity is checked in forms/events but not consistently in projects, messaging, announcements, channels, artifacts, groups, workspaces, or integrations.
- Query date ranges such as `FromDate > ToDate` are not consistently rejected.
- Nullable collections such as conversation members and form answers need explicit null handling.

### JSON validation

- `IntegrationService.NormalizeJson` can throw `JsonException`.
- Invalid integration settings, webhook event JSON, and API-token scopes can therefore become 500 responses.
- JSON kind is not consistently constrained; values intended as objects or arrays may accept another valid JSON kind.

### Persistence-length validation

Important examples:

- project name 200, description 4,000;
- task title 240, description 8,000;
- announcement title 200, body 20,000;
- message/post/comment body 12,000;
- event/form title 240, description 4,000;
- notification title 200, body 2,000;
- webhook/integration names 160;
- delete reason 500.

Oversized requests should fail before EF/PostgreSQL persistence rather than becoming database exceptions.

### File validation

- The declared MIME type is trusted without inspecting content signatures.
- The reported length is trusted without verifying the actual number of copied bytes.
- File deletion is metadata-only; physical cleanup behavior and retention are not defined.
- A missing physical file raises from storage rather than returning a controlled missing-file result.

## 5. Error-handling problems

### EH-001: Inconsistent response shapes

- Global exceptions use `ErrorResponse(Code, Message, TraceId)`.
- Most controllers return `{ "error": "..." }`.
- MVC model binding can return framework validation problems.
- CSRF middleware returns another `{ error }` payload.

Patch suggestion: use one safe API error contract and include a trace ID consistently.

### EH-002: Global exception middleware treats request cancellation as a server failure

- Exact file and method:
  - `src/Coglatas.Web/Middleware/GlobalExceptionHandlingMiddleware.cs`
  - `InvokeAsync`
- `OperationCanceledException` caused by `RequestAborted` is logged and returned as 500.
- The middleware also writes JSON without first checking whether the response has started.

Patch suggestion: handle request cancellation separately and avoid replacing an already-started response.

### EH-003: Missing physical files become 500 responses

- Exact files and methods:
  - `src/Coglatas.Infrastructure/Files/LocalFileStorageService.cs`
    - `OpenReadAsync`
  - `src/Coglatas.Application/Files/FileService.cs`
    - `DownloadAsync`
  - `src/Coglatas.Application/Artifacts/ArtifactService.cs`
    - `DownloadVersionAsync`

Patch suggestion: translate `FileNotFoundException`/missing object results to a safe not-found response and audit the metadata/storage inconsistency.

### EH-004: Expected database conflicts are not translated

Unique, foreign-key, and length violations currently flow to the global 500 handler. Expected conflicts should be recognized at service/controller boundaries and mapped to safe validation or conflict responses.

### EH-005: Tenant export persists raw exception messages

- Exact file and method:
  - `src/Coglatas.Application/TenantExports/TenantExportService.cs`
  - `ExportAsync`
- The raw exception message is stored in `ExportJob.ErrorMessage` and later returned through the job DTO.

Patch suggestion: log the internal exception with correlation data, but persist and return a safe operational summary.

## 6. Suggested test plan

### Critical regression tests

1. Announcement visibility matrix across workspace, group, public channel, private channel, and confidential channel scopes.
2. Search authorization parity with `CanViewProject` for projects, tasks,
   artifacts, logs, comments, and project-bound messages. **Covered by PR #281
   real-PostgreSQL matrix.**
3. PostgreSQL direct/group conversation creation with real workspace FKs.
4. Canonical file attachment persistence and authorization for messages.

### High-priority regression tests

5. EF-backed post update, delete, pin, and unpin persistence.
6. Assignee-filtered project tasks using a real scoped `DbContext`.
7. Upload cleanup after metadata, audit, notification, cancellation, or concurrency failure.
8. Reject read-state message IDs from another conversation.
9. Verify task-comment notification source IDs.
10. Verify one My Tasks row per task despite multiple assignment roles.
11. Boundary tests for notification title/body lengths.
12. HTTP contract tests for 201, 204, 400, 401, 403, 404, 409, 422, and one error shape.

### Medium-priority regression tests

13. Route task ID does not match assignment/dependency owner.
14. Explicit clearing of nullable PATCH fields.
15. Concurrent artifact version uploads.
16. Concurrent event attendance at final capacity.
17. Removed group members lose expected visibility.
18. Suspended workspace memberships are absent from workspace lists.
19. DI smoke test resolving every controller and Application service with scope validation enabled.

### Validation tests

20. Maximum and maximum-plus-one lengths for every persisted request string.
21. Undefined numeric enum values for each request DTO.
22. Empty GUIDs and null collections.
23. Reversed date/query ranges.
24. Malformed JSON and valid JSON of the wrong root kind.
25. File content/signature mismatch, actual-byte-count mismatch, and missing physical file.

## 7. Exact primary files and methods

| Area | File | Methods/types |
| --- | --- | --- |
| Announcements | `Application/Announcements/AnnouncementService.cs` | `ResolveCreateScopeAsync`, `CreateAsync`, `UpdateAsync` |
| Announcement visibility | `Infrastructure/Persistence/AnnouncementReadScope.cs`, `Infrastructure/Persistence/AnnouncementRepository.cs` | `VisibleAnnouncementsFor`, `ListVisibleAsync`, `IsVisibleToUserAsync` |
| Search | `Infrastructure/Persistence/ProjectReadScope.cs`, `Infrastructure/Persistence/DbSearchService.cs` | `VisibleProjectsFor`, `SearchAnnouncementsAsync`, `SearchCommentsAsync`, project-derived searches |
| Messaging | `Application/Messaging/ConversationService.cs` | `CreateAsync`, `SendMessageAsync`, `MarkReadAsync` |
| Message contracts | `Application/Messaging/MessagingDtos.cs` | `CreateConversationRequest`, `AttachmentMetadataRequest`, `SendMessageRequest` |
| Post persistence | `Infrastructure/Persistence/OrganizationRepositories.cs` | `ChannelRepository.GetPostByIdAsync` |
| Channel logic | `Application/Channels/ChannelService.cs` | `UpdatePostAsync`, `DeletePostAsync`, `SetPinnedAsync` |
| Projects/tasks | `Application/Projects/ProjectService.cs` | `ListTasksAsync`, `NotifyCommentAsync` |
| Project authorization | `Application/Projects/ProjectAuthorizationService.cs` | `CanViewProject` |
| Planning | `Infrastructure/Persistence/PlanningRepository.cs` | `ListMyTasksAsync` |
| Files | `Application/Files/FileService.cs` | `UploadAsync`, `DownloadAsync`, `DeleteAttachmentAsync` |
| Artifacts | `Application/Artifacts/ArtifactService.cs` | `UploadVersionAsync`, `DownloadVersionAsync`, `DeleteVersionAsync` |
| Storage | `Infrastructure/Files/LocalFileStorageService.cs` | `SaveAsync`, `OpenReadAsync`, `DeleteAsync` |
| Artifact versions | `Infrastructure/Persistence/ArtifactRepository.cs` | `GetNextVersionNumberAsync` |
| Events | `Application/Events/EventService.cs` | `UpsertAttendanceCoreAsync`, `NotifyEventChangeAsync` |
| Forms | `Application/Forms/FormService.cs` | `SubmitResponseAsync`, `NotifyFormOpenedAsync`, validation helpers |
| Notifications | `Infrastructure/Persistence/DbNotificationService.cs` | `CreateAsync`, `CreateManyAsync` |
| Groups | `Application/Groups/GroupService.cs` | `RemoveMemberAsync` |
| Workspace lists | `Infrastructure/Persistence/OrganizationRepositories.cs` | `WorkspaceRepository.ListForUserAsync` |
| DI | `Application/DependencyInjection.cs` | `AddApplication` |
| Errors | `Web/Middleware/GlobalExceptionHandlingMiddleware.cs` | `InvokeAsync` |
| HTTP mapping | `Web/Controllers/*.cs` | controller-local `ToActionResult` and `OkOrBad` helpers |
| Export errors | `Application/TenantExports/TenantExportService.cs` | `ExportAsync` |

## 8. Suggested issue list

1. **Prevent workspace-wide disclosure of scoped announcements** — resolved by the current WS-01-BE candidate.
2. **Align search authorization with project and comment access rules** — resolved by PR #281 for Project-derived results under the current Project read policy.
3. **Persist conversations with an authorized workspace** — critical.
4. **Replace client-supplied chat attachment metadata with canonical file IDs** — critical.
5. **Persist channel post edit, delete, and pin mutations** — high.
6. **Remove parallel EF queries from assignee task filtering** — high.
7. **Make file and artifact uploads storage/database-safe** — high.
8. **Validate conversation read-state message ownership** — high.
9. **Correct task-comment notification target IDs** — high.
10. **Return distinct tasks from My Tasks** — high.
11. **Enforce notification persistence limits** — high.
12. **Standardize API status codes and error responses** — high.
13. **Validate nested route parent-child relationships** — medium.
14. **Define explicit nullable-field clearing for PATCH requests** — medium.
15. **Handle concurrent artifact version allocation safely** — medium.
16. **Make event capacity enforcement concurrency-safe** — medium.
17. **Align group member removal behavior with its API name** — medium.
18. **Exclude suspended memberships from workspace lists** — medium.
19. **Register file service interfaces through one concrete scoped service** — medium.
20. **Validate DTO lengths, enums, GUIDs, JSON, collections, and date ranges** — medium.
21. **Translate missing files and expected database conflicts into safe API errors** — medium.
22. **Prevent tenant export jobs from exposing raw exception messages** — medium.

## Implementation constraints for follow-up patches

- Do not weaken authentication or resource authorization.
- Do not change the database schema solely to work around service defects.
- Keep controllers thin.
- Prefer focused repository/service patches over architectural refactors.
- Keep search authorization identical to the canonical resource authorization policy.
- Treat client IDs, parent IDs, file metadata, and storage paths as untrusted.
- Add denied-case and PostgreSQL-backed tests for authorization and persistence fixes.
