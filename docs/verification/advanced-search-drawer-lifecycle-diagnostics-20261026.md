# Advanced-search drawer lifecycle diagnostics

Status: **REVIEW_ONLY** diagnostic candidate for
[Issue #1413](https://github.com/NYGsatoshi/Coglatas/issues/1413).
This does not repair or establish the cause of the observed browser failure.
Student Council remains **RELEASE_NO_GO**; existing Owner holds remain in force.

## Retained first-attempt evidence

Exact Main `d78f3722f04c3573c3f0890f9ca950d24090144e`,
[run 40219519075](https://github.com/NYGsatoshi/Coglatas/actions/runs/40219519075),
attempt 1, frontend job `121248997035`: mobile
`message-search-filters.spec.ts:259` failed its unchanged five-second
advanced-drawer visibility assertion after native Enter on its opener.
Retry 1 passed. The job's terminal SUCCESS does not erase that initial failure.

The successful job did not retain general failure-only trace/screenshot/video
bytes. A logged artifact filename does not establish available evidence.
The initiating callback, DOM identity and actual focused target remain UNKNOWN.
No new general trace publication is introduced to fill that gap.

A separate controlled experiment with actual Angular page/facade/search
components demonstrated a reachable lifecycle mechanism: registered ordinary
directory catch-up destroys the open child while the parent loads, and a fresh
list mounts a different closed child. The control's native opener click works
without catch-up. This does not prove that catch-up caused the hosted failure.
Existing public loading state cannot safely distinguish routine work from every
manual/protected transition; no speculative query, result or drawer restoration
is implemented here.

## Finite observation contract

Only the existing representative mobile advanced-search owner is wrapped.
Its assertions, native actions, five-second expectation, configured retries,
privacy checks and selected API behavior remain byte-identical. Removing the
single import and wrapper reconstructs the entire Main d78 test file, SHA-256
`037fb92b9d35303d2aed23760b03cea95bbc2eb34faac9049d9f04725766ba6f`.

An init script records at most **128 events / 64 KiB per attempt**, with a
fixed Chromium-mobile profile and attempts 0–2. Its closed schema permits only:

- Native Enter/Tab/Shift+Tab/Escape, click and focus phases, with closed target
  categories, connectivity, document focus and default-prevented flags.
- Ephemeral positive integers identifying DOM nodes, capped at 4096. These
  numbers are local to one document and contain no application identifiers.
- Current and previously tracked search-host connectivity, opener/drawer node
  identity, panel/drawer-open flags and parent loading/denied flags.
- Bounded monotonic milliseconds and explicit outcome/capture/truncation state.

Mutation observation records only changes in that structural projection.
It never serializes mutations, DOM content, input values, arbitrary attributes,
queries, search terms, suggestions, names, URLs, request/response bodies,
cookies or authentication/resource identifiers. Finalization disconnects the
observer and aborts its event listeners. The script does not prevent an event,
change focus, activate a control or alter product state.

Complete finite captures are classified only **UNKNOWN**. Empty, unavailable or
truncated captures are **INCOMPLETE** and never causal qualification. The original
primary error is preserved by identity if final capture/publication also fails.
A diagnostic failure cannot make an otherwise failing owner green.

## Publication boundary

The new collector follows the existing bounded keyboard pattern. It validates
every file before creating output; rejects extra/raw fields, invalid names,
oversized inputs, linked directories/files and special-file acquisition races;
and bounds same-inode growth. It requires clean tracked source, exact current
`GITHUB_SHA` and the trusted `frontend-build-sha` producer marker. Each newly
serialized output contains both source and producer SHAs. Stale output is never
overwritten. Missing input gives no execution credit.

PR and Main workflows always collect after the browser job and publish only
`artifacts/search-drawer-observation-validated/*.json`, after collector success,
with seven-day retention. First failure and retry success have distinct files.
The existing general-browser artifact remains conditional on job failure and
its paths are unchanged. No production, MessagingFacade, server, authorization,
RLS, schema, deployment, held Composer or held Thread diagnostic code changes.

## Local validation and limits

- All four browser-helper files: **36/36 PASS**, including validator/privacy
  negatives, explicit unavailable/truncation controls, exact-primary preservation,
  actual mechanical Playwright retry-file retention, synthetic source/producer
  binding and collector file-race/publication guards. No skips. An initial
  restricted-sandbox subprocess `EPERM` affected the existing file-race control
  too; the unchanged controls passed with the supported execution grant.
- Portable native recorder controls: **3/3 PASS**. Native Enter opens the
  synthetic drawer without leaking a private-text canary; host replacement
  records different nodes and disconnected old host; a finite-cap control
  retains exactly 128 events and explicitly reports INCOMPLETE.
- The unchanged complete representative mobile owner passes locally with zero
  retries using the exact Main d78 production build and pinned Playwright 1.64.1
  / Chromium 156. Its final recorder output has 84 events, is untruncated and
  UNKNOWN. A prior positive run before listener-finalization cleanup also passed;
  neither run replaces the retained hosted first failure.
- The changed UI owner passes strict TypeScript checking; diff checking passes.

```sh
node --test tests/ui/student-council-pilot-helpers.node-test.mjs tests/ui/group-keyboard-observation.node-test.mjs tests/ui/search-drawer-observation.node-test.mjs tests/ui/public-https-golden-path-helpers.node-test.mjs
node tests/ui/search-drawer-observation.browser-control.mjs
node frontend/node_modules/typescript/bin/tsc --noEmit --target ES2023 --module ESNext --moduleResolution Bundler --strict --types node tests/ui/message-search-filters.spec.ts
```

These are mocked UI/diagnostic controls, not real ASP.NET Core/PostgreSQL,
physical-device, hosted exact-candidate or resulting-Main qualification.
Independent review and exact-head CI remain required. Historical Main failures,
security approvals and every mandatory release gate remain independently open.
