# Workspace Channel reply mutation persistence controls

Two additional PostgreSQL Facts exercise editing and deleting a canonical
same-Conversation reply through the production `IConversationService` and
existing Workspace Channel service composition. The existing fixture and
assertions are unchanged apart from the outer class becoming partial.

The original root, reply and unrelated message identities are established
before mutation. Independent platform read contexts observe every mapped
Message and Conversation scalar with query filters disabled. Only the named
reply fields and independently bounded persisted timestamps may change; the
root, unrelated message, keys and other scalars remain unchanged. PostgreSQL's
microsecond timestamp truncation is explicitly allowed at the lower boundary.
Both persisted non-reply messages independently retain the expected Tenant,
Workspace, Conversation, author, null thread-root relationship and version.

The post-save read requires two new stored Outbox events: the exact versioned
Message edit/tombstone and metadata-only Thread refresh. Tenant, actor, subject,
schema, routing and payload identities are checked. The Thread refresh retains
the durable reply count/latest creation time, requests authorized refetch and
contains neither reply text nor the actor display name. Deletion retains a
bodyless reply tombstone. One new persisted communication Audit row has the
expected action, actor, Tenant, resource, decision and reason. Every previously
stored Outbox/Audit row's mapped scalars and identities are retained.
Envelope event/schema/aggregate fields are checked independently of the stored
columns against the original identities and expected versions. The Thread
payload must contain exactly its six declared metadata properties; additional
properties cannot pass the content-exclusion assertions unnoticed.
The edit event's `updatedAt` and delete event's `deletedAt` independently match
the corresponding persisted mutation timestamp, allowing only the zero-to-nine
ticks removed by PostgreSQL's microsecond truncation.

The existing PostgreSQL harness requires an explicit test connection and
creates an isolated migrated temporary database. The original raw Main report
selected previously unvisited reply-mutation branches; it does not forecast
coverage gain. Supported hosted compilation, named PostgreSQL execution and
source/run/attempt/original artifact binding remain mandatory. No local .NET,
PostgreSQL or WSL execution is claimed.

The actor adapter is synthetic. These positive persisted-state controls do not
establish HTTP/cookie behavior, competing revocation or write failure atomicity,
caller-owned transaction rollback, actual SignalR dispatch/refetch, full payload
redaction, deployed Application Role/all-table RLS, File/Artifact reconciliation
or full Recovery. Navigation and other tables are outside the scalar snapshot.
Historical MSG/Thread/P0/HTTP500 causes remain UNKNOWN; no issue closure or
release acceptance follows. Product, migrations, authority, producers, raw
reports, coverage scope, thresholds and budgets remain unchanged. Integration
must follow the existing #1889 and #1891 resulting-Main evaluations and all
fresh protected merge conditions. Pilot status remains NO_GO.
