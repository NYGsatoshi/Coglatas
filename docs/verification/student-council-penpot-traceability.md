# Student Council pilot: Penpot traceability

## Read-only source

`CoglatasUI` file `52a5041f-08a1-80f1-8008-c5a09f8b5f61`, revision 441,
was read through the connected Penpot environment. Initial retrieval was
2026-10-19T10:33:27Z; all ten priority identities/dimensions were rechecked
at 16:59:14.565Z. W10 was inspected again at 20:10:36.809Z, Channel and Thread
were revisited during implementation, and Thread/file metadata still reported
revision 441 at 21:43:50.927Z. All ten priority boards were found again in a
read-only metadata check at 22:04:17.807Z, still revision 441.
These are design-source checkpoints, not new PNG
exports unless the individual receipt explicitly records an export.

Approved Dark current boards take precedence over Legacy comparison boards
and older revision 339 local references. Penpot was not edited. The source
reference and runtime qualification are separate: faithful styling does not
grant an API capability or complete target acceptance.

## Source-to-component mapping

| Board | Board ID | Angular implementation and contract limit |
| --- | --- | --- |
| Approved Dark Home G04 | `1f711f64-03d8-8014-8008-c5d93fda2ae2` | Workspace landing/shared shell. Dedicated Home summary/activity composition remains partial. No invented counts. |
| Approved Dark Workspace Hub W09 | `9a59a7a6-1369-80f7-8008-cfe6e4adff2f` | Existing Workspace dashboard/list/cards, native authorized selection, search and scoped switching. Unsupported activity/Group panels remain absent. |
| Approved Dark Team Channel Chat | `9b18ec83-d336-8057-8008-c5a1fe4aaa3d` | Conversation-backed Channel page, dense timeline/composer, loading/error/retry and current server participant details. A 300px desktop inspector becomes a native Members disclosure on narrower screens and is replaced by the active Thread. |
| Approved Dark DM / Group Chat | `9b18ec83-d336-8057-8008-c5ad393394ef` | Separate DM directory, direct messaging, Group identity/routing and multi-recipient selection. Unsupported Group submission is explicitly unavailable. |
| Dedicated Thread Workspace | `9b18ec83-d336-8057-8008-c5bb050c35c1` | Existing ThreadPreview/ThreadWorkspace, compact reply composer, session tabs, loaded root excerpt/author, server reply count/update time and current/open-state navigation. One active authorized body; simultaneous multi-card/global-directory/topic/status parity is not implemented. |
| Approved Dark File Hub FHV05 | `9a59a7a6-1369-80f7-8008-cf126eeff377` | Files page, bounded list/search/folders, inspector and explicit loading/empty/error/denied states using existing APIs. |
| Approved Dark File Preview FHV09 | `9a59a7a6-1369-80f7-8008-cf261fd20680` | Existing grant-backed image viewport, Fit/100%/zoom/rotation and responsive expanded surface. Download reauthorizes; protected state clears on denial/session/scope replacement. |
| Approved Dark Global Navigation | `9a59a7a6-1369-80f7-8008-cf0952f2ee2f` | Shared feature menu/AppShell, one desktop rail and clear selected marker. Workspace, Teams, DM and Files are distinct; Group DM belongs to DM. |
| Approved Dark Global Header | `9a59a7a6-1369-80f7-8008-cf09763030da` | Compact shared header. Mobile Workspace remains visible; search/account utilities use an accessible disclosure. Existing Ctrl/Cmd+K owner and theme persistence remain. |
| Approved Dark Workspace Members W10 | `e1d76e7e-ebca-80a9-8008-d017f5414675` | Existing members directory/search plus 376px read-only detail surface on large desktops, stacked below at narrower widths. Shows only the ordinary GET's display name and canonical role; directory status remains separate. No email, edit action or illustrative capability is fabricated. |

## Shared tokens and dimensions

The reference desktop boards are 1536×1024. Global navigation is 208px and
the shared header 54px; content inset 24px, panel gap 16px, Workspace panel
radius 7px, navigation row 40px and touch controls at least 44px. W10's detail
surface is 376px; Channel participants use the measured 300px reference.
Thread's specialized composition has different reference dimensions and is
adapted to the existing authorized pane model rather than forced onto Mobile.

Reference typography uses Noto Sans JP: body 13px, metadata 11px and bold
Workspace/Home titles 28px. Existing system fallbacks remain; no remote font
dependency was introduced. Exact font parity is unqualified.

| Semantic role | Revision 441 value |
| --- | --- |
| Canvas / navigation / header | `#0B141D` / `#0D1520` / `#101B27` |
| Panel / input or secondary action / raised row | `#131F2B` / `#102030` / `#192939` |
| Border | `#243547` |
| Heading / body | `#F2F5FB` / `#D6E1F0` |
| Secondary / muted metadata | `#B2C3D9` / `#879BB4` |
| Selected background | `#12305A` |
| Accent or focus / accent marker | `#75BAFF` / `#0877FF` |
| Workspace primary action | `#0669DF` |

These values integrate with the existing semantic theme and Syncfusion theme
switching. Approved Dark is the default; an explicit saved light preference
continues to work. No new vendor version/license contract or arbitrary palette
was introduced. Native selected/expanded/disabled states, visible focus and
loading/empty/error/denied states remain part of the implementation contract.

## Verification and remaining differences

Use original pinned-Linux captures at 1536/1366/768/390/320 CSS pixels,
plus explicit 960px and 320×420 keyboard-height diagnostics. Check layout,
spacing/density, typography/color, surface hierarchy, navigation, dialogs,
overflow, visible state, keyboard focus, actual hit targets and IME handling.
Mobile uses a responsive pane/stack/drawer rather than desktop fixed widths.

The integration source `89fe1c54` combines reviewed member `585fe507`, participant `600191bb` and
Thread/header `eebaa8df` changes with density `66fb0d8e`. Its 27 added/changed blobs exactly
match their reviewed worker versions. Worker results remain source-specific;
the combined build/units/full static/captures must have their own receipt.
Hosted real-backend run `39328164824` passed the existing eight required cases at
exact `89fe1c54`, including ordinary member-detail HTTP/UI checks; this is backend
integration evidence rather than pixel parity or target HTTPS acceptance.

Full design parity remains **PARTIAL**. Reference Home/activity/summary and
capability panels, unsupported Group lifecycle, simultaneous multi-card Thread,
global Thread directory and server/cross-device tab persistence are absent.
Only currently loaded authorized metadata is shown. Exact Noto font matching,
long-roster exhaustion, physical Japanese IME and full WCAG conformance are not
established by screenshots, axe or synthetic key events alone.

Prior failed captures/tests remain in their source-bound records. One Linux
390×844 drawer baseline was reviewed against the original expected/actual/diff:
the 280px rail was identical and only the exposed content gutter reflected the
intended compact header. Tolerance, timeouts and assertions were not relaxed.
Unchanged-source diagnostics do not establish causes or turn a failed full run
into a pass. See [the release gate](student-council-release-gate.md) for exact
qualification, retained failures and deployment limitations.
