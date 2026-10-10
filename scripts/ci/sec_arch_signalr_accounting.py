"""Advisory accounting of explicit real SignalR assertions; approval stays UNVERIFIED."""

from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import json
from pathlib import Path
import re
import xml.etree.ElementTree as ET

from sec_arch_evidence import NS, digest, instant, observed_trx, reconcile_identity
from sec_arch_http_accounting import read_bounded, read_json
from sec_arch_assembly_binding import ASSEMBLIES, validate_local_assemblies

PREFIX = "Coglatas.Tests.SecurityArchitecture."
EVENT = PREFIX + "SecurityArchitectureSignalREventTests.EveryDeclaredEventHasLiveTenantAndCurrentMembershipControls"
RESOURCE = PREFIX + "SecurityArchitectureSignalREventTests.CurrentResourceReadChangesPreventEveryApplicableCatalogueDeliveryAndRestore"
HIDDEN = PREFIX + "SecurityArchitectureSignalREventTests.SameTenantHiddenResourcesRejectSubscriptionAndDeliveryWithLivePeers"
UNSUBSCRIBE = PREFIX + "SecurityArchitectureSignalREventTests.ProjectAndWorkspaceUnsubscriptionOnlyRemovesCallingConnection"
REVOKED = PREFIX + "SecurityArchitectureSignalRTests.ProductTransportSessionInvalidationPreventsDelayedDeliveryAndReconnect"
EXPIRED = PREFIX + "SecurityArchitectureSignalRTests.ProductTransportExpiredSessionPreventsDelayedDeliveryAndReconnect"
SWITCH = PREFIX + "SecurityArchitectureSignalRTests.ProductTransportTenantCookieSwitchCannotRetargetExistingOrNewSubscriptions"
FOREIGN = PREFIX + "SecurityArchitectureSignalRTests.ProductTransportRejectsForeignSubscriptionsAndDeliveryWithLiveControls"
ORIGIN = PREFIX + "SecurityArchitectureSignalRTests.ProductTransportRejectsUnapprovedOriginsWithAuthenticatedLiveControls"
APPROVED_ORIGIN = PREFIX + "SecurityArchitectureSignalRTests.ProductTransportApprovedOriginRetainsSessionAndResourceAuthorization"
ROLE = PREFIX + "SecurityArchitectureSignalRTests.ProductTransportPreservesReadButRejectsPostingAfterRoleDowngrade"
CATCH_UP = PREFIX + "SecurityArchitectureSignalRTests.ProductTransportReconnectUsesCurrentHttpCatchUpAuthority"
CONVERSATION_REPLAY = PREFIX + "SecurityArchitectureSignalRTests.ProductTransportReauthorizesRevokedConversationAndReplayedEvents"
PRODUCER = PREFIX + "SecurityArchitectureSignalRProducerTests.ActualMessagingHttpProducersReauthorizeCurrentResourceWithoutMutationEffects"
METHOD_SOURCES = {method: "tests/Coglatas.Tests/SecurityArchitecture/" + method.rsplit(".", 2)[-2] + ".cs"
                  for method in (EVENT, RESOURCE, HIDDEN, UNSUBSCRIBE, REVOKED, EXPIRED, SWITCH, FOREIGN,
                                 ORIGIN, APPROVED_ORIGIN, ROLE, CATCH_UP, CONVERSATION_REPLAY, PRODUCER)}
