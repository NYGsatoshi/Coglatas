# Artifact version upload compensation

## Upload save outcome and compensation boundary

File and Artifact uploads now use an operation-local save outcome. Compensation
deletes the newly accepted object only when persistence reports NotCommitted and
successfully discards the failed upload's pending metadata and policy documents.
An opaque persistence implementation defaults to Unknown. Committed and Unknown
outcomes retain the stored bytes and emit the fixed warning event 53502 without
identifiers, paths or exceptions. A failing log provider preserves the original
upload failure. Existing deletion-failure event 53501 remains unchanged.

EfUnitOfWork owns an upload transaction only without an existing, ambient or
enlisted transaction. It keeps the existing policy-document save path inside that
transaction. An acknowledged commit establishes Committed before disposal. Only
a failure before commit starts followed by acknowledged rollback and successful
pending-write discard establishes NotCommitted. A commit-start failure remains
Unknown even if rollback subsequently returns. Rollback and disposal errors do
not replace the initiating failure. Caller-owned and nonrelational saves retain
Unknown rather than claiming the eventual transaction outcome.

The original two regressions at 9fa1748f use owned PostgreSQL 18.6, real service
and storage implementations, a second verification context, and synthetic
acknowledgement loss after a real commit. Both first attempts failed because
durable metadata referenced the newly deleted object. They preserve their source
and original evidence. This is a controlled lost-acknowledgement scenario, not a
reproduction of a production network failure. Fresh correction qualification is
required separately.

Conservative retention can leave an orphan when a transaction actually rolled
back without conclusive evidence. Reconciliation of unknown outcomes and a
caller-owned transaction's eventual rollback remain operational follow-up work;
this change does not establish full #535 completion, G01 acceptance or release GO.

## Historical compensation qualification

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
## Explicit post-cancellation observation tokens

Main2fad CI41037948042/1 retained a Qodana rule-ratchet failure:
MethodSupportsCancellation increased from budget0 to2, both in the existing
post-storage cancellation regression at lines56-57. Critical findings,
unresolved symbols and project-model failures were all zero. The six protected
checks succeeded; this did not make the overall Main CI successful.

Both postcondition reads now explicitly pass CancellationToken.None, matching
the existing optional default token. They must observe the compensation result
after the caller token has already been cancelled. The cancelled upload,
original exception, deleted-key identity and uncancellable cleanup assertions
are unchanged. Product runtime, ratchet baseline, thresholds, inspections,
assertions, retries and timeouts are unchanged. Current focused and hosted
qualification remain required; the original failed Main attempt is retained.
