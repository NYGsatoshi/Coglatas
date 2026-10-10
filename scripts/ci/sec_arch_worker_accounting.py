"""Advisory accounting of explicit registered-worker assertions and real transport peers."""

from __future__ import annotations

import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import xml.etree.ElementTree as ET

from sec_arch_evidence import NS, digest, instant, observed_trx
from sec_arch_http_accounting import read_bounded, read_json
import sec_arch_signalr_accounting as signalr

ENVIRONMENT = "ACTUAL_TEST_WEB_ENTRY_POINT_MIGRATED_POSTGRESQL_REGISTERED_WORKERS_AND_REAL_WEBSOCKET"
ANNOUNCEMENT = "Coglatas.Web.Notifications.AnnouncementPublisherWorker"
DIGEST = "Coglatas.Web.Notifications.TaskDeadlineDigestWorker"
METHOD_RULES = {
    signalr.ANNOUNCEMENT_WORKER: {
        "ACTUAL_PUBLICATION": (ANNOUNCEMENT, "PUBLISHED_WITH_AUDIT_FROZEN_COHORT_AND_TYPED_FRAMES", "WORKER_ANNOUNCEMENT_ORIGINAL_PRODUCER"),
        "CURRENT_AUTHOR_DENIED": (ANNOUNCEMENT, "DEFERRED_DRAFT_WITH_NO_ANNOUNCEMENT_NOTIFICATION_COHORT_OR_OUTBOX_AND_ONE_DEFER_AUDIT", None),
        "CURRENT_AUDIENCE_RECOMPUTED": (ANNOUNCEMENT, "CURRENT_OWNER_FRAMES_WITH_NO_SUSPENDED_MEMBER_NOTIFICATION_OR_DELIVERY", "WORKER_ANNOUNCEMENT_CURRENT_AUDIENCE"),
        "RESTORED_AUDIENCE_PUBLICATION": (ANNOUNCEMENT, "FRESH_PUBLISHED_ANNOUNCEMENT_AND_NOTIFICATION_TYPED_FRAMES", "WORKER_ANNOUNCEMENT_RESTORED_AUDIENCE"),
    },
    signalr.DIGEST_WORKER: {
        "ACTUAL_DIGEST_PUBLICATION": (DIGEST, "SUCCEEDED_JOB_FENCED_ATTEMPT_NOTIFICATION_OUTBOX_AND_TYPED_FRAME", "WORKER_DIGEST_ORIGINAL_PRODUCER"),
        "CURRENT_WORKSPACE_MEMBER_EXCLUDED": (DIGEST, "LIVE_OWNER_DIGEST_WITH_NO_SUSPENDED_MEMBER_JOB_OR_NOTIFICATION", None),
        "RESTORED_WORKSPACE_DIGEST_PUBLICATION": (DIGEST, "FRESH_CURRENT_MEMBER_SUCCEEDED_JOB_AND_TYPED_NOTIFICATION_FRAME", "WORKER_DIGEST_RESTORED_WORKSPACE_MEMBER"),
    },
}
POSITIVE = {signalr.ANNOUNCEMENT_WORKER: "ACTUAL_PUBLICATION", signalr.DIGEST_WORKER: "ACTUAL_DIGEST_PUBLICATION"}


