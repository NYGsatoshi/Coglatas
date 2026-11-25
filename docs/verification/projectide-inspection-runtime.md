# Development model profiles and Compiler output capture

The Application service supports exact fixed model profile query and export,
plus bounded Compiler output capture, query, export and deletion. Each action
requires its own operation authorization and a current retention and disclosure
policy decision. The shared viewer reader rechecks the persisted session, account
and Tenant membership before disclosure. Capture and deletion also require the
storage adapter to reauthorize before commit.

The model profile retains the original complete or recorded-prefix state and
aggregate virtual tick metrics. The response excludes trace events, Source and
participant identities. It admits only the exact original run and root Source.
Cross-region contributions require independent admission before they can be used
by this service.

Compiler capture binds the original invocation to the shared PostgreSQL evidence
receipt and enforces the requested byte limit over the original snapshot. The
redacted capture retains the original execution, coverage, verdict and evidence
axes with fixed output availability flags. It omits generated bodies, Source maps,
identifiers, diagnostic text and recording digests. External materializations
require independent authorization before this service can admit them. An exact
request can replay its existing capture. Deletion must prevent that replay from
restoring the capture.

The development PostgreSQL store retains the redacted projection and bounded
request metadata under a scoped reference to immutable Compiler evidence. An
atomic tombstone removes the projection, byte bound, binding digest and original
run link. The tombstone preserves the scoped capture identity so a retry cannot
restore a deleted capture.

Development hosts supply their approved retention and disclosure policy and an
original recording provider. Lifetime and purge policy, Web and Native consumers,
and independent operational qualification remain open under #1962 and #1966.
