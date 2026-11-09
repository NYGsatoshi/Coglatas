# Task scope override clearing controls

The additive Application service tests exercise the existing override-clear
entry point with the existing isolated EF InMemory fixture. Negative versions,
empty/missing/deleted/foreign-Tenant tasks and fixture-denied management retain
the selected raw stored Task/override fields with no save, audit or Outbox effect.
Stale existing versions and nonzero versions for an absent override also deny
before mutation. An absent override with expected version zero returns the
inherited policy without saving.

The successful control uses versions above the 32-bit range and distinct Project
and override policies. It checks removal, inherited response, Task version,
exact audit identity/metadata, one persisted invalidation and selected payload
fields. Repeating the clear with version zero preserves selected stored fields
and adds no save, audit or event. The existing synthetic conflict unit of work
qualifies failure translation and unchanged selected stored state. Its audit
double still records the staged call; this does not prove effect atomicity or
rollback of audit/Outbox/policy documents in a real transaction.

These are InMemory service controls with fixture management authority. They do
not establish PostgreSQL SQL/constraints, source-policy document deletion,
persisted-current account/membership admission, HTTP privacy, concurrent
revocation, actual dispatch, all-table RLS, File/Artifact reconciliation or full
Recovery. Product logic, quality and coverage contracts, all original test
methods/assertions and Owner decisions are unchanged. Hosted compilation and
all named cases, required CI, original PR Gate and independent review must
complete before integration. Every resulting Main needs separate measurement;
no local or projected Main coverage improvement is claimed.
