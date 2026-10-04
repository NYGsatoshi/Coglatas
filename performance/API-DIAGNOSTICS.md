# PERF-04 bounded API variance investigation

This observer supplies diagnostic evidence for #1046 / #606. It changes no
threshold, scenario, sample count/order, warm-up, comparison policy, baseline,
fixture version, protocol version or environment compatibility key.

## Historical failure and classification

The original ordered current `mutation.kanban-move` p95 values were
`115.27025635, 56.506962, 87.53584195, 64.81948335, 47.61438685` ms.
Relative MAD was `0.26543094 > 0.20`; this remains UNSTABLE. Each trial contained
20 mutation requests. Trial 3 also had a p50 near 64.165 ms, versus roughly
33–36 ms in the other current trials. A single slow first request cannot explain
all of that median change. This challenges a cold-only explanation; it does not
prove another cause.

Classification remains **UNKNOWN** until actual exact-SHA traces establish where
variance arose. Existing percentile summaries lack per-request CPU/DB/worker
evidence. New diagnostic numbers never waive that failure or select a baseline.

## Prospective bounded cohort

`COGLATAS_PERFORMANCE_API_DIAGNOSTICS_ENABLED=true` independently enables the
observer only with explicit PERF fixture opt-in and `ASPNETCORE_ENVIRONMENT=Test`.
It does not enable DB capture or `dbScenarioFixture`; those would change workload.
Ordinary API gate runs leave the observer disabled.

An observer-changing PR runs a separate diagnostic job on its initial `opened`
event at workflow attempt 1. Synchronize/reopen events cannot start another cohort.
An explicit diagnostic dispatch is also supported. Before any measurement, the
job writes `cohort-manifest.json` binding exact SHA, workload/toolchain, creation
and expiry, 13-scenario order, 20 requests per scenario, five current-only capture
groups, no early stop and zero retries. It executes all five groups, retains
each failure status, and neither compares with nor promotes a baseline. Failed
groups are never replayed. Workflow reruns cannot launch this diagnostic job.

Each group uses a fresh PERF-02 stack and deterministic reset. Existing two read
warm-up iterations and read-only mutation preparation remain unchanged. There
is no POST warm-up. Memory is bounded to 1,300 captures (largest supported 13 ×
100 one-VU profile); this cohort uses 260 per group. Only five slowest numeric DB
durations are retained. Private k6 NDJSON is bounded to 32 MiB and 16 KiB per line.

The normal `performance-fast` aggregate still uses exclusively the ordinary
benchmark. Diagnostic artifacts explicitly say
`investigation-only-not-gate-or-baseline-evidence`. They cannot supply gate
credit, rescue a sample or fabricate a baseline.

## Sample evidence and boundaries

Only measured requests receive locally generated opaque capture IDs plus fixed
scenario/trial/sample ordinals. Authentication, CSRF, warm-up, mutation preparation
and health calls receive no sample ID. Original k6 latency/counter/active-wall-time
update order is preserved; diagnostic assembly follows those updates.

The server records Stopwatch start/end/frequency and the entire processing
pipeline, including authorization, handler, persistence and response serialization.
The client records existing k6 duration, blocked, connection, sending, waiting
and receiving timings, plus UTC wall endpoints. k6 exposes no monotonic endpoints;
the sidecar states that limitation instead of calling `Date.now()` monotonic.

A scalar EF interceptor counts successful/failed commands, sums execute durations,
retains the slowest five durations, and records logical connection Open count/time.
It never reads SQL, parameters, connection strings or exception messages. Execute
time excludes deferred reader drain and direct Npgsql commands. Logical Open
includes pool acquisition and possible physical setup. Acquisition-only delay,
isolated transaction setup and JIT are explicitly unavailable.

