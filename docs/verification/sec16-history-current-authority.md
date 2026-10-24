# SEC-16 ordinary-host initial-version read authority

Related: #1349. This test-only dependency is based on reviewed `f2070136`
(Draft #1357), not resulting Main. Its seventeen existing File facts and helpers
remain unchanged. Two new explicit facts cover the public Activity and
`GET /api/files/{fileObjectId}/versions/{versionId}/content` routes with the
actual Web host, migrated PostgreSQL, cookie/CSRF sessions and physical bytes.

Only immutable native **initial version 1**, captured by the existing upload
trigger, is exercised. No replacement, synthetic history writer, version-2 row,
new API or lifecycle policy is introduced. These tests do not prove that an
older version survives a later content replacement.

The membership fact caches the actual Activity version path, proves Member and
Owner byte reads, removes the Member through the Owner's real HTTP endpoint,
and verifies persisted Workspace suspension while Tenant membership and the
original session remain active. Both cached content and Activity must return
the exact established canonical 400 `FILE_NOT_FOUND` envelope. Owner reads
remain positive. Actual membership PATCH restoration permits the same cached
path and bytes again without a new login.

The binding fact uses different physical bytes for two private Files in the
same Workspace and one in a foreign Tenant. An independently authorized Owner
reads both local resources before their version identifiers are exchanged;
both substitutions must fail. An unauthorized ordinary Member cannot read the
other private File. Cross-Tenant Activity/content requests fail in both
directions while each actor retains its own positive byte reads.

Immediate pre/post-denial digests include File, Attachment, sharing, grant,
successful grant-use, native version and selected File-audit state. Current
history reads create no such effects; unrelated session/audit state is outside
that comparison. Safe envelopes, `no-store`, MIME type and metadata/audit
nondisclosure assertions retain protected content only in memory.

Existing unit tests cover a fake version/storage oracle. Draft composed-RLS
tests separately exercise selected native-version contexts and compatibility
HOLDs; these ordinary-host tests neither import those policies nor claim their
approval. Local qualification requires an exact clean committed Release build,
nineteen named File facts with zero failed/skipped/not-executed, seven
canonical/test-directory assembly byte/version pairs, real PostgreSQL privacy
and final TCP/database/version receipts, and owned stopped/retained fixtures.

This is not full history, role/scope/batch/placement/eligibility or SEC-16
coverage. No storage-open spy, concurrency linearizability, target HTTPS,
RLS activation, #582 finding waiver or release qualification is claimed.
Both Owner HOLDs, held #1320/#1326 and #1342/#1346/#1353/#1355 remain unchanged.
