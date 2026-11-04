# Core Slice 2e: pinned calendar and time contracts

Parent: #905; execution #798; owner decision #888 / specification PR 300.
This is a pure Domain candidate, not Core or UI acceptance. Product and tests
remain separate; no Main, integration or SecurityTestImplimation merge is implied.

`ProjectTimeValues` retains checked 100 ns elapsed ticks, Gregorian dates,
canonical UTC instants, distinct working durations and period components. Civil
date-time fields use exactly seven fractional digits with no offset. Calendar
references retain the existing wire shape.

`TimeZoneSnapshot` consumes a finite ordered contiguous table of UTC half-open
intervals and exact signed tick offsets. `TimeZoneReference` pins the zone ID,
rules version and domain-separated SHA-256 of the entire canonical snapshot.
No OS timezone database, current clock, network or ambient culture is consulted.
Snapshot validation establishes input consistency, not authorization or actual
manifest availability. Providers must supply authorized fixed inputs.

Resource bindings accept an optional pinned CalendarReference; Task working
durations carry that same calendar contract. Both retain strict unknown-member
handling and do not load snapshots or reserve real resources.

Civil-time resolution tests every applicable segment. A gap remains Invalid;
an overlap retains all candidates as Ambiguous. Only an explicitly supplied
matching offset selects a candidate. Missing finite coverage stays Unknown.
An unsupported rule member stays Unsupported with its original canonical data.

`CalendarSnapshot` binds calendar ID/version/digest, a pinned timezone reference,
finite validity, materialized working/nonworking intervals and explicit holiday,
nonworking or working exception intervals. Working-time measurement takes the
union of work and subtracts the union of exclusions with checked integer ticks.
Conflicting working/nonworking exception precedence remains Unsupported.
There is no recurring-rule expansion or host holiday lookup.

`ProjectTimeInterval` compares identical time domains and civil calendar contexts
using half-open intervals. Empty intervals do not overlap. Unknown endpoints
stay Unknown and Null stays Null. A working duration never becomes elapsed
duration without its calendar. An explicit Gregorian `reject-invalid-day`
period policy applies years/months, then days to an anchor; invalid month-end,
date range and integer overflow fail rather than clamp or wrap. This civil
component calculation requires unique midnight boundaries for both civil dates
under the fixed zone input and full date coverage within calendar validity;
missing or ambiguous coverage remains Unknown. It does not convert a period to
elapsed seconds or schedule work outside a calendar validity interval.

Processing limits are deterministic provisional safeguards, not approved user
quotas or performance guarantees. Exhaustion is explicit ResourceExhausted.
New recurrence languages, implicit DST policies, period rounding/clamping,
unanchored elapsed conversion, zone database distribution, snapshot loading,
resource reservations and scheduling engines remain outside this candidate.

The Source codec supports local date-time, zoned civil time and comparable time
interval values. Interval endpoints are restricted before child decoding to
instant, local date or local date-time scalars; nested intervals/collections and
unresolved zoned civil endpoints remain Unsupported. The formerly held incomplete
zoned shape is now Invalid under the implemented pinned-rules schema.

Final fixed-SHA combined verification is recorded in the PR and aggregate Slice
2 evidence. Passing these Core tests
does not accept Web UI, Windows/Avalonia, SEC-ARCH, RLS, Shadow/Enforce, school
publication or release. Pre-Avalonia gates and #1128 suspension remain unchanged.