ENVIRONMENT = "ACTUAL_TEST_WEB_ENTRY_POINT_MIGRATED_POSTGRESQL_AND_REAL_WEBSOCKET"
INVALIDATION = "Security.AuthorizationStateChanged.v1"
EVENT_TARGETS = {
    **{"Messaging." + event + ".v1": "Conversation" for event in ("MessageCreated", "MessageUpdated", "MessageDeleted", "ThreadChanged")},
    "Messaging.ConversationUnreadChanged.v1": "User",
    **{"Projects." + event + ".v1": "Project" for event in ("TaskChanged", "TaskAssignmentChanged", "TaskWorkflowChanged", "TaskCommentChanged", "ProjectChanged")},
    "Files.FileChanged.v1": "Workspace", "Announcements.AnnouncementChanged.v1": "User",
    "Notifications.NotificationCreated.v1": "User", "Notifications.NotificationReadStateChanged.v1": "User", INVALIDATION: "User",
}
ROUTES = set(EVENT_TARGETS.items()) | {("Projects.TaskChanged.v1", "User"), ("Projects.ProjectChanged.v1", "Workspace")}
PROTECTED = {route for route in ROUTES if route[0] != INVALIDATION}
RESOURCE_ROUTES = {route for route in PROTECTED if route[0] != "Announcements.AnnouncementChanged.v1"}
HIDDEN_ROUTES = {("Messaging.MessageUpdated.v1", "Conversation"), ("Projects.ProjectChanged.v1", "Project"), ("Projects.ProjectChanged.v1", "Workspace")}
MESSAGING_PRODUCERS = {route for route in ROUTES if route[0].startswith("Messaging.")}


def scoped(routes, *controls):
    return {(event, target, control) for event, target in routes for control in controls}


RULES = {
    EVENT: scoped(ROUTES, "CROSS_TENANT") | scoped({route for route in ROUTES if route[1] == "User"}, "SAME_TENANT_RECIPIENT") |
        scoped(PROTECTED, "CURRENT_TENANT_MEMBERSHIP", "REPOSITORY_REPLAY_CURRENT_TENANT_MEMBERSHIP", "REPOSITORY_REPLAY_RESTORED_TENANT_MEMBERSHIP") |
        scoped({(INVALIDATION, "User")}, "INVALIDATION_RECIPIENT_MISMATCH", "REPOSITORY_REPLAY_INVALIDATION_RECIPIENT_MISMATCH"),
    RESOURCE: set().union(*(scoped({route for route in RESOURCE_ROUTES if boundary != "CONVERSATION" or route[0].startswith("Messaging.")},
        "CURRENT_" + boundary + "_READ", "REPOSITORY_REPLAY_CURRENT_" + boundary + "_READ", "REPOSITORY_REPLAY_RESTORED_" + boundary + "_READ")
        for boundary in ("WORKSPACE", "PROJECT", "CONVERSATION"))),
    HIDDEN: scoped(HIDDEN_ROUTES, "SAME_TENANT_HIDDEN_RESOURCE"),
    UNSUBSCRIBE: scoped(HIDDEN_ROUTES, "CALLING_CONNECTION_UNSUBSCRIBED"),
    REVOKED: scoped(PROTECTED, "CURRENT_SESSION_REVOCATION", "FRESH_SESSION_DELIVERY_OLD_SESSION_EXCLUDED"),
    EXPIRED: scoped(PROTECTED, "CURRENT_SESSION_EXPIRY", "FRESH_SESSION_DELIVERY_OLD_SESSION_EXCLUDED"),
    SWITCH: scoped(ROUTES, "TENANT_SWITCH_CONNECTION_PINNING") | scoped(PROTECTED,
        "TENANT_SWITCH_CURRENT_ORIGINAL_TENANT_MEMBERSHIP", "TENANT_SWITCH_RESTORED_ORIGINAL_TENANT_MEMBERSHIP"),
    FOREIGN: scoped({("Messaging.MessageUpdated.v1", "Conversation")}, "CROSS_TENANT"),
    ORIGIN: scoped({("Messaging.MessageUpdated.v1", "Conversation")}, "CURRENT_ORIGIN_INITIAL_DELIVERY", "CURRENT_ORIGIN_FINAL_DELIVERY"),
    APPROVED_ORIGIN: scoped({("Messaging.MessageUpdated.v1", "Conversation")}, "CURRENT_ORIGIN_INITIAL_DELIVERY", "CURRENT_ORIGIN_FINAL_DELIVERY"),
    ROLE: scoped({("Messaging.MessageCreated.v1", "Conversation")}, "CURRENT_HTTP_BUSINESS_MESSAGE_CREATED_DELIVERY") |
        scoped({("Messaging.MessageUpdated.v1", "Conversation")}, "CURRENT_READ_ONLY_ROLE_DELIVERY"),
    CATCH_UP: scoped({("Messaging.MessageCreated.v1", "Conversation")}, "RECONNECT_HTTP_BUSINESS_MESSAGE_CREATED_DELIVERY", "RECONNECT_CURRENT_CONVERSATION_READ"),
    CONVERSATION_REPLAY: scoped({("Messaging.MessageUpdated.v1", "Conversation")}, "CURRENT_CONVERSATION_INITIAL_DELIVERY",
        "CURRENT_CONVERSATION_READ", "REPOSITORY_REPLAY_CURRENT_CONVERSATION_READ", "CURRENT_CONVERSATION_FINAL_READ"),
    PRODUCER: scoped(MESSAGING_PRODUCERS, "HTTP_BUSINESS_ORIGINAL_PRODUCER") |
        scoped({route for route in MESSAGING_PRODUCERS if route[1] == "Conversation"}, "HTTP_BUSINESS_CURRENT_CONVERSATION_AUTHORITY") |
        scoped(MESSAGING_PRODUCERS - {("Messaging.MessageCreated.v1", "Conversation")}, "HTTP_BUSINESS_RESTORED_CURRENT_AUTHORITY"),
}
POSITIVE_ONLY = {control for scopes in RULES.values() for _, _, control in scopes if control.startswith("REPOSITORY_REPLAY_RESTORED_")} | {
    "CURRENT_ORIGIN_INITIAL_DELIVERY", "CURRENT_ORIGIN_FINAL_DELIVERY", "CURRENT_HTTP_BUSINESS_MESSAGE_CREATED_DELIVERY",
    "CURRENT_READ_ONLY_ROLE_DELIVERY", "RECONNECT_HTTP_BUSINESS_MESSAGE_CREATED_DELIVERY", "CURRENT_CONVERSATION_INITIAL_DELIVERY",
    "HTTP_BUSINESS_ORIGINAL_PRODUCER", "HTTP_BUSINESS_RESTORED_CURRENT_AUTHORITY"}
