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
The branch adds a credential-free push workflow invoking the immutable existing
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
