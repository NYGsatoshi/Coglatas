# Form list pagination bounds

The Form list applies current view authorization before paging its visible
repository results. Its previous 32-bit offset multiplication could wrap an
out-of-range page back into that visible list. With five visible Forms, page
1,073,741,825 and size 4 produced offset zero and returned the first four items
instead of an empty page. Negative wrapped offsets also restarted at the first
item, while a small positive wrap could incorrectly return a later item.

The offset now uses 64-bit multiplication after the existing page and size
normalization. It is bounded by the visible list count before conversion to the
integer accepted by `Skip`. The normalized page/size metadata, authorized total,
repository ordering, current actor and scope checks, and read-only behavior remain.
No additional query or persistence operation is introduced.

Eleven new cases use the real Application service with its existing repository,
identity and authority test doubles. Four large-page cases cover negative, zero
and positive wraps; six ordinary cases retain first/middle/final/empty pages and
the existing lower-page and size clamps. The shared fixture includes a foreign
Workspace parent that remains excluded. Unauthenticated admission still fails,
and no audit or save is emitted. Original test bodies and assertions are unchanged.

The immutable test-only revision and original failed TRX, native report, log and
binary identities are retained separately from repaired execution. Native tests,
independent review, required checks, the original PR Gate and exact resulting-Main
qualification are separate conditions. Repository doubles do not establish real
PostgreSQL, HTTP, concurrent authority, RLS, deployed/pilot acceptance or Release GO.
No coverage import, source exclusion, threshold, baseline or formal finding
disposition is changed, and local counters are not a Main coverage forecast.
