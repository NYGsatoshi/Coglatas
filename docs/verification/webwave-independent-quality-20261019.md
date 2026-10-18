# Web wave independent frontend quality review — 2026-10-19

This is Agent D's independent review and synthetic-browser evidence. It grants
no specification, security architecture, deployment, or merge approval.

## Source and scope

- Baseline Product Main: `9b44d631df88cdc2f15a45b6278b3e1b2552a8df`.
- Agent A review: `c3dab4d7`, then quality repair `461d82c4` / PR #1252. DM and Teams directories reuse the
  existing authorized paginated Conversation API. Normalized `httpStatus`
  denial handling clears previous rows and the active persisted draft.
- Agent B initial review: `cef3390a`, then `d2c82ce9`, accessibility repair
  `cf192309`, focus repair `5e24e417`, and narrow spacing `88bfb3ca` / PR #1251. Drafts use an NgRx ComponentStore with tab-memory
  checkpoints; no successful server save is representable in the default
  adapter. Links is an explicit opt-in synthetic demonstration.
- Agent C initial review: `7f66888e`, then static-quality repair `85205439`, responsive repair `a53497de`, cache repair `c2116300`, and focus repair `7d63e41d` /
  PR #1250. Stars store only versioned IDs partitioned by Tenant, user and
  Workspace. Exact existing File GETs reauthorize names on every visit.
- Test source: `tests/ui/web-wave-local-workbench.spec.ts`, introduced in
  `2ad3701e582c80f62918904390717bb10a493437`; the normal static-suite runner
  explicitly includes this file.
- Coordinator generic action-key repair: `4f456a0e681e23f6331627d16d2bdcedaa8a7744`,
  with stable row identity `3c548230588856c4d3707b61737e8e890984211e`.
- Final combined committed qualification source:
  `3c0f75d697bec45798e3185b44e4c9fd81dddbfa`, merging coordinator source
  `a269944331863da1a22925eea702c83cbd34bd17` with Agent D's stronger tests.
  Test blob: `439392d73fdf24ce3e406bc4e8c93de731b9aa0c`.
  Frontend tree: `12bc68fbce2707c654faa20a7fb09781a355594e`.

No production source was changed by Agent D. Repairs remain on the owning
feature branches or the coordinator branch and are integrated by merge,
preserving ownership/history.

## Independent findings and retained failures

| Finding | Evidence | Disposition |
| --- | --- | --- |
| Draft Links empty-result `<li role="status">` overrides list-item semantics | Exploratory Chromium run on built source `e050b86d`; axe `list` serious and `aria-allowed-role` minor, in light and dark themes | Owner repair `cf192309` retains native `li` and nested `output`; repaired Draft browser matrix passed 8 cases |
| Local Starred can be saved but cannot be opened at 320px | File-browser host was hidden below 860px, with no alternate shortcut control; visible-user-action regression failed | Responsive repair `a53497de`; repaired narrow Starred browser matrix passed 4 cases |
| Cancelling inline Draft discard loses focused control | Keep editing removed the focused button without returning focus | Focus repair `5e24e417`; repaired Draft browser matrix passed 8 cases |
| Desktop File filename action does not respond to Enter | Native action button is focused, but AG Grid intercepts activation and Preview stays empty; existing Main renderer is unchanged by the original wave | Repair `4f456a0e`; final matrix verifies keyboard activation without weakened assertions |
| Starred denial hides its list but leaves a cached filename in the disabled attachment picker | Whole-page redaction assertion fails after exact File GET 403; stale `vm.pickerFiles` remains visible | Repair `c2116300`; final matrix verifies full-page redaction and held Recent reauthorization |
| Starring a File recreates its desktop grid action and loses Preview return focus | After keyboard Preview open, Star and Close, the stored DOM node is detached and focus does not return to the current filename action | Repair `7d63e41d` plus stable identity `3c548230`; final matrix verifies return focus |

The exploratory run reported **8 FAIL**: four Draft cases exposed the list
semantics defect; two narrow Starred cases exposed hidden shortcuts; two
desktop Starred cases had an incorrect test selector. Desktop grid buttons
use the filename while mobile cards use `Preview <filename>`; this selector
was corrected in the test without weakening product assertions. The actual
shortcut accessible name is `Starred files`.

Exploratory log: `d-interim-browser.log`. Failure screenshots and error contexts
were retained in `d-interim-artifacts/`. They are diagnostic Windows evidence,
not approved Linux visual baselines.

## Executed checks

| Check | Source | Result |
| --- | --- | --- |
| Focused Draft component/store, stars repository/service, messaging component tests | `65edf74ebb15b2d026d687be865b274c149ad6c3` | **PASS: 5 files, 119 tests** |
| Production Angular build using explicit `ng.js` with D's frontend working directory | `e050b86d7ae40bded4f137b44fe49c357b1fcc2e` | **PASS**; initial raw total 929.33 kB; existing style/bundle warnings retained |
| Initial exploratory static browser matrix | Built source `e050b86d` | **FAIL: 8 cases**, classified above |
| Intermediate combined production Angular build | `a7c428955c9794c9455629c86d31bb6bcfc878f3` | **PASS**; initial raw total 929.74 kB, estimated transfer 193.73 kB; warnings retained |
| First combined 20-case browser run | Built source `a7c42895`, test blob `514f4d0c` | **12 PASS, 8 FAIL**: 4 Details-tab test assumptions and 4 inherited desktop keyboard failures |
| Corrected narrow Starred matrix | Test commit `c48e81e49d3fbef303c3839a52db64f0290e0170`, same built source | **4 PASS**; Details hides Preview controls, Preview exposes disabled content action |
| Stronger whole-page denial probe | Test commit `a401ff01`, same built source | **1 FAIL**; confirmed stale disabled-picker filename |
| Pre-focus-repair matrix including whole-page denial and held reauthorization | Source `8f27289563d6da9c7a1b254476bf02d7c43a8dba` | **16 PASS, 4 FAIL**; all failures are desktop Preview return focus after Star; all narrow denial/cache assertions passed |
| Final production build | `3c0f75d697bec45798e3185b44e4c9fd81dddbfa` | **PASS**; initial raw total 929.74 kB, estimated transfer 193.70 kB; existing warnings retained |
| Final desktop keyboard/focus preflight | Same final source and unchanged test blob | **1 PASS**, 4.5 seconds |
| Final strong browser matrix | Same final source and unchanged test blob | **20 PASS, 0 FAIL, 0 SKIP, 0 FLAKY**, 58.2 seconds; no retry |

