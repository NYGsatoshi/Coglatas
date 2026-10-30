# Explicit Node test registration completion ownership

Sonar's current Main analysis reports 193 S9383 findings on unused completion
promises returned by Node's test-registration API. For these source-bound calls,
Node's runner already owns callback execution, completion and failure reporting.
The change makes that intentional ownership explicit with `void test(...)`.
It does not add an error handler or change test behavior.

## Exact source classification

The official issue snapshot is bound to Main
`99f257b5a2b0859581386cefe9b07abf2cb931c0`, analysis
`66aa8640-9f4a-4486-847b-8ffe32f0307e`. The analysis identity was unchanged
before and after the query. All 25 affected file blobs are identical on candidate
base Main `2fad6bb4412fc5241a68a904e8dbbea77cb6f317`, tree
`af329091ea0b378bf02cdaf2a82d6d85c356ebf2`; this is source equivalence, not a
claim that the old analysis ran on the new revision.

Each of the 193 issue ranges exactly matches one ExpressionStatement whose
callee resolves lexically to the default import from node:test. There are
191 module-top-level registrations and two registrations in module-level
for-of loops; none is nested in a function/test callback. There are 65 calls with an explicitly async inline callback and 128 without
an async modifier at registration; this is syntactic, not runtime return-type
classification. No imported binding write was found.

The two control-flow sites are av-mig-source.node-test.mjs:137 and
thread-initial-observation.node-test.mjs:269 in the analyzed source. They remain
ordinary module registration loops; no await or iteration change is introduced.
Nested test-context subtests are not covered by this recommendation: their
parent-completion obligations require separate review.

| Affected source | Reviewed registrations |
| --- | --- |
| frontend/scripts/angular22-third-party-compatibility.node-test.mjs | 4 |
| frontend/scripts/av-mig-inventory.node-test.mjs | 1 |
| frontend/scripts/av-mig-source.node-test.mjs | 3 |
| scripts/ci/check-npm-audit-baseline.node-test.mjs | 4 |
| tests/ci/test_restore_data_dependency_advisor.mjs | 23 |
| tests/diagnostics/thread-matrix.node-test.mjs | 5 |
| tests/functional/diagnostic-fixture-runtime.node-test.mjs | 3 |
| tests/functional/fci07-runner.node-test.mjs | 1 |
| tests/functional/fresh-request-response.node-test.mjs | 7 |
| tests/functional/functional-diagnostics.node-test.mjs | 3 |
| tests/functional/messaging-observation.node-test.mjs | 7 |
| tests/functional/mobile-acceptance.node-test.mjs | 9 |
| tests/functional/thread-initial-observation.node-test.mjs | 14 |
| tests/functional/thread-probe-observation.node-test.mjs | 20 |
| tests/ui/compat-browser-engine-workflow.node-test.mjs | 1 |
| tests/ui/group-entry-coverage.node-test.mjs | 1 |
| tests/ui/group-keyboard-observation.node-test.mjs | 13 |
| tests/ui/prepare-host-artifacts.node-test.mjs | 12 |
| tests/ui/readonly-roster-fixture.node-test.mjs | 4 |
| tests/ui/readonly-roster-runner.node-test.mjs | 20 |
| tests/ui/real-backend-smoke-compose-helpers.node-test.mjs | 8 |
| tests/ui/search-drawer-observation.node-test.mjs | 10 |
| tests/ui/student-council-pilot-helpers.node-test.mjs | 13 |
| tests/ui/student-council-pilot-origin-guard.node-test.mjs | 1 |
| tests/ui/webkit-pinned-provisioning.node-test.mjs | 6 |

## Change and error ownership

Only the catalogued expressions receive a void prefix, with one brief ownership
comment per file. Call arguments, callbacks, bodies, assertions, timeout values,
options, skip/TODO behavior, loops and scheduling remain unchanged. A byte-level
reverse-edit guard reconstructs every original file exactly, and a parsed-token
guard permits only the 193 inserted void keywords plus comments. No generic
Promise expression is altered. No rule profile, exclusion, threshold, API,
authorization or feature activation changes are made.

