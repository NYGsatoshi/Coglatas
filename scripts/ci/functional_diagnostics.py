"""Validate structural diagnostics independently of Functional gate acceptance."""
import re
import hashlib
import struct
import zlib
from pathlib import Path

from functional_evidence import STATES, expected_owners

MAX_DIAGNOSTIC_BYTES = 131072
FIELDS = {"schemaVersion", "commitSha", "gate", "runId", "runAttempt", "suite", "capturePolicy", "producer", "artifactName", "journeys"}
JOURNEY_FIELDS = {"journeyId", "status", "expectedStatus", "evidenceState", "reason", "attempts"}
ATTEMPT_FIELDS = {"retry", "status", "failureKind", "failedStepId", "completedStepIds", "browserEvidenceState", "browser"}
KINDS = {"NONE", "TIMEOUT", "SKIPPED", "INTERRUPTED", "REQUEST_FAILURE", "ASSERTION", "UNCLASSIFIED_FAILURE"}
PROJECTION_KEYS = {"filesPage", "uploaderEnabled", "inventoryRows", "selectedRows", "detailPane", "detailHeading", "downloadEnabled", "errorIndicator"}
OPERATIONS = {"files.inventory", "files.detail", "files.download", "files.grant", "files.versions", "files.sharing", "files.move", "files.folders", "search", "auth", "workspace", "project", "task", "message", "notification", "announcement", "api.other"}
STEPS = {
    "FUNC-TASK-001": r"STEP-(?:0[1-9]|1[01])",
    "FUNC-FILE-002": r"F05-(?:FAST-(?:0[1-9]|1[0-2])|FULL-0[1-4])",
    "FUNC-MSG-001": r"MSG-(?:0[1-4]|FULL|NEG)",
    "FUNC-NOTIF-001": r"NOTIF-(?:0[1-4]|FULL)",
    "FUNC-ANN-001": r"ANN-0[1-4]",
    "FUNC-AUTHZ-001": r"AUTHZ-0[1-9]",
    "FUNC-AUTHZ-002": r"AUTHZ-0[1-9]",
}


class DiagnosticEvidenceError(ValueError):
    def __init__(self, state, reason):
        super().__init__("Diagnostic evidence was refused.")
        self.state = state
        self.reason = reason


def exact_fields(data, fields):
    if not isinstance(data, dict) or set(data) != fields:
        raise ValueError("Unexpected diagnostic fields.")


def integer(value, maximum):
    return type(value) is int and 0 <= value <= maximum


def step_id(journey, value):
    return isinstance(value, str) and (value == "OWNER" or re.fullmatch(STEPS[journey], value) is not None)


def validate_browser(data):
    exact_fields(data, {"schemaVersion", "scope", "network", "networkTruncated", "consoleCounts", "projection", "projectionCheckpoint", "structuralSnapshotState", "structuralSnapshot"})
    if type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1 or data["scope"] != "primary-browser-context" or data["projectionCheckpoint"] not in {"failed-step", "after-test-cleanup"}:
        raise ValueError("Invalid browser diagnostic provenance.")
    if data["structuralSnapshotState"] not in {"NOT_REQUIRED", "CAPTURED", "UNAVAILABLE"}:
        raise ValueError("Invalid structural snapshot classification.")
    snapshot = data["structuralSnapshot"]
    if data["structuralSnapshotState"] == "CAPTURED":
        exact_fields(snapshot, {"name", "sha256"})
        if not isinstance(snapshot["name"], str) or not re.fullmatch(r"structural-(?:core|files|collaboration|authz-negative)-FUNC-[A-Z]+-\d{3}-[0-9a-f]{40}-[1-9]\d{0,19}-[1-9]\d{0,8}-\d{1,4}\.png", snapshot["name"]) or not isinstance(snapshot["sha256"], str) or not re.fullmatch(r"[0-9a-f]{64}", snapshot["sha256"]):
            raise ValueError("Invalid structural snapshot provenance.")
    elif snapshot is not None:
        raise ValueError("Unexpected structural snapshot provenance.")
    if not isinstance(data["network"], list) or len(data["network"]) > 80 or not integer(data["networkTruncated"], 1000000):
        raise ValueError("Unbounded browser diagnostics.")
    for event in data["network"]:
        exact_fields(event, {"event", "requestSequence", "method", "operation", "status"})
        if event["event"] not in {"REQUEST", "RESPONSE", "NETWORK_FAILURE"} or event["method"] not in {"GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"} or event["operation"] not in OPERATIONS or not integer(event["requestSequence"], 1000000) or not event["requestSequence"]:
            raise ValueError("Invalid browser diagnostic event.")
        if (event["event"] == "RESPONSE" and (not integer(event["status"], 599) or event["status"] < 100)) or (event["event"] != "RESPONSE" and event["status"] is not None):
            raise ValueError("Invalid diagnostic response status.")
    exact_fields(data["consoleCounts"], {"error", "warning", "info", "debug", "other", "pageError"})
    if any(not integer(count, 1000000) for count in data["consoleCounts"].values()):
        raise ValueError("Invalid console severity counters.")
    exact_fields(data["projection"], PROJECTION_KEYS)
    if any(value is not None and type(value) is not bool for value in data["projection"].values()):
        raise ValueError("Invalid structural projection.")


