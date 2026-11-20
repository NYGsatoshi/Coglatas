# Elapsed schedule impact for a fixed Scenario

This local Core query implements the elapsed finish-to-start part of #1032
under #888 and IMPACT-02, IMPACT-04, IMPACT-11 and IMPACT-18. It compares
two existing immutable simulation runs for the exact frozen baseline and
Scenario inputs. It checks both expected recording digests through the
shared run comparator and requires completed, current recordings with
complete coverage, the supported deterministic engine, the same virtual
start and the same run limits.

A schedule records the original run digest and each Task's exact source
location, start offset, finish offset and total float in elapsed ticks. Total
float is the latest allowed finish minus the observed finish, holding the
modeled finish and all other task durations and dependency lags fixed. The
backward pass uses the plan's stable topological order and takes the tightest
bound when several successors constrain one Task. Each slot uses the full
captured Source context and IR node identity. The signed modeled finish
difference comes from the two original complete simulation recordings.

The two-path join fixture shows that a three-tick delay can fit entirely in
slack. A delay that exceeds six ticks changes the modeled finish only by
the excess. The fork fixture shows why all successors must constrain float.
Partial or stale recordings have no complete schedules or delta. Admission
failures, cancellation and the projection work limit preserve the original
recordings. The work limit charges Task definitions, events, dependencies,
the evaluation order and the backward pass before publishing both schedules.

This profile models elapsed time, nonnegative finish-to-start lags and
unlimited capacity. Calendar and Resource-aware scheduling, milestone or
release estimates, and paired client and runtime acceptance remain under
#1032. The query starts no simulation and produces no Test verdict.

The offline Windows Workbench keeps the simulation recording that was current
when the user loaded the Scenario. An explicit schedule-impact read compares
that original baseline with the current Scenario recording. A Scenario without
a captured baseline has no comparison. A partial recording leaves modeled
finish unavailable. Replacing the run or scope withdraws the comparison.
Applying a Draft also withdraws the Scenario baseline. A cancelled read
withdraws its own publication, even when a subscriber also throws during
notification. The original subscriber failure reaches the caller. A reentrant
newer read retains its own result.

The fixed Workbench fixture compiles and runs its actual baseline Test,
applies duration and input-default assumptions, and compiles and tests the
fresh Scenario. The Source diff, typed assumptions, Test verdicts and elapsed
schedule comparison remain inspectable as distinct layers before applying
and revalidating the new Draft.

The Angular Scenario schedule example associates the schedule frame with
the verified baseline and Scenario recordings and their overlay digest. The
reader checks the modeled finish, per-Task offsets, and dependency
constraints on total slack against those recordings and their lowered plans.
It displays the schedule beside the Source, assumptions, and Test results.
The bounded example displays an unavailable finish change, and a stale or
mismatched schedule cannot replace the other admitted Scenario results.
Loading another stage withdraws the comparison.

Generate the separate fixture with the Preview tool's `--scenario-schedule`
option. Run the Angular Scenario and schedule unit tests and
`tests/ui/projectide-scenario-schedule.spec.ts` against that generated asset.
