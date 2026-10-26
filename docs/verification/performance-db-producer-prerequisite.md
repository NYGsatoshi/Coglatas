# Exact-main DB producer prerequisite correction

## Source-bound first failure

Scheduled PostgreSQL structural performance [40407561459/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40407561459) targeted `d743341ec4d772da5eafed16b9f5d211f46e52f6`.
Routing [job 121869158199](https://github.com/NYGsatoshi/Coglatas/actions/runs/40407561459/job/121869158199) failed in producer resolution at 2026-10-26 21:49:40 UTC.
`REUSE_MAIN_ARTIFACTS=false`; an empty `PRODUCER_RESULT` is expected on this standalone path and was not the executed failure condition.
The first failure is preserved. No DB collection ran; the collector was skipped and the aggregate failed.

The exact source Main producer was [40407265791/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40407265791).
Its frontend producer completed at 21:49:58 UTC; [runtime assembler 121869382481](https://github.com/NYGsatoshi/Coglatas/actions/runs/40407265791/job/121869382481) ran 21:50:00–21:51:19 UTC.
Its same-source [DB query regression gate 121873068142](https://github.com/NYGsatoshi/Coglatas/actions/runs/40407265791/job/121873068142) passed at 21:58:19 UTC.
The overall old Main run ultimately concluded cancelled. Its DB result is a historical structural measurement, not a completed Main qualification.

The old resolver explicitly requested completed runs and filtered completion again.
It could not select an active exact-source producer. It also iterated candidates, allowing an older successful producer to substitute.
The historical API response was not retained, so the precise response contents are UNKNOWN.
The observed timestamps and source establish the sequencing defect without claiming a database performance regression.

## Bounded correction

Discovery includes active push/Main runs of the exact target SHA and exact trusted workflow.
The latest run ID and attempt are pinned before waiting. Attempt-specific jobs prevent a rerun from changing the inspected assembler.
Only that producer's active metadata is polled. Cancellation, replacement, disappearance, invalid state, failed assembler, incomplete API page, missing/expired/duplicate artifact, identity mismatch, API failure and budget expiry fail closed.
There is no older-run fallback and no missing-evidence PASS.

A successful assembler can supply its exact-source artifact while unrelated Main validation consumers still run, or after they fail; this preserves the existing producer/consumer distinction.
Artifact metadata binds ID, source run, source SHA, nonzero size and provider digest.
The resolver rechecks producer identity after artifact discovery. The standalone consumer downloads by the pinned immutable artifact ID, preventing a later same-run rerun from replacing a name lookup. The workflow-call path continues using its explicitly provided same-run producer contract.
The existing runtime restoration independently checks the actual artifact's source stamps before execution.
A metadata receipt is not a verification of downloaded archive bytes.

The monotonic prerequisite cap remains five minutes inside the unchanged five-minute routing job.
HTTP timeouts are clipped to the remaining cap. No measurement threshold, owner assertion, deadline, retry, permission, concurrency or security gate is relaxed.
A safe producer receipt is uploaded with route evidence on handled resolution failure. Runner loss, cancellation or the outer hard job timeout can preempt receipt creation/upload; such outcomes remain failed or incomplete, never PASS.
The workflow-call same-run producer path retains its existing explicit producer-result check.

## Validation boundary

Deterministic controls exercise active-to-ready assembler admission, exact attempt pinning, latest-run no-fallback, replacement, cancellation during artifact discovery, failed assemblers, invalid artifacts, partial pages, API failure and request-inclusive timeout.
Local Python/Docker execution was unavailable when this patch was authored: the selected execution environment was starting/offline and rejected `exec_command` with `No usable execution environment`.
Hosted checks on the exact published HEAD must supply execution results; source review and unexecuted tests are not passing evidence.

Scheduled/manual execution of the corrected standalone resolver is separately required; PR execution uses source runtime and does not exercise remote Main lookup.
The original scheduled attempt remains failed after any later qualification.
Student Council remains RELEASE_NO_GO. This correction neither lifts Owner holds nor authorizes RLS, recovery strategy changes, deployment or school-user admission.
