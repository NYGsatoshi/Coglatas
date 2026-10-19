# Student Council mobile header follow-up

Date: 2026-10-19. Base: `1eee0ceb2c0c8b8a3056d981021577cb3b0fa95b`.
Branch: `codex/pilot-a-mobile-members-polish-20261019`.

## Scope and source

The follow-up retains the revision-441 CoglatasUI tokens and existing controls
recorded in `student-council-penpot-traceability.md`. The relevant approved sources
remain Global Header `9a59a7a6-1369-80f7-8008-cf09763030da`, Global Navigation
`9a59a7a6-1369-80f7-8008-cf0952f2ee2f`, and Workspace Members W10
`e1d76e7e-ebca-80a9-8008-d017f5414675`. No new design retrieval or Penpot edit
was needed for this narrow layout correction.

Only the TopBar stylesheet changes. Below 861px, controls in the existing labelled
navigation groups share available wrapping lines. Between 521px and 860px, search
uses the space beside the Workspace selector. Research state remains on its own
row. Controls, labels, DOM order, authority, and data contracts are unchanged.

## Executed verification

All browser evidence below uses the pinned
`mcr.microsoft.com/playwright:v1.64.0-noble` image, synthetic static API data, and
an independent `artifacts/mobile-members-polish/` output directory. It does not
establish PostgreSQL, real-backend, or public HTTPS acceptance.

- Focused TopBar and Workspace Members unit suites: **19/19 passed**, two files.
- Production build: **passed**, initial bundle **930.00kB**, estimated transfer
  **193.51kB**. Existing size warnings remain; budgets were not changed.
- Unchanged `student-council-shell.spec.ts`: **12/12 passed**, both Chromium
  desktop and mobile profiles at all six widths below. Workspace selection,
  navigation, notifications toggle/focus return, Members filtering, closed-page
  accessibility, and document overflow checks passed.
- Additional native-browser inspection: **nine contexts completed** (English at
  all six widths, Japanese additionally at 320/390/768px). Tab reaches Workspace,
  search, Members, theme, notifications, account, and logout; mobile navigation
  is also reachable. Both labelled action-navigation landmarks remain exposed.
  Every closed-page axe scan reports zero violations and no document overflow.
- The first ignored inspection run used an immediate assertion after asynchronous
  panel closing. The harness was corrected to use Playwright's focus auto-wait;
  the subsequent complete run passed. Product focus handling was not changed.

| Width | Before header height | After header height |
| --- | ---: | ---: |
| 320px | 294.6px | 244.6px |
| 390px | 244.6px | 244.6px |
| 768px | 192.6px | 140.6px |
| 960px | 101px | 101px |
| 1366px | 54px | 54px |
| 1536px | 54px | 54px |

These heights exclude the separate 56px mobile navigation header. At 390px the
controls use two wrapping rows with unchanged total height. Desktop layout rules
remain untouched. Saved Members screenshots were inspected at all six widths;
1366px and 1536px primary captures are byte-identical to the frozen build. The
existing six-width suite also captured Workspace views. No screenshot baseline,
threshold, test policy, dependency, or generated artifact was edited.

## Members clipping assessment

No Members grid repair was justified. At 960px, native End navigation focuses the
last `rowActions` column within the visible grid (bounds 685–935px inside
232–936px) and scrolls the content horizontally. At 960/1366/1536px, the page-size
control opens its listbox with Enter, and the page-number control accepts focus.
Pager controls remain within the visible grid. Smaller widths use the existing
member cards. The observed partial columns are accessible internal scrolling,
not demonstrated lost controls. Shared-grid and Members source remain unchanged.

## Additional accessibility findings

Open-notifications inspection found separate issues on the frozen base and after
this patch: unresolved `aria-controls="right-panel-region"`, a drawer's
`aside role="dialog"`, a nested banner landmark, and hardcoded note text contrast.
The same rule IDs occur on the base; this layout patch adds none. Individual
contrast detection varies with viewport/occlusion, so the reduced count at 768px
is not claimed as a fix.

Source comparison against pre-pilot Main
`6f940987f8df746736b6df5ea7371ab698028f5e` confirms the same IDREF/template and
hardcoded `#5f6f7f` note color already existed. The old dark surface `#1C2733`
gives calculated contrast 2.93:1; revision-441 surface `#131F2B` gives the observed
3.23:1, both below 4.5:1. Making dark the default exposes this existing dark-theme
debt to more users. Historical executable behavior was not rerun; that origin
classification is source-backed. The coordinator owns a separate bounded repair.

## Local evidence

- `artifacts/mobile-members-polish/unit.log`
- `artifacts/mobile-members-polish/build.log`
- `artifacts/mobile-members-polish/shell.log` and `shell-captures/`
- `artifacts/mobile-members-polish/baseline-check/inspection.json` and PNGs
- `artifacts/mobile-members-polish/after/inspection.json` and PNGs
- `artifacts/mobile-members-polish/inspect.mjs` (ignored diagnostic harness)

This follow-up remains local for coordinator integration and independent review.
It has not been pushed or published as a new pull request.
