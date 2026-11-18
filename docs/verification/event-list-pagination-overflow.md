# Event list pagination bounds

The Event list normalizes page and size, retrieves filtered candidates, and
applies view authority before paging the visible events. Its previous 32-bit
offset could wrap a huge positive page back into that visible list. With five
visible events, page 1,073,741,825 and size four returned the first four events
instead of an empty page. Negative and small positive wraps also selected rows.
This is a paging defect after admission, not an authorization-bypass finding or
evidence of a historical scanner failure's cause.

The offset now uses 64-bit multiplication after the existing normalization and
is bounded by the visible count before conversion to the integer accepted by
`Skip`. Authentication, current view authority, candidate filters, repository
order, normalized page metadata, authorized total, selected-ID attendance counts,
DTO projection, and the existing read-only path remain unchanged. The controller
still forwards `EventListQuery` to the same service contract. The production
repository orders by `StartsAt`, then `Title`; no ordering change is introduced.

Fourteen new cases use the actual Application service and existing repository,
identity and authority doubles. The original thirteen cases remain unchanged.
Four large-page cases reproduce negative, zero
and positive wraps. Six ordinary cases retain first/middle/final/empty pages,
page/size clamps, visible order, dates, titles and attendance counts. A foreign
Workspace ID with a missing parent is interleaved in candidate order. An unauthenticated control
retains the existing failure, and two empty-list controls distinguish no
candidates from existing events hidden from an authenticated nonmember. Event
and attendance collection counts remain unchanged. The original test class gains
only `partial`; its existing test bodies, assertions and doubles are unchanged.

A separate fourteenth case registers an existing active foreign Workspace and
its administrator membership. It verifies that the ordinary viewer has no
membership there, then checks the authorized total and second page across that
interleaved foreign event. The missing-parent fixture and its original immutable
failure evidence are retained separately from this existing-parent control.

The immutable test-only revision's four failures and nine passing controls are
retained separately from repaired native execution. Source/binary/native-report
proof, independent review, required checks, original official PR Gate and exact
resulting-Main qualification remain distinct. Doubles do not establish actual
PostgreSQL ordering, HTTP, concurrent admission, RLS, deployed/physical pilot
acceptance or Release GO. No report import/rewriting, source exclusion, threshold,
baseline, Owner hold or formal finding disposition changes; local coverage is
not a Main improvement forecast.
