# Student council pilot: Penpot traceability

## Source receipt

- Read-only Penpot MCP retrieval: **2026-10-19T10:33:27Z**.
- Connected file: `CoglatasUI` (`52a5041f-08a1-80f1-8008-c5a09f8b5f61`).
- Live file revision: **441** (`penpot.currentFile.revn`).
- Product implementation baseline: `6f940987f8df746736b6df5ea7371ab698028f5e`.
- Existing Angular tokens referenced revision 339. Revision 441 Approved Dark
  boards take precedence over the older local library color collection.
- No Penpot mutation was performed. Legacy comparison boards are not authority.
- Read-only recheck at **2026-10-19T16:59:14.565Z** found the same ten priority
  board identities, names and dimensions at revision 441.
- Implementation checkpoint after the **2026-10-19T17:55:34Z** normal merge is
  Product Main `a7a82cce`; later candidates and actual Main execution are
  identified separately from prior PR qualification.
- The Channel and Dedicated Thread boards were rechecked read-only at
  **2026-10-19T19:05:07.679Z**, still revision 441. The fresh Channel export
  matched the earlier revision-441 reference bytes. Five-width comparisons of
  the final integrated successor remain separate from earlier worker captures.

## Approved source and implementation mapping

| Approved board | Board ID | Angular target | Status |
| --- | --- | --- | --- |
| Dark Home G04 | `1f711f64-03d8-8014-8008-c5d93fda2ae2` | Workspace landing and shared shell | Shared palette and shell implemented; full G04 remains outside this slice |
| Dark Workspace Hub W09 | `9a59a7a6-1369-80f7-8008-cfe6e4adff2f` | Workspace dashboard, summary list and cards | Dense authorized list/selection implemented and compared at all five widths; unsupported summary/activity panels remain absent |
| Dark Global Navigation | `9a59a7a6-1369-80f7-8008-cf0952f2ee2f` | Feature menu, AppShell | Single 208-pixel navigation and selected marker implemented |
| Dark Global Header | `9a59a7a6-1369-80f7-8008-cf09763030da` | Top bar | Compact desktop header implemented; contextual utilities retained |
| Workspace Members W10 | `e1d76e7e-ebca-80a9-8008-d017f5414675` | Workspace members page | Dark-safe authorized list and search styles implemented; inspector remains unavailable |
| Team Channel Chat | `9b18ec83-d336-8057-8008-c5a1fe4aaa3d` | Channel messaging | Dense Conversation-backed timeline, composer and selected navigation implemented; IME/retry/denial controls retained |
| DM / Group Chat | `9b18ec83-d336-8057-8008-c5ad393394ef` | DM and Group DM | DM directory, distinct Group identity/routing and multi-recipient selection implemented; unsupported Group submission remains disabled |
| Dedicated Thread Workspace | `9b18ec83-d336-8057-8008-c5bb050c35c1` | Message thread panel | Existing Message Thread API, compact panel and session-local tabs retained; simultaneous multi-pane/reference parity and cross-device tab persistence are unqualified |
| File Hub FHV05 | `9a59a7a6-1369-80f7-8008-cf126eeff377` | Files hub | Dense list/search/folders, inspector, explicit loading/empty/denied states and responsive widths implemented with existing Files contracts |
| File Preview FHV09 | `9a59a7a6-1369-80f7-8008-cf261fd20680` | File preview | Grant-backed image Fit/100%/zoom/rotation merged in #1282; larger responsive surface merged in #1284 after source/rendered review and fresh current-base CI |

## Measured shared contract

The desktop boards are 1536 by 1024. The navigation is 208 pixels wide and
the header is 54 pixels tall. Content starts 24 pixels from the navigation;
panel gaps are 16 pixels. Workspace panels have a 7-pixel radius. Navigation
rows are 40 pixels tall; touch controls retain the existing 44-pixel minimum.
Typography is Noto Sans JP, with 13-pixel regular body text, 11-pixel metadata,
and 28-pixel bold Workspace/Home titles. Existing system fallbacks remain;
this change adds no remote font dependency.

| Semantic role | Approved Dark board value |
| --- | --- |
| Canvas | `#0B141D` |
| Navigation | `#0D1520` |
| Header | `#101B27` |
| Panel | `#131F2B` |
| Input / secondary action | `#102030` |
| Raised row | `#192939` |
| Border | `#243547` |
| Heading | `#F2F5FB` |
| Body | `#D6E1F0` |
| Secondary text | `#B2C3D9` |
| Muted metadata | `#879BB4` |
| Selected background | `#12305A` |
| Accent / focus | `#75BAFF` |
| Accent marker | `#0877FF` |
| Workspace primary action | `#0669DF` |

