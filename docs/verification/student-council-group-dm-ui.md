# Student Council Group DM frontend candidate

Status: frontend implementation candidate. This document does not qualify the
backend, activate Group support on Main, or authorize pilot accounts. Integrate
only with the separately qualified Workspace-only Group backend and independent
review. Mock browser tests are auxiliary evidence, not PostgreSQL authorization
or real-backend acceptance.

## Implemented surface

- The existing new-message dialog has explicit Direct and Group modes. Direct
  keeps its dedicated command and recipient search. Group requires a committed
  selected Workspace and at least two other selected members; the server checks
  the Active Workspace lifecycle on submission. Its optional title
  uses the existing 200-character boundary. It submits numeric Group type 7 to
  the existing generic Conversation command and accepts the corresponding Group
  detail only in the requested Workspace, routing it to DM.
- Group creation and member addition reuse one picker over the ordinary
  `GET /api/workspaces/{id}/members` response. Only explicit Active memberships
  are candidates. This is not an account or Tenant admission guarantee: each
  mutation is reauthorized by the server. Missing scope or denied member reads
  fail closed, without a broader recipient-directory fallback.
- Group timelines use current readable participant flags, current posting flags,
  the existing Member/Admin posting roles and unlocked/unarchived Conversation
  state. A server-readable historical participant list shows active, left and
  removed participants. The existing response DTO is unchanged; this UI displays
  names and roles rather than introducing a new email or account directory.
- Membership controls preview the returned current participant's Admin or
  CanManageMembers authority, including a ReadOnly participant with that explicit
  management flag. They call the existing add/remove/leave commands. Removing
  an Admin and sole-manager withdrawal are not blocked by an invented hierarchy
  or last-manager rule; the confirmation explains the resulting operational risk.
- Commands remain pending until an actual response. Add/remove results and
  failures trigger a fresh authorized aggregate; no participant mutation is
  shown optimistically. Scope, identity and authorization boundaries cancel
  requests and clear participant and message projections. Withdrawal clears
  the conversation and returns to the DM directory. Focus returns to the current
  member panel after successful revalidation.
- Group creation has no reuse/idempotency contract. An unknown command outcome
  is described as unconfirmed and directs the user to refresh DMs before
  retrying. No automatic create or membership replay is introduced. Existing
  message idempotency, failure recovery and Japanese composition handling remain
  in the shared composer.
- Group Message threads, legacy Conversation threads, metadata changes and
  moderation remain unavailable. The mapper, timeline and facade each retain
  the Group thread boundary; a returned thread flag cannot enable that operation.
  Group identity is based on the server type, not participant count.

## Design source and adaptation

The read-only Penpot source is CoglatasUI, File
`52a5041f-08a1-80f1-8008-c5a09f8b5f61`, Approved Dark DM / Group board
`9b18ec83-d336-8057-8008-c5ad393394ef`. The direct board read
at `2026-10-20T01:36:22.478Z` returned its 1536 by 1024 geometry: navigation
208px, header 54px, DM navigator 266px, conversation 702px and details 300px.
The pilot's last separately recorded File revision is 441; this geometry read
did not itself return a revision number or page identity. The Coordinator's
separate 2026-10-19T23:58Z ten-board metadata observation placed this board on
`Coglatas · Chat UI`; that observation is not a new board export or revision
claim. Exact source names, including Japanese
design labels, remain attributed design evidence.

The candidate reuses existing Approved Dark tokens, personal/Group navigation
and compact composer. The supported participant fields occupy the details pane;
on narrow screens it becomes a scrollable, collapsible disclosure. The shared
Workspace picker and creation dialog use existing surface, border, focus and
spacing values and standard Lucide controls. Creation keeps its actions visible
while its content scrolls. This is partial parity: unsupported calls, presence,
shared-work/contact metadata and design-only shared-file/pinned content are not
fabricated. Frozen `09e58d4a` in draft PR #1293 has source-bound original
captures at 1536, 1366, 768, 390 and 320 CSS pixels plus pending-dialog
320-by-420 controls. These are candidate mock-frontend evidence; the later
combined application's rendering still requires its own source/build binding.

## Qualification requirements

Owning frontend regressions cover the two enum representations, current role and
flags, held threads, malformed legacy shapes, removed/left readers, exact
Workspace selection and stale request cancellation, duplicate submit blocking,
safe command failures, ordinary participant leave and ReadOnly management.
Mock browser regressions cover creation, send/reload, membership lifecycle,
revocation, focus, composition and responsive overflow. Their five-width frames
must be bound to the actual production build and source used for execution.

The final integration source includes the separately reviewed shared dialog
composition and narrow-screen footer fix, explicit read action `5933f8f9` and
real unread helper `276e8194`. The 09e packet is sealed: 375 files, a fresh
81-file build, 24 distinct mocked browser executions with zero skips/retries,
and 34 original frames. Its earlier c8 units/architecture/license checks remain
attributed to c8. Do not attribute a standalone Group-branch check to the later
combined source. Real migrated
PostgreSQL/HTTP tests, Group delivery/revocation tests, real-backend browser
integration, current CI and independent review remain separate gates. Physical
IME, complete accessibility, public HTTPS, target operations, school role/privacy
acceptance and formal opening are not established by frontend mocks. Public
pilot verdict remains NO_GO until all applicable gates are satisfied.
