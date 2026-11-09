# Held Thread response mechanism investigation

This independent test-only investigation starts from reviewed integration
`6d4f1c35f4464f1e3229d69658282b609a498c98`. It does not modify product source,
the canonical real-backend owners, the protected workflow, or frozen mobile
source `b9c3f5e1b671421f34377838f6bdc03d53538a1f`.

The earlier integration `bed6e2f7382f5ee10189c04064e50eb10b2622e0` failed
run `39955831750`, job `120421315334`, during restricted retained-tab activation.
The timeout alone establishes neither its cause nor an authorization outcome.
The later diagnostic integration pass does not retroactively explain that
failure. It remains a release blocker pending evidence-backed disposition.

## Gate choice and default-fixture preservation

`BrowserSmokeResponseGateRegistry.IsAllowedTarget` accepts only project Kanban
and Gantt GETs. It cannot gate Thread reads. Its server allowlist and runtime
boundary remain unchanged. These cases therefore use mocked transport with
the actual Angular UI; no real cookie, backend, PostgreSQL, SignalR or actor
isolation credit is claimed.

The existing `installThreadApi` and its DTO builders move from the Thread spec
to `tests/ui/fixtures/thread-api.ts` without changing their default functions.
Their normalized source SHA-256 before and after extraction is
`8486949b6e4b17d08ba8f5d4d5217f604e55dfaf7ce6e85f790c67fab29cfa04`.
The existing spec imports that same fixture. A separate diagnostic route overlay
adds queued hold/release and explicit synthetic 400 controls; the default fixture
and its established 403 behavior remain unchanged.

The background read is a product path: an actual UI reply receives a synthetic
validation 400; `sendThreadDraft` calls `refreshThreadProjection` while keeping
the body/draft until the authoritative GET completes. Tests do not inject or
invoke Angular services or product globals.

## Predeclared cases

| Case | Controlled ordering | Required observations |
| --- | --- | --- |
| TD-01 | Ordinary click on an actionable retained selected tab | Positive 200 control, a newly emitted GET, exact hidden 400 envelope, nonleak and complete protected UI/draft clear |
| TD-02 | Release a held background denied GET before another tab activation | Retained body/draft before release; exact hidden 400, protected state and tab removed; no additional GET or activation claimed |
| TD-03 | Begin ordinary tab click, observe its new GET, release the earlier denied background response, then release the new denied response | Old response cannot satisfy the new request observation; newer loading tab survives stale generation; exact fresh 400 and complete UI clear |

TD-02 deliberately records `LEGITIMATE_EARLY_REDACTION`, `clickPerformed=false`
and `freshStatus=null`. A missing tab or zero new events is not isolation proof
and does not qualify the canonical retained-activation owner. TD-03 retains the
same `responseToFreshRequest` helper as the reviewed real owner, with no old
response substitution, force-click or API-only activation.

`oldResponseExcluded` is explicitly `null` (not applicable) for TD-01, which has
no older response, and TD-02, which has no later activation/new request. Only
TD-03 asserts `true` after exercising both requests. The contract rejects either
boolean in the non-applicable cases and rejects false/null for TD-03.

The initial source `5a917bc6a40cbfc6df1922bd614ea16dadc49789` executed all three
matrix cases and three existing regressions successfully. Its retained receipt
incorrectly emitted `oldResponseExcluded=true` in all cases; those first two
values must not be used as exclusion evidence. This evidence-semantics correction
requires its own committed-source execution. The UI assertions, default fixture,
product behavior and canonical owners remain unchanged.

The matrix fixes Chromium touch/mobile emulation, 320x800 viewport/screen,
scale 1, one worker, zero retries, the existing mobile 120-second test and
15-second expect deadlines, and stop-on-first-failure. No authenticated pixels,
traces, videos, raw assertions, response bodies or arbitrary attachments are
published. The sole reporter accepts exactly three complete cases and emits
only fixed classifications, phases, statuses and booleans. Its label is
`MOCKED_TRANSPORT_UI_MECHANISM_ONLY`, never real acceptance PASS.

## Reproduction

Build the unchanged Angular source locally, then run:

```powershell
node node_modules/@playwright/test/cli.js test --config playwright.thread-investigation.config.ts --output .\artifacts\thread-investigation-output
$env:THREAD_INVESTIGATION_MODE = 'default-fixture'
node node_modules/@playwright/test/cli.js test --config playwright.thread-investigation.config.ts --output .\artifacts\thread-investigation-output
```

Native non-Linux runs require an explicit trusted output directory. Select a
private location appropriate for the operator and machine; the example path
does not establish a Windows ACL guarantee. Linux may use its private default.

The second run requires three existing regressions: ordinary dedicated-pane
keyboard/draft operation and authoritative 403 purge at 320px and 1366px. Those
tests retain their own explicit viewport overrides; this is fixture regression
evidence, not device compatibility qualification. Their existing static test
and expect deadlines remain 30 seconds and 5 seconds; retries remain zero in
this bounded investigation. Discovery creates no execution
receipt. Tests require the declared local loopback static server and do not use
the protected licensed environment. An unlicensed local frontend build is
diagnostic compilation only.

Results, exact committed source, finite artifacts and all failed attempts must
be recorded separately. Even a reproduced mechanism cannot prove the missing
historical network ordering or remove the bed6 release blocker by itself.
