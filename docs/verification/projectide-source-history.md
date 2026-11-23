# ProjectIDE bounded Source history reconstruction

This Product slice advances #908 / A-R2 Q04/Q08-A and HIST-01/02/04/05/12.
It supplies an actual pure checkpoint/forward-delta reconstruction engine and
paired synthetic client preparation. It does not complete stored History,
server Branch/Live operations, historical authorization, retention/deletion,
atomic revision/audit/required Outbox, operational Revert, migration or actual Avalonia.
Security-side work remains P0 #1932; the ProjectIDE agent does not execute
SecurityTestImplimation changes. #906 owns governed proposal application.

## Core data and integrity

`ProjectSourceDelta.Create` compares admitted immutable committed Sources on
one Branch, with distinct RevisionIds. It retains changed DocumentId, EntityId
and RelationId records, both original sides of additions/removals/replacements,
and before/after envelope metadata. It does not retain either whole Source
object. Manifest data, unknown fields and deleted values are preserved without
guessing semantic meaning. Roots remain in their exact document record.
Canonical Source digests establish content integrity, never entity identity,
authorization, semantic compatibility or producer trust.

`ProjectHistoryRevision` carries a caller-materialized checkpoint or typed
delta, exact RevisionRef, expected Source digest, Branch-local sequence and
explicit parent roster. A checkpoint may anchor retained newer versions after
older ancestry has been compacted. Sequence values are not a global clock;
cross-Branch parents cannot be ordered by comparing their counters.
These are in-process candidate objects, not a newly adopted Registry artifact,
durable storage format or generic historical reader enrollment.

`ProjectSourceHistory.Read` bounds and disposes the supplied enumeration, checks
Tenant/Project scope, refuses duplicate/self/known-cycle lineage and invalid
same-Branch ordering, then follows the exact forward-delta parent path to an
anchor. Every applied delta checks its exact base context/digest/metadata and
original changed values. Reconstructed Source is admitted again and checked
against the exact requested context and expected digest. Missing bodies or
anchors return Unavailable; unsupported Source schemas return Unsupported;
integrity/lineage contradictions return Corrupt. Refused results expose no
partial Source or reconstruction path. Cancellation throws without an empty
success. A caller must already have current authority to supply/read all bodies.

Ready means exact Source reconstruction only. `SourceAdmissionStatus` separately
records structural admission. The existing codec can preserve opaque members
with Unsupported status and a usable Source; that body remains reconstructible
without claiming Compiler support. An unsupported schema without a Source body
is refused. `ReconstructionPath` identifies only the actual anchor/delta path,
not a complete ancestry graph or current-authority receipt.

## Bounds and compaction

Provisional local technical ceilings: 256 supplied records, eight parents per
carrier, 32 reconstruction records including the anchor, 8 MiB total retained
canonical body/delta-value bytes, individual Sources/checkpoints up to 4 MiB,
100000 JSON node charges, depth at most 63 from the root and numeric lexemes up
to 100000 characters. Delta node charge conservatively counts both original
Sources, including unchanged values, to bound later processing; it is not a
managed heap measurement. Oversized construction is rejected; oversized reads
return ResourceLimit. Parent/record enumeration and JSON/graph/record loops
check cancellation. Source/model/history collections retain private owned
storage and reject generic list mutation; no non-generic SyncRoot exposes it.

Compaction here is a verified local collection replacement: reconstruct a
version, construct a checkpoint at that exact version, then reconstruct later
retained deltas without the older chain. Tests prove exact byte/digest equality
and explicit failure if the required anchor is removed. There is no production
deletion/cleanup or retention scheduler. Measured operational thresholds and
authorized deletion policy remain mandatory before storage activation. Pin is
not a deletion, revocation or capacity exemption.

## Paired clients and remaining work

The Windows pure .NET ViewModel reads the same Core history only within its
accepted Project scope, rejects foreign requests before invoking a body supply,
notifies consumers when old history is cleared even if the next read is foreign,
cancelled or throws, and cannot publish an obsolete result after reentrant
reset/disposal. Actual Avalonia is not activated.

The offline exporter now creates the existing document-after fixture through
the real delta/checkpoint Reader, compares it to the full-reference Source,
proves missing-anchor refusal and verifies replacement-checkpoint compaction.
All fourteen pre-existing Source/Compiler case objects remain unchanged; only
optional prepared-history metadata is added. Native contract tests independently
reconstruct the exact exported pair rather than trusting its success flags.

Angular validates exact case/context/digest/path/parent/sequence bindings,
including one-Branch forward-delta paths, and
shows a localized 32-row History panel, distinct Source-admission status,
missing-anchor outcome and exact historical Source navigation. It does not
reimplement reconstruction, confer history capability or present a non-Live
Branch as operational truth. Context/load/denial changes discard the projection;
actions revalidate the retained object and current UI permission. Browser tests
exercise both locales and Desktop/Mobile keyboard/accessibility controls.

Durable/object storage, transactional atomicity, old-version authorization and
privacy, tombstones/retention/deletion, real compaction failures/migrations,
persisted/reviewed inverse-Draft application and actual API/UI operational qualification
remain open in #908/#906/P0 #1932. No acceptance checkbox for the whole program
is promoted by this slice. Commands, actual source/host counts, original failures,
immutable artifacts and review/merge evidence are recorded in its PR; listing
tests here is not a claim that a current run passed.

The separate [local inverse-Draft preparation](projectide-draft-revert.md) now
uses this reader and exact lineage; it does not activate operational Revert.
