# PR-J: bounded source/native case reconciliation

This candidate implements an offline N03 content adapter and an opt-in, memory-only
Playwright event observer. It does not implement the PR-N final verifier. The
[approved decisions and PR-N contract](https://github.com/NYGsatoshi/Coglatas/pull/1747)
remain binding. The original 45-capability ledger and retained evidence are unchanged.

## Source checkpoint and dependency boundary

The implementation branch starts at SecurityTestImplimation
`3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`. Only #1716's hashed identity
preparation is integrated there. The following refreshed heads remain unmerged:

| PR | Head | Role |
| --- | --- | --- |
| #1717 | `4ca3d55951fbf3808d00d0c5bf8908d47188157b` | Source-only discovery schema and reporter |
| #1680 | `d52b365c1049c6d17915b692855a0c5506fd2fd8` | Two-shard content reconciliation |
| #1738 | `b34a0503ca0637634b3f492b69f3d3491c12c6d2` | Capability ledger |
| #1739 | `768c487b394cf81245fad41fed18e73f15d6c9b0` | Actor diagnostics |
| #1747 | `f3c37ae5b56e42f9b45c6a629ec99015ef7e6895` | D01–D15 and PR-N acceptance |
| #1749 | `fe059005a9e97af3f28db68120331b5b8a983e99` | Independent PR-I coverage work |

These heads had six successful required contexts at the initial checkpoint. That
does not qualify this candidate. #1680's recorded base is older than the current
Security head. No dependency code is silently integrated. This adapter accepts
#1717's existing source schema; it neither discovers source tests nor reconciles
shards. A full, unsharded observation cannot supply invented shard or Artifact IDs
to #1680's different contract.

## Identity audit

The machine-readable [identity map](identity-map.json) records the supported and
unsupported relationships. Source locations below refer to the checkpoint unless
an explicit dependency head is given.

| Producer or consumer | Identity evidence | Qualification |
| --- | --- | --- |
| `tests/ui/run-angular-playwright.mjs` | Literal 24-file static selection, unchanged | Selection only |
| #1717 `playwright-full-source-list-reporter.mjs` | `suite.allTests()` → `TestCase.id` and declaration-time expected status | Source discovery only |
| Locked Playwright 1.64.1 JSON reporter | `_serializeTestSpec` emits `id: test.id` in `playwright/lib/runner/index.js` | Direct SDK mapping; exercised by native SDK controls |
| New memory observer | `onBegin` and `onTestEnd(test, result)` hash the same `test.id` | Native event content, without producer authentication |
| Configured HTML reporter | `_createTestEntry` emits `testId: test.id` | Native identity exists, but complete status/provenance is not established by this field |
| Configured JUnit and `verify-playwright-required-tests.mjs` | Display-title matching | No demonstrated native ID mapping; unsuitable for exact case reconciliation |
| Functional evidence and owner reporters | Journey/owner annotations and status/attempt fields | Journey identity, not native case identity |
| `functional_evidence.py` and `finalize-functional-lane.py` | Source/run checks and owner completeness; original failure/blocked outcomes | No native case-ID inventory |

The default static reporters are list, HTML and JUnit. CI retains browser HTML/JUnit
on failure; a successful job is not a retained complete per-case ID report. The
Functional configuration additionally uses files/FCI04/Functional owner reporters.
No one-to-one mapping from their journey IDs to native case IDs has been proven.
Functional native case coverage remains `COVERAGE_UNVERIFIED`.

The original static source was listed locally with #1717's exact pinned reporter
and verifier, exported outside this implementation tree. It yielded **456 hashed
cases, including two declaration-time skips**, canonical roster digest
`f14a75e25bf4eb41dac5e0742207da8d6747c9e3f76601e2735981f45fafec38`.
This is a source-list observation at the Security SHA, not a candidate execution
or branch-native admission of #1717. Runtime skips cannot be authorized by a
historical aggregate skip count. Dynamic expected-status changes must reject until
their source applicability is independently established.

## Adapter contract

`tests/ui/playwright-case-capture.mjs` exports `NativeCaseCapture`. It is not
registered in either Playwright configuration or any workflow. It writes nothing,
publishes nothing and never overrides the runner conclusion. An authorized caller
may take a snapshot after `onEnd`. The only modification to the existing shard
helper exports its existing hash function for reuse:

`SHA256(UTF8("coglatas.playwright-test-id.v1\0") || UTF8(TestCase.id))`.

Snapshots retain declaration-time and final expected status, native SDK outcome,
every observed retry number/status, full-run status and bounded capture error
codes. The SDK `flaky` outcome is retained as an SDK enum, not a causal diagnosis.
Raw titles, paths, errors, bodies and attachments are never copied. Limits are
5,000 cases and ten attempts per case; unsupported expected failures reject.
List-only collection has no results and cannot appear complete.

`scripts/ci/playwright_case_reconcile.py` accepts one JSON envelope on stdin:

```json
{"source": {}, "execution": {}, "expected": {"source_sha": "<40 lowercase hex>", "execution_kind": "LOCAL", "run_id": "local-<32 lowercase hex>", "attempt": 1}}
```

Replace the empty objects with the original #1717
`coglatas.playwright-source-discovery.v1` record and the observer's
`coglatas.playwright-case-execution.v1` snapshot. GitHub observations instead use
`GITHUB_ACTIONS` and a positive decimal Run ID. Expected metadata must come from
the independent caller, never be inferred from the report being checked.

The CLI bounds its entire envelope to 2 MiB; the Python API separately bounds each
byte input to 2 MiB. Both reject malformed JSON, duplicate JSON keys, nonfinite
values, unsupported fields/schema, invalid IDs and source/run/attempt mismatch.
Valid but incomplete or unsuccessful content produces a deterministic `REJECTED`
report and exit 1, preserving all allowlisted attempts and duplicate records.
Malformed content yields only `CASE_RECONCILIATION_REFUSED` and exit 1. Inputs must
be retained independently; the refusal does not replace original evidence.

`CONTENT_MATCH` (exit 0) requires exact full ID-set parity, correct expected and
actual statuses, no unexpected skips, no missing results, no duplicates, no retry,
no failure/interruption and a complete successful capture. This conservative
candidate does not admit retry-then-success as acceptance.

States are independent:

| State | Meaning |
| --- | --- |
| `SOURCE_DISCOVERED` | Supplied source receipt is structurally and digest valid |
| `NATIVE_EXECUTION_RECONCILED` | Supplied event contents reconcile; not authentication |
| `ARTIFACT_PROVENANCE_VERIFIED` | Always false here; D10 requires independent verification |
| `REQUIRED_GATE_QUALIFIED` | Always false here; protected runtime admission is separate |

Even `CONTENT_MATCH` retains `COVERAGE_UNVERIFIED` and an explicit offline-content
qualification. A self-declared producer label, hash or Run ID cannot authenticate
execution. Artifact ID, GitHub producer/run/attempt, source and content digest must
be independently verified before later provenance qualification. No output field
promotes review, governance, security, performance, Nightly, release or Owner state.

## Validation and admission

Run the narrow controls with:

```sh
python3 -B scripts/ci/test-playwright-case-reconcile.py
node --test tests/ui/playwright-case-capture.node-test.mjs
```

The Node suite also invokes the locked native Playwright SDK on isolated synthetic
pass/declared-skip, failure, retry-then-pass and runtime-skip fixtures, then invokes
the Python CLI on those actual events. These are adapter controls, not product,
browser or authenticated real-backend acceptance. Child titles/diagnostics remain
private. Set `COGLATAS_CASE_PYTHON` only when Python is not available as `python3`.

Original test selection, Functional owner lists, required contexts, worker budgets,
retries, timeouts, assertions, scanners, permissions and artifact ownership are
unchanged. No sharding is activated and no workflow is added. Protected execution
integration needs a separate approval-dependent candidate, complete source/native
evidence and D10 verification. The next eligible slice is qualifying actual
unsharded browser event capture without changing the protected execution contract;
Functional case mapping requires a separate source-confirmed applicability audit.

Registered-ruleset identity drift and Security branch protection remain unresolved.
Six matching checks do not resolve either governance or mandatory independent
review. Keep this candidate unmerged while either requirement is unmet.

```text
PROGRAM_STATUS=INCOMPLETE
SECURITY_BRANCH_INTEGRATION=HOLD
MAIN_UNCHANGED=true
GOVERNANCE_IDENTITY_DRIFT=OWNER_HOLD
TRUSTED_SONAR_IMPORT=NOT_VERIFIED
PR_J_TO_N_ACCEPTANCE=OPEN
```

Rollback is a revert of this candidate's additive observer, adapter, controls and
documentation, plus its hash export. Preserve all source receipts, original
failures, native run attempts, cancelled/skipped jobs and unsuccessful reviewer
attempts independently; a rollback never deletes that history.
