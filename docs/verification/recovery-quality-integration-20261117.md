# Recovery and Qodana integration qualification — 2026-11-17 UTC

Status: **REVIEW CANDIDATE / no Main merge / production release NO_GO**.

## Source and existing-work reconciliation

One independent branch, `integration/recovery-qodana-completion-20261117`, starts at
Main `ffd069e53df4f91b01f9cdc2df48fc347a77a171`. The normal Main-targeted PR is
[#2054](https://github.com/NYGsatoshi/Coglatas/pull/2054). The original checkout and
all existing PR branches are preserved. No merge commits or stacked dependency
are introduced. PR checks execute the PR merge ref where the workflow specifies
it; full Qodana checks the pushed branch HEAD. Neither qualifies a future Main SHA.

Current GitHub/source reconciliation:

| PR | Current disposition | Integration handling |
| --- | --- | --- |
| #1379 | Merged at `8202ce1cca0913039adbf9dc9477172256a101c4` | Preserve existing synthetic controls |
| #1397 | Merged | Preserve existing Thread double-GET/catch-up regression |
| #2017 | Merged at `492245200f737b28c912483228c33e8793688d6d` | Preserve historical integration audit |
| #2022 | Merged at `4499aa2fe554201dcc63a32148b0e2ea9f475d3a` | Do not replay async disposal/UTF-8 fixes |
| #2041 | Merged at `4522a153d06f008c9e839677598e92c02ccd88ea` | Preserve all three shadow-property assertions |
| #2048 | Merged at the starting Main SHA | Do not replay explicit defaults/cancellation fixes |
| #2018 | Open, HEAD `88c2629a27c5e141a2ff873612a5f48a34117be6` | Reuse only its seven-file Main-relative patch |
| #2047 | Open, HEAD `e57fe63b4acf4b68c874d9cf0950c72505e6c6e5` | Reuse its nine-file Lane B patch |
| #2049 | Open, HEAD `40651bface84e76bf98d202f5ae908da9770a87c` | Reuse its six-file bounded patch |

Open PR histories are not merged wholesale: their Main synchronization commits
are not replayed. Their older executions remain attributed to their own SHAs.
The other source branches remain open for owner reconciliation; integration here
does not authorize closing or merging those PRs. Issue #947 is already closed;
#862/#895/#935 remain policy/inventory trackers, and #1355 remains open.

## Current full Main Qodana inventory

Authoritative inspected snapshot: Main `ffd069e53df4f91b01f9cdc2df48fc347a77a171`,
[Main CI 44043373783](https://github.com/NYGsatoshi/Coglatas/actions/runs/44043373783),
Community full inventory artifact `14311242477`, raw `qodana.sarif.json` SHA-256
`3ef94b8d6a34eaf2429d5956413669ef0f2203bd5eab309b8543d93d5c9472f6`.
The complete raw SARIF contains **2043 results**. This is a failing inventory,
not a successful overall Main CI or Cloud gate. The earlier completed Main
`4522a153d06f008c9e839677598e92c02ccd88ea`, run `44025551279`, Cloud artifact
`14303623244`, raw SHA-256
`7fc49ef7705f75fb54a57b81395b17d51f186b6594d64e9c33fd0e60c9305f3b`,
contained 2055 results. Do not substitute either snapshot for candidate results.

| Rule | Unchanged budget | Current Main observed | Current excess |
| --- | ---: | ---: | ---: |
| `MergeIntoPattern` | 132 | 151 | 19 |
| `MethodSupportsCancellation` | 0 | 0 | 0 |
| `PreferConcreteValueOverDefault` | 0 | 0 | 0 |
| `UseUtf8StringLiteral` | 0 | 0 | 0 |
| `xUnit2029` | 0 | 0 | 0 |
| `RedundantTypeDeclarationBody` | 0 | 2 | 2 |
| `RedundantVerbatimStringPrefix` | 0 | 0 | 0 |
| `UseAwaitUsing` | 2 | 0 | 0 |
| `ConvertIfStatementToConditionalTernaryExpression` | 0 | 0 | 0 |
| `MemberCanBePrivate.Local` | 0 | 1 | 1 |
| `MethodHasAsyncOverload` | 8 | 9 | 1 |
| `UnusedParameter.Global` | 21 | 22 | 1 |
| `UseWithExpressionToCopyRecord` | 0 | 1 | 1 |

The six currently over-budget rules are covered by reused #2047/#2049 source
changes. Their candidate observed counts remain **UNMEASURED** until the new
complete inventory is inspected. Zero excess does not mean zero retained debt.
The branch adds a credential-free push workflow invoking the existing source-owned
trusted Qodana reusable workflow on integration branches. Push context selects
its original full scan and original full ratchet, rather than PR changed-file
mode. Protected Cloud publication remains Main-only; no token, baseline,
severity, suppression, skip or success-criterion change is introduced.

### Slice and contract audit

Under the standing #862 decisions, #2047 is Lane B. The changed predicates inspect
already constructed test DTOs/records or value tuples with pure auto-properties;
expected values, conjunctions and assertions remain. URI matching stays after
the original successful `TryCreate` guard. No expression tree or provider SQL
predicate is rewritten. Native PostgreSQL/RLS and real transport evidence is
required for the affected fixtures, rather than an unconfigured local test PASS.

#2049's nested Graph property, record copy and ZIP reads are Lane B. The private
Graph owns all Workspace reads. `ChannelScope` is an unexposed three-Guid record
with no custom clone/additional fields. ZIP reads retain read access, awaited
lifetime, JSON parsing and redaction assertions on net11.0. The public
`IFileFilterReviewHost` marker receives a D audit, then a bounded C implementation:
only `{ }` becomes `;`, preserving its name, visibility, reflection/DI identity
and absence of production registration. No member or external shape is removed.
The internal synthetic tool's ignored diagnostic parameters receive a D scope
audit then C cleanup: this standalone internal helper has only its Program.cs
calls, never emitted the ignored strings, and still throws the exact closed
`SYNTHETIC_SOCKET_CONTROL_REJECTED` message. No socket observation duration,
negative assertion, receipt, authorization or privacy behavior is changed.

## Recovery mechanisms and execution boundaries

- Logical retains `MAIN_PORTABLE_LOGICAL_V1`, one custom `pg_dump`, one planned
  `pg_restore`, every original TOC entry/FK slot, six byte-exact non-catalog
  families, exact portable catalog, and the digest-bound raw catalog difference.
  Portable success is never recorded as original raw catalog equality.
- Physical and continuation now separately select Main pushes on their original
  PR source paths. A Main push requires GitHub-hosted Linux, exactly
  `NYGsatoshi/Coglatas`, `refs/heads/main`, a valid SHA matching actual checkout,
  and empty PR metadata. Continuation reuses logical admission and records
  `sourceHeadSha=GITHUB_SHA`, `baseSha=null`. Physical records its actual source SHA.
- Private original-DDL/evidence paths, owned immutable containers, disjoint
  volumes, exact image/build bindings, nonempty admitted IDs, verified cleanup,
  closed failure receipts and original failed verdicts remain mandatory.
  Physical/continuation retain all seven strict comparison families.
- Added tests exercise unsupported events, repositories, refs, self-hosted and
  non-Linux runners, malformed/forged SHAs and PR metadata before preparation or
  Docker. Separate checkout mismatch controls require rejection before Docker.
  Existing path, cleanup, corruption, portable-catalog and TOC negatives remain.
- All three Main-push mechanisms are configured candidates, **not executed Main
  qualification**. After a separately approved integration, bind each actual
  push run/attempt/checkout/source/receipt to that exact Main SHA and verify every
  mandatory control, cleanup, privacy field and initial outcome. No PR result or
  historical Main `8202ce1c`/`49224520` result transfers that credit.

The original #2018 logical proposal below the current section in the physical
candidate document is historical; its old PR-only physical/continuation statement
is superseded only by this configured integration candidate.

## Verification ledger

This is an implementation-time checkpoint, not a live final-status assertion.
The final current-HEAD receipt is maintained in #2054 without embedding a
self-referential commit SHA into this source document.

Local recovery contract cohort: **86 tests PASS**, no skips. Portable catalog,
trigger and restore-list Node cohort: **23 tests PASS**, no skips. These are
contract/oracle tests, not native PostgreSQL recovery executions.
An initial local Sonar process-coverage test invocation omitted the required
`SONAR_PYTHON_SOURCE_ROOT` environment and failed one configuration test; that
failed invocation is retained privately. The correctly configured rerun and
hosted provider result must be recorded separately.

At earlier integration HEAD `81bbbda7fd22901a332f0d9b475b7fb8294af480`, actual
PR native logical run `44045983233`, physical `44045983195` and continuation
`44045983421` completed successfully. They qualify that source only; later
Main-routing changes require fresh exact-head runs. Their initial successes
are not used as the final branch qualification.

The initial `91e6c6ec` publication run `44047083309` failed action-pin policy
because a same-repository workflow was expressed as an external action outside
the allowlist. The caller now uses the existing local reusable-workflow form;
the allowlist, reusable workflow and its gates remain unchanged. The obsolete
`b08f416c` full scan was canceled only after a newer source superseded it, so the
current source could qualify without running two serialized obsolete scans.
Those outcomes remain canceled/failed, never PASS.

The final exact-HEAD checklist and provider links are maintained in #2054:
required build-test/frontend-test/security-scan/publication-readiness/
functional-fast/performance-fast, real configured PostgreSQL tests, CodeQL,
Sonar analysis **and provider Quality Gate**, full Qodana inventory and unchanged
ratchet, actionlint/zizmor, Dependency Review/OSV/Trivy, ReSharper, native
logical/physical/continuation, structural performance, independent Codex and
CodeRabbit review, and a final re-read of all review threads. Correct policy
skips remain skips. A required missing/skipped check is not PASS.

CodeRabbit CLI 0.9.5 installed in task work storage, but authentication is absent.
`auth login --agent` returned `environment_unsupported`: browser login is
unavailable in this CI/Docker environment. No CLI review ran. A requested GitHub
bot review is a separate possible route; rate limiting or Draft skipping must
not be counted as successful review. GitHub branch-protection and code-scanning
alert APIs returned HTTP 403 (`Resource not accessible by integration`), so their
current protected configuration and formal scanner dispositions remain unverified.
No protection, alert disposition or project permissions were changed.

## Production and release holds

All existing Owner HOLDs and release NO_GO remain. Synthetic PostgreSQL bootstrap
roles do not establish production roles/permissions, all-table RLS or least
privilege. Synthetic auth/CSRF/tenant/Audit/File/key/continuation controls cover
bounded fixtures, not Full Application Restoration or an approved target.

Still require approved target/strategy scope, full application and migration
restoration, authentication/authorization/Audit/File completeness, Data Protection
custody, actual PostgreSQL roles/RLS/tenant isolation, protected HTTPS, off-host
backup retention/custody, real rollback and approved RPO/RTO. No production
credential, operational backup, real user data, target execution, Enforce
activation, backup-strategy adoption, HOLD release or Main merge is performed.
The next permissible step is to review the concrete branch and its actual checks;
Main integration and target operations remain separate decisions.

## Retained official Sonar failure and bounded repair

The earlier candidate `8859ee020ff13a4205fd6475b736e628d8148de2` passed all
six required checks, full Community Qodana (2015 findings, zero regressions),
and native logical/physical/continuation qualification. Its Sonar analyzer run
`44048050836` succeeded, but the separate official `SonarCloud Code Analysis`
check `133936881080` **failed**: new-code coverage was **78.8%**, below the
unchanged **80%** requirement. Analyzer success is not Quality Gate success.
The check's annotations also identified a duplicated SHA regex, excessive
path-admission complexity and three nested context-manager suggestions.

The follow-up keeps the exact SHA regex in one logical-runner constant,
extracts the existing Main-event predicate from private-path admission, and
combines adjacent test context managers without changing their evaluation or
cleanup order. The checkout control now exercises malformed and mismatched
identities plus a genuine matching identity. Only the matching identity may
reach the first mocked Docker observation; that deliberate stop must still
publish FAIL with incomplete cleanup, never synthetic recovery PASS. No real
Docker operation is executed by this unit control. Native positive admission
and complete recovery remain separate exact-source workflow requirements.

The original failed check and its percentage remain failed evidence. No
coverage threshold, exclusion, source classification, security rule, baseline
or success condition is relaxed. Source changes require new current-HEAD
native, quality, security and review evidence; none of the earlier candidate's
successful checks is inherited by the amended SHA.

## Continuation acceptance-tool admission follow-up

CodeRabbit review `5936171841` of candidate
`b069300231ac855b9c7d2076848d3039fe211460` identified a valid remaining
Main-route defect: the Python caller admitted qualified Main pushes, but the
compiled acceptance tool still accepted only `pull_request`. Successful PR
continuation receipts do not exercise or qualify that Main-only path.

The tool now applies an independently tested admission predicate before any
private configuration path or file is read. Existing hosted-Linux PR behavior
is preserved. Pushes additionally require the exact repository
`NYGsatoshi/Coglatas`, `refs/heads/main`, an exact 40-character lowercase
hexadecimal SHA, and absent or empty PR head/base metadata. Other events,
repositories, refs, malformed identities, PR metadata, self-hosted runners and
other operating systems fail closed. The caller's actual checkout-SHA check,
private-path ownership, closed configuration, assembly identities, all recovery
controls and receipt privacy remain unchanged.

The pure predicate is tested against real compiled C# code through a test-only
friend assembly and project reference, without changing process environment or
starting Docker, PostgreSQL or HTTP. The dependency lock update records only
this existing local tool project; no package version changes. The Python
contract also requires the actual tool entry to call the predicate before
configuration I/O. This is Lane C operational-tool behavior with a D-to-C
contract audit (no product API, DI, serialization or persistence change), plus
Lane B negative/positive tests. A new source epoch must repeat native recovery,
full quality gates and independent reviews. Future exact-Main push execution
and all production/strategy/release holds remain unexecuted and unchanged.

Local locked Solution restore and the compiled helper cohort passed (29/29,
zero skips); the Recovery Python cohort passed 89/89. The initial helper run
retains its 28 PASS / 1 FAIL outcome: its supposed valid SHA fixture contained
42 characters. Correcting that fixture to 40 characters, rather than relaxing
the admission predicate, produced the successful rerun. The negative fixtures
separately retain 39/41-character, uppercase, non-hexadecimal and newline cases.

## Native Windows provenance follow-up

At `8d02c8dee4e1421a60994b383f49d8854ffa6af3`, native recovery passed again,
but Sonar run `44061070670`, Windows job `133963023410`, failed after all
188 portable tests passed with zero skips. Original coverage artifact
`14317908296` contained the newly measured acceptance tool's actual compiler
document under `/_/tools/synthetic-recovery-acceptance/obj/Release/net11.0/`
and the existing OpenAPI XML-comment generator identity. The provenance
verifier rejected this known project path as nondeterministic. The dependent
Linux analyzer was skipped; neither stage is recorded as Quality Gate PASS.

The follow-up recognizes that exact tool project root under the existing
compiler-document policy. Generated documents retain their original bytes,
ranges and zero hits and receive no mapped Git-source coverage credit. Unknown
tool roots, handwritten substitutes and traversal still fail closed. The newly
executed acceptance-tool DLL and PDB are also added to the mandatory
source-stamped, project-output/effective-test-output identity manifest: eight
assemblies / sixteen artifacts, with missing or differing tool binaries rejected.
No report is rewritten and no source is excluded or reclassified.

Reproduction against the untouched failed-run Windows report now verifies
630 mapped Git source files, four uncredited generated documents, 319784 native
line entries (318721 zero-hit / 1063 covered), and the original 24262/610
total/covered conditions. Original report SHA-256 is
`2285000d39007509c65e805bdaa8d810c857ea6791b79eb9a11d9327e5328b30`.
All 17 provenance mutation tests pass, including the new generated-path and
mandatory tool-binary controls. These local parser results are not replacement
Windows producer evidence: the amended source must repeat native capture,
independent Linux consumption, scanner analysis and the official Gate.

## Composed-host canonical dependency follow-up

Candidate `ac0f6367644e82f0e372b3659b00c7e1a21fdd57` passed full Qodana
(2015 findings, zero regressions), native recovery and Windows provenance, but
PR CI `44062599594`, build-test job `133974674508`, failed: 4542/4552 backend
tests passed and ten composed-host RLS tests failed, with zero skips. The twelve
Architecture tests passed. Sonar run `44062590636`, Linux job `133971313438`,
passed 170 Angular test files, then encountered the same ten backend failures;
no official Quality Gate PASS is credited. CodeRabbit's completed advisory
review with no actionable comments does not override these actual failures.

The new test-project reference placed the recovery acceptance-tool DLL beside
the test assembly. The composed-host fixture correctly rejected it before child
startup because it was absent from the canonical dependency bindings. The repair
captures its actual loaded assembly and exact producer path through the existing
bounded PE/MVID, digest and collector-original-backup checks. Its copied child
module must be the canonical producer bytes, with the loaded copy independently
bound. Unknown product modules still hit the original rejection assertion.
This copy diagnostic already includes test-only UI/tool dependencies and does
not change or grant authority to the separate formal six-assembly runtime
reconciliation scope. No unknown-module exemption or security assertion is added.

Five new cross-platform copier controls exercise canonical versus instrumented
copy selection, rejection before copying an unknown module, producer/test-copy
tampering, and preservation/rejection of an existing incorrect destination.
An isolated, network-free PostgreSQL 18.6 fixture reproduced the original ten
failures (2 PASS / 10 FAIL / 0 SKIP). After the repair, the same twelve tests plus
the five copier controls and 29 admission controls passed: **46/46, zero skips**.
The Solution rebuild passed with zero errors and the two existing NU1510 warnings.

Initial local fixture setup attempts failed before tests: the custom Socket
conflicted with the image's initialization client, then a redundant EF CLI
pre-step lacked a restored local tool. The final fixture uses the image's
standard private Socket mount and the existing tests' real IMigrator template
creation. Test/migration implementation and assertions are unchanged. Only
nonce-labeled immutable owned containers and tmpfs data were used; stopped
fixtures were removed and absence verified. No production data, endpoint or
external data volume was used. The initial failures remain failed records.

This source repair requires another exact-HEAD CI, full inventory, native
recovery, formal Gate and independent review cycle; earlier successful results
are not inherited. Main integration and all production/strategy/release holds
remain separate and unchanged.

## Closed observation catalogue integration follow-up

At candidate `9db46bde102d16410bcb295d8c7fbb7e7d9a148a`, PR CI
`44069805917`, build-test `133994217186`, successfully executed all
**4557/4557 backend tests and 12/12 Architecture tests**, with zero skips. The
subsequent SEC-ARCH observation capture failed closed because the five new
copier cases were absent from its explicit catalogue. Test success did not make
the required build-test job successful; no green qualification is inferred.

The repair explicitly registers those four methods / five cases, including both
producer/test-copy tamper variants. Required observations increase from 353 to
358; every existing case remains mandatory. Unknown cases still raise an error;
missing, skipped, failed, aborted and duplicate new cases cannot produce complete
PASS. This is advisory observation-catalogue maintenance for real new regression
tests, with no threshold reduction, ignored test, runtime-acceptance promotion,
formal six-assembly scope change or Owner/attestation/HOLD change.

Reproduction uses the untouched original backend TRX from artifact
`14322493839`, SHA-256
`e83ab3220b8bf48003f119563e3a8e98ea71280ce937c5d24bf3bac4fd26cc17`.
The previous parser raises `Unclassified or duplicate SEC-ARCH test case.`;
the amended catalogue observes exactly 358/358 required cases with no missing
methods. All 129 catalogue/evidence controls pass, including new unknown-case,
missing/skipped/failed/aborted and duplicate-name mutations. This local replay
does not substitute for a new clean-checkout/build-stamped CI capture: the old
failed job retains its original outcome and the amended SHA must run all gates.

The 25 producer/reconciliation controls and 19 Python process-coverage controls
also pass. The first reconciliation run retained three environment errors because
Ruby was absent; a signed-distribution Ruby runtime was extracted only into the
private task work directory, then all 25 tests passed. No system setting,
repository dependency, test condition or skip was changed to address that setup.

The separate official Sonar check `134011381543` for the same prior candidate
subsequently passed at **80.7% new-code coverage**, above the unchanged 80%
requirement, with zero new hotspots and zero duplication. Its single annotation
requested one potentially throwing call in the generated-document rejection
test. The follow-up constructs the mutated input before the exception assertion,
so only the actual verifier can satisfy it. All seventeen Windows provenance
controls still pass. The failed build-test observation capture is not overwritten
by that Sonar success; the amended source requires a fresh complete CI/Gate cycle.

## Admission negative-control review follow-up

CodeRabbit review `5937870888` of candidate
`afb41dbd3e916049df7573317b1b5c7320248a47` found one valid test gap: physical
admission negatives omitted run ID/attempt, so a broken admission guard could
still appear rejected at the later metadata check. The test now provides valid
run metadata and independently requires the admission function itself to raise
for each invalid context. Existing main-entry workspace/subprocess ordering
assertions remain intact. All 89 Recovery Python controls pass. A private
mutation check replaces only the admission function with a no-op: all eleven
invalid-context subtests then fail as expected, with no real Docker operation;
the original guard is restored after that controlled patch context. This is a
test-strengthening change, not a production-admission or acceptance-rule change.
The reviewed candidate's results remain bound to its own SHA; the final source
must repeat all checks and independent review before readiness.
