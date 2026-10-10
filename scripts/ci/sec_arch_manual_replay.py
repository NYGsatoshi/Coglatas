"""Bind isolated manual replay receipts; operational authority stays UNVERIFIED."""

from __future__ import annotations

from pathlib import Path
import re
import subprocess

from sec_arch_assembly_binding import SIX_ASSEMBLY_SCOPE, capture_assemblies
from sec_arch_evidence import digest, instant
from sec_arch_http_accounting import read_bounded

MANUAL_REPLAY = ("Coglatas.Tests.SecurityArchitecture.SecurityArchitectureOutboxReplayTransportTests."
                 "ActualReplayServiceDeliversOriginalEventAndCurrentGrantRevocationHasNoTransportOrAuditEffects")
MANUAL_SOURCE = "tests/Coglatas.Tests/SecurityArchitecture/SecurityArchitectureOutboxReplayTransportTests.cs"
MANUAL_CONTROLS = {"MANUAL_REPLAY_BASELINE_CROSS_TENANT", "ACTUAL_APPLICATION_MANUAL_REPLAY",
                   "CURRENT_GRANT_RESTORED_MANUAL_REPLAY"}
STAGES = ("baseline", "firstReplay", "beforeDeniedReplay", "afterDeniedReplay", "restoredReplay")
NATIVE_FIELDS = {
    "schemaVersion", "approvalStatus", "ownerApproval", "candidateSha", "assemblyBindingScope", "assemblyDigests",
    "verifierMethod", "sourcePath", "sourceDigest", "eventType", "payloadSchemaVersion", "executionScope",
    "replayActorAuthority", "recipientAuthentication", "durableEnvelopeProducer", "businessProducerCoverage",
    "authenticatedHttpReplayAdapter", "operationalCliReplayAdapter", "operatorIssuanceAuthority", "productRlsAppliedCount",
    "replayDatabaseRole", "databaseVersion", "replayDatabaseRoleIsSuperuser", "replayDatabaseRoleHasBypassRls",
    "operationalDatabaseIdentity", "originalPositiveDeliveryCount", "firstManualReplayDeliveryCount",
    "deniedManualReplayAdditionalDeliveryCount", "restoredManualReplayDeliveryCount", "verifiedPositiveRecipientCount",
    "verifiedCrossTenantPeerCount", "verifiedRevocationSentinelRecipientCount", "finalReplayAuditCount",
    "originalIdentityPayloadRoutingPreserved", "deniedEventStatePreserved", "deniedReplayAuditPreserved",
    "immutableDigest", "deniedEventStateDigest", "deniedReplayAuditDigest", "snapshotDigests", "recordedAtUtc",
    "runtimeOutcome", "normativeContractCompletion", "preAvaloniaVerdict",
}


def require(condition: bool, message: str) -> None:
    if not condition: raise ValueError(message)


def clean_candidate(root: Path, sha: str) -> None:
    root = root.resolve(strict=True)
    head = subprocess.run(["git", "-C", str(root), "rev-parse", "HEAD"], capture_output=True,
                          text=True, check=True, timeout=20).stdout.strip()
    status = subprocess.run(["git", "-C", str(root), "status", "--porcelain", "--untracked-files=all"],
                            capture_output=True, text=True, check=True, timeout=20).stdout
    require(head == sha and not status, "Manual replay source requires the clean exact candidate.")


