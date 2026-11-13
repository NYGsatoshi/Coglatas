# ProjectIDE host and time snapshot collection storage

This Product repair follows the existing #905 and #1034 exact host-input and
immutable snapshot contracts. It continues the Domain envelope repair and leaves
independent Security qualification in P0 #1938. It adds no authorization grant,
host capability, transport, schema, dependency package or runtime activation.

## Reproduced failures

Read-only wrappers exposed mutable synchronization storage. Clearing a host
profile's contract dictionary removed compatibility checks; an unsupported
Ruleset then reached actual compilation instead of being refused. Capability
array changes altered the same profile's Supports result. Request snapshot
authorization could diverge from the retained materialization and execution-input
digests, and a stale response's explanation could be cleared.

Nested calendar and timezone storage had the same problem. Clearing working,
nonworking or holiday intervals changed a six-hour measure to zero or seven
hours under the same rules digest. Removing timezone segments changed a Known
conversion to Unknown without changing the pinned rules. A resolved candidate
list could contain null while its retained Value remained Known. These are local
in-process counterexamples, not evidence of a remote exploit or production
incident.

## Owned collections

Host profiles use a framework frozen contract map and immutable capability list.
Request snapshot, unit and calendar rosters and both host freshness overloads
own immutable lists. Existing token, count, null, compatibility and exact-input
validation remains in place. No additional package or configuration is needed.

Calendars own working/nonworking intervals and exceptions. Timezone snapshots
own their segments, and pure time results own their candidate lists. The existing
Domain immutable view preserves generic mutation rejection while hiding the
mutable synchronization storage. Supplied unit definitions retain their existing
owned Dimension map and canonical data.

Ordering, multiplicity, scope claims, coverage, reasons, typed values and digest
domains retain their existing meaning. Reconstructing a request from its exposed
typed rosters must reproduce the stored materializations and execution binding.
Collection access must preserve compatibility rejection and time evaluation.
This does not make an admitted historical snapshot current authorization.

## Verification and boundaries

Ten new functional controls retain the original host and nested-time failures.
Existing host parity, materialization mismatch/freshness, calendar/DST/budget,
Windows preparation and full preview fixture controls remain part of validation.
Non-incremental builds prevent stale copied-source timestamps from reusing an
earlier assembly. Original failures and corrected discovery/TRX are kept
separately, and the complete preview bundle must remain byte-identical.

Product evidence is not independent Security approval. Current actor/session/
capability checks, persistence, lifecycle/revocation/retention and external
acquisition still require their owning runtime contracts. P0 #1938 remains open;
#1930 and #1932 keep their separate query and history responsibilities.
No Trust/Eligibility/Registry enrollment, RLS, actual data, actual Avalonia,
Native/Production/Release or numerical resumption is implied. Existing HOLDs,
#1144 BLOCKED and #1128 SUSPENDED/NOT_EVALUATED remain.
