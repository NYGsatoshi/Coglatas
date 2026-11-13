# ProjectIDE inverse Draft preparation

This Product slice advances #908/#906 under #888 / A-R2, CHANGE-21/22 and
HIST-01/02/04/05/12. It prepares an actual new immutable candidate in Pure Core
and exposes the same result through the offline Angular fixture and Windows
pure ViewModel. It does not persist a Proposal, review or merge a Project,
rewrite history, change Live designation, run an external effect, or qualify
actual Avalonia. Current historical rights and independent Security work remain
in P0 #1932. The broader program acceptance criteria remain open.

## Exact inputs and lineage

`ProjectDraftRevert.Preview` receives the selected parent, selected revision and
current committed semantic models, three exact expected input digests, a fresh
candidate context and already-authorized historical materializations. All three
Sources must belong to one Branch. The candidate's base and captured target
head must both equal the exact current revision. Existing pins, materializations
and decoded references reserve their contexts, including unresolved references;
a reserved candidate context is refused.

The bounded history reader independently reconstructs each supplied Source and
compares its exact canonical body. A selected single-parent revision must name
the supplied parent. Known parent links must demonstrate descent from the current
head to that selected revision. A compacted checkpoint with missing ancestry
does not manufacture this proof. Missing/corrupt/mismatched bodies or missing
descent return Unavailable; multi-parent selection or Branch-hopping ancestry
returns Unsupported. History metadata does not establish authorization or trust.

## Inverse and conflict behavior

Decoded local references and declaring value origins are normalized into the
fresh candidate context by the existing typed Draft transition visitor. Pinned
value targets/manifests and opaque data are preserved exactly. No text search
replaces contexts inside arbitrary payloads.

DocumentId, EntityId and RelationId records are compared by typed identity.
An inverse restores the original record only when its current normalized value
still equals the selected after-side. A current value already equal to the
original side is an explicit no-op. A different overlapping record produces
Conflict. Conflicts expose no partially applicable Draft, even if other inverse
changes were independently possible. This conservative profile requires later
review/resolution for different edits within the same record; it does not claim
property-level three-way merge or automatic semantic conflict resolution.

Document additions/deletions and matching manifest entries are inverted together;
later independent Documents survive. Document/manifest digests are rebuilt from
the resulting records. Source and manifest metadata invert by exact member value,
with absence distinct from JSON null. Unknown values are restored literally,
without semantic interpretation. Current-only metadata and unrelated later
records remain intact. Structural admission rejects identity collisions or other
invalid combinations as InvalidSource, without publishing a candidate.

Document and manifest-reference Digest envelopes remain independent. The shared
Draft transition refreshes only the known hash value, retaining each envelope's
opaque members. Revert excludes the derived manifest hash value from comparison,
while retaining unknown digest members for inverse/conflict checks. A metadata-only
change must not disappear as NoChange, and document digest metadata must not be
copied over a manifest reference's separate metadata. Retained opaque fields are
not verified signatures or trust evidence; semantic coverage remains incomplete.

The new candidate always retains the current Compiler settings and pins; old
settings are not restored when historical rules/configuration differ. Ready
means structural inverse preparation only. Opaque Source may still have
incomplete semantic coverage, and a structurally admitted inverse still needs
the current required checks and governed review before actual application.

`ProjectDraftRevertPlan.ApplyToDraft` returns the complete prepared input only
against its exact current Before input and Source, or returns null. It cannot
apply against an older snapshot, a changed current input or its own After input.
Cancellation throws and no partial candidate is returned. No returned object
confers history capability, persistence, approval, Merge or execution authority.

## Bounds and clients

Local provisional ceilings reuse 256 supplied history records, eight parents
per carrier, 32 reconstruction records, 8 MiB of supplied history storage and
100000 JSON node charges. The three input Sources are also charged together
against 8 MiB and 100000 nodes, with the history JSON depth/numeric ceilings.
At most 4096 inverse record/member operations are considered, including
corresponding manifest operations. Enumeration is disposed and loops check
cancellation. The composed candidate also obeys the default 4 MiB Source ceiling;
an oversized composition returns ResourceLimit rather than schema Unsupported.
These limits are not operational retention or deletion policy.

The Windows synthetic ViewModel prepares only against its accepted current
compilation. Clearing a previous preparation notifies subscribers before a
rejected/cancelled/throwing read, and lifecycle checks reject reentrant reset or
disposal. A preparation generation also prevents an older read from replacing
a newer nested preview without a lifecycle change. Only the exact retained plan and a fresh client generation can start
the next local synthetic compile; prior results and preparation are cleared.

The exporter invokes the real inverse engine and exact application, checks its
current base and restored document, and compiles the new candidate. The original
fourteen Source/Compiler fixture cases stay unchanged; one new candidate case
and optional prepared-inverse metadata are added. Native tests recompute the
plan and candidate rather than trusting success flags.

Angular binds all four contexts/input digests, case identities, current
freshness, same-Branch scope and candidate base/head. It revalidates retained
objects before loading a prepared fixture. Context/load/denial changes discard
the action. The History panel displays the reversed revision and current base,
opens the new candidate with keyboard focus, and preserves the distinction from
a saved/reviewed/merged Project. The browser computes no inverse and proves no
ancestry, canonical digest or authority. Both locales and Desktop/Mobile are
covered by keyboard/accessibility/no-modifying-request controls.

Durable Draft storage, current actor/session/history capability, stale/concurrent
server commit, reviewed conflict resolution, policy/check invalidation,
idempotency, revision/audit/required Outbox atomicity and actual API/Avalonia
acceptance remain #906/#908/P0 #1932 work. No Source Registry artifact, migration,
Trust/Eligibility enrollment, RLS, Native/Production/Release or numerical
resumption is adopted. Actual tests, original failures, review and exact source
qualification are recorded in the PR, not inferred from this document.