The final selection contains 16 new Draft/Links/Starred cases plus 4 independently
rerun DM/Teams/paging/denial cases from Agent A, across both ordinary Chromium
projects. The test timeout remains 30 seconds and assertions were preserved.
All findings above are resolved within this selected frontend scope; broader
runtime and environmental exclusions below remain unqualified.

Final browser command: `node node_modules/@playwright/test/cli.js test
tests/ui/web-wave-local-workbench.spec.ts tests/ui/message-search-filters.spec.ts
--grep='Web wave local|separates DM and Teams|pages the existing'
--project=chromium-desktop --project=chromium-mobile --reporter=line,json`, with
the freshly built D output served only at `http://127.0.0.1:4181`.

Final logs and artifacts: `d-accepted-build.log`, `d-accepted-browser.log`,
`d-accepted-browser.json`, and `d-accepted-test-results/`. The complete built-file
hash manifest is `d-accepted-build-manifest.json`, SHA-256
`bc6502a001ad3aab9db2248b1b8a26af0ae9860681d040123499e3e251605483`.
The manifest binds every built file to the source commit, frontend tree, test
blob and Node.js `v24.19.0`.

Windows screenshots were visually inspected for the narrow Draft notice spacing,
light/dark presentation, visible responsive shortcuts, long Unicode wrapping,
and the absence of prior Recent names from the Starred attachment picker.
The final 320px Draft notice is clear of the fixed theme control. These are
diagnostic observations, not Linux screenshot-baseline approval.

The unit command was `node node_modules/@angular/cli/bin/ng.js test
--watch=false --include='src/app/features/drafts/**/*.spec.ts'
--include='src/app/features/files/file-stars*.spec.ts'
--include='src/app/features/messaging/messaging-ui.spec.ts'`. JSDOM emits
`cdkFocusInitial` focusability warnings; browser focus checks are separate.

## Browser regression contract

The new eight-case source matrix runs at 320 and 1366 CSS pixels, in light and
dark themes, in each ordinary Chromium project. It checks:

- Working Unicode text, checkpoints, explicit unavailable server saving,
  synthetic composition events, and no accidental network mutation.
- Links opt-in, normalized full-width search, add/remove, empty results, and
  focus return. No real resource association is claimed.
- Draft retention across same-Workspace Angular navigation, explicit discard
  and cancellation, and loss after refresh as documented by the UI.
- Saving/removing local stars, exact opaque storage schema, reauthorized
  Unicode names, Details-only behavior, and focus return.
- Offline File-detail failure hides unverified names, retains only IDs, and
  recovers on retry. HTTP 403 prunes the inaccessible ID and redacts diagnostic
  text. No Draft, Links, Starred, Shared or Artifacts API is invented.
- Returning to Recent holds the authorized list response open to ensure neither
  the list nor the disabled picker can revive a revoked filename before or
  after the response.
- Full-page axe, document horizontal containment, and diagnostic screenshots.

Synthetic composition events verify browser handlers; they are not native OS
IME evidence. Mock responses prove frontend state and presentation behavior,
not ASP.NET Core authorization or PostgreSQL persistence.

## Separate Agent E review

Agent D independently reviewed the production filename fix
`eaf7c7a3da428d5db7306a8c4255fb43bc3fb88e`, regression source
`a15e130bdae47e09d9b1b193d1d99b2c3c7b01b6`, and HTTP regression source
`48236eb7a24582f83a3f5692ad07840054b62aa9`. No blocking source finding was
identified. The filename change tests the valid pair across positions 259/260
only when input exceeds the existing 260 UTF-16-unit bound; reducing the cut
to 259 avoids preserving a high surrogate without its low surrogate. Tests
exercise both sides of that boundary.

The HTTP test source covers positive direct/grant downloads, persisted
membership revocation, no additional storage read, generic missing/denied
error equivalence, no protected values in responses/audit/security/logs, no
error-level logged exceptions, unchanged File digests, and restored access.
This is static independent review; Agent E owns its execution evidence. It
does not establish PostgreSQL/RLS or broader SEC-ARCH qualification.

## Limits and HOLD

- Agent D introduces no backend, migration, RLS, Role/GRANT, Issuer, custody,
  or cursor-key change.
- No private specification contents or owner approval claims are included.
- Docker/Linux screenshot parity, real-backend browser acceptance, hardware
  profiling, low-end/Chromebook memory/scroll assurance and public HTTPS are
  **NOT_EXECUTED in this Agent D run**. Docker daemon availability and protected
  environment configuration remain separate gates.
- Existing bundle/style budget warnings are retained. Raw build sizes are
  observations, not a new numerical performance acceptance threshold.
- Agent D does not merge. GitHub CI and exact-final-source qualification remain
  coordinator responsibilities; no earlier green run qualifies a later head.

Local logs/artifacts are under
`C:/Users/Hirota Kaname/AppData/Local/Temp/coglatas-webwave-20261019/`.
