# Scoped Realtime connection invalidation controls

The production `RealtimeConnectionInvalidator` removes registry subscriptions for
the selected Tenant/User before awaiting external group cleanup and sends an
empty `AuthorizationInvalidated` control to the affected connections. The new
tests exercise the real registry and invalidator with strict SignalR dependency
doubles. Production source and every existing test remain unchanged.

Twelve native controls cover all five subscription types, multiple sessions and
connections, another User in the same Tenant and the same User in another Tenant,
exact original group removal, per-subscription removal-before-notification order,
empty control payloads and cancellation-token propagation. Unknown identities
must have no effect. Repeated invalidation is quiet until a later registration.
The asynchronous control holds group cleanup in progress and verifies that all
target registry authority is already removed while other identities are retained.
An additional mixed-type control requires simultaneous removal of Tenant, User,
Workspace, Project and Conversation subscriptions on the same connection.
The pending-cleanup test races entry against early invalidation completion,
bounds waits on returned tasks, and releases the fake in `finally`. These waits
do not bound synchronous blocking before a task is returned or cancel a task
that exceeds the wait. Tokens are captured as values rather than retaining a
disposable source in callbacks; cancellation uses the asynchronous API.

Group-removal failure, client-send failure and cancellation propagate without
restoring removed registry entries. The tests preserve that current contract;
they do not add a cleanup retry or claim every external group/client was notified
after a failure. Initial compilation of the nested recording proxy failed with
CS9105; recording through the owner corrects that test-only error. The retained
failed build is not a behavioral counterexample or a passed test run.

These isolated synthetic registry records do not establish the validity of
subscription enrollment, HTTP/session admission, persisted membership revocation,
live SignalR delivery, reconnect recovery, application-role RLS, cancellation
behavior of a real transport, or revocation atomicity through an in-flight send.
Selected native coverage is not an additive Main coverage forecast. Current-head
CI, original official PR Gate, independent source review and complete qualification
of each normally merged Main remain separate requirements. No budget, baseline,
threshold, assertion, source exclusion or authority contract is changed, and no
formal finding disposition, Pilot acceptance or Release GO is granted.
