# Owned pass Source target storage

This #782 / #1034 Product correction preserves the existing selected-original-map
navigation contract. It changes the Windows pure ViewModel target collection's
storage exposure; actual Avalonia and current runtime authority remain separate.

## Reproduced behavior

On the retained .NET 10 runtime, `Array.AsReadOnly` exposes its original array
through the non-generic `ICollection.SyncRoot` surface. A caller could replace
one `SyntheticPassSourcePage.Targets` entry with a target from another actual
recorded invocation with the same Source. The page's reference-membership check
then accepted that foreign object. This is a local page-ownership defect; it does
not establish an unauthorized server read, current actor permission or activation.

Two real-page regressions fail against the previous implementation: the original
entry changes through legacy storage, and the foreign run's target becomes
navigable after that change. The preceding 28 focused cases still pass. Original
failed TRX/logs and the runtime storage probe remain retained.

## Corrected contract

The page now owns a copied private array behind an `IReadOnlyList` implementation
that exposes only count, indexed reads and enumeration. No generic mutator or
non-generic synchronization-storage interface exposes that array. Indexed bounds
keep the existing argument-out-of-range behavior. Targets themselves retain their
original instances, order, identities and Source locations.

The public property type, map/record digests, Source/run/boundary ownership,
explicit read requirements, page bounds, cancellation and lifecycle guards remain
unchanged. A foreign target remains rejected after attempted storage mutation;
the original target still navigates. Existing selection and invalidation behavior remains covered by the preceding
regression cases.

No Compiler/reader/artifact schema, fixture, dependency, Angular source,
CI/Security branch, protection or threshold changes are included. Current query,
open and retention authority, independent Security, actual Avalonia,
Registry/Trust/Eligibility/RLS/Native/Production/Release acceptance stay separate.
#1144 remains BLOCKED; #1128 SUSPENDED/NOT_EVALUATED; wider parents remain OPEN.

Execution results and immutable source/TRX/SARIF mappings are recorded on the
current Product PR rather than treating this behavioral contract as a CI receipt.
