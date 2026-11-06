# Tenant settings persistence limits

Main741 first-attempt Security runtime failed its alpha-owner Schemathesis
fuzzing case `maqXLt`, seed715069838, on `PATCH /api/tenant/settings` with vendor
JSON. An oversized `defaultLocale` reached PostgreSQL `varchar(20)` and returned
HTTP500/SQLSTATE22001. The other three scanner roles and all three ZAP roles
passed. SEC05's original JSON reports37 passing cases; its console summary
reports35. Both original records remain retained without reconciliation by
rewriting. The original Main attempt and raw evidence
remain retained; passing Qodana and official Sonar execution are separate results.

The existing settings validator now rejects normalized locale text exceeding20
Unicode scalar values and timezone text exceeding80, matching their existing
PostgreSQL column bounds. Rune counting examines at most21 or81 scalars and
uses the text after the same trim applied for storage. Trimming and UTF-16
validation still inspect input text. Malformed UTF-16 locale/timezone values
from in-process callers are rejected before applying settings; an added
PostgreSQL regression first reproduced Npgsql's encoding exception on a lone
surrogate. Supplementary-plane characters, padded valid values
and omitted fields retain their original persistence behavior. No new culture
or timezone allowlist, migration, schema suppression or scanner exclusion is
introduced. Authorization still runs first; invalid settings cannot stage
settings or audit mutations.

Four real PostgreSQL controls cover service rejection, malformed UTF-16 rejection,
exact stored boundaries
and Unicode/trim/null preservation, and loopback vendor-JSON HTTP400 versus500,
unchanged settings/audit snapshots, valid update and current-member403. The
clean test-only7ea2b78b run reproduced2FAIL/1PASS before implementation.
The first implementation run retained2PASS/1FAIL: HTTP400 and Unicode boundary
controls passed, but the new service control created the shared fixture twice
inside one database and collided on its default normalized email. It now reuses
one fixture for both rejected updates; every rejection assertion is retained.
Original tests/assertions, authentication, Tenant authority, RLS and quality policies
remain unchanged. Synthetic Development HTTP does not establish deployed HTTPS,
production database or Student Council Pilot acceptance.
