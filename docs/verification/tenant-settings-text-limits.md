# Tenant settings persistence limits

Main741 first-attempt Security runtime failed its alpha-owner Schemathesis
fuzzing case `maqXLt`, seed715069838, on `PATCH /api/tenant/settings` with vendor
JSON. An oversized `defaultLocale` reached PostgreSQL `varchar(20)` and returned
HTTP500/SQLSTATE22001. The other three scanner roles, SEC05's35 authorization
cases and all three ZAP roles passed. The original Main attempt and raw evidence
remain retained; passing Qodana and official Sonar execution are separate results.

The existing settings validator now rejects normalized locale text exceeding20
Unicode scalar values and timezone text exceeding80, matching their existing
PostgreSQL column bounds. Counting is bounded and uses the text after the same
trim applied for storage. Supplementary-plane characters, padded valid values
and omitted fields retain their original persistence behavior. No new culture
or timezone allowlist, migration, schema suppression or scanner exclusion is
introduced. Authorization still runs first; invalid settings cannot stage
settings or audit mutations.

Three real PostgreSQL controls cover service rejection, exact stored boundaries
and Unicode/trim/null preservation, and loopback vendor-JSON HTTP400 versus500,
unchanged settings/audit snapshots, valid update and current-member403. The
clean test-only7ea2b78b run reproduced2FAIL/1PASS before implementation. Original
tests/assertions, authentication, Tenant authority, RLS and quality policies
remain unchanged. Synthetic Development HTTP does not establish deployed HTTPS,
production database or Student Council Pilot acceptance.