def validate_diagnostics(data, lane):
    exact_fields(data, FIELDS)
    if type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1 or data["capturePolicy"] != "allowlisted-functional-v1" or data["producer"] not in {"playwright-reporter", "lane-finalizer"}:
        raise ValueError("Unknown diagnostic capture policy.")
    if any(data[field] != lane[field] for field in ("commitSha", "gate", "runId", "runAttempt", "suite")):
        raise DiagnosticEvidenceError("STALE", "EXECUTION_IDENTITY_MISMATCH")
    if data["artifactName"] != f"functional-execution-diagnostics-{lane['gate']}-{lane['suite']}-{lane['runAttempt']}":
        raise ValueError("Invalid diagnostic artifact provenance.")
    owners = expected_owners(lane["suite"], lane["gate"])
    journeys = data["journeys"]
    if not isinstance(journeys, list) or len(journeys) != len(owners):
        raise ValueError("Missing diagnostic owner coverage.")
    seen = []
    for journey in journeys:
        exact_fields(journey, JOURNEY_FIELDS)
        owner = journey["journeyId"]
        if owner not in owners or journey["status"] not in STATES or journey["evidenceState"] not in {"COMPLETE", "MISSING", "INCOMPLETE"} or journey["reason"] not in STATES | {"NONE", "OWNER_COVERAGE", "MISSING_EVIDENCE"}:
            raise ValueError("Invalid diagnostic classification.")
        lane_owner = next(item for item in lane["journeys"] if item["journeyId"] == owner)
        if journey["status"] != lane_owner["status"] or not isinstance(journey["attempts"], list) or len(journey["attempts"]) > 10:
            raise ValueError("Diagnostic result does not match its lane.")
        if journey["evidenceState"] == "COMPLETE" and len(journey["attempts"]) != lane_owner["attempts"]:
            raise ValueError("Incomplete diagnostic attempt coverage.")
        if journey["expectedStatus"] not in {None, "passed", "failed", "timedOut", "skipped", "interrupted"}:
            raise ValueError("Invalid diagnostic expectation classification.")
        if journey["evidenceState"] == "COMPLETE":
            records = journey["attempts"]
            for record in records:
                exact_fields(record, ATTEMPT_FIELDS)
            if journey["expectedStatus"] is None or not records or any(record.get("retry") != index for index, record in enumerate(records)):
                raise ValueError("Invalid diagnostic retry sequence.")
            derived = ("SKIPPED" if journey["expectedStatus"] != "passed" or any(record.get("status") == "skipped" for record in records)
                       else "PASS" if len(records) == 1 and records[0].get("status") == "passed" and records[0].get("retry") == 0
                       else "FLAKY" if any(record.get("status") == "passed" for record in records)
                       else "FAIL" if any(record.get("status") in {"failed", "timedOut"} for record in records)
                       else "BLOCKED")
            if journey["status"] != "QUARANTINED" and journey["status"] != derived:
                raise ValueError("Diagnostic outcome conflicts with its attempts.")
            if journey["reason"] != ("NONE" if journey["status"] == "PASS" else "OWNER_COVERAGE" if journey["status"] == "BLOCKED" else journey["status"]):
                raise ValueError("Diagnostic reason conflicts with its outcome.")
        seen.append(owner)
        for attempt in journey["attempts"]:
            exact_fields(attempt, ATTEMPT_FIELDS)
            if not integer(attempt["retry"], 1000) or attempt["status"] not in {"passed", "failed", "timedOut", "skipped", "interrupted"} or attempt["failureKind"] not in KINDS:
                raise ValueError("Invalid diagnostic attempt classification.")
            if attempt["status"] == "passed":
                if attempt["failedStepId"] is not None or attempt["failureKind"] != "NONE":
                    raise ValueError("Passing diagnostic attempt contains a failure.")
            elif not step_id(owner, attempt["failedStepId"]) or attempt["failureKind"] == "NONE":
                raise ValueError("Failure diagnostic lacks a stable classification.")
            completed = attempt["completedStepIds"]
            if not isinstance(completed, list) or len(completed) > 24 or any(not step_id(owner, item) or item == "OWNER" for item in completed) or len(set(completed)) != len(completed):
                raise ValueError("Invalid stable execution trace.")
            if attempt["browserEvidenceState"] not in {"COMPLETE", "MISSING", "INCOMPLETE"}:
                raise ValueError("Unknown browser evidence state.")
            if attempt["browserEvidenceState"] == "COMPLETE":
                validate_browser(attempt["browser"])
                snapshot = attempt["browser"]["structuralSnapshot"]
                if snapshot is not None and snapshot["name"] != f"structural-{lane['suite']}-{owner}-{lane['commitSha']}-{lane['runId']}-{lane['runAttempt']}-{attempt['retry']}.png":
                    raise ValueError("Structural snapshot belongs to another execution.")
            elif attempt["browser"] is not None:
                raise ValueError("Incomplete browser evidence contains unexpected data.")
    if sorted(seen) != sorted(owners):
        raise ValueError("Duplicate or missing diagnostic owners.")
    return data