## Capability and parity limits

Source panels showing Project/Task creation, pending email invites, Group
exploration and activity do not establish pilot API availability. Existing
authorized DTOs remain the only source of counts, roles and actions. The
Workspace landing remains the Home entry point; no duplicate dashboard or
fabricated count is introduced. Email invitation, unsupported Workspace member
mutations and cross-device thread-tab synchronization must not be claimed.

## Candidate implementation

- Existing semantic variables now use the current board palette, shared by the
  conversation and Files surfaces. Existing Syncfusion application theme
  switching remains in place; no vendor version or license contract changed.
- First navigation destinations are Workspace, Teams, DM and Files. The
  duplicate account rail is removed from AppShell; Account/Logout remain in
  the header. Existing capability-filtered secondary and pinned routes remain.
- The default is Approved Dark even with a light OS preference. An explicit
  saved light preference and the existing theme toggle still work.
- A bounded viewport and internally scrolling main pane prevent a long
  Workspace list from stretching the whole shell. Mobile retains its focus
  trap, drawer, 44-pixel touch controls and responsive single-column layout.
- Workspace names are native selection buttons only when the server projects
  `openWorkspace`. Selection delegates to the existing WS-02 authority map and
  clear-before-activate boundary. A pending switch disables repeated actions;
  the selected row exposes textual/button accessibility state.
- Member headings and search controls no longer hardcode light-only colors.
  No member mutation, email invitation, capability grant or new API is added.

## Verification receipt and remaining differences

- `npm ci --ignore-scripts`: completed against the unchanged lockfile; npm
  reported seven high advisories in the existing dependency graph. No automatic
  dependency upgrade or audit suppression was applied.
- `npm run build`: **passed**, exit 0, initial bundle 929.17 kB; existing bundle
  and component-style warnings remain. Syncfusion sanitizer scanned six styles
  and changed one generated output. Hosted artifacts were not committed.
- `node scripts/check-architecture.mjs`: **passed**.
- `git diff --check`: **passed**.
- Focused theme/layout/Workspace/WS-02 unit-test bundle compiled, but all twenty
  worker startups timed out before test collection: **zero assertions ran**.
  The single-worker retry was stopped to release the shared host for integrated
  qualification; it is not a pass. No check or timeout was weakened.
- The new `student-council-shell.spec.ts` is included in the existing Angular
  static runner and covers five requested widths, native Workspace selection,
  distinct navigation, dark preference, member search, overflow and axe checks.
  A premature local attempt ran before production output existed and failed all
  five cases on missing application markup; it provides no UI evidence.
- That early attempt did not provide screenshot acceptance. Subsequent pinned
  Linux Docker runs cover **1536, 1366, 768, 390 and 320 pixels**, plus 960px
  shell/Channel checks; the combined evidence below supersedes it. Mocked tests
  cannot establish PostgreSQL or public HTTPS acceptance.

## Coordinator visual corrections and independent review

The initial Linux shell run passed all five widths but comparison with the
actual W09 export found an oversized desktop header and Workspace rows, an
unnecessary collapsed right rail, and a missing brand mark. Those are corrected
by A amendment `25bba9c9`: the paper-plane SVG is generated directly from source
shape `9a59a7a6-1369-80f7-8008-cf09530b7e5d` at revision 441; the desktop header
uses the 54-pixel measurement; empty attention state and rows are compact; and
the header controls reopen the hidden collapsed utility panel. Explicit saved
theme preferences and capability checks remain intact.

Independent source review identified three issues in the first correction:
Research status became visually hidden, expanded utility panels narrowed the
card below its fixed grid minimum, and desktop close lost keyboard focus.
The amendment preserves visible server-derived status, uses the available
`shell-body` container width for reflow, and returns focus to the header opener.
The browser regression now checks component overflow while the panel is open
and focus after closing it. Agent E approved the amended source; final combined
browser evidence belongs in the release gate.

Two intermediate corrected shell runs passed five widths each. An additional
notification test initially targeted the zero-height host of a fixed mobile
drawer and failed three cases; it now checks the actual panel and closes via
its modal control. This assertion correction does not hide a failed UI action.

The integration at `9728f32d` passed 552 relevant frontend tests across 51 files
in the pinned Linux image, including theme, layout, Workspace, messaging, Files
and notification panels. This supersedes the unexecuted Windows assertion
attempt above. It does not establish actual backend behavior.