def account_manual_replay(root: Path, native: dict, transport: dict, interval: tuple,
                          passed: bool, execution: dict, identity: tuple[str, str, str, str]) -> dict:
    require(isinstance(native, dict) and set(native) == NATIVE_FIELDS, "Missing or unsafe manual replay receipt fields.")
    require(type(native["schemaVersion"]) is int and native["schemaVersion"] == 2 and
            native["assemblyBindingScope"] == SIX_ASSEMBLY_SCOPE and native["approvalStatus"] == "DRAFT" and
            native["ownerApproval"] is None and native["preAvaloniaVerdict"] == "BLOCKED" and
            native["runtimeOutcome"] == "PASS", "Manual replay receipt scope or authority differs.")
    require(native["verifierMethod"] == MANUAL_REPLAY and native["sourcePath"] == MANUAL_SOURCE and
            native["candidateSha"] == identity[0] and execution["schemaVersion"] == 2 and
            transport["schemaVersion"] == 2 and native["assemblyDigests"] == transport["assemblyDigests"] == execution["assemblyDigests"],
            "Manual replay candidate/verifier/assembly binding differs.")
    clean_candidate(root, identity[0])
    source = read_bounded(root / MANUAL_SOURCE, 4 * 1024 * 1024)
    require(native["sourceDigest"] == transport["sourceDigest"] == digest(source) and
            native["assemblyDigests"] == capture_assemblies(root), "Manual replay source or actual loaded bytes differ.")
    require(native["eventType"] == "Projects.ProjectChanged.v1" and type(native["payloadSchemaVersion"]) is int and
            native["payloadSchemaVersion"] == 1 and
            native["executionScope"] == "ACTUAL_APPLICATION_REPLAY_TO_PRODUCT_POSTGRES_OUTBOX_AND_REAL_WEBSOCKET" and
            native["replayActorAuthority"] == "SUPPLIED_TEST_ACTOR_WITH_CURRENT_PERSISTED_AUTHORITY_CHECKS" and
            native["recipientAuthentication"] == "ACTUAL_PRODUCT_PASSWORD_COOKIE_SESSION" and
            native["durableEnvelopeProducer"] == "SYNTHETIC_TEST_ENQUEUE" and
            all(native[key] == "UNVERIFIED" for key in ("businessProducerCoverage", "authenticatedHttpReplayAdapter",
                "operationalCliReplayAdapter", "operatorIssuanceAuthority", "operationalDatabaseIdentity", "normativeContractCompletion")),
            "Manual replay boundary was broadened or operational authority was invented.")
    require(isinstance(native["replayDatabaseRole"], str) and
            re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]{0,62}", native["replayDatabaseRole"]) is not None and
            isinstance(native["databaseVersion"], str) and re.fullmatch(r"[0-9]{1,2}\.[0-9]{1,3}", native["databaseVersion"]) is not None and
            all(type(native[key]) is bool for key in ("replayDatabaseRoleIsSuperuser", "replayDatabaseRoleHasBypassRls")),
            "Manual replay native database identity is unbounded or malformed.")
    counts = {"productRlsAppliedCount": 0, "originalPositiveDeliveryCount": 1, "firstManualReplayDeliveryCount": 2,
              "deniedManualReplayAdditionalDeliveryCount": 0, "restoredManualReplayDeliveryCount": 3,
              "verifiedPositiveRecipientCount": 2, "verifiedCrossTenantPeerCount": 1,
              "verifiedRevocationSentinelRecipientCount": 3, "finalReplayAuditCount": 2}
    require(all(type(native[key]) is int and native[key] == value for key, value in counts.items()),
            "Manual replay positive/denial/restoration counts differ.")
    require(all(native[key] is True for key in ("originalIdentityPayloadRoutingPreserved", "deniedEventStatePreserved",
                                               "deniedReplayAuditPreserved")), "Manual replay content or denial state changed.")
    snapshots = native["snapshotDigests"]
    require(isinstance(snapshots, dict) and set(snapshots) == set(STAGES), "Manual replay stage evidence is incomplete.")
    for stage, count in zip(STAGES, (0, 1, 1, 1, 2)):
        row = snapshots[stage]
        require(isinstance(row, dict) and set(row) == {"immutableDigest", "eventStateDigest", "replayAuditDigest", "replayAuditCount"} and
                all(isinstance(row[key], str) and re.fullmatch(r"[a-f0-9]{64}", row[key]) is not None
                    for key in ("immutableDigest", "eventStateDigest", "replayAuditDigest")) and
                type(row["replayAuditCount"]) is int and row["replayAuditCount"] == count,
                "Manual replay stage digest/count is invalid.")
    require(all(row["immutableDigest"] == native["immutableDigest"] for row in snapshots.values()) and
            snapshots["firstReplay"] == snapshots["beforeDeniedReplay"] == snapshots["afterDeniedReplay"] and
            snapshots["beforeDeniedReplay"]["eventStateDigest"] == native["deniedEventStateDigest"] and
            snapshots["beforeDeniedReplay"]["replayAuditDigest"] == native["deniedReplayAuditDigest"] and
            all(len({snapshots[stage][key] for stage in ("baseline", "firstReplay", "restoredReplay")}) == 3
                for key in ("eventStateDigest", "replayAuditDigest")), "Manual replay snapshots contradict positive or unchanged denial effects.")
    require(interval[0] <= instant(native["recordedAtUtc"]) <= interval[1], "Manual replay native observation is outside its actual execution.")
    controls = {row["control"] for row in transport["observations"]}
    outcome = "PASS" if passed and controls == MANUAL_CONTROLS else "UNVERIFIED"
    clean_candidate(root, identity[0])
    return {"applicationToTransportOutcome": outcome, "qualificationScope": "APPLICATION_SERVICE_TO_TRANSPORT_ONLY",
            "nativeReceiptBound": True, "nativeReceiptSchemaVersion": 2, "snapshotStageCount": len(snapshots),
            "sourceAndSixLoadedAssembliesBound": True, "positiveRecipientCount": 2, "crossTenantLivePeerCount": 1,
            "revocationSentinelRecipientCount": 3, "replayActorAuthority": native["replayActorAuthority"],
            "recipientAuthentication": native["recipientAuthentication"], "httpReplayAdapter": "UNVERIFIED",
            "operationalCliReplayAdapter": "UNVERIFIED", "operatorIssuanceAuthority": "UNVERIFIED",
            "operationalDatabaseIdentity": "UNVERIFIED", "productRls": "UNVERIFIED", "normativeContractCompletion": "UNVERIFIED"}
