# Interrupted local file storage

Related: Student Council Pilot #615; existing Files / FCI-05 contracts.

`LocalFileStorageService.SaveAsync` previously created or truncated the canonical
storage key before copying the input. An interrupted input could leave partial
bytes without metadata; a failed replacement could destroy previously committed
bytes. Application metadata compensation starts only after storage succeeds.

The adapter now copies into a unique sibling temporary file, closes the output,
checks cancellation and moves the complete file over the canonical key. Copy,
disposal, cancellation and publication failures delete the temporary file. If
cleanup itself fails, an aggregate exception retains both failures. Successful
replacement retains the existing overwrite behavior. The caller owns the input.

Regression tests observe the canonical key during copying, inject input failure
and cancellation with and without existing bytes, compare successful replacement
bytes, reject cancellation at end-of-input and verify publication-failure cleanup.
A directory destination raises IOException on Linux or UnauthorizedAccessException
on Windows; both must preserve the destination and leave no temporary bytes.

The first four regressions failed against the original adapter (6 existing/positive
passes, 4 failures). After the adapter repair, those ten cases passed. The first
expanded run had 11 passes and one test-oracle failure because Windows reports
UnauthorizedAccessException for a directory destination. The oracle now accepts
only those two platform filesystem errors and still verifies complete cleanup.
Final source-bound results belong in the PR and #615 checkpoints.

No intake feature, scanner status, privacy policy, authorization, schema, RLS,
API contract or FCI-05/06 acceptance is changed. UI intake remains unavailable.
This is not crash/power-loss durability, a cross-DB/storage transaction, orphan
reconciliation after process termination, or full recovery qualification. Empty
run-owned directories can remain after failed writes. A filesystem that denies
cleanup remains a failure; it is never reported as a successful upload.