SonarSource maps S9383 to
[typescript-eslint/no-floating-promises](https://github.com/SonarSource/SonarJS/blob/master/packages/analysis/src/jsts/rules/README.md).
The [upstream rule documentation](https://typescript-eslint.io/rules/no-floating-promises/#ignorevoid)
accepts explicit void intent by default and warns that it does not handle
rejections. [Node24 test API documentation](https://nodejs.org/docs/latest-v24.x/api/test.html#testname-options-fn)
defines the registration return as a completion promise fulfilled with undefined;
callback failures are reported by the test runner and produce a failing process.
The proposal is limited to that independently verified framework contract.

## Verification

Pinned Linux Node24.22.2 controls compare original and annotated code in six
paired scenarios, twelve executions. Synchronous and asynchronous registered
failures retain TAP failure and exit1 with zero unhandledRejection events.
Passing async work is awaited, sibling/module-loop order is identical, and
skip2/TODO1 counts remain unchanged. A deliberately unowned generic rejection
still produces exit1 and an unhandledRejection event in both forms, demonstrating
that void is not an error handler.

The full affected 25-file cohort passed before and after the annotation under
Linux Node24.22.2: 284 passed, zero failed/cancelled/skipped/TODO, exit0 in both
executions. All test names, ordering, counters and skipped/TODO entries match
exactly. The existing six controlled-DOM Chromium regressions execute inside
this cohort; no Product server or database is started.

The image combines the repository-pinned Node24.22.2 binary with the pinned
Playwright image. Frontend dependencies were installed with explicit npm11.17.0
from the unchanged lock; existing root dependencies have the same exact lock.
Both executions used the identical image/dependencies and one-worker selection.

Initial setup evidence is retained separately: Windows archive line endings
were rejected by source guards before test execution; a dependency CLI path was
corrected to the pinned npm; and the first physical dependency-directory layout
caused 274 passes/10 module-resolution failures. Correcting only that harness
layout produced the before/after284 passes. No assertion, timeout, retry or
skip policy was changed to obtain those passes.

| Immutable local evidence | SHA-256 |
| --- | --- |
| Official Main99 unresolved S9383 snapshot | 5da90ce90be67a16bfea35f735dbacddc2923a492e98c1037f4152197ba1fba2 |
| 193-row AST/import/scope catalog | 48a3efb5aef1cedbc2d44a52de0795a0a7b09f1bcc097c3fba12409cea45d1f3 |
| Pinned Node six-pair controlled results | d3d380195d09e35d8d72211f439d9da2a27dba80364030ffa445ad0025797a05 |
| 25-file source/token guard | 340ade4f298935dd916ceb2b2150ef474867d35a993cd42db2142ae207555d05 |
| cohort-original.json | 7b41e2bd3c204023e609702169f1fca90836608fdc5e6f32284f41791063a153 |
| cohort-candidate.json | d9d2f00c7201743312d292e40e12522075a9c3368093ba8b2829bdf837acca9f |

No issue disposition has changed. Official PR and Main analysis must establish
whether these exact findings are resolved; no resolution is inferred from local
syntax or test success. Historical findings, including the prior 230-finding
baseline, remain independently tracked. Protected checks, independent review and
normal merge qualification remain required. This change grants no architecture,
intake, staging, Pilot or public-release approval. RELEASE_NO_GO remains.

## Separate callback-arity clarification

A subsequent, separate commit addresses the single source-bound S7727 finding
AaGDs85EG0KyzUI2IEPf in webkit-pinned-provisioning.node-test.mjs.
The command-log reader now uses .map((line) => JSON.parse(line)) instead of
.map(JSON.parse). Its inputs are primitive string lines from split/filter, and
the builtin parser has no local shadow or replacement in this file.

[ECMAScript JSON.parse](https://tc39.es/ecma262/#sec-json.parse) applies a reviver
only when that argument is callable. The array index previously supplied as the
second argument and the omitted argument are both non-callable. The wrapper
therefore makes the intended callback arity explicit while preserving parsing,
returned values and malformed-input exceptions in this bounded string domain.
This is not a claim of a previously observed runtime failure or equivalence for
arbitrary callbacks.

The original 193-prefix proof and 284-before/284-after results remain bound to
commit 3ed21d0956259d14dd341535abcda7ff085301f4. A separate byte guard permits
exactly this one additional replacement, reconstructs that commit's file
exactly on reversal and verifies the other 24 test files are unchanged.
Its SHA-256 is
8e34d0d6c0d157b3e1029db1a308fd172e774949fa20957b64a1495ee583ea9b.

The existing six WebKit-provisioning controls pass under the identical pinned
Linux Node24.22.2 image: six passed, zero failed/cancelled/skipped/TODO, exit0.
Their names and order match the corresponding frozen 284-test subset exactly.
The focused result SHA-256 is
b189ad61ed75259bb4348dd685169247e04ea23dd8b8d28d68fa3ae811ad96c7.
These controls use fake Docker commands and establish provisioning behavior;
they do not qualify an actual WebKit browser, Product backend or deployment.
No test case, assertion, timeout, retry, skip or catch behavior was changed.
Official Sonar resolution remains to be observed after publication and analysis.

## Integration-only roster registration accounting

The normal-history Pilot integration combines the original 193-call ownership
component with four registration call sites added independently by the roster
finalizer component: two module expression statements and two module-level
for-of bodies (ten existing runtime cases). A TypeScript 6.0.3 checker confirms
all four use the unchanged default `node:test` import, with no nested context,
return-value use or alternate binding. The integration adds exactly four
`void ` prefixes; reversing those four spans reconstructs the pre-integration
file byte-for-byte. Original component #1582 remains a 193-call change; the
integration separately accounts for 197 explicit owned registrations.
No callback, assertion, timeout, retry, catch or test case was changed. Existing
roster controls passed 30/30 before and 30/30 after on pinned Windows Node
24.22.2, with zero skips; these fake-Docker controls are not an actual roster,
PostgreSQL, Mobile or Pilot acceptance. Hosted exact-source checks, official
Sonar disposition and resulting Main qualification remain separate.
