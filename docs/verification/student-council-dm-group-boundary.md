# Student Council DM and Group DM implementation boundary

Baseline: Product Main `6f940987f8df746736b6df5ea7371ab698028f5e`.

## Current backend contract

`ConversationType.Group` exists as enum value `7`, but
`ConversationService.IsSupportedMvpType` permits only `DirectMessage`,
`ProjectChannel`, and legacy `Thread`. Both generic create and message send
enforce that boundary. The Group entity and membership routes do not establish
an enabled Group DM workflow.

The existing one-to-one workflow uses `GET /api/conversations/recipients` and
`POST /api/conversations/direct`. Search returns at most 20 authorized direct
recipients. Direct creation finds a shared active Workspace and checks the
recipient's active account before delegating to the existing create command.

Source inspection identified separate backend admission concerns: generic
`CreateAsync` checks requested users for existence and optional Project access,
and `AddMemberAsync` checks existence after manager authorization. Neither path
explicitly checks the requested user's current active Workspace membership in
that service. The backend workstream must verify and repair those current
contracts without treating frontend visibility as authorization. No backend
authorization or Group activation change is included here.

## Implemented frontend behavior

- Exact backend `Group` / `7` values belong to the DM directory and `/dm/:id`
  route, including saved Message links. They never become a Teams channel.
- Group rows retain the same hidden inbox preview as one-to-one DM and show a
  Group DM label. An untitled group does not use its first participant as its
  title.
- Existing authorized Group detail reads remain read-only. The frontend does
  not offer sending or first-thread creation for a type whose backend rejects
  those commands. This is a presentation restriction, not a server control.
- The recipient dialog keeps multiple named selections across server searches,
  allows removal, clears on close and protected reload, and prevents selection
  changes and duplicate submit while creating. One recipient uses the existing
  direct endpoint. More than one recipient shows an explicit unavailable
  message and cannot issue any create command.
- A compact desktop navigator separates personal and group conversations from
  the authorized current inbox page. The page scope is explicit; this does not
  claim a complete cross-page directory. Narrow screens use the DM list route.

Group create, participant add/remove/leave, and Group send remain **NO_GO** for
real pilot accounts until an approved runtime contract and real PostgreSQL/HTTP
negative matrix exist. No new API, migration, dependency, role, RLS, or GRANT is
introduced. Existing security holds are unchanged.

## Penpot traceability

Read-only source: `CoglatasUI`, Approved Dark DM / Group Chat board
`9b18ec83-d336-8057-8008-c5ad393394ef`, retrieved
`2026-10-19T10:33:25.287Z`. The common shell workstream records the file revision.

| Source element | Source geometry | Angular mapping |
| --- | --- | --- |
| Personal and group navigator `9a59a7a6-1369-80f7-8008-cf0e393c78d8` | 266 × 944, x220/y70 | DM desktop navigator with direct/group sections |
| Conversation `9a59a7a6-1369-80f7-8008-cf0e482b7ca3` | 702 × 944, x502/y70 | Existing timeline/composer with responsive width |
| Global navigation | 208px | Shared AppShell workstream |
| Global header | 54px | Shared AppShell workstream |

The board uses Noto Sans JP and approved dark semantic surfaces. This work
reuses the existing common theme variables. Voice/video controls, shared Task
and File inspector placeholders are not added as unimplemented product actions.
The Group create dialog is a contract-limited adaptation; that board is not
evidence of an approved Group backend contract or a dedicated picker design.

Visual parity is not established by source extraction. The five-width Linux
Playwright screenshot comparison remains a separate release gate.

## Verification

Focused Angular tests cover Group enum routing, read-only capabilities,
removed-participant denial, DM/Teams directory separation, hidden Group inbox
previews, saved-message routing, multi-recipient selection, unavailable Group
submission, and duplicate direct-create prevention. These use mocked HTTP and
do not prove PostgreSQL persistence, server authorization, or pilot acceptance.
