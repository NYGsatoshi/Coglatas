# Fixed model summary to its existing recording

Existing work: #1033 / #782 under #888. The paired fixed model trace summary
already shows exact metrics/provenance and its admitted model recording. This
slice gives that summary an explicit keyboard action that focuses the recording
already present above it. No new recording, simulation or persisted query is
created. The shared profile protocol, generated fixture bytes, definitions,
units, Complete/Incomplete meaning and original event rows stay unchanged.

## Ownership and interaction

The pure ViewModel's ReadSimulationTraceProfileRun accepts only the exact current
summary instance and returns that summary's already-owned immutable Run. A
copied equal-content summary, an older query, a replaced run, Source reload or
reset cannot retrieve the current recording through an old basis. Refused
summaries have no Run. Disposed/null arguments follow the ViewModel API guards.
The method emits no notification or mutation and does not acquire new bodies.

The Angular store likewise resolves only its exact current admitted summary,
whose existing getter validates current bundle/context/result/run ownership.
The component checks current read availability and then validates that basis
before advancing focus generation. A stale callback must not cancel the focus
scheduled by a newer summary. Access withdrawal still clears the local view.
These local ownership/UI checks are not server authorization.

The English/Japanese button focuses the existing recording section, using its
heading as the accessible name. It retains all six metric rows and their exact
original data. Root, external parallel and bounded-prefix examples keep their
original simulated-model labels and provenance; no absent operational or future
events, Resource/person cause, outcome judgment or predictive validity is added.

## Product verification boundary

Native Product tests use actual existing Compiler/recording fixtures. They retain
original Source/result/run/summary data, reject detached and older basis objects,
and cover profile/run/Source/reset/refused boundaries. Angular tests check the
same exact-instance refusal, lifecycle withdrawal, both localized actions and a
stale/revoked callback while preserving a newer pending focus. Actual browser
cases require keyboard recording focus, unchanged metric text, no modifying
requests, accessibility and Source-reload withdrawal in both locales across the
three existing model families.

The private first Angular focused result67PASS/1FAIL and its exact source are
retained. The failure asserted focus before the component's queued setTimeout
callback ran; fixture.whenStable does not flush that callback. The same existing
zero-delay macrotask flush now precedes the unchanged exact identity assertion.
The original Node mirror's three stale reference-page inputs were separately
refused and reconciled before compilation; no source guard or assertion changed.
Record exact current source/Tree, executed counts, failures, pins, review and CI
in the PR. Native-only, focused Angular, full Angular and actual browser results
must remain separate until each has actually been executed and qualified.

This is a partial offline Product navigation improvement. It adds no server or
observed-data adapter, durable drill-down/query, export, retention, cross-Project
contribution admission or permission grant. Independent current actor/Tenant,
privacy, original-model/operational separation and actual Web/Avalonia security
qualification stay OPEN/P0 #1962 (related #1951/#1932/#793/#794). Product tests or
a human-review waiver do not satisfy those controls. Full #1033 remains OPEN
for observed result kinds, matched Scenario comparisons, critical-path/Resource/
approval metrics and privacy-safe integration. #1144 BLOCKED, #1128 SUSPENDED/
NOT_EVALUATED and Registry/Trust/Eligibility/RLS/Native/Production/Release HOLDs
remain unchanged.
