# Forms question lifecycle controls

`FormServiceTests.QuestionLifecycle.cs` adds 19 cases using the existing Forms
Application fixture. Existing tests, fixture, assertions and product sources
remain unchanged.

Eight invalid-input cases each execute create and update. They check exact
validation errors, the complete selected question scalar snapshot, unchanged
question cardinality, empty response collections and zero audit/save calls.
Missing-form and unauthenticated mutations exercise all three modifying APIs.
Two foreign-question cases retain both selected and foreign question snapshots.
An answered question retains its response/answer identities and contents after
type-change and deletion refusals. Positive lifecycle controls compare DTO and
stored text/type/required/order/choice values, omitted choices and conversion to
text, then deletion. An unloaded navigation control exercises repository lookup
for update/delete and refusal of a second deletion.

Each successful update captures the question ID before calling the service and
compares both the selected stored object and returned DTO against that immutable
value. Existing DTO-to-object comparisons and all earlier assertions remain.

The fixture stores objects in memory. Audit observations count calls without
establishing their identity or persisted contents. These tests do not establish
PostgreSQL behavior, persisted-current account/membership authority, HTTP denial
privacy, concurrent revocation, transaction/effect atomicity, delivery, deployed
roles/RLS, File/Artifact reconciliation, full Recovery or pilot acceptance.
The existing Forms authorization implementation and its separate authority
tests remain in place.

The candidate is uncompiled and unexecuted until its exact-source hosted run is
qualified. Record the source SHA/tree, tested merge parents, run/job/attempt and
original log/artifact digests. Require the six checks, original official PR
Gate, independent source inspection and live protection/thread/HOLD checks.
After protected integration, measure actual resulting-Main full scans and
Coverage denominator/covered-line/condition deltas separately; no projected or
summed Coverage gain is claimed. Historical failures remain retained. No local
.NET execution, quality-policy change, formal disposition or Release GO occurs.