BUSINESS_PRODUCER_CONTROLS = {"CURRENT_HTTP_BUSINESS_MESSAGE_CREATED_DELIVERY", "RECONNECT_HTTP_BUSINESS_MESSAGE_CREATED_DELIVERY",
                            "HTTP_BUSINESS_ORIGINAL_PRODUCER", "HTTP_BUSINESS_RESTORED_CURRENT_AUTHORITY"}
SUBSCRIBE_RESULTS = {(name, True, "Subscribed") for name in ("SubscribeUser", "SubscribeConversation", "SubscribeWorkspace", "SubscribeProject")}
RESOURCE_DENIALS = {(name, False, "AccessDenied") for name in ("SubscribeWorkspace", "SubscribeProject", "SubscribeConversation")}
HUB_RULES = {method: set(SUBSCRIBE_RESULTS) for method in METHOD_SOURCES}
HUB_RULES[RESOURCE] |= RESOURCE_DENIALS
HUB_RULES[HIDDEN] |= RESOURCE_DENIALS
HUB_RULES[UNSUBSCRIBE] |= {(name, True, code) for name in ("UnsubscribeWorkspace", "UnsubscribeProject", "UnsubscribeConversation")
                          for code in ("Unsubscribed", "NotSubscribed")}
HUB_RULES[FOREIGN] |= RESOURCE_DENIALS | {("SubscribeTenant", True, "Subscribed"), ("UnsubscribeConversation", True, "Unsubscribed")}
for method in (ORIGIN, APPROVED_ORIGIN, ROLE, CATCH_UP, CONVERSATION_REPLAY):
    HUB_RULES[method] = {("SubscribeConversation", True, "Subscribed")}
