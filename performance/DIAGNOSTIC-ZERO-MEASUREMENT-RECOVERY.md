# PERF-05 zero-measurement diagnostic recovery

Rule `perf05-diagnostic-zero-measurement-recovery-v1` authorizes exactly one
replacement of diagnostic `37695270091/1`. It never applies to production
acceptance, API acceptance, #1046 duration acceptance, baseline campaigns or
ordinary benchmark reruns. No MAD, sample count, baseline, environment class,
runner assignment or protected acceptance criterion changes.

The original first and only dispatch failed at collector fixture parsing before
manifest creation, login, explicit warmup or entry into the measurement loop.
The retained log identifies `JSONDecodeError: Unexpected UTF-8 BOM` at line 60.
Artifact `11515750860` contains four setup files and their execution identity,
with no timing cohort. Setup warmup is not measured diagnostic evidence.
The versioned rule binds the exact run, attempt, source, archive member hashes,
archive digest and failure log digest. Both the original run/log and artifact
must remain available. Removing or changing any evidence fails closed.

The original implicit JSON reader rejects the same BOM input that the fixed
strict UTF-8 reader accepts. Offline regressions cover UTF-8 with/without BOM,
empty, malformed, truncated, duplicate-field, nonfinite, unexpected-encoding and
multiple-document inputs. Only JSON is consumed; CSV/NDJSON are rejected.
The fix does not ignore corrupt data. No offline test makes a network request.

The workload is frozen to five serial groups of twenty `announcement.list`
requests, original Medium fixture, page 1/page size 5, ordinary `ubuntu-24.04`,
original preflight and fourteen setup warmups, one explicit warmup and one
uninterrupted collection loop. Every attempt is retained in order, with no
retry or early stop. Exact source tree, observer files, environment/fixture
contracts, accepted baseline tree and statistical-policy bytes are pinned.
CPU fingerprints remain evidence only. No source/instrumentation change is
allowed; only strict collector parsing and new diagnostic identity differ.

Before dispatch and again before setup, `diagnostic_recovery.py` validates live
original run/artifact inventories against retained archive/log bytes and the
new declaration. The caller must exhaust all inventory pages; the replacement
inventory aggregates every branch prefixed
`diagnostics/perf05-announcement-diagnostic-recovery-`, not just its own branch.
Before dispatch that inventory must be empty; in-run it must contain exactly
the current first-attempt run. Any prior recovery, retry, incomplete inventory,
reused ID, changed bounds or third attempt is rejected. A task-local write-once
dispatch ledger is also written before the dispatch HTTP request, so an unknown
submission outcome is audited instead of submitted again.

The declaration has a fresh diagnostic ID, UUID dispatch identity, canonical
SHA-256 digest, reference, fixed collector commit and predecessor `37695270091/1`
with zero measurements and BOM defect. A replacement failure cannot itself be
recovered, even if it collects zero samples. No retry-until-data is permitted.
The branch-only workflow has no push/PR trigger, protected check name, comparator
or baseline consumer. It is published with `[skip ci]` and is never merged as a
product repair. This focused rule PR includes no application instrumentation or
#1046 product changes.

After this rule merges, exact new Main must qualify all required and applicable
checks, including API performance, before dispatch. Diagnostic results earn no
acceptance or baseline credit. Keep rejected production `37656457914/1`
immutable and rejected. If one recovered cohort cannot support a causal product
repair, do not invent one or take more measurements; an acceptance-policy
proposal needs an explicit owner decision.