This remains a contract-limited design adaptation. Four summary cards, Group
explorer, activity panels, dedicated G04 Home and W10 Capability Inspector are
not reproduced. The current server-backed Workspace summary stays visible
without invented counts, roles, grants or pending email invites. Research status
is retained from the active WS-02 contract, and a narrow expanded utility panel
can make the header wrap. Full Penpot parity and public release are not claimed.

## Final integrated visual amendment

The `f491f779` integration passed **569/569** frontend tests, production build
and **22/22** width diagnostics. Independent inspection of all 54 PNGs still
found a floating theme control covering part of Send on desktop conversations.
This demonstrates why assertion passes alone do not establish visual parity.
The theme control now belongs to the shell header; standalone routes retain
their existing floating control. The theme remains initialized at the root.
The existing theme-persistence browser test now also checks standalone/shell
transitions and exactly one control. Conversation diagnostics check the actual
Send hit target at three points, without sending the IME draft.

The amended source passed **12/12** focused App, header and Login tests and a
fresh production build. A concurrent diagnostic test accidentally reused the
browser artifact directory: **21 passed / 2 trace-cleanup ENOENT failures** are
preserved. The isolated final regeneration passed **23/23**, covering all 22
width diagnostics and the extended theme-persistence test. No assertion or trace
policy was relaxed. The integration PR receipt records the independent comparison
against revision-441 sources and its remaining contract-limited differences.

## Workspace readability and baseline acceptance

The next CSS-only amendment uses available metric width and natural word
boundaries. Independent QA inspected all six changed Workspace frames, both
1280px desktop / 390px drawer Linux baselines, and every other changed capture.
Thirty-six of 54 primary PNGs were byte-identical; twelve additional pairs
differed only in tiny raster regions, with no observed layout or content change.
The metric word-splitting finding is closed. Metrics can occupy an additional
row on narrow cards to remain readable.

The targeted suite passed **23/23** in each Chromium desktop and mobile profile.
The two existing baseline cases first failed against the old light/rail design,
then their deliberately regenerated Linux images passed **2/2** without update
mode. Independent baseline approval retains desktop `0.055` and mobile `0.002`
thresholds. Other platform baselines, test exclusions and retry policy did not
change. Full design parity remains unqualified.

Member role rendering now preserves existing Owner/Admin/Adviser/Member/ReadOnly
values instead of coarse Teacher/Viewer labels. Unknown values are unavailable,
and both supported grids use localized labels. This is presentation only: no
role, permission, capability or mutation contract is introduced. The current
Workspace membership audit records the missing management/self-leave UI and
server projection instead of inventing a W10 capability inspector.

## Image preview follow-ups

The [image viewport controls](pilot-file-image-viewport.md) merged normally in
[#1282](https://github.com/NYGsatoshi/Coglatas/pull/1282). Independent review
compared the actual FHV09 reference and thirty desktop/five mobile frames;
all one hundred capture records were verified. Local 34-unit/22-browser checks
and separate exact-head hosted qualification passed. Actual Main `12d84a56`
then independently passed run `39287466687`, attempt 1. This is bounded renderer
acceptance, not full standalone FHV09 parity or server-intake qualification.

The larger modal adaptation is [#1284](https://github.com/NYGsatoshi/Coglatas/pull/1284).
Local production build, architecture, 36 focused units and 34 browser cases
passed with zero retries/skips. Independent source/rendered review opened thirty
final captures plus the reference and verified all 140 capture hashes. Earlier
style-budget and reverse-focus failures remain retained; their fixes changed
neither budgets nor assertions. Native vertical pane scrolling at 320px can move
the title above the viewport. Only 1024px viewport heights were captured.

Initial head `388ee3f3` passed 1,491 units/142 files, 59 architecture contracts,
275 browser cases and thirteen unchanged historical skips in run `39290785374`.
Head `13da2a97` normally integrated Main `699d2350`; all ten effective
change paths and Git blobs are unchanged, including the frontend tree
`3aa9ed06c7849438cf20d536b6ae16bc673739d7`. Fresh current-base run `39294792361`,
attempt 1, independently passed 1,491 units/142 files, 59 architecture checks,
275 browser cases and all six required contexts. The thirteen semantic skips
match fresh Main699d and earn no acceptance credit. All sixteen jobs passed;
backend/security-runtime/license-guard executions were source-routed out.
#1284 merged normally as `a7a82cce`; actual Main qualification remains separate.
Transfer queues, version summaries, Safe open/scanner guarantees, physical
devices, exact Noto Sans JP glyph parity and complete accessibility conformance
remain unqualified.
