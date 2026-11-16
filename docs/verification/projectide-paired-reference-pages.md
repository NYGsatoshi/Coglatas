# Paired local reference pages

This partial #1030 / #782 Product slice adopts the bounded Core query from
#2014 in the Windows pure ViewModel and the Angular offline preview. It follows
#888 / A-R2's shared Semantic Index direction. It introduces no runtime endpoint,
serialized cursor, Registry contract, dependency, CI change or new authority.

## Windows pure ViewModel

After explicit current root Source admission and selection of a mapped decoded
symbol, ReadReferenceSearchPage calls the actual Core FindReferencesPage API.
Each page keeps the original references, canonical order, input digest and Core
coverage/scan axes. The genuine in-process cursor remains owned by its inspection
and target. A current page and an actual member are required to open an origin;
the Inspector symbol anchor remains selected. Every returned origin requires the
current original Source map. Mapping gaps expose no partial page.

Selection, Source re-admission, result/context replacement, reset, disposal,
cancellation and synchronous Changed re-entry invalidate old publications and
page-owned navigation. A newer query or ordinary navigation wins over an older
callback. Index coverage, scan exhaustion, Source mapping and evidence completion
remain separate. The existing unpaged query is unchanged.

## Angular offline preview

The exporter adds one reference-pages case to the existing default fixture.
Its 35 distinct Requirement declarations reference the actual root Project.
Real Compiler output and FindReferencesPage calls produce the 32 + 3 pages;
no reference is synthesized by the browser. The original first 31 cases and
bundle header remain byte-identical, including their source/run identities.
Other exporter profiles and retained recordings are unchanged.

An optional local referenceSearches projection contains the target key, exact
input digest, per-page scan/coverage axes and indices into the actual canonical
inspection reference array. It contains no cursor or permission assertion.
Admission verifies every page against the Core's 32-result / 256-scan bounds,
including original order, exhaustion and complete index membership. The opt-in
profile retains the existing browser's 4096-reference ceiling and admits at most
32 target groups. The case is owned immutable data after full bundle binding;
its object graph is limited to 256 levels / 100000 objects / 16 MiB. Omitting
the optional inspection keeps this projection inert, preserving the existing
legacy read contract without inferring symbols or references. Existing
legacy cases remain unchanged and retain their original query limits.

Only admitted current pages and their actual original members can navigate.
An explicit retained root Source read is required. Re-admission, selection,
result/context changes and permission loss withdraw pages and their owned
navigation. Previous/next controls retain the symbol anchor, clear the prior
origin detail and place focus on the newly displayed current page. Deferred
focus cannot reclaim focus after newer navigation or clearing. Both English
and Japanese keep continuation and local scan completion separate from missing
Compiler evidence. The fixture remains partial and does not become PASS.

## Verification and remaining scope

Native regressions use 35 actual distinct Requirement origins and cover
continuation, foreign ownership, bounds, cancellation and Changed re-entry.
Windows and Ubuntu strict builds and the full original inventories are checked
separately. Both hosts generate byte-identical preview output. Browser unit,
component and real Chromium desktop/mobile journeys exercise original Source
admission, 32 + 3 origins, keyboard Enter/Space, current focus, lifecycle cleanup,
accessibility and absence of modifying requests. Execution status and exact
source/test hashes belong to the current PR evidence; unexecuted checks are not
PASS.

This is offline Angular and pure ViewModel adoption. Actual Avalonia acceptance,
persisted/incremental indexes, runtime current-actor query/open authorization,
collaboration and full #1030 editor acceptance remain separate work. Matching
hashes, index counts and local read permission grant no server authority.
Independent Security qualification and Registry/Trust/Eligibility/RLS/Native/
Production/Release holds remain. #1144 is BLOCKED; #1128 is SUSPENDED/
NOT_EVALUATED. Wider parents remain OPEN. Security/shared CI work is handed to
P0 issues under the Owner's current ProjectIDE-only scope.