Before/after snapshots include GC generation counts, approximate process-wide
allocated bytes, available worker/I/O threads, pending ThreadPool queue, process
CPU time and effective processor count. Bounded reads collect numeric Linux host
total/idle CPU counters and cgroup v2 usage/throttled time/periods/quota. Unavailable
kernel counters stay null with a fixed reason. Host and container CPU counters
describe different boundaries and must not be treated as identical resources.

Fixture reset UTC/monotonic completion follows unchanged seed completion. Each
sample binds warm-up completion identity/UTC. Sidecars bind SHA/trial, fixture
digest, warm-up digest, projected raw client-sample digest, each raw server-capture
digest and observer implementation digest. Samples remain in original order.
Missing/duplicate/reordered/unexpected/partial groups fail closed. Available
partial evidence is retained; no request is replayed.

## Worker and mutation source audit

All existing workers remain enabled with unchanged scheduling and behavior:

| Worker | Observation | Cost boundary |
| --- | --- | --- |
| `OutboxDispatcher` | Batch starts/active batches, event attempts, SignalR send attempts | Async claim, authorization, delivery and marking may overlap requests; transactional staging remains in mutation |
| `TaskDeadlineDigestWorker` | Cycle starts/active cycles | Notification DB/CPU work remains enabled |
| `AnnouncementPublisherWorker` | Cycle starts/active cycles | Due-time publication and persistence remain enabled |
| `AuditPackageExportWorker` | Cycle starts/active cycles | Export polling/processing remains enabled |

Global before/after counters also count every EF command. Global-minus-request
command deltas are overlap evidence, not proof of worker causality. Active-worker
counters expose work already underway at sample start; start counters expose new
cycles. Event/send counters never export recipients, connections, event IDs or
resources. Zero sends does not mean zero outbox work.

Source confirms that the first Kanban POST is measured. The mutation retains
authorization, optimistic Task/board version advancement, synchronous audit,
required transactional outbox staging, command persistence and the post-write
authorized snapshot. Async dispatch remains enabled. No product cost is removed.
Reads can leave write-path initialization unexercised, but source alone cannot
attribute the failure to EF/Npgsql, transaction/outbox initialization, JIT, GC or
worker contention. POST warm-up requires separate steady-state-contract and
state-equivalence evidence; it is not introduced here.

## Privacy and observation overhead

Uploaded artifacts contain no SQL/parameters, route with resource IDs, header
values, bodies, credentials, Tenant data, Task titles/bodies, user content or
exception messages. Raw k6 NDJSON stays private and is destroyed; only fixed
numeric/identity projections are saved. Unknown fields cannot pass the allowlist.

Server sidecars accumulate in bounded memory. The normal teardown health probe
uses a Test-only observer flush header; after that response completes, the buffer
is sealed and persisted. No new product endpoint is added. Disk writes cannot
overlap measured requests. A missing flush produces missing evidence and failure;
available failed-response captures are retained if the final health flush completes.

Observation still costs CPU/allocations: kernel/process/GC snapshots, EF callbacks,
counters, buffer objects and k6 points. Pre/post snapshots lie outside the server
stopwatch; client timing may include surrounding observer work. Buffer assembly
after one metric window can affect the next request. These limitations forbid
claims of identical numerical performance or use of this cohort for acceptance.
Correlation is not causality. Classifications must cite real traces, acknowledge
observer overhead, and remain UNKNOWN if factors cannot be distinguished.

## Verification

Python tests cover scalar privacy, private NDJSON transport across VU/summary
contexts, missing/duplicate/reordered samples, numeric/clock/identity failures,
bounded arrays and retention before partial-group failure. Node transport tests
verify unchanged ordinary latency/count values in that model, persisted mutation
version advancement and absence of preflight/warm-up capture headers.

C# tests cover independent Test-only registration, command/lifetime bounds,
worker overlap, closed-request isolation, and middleware buffered flush with
unchanged response content and protected input omitted. Local tests are not actual
PostgreSQL/k6 evidence. The prospective hosted cohort is required for attribution.