def missing_diagnostics(lane):
    return {"schemaVersion": 1, **{field: lane[field] for field in ("commitSha", "gate", "runId", "runAttempt", "suite")},
            "capturePolicy": "allowlisted-functional-v1", "producer": "lane-finalizer",
            "artifactName": f"functional-execution-diagnostics-{lane['gate']}-{lane['suite']}-{lane['runAttempt']}",
            "journeys": [{"journeyId": owner["journeyId"], "status": owner["status"], "expectedStatus": None, "evidenceState": "MISSING", "reason": "MISSING_EVIDENCE", "attempts": []} for owner in lane["journeys"]]}


def validate_snapshot_files(data, source_directory):
    """Read only identity-bound, digest-bound PNGs referenced by validated metadata."""
    verified = []
    for journey in data["journeys"]:
        for attempt in journey["attempts"]:
            browser = attempt["browser"]
            snapshot = browser["structuralSnapshot"] if browser is not None else None
            if snapshot is None:
                continue
            with (Path(source_directory) / snapshot["name"]).open("rb") as source:
                raw = source.read(262145)
            if len(raw) > 262144 or hashlib.sha256(raw).hexdigest() != snapshot["sha256"]:
                raise ValueError("Structural snapshot is missing, unbounded or incomplete.")
            validate_png(raw)
            verified.append((snapshot["name"], raw))
    return verified


def validate_png(raw):
    """Accept bounded screenshot PNG chunks; exclude text/external metadata and tails."""
    if not raw.startswith(b"\x89PNG\r\n\x1a\n"):
        raise ValueError("Invalid structural PNG.")
    position, chunks, image_chunks = 8, 0, 0
    while position + 12 <= len(raw):
        size = struct.unpack(">I", raw[position:position + 4])[0]
        kind = raw[position + 4:position + 8]
        end = position + size + 12
        if end > len(raw) or kind not in {b"IHDR", b"IDAT", b"IEND", b"sRGB", b"gAMA", b"cHRM", b"pHYs"} or zlib.crc32(raw[position + 4:end - 4]) != struct.unpack(">I", raw[end - 4:end])[0]:
            raise ValueError("Unexpected structural PNG metadata.")
        if chunks == 0:
            if kind != b"IHDR" or size != 13:
                raise ValueError("Invalid structural PNG dimensions.")
            width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", raw[position + 8:end - 4])
            if width != 900 or not 0 < height <= 4096 or depth != 8 or color not in {2, 6} or compression or filtering or interlace:
                raise ValueError("Unbounded structural PNG dimensions.")
        elif kind == b"IHDR":
            raise ValueError("Duplicate structural PNG header.")
        if kind in {b"sRGB", b"gAMA", b"cHRM", b"pHYs"} and size != {b"sRGB": 1, b"gAMA": 4, b"cHRM": 32, b"pHYs": 9}[kind]:
            raise ValueError("Unexpected structural PNG metadata size.")
        image_chunks += kind == b"IDAT"
        chunks += 1
        position = end
        if chunks > 64:
            raise ValueError("Unbounded structural PNG chunks.")
        if kind == b"IEND":
            if size or position != len(raw) or not image_chunks:
                raise ValueError("Incomplete structural PNG.")
            return
    raise ValueError("Incomplete structural PNG.")


def summarize_diagnostics(data):
    lines = ["", "Structural execution diagnostics (separate from gate acceptance):", "",
             f"Producer: `{data['producer']}`; capture policy: `{data['capturePolicy']}`", "",
             f"Diagnostic artifact: `{data['artifactName']}` (planned subsequent upload).", ""]
    for journey in data["journeys"]:
        failures = [f"{attempt['failedStepId']} / {attempt['failureKind']}" for attempt in journey["attempts"] if attempt["failedStepId"]]
        lines.append(f"- {journey['journeyId']}: {journey['status']}; evidence={journey['evidenceState']}; reason={journey['reason']}; failed steps={'; '.join(failures) or 'none recorded'}")
        for attempt in journey["attempts"]:
            lines.append(f"  Browser observation: {attempt['browserEvidenceState']}; retry={attempt['retry']}")
    lines.extend(["", "Authenticated native traces, product screenshots/video, bodies, URLs, console text and backend log tails are excluded.",
                  "Network events cover the primary browser context; standalone API contexts are not captured. Files failure checkpoints freeze observation before cleanup; other records identify their after-cleanup checkpoint."])
    return "\n".join(lines) + "\n"
