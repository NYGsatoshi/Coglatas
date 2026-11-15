# Obsolete PR CI queue-block observation (P0 #1974)

This Security-owned script is **read-only**. It does not call GitHub
cancel/force-cancel/rerun endpoints, modify a workflow, approve a PR, waive
required checks, change branch protection, or authorize any later side effect.
A positive observation is **not** permission for an agent to stop a job.

## Reproduced historical sequence

The original ProjectIDE [PR #1972](https://github.com/NYGsatoshi/Coglatas/pull/1972)
ran `PR CI 43670058840` at the obsolete source
`7a0bbeba609a7a2428b2cf5ae2a3a307104cfa97`. Its ReSharper
job failed for a real source finding, while its `frontend-test` remained
active under `if: always()`. A corrected source
`573795592ee06e811cfef8d9130e66a720908d6b` triggered
`PR CI 43671221166`, initially pending without jobs. The Owner
specifically verified current PR/source/run identities and applied the
documented force-cancel endpoint *only* to that historical obsolete run;
it became `CANCELLED`, allowing the corrected run to start. Both
historical runs are **now completed/cancelled**; neither should ever be
declared a passing PR CI. The current PR #1972 has subsequently merged.

The GitHub Actions REST response for those runs has
`pull_requests: []`. A safe observer must not assume this array is
nonempty, nor infer the originating PR merely from a matching branch
name. The script requires **both** immutable GitHub
`commits/{obsoleteSha}/pulls` and `commits/{currentSha}/pulls`
associations to be unique and point to the same current PR and HEAD;
multiple PR associations are ambiguous and fail closed. It also requires
source/repository/branch pins, explicit run IDs and attempts, then re-reads
both runs and the PR.

## Intended checks

The policy requires all of the following in its *single observed
snapshot*: exact open current PR/branch/base/head, same repository and
real source PR association, exact workflow ID/path/event, immutable
old and current source SHAs, distinct explicit run IDs and attempts,
an in-progress obsolete PR CI run with exactly one still-active
`frontend-test`, a newer successor run that is actually pending/queued
and has no dispatched jobs, complete bounded original job and
association listings, and consistent fresh PR/run re-reads.

Any missing/malformed/paginated response; changed HEAD/base, wrong run
attempt, wrong PR/repository/workflow, already-cancelled/complete old
run, already running successor, or old run whose frontend test ended
returns a fixed `HOLD` reason without raw source, logs or secrets.
No observation can represent a cancelled run as SUCCESS or qualify
a required check. A failed old ReSharper job remains a separate
historical fact.

`projectide_obsolete_run_evidence.py` can be called manually with
a previously authorized, read-only GitHub CLI session and the explicit
`--repository`, `--pr`, `--obsolete-run`, `--successor-run`,
`--obsolete-head`, `--current-head`, `--base`,
`--obsolete-attempt`, and `--successor-attempt` inputs.
It uses only `gh api --method GET`, never a shell or a mutation. The
output is a small fixed-code JSON record with
`observationOnly=true`, `cancellationAuthorized=false`,
`requiredCheckQualified=false` and release flags `false`.
The historical case now returns HOLD because the PR is merged and
runs are completed. No force cancellation is tested or invoked by
this source.

## Evidence, admission and unfinished ownership

The additive nonrequired advisory on `SecurityTestImplimation` uses
literal Ubuntu 24.04, SHA-pinned actions, a read-only token and pinned
Test/Policy Git blobs. Its 21 independently authored stdlib unittest
controls cover the one positive observation and refusal for changed
PR, current HEAD, other PR/repository, workflow, run attempt, missing
commit association, missing/different old jobs, cancellation,
pagination, clock ordering, successor dispatch, TOCTOU rechecks,
and read-only API URL inventory. The advisory stores the original
unittest output and a sanitized receipt, not a fake live cancellation.

Even perfect synthetic results are **not** live queue-release evidence,
a new required check, or permission to invoke force-cancel. Actual
lifecycle cleanup, credential/resource teardown, bounded diagnostic
artifacts and real subsequent corrected-head CI must be separately
validated by the Security/CI Owner. Any future mutation must perform a
fresh atomic-enough expected-HEAD/PR/run recheck and obtain actual
authorization at the action boundary. GitHub's cancellation API is
not transactionally coupled to a PR HEAD, so the remaining race
cannot be eliminated by this read-only proposal.

Related P0: #1954 (ReSharper premerge source check), #1959 (trigger
coverage), #1927 (Security Owner governance), #1974. Keep #1144
BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Release NO_GO.
No Main, rule, mandatory CI, RLS, Trust, Native or Production update.
