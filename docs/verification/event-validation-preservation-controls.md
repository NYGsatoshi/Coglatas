# Event validation and normalization preservation controls

The additive partial suite adds 21 cases to the existing Event lifecycle
fixture. Its original source changes only the outer declaration to `partial`;
every original assertion and fixture implementation remains unchanged.

Eleven rejected creates cover scope cardinality, required title, chronological
bounds, deadline, capacity and missing/archived/deleted Workspace validation.
Six rejected updates retain every selected Event scalar, including inherited
creation/update/deletion metadata. Both paths compare pre-operation tracked
and independently materialized InMemory-store snapshots, retain the original
Event identity and isolated table cardinality, and require no Audit or
Attendance rows, notification calls, saves or pending tracked changes.

Two successful creates cover a deadline exactly equal to the start, zero
capacity and blank versus trimmed optional fields. They independently compare
DTO expectations, stored fields, the untouched original Event, audit actor,
action, entity and Tenant, and a single save. Two successful updates distinguish
omitted optional fields from explicitly blank fields, retain parent/creator and
deletion metadata, and compare stored and response values plus audit and
notification identity. Existing status transitions and destination authority
controls remain in their original suite.

These use EF InMemory and recording notifications. They do not establish
PostgreSQL transactions, current persisted authority, HTTP privacy, delivery,
exception atomicity, application roles/RLS, File/Artifact reconciliation,
full Recovery or pilot acceptance. No authorization, status-transition rule,
quality budget, coverage scope or original report is changed.

Exact-source compilation, named hosted execution, analyzers, required checks,
original official PR Gate, complete independent review and live merge guards
remain pending. The resulting Main requires its own full qualification and
actual denominator/covered-line/condition comparison. No local .NET execution,
projected or summed Main Coverage gain, formal disposition or Release GO is
claimed. Historical failures and Owner HOLDs remain unchanged.
