# Scoped Outbox dispatch failure controls

The added controls exercise the existing dispatcher batch through the same
private-method test entry used by the original Task V1 suppression controls.
Production source, existing tests, rollout flags, coverage configuration and
quality thresholds remain unchanged. Actual dependency-injection scopes and
the subscription registry are used with strict repository, authorization and
SignalR doubles; this is not live transport or PostgreSQL persistence evidence.

The cases require exact claimed event/lock/token preservation, platform scope
for claims and Tenant scope for recording outcomes, feature-disabled release,
no action on an unlocked claim, and rejection of malformed or identity-mismatched
stored envelopes before authorization or transport. Transient authorization,
send and delivery-acknowledgement failures retain bounded exponential/jittered
retry arguments and fixed persisted diagnostics. JSON and invalid-operation
contract failures do not schedule automatic retry. Host cancellation propagates
without recording false failure/delivery; unrelated cancellation remains retryable.
Duplicate targets send a single authorized envelope and do not reach a foreign
Tenant's connection. Broad SignalR sends and unused repository operations fail.
Claim identities are snapshotted when returned, independently of the mutable
event row. Non-default tokens must reach claims, authorization, sends and outcome
calls. Terminal-failure controls require the injected send attempt and assert
recorded unexpected transport operations outside the dispatch exception handler.
Authorizer arguments and send tokens are also recorded first and validated after
the dispatch task completes or throws, so assertion failures cannot be mistaken
for the injected retryable fault.

A transport send followed by lost delivery acknowledgement is deliberately
recorded as retryable. These controls do not assert exactly-once delivery,
successful persistence after an acknowledgement loss, durable replay, completed
cleanup, background worker-loop recovery, production RLS or complete Realtime
acceptance. The original suppression/replay tests remain required separately.
Native collector coordinates and resulting Main Sonar counters must be qualified
independently; local point counts are not an additive Main coverage forecast.
