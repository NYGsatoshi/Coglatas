# ProjectIDE ReSharper premerge provenance gate (P0 #1954)

## Failure chronology, not a retroactive clearance

ProjectIDE Product [#1946](https://github.com/NYGsatoshi/Coglatas/pull/1946)
was Squash-Merged on 2026-11-14 at 12:49:42 UTC, when its separately
required [ReSharper job](https://github.com/NYGsatoshi/Coglatas/actions/runs/43593444836/job/132443496304)
was **still in progress**. The independent source-WARNING/SARIF scope
probe and final changed-file gate only completed by 12:54:24 UTC, followed
by report upload. Their later SUCCESS does not establish that a separate
applicable premerge prerequisite was complete before the owner clicked Merge.
The original run artifacts and post-merge success must be preserved.

This implementation supplies a **read-only technical qualification gate**,
not an automated repository merge, branch-protection change, or a new
authorized human reviewer. Its 13 synthetic refusal tests run in a separate
nonrequired Security-owned advisory. The gate is not active in the actual
Product merge controller until the authorized Owner adopts it.

## Source-bound live verification contract

`scripts/ci/projectide_resharper_integration_gate.py` accepts explicit
expected source **HEAD SHA**, base **SHA**, Product pull request number,
exact PR CI **run ID** and **attempt**. It reads real GitHub objects via
`gh api` using only current read permissions. It refuses incomplete
pagination, malformed/unavailable evidence, noncurrent heads, changed base
or target branch, closed or Draft PR, or a wrong check producer.

It requires the original **six** `app.id=15368` GitHub check contexts
(`build-test`, `frontend-test`, `security-scan`,
`publication-readiness`, `functional-fast` and
`performance-fast`) on the exact head to be terminal SUCCESS.

That alone is **insufficient**. The same immutable Product source must
have one selected `PR CI` pull_request run with the requested run ID,
attempt, source head/branch, successful completed result and an exact
`ReSharper PR fast lane / ReSharper InspectCode / PR` job. That job
must itself be terminal successful and prove successful, properly ordered,
completed **actual source** steps:

1. Test exact composition and SARIF false-green controls.
2. Run ReSharper InspectCode against shared build outputs.
3. Verify independent inspection scope and complete SARIF.
4. Gate changed files on ReSharper findings.
5. Upload ReSharper report.

A running, cancelled, missing or wrongly sourced inspection/probe/final
gate returns `HOLD` even when all six original Required Checks are green.
This is an evidence gate and does not weaken severity, test coverage,
source canaries, scanner configuration, source/credential authority or
time limits.

## Invocation — explicit and read-only

From an authorized CLI with `gh` authenticated to read the repository:

```text
python -B scripts/ci/projectide_resharper_integration_gate.py --repository NYGsatoshi/Coglatas --pr <number> --expected-head <40-hex-PR-head> --expected-base <40-hex-current-base> --run-id <current-PR-CI-run-ID> --attempt <exact-run-attempt>
```

A real successful query prints
`CI_PROVENANCE_QUALIFIED_NOT_OWNER_APPROVAL`; failure prints only a
fixed, privacy-safe `HOLD:<code>` and returns nonzero. All raw GitHub
JSON, arbitrary test names, paths, tokens, findings and exception details
remain nonpublished. Tests may supply a synthetic `--fixture` JSON file,
but its successful result is labeled
`SYNTHETIC_SOURCE_GATE_ONLY_NOT_MERGE_APPROVAL` and must **never**
qualify a real PR.

A single read-only query cannot create an atomic merge transaction.
After qualification, a distinct authorized integration controller must
still enforce **expected current head/base** at the actual Squash Merge,
check permissions, Owner rules and formal reviewer source approval and
recheck after any update. No user consent, review, rule or protected check
is waived by this source gate. Product #1946 is already merged; applying
this control to future PRs does not retroactively qualify its timing.

## Synthetic test coverage

The 13 source-owned `unittest` functions cover exact positive source,
six-green-while-inspection-running, cancelled/missing scope probe, final
gate incomplete, wrong head/base/target branch, wrong run attempt/source
branch, untrusted check app, duplicate check producer, incomplete check
or job pagination, wrong ReSharper source/job/run and failing job, wrong
step order or missing completion, cancelled/failed CI, Draft/closed/foreign
PR and malformed inputs. These are synthetic checks of the decision policy,
not a replayed real GitHub Merge or actual protected-branch enforcement.

**Remaining P0:** Owner/CI review of this design; independent actual
source and historical run/attempt provenance audit; permitted controller
integration or Owner-approved protected gating. No Merge of this Draft
or activation of real policy is assumed. Keep #1954 and #1927 open;
#1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, Release NO_GO.
