# Deterministic native preference update collision

The [original failed #2312 proof](https://github.com/NYGsatoshi/Coglatas/actions/runs/46121602674/job/140911257186) at 4db08fc1fab52a9e4196ac48ace695200710e424 passed all five collection lanes but failed exact native coverage equality: zero missing, zero extra and four changed hit states. All four are in TaskNotificationPreferenceService.UpdateAsync, on the conditional-update loser re-read and result classification. Baseline did not hit those points; shard union did. This occurred before the new compiler packet staging step; it does not qualify or disqualify that later step.

The existing real PostgreSQL preference race test allowed one update to finish before the other read version 1. Both valid outcomes could satisfy the old outcome assertions, but only a collision after both initial reads exercises the conditional database-update failure branch.

A test-only forwarding repository now synchronizes the first completed materialized reads with a bounded two-party barrier. Both reads must observe version 1, both real conditional SQL writes must occur, exactly one returns true and one false, and both re-read actual PostgreSQL state. Preserve every original success/conflict/current-version and retry assertion, real independent DbContexts and original service/repository. No application code, collector, hit-state comparator, filters, timeout relaxations, retries or coverage exclusions changed. Add the test's exact path to native proof routing.

Requalify the entire #2312 candidate. Do not claim original native branch divergence fixed or compiler staging verified until the full workflow succeeds on exact new head. Keep original failed evidence. No Main merge.
