"""Capture sanitized observed SEC-ARCH TRX coverage; this is not a completion attestation."""

from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timedelta, timezone
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import xml.etree.ElementTree as ET

from sec_arch_assembly_binding import capture_assemblies, SIX_ASSEMBLY_SCOPE

NS = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
PREFIX = "Coglatas.Tests.SecurityArchitecture."
REPLAY_PREFIX = "Coglatas.Tests.PostgreSql.OutboxReplayPostgreSqlTests."
CATALOG = {
    "SecurityArchitectureApiInventoryTests": {
        "ActualComposedHostInventoryPreservesAnonymousAndProtectedMetadataWithoutAcceptance": 1,
    },
    "SecurityArchitectureApiAuthorizationTests": {
        "EveryComposedProtectedHttpEndpointRejectsAnonymousRequestsAfterValidCsrf": 1,
    },
    "SecurityArchitectureApiCurrentAuthorityTests": {
        "AnonymousBypassHandlersAreClassifiedAndApplicationOwnedAuthenticationStillRejects": 1,
        "PersistedProjectCreateCapabilityChangesDenyRealHttpWithoutCreationEffects": 1,
    },
    "SecurityArchitectureCliTests": {
        "AllTypedSyntheticContractsValidateWithoutServices": 5,
        "InvalidContractsAreRejectedThroughCli": 18,
        "MalformedAndManualPassDocumentsCannotValidate": 9,
        "OutcomeWireNamesRemainCanonicalUppercase": 1,
        "InventoryDiffAcceptsSameScopeAndDeterministicallyRejectsRelaxation": 1,
        "InventoryDiffRejectsRemovedAndUnclassifiedSurfaces": 1,
        "EvidenceCheckAcceptsExecutedSyntheticControlsOnExactCandidate": 1,
        "EvidenceMutationsCannotCreateFalseGreen": 18,
        "ConditionalProductApplicabilityDoesNotExemptIsolatedRuntime": 1,
        "ManualReviewCannotCountAsRuntimePass": 1,
        "InvalidInputDiagnosticsDoNotEchoSensitiveContent": 1,
    },
    "SecurityArchitectureInventoryTests": {
        "ControllerRuntimeAndModelInventoryReportUnclassifiedBoundaries": 1,
        "ActualPostgreSqlInventoryMatchesAllModelTablesAndPreservesUnknownRoleState": 1,
    },
    "SecurityArchitectureRlsTests": {
        "SyntheticOutboxAndAuditRlsRequireContextAndRejectTenantMutationAndBypass": 1,
    },
    "SecurityArchitectureRlsCatalogTests": {
        "DraftPolicyCatalogueCoversTenantColumnsAndParentsAndRejectsStructuralDrift": 1,
    },
    "SecurityArchitectureRlsOperationTests": {
        "MigratedSourceRowsRequireRealPositiveControlsBeforeEveryDraftPolicyDenial": 1,
    },
    "SecurityArchitectureRlsSourceReferenceTests": {
        "FreshMigratedCatalogueIndependentlyBindsEveryNativeSourceAndUnavailableDirectOperation": 1,
        "ActualNativeGuardFunctionAndConstraintMutationsInvalidateRetainedSourceIdentity": 1,
    },
    "SecurityArchitectureRlsDispositionTests": {
        "ParentRetentionCascadeIsDistinctFromForbiddenDirectRuleDeletion": 1,
        "SourceGuardAndConstraintIdentitiesDetectDisabledAndSemanticallyWeakenedDefinitions": 1,
        "TriggerForgingRlsErrorTextAndSqlStateCannotQualifyAsPolicyDenial": 1,
    },
    "SecurityArchitectureRlsRuntimeTests": {
        "RealSerializationConflictRetriesWholeTransactionWithFrozenTenantAndNoPartialWrites": 1,
        "PersistedSessionRevocationStopsRetryBeforeAnotherScopedTransaction": 1,
        "PersistedMembershipRevocationStopsRetryBeforeAnotherScopedTransaction": 1,
        "ValidatedCookieAndCurrentMembershipBindIsolatedEfAndRawSqlTransactions": 1,
        "ActualOutboxRepositoryRunsUnderBoundedSyntheticWorkerAndExposesUnscopedAndMutableContextLimits": 1,
    },
    "SecurityArchitectureRlsRecoveryTests": {
        "FailedAllTablePreparationRollsBackEveryRowGuardPolicyGrantAndRole": 1,
        "SuccessfulPreparationAndFailedThenSuccessfulRecoveryPreserveAllMigratedDataAndNativeGuards": 1,
        "LiveRecoveryGuardsDetectMissingPoliciesBroadGrantsBypassAndPolicyWeakening": 1,
        "CommittedIncompleteRecoveryAndChangedRowsCannotMatchTheOriginalIdentity": 1,
    },
    "SecurityArchitectureRlsAdapterTests": {
        "ActualRawAdaptersUseOwnedContextAndPreserveRollbackBeforeForeignScopeNegatives": 1,
        "ActualAnnouncementAndDigestClaimAdaptersRespectBoundedWorkerContextAndCurrentClaimTokens": 1,
        "ActualAuditQueueAndStaleRecoveryMethodsRequireOwnedContextAndPreserveForeignJobs": 1,
    },
    "SecurityArchitectureRlsComposedHostTests": {
        "ActualWebEfWorkspaceMutationAndStagedAuditAreAtomicAcrossRlsDenialAndCurrentMembershipChange": 1,
        "SelectedComposedHostProbeRequiresExplicitTestEnvironmentAndHasNoDisabledRegistration": 1,
        "ActualWebPasswordLoginAndSelectedWorkspaceReadDistinguishPostAuthRlsFromPreAuthCompatibility": 1,
        "ActualWebRawPreferenceMutationRollsBackOnExceptionAndRevokedSessionNeverStartsScopedAction": 1,
        "CurrentCookieMembershipReadFailsBeforePostAuthContextWhenDraftMembershipRlsIsInstalled": 1,
    },
    "SecurityArchitectureOutboxReplayTransportTests": {
        "ActualReplayServiceDeliversOriginalEventAndCurrentGrantRevocationHasNoTransportOrAuditEffects": 1,
    },
    "SecurityArchitectureSpecRegistryTests": {
        "AllCanonicalFamiliesSupportSyntheticAllocationWithoutCreatingRequirements": 6,
        "AllocationHistoryAndVersionGovernanceCannotBeBypassed": 4,
        "CliReadsPinnedGitSourceFromCleanSyntheticRepositoryAndRejectsForgedApprovalField": 1,
        "DeliberateInvalidRegistryFixturesAreRejectedDeterministically": 27,
        "ExactExplicitAnchorMustExistOnlyOnce": 1,
        "ExecutionLinksCannotConflateMissingSkippedOrAnotherCandidateWithPass": 10,
        "MissingStaleAndSemanticallyChangedVerifierBindingsFail": 17,
        "OversizeImmutableGitBlobIsRejectedWithoutUnboundedSourceAllocation": 1,
        "RetiredHistoryIsPreservedAndCannotBeReallocated": 1,
        "SharedVerifierRetainsDistinctPerObligationExecutionLinks": 1,
        "TraceabilityReportsSeparateStructuralLinksExecutionAndManualAuthority": 1,
        "VersionedSyntheticRegistryValidatesWithoutGrantingNormativeAuthority": 1,
    },
    "SecurityArchitectureOwnerReviewTests": {
        "ForgedUnscopedAndStaleReviewAuthorityCannotQualify": 18,
        "LiveReviewAdapterRequiresScopedOwnerReviewAndIndependentExactArtifactBytes": 1,
        "LocalApprovalDocumentCannotReplaceLiveAuthority": 1,
    },
    "SecurityArchitectureParentRlsTests": {
        "SnapshotItemsRequireVisibleParentForReadAndEveryMutation": 1,
        "TextTenantJournalPreservesAppendOnlyRulesAndDistinguishesTriggerFromRlsDenial": 1,
    },
    "SecurityArchitectureServiceTests": {
        "RealTlsServiceRejectsInvalidIdentityAndScopeWithoutEffects": 10,
        "RealTlsListenerRejectsPlainHttpAndWrongCertificateTrust": 1,
        "DestinationAndNetworkFixtureDetectsBroadOrUnintendedRules": 1,
    },
    "SecurityArchitectureSignalRTests": {
        "ProductTransportRejectsUnapprovedOriginsWithAuthenticatedLiveControls": 1,
        "ProductTransportApprovedOriginRetainsSessionAndResourceAuthorization": 1,
        "ProductTransportRejectsForeignSubscriptionsAndDeliveryWithLiveControls": 1,
        "ProductTransportReauthorizesRevokedConversationAndReplayedEvents": 1,
        "ProductTransportSessionInvalidationPreventsDelayedDeliveryAndReconnect": 1,
        "ProductTransportExpiredSessionPreventsDelayedDeliveryAndReconnect": 1,
        "ProductTransportPreservesReadButRejectsPostingAfterRoleDowngrade": 1,
        "ProductTransportReconnectUsesCurrentHttpCatchUpAuthority": 1,
        "ProductTransportTenantCookieSwitchCannotRetargetExistingOrNewSubscriptions": 1,
    },
    "SecurityArchitectureSignalREventTests": {
        "EveryDeclaredEventHasLiveTenantAndCurrentMembershipControls": 1,
        "ProjectAndWorkspaceUnsubscriptionOnlyRemovesCallingConnection": 1,
        "SameTenantHiddenResourcesRejectSubscriptionAndDeliveryWithLivePeers": 1,
        "CurrentResourceReadChangesPreventEveryApplicableCatalogueDeliveryAndRestore": 1,
    },
    "SecurityArchitectureSignalRProducerTests": {
        "ActualMessagingHttpProducersReauthorizeCurrentResourceWithoutMutationEffects": 1,
    },
    "SecurityArchitectureDomainProducerTests": {
        "ActualProjectTaskFileAndAuthorizationProducersUseCurrentHttpAuthority": 1,
        "ActualAnnouncementAndNotificationProducersPreserveRecipientAndResourceAuthority": 1,
    },
}
EXPECTED = {PREFIX + group + "." + method: count
            for group, methods in CATALOG.items() for method, count in methods.items()}
