# PR-I: existing Qodana controls and scanner responsibility audit

Status: IMPLEMENTED / LOCALLY_VALIDATED for this bounded coverage slice.
Branch-native qualification, trusted Sonar import, independent review and
integration remain incomplete. SECURITY_BRANCH_INTEGRATION = HOLD.
Main modification is prohibited. This does not complete the CI assurance program.

## Verified gap and change

At Security source `3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`, five existing
Qodana parity/provenance/signature/Cloud-layout suites execute in the static
analysis workflow but are absent from the Sonar Python producer. Their five
operational subjects are absent from its explicit coverage include list. This
is distinct from the reconciliation/packet-verification subjects in the existing
quality-evidence coverage candidate, and the newer generic Python/Node coverage
candidates. No held candidate was merged or copied wholesale.

The existing single producer now executes these five unchanged suites and
collects their five operational modules. All 37 original include entries remain;
the total becomes 42. The existing exact-set configuration assertion expands by
those same five paths and retains its equality operator. Branch/parallel pytrace,
subprocess startup, checksum-pinned coverage 7.17.0, single combine/XML import,
test-body exclusions, original native reports and all scanner guards remain.
No product code or Qodana test assertion changes.

| Unchanged suite | Native controls | Operational subject |
| --- | ---: | --- |
| `test_qodana_sarif_parity.py` | 16 | `qodana_sarif_parity.py` |
| `test_qodana_artifact_provenance.py` | 12 | `verify_qodana_artifact_provenance.py` |
| `test_qodana_cloud_send_preflight.py` | 11 | `qodana_cloud_send_preflight.py` |
| `test_qodana_shadow_signature.py` | 20 | `verify_qodana_shadow_signature.py` |
| `test_qodana_signed_subject.py` | 16 | `verify_qodana_signed_subject.py` |

[The native receipt](ci-assurance-pr-i-local-coverage-20261106.json) binds original
Linux reports, data shards, logs and source bytes. All 75 controls passed with
no skips. The original XML has exactly the five operational subjects, 643 source
line rows, 514 positive rows and 129 zero-hit rows; no test bodies enter it.
Missing branches and zero-hit lines remain in the original native outputs.
These local counts are not an additive project coverage prediction or a Sonar
Quality Gate result. The full prior producer cohort has not been reexecuted
locally; hosted qualification must retain all prior subjects and tests.

The first preparation selected a local image without pip and executed no tests.
That failure log remains. The existing ten-test process-coverage regression
suite subsequently hit its unchanged 15-second child deadline with source and
collector on Windows bind mounts, and again with only the collector still on a
bind mount. With identical source bytes and the same collector copied to the
container filesystem, all ten tests passed under the original deadlines. Source
archive hashes and all three outcomes remain in the local evidence archive.
This supports an I/O-placement association; it is not a flaky-test designation
or permission to discard the earlier failures.

Sonar routing/source-scope controls passed 28/28 in the existing Linux Git/jq
image with a local Git index populated from the exact source archive. Earlier
Windows execution could not use the configured WSL shell; an incomplete Linux
image lacked jq and could not resolve the Windows worktree Git pointer. Those
preparation/harness limitations are retained separately. Static publication and
required-check validators pass; no routing guard, deadline or assertion was
relaxed to obtain those results.

## Coverage producer audit and remaining limits

The current Sonar workflow produces .NET OpenCover/TRX plus a source receipt,
Angular LCOV from `ng test --watch=false --coverage`, native Node LCOV/raw V8,
the separate Pilot helper LCOV, operational raw V8/c8 with zero controls, and
Python branch coverage XML. Existing .NET filtering rejects missing production
sources and excludes test/generated subjects; existing Node controls validate
source identity and preserve disjoint report cohorts. These configured guards
are reused, not treated as current execution proof.

The bounded Python gap above is corrected. Complete .NET module/cross-process
instrumentation, all TypeScript/JavaScript source-map ranges, Python operational
subject completeness, duplicate report reconciliation and source/run provenance
for every imported artifact remain incomplete audits. Operational scripts outside
the explicit Python/Node cohorts must not be called covered merely because they
have tests. Inactive `coglatas-frontend` and generated hosted assets remain under
their preexisting Sonar scope; this change does not alter that scope.

Token-backed Sonar target routing still accepts Main or an eligible exact-source
same-repository PR whose base is Main. This Security-targeted candidate therefore
does not receive official import credit from its branch push. Expanding that
trust/activation contract is not part of this slice. No Main-targeted PR was
created as a workaround. The quality gate, New Code definition, exclusions,
thresholds, credentials and Action permissions are unchanged.

## Qodana consolidation decision

Existing strict SARIF parity, artifact provenance, signed-subject and shadow
signature verifiers are reused. Historical #1500 evidence observes three pairs
equal outside the one validated automation GUID; it does not prove current
analysis-to-publication equivalence. The Cloud-send preflight explicitly never
uploads, records no approved CLI digest, and leaves Cloud replay and gate reuse
false. Signed-subject verification also retains its explicit Main-producer/Cloud
qualification limits.

Both original Community and Cloud analyzer paths remain. Before removal of either,
independently verify exact source/run/attempt, GitHub artifact ID/digest, original
SARIF signature, findings/rules/artifacts, project-model and rule-baseline verdicts,
GitHub SARIF category, and real trusted same-report Cloud ingestion. Prove missing
credentials and publication failure fail the publisher separately from analysis.
Do not add execution to the non-executing Cloud preflight without its separately
approved security contract. Local mocked controls are not such approval.

## Dependency and supply-chain responsibility

[The source-bound inventory](ci-assurance-pr-i-scanner-inventory-20261106.json)
records all twelve requested tools, their actual configured responsibilities,
canonical Git-blob digests, component manifests/locks, image build definitions,
installer scripts and explicit graph/database/binary/artifact limits. It is a
configuration audit pending independent review, not an execution-completeness
attestation. Native workflow success alone does not establish advisory database
or resolved transitive-graph completeness.

Notable remaining boundaries are manual NuGet graph submission, variable-gated
Socket SCA, supported-manifest discovery limits, accepted npm debt, external
binary identities, generated artifacts, and current image/database provenance.
The Socket SCA workflow's title does not prove Main-only manual dispatch; its
visible job condition checks an opt-in variable and actual protected-environment
branch restrictions were not retrieved. No environment or credential was changed.

Renovate is the sole configured version-update bot. The old Dependabot-only
fast-lane document now clearly distinguishes current Renovate policy from its
preserved historical schedule, rationale and narrow Sonar exception. The Action
pinning guide now reflects the current held protected-Action rule. Dependency
alerts and graph submission remain separate services. No second bot is added.

No new scanner is admitted: unique extra detection, license, maintenance, cost
and trust justification are not established. Existing externally reported checks
also do not imply a new repository-owned scanner was introduced by this slice.
No responsibility, finding, baseline, original analyzer or failure path is removed.

## Remaining admission

The governance owner packet and current checkpoint evidence are retained in
[PR #1747](https://github.com/NYGsatoshi/Coglatas/pull/1747). Registered/live
identity drift, unprotected Security scope and unavailable independent review
remain blocking. The missing D01–D15 text and truncated PR-N requirements have
not been invented. Qualify this PR's exact final head through existing native
checks, separately establish trusted import, resolve review findings, and retain
the implementation unmerged while any applicable hold persists.
