# ProjectIDE local semantic comparison

This is an initial Product slice of #908, following its existing typed-identity
and explicit-coverage contract. It does not complete History, storage,
reconstruction, retention, current historical authorization, three-way Merge or
Revert. Security-side implementation/independent qualification is P0 #1932;
the ProjectIDE agent does not edit or merge SecurityTestImplimation.

`ProjectSemanticDiff.Compare` compares two already-materialized semantic models
at exact expected Compiler input digests in the same Tenant and Project. Branch,
committed revision and candidate contexts stay explicit on both sides. The
caller must already have current authority to materialize/read both inputs.
The method performs no access lookup or grant, persistence or external action.

Entities match by EntityId, edges by RelationId, and documents by DocumentId.
Names, hashes and JSON similarity never establish identity. The result preserves
direction, original values, missing versus present values and side-specific
Source locations. Name, property and reference changes have distinct categories;
new identities produce additions/removals. Document names are metadata.
Project root membership is a reference change between stable EntityIds.
Arrays remain bounded whole-property comparisons, including port/transition
collections; this slice does not claim individual nested-ID edit scripts.

Decoded structural references to each side's own context use an internal local
comparison tag. They are never rewritten to the other side's actual revision:
a foreign reference already naming that revision cannot alias a local reference.
Typed value targets and manifest pins remain exact; only decoded declaring
origins receive local comparison treatment. Nested collections, structures,
nullable/optional values and set canonical ordering follow those typed contracts.
Opaque/Unknown basis JSON is not searched for context-shaped objects.

Complete means both binding models are complete for this structural comparison,
not Compiler PASS, Impact, Simulation, evidence eligibility or execution
authority. Missing models, stale expected inputs, foreign scope and resource
limits return explicit refusal states without rows or pair metadata. Partial,
invalid, unsupported or unresolved models return Incomplete; an empty list then
never means no changes. Changed extension/package/source-reference data is
Uninterpreted and incomplete. Unsupported envelope content can also make an
empty comparison incomplete. No opaque field is guessed to be layout data;
layout comparison awaits its actual layout contract.
Two different Source bodies under one supposedly immutable version are also
Incomplete, retaining original rows without claiming complete version provenance.

Local ceilings: two Source bodies total 2 MiB canonical UTF-8, 8192 decoded
records across the pair, 100000 JSON nodes across the pair, Source depth at most
48 and numeric lexemes at most 100000 characters, at most 256 output changes by default (caller range
1–1024), and 256 KiB original before/after value bytes. Exceeding output bounds
discards the entire row list and reports ResourceLimit. Cancellation throws;
it never returns an empty success. These are provisional technical bounds,
not measured operational retention thresholds or memory/performance guarantees.
Depth headroom protects serialization of original opaque values in the result;
larger Source codec allowances do not bypass the comparison's explicit bounds.
Source document/manifest and semantic model collections use owned private
storage with rejecting generic list APIs. Non-generic ICollection.SyncRoot
cannot expose their retained arrays and change a comparison under stable digests.

The Windows pure .NET synthetic ViewModel prepares the same comparison against
its exact accepted current input and clears it on result/lifecycle changes.
Reentrant reset/disposal cannot publish an accepted obsolete comparison. Actual
Avalonia, native history navigation and runtime authorization remain unactivated.

The offline exporter emits three `coglatas.synthetic-semantic-diff/1` projections
inside the existing synthetic fixture bundle: a real Core-prepared Draft Rename,
an equal structure with Compiler FAIL, and an incomplete empty comparison. This
format is local preview data, not a formal Registry/history artifact adoption.
Angular checks the entire pair's digests/contexts and each row's original value,
identity, owning document and location before display/navigation. It does not
reimplement semantic comparison. Legacy fixtures without optional inspection
stay readable with comparison inert. A dedicated localized Diff panel renders
32 changes per page, distinguishes absence/incompleteness, and opens the exact
before/after Source snapshot. Context, load and denial boundaries discard it;
navigation rechecks the retained pair and current UI permission. This synthetic
visibility check does not establish server history authority.

Focused tests exercise identity, direction, typed references, nested pinned
values, missing/stale/foreign/partial/unsupported/resource/cancellation outcomes,
original projection bytes, native ViewModel lifecycle, malformed mixed browser
pairs, legacy compatibility, permission loss and keyboard/accessibility paths.
Executed source/run/host counts and failures are recorded in the PR; merely
listing these tests is not execution evidence. Existing assertions and required
checks remain unchanged. All broader #908 acceptance stays open.
