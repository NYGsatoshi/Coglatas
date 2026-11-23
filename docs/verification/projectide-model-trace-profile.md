# Fixed model trace profile foundation

Authority: existing #1033 under #907 and #888. This is a bounded Pure Core and Windows synthetic ViewModel foundation over an already produced typed `ProjectSimulationRun`. It does not close #1033 or establish an actual Web/Avalonia Profiler view, observed operational-data support, Resource scheduling or privacy acceptance.

## Input and result boundary

`ProjectSimulationTraceProfile.Read` requires the immutable run and its expected full recording digest. It accepts only the current owned simulator engine, fixed-input freshness Current and an actually executed recording. Wrong digest, unsupported engine, stale/unexecuted input or an exceeded query event budget produces Refused with no retained run, basis, period or metric summary. A query budget is explicit in the hashed result, ranges from 1 to 8192 and never silently truncates a larger recording. Cancellation throws without modifying the original run. No new schedule or events are generated.

Complete means only that this owned model recording completed with Complete coverage. Other admitted execution prefixes are Incomplete, including an empty cancelled prefix. Their metrics stop at the last committed virtual clock, with open Tasks counted explicitly. No future unrecorded duration is filled in. Refused and Incomplete never become a passing outcome or current runtime authority.

The local candidate format `coglatas.local-simulation-trace-profile/1` explicitly declares SimulatedModel, evidence kind coglatas.simulation, verdict NotRun, authority NotEvaluated and unlimited capacity. It binds run identity/digest, manifest, plan, input, Source, exact revision context, engine, execution/coverage/freshness and query budget. Period start/through are virtual ticks. The as-of marker identifies the fixed recording and input digests; it does not manufacture a wall-clock observation time or claim live freshness.

## Metric definitions

| Metric | Unit | Meaning |
| --- | --- | --- |
| recordedElapsedTicks | virtual-tick | Last committed virtual tick minus the fixed run start. |
| recordedBusyTicks | virtual-tick | Union of recorded intervals with at least one active Task; concurrent Tasks count once. |
| unattributedIdleTicks | virtual-tick | Recorded elapsed minus busy union; no dependency, approval, Resource or person cause is inferred. |
| recordedTaskTicks | task-virtual-tick | Sum of each active Task's recorded interval; concurrent Tasks count separately and open Tasks end at the recorded boundary. |
| peakActiveTasks | task-count | Maximum active count after a committed transition in the declared engine event order, including zero-duration transitions. |
| openTasks | task-count | Started Tasks not ended at the recorded boundary. |

Task-time uses exact `BigInteger` arithmetic and a decimal string, because overlapping legal Int64 durations may sum beyond Int64. Clock and busy-union remain legal elapsed Int64 values. Start offsets do not become work. For the existing parallel-roots/join fixture, elapsed is15, busy8, unattributed idle7, task-time10 and peak2. Another fixture with overlapping10/5-tick Tasks has busy10 and task-time15. These values describe model transitions, not utilization, contention, individual ability, real performance or predictive validity. In particular, zero-duration transition peak is not a measured concurrent-resource demand.

## Windows pure ViewModel lifecycle

`ProfileSyntheticSimulation` reads only the currently owned admitted run and expected digest. It neither recompiles nor starts a simulation. The exact recording, compiler result, plan and unrelated run comparison remain immutable. Reset, disposal, input/readmission, a new run or debugger replacement withdraws the old profile. A paused debugger without a terminal run has no run profile; its eventual terminal recording can be read explicitly. Generation and recording-reference guards prevent delayed/reentrant publication from restoring an older result or erasing a newer query. A rejected/cancelled/invalid query withdraws its previous summary.

This API remains within the synthetic caller's existing materialization boundary. It is not a server authorization check or a permission-bearing artifact. SecurityBranch independent qualification and any durable query/export/current-permission adapters must be handed off as P0 Issues; no SecurityBranch implementation is part of this Product slice.

## Validation and remaining acceptance

Focused actual Core tests cover overlap/idle definitions, five bounded prefix boundaries, empty cancellation, zero durations, aggregate overflow, clock offsets, exact provenance, unsupported/stale/wrong inputs, query bounds, culture and immutable replay. Actual Windows pure ViewModel tests cover ownership, coexistence with comparison, partial/cancelled records, withdrawal, debugger completion and notification reentrancy before/after publication. Final platform counts and CI/review evidence belong in the exact-source PR record, not inferred from this description.

Keep full #1033 open for paired actual UI/fixtures, matching-condition Scenario comparisons, actual observed operational-data support, critical-path sensitivity, kind-aware Resource/approval waiting and contention, multiple-cause/unknown attribution, privacy/current-permission drill-down/export and independent #793/#794 acceptance. Pure ViewModel tests do not establish actual Avalonia acceptance. #1144 remains BLOCKED; #1128 remains SUSPENDED/NOT_EVALUATED; Native, RLS, Trust, Eligibility, Production and Release holds remain unchanged.
