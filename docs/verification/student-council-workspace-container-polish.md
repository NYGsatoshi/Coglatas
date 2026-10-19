# Workspace attention-card width follow-up

Date: 2026-10-19. Base: `abb6ab2ff3afa839d7f7e57ac54a50896648cf87`.
Branch: `codex/pilot-a-workspace-container-polish-20261019`.

## Observed defect and correction

Independent review of the 960px open-notifications capture found word fragments
in the Workspace attention title. The Dashboard's available width was 424px,
while its desktop empty-state and search layouts still used two columns. A native
Range measurement confirmed that both `needing` and `attention` each spanned two
lines within the words. The page did not overflow, so overflow checks alone did
not establish readability.

The nine-line stylesheet change names the Dashboard host as an inline-size
container. Inside the existing desktop rules, an available width of at most
40rem changes only the empty attention card and local search to one column.
This follows the existing Files component container-query pattern. It retains
safe wrapping, existing revision-441 tokens, responsive mobile rules, content,
and controls. No API, permission, state, markup, dependency, or test policy changes.

At the same 424px available width, the attention content now has a 390px track,
and search has a 398px track. Each title word fits one line. The title, unresolved
count, empty-state message, search, and notifications controls remain visible.

## Executed verification

The pinned `mcr.microsoft.com/playwright:v1.64.0-noble` runner used an isolated
output directory. Frozen base assets were copied before comparison. Browser
checks use synthetic static data and do not establish real-backend acceptance.

- Dashboard unit suite: **25/25 passed**.
- Production build: **passed**, initial **930.13kB**, estimated transfer
  **193.50kB**. Existing warnings remain; budgets were not changed.
- Existing shell tests at 320/390/768/960/1366/1536px in both Chromium profiles,
  plus both approved Linux screenshot baseline cases: **14/14 passed**, one worker,
  retries zero. Baselines and thresholds were not updated.
- Additional before/after inspection: all six widths in both closed and
  open-notifications states. All twelve after states have zero document, attention,
  or search overflow, zero axe violations, and no split title words. Search/filter
  and keyboard panel-close/focus return pass at every width.
- Saved 960px after capture was visually inspected. Closed and open 390px captures
  are byte-identical to the frozen base. The existing approved 1280px desktop and
  390px drawer baselines pass without updates.

Local evidence is retained under `artifacts/workspace-container-polish/`:
`unit.log`, `build.log`, `browser.log`, `browser-output/`, and the before/after
`inspection.json` files and PNGs. The ignored `inspect.mjs` harness records native
word-line measurements, track widths, overflow, and axe results.

This is a bounded readability correction. Full Penpot parity, physical-device
qualification, real-backend integration, and public pilot release remain outside
this evidence. The workstream commit was produced locally, then integrated into
the coordinator's existing #1280 follow-up for independent source/visual review
and fresh protected CI. It does not create another runtime or permission contract.
