# ProjectIDE immutable Compiler envelopes

This Product correctness repair follows the existing #905 and #1034 immutable
input/result/evidence contract. It does not adopt a new evidence schema, policy,
Producer, Registry, API or execution mode. Independent Security qualification is
tracked separately in P0 #1938 under the Owner's ProjectIDE-only work boundary.

## Reproduced inconsistency

Read-only collection wrappers exposed their mutable backing arrays through the
legacy ICollection.SyncRoot property. An in-process caller could replace locked
input rules without changing the retained canonical input digest, change proof
premises without changing the basis data, or replace a result's typed PASS rule
with FAIL while its stored verdict and result digest still described PASS.
This is a local aliasing defect; no remote exploit or production incident is
established by these controls.

The same storage exposure affected result diagnostics, stages and artifact
references, derived IR nodes, and freshness explanation dimensions. Merely
rejecting generic list edits did not protect the synchronization storage.

## Owned snapshots

The existing ImmutableListView now owns the input rule roster, proof premises,
result rules/diagnostics/stages/artifacts, derived IR nodes and freshness
explanations. Caller-owned collections are copied, supported generic mutation
operations still throw, and synchronization storage cannot reveal the arrays.
Canonical ordering, field values, IDs, digest domains and serialized bytes keep
their existing meaning. Empty evidence/result/freshness lists remain valid.

A result's typed view must continue to reproduce its stored canonical data.
A locked input must recompile identically after public collection access.
Windows pure preparation consumes the same actual Core response, while the
offline Angular exporter continues to reproduce the existing complete fixture
bundle. This is not actual Avalonia or a server authorization decision.

## Qualification and boundaries

Regression controls retain the original failures and verify recompilation,
typed-versus-canonical consistency, input collection ownership, generic and
legacy mutation attempts, empty views, freshness and Windows consumption.
Full existing fixture cases protect the paired synthetic clients. The exporter
must remain byte-identical; this change adds no preview field or fixture case.

A source-copy timestamp can leave an incremental build using an earlier
assembly. Original and corrected counterexamples therefore use explicit
non-incremental builds, with discovery/TRX and source identities retained.
Unexecuted or stale-assembly probes receive no qualification credit.

Product tests and this storage repair do not grant independent approval,
current query/open/apply authority, Producer Trust, Evidence Eligibility or
retention rights. P0 #1938 owns the independent Security follow-up; #1930 and
#1932 keep their separate runtime authority and history responsibilities.
#1144 remains BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, and existing RLS, Trust,
Eligibility, Native, Production and Release HOLDs remain unchanged.

Application host profile/request/freshness collections and nested supplied
calendar/timezone data require separate aliasing controls and repair. Four
Application counterexamples are retained in P0 #1938; this Domain-only change
does not fix those host collections or complete that issue.
