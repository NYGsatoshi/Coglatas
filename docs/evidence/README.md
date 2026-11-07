# Evidence index

This index groups retained evidence by the work it records. For task-oriented
documentation, use the [documentation index](../README.md). For verification
policies, reusable instructions, and domain records, use the
[verification index](../verification/README.md).

Read each record with its stated date, source commit, environment, and scope.
A baseline, successful command, or structural check does not by itself grant
release approval. Preserve the original result and any stated verification
limits when comparing it with later evidence.

## MVP-A baseline and failure records

Read each baseline together with its failure log. The pairs preserve the
commands, observed results, fixes made during that pass, and remaining
limitations of the recorded environment.

| Record | Scope | Baseline | Failure log |
| --- | --- | --- | --- |
| A-01 | Build, tests, and environment | [Build/test baseline](mvp-a/a-01-build-test-baseline.md) | [Baseline failures](mvp-a/a-01-baseline-failure-log.md) |
| A-02 | Health and readiness | [Health baseline](mvp-a/a-02-health-check-baseline.md) | [Health failures](mvp-a/a-02-health-check-failure-log.md) |
| A-03 | Application smoke and runtime dependencies | [Smoke baseline](mvp-a/a-03-application-smoke-baseline.md) | [Smoke failures](mvp-a/a-03-smoke-failure-log.md) |
| A-04 | Authentication, authorization, and private-data boundaries | [AuthZ baseline](mvp-a/a-04-authz-boundary-baseline.md) | [AuthZ failures](mvp-a/a-04-authz-boundary-failure-log.md) |
| A-05 | Sensitive data, secrets, and error disclosure | [Sensitive-data baseline](mvp-a/a-05-sensitive-data-boundary-baseline.md) | [Sensitive-data failures](mvp-a/a-05-sensitive-data-boundary-failure-log.md) |
| A-07 | Files, attachments, downloads, and grants | [File baseline](mvp-a/a-07-file-boundary-baseline.md) | [File failures](mvp-a/a-07-file-boundary-failure-log.md) |
| A-08 | Conversations, messages, and participant boundaries | [Communication baseline](mvp-a/a-08-communication-boundary-baseline.md) | [Communication failures](mvp-a/a-08-communication-boundary-failure-log.md) |
| A-09 | Audit queries, events, and security logging | [Audit/security baseline](mvp-a/a-09-audit-security-baseline.md) | [Audit/security failures](mvp-a/a-09-audit-security-failure-log.md) |

These records retain their original stage identifiers. The related
[MVP-A evidence ledger](../verification/mvp-a/mvp-a-evidence-log.md) records
additional milestone context.

## Frontend verification logs

The [2026-07-03 frontend log bundle](mvp-a/frontend-final-2026-07-03/)
contains 30 retained log files. Read it with the
[frontend verification report](../frontend/p0-angular-final-verification.md),
which identifies the commands and their recorded interpretation.

The bundle keeps original attempts, reruns, build/test output, and screenshot
baseline work together. Preserve their filenames and chronology. A later rerun
does not erase an earlier failure or establish a result for a different source
commit or environment.

## Syncfusion activation evidence

The [Frontend v0.4 PR02 activation record](frontend-v0.4-pr02-syncfusion-license-activation.md)
distinguishes local negative controls and source inspection from real-secret
activation and final-image verification. Read its specification-status section
with the recorded execution limits.

Use the [license runbook](../SYNCFUSION_LICENSE_RUNBOOK.md) for operational
instructions. Do not infer successful licensed execution from a standard build,
a placeholder value, or the existence of a workflow.

## Other evidence collections

- [Verification records](../verification/README.md) include domain checks,
  release snapshots, and their linked JSON or image assets.
- [Performance contracts and evidence](../../performance/README.md) retain
  their own approval and immutable-evidence rules.
- [Archived documentation](../archive/README.md) provides historical plans
  and explanations, with links to current guidance.

## Preserve evidence identity

Keep baseline/failure pairs and their related logs, JSON, screenshots, and
archives connected. Retain the source SHA, run/attempt, artifact identity, and
original outcome where recorded; do not fill absent metadata with guesses.

Use the applicable generator or evidence-revision procedure for generated or
digest-bound records. Add explanations and navigation outside protected
original bytes. Preserve effective approval conditions separately from
historical execution results, and verify the governing decision before treating
a restriction as superseded.

