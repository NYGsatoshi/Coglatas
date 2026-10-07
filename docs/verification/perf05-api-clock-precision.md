# API clock accumulation precision

Candidate #1116 head `b1e316d51dd0b7a65825ee0d4ff714a5f3f5ef07`, run
`37647010295/1`, produced 76 PASS, one REGRESSION and one UNSTABLE. The measured
synthetic merge was `f4dfdf292fae0e0ad9e6b22057f41158904071a5`.
Artifact 11495142066, ZIP SHA-256
`c453a1012d0592727314d18f2ca715b0f7b7e134da65ea4fe628dacc17a1674b`,
retains all five current and five baseline trials. Independent replay from the
pinned candidate source exactly matches that rejected result. No CPU-model
rejection occurred; the hard environment class matched.

| Stage | Expected | Observed | Evidence | Classification |
| --- | --- | --- | --- | --- |
| Aggregate wall-clock duration | Sum integer clock ticks, convert units once | Twenty fractional-second additions accumulate representation error | Actual `api-k6.js` counter and deterministic integer-clock regression | Proven instrumentation defect |
| Notification throughput boundary | 20 requests / 81 ms versus 20 / 135 ms is exactly a 40% decrease | 246.91358024691348 versus 148.14814814814807 computes a 40.00000000000001% decrease | Authenticated original scalars; red/green clock test | False numerical boundary crossing |
| Workspace detail baseline p99 | Baseline relative MAD <= 0.20 | 0.2050130570963322; current relative MAD 0.04300515153181199 | Complete original five-sample baseline/current arrays | Genuine baseline variability; initiating cause UNKNOWN |

The original scalar rates and request counts recover the integer-microsecond
totals 81000 and 135000; individual original request durations were not retained.
The deterministic test constructs a twenty-request clock sequence with those
same totals and reproduces the accumulation error. It also proves that a real
136 ms current total remains beyond the unchanged 40% limit.

The repair accumulates integer microseconds, retaining the same wall clock,
request boundaries and one-microsecond minimum, then converts once to the
existing `durationSeconds` summary field. Integer totals are exact within the
bounded twenty-request / 120-second execution. No tolerance, budget, MAD limit,
sample count/order, workload, warm-up count, timeout or percentile calculation
changes. Existing authentication, persistence, failure and diagnostic tests pass.

This repair does not explain or waive baseline p99 variability. That original
cohort remains rejected, including its UNSTABLE result. A new substantive source
SHA requires one fresh acceptance cohort; unchanged failing candidates are not
rerun. The API implementation identity changes explicitly. The DB implementation,
configuration, fixture, approved Small/Medium samples and class digests remain
unchanged, so no Medium baseline capture is needed or repeated.