EXPECTED.update({REPLAY_PREFIX + method: 1 for method in (
    "CurrentTenantCapabilityReplaysOriginalEventAndPersistsReasonInSameTransaction",
    "PersistedRevocationsScopeAndIdentityChangesDenyWithoutEventOrAuditEffects",
    "PersistedWorkerClaimCannotBeRewoundByAnEarlierTrackedReplayState",
    "RequiredAuditFailureRollsBackTheRepositoryImmediateSave",
    "AuthorizationRevokedWhileWaitingForEventLockDeniesWithoutReplayEffects",
    "CurrentCapabilityReadDoesNotReusePreviouslyTrackedWorkspaceState",
)})


def digest(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def instant(value: str) -> datetime:
    result = datetime.fromisoformat(value.replace("Z", "+00:00"))
    if result.tzinfo is None:
        raise ValueError("Execution timestamp has no offset.")
    return result.astimezone(timezone.utc)


def observed_trx(data: bytes, now: datetime, expected_methods: dict[str, int] | None = None) -> dict:
    expected = EXPECTED if expected_methods is None else expected_methods
    if not expected or any(not isinstance(count, int) or count <= 0 for count in expected.values()):
        raise ValueError("Expected verifier catalogue invalid.")
    if len(data) > 64 * 1024 * 1024 or re.search(br"<!\s*(?:DOCTYPE|ENTITY)\b", data, re.I):
        raise ValueError("Unbounded or unsupported XML input.")
    root = ET.fromstring(data)
    if root.tag != "{" + NS["t"] + "}TestRun":
        raise ValueError("Unsupported TRX namespace.")
    times = root.find("t:Times", NS)
    if times is None:
        raise ValueError("Execution times missing.")
    start, finish = instant(times.attrib["start"]), instant(times.attrib["finish"])
    if not start <= finish <= now + timedelta(minutes=5) or start < now - timedelta(hours=24):
        raise ValueError("Execution times are stale or inconsistent.")
    definitions = {}
    for definition in root.findall("t:TestDefinitions/t:UnitTest", NS):
        identity = definition.attrib["id"]
        if identity in definitions:
            raise ValueError("Duplicate test definition.")
        method, execution = definition.find("t:TestMethod", NS), definition.find("t:Execution", NS)
        if method is None or execution is None:
            raise ValueError("Test identity missing.")
        definitions[identity] = (method.attrib["className"] + "." + method.attrib["name"],
                                 execution.attrib["id"], definition.attrib["name"])
    results = root.findall("t:Results/t:UnitTestResult", NS)
    counters = root.find("t:ResultSummary/t:Counters", NS)
    if counters is None or int(counters.attrib["total"]) != len(results) or not results:
        raise ValueError("TRX counters disagree with observed results.")
    observed = Counter(item.attrib["outcome"] for item in results)
    if (int(counters.attrib["passed"]) != observed["Passed"] or
            int(counters.attrib["failed"]) != observed["Failed"] or
            int(counters.attrib["executed"]) != observed["Passed"] + observed["Failed"]):
        raise ValueError("TRX execution counters disagree.")
    rows, seen, case_names = [], set(), set()
    for result in results:
        identity, execution = result.attrib["testId"], result.attrib["executionId"]
        if identity in seen or identity not in definitions:
            raise ValueError("Missing or duplicate execution identity.")
        seen.add(identity)
        method, definition_execution, name = definitions[identity]
        if execution != definition_execution or result.attrib["testName"] != name:
            raise ValueError("Execution identity disagrees with definition.")
        if expected_methods is None and not method.startswith((PREFIX, REPLAY_PREFIX)):
            continue
        if expected_methods is not None and method not in expected:
            continue
        if method not in expected or name in case_names or not (name == method or name.startswith(method + "(")):
            raise ValueError("Unclassified or duplicate SEC-ARCH test case.")
        case_names.add(name)
        case_start, case_finish = instant(result.attrib["startTime"]), instant(result.attrib["endTime"])
        if not start <= case_start <= case_finish <= finish:
            raise ValueError("Case timestamp disagrees with execution.")
        outcome = {"Passed": "PASS", "Failed": "FAIL", "NotExecuted": "UNVERIFIED"}.get(result.attrib["outcome"], "ERROR")
        rows.append({"method": method, "caseDigest": digest(name.encode()), "outcome": outcome})
    coverage = Counter(row["method"] for row in rows)
    missing = sorted(method for method, count in expected.items() if coverage[method] != count)
    outcome = "FAIL" if any(row["outcome"] == "FAIL" for row in rows) else (
        "ERROR" if any(row["outcome"] == "ERROR" for row in rows) else (
            "UNVERIFIED" if missing or any(row["outcome"] != "PASS" for row in rows) else "PASS"))
    return {"startedAtUtc": start.isoformat(), "completedAtUtc": finish.isoformat(),
            "outcome": outcome, "requiredCaseCount": sum(expected.values()),
            "observedCaseCount": len(rows), "missingMethods": missing,
            "cases": sorted(rows, key=lambda row: (row["method"], row["caseDigest"]))}


def reconcile_identity(receipt: dict, sha: str, environment: str, run_id: str, attempt: str) -> None:
    if (not re.fullmatch(r"[a-f0-9]{40}", sha) or not re.fullmatch(r"[a-f0-9]{64}", environment) or
            receipt["candidateSha"] != sha or receipt["environmentFingerprint"] != environment or
            receipt["runId"] != run_id or receipt["runAttempt"] != attempt or
            receipt["buildStampMatchesCandidate"] is not True):
        raise ValueError("Candidate, environment, execution or build binding mismatch.")


def capture(root: Path, trx: Path, sha: str, now: datetime, environment: dict) -> dict:
    def git(*args: str) -> str:
        return subprocess.check_output(["git", "-C", str(root), *args], text=True).strip()
    if not re.fullmatch(r"[a-f0-9]{40}", sha) or git("rev-parse", "HEAD") != sha or git("status", "--porcelain"):
        raise ValueError("Expected candidate requires an exact clean checkout.")
    stamp = root / "artifacts/ci/dotnet-build-sha"
    binding = stamp.is_file() and stamp.read_text().strip() == sha
    if (stamp.is_file() and not binding) or (os.environ.get("GITHUB_ACTIONS") == "true" and not binding):
        raise ValueError("Required build stamp missing or mismatched.")
    assemblies = capture_assemblies(root)
    data = trx.read_bytes()
    observed = observed_trx(data, now)
    report = {"schemaVersion": 2, "verifierId": "SEC-ARCH-EXECUTION-COVERAGE", "verifierVersion": "2",
              "assemblyBindingScope": SIX_ASSEMBLY_SCOPE,
              "candidateSha": sha, "buildStampMatchesCandidate": binding,
              "sourceBinding": "UNVERIFIED_PENDING_TRUSTED_ARTIFACT_RECONCILIATION",
              "environmentFingerprint": digest(json.dumps(environment, sort_keys=True).encode()),
              "environment": environment, "executionDigest": digest(data), "assemblyDigests": assemblies,
              "runId": os.environ.get("GITHUB_RUN_ID", "LOCAL"), "runAttempt": os.environ.get("GITHUB_RUN_ATTEMPT", "1"),
              "testIdentityCategory": "SEC02_SYNTHETIC", "evidenceClass": "Runtime",
              "observedExecution": observed, "qualification": "REPRESENTATIVE_ADVISORY",
              "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED",
              "trustedAttestation": "UNVERIFIED", "ownerApproval": None,
              "limits": ["Build stamp and job metadata need independent trusted artifact/run reconciliation.",
                         "Observed tests do not qualify complete endpoint/event/table/role/operation coverage.",
                         "Six assembly hashes include the copied verifier and matching loaded product dependencies; hosted bytes remain unreconciled.",
                         "Initial mapping, concrete policies and product activation require separate owner approval.",
                         "This receipt cannot close #842/#614 or promote a gate."]}
    if git("rev-parse", "HEAD") != sha or git("status", "--porcelain"):
        raise ValueError("Candidate changed during capture.")
    return report


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-sha", required=True)
    parser.add_argument("--trx", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    environment = {"system": platform.system(), "architecture": platform.machine(),
                   "dotnetSdk": subprocess.check_output(["dotnet", "--version"], text=True).strip(),
                   "postgresImage": "UNVERIFIED", "fixture": "SEC02_SYNTHETIC"}
    container = os.environ.get("SEC_ARCH_POSTGRES_CONTAINER")
    if container:
        image_id = subprocess.check_output(["docker", "inspect", "--format", "{{.Image}}", container], text=True).strip()
        if not re.fullmatch(r"sha256:[a-f0-9]{64}", image_id):
            raise ValueError("PostgreSQL image fingerprint invalid.")
        environment["postgresImage"] = image_id
    report = capture(root, args.trx, args.candidate_sha, datetime.now(timezone.utc), environment)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8", newline="\n") as destination:
        destination.write(json.dumps(report, indent=2) + "\n")
    print("SEC-ARCH observed execution: " + report["observedExecution"]["outcome"] + "; pre-Avalonia BLOCKED")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, OSError, ET.ParseError, subprocess.CalledProcessError):
        # Never echo input paths, raw XML, environment values or exception contents.
        print("SEC-ARCH evidence capture ERROR: invalid or unavailable execution inputs.")
        raise SystemExit(1)
