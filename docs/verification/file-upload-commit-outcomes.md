# File upload commit outcomes

File and Artifact uploads retain newly stored bytes when persistence has committed
or its final outcome is unknown. An exception is not evidence that metadata failed
to commit. Acknowledgement loss after commit must preserve all referenced bytes.
The original exception remains primary even when rollback, disposal, cleanup or
logging fails. Warnings contain fixed text without identifiers or exception data.

The concrete EF adapter captures tracking immediately before upload writes are
staged. A pre-save admission denial can remove the new object only after pending
upload writes are discarded. Existing unchanged tracked entities retain their
identity. Existing pending caller writes or policy documents prevent this cleanup
qualification; they are never cleared to make an upload compensatable.

Without an existing, ambient or enlisted transaction, the adapter owns a transaction
for the upload save. Only acknowledged precommit rollback plus safe tracking
discard permits cleanup. Once commit starts, a thrown acknowledgement remains
unknown, including when a later rollback call returns successfully.

Inside an explicit caller transaction with savepoint support and a clean captured
tracking baseline, the adapter creates a private savepoint. It never commits,
rolls back or disposes the outer transaction. Failed persistence becomes
NotCommitted only after rollback to that savepoint is acknowledged. The tracking
discard restores captured scalar values and detaches only writes introduced by
the upload. Newly tracked unrelated reads remain tracked. Failed rollback or
unsupported/ambient/enlisted ownership retains the object. A successful save
inside the caller transaction remains Unknown until the caller decides its fate.
Loss of the private savepoint release acknowledgement also remains Unknown. It
does not trigger rollback to a possibly removed savepoint, which would abort an
otherwise usable caller transaction. The caller can still commit its saved rows.
Discard also removes introduced writes from retained navigation collections before
checking the live tracker, preventing a later save from resurrecting a failed
Artifact version after its stored bytes were removed.

The original actual-PostgreSQL File/version and composed authenticated HTTP controls
remain unchanged. They require storage compensation after a genuine permission or
selected RLS-policy INSERT failure, retain tracked authority after post-storage
revocation, and preserve the caller-owned rollback/orphan compatibility hold.
Additional provider controls exercise an error after SaveChanges accepts its tracked
state, later caller commit, failed savepoint rollback, unrelated tracked reads,
pending caller insert/update preservation, and successful caller saves. Existing
lost-commit-acknowledgement File and Artifact regressions remain required.

These synthetic controls do not qualify production RLS activation, full upload
atomicity or an orphan reconciliation service. Unknown-outcome reconciliation and
caller rollback after service success remain unresolved under #535. Main CI,
official Sonar, both full Qodana lanes and independent review must qualify each
new candidate separately. Student Council publication remains RELEASE_NO_GO.
