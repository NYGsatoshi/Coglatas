# Core Slice 6: semantic structure and derived IR

Product-only #905 continuation after #1617, with independent tests after #1618.
The immutable binding model is checked for supported relation/port/cardinality/
cycle contracts, exact descriptor capabilities, units, time context and local
closure before a regenerable IR and Source Map are produced. Invalid/Unknown/
Unsupported closure returns no IR. Relatedness cycles remain legal; unsupported
dependency/dataflow cycles cannot be lowered into executable success.

IR is an inspection/analysis artifact, with no runnable plan. Nodes derive from
scoped Source EntityId plus stable lowering role/key; their UUID v8 derivation
keeps full digests and explicitly detects truncated-ID collisions. Source maps
bind exact input context and typed locations. IR, semantic-model and Source-map
digests include their input binding. Source remains unchanged; edits require
Source changes and a full recompile. No independent writable IR, incremental
cache or operational state is introduced.

This slice supplies a missing minimal input seam: an explicit `units` lock list,
using exact artifact ID/version/digest pins. Materialized Unit definitions and
calendars must match the locked manifest. Quantities/ports require compatible
dimensions; no implicit conversions or missing-unit success. Calendar snapshots
also require their pinned timezone artifact (`coglatas.time-zone/<IANA ID>`).
Working durations cannot use the host timezone/calendar. Unknown numeric/time
inputs retain their states. Zoned-time lowering and external semantic closure
remain Unsupported rather than guesses.

Binding and semantic validation carry deterministic logical work consumption
forward. Input, materialization and graph bounds produce explicit incomplete
outcomes; expansion beyond artifact bounds produces no artifact. These are
provisional safety limits, not #1128 performance assurance.

Requirement evidence, Constraint satisfaction and Resource snapshot evaluation
are recorded as deferred checks, distinct from validated relation structure.
Their original graph outcomes remain visible in the validation record. Slice 7
must evaluate them or publish Unknown; a typed IR or valid link proves none of
those facts. Unsupported Workflow execution is not silently enabled.

Compiler Result artifact references bind the exact input and must name digests
actually emitted by a producing stage. Their identity participates in the result
digest; random storage locators and host timings do not. No plan/run permission
or Product merge authority follows from successful structural compilation.

Tests include exact Source maps/digests, repeated full compilation, revision
changes with stable Source-derived node IDs, an independent Python IR identity
golden, graph-kind differences, unsupported closure, unit pin/dimension errors,
shared processing budgets, descriptor capabilities, cancellation and false
artifact receipts. Exact candidate receipts/CI are recorded on paired Draft PRs.

No protected branch push/merge, check relaxation, new dependency, production,
RLS/SEC-ARCH/Shadow/Enforce or Student Council release decision change. Angular
retained; new Web UI NOT_STARTED, Windows/Avalonia BLOCKED by #1144. #1128 remains
SUSPENDED / NOT_EVALUATED. Source review/CODEOWNER/integration acceptance are pending.