for method in (APPROVED_ORIGIN, CATCH_UP, CONVERSATION_REPLAY):
    HUB_RULES[method].add(("SubscribeConversation", False, "AccessDenied"))
for method in (CATCH_UP, CONVERSATION_REPLAY):
    HUB_RULES[method].add(("SubscribeUser", True, "Subscribed"))
HUB_RULES[PRODUCER] = {("SubscribeConversation", True, "Subscribed"), ("SubscribeUser", True, "Subscribed")}
ORIGIN_RULES = {method: set() for method in METHOD_SOURCES}
ORIGIN_RULES[ORIGIN] = {(surface, control, 403) for surface in ("HUB_NEGOTIATE", "HUB_WEBSOCKET_UPGRADE")
                      for control in ("FOREIGN_ORIGIN", "NULL_ORIGIN", "ORIGIN_WITH_PATH")}
for method in (ORIGIN, APPROVED_ORIGIN):
    ORIGIN_RULES[method] |= {("HUB_NEGOTIATE", "AUTHORIZED_ORIGIN", 200),
                           ("HUB_WEBSOCKET_UPGRADE", "AUTHORIZED_ORIGIN_ANONYMOUS", 401)}


def account(root: Path, inventory: dict, recordings: list[dict], trx: bytes, now: datetime,
            execution_receipt: dict | None = None, identity: tuple[str, str, str, str] | None = None) -> dict:
    if inventory.get("schemaVersion") != 1 or inventory.get("catalogScope") != "ACTUAL_COMPOSED_TEST_HOST":
        raise ValueError("Actual composed inventory required.")
    realtime = inventory["realtime"]
    event_types = [row["eventType"] for row in realtime["events"]]
    if len(event_types) != len(set(event_types)) or set(event_types) != set(EVENT_TARGETS):
        raise ValueError("Changed or incomplete event inventory requires review.")
    subscriptions = realtime["subscriptionTypes"]
    if len(subscriptions) != len(set(subscriptions)) or set(subscriptions) != {"User", "Tenant", "Workspace", "Project", "Conversation"}:
        raise ValueError("Changed subscription inventory requires review.")
    hub_methods = [row["name"] for row in realtime["methods"]]
    if len(hub_methods) != len(set(hub_methods)) or set(hub_methods) != {
            "SubscribeUser", "SubscribeTenant", "SubscribeWorkspace", "SubscribeProject", "SubscribeConversation",
            "UnsubscribeWorkspace", "UnsubscribeProject", "UnsubscribeConversation"}:
        raise ValueError("Changed Hub inventory requires review.")
    methods = [record["verifierMethod"] for record in recordings]
    if not methods or len(methods) != len(set(methods)) or set(methods) - METHOD_SOURCES.keys():
        raise ValueError("Missing, duplicate or unsupported transport verifier.")
    execution = observed_trx(trx, now, {method: 1 for method in methods})
    passed = {row["method"] for row in execution["cases"] if row["outcome"] == "PASS"}
    xml = ET.fromstring(trx)
    definitions = {row.attrib["id"]: row.find("t:TestMethod", NS) for row in xml.findall("t:TestDefinitions/t:UnitTest", NS)}
    intervals = {}
    for result in xml.findall("t:Results/t:UnitTestResult", NS):
        definition = definitions[result.attrib["testId"]]
        method = definition.attrib["className"] + "." + definition.attrib["name"]
        if method in methods:
            intervals[method] = (instant(result.attrib["startTime"]), instant(result.attrib["endTime"]))
    candidate = "UNVERIFIED"
    if execution_receipt is not None:
        if identity is None:
            raise ValueError("Independent candidate/run identity required.")
        reconcile_identity(execution_receipt, *identity)
        validate_local_assemblies(root, execution_receipt, execution=True)
        if execution_receipt["executionDigest"] != digest(trx):
            raise ValueError("Wrong execution artifact.")
        candidate = "EXACT_RECEIPT_RECONCILED_TRUSTED_ATTESTATION_PENDING"
    observations, invocations, origins = [], [], []
    for record in recordings:
        method = record["verifierMethod"]
        if set(record) - {"schemaVersion", "assemblyBindingScope", "verifierMethod", "sourcePath", "sourceDigest", "environment", "assemblyDigests", "observations", "hubInvocations", "originBoundaries", "contractCompletion", "ownerApproval", "limits"}:
            raise ValueError("Unrecognized or unsafe receipt fields.")
        if record.get("ownerApproval") is not None or record.get("environment") != ENVIRONMENT or record.get("contractCompletion") != "UNVERIFIED":
            raise ValueError("Unsupported receipt or self-declared approval.")
        source = METHOD_SOURCES[method]
        source_bytes = read_bounded(root / source, 4 * 1024 * 1024)
        declaration = rb"public\s+(?:async\s+)?Task\s+" + re.escape(method.rsplit(".", 1)[1].encode()) + rb"\s*\(\s*\)"
        if record["sourcePath"] != source or record["sourceDigest"] != digest(source_bytes) or len(re.findall(declaration, source_bytes)) != 1:
            raise ValueError("Renamed, deleted or changed verifier source.")
        assemblies = record["assemblyDigests"]
        validate_local_assemblies(root, record)
        if inventory["webAssemblyDigest"] != assemblies["Coglatas.Web"]:
            raise ValueError("Inventory from a different build.")
        if execution_receipt is not None and (execution_receipt["schemaVersion"] != record["schemaVersion"] or
                                               execution_receipt["assemblyDigests"] != assemblies):
            raise ValueError("Wrong candidate assemblies.")
        seen = set()
        for row in record["observations"]:
            if set(row) != {"eventType", "subscriptionType", "control", "positiveDelivery", "excludedDelivery", "observedAtUtc"}:
                raise ValueError("Unrecognized or unsafe observation fields.")
            key = (row["eventType"], row["subscriptionType"], row["control"])
            if key not in RULES[method] or key in seen:
                raise ValueError("Unreviewed or duplicate control cannot multiply coverage.")
            seen.add(key)
            if row["positiveDelivery"] != "OBSERVED" or row["excludedDelivery"] != (None if row["control"] in POSITIVE_ONLY else "NOT_OBSERVED"):
                raise ValueError("Live positive and exact expected delivery outcome required.")
            timestamp = instant(row["observedAtUtc"])
            if method not in intervals or not intervals[method][0] <= timestamp <= intervals[method][1]:
                raise ValueError("Observation outside actual verifier execution.")
            observations.append({**row, "verifierMethod": method, "accountingOutcome": "PASS" if method in passed else "UNVERIFIED"})
        seen_hub = set()
        for row in record.get("hubInvocations", []):
            if set(row) != {"hubMethod", "allowed", "code", "observedAtUtc"} or not isinstance(row["allowed"], bool):
                raise ValueError("Unrecognized or unsafe invocation fields.")
            key = (row["hubMethod"], row["allowed"], row["code"])
            if key not in HUB_RULES[method] or key in seen_hub:
                raise ValueError("Unreviewed or duplicate actual invocation result.")
            seen_hub.add(key)
            timestamp = instant(row["observedAtUtc"])
            if method not in intervals or not intervals[method][0] <= timestamp <= intervals[method][1]:
                raise ValueError("Invocation outside actual verifier execution.")
            invocations.append({**row, "verifierMethod": method, "accountingOutcome": "PASS" if method in passed else "UNVERIFIED"})
        seen_origin = set()
        for row in record.get("originBoundaries", []):
            if set(row) != {"surface", "control", "observedStatus", "expectedStatus", "positiveDelivery", "observedAtUtc"} or any(type(row[field]) is not int for field in ("observedStatus", "expectedStatus")):
                raise ValueError("Unrecognized or unsafe Origin assertion fields.")
            key = (row["surface"], row["control"], row["observedStatus"])
            if key not in ORIGIN_RULES[method] or key in seen_origin or row["observedStatus"] != row["expectedStatus"] or row["positiveDelivery"] != "OBSERVED":
                raise ValueError("Missing live positive or unreviewed Origin boundary result.")
            seen_origin.add(key)
            timestamp = instant(row["observedAtUtc"])
            if not intervals[method][0] <= timestamp <= intervals[method][1]:
                raise ValueError("Origin assertion outside actual verifier execution.")
            if not any(peer["verifierMethod"] == method and peer["control"] in {"CURRENT_ORIGIN_INITIAL_DELIVERY", "CURRENT_ORIGIN_FINAL_DELIVERY"}
                       and instant(peer["observedAtUtc"]) <= timestamp for peer in observations):
                raise ValueError("Origin boundary lacks a recorded earlier live delivery.")
            if row["surface"] == "HUB_NEGOTIATE" and row["observedStatus"] == 403 and not any(
                    peer["surface"] == "HUB_NEGOTIATE" and peer["control"] == "AUTHORIZED_ORIGIN" and peer["observedStatus"] == 200
                    and instant(peer["observedAtUtc"]) <= timestamp for peer in record.get("originBoundaries", [])):
                raise ValueError("Origin denial lacks a same-operation negotiation positive.")
            origins.append({**row, "verifierMethod": method, "accountingOutcome": "PASS" if method in passed else "UNVERIFIED"})
    observed = {(row["verifierMethod"], row["eventType"], row["subscriptionType"], row["control"])
                for row in observations if row["accountingOutcome"] == "PASS"}
    missing = [{"verifierMethod": method, "eventType": event, "subscriptionType": target, "control": control, "outcome": "UNVERIFIED"}
               for method, scopes in sorted(RULES.items()) for event, target, control in sorted(scopes)
               if (method, event, target, control) not in observed]
    observed_hub = {(row["verifierMethod"], row["hubMethod"], row["allowed"], row["code"])
                    for row in invocations if row["accountingOutcome"] == "PASS"}
    missing_hub = [{"verifierMethod": method, "hubMethod": name, "allowed": allowed, "code": code, "outcome": "UNVERIFIED"}
                   for method, scopes in sorted(HUB_RULES.items()) for name, allowed, code in sorted(scopes)
                   if (method, name, allowed, code) not in observed_hub]
    invoked = {row["hubMethod"] for row in invocations if row["accountingOutcome"] == "PASS" and row["allowed"]}
    observed_origin = {(row["verifierMethod"], row["surface"], row["control"], row["observedStatus"])
                       for row in origins if row["accountingOutcome"] == "PASS"}
    missing_origin = [{"verifierMethod": method, "surface": surface, "control": control, "expectedStatus": status, "outcome": "UNVERIFIED"}
                      for method, scopes in sorted(ORIGIN_RULES.items()) for surface, control, status in sorted(scopes)
                      if (method, surface, control, status) not in observed_origin]
    return {"schemaVersion": 2, "verifierId": "SEC-ARCH-SIGNALR-ASSERTION-ACCOUNTING", "mode": "ADVISORY",
            "inputReceiptSchemaVersions": sorted({record["schemaVersion"] for record in recordings}),
            "fullDependencyQualification": "SIX_ASSEMBLY_LOCAL_BYTES_RECONCILED" if all(record["schemaVersion"] == 2 for record in recordings) else "UNVERIFIED",
            "candidateBinding": candidate, "executionOutcome": execution["outcome"], "executionDigest": digest(trx),
            "inventoryDigest": digest(json.dumps(inventory, sort_keys=True).encode()),
            "eventTypeCount": len(event_types), "reviewedRouteCount": len(ROUTES), "observedControlCount": len(observations),
            "controlCounts": dict(sorted(Counter(row["control"] for row in observations if row["accountingOutcome"] == "PASS").items())),
            "reviewedVerifierCount": len(RULES), "observedVerifierCount": len(methods), "unobservedScopedControls": missing,
            "assertionCoverageOutcome": "UNVERIFIED" if missing or missing_hub or missing_origin else "PASS",
            "observedHubInvocationCount": len(invocations), "unobservedInvocationAssertions": missing_hub,
            "observedOriginAssertionCount": len(origins), "originBoundaries": origins, "unobservedOriginAssertions": missing_origin,
            "observedBusinessProducerEventTypes": sorted({row["eventType"] for row in observations
                if row["accountingOutcome"] == "PASS" and row["control"] in BUSINESS_PRODUCER_CONTROLS}),
            "unrecordedVerifierMethods": sorted(METHOD_SOURCES.keys() - set(methods)),
            "events": [{"eventType": event, "controls": [row for row in observations if row["eventType"] == event],
                        "specIds": [], "specMappingOutcome": "UNVERIFIED", "fullContractCoverage": "UNVERIFIED",
                        "businessProducerCoverage": "UNVERIFIED", "currentCapabilityApplicability": "UNVERIFIED"} for event in sorted(event_types)],
            "hubInvocationReceiptOutstandingMethodCount": len(set(hub_methods) - invoked),
            "hubMethods": [{"name": name, "invocationReceiptOutcome": "PASS" if name in invoked else "UNVERIFIED", "specIds": [],
                            "invocations": [row for row in invocations if row["hubMethod"] == name]} for name in sorted(hub_methods)],
            "tenantRoutingApplicability": "UNVERIFIED", "tenantRoutingOutstandingDecisionCount": 1,
            "manualReplayToTransportOutstandingAdapterCount": 1, "productRlsOutstandingAdapterCount": 1,
            "approvedSpecMappingOutstandingEventCount": len(event_types), "trustedAttestation": "UNVERIFIED", "ownerApproval": None,
            "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED",
            "limits": ["Recorded delivery pairs are explicit assertions, not normative requirements or authentic approval.",
                       "Historical five-assembly V1 receipts retain scoped credit with full dependency qualification UNVERIFIED.",
                       "Only recorded actual Hub/Origin results count; browser evidence and unrecorded catch-up endpoints receive no inferred credit.",
                       "Repository fixture replay does not qualify manual operator replay authorization or deployed infrastructure.",
                       "Recorded actual HTTP messaging producers cover named flows only; complete producer, payload and capability matrices remain UNVERIFIED.",
                       "Metadata invalidation retains separate recipient semantics and is not presumed subject to protected-event session denial."]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inventory", required=True, type=Path)
    parser.add_argument("--observations", required=True, nargs="+", type=Path)
    parser.add_argument("--trx", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--execution-receipt", type=Path)
    for name in ("candidate-sha", "environment-fingerprint", "run-id", "run-attempt"):
        parser.add_argument("--" + name)
    args = parser.parse_args()
    identity = (args.candidate_sha, args.environment_fingerprint, args.run_id, args.run_attempt)
    if args.execution_receipt is not None and not all(identity):
        raise ValueError("Independent candidate/run binding missing.")
    report = account(Path(__file__).resolve().parents[2], read_json(args.inventory, 16 * 1024 * 1024),
        [read_json(path) for path in args.observations], read_bounded(args.trx, 64 * 1024 * 1024), datetime.now(timezone.utc),
        read_json(args.execution_receipt) if args.execution_receipt else None, identity if args.execution_receipt else None)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as output:
        output.write(json.dumps(report, indent=2) + "\n")
    print("SEC-ARCH SignalR accounting: " + report["executionOutcome"] + "; pre-Avalonia BLOCKED")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError):
        print("SEC-ARCH SignalR accounting ERROR: invalid or unavailable evidence inputs.")
        raise SystemExit(1)