def account(root: Path, inventory: dict, recordings: list[dict], transport: list[dict], trx: bytes, now: datetime,
            execution_receipt: dict | None = None, identity: tuple[str, str, str, str] | None = None) -> dict:
    methods = [record["verifierMethod"] for record in recordings]
    if not methods or len(methods) != len(set(methods)) or set(methods) - METHOD_RULES.keys():
        raise ValueError("Missing, duplicate or unsupported worker verifier.")
    if {record["verifierMethod"] for record in transport} != set(methods):
        raise ValueError("Exact same-method real transport companions required.")
    peers = {record["verifierMethod"]: record for record in transport}
    # This independently validates source declaration/cardinality, six canonical
    # and loaded assemblies, actual passed TRX, intervals and typed frame credit.
    frames = signalr.account(root, inventory, transport, trx, now, execution_receipt, identity)
    registered = [row["type"] for row in inventory["serviceTopology"]["hostedServices"]]
    for worker in {rule[0] for method in methods for rule in METHOD_RULES[method].values()}:
        if registered.count(worker) != 1:
            raise ValueError("Required actual registered worker type is absent or duplicated.")
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
    observations = []
    for record in recordings:
        method = record["verifierMethod"]
        if set(record) != {"schemaVersion", "assemblyBindingScope", "verifierMethod", "sourcePath", "sourceDigest", "environment",
                           "workerPollSeconds", "assemblyDigests", "observations", "contractCompletion", "ownerApproval", "limits"}:
            raise ValueError("Unrecognized or unsafe worker receipt fields.")
        if record["schemaVersion"] != 2 or record["ownerApproval"] is not None or record["contractCompletion"] != "UNVERIFIED" or record["environment"] != ENVIRONMENT:
            raise ValueError("Unsupported worker receipt or forged approval.")
        if type(record["workerPollSeconds"]) is not int or record["workerPollSeconds"] != 1:
            raise ValueError("Only explicitly scoped non-default worker fixture cadence is reviewed.")
        peer = peers[method]
        if any(record[field] != peer[field] for field in ("schemaVersion", "assemblyBindingScope", "sourcePath", "sourceDigest", "assemblyDigests")):
            raise ValueError("Worker and actual transport source/build bindings differ.")
        seen, previous_positive = set(), None
        for row in record["observations"]:
            if set(row) != {"workerType", "control", "assertion", "outcome", "observedAtUtc"} or row["outcome"] != "OBSERVED":
                raise ValueError("Unsafe or unobserved worker control.")
            control = row["control"]
            if control not in METHOD_RULES[method] or control in seen:
                raise ValueError("Unreviewed or duplicate worker assertion.")
            seen.add(control)
            worker, assertion, frame_control = METHOD_RULES[method][control]
            if (row["workerType"], row["assertion"]) != (worker, assertion):
                raise ValueError("Wrong worker or weakened current-state assertion.")
            timestamp = instant(row["observedAtUtc"])
            if not intervals[method][0] <= timestamp <= intervals[method][1]:
                raise ValueError("Worker assertion outside actual execution.")
            if control == POSITIVE[method]:
                previous_positive = timestamp
            elif previous_positive is None or timestamp < previous_positive:
                raise ValueError("Current-state worker assertion lacks a prior actual worker positive.")
            if frame_control is not None:
                required = signalr.WORKER_PRODUCERS if method == signalr.ANNOUNCEMENT_WORKER else {("Notifications.NotificationCreated.v1", "User")}
                received = {(row["eventType"], row["subscriptionType"]) for row in peer["observations"]
                            if row["control"] == frame_control and row["positiveDelivery"] == "OBSERVED" and instant(row["observedAtUtc"]) <= timestamp}
                if received != required:
                    raise ValueError("Worker assertion lacks exact earlier typed transport peers.")
            observations.append({**row, "verifierMethod": method, "accountingOutcome": "PASS" if method in passed else "UNVERIFIED"})
    observed = {(row["verifierMethod"], row["control"]) for row in observations if row["accountingOutcome"] == "PASS"}
    missing = [{"verifierMethod": method, "control": control, "outcome": "UNVERIFIED"}
               for method, rules in sorted(METHOD_RULES.items()) for control in sorted(rules) if (method, control) not in observed]
    return {"schemaVersion": 1, "verifierId": "SEC-ARCH-REGISTERED-WORKER-ASSERTION-ACCOUNTING", "mode": "ADVISORY",
            "executionOutcome": execution["outcome"], "executionDigest": digest(trx), "candidateBinding": frames["candidateBinding"],
            "observedControlCount": len(observations), "controls": observations, "unobservedControls": missing,
            "assertionCoverageOutcome": "UNVERIFIED" if missing else "PASS", "workerPollSeconds": 1,
            "performanceQualification": "NOT_EVALUATED", "applicationRoleOutcome": "UNVERIFIED", "workerRoleOutcome": "UNVERIFIED",
            "productRlsAuthority": "UNVERIFIED", "ownerApproval": None, "contractCompletion": "UNVERIFIED",
            "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED",
            "limits": ["Only named configured worker paths and disposable synthetic state are executed.",
                       "Non-default worker cadence, synthetic database authority and test-only digest opt-in confer no operational or performance qualification.",
                       "No arbitrary prequeued event authorization, default-off negative, full worker operations or owner approval is inferred."]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    for argument in ("inventory", "trx", "output"):
        parser.add_argument("--" + argument, required=True, type=Path)
    for argument in ("observations", "transport-observations"):
        parser.add_argument("--" + argument, required=True, nargs="+", type=Path)
    parser.add_argument("--execution-receipt", type=Path)
    for argument in ("candidate-sha", "environment-fingerprint", "run-id", "run-attempt"):
        parser.add_argument("--" + argument)
    args = parser.parse_args()
    identity = (args.candidate_sha, args.environment_fingerprint, args.run_id, args.run_attempt)
    if args.execution_receipt is not None and not all(identity):
        raise ValueError("Independent candidate/run identity required.")
    report = account(Path(__file__).resolve().parents[2], read_json(args.inventory, 16 * 1024 * 1024),
        [read_json(path) for path in args.observations], [read_json(path) for path in args.transport_observations],
        read_bounded(args.trx, 64 * 1024 * 1024), datetime.now(timezone.utc),
        read_json(args.execution_receipt) if args.execution_receipt else None, identity if args.execution_receipt else None)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as output:
        output.write(json.dumps(report, indent=2) + "\n")
    print("SEC-ARCH registered-worker accounting: " + report["executionOutcome"] + "; pre-Avalonia BLOCKED")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except (ValueError, KeyError, TypeError, OSError, ET.ParseError):
        print("SEC-ARCH registered-worker accounting ERROR: invalid or unavailable evidence inputs.")
        raise SystemExit(1)
