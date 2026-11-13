# ProjectIDE immutable validation snapshots

This bounded Product correction continues #905 and #1034 immutable Compiler
results and typed evidence. Independent Security qualification remains P0 #1938.
It changes collection ownership without changing validation rules or authority.

## Original functional counterexamples

A self-dependent Task produces an invalid Relation result. The read-only
Findings wrapper exposed its backing array through ICollection.SyncRoot.
Replacing Invalid states with Unknown made the same result's HasInvalid become
false, without changing the supplied Source.

A Requirement assessment retained a Current claim. Replacing its public claim
with a result from another revision made reassessment Stale while the original
assessment still reported Current. Its outcome remains Inconclusive; supplied
claims never prove satisfaction or acquire evidence eligibility.

A full compilation retained an Unsupported semantic-version finding and its
canonical result. Public synchronization storage allowed the validation finding
to become Invalid while the retained result continued describing the original
validation. These three clean original controls fail at their semantic
assertions. They are local correctness counterexamples, not a remote exploit,
production incident or runtime authority decision.

## Owned views and verification

The existing ImmutableListView owns semantic findings/deferred checks, Relation
findings/endpoint resolutions and Requirement claims/findings. Existing sorting,
deduplication, input identities, budget/cancellation behavior, closure refusal,
freshness and explicit incomplete evidence remain unchanged.

Corrected verification includes the three functional controls plus existing
Requirement evidence, IR, diagnostic integration, Relation cancellation,
host/time, semantic interpretation, conversion, predicate and paired preview
preparation cases. A non-incremental build precedes execution. All543 cases pass,
zero failed/skipped; all18 exported fixture cases remain byte-identical.

The first corrected run exposed a test assertion comparing newly reconstructed
CompilerDiagnostic object references rather than their semantic values. That
failed receipt is retained. The corrected assertion compares both finding State
and canonical diagnostic data; freshness and Inconclusive assertions remain.

The complete preview remains a synthetic Angular/Windows pure contract. There
is no actual Avalonia, persistence, current authorization or independent
Security acceptance claim. Public hosted checks and exact eventual integration
source still require their normal qualification.

P0 #1938 and parent issues remain open. No Security branch edit/synchronization/
merge, Trust/Eligibility/Registry enrollment, RLS, external/actual data,
Native/Production/Release or numerical resumption. #1144 remains BLOCKED and
#1128 SUSPENDED/NOT_EVALUATED.
