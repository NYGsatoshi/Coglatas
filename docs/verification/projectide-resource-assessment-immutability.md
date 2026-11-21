# ProjectIDE Resource assessment diagnostic storage

This bounded #905/#1034 Product correction preserves input-specific diagnostic
binding for an admitted Resource reference assessment. It changes collection
ownership without acquiring a Resource or changing resolution or freshness.

## Original counterexamples

The public Findings wrapper exposed its array through ICollection.SyncRoot.
Replacing the unavailable-snapshot finding with one from another admitted
revision changed its diagnostic InputId and location context while the
assessment's Reference and Binding still described the original Source.

Two clean controls, using distinct other revisions, fail at exact diagnostic
input identity. Resolution remains Unsupported and freshness Undetermined
before and after access. No successful Resource lookup, reservation, permission
or materialization is inferred from this local report inconsistency.

## Owned results and verification

The existing ImmutableListView owns the ordered findings. Original input IDs,
typed locations, canonical diagnostics, missing-snapshot reasons, ordinal
sorting, resource kind/pin checks and cancellation/budget behavior keep their
existing meaning. Public access cannot substitute another assessment's rows.

A non-incremental build precedes corrected execution. All569 cases pass with no
failed or skipped cases: 549 prior paired/core/numeric cases, 18 existing
Resource reference validation cases, and two new ownership controls. All18 Core
fixture exports remain byte-identical. Original failures and corrected receipts
are retained separately.

This remains pure structural comparison. It does not fetch a Tenant Resource,
prove reusable/consumable capacity, make a reservation or establish current
authority. Independent qualification remains P0 #1938; parent work stays open.
No Security branch edit, synchronization or merge is performed.

No new role, schema, dependency, Registry/Trust/Eligibility enrollment, RLS,
external/actual data, actual Avalonia, Native/Production/Release or numerical
resumption. #1144 remains BLOCKED and #1128 SUSPENDED/NOT_EVALUATED.
