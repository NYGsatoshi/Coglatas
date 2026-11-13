# ProjectIDE local Draft display-name Rename preparation

This extends #1030 through #906's checked Draft boundary under #888's shared
Core and paired-client preparation authority. It does not create a server
Proposal, save a Project, commit a shared Branch or activate Avalonia.

`ProjectDraftRename.Fork` takes an admitted committed model and an explicit
Proposal/candidate/base/captured-target context. It creates an immutable local
candidate while preserving the original Source and typed IDs. An explicitly
different target Branch remains visible; this carries no target authority.

`Preview` prepares a display-name edit for a known declaration in a candidate.
The next candidate must keep the same Proposal, base and captured target head
and have a different candidate revision. Source/input/context drift, unsupported
origins, invalid Unicode, name/reference processing bounds and unchanged names
have explicit outcomes. The plan lists resolved typed incoming reference origins
and binding coverage, and refuses a next context already declared by a snapshot pin or materialized
snapshot. The runtime still has to enforce freshness against durable history;
Core cannot prove absence from inputs it has not received.
Incomplete binding is not a complete reference inventory.
The plan preserves compiler settings and carries the whole immutable after input.
`ApplyToDraft` returns that whole input only for the exact original Source and
input, or returns nothing. There is no partial field write or persistence API.

The transition visits decoded contracts only: local structural reference
contexts advance to the new candidate, and typed value references advance their
declaring origins while retaining pinned target contexts/manifests. Unknown
declarations, extension objects and Unknown basis remain opaque. Document and
manifest digests are recomputed through the existing canonical codec. Known set
values retain canonical ordering. Previously decoded declarations cannot silently
become invalid. Existing canonical byte bounds are applied after canonicalization,
not to a larger intermediate Unicode-escaped representation.
This also applies to canonical Set identities during both Fork and Rename; a
large admitted Unicode Set value is preserved without an escape-size exception.

This changes display names, not entity identity, external/public keys, Symbol
aliases or graph topology. Version/evidence payloads are not invented or repaired.
In particular, existing candidate/Scenario Requirement version interpretation and
local value-reference manifest holds remain as documented by the earlier Core
slices. A local rename plan is not a whole-model PASS, evidence or Merge approval.

The Windows pure ViewModel can prepare and apply its displayed plan to the local
synthetic Draft. It rechecks current selection, exact accepted input, plan identity,
Source mapping and generation. Application clears old results/selections and
requires a fresh Compiler response. A late old response cannot become current.
The host's immutable materializations are carried forward, not reacquired or
re-authorized by Core.

The offline exporter executes a real Core fork/Preview/application and compilation
for the `draft-before` and `draft-after` fixtures. Angular validates their exact
input/context/Proposal/base/head/target/name/reference pairing and offers a
localized prepared-Draft fixture load. It does not implement arbitrary browser
name editing or a modifying server API. Permission is checked again on activation;
stale/copied pairs, selection changes and revoked access cannot load a retained
pair. Without the optional inspection, legacy projections remain inert. Candidate
IDs, Proposal, base and captured target are displayed distinctly from committed
revision IDs; unsupported Scenario profiles are not guessed into this reader.

Runtime query/Preview/open/apply authorization, durable Draft persistence, concurrent
commit/idempotency/audit, broader typed aliases, incremental indexing, actual Windows
UI and shared-feature acceptance remain required future work. The ProjectIDE agent
does not implement Security-branch work; necessary runtime/independent-security
qualification is handed off in [P0 #1930](https://github.com/NYGsatoshi/Coglatas/issues/1930).
Inherited CI/Inspector work remains separate in P0 #1927. Trust/RLS, Native, Production, Numerical
Assurance and Release restrictions remain unchanged.
