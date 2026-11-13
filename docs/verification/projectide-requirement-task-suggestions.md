# ProjectIDE Requirement Task candidates

This partial #1030 Product slice follows #888 / A-R2 and the existing INDEX,
SEM-12 and FIX-08 direction. It supplies reasoned read-only candidates, not a
Requirement satisfaction judgment, trusted evidence, an automatic Draft edit or
full Language Service acceptance. Runtime read authority and independent Security
qualification remain in existing P0 #1930. No SecurityTestImplimation changes or
activation are part of this contribution.

## Shared Core query

`CompilerSemanticInspection.SuggestRequirementTasks` accepts an exact expected
Compiler input digest and the location of one decoded unique Requirement in
that inspection. Stale inputs, every foreign context dimension, a different
candidate context and non-Requirement origins are refused without candidates or
scan counts. The caller must already have current authority to materialize and
read the input; an inspection, digest, cursor or candidate confers none.

Only decoded local Task declarations are candidates. Ports, unknown kinds and
external snapshots do not become untyped matches. Each result has the explicit
structural reason `DeclaredTaskInSnapshot`; the method does not evaluate the
Requirement condition, rank semantic suitability or claim fulfillment. The page
stays `CandidateOnly` even when binding coverage is complete.

Pages use the existing 32-candidate and 256-scanned-symbol technical ceilings.
An owner-and-Requirement-bound in-process cursor retains remaining work even
when an entire scan finds no Task. It is not a serialized server token. Binding
coverage and an unexhausted local page are separate facts; an empty partial or
truncated page never establishes global absence. Cancellation throws. Source,
settings, IDs and Compiler results are unchanged.

Inspection symbols/references, Find References results and both candidate-query
lists use owned immutable collection views. Generic list mutations are rejected
and non-generic synchronization storage cannot expose mutable backing arrays or
lists. Original negative tests demonstrated that the previous read-only wrappers
could be emptied or changed through `ICollection.SyncRoot`; those failures are
retained separately from corrected results.

## Paired synthetic clients

The Windows pure ViewModel queries only its accepted current compilation and
selected Requirement. It accepts only the exact retained page/candidate for
source navigation, checks lifecycle/selection/input stamps, and clears on
selection/result/reset and before cancellation. A separate query generation
prevents an older call from overwriting a nested newer page during notification.
This is pure .NET preparation, not actual Avalonia acceptance or a server query.

The exporter invokes the real Core query for two Requirements in one new
synthetic case. Both receive explicit structural reasons while the Compiler
result remains Inconclusive. The prior seventeen full fixture case objects are
unchanged; the exact roster grows to eighteen. Optional candidate metadata is a
local preview projection, not formal Registry/artifact/reader adoption.

Angular validates input, origin kind, candidate identity/name/reason, coverage,
uniqueness and candidate-only status before showing the page. Navigation checks
the exact retained objects, current selected Requirement, source mapping and
current UI read permission. A changed origin cannot reuse a cached list to open
a different Requirement's candidates. Context/load/denial boundaries discard the
page; legacy inspection-free fixtures keep suggestions inert. Both locales label
the reason and the absence of satisfaction proof. Opening a candidate only
navigates to its declaration; it issues no modifying Project API request.

Current persisted actor/session/capability checks for query/open, names, counts,
paths and candidates remain P0 #1930 work. No new role, policy relaxation,
Requirement-version/evidence adoption, stored Draft, shared Merge, RLS,
Trust/Eligibility, actual data/Avalonia/Native/Production/Release or numerical
resumption is implied. #1144 BLOCKED and #1128 SUSPENDED/NOT_EVALUATED remain.

Executed tests, original failures, current source/review/host/CI identities and
their limitations are recorded in the PR; merely describing the controls is not
acceptance evidence. Wider #1030 and #906 remain open.
