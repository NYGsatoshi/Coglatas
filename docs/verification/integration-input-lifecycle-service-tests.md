# Integration input and lifecycle service controls

The additive Application tests use the existing Integration fixture and real
service implementation. Invalid settings, nested sensitive-key names, webhook
inputs, token scope shape/size and expiry boundaries reject before any new
repository state, audit entry or unit-of-work save. Invalid updates preserve the
complete selected response record. Positive Integration and Webhook lifecycles
check normalization, null-field retention, updates, soft-deletion visibility,
repeated denial and scoped audit/save counts. Token use checks the stored hash,
returned identity/scopes, last-use timestamp and repeated revocation timestamp.
The repeated-revoke control seeds a distinct earlier stored timestamp before
the second command. Webhook null updates start with non-default events/status
and a generated synthetic secret, and check its stored hash. Rejected creates
also check unfiltered repository lists; audit checks include entity types.
All data is synthetic. The webhook validation command sends no outbound request.

The original tests and assertions are preserved. The fixture only gains audit
and save observations; its original successful return behavior remains intact.
These repository doubles do not exercise SQL, tenant filtering, persisted-current
authority, HTTP, real webhook delivery, failure atomicity, RLS, file reconciliation
or full Recovery. The token identity assertions compare the repository record;
they do not establish tenant stamping or authentication middleware integration.

Actual hosted compilation and every named case, required CI, original official
PR Gate and independent source review remain required. Local .NET is not used.
No product, authorization, schema, coverage scope, quality threshold, finding
disposition, Owner HOLD, promotion or release decision changes. Resulting Main
coverage must be measured separately; test counts are not a coverage prediction.
