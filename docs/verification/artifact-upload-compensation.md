# Artifact version upload compensation

The existing Artifact version intake path could successfully store new bytes and
then fail during version-number lookup, metadata staging, audit, notification
staging or the final save, leaving the new object behind. This repair extends
the existing FileService compensation pattern to ArtifactService.UploadVersionAsync.

Only work after successful storage acceptance is inside the compensation boundary.
A failure deletes that new server-generated key with CancellationToken.None and
rethrows the original exception, including when deletion itself fails. Success
keeps the bytes. Response projection follows the save outside the catch boundary.
The ordinary DbNotificationService.NotifyAsync path stages notification rows and
does not independently save this operation.

The seven new regressions use real LocalFileStorageService bytes and EF InMemory
repositories. They cover audit/notification/persistence failures after an existing
successful version, unchanged prior bytes and persisted current-version identity,
post-storage cancellation, failed compensation preserving the initial exception,
SHA-256 byte/metadata agreement on success, storage rejection and closed intake.
The unchanged tests against baseline Main07 produced five failures and two passes;
the repaired source produced seven passes. A forced nonincremental current-source
build and related Files/contract/storage controls produced 38 passes, zero skips,
against Mainc9c16772 and candidate668242c0. The earlier fixture-seed failures and
an incremental-build run that retained the baseline binary remain retained; they
are not replaced by later success. Hosted CI and official Sonar gates must qualify
the published candidate separately.

This is not a scanner, intake policy, authorization or transaction contract change.
FileSharing remains authoritative; the production Files upload UI stays disabled.
ScanStatusSkipped remains Skipped and is not scanner-clean evidence. The cleanup
is best effort: a delete failure can still leave an orphan. A caller-owned
transaction that later rolls back after a successful return and an ambiguous
commit outcome remain outside this repair, as in the existing FileService pattern.
No upload-enabled Pilot B or release acceptance is established. RELEASE_NO_GO.