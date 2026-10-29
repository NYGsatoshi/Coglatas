# SEC-04 caller main-path controls

Tracking: #615. Baseline Main `a36d5978818edf6f1d96ce07152120016265f749`.

## Scope

Add ten main-path tests to the existing twenty SEC-04 caller controls. Production caller, policies, native acceptance parsers, workflow, scanner budgets/seeds/roles/targets, qualification-branch admission, publication format and permissions are unchanged. No held implementation is copied or contract activated.

The unit fixture intercepts every child command; no real Docker, registry, scanner, network target or GitHub operation occurs. It uses local synthetic files and the actual existing five-role metadata/native-event parser. Contract reservation/staging collaborators are mocked here; their existing POSIX controls remain unchanged and run on Linux. A technically successful unit result always retains securityProgramAcceptance=NOT_GRANTED, scheduleActivated=false and publicGo=false. This is not actual deep execution, program approval or release evidence.

## Meaningful controls

- Positive main flow binds expected source/tree and a digest-pinned image, consumes all five synthetic native reports, preserves private child-output canaries, publishes once and emits the successful publication marker.
- Wrong environment/event/ref/run/attempt/password and extra CLI arguments reject before any child command.
- Wrong source/tree or dirty initial source cannot build or start a runtime.
- Remote Docker context/certificate settings and target/example/seed/project/state overrides do not reach commands. Explicit Docker argv and child environment retain the local socket; runtime stages use the inspected image digest. Caller environment remains unchanged.
- Build, scanner and cleanup failures remain nonzero, preserve first exit codes and invoke cleanup once.
- Missing native role, source-binding mutation or dirty final source cannot inherit otherwise successful stage exits. Negative controls require the exact expected phase and all four stage records, preventing unrelated early failures from passing.
- Existing qualification-branch push still requires complete native evidence. Publication failure returns nonzero without a published marker or private error text.

## Verification and limits

- Unchanged baseline:20/20 PASS on Ubuntu/Python3.14.4,0.059s.
- Initial29-case extension:3FAIL/1ERROR due to a missing synthetic reports parent, retained. Correct the fixture and strengthen exact negative-phase/stage assertions;29PASS. Refactor the finite fake-command boundary into small test-only methods without relaxing assertions.
- Final suite:30/30 PASS on Linux,0.126s; same suite under pinned coverage.py7.17.0 Python tracer:30/30 PASS,0.208s.
- Operational caller:261statements/3missed,40branches/2partial, combined coverage.py98%. Prior independently inspected Main7ee report had185/261statements and30/40branches; the production caller is unchanged in Maina36. These are measured-subject values, not Sonar New Code Coverage or an official Gate.
- Original process data retained. Existing already-imported warning for sitecustomize.py retained, not suppressed.
- Current exact Sonar source/scope/AST controls:11PASS, including pure-unit classification and operational CLI/import retention. Test-body coverage classification, source/rule analysis and thresholds are unchanged.

Fresh protected CI/native analysis, official Gate, independent review, normal merge and resulting Main qualification are required. All historical failures, formal security/model/RLS/Recovery/staging/admission holds and STUDENT_COUNCIL_PILOT=RELEASE_NO_GO remain.

## Observed native quality follow-up

The original 6093 native analysis job 123591826190 was SKIPPED. The next 56ce analysis ran and official Gate 123615361815 passed, but reported 26 Low Maintainability `python:S1481` issues in new tuple bindings (lines 516, 535, 544, 556, 564, 571, 579, 591 and 600; provider keys AaGIs1PCIAhmcECsAX_B through AX_a). Current-code AST/liveness checks confirm these 26 bindings are unused and no affected method introspects locals. Replace only those discarded bindings with the ordinary underscore discard. The resulting AST differs only in these 26 Store names; all assertions, controls, fixture calls and production behavior are preserved. No provider dismissal, suppression, source exclusion, rule or threshold change. Fresh tests, analysis and current-source review remain required.
