"""Inspect one pinned pre-Thread send failure; never grants Functional acceptance."""
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import zipfile

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("original_archive_verifier", HERE / "verify-composer-diagnostic-evidence.py")
archive_tools = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(archive_tools)
from functional_diagnostics import MAX_DIAGNOSTIC_BYTES, validate_diagnostics, validate_png

SUBJECT = dict(run=40439329777, head="de10214927a0d396d3cbb2b15b0c2201465bdb0b",
    base="7499ac320ff12889435a90848beae79ce1be2f9b",
    merge="b237edde751cc3898845b9e7634150d356935321",
    tree="f99fa187dd923aef9c6be011de0ec11a3ff4e11b",
    artifact=12705272743, size=141135,
    digest="3938e9f32cfdca9c9a04bd14bc05bd55aa94be97b9f37dbea09fd22e07506678",
    expires="2026-11-10T02:35:38Z", collaboration=121977068019)
OUTPUT = Path("artifacts/messaging-send-triage/report.json")
require = archive_tools.require
sha = archive_tools.sha


def lane(record):
    return dict(commitSha=record["merge"], gate="functional-fast", suite="collaboration",
        runId=str(record["run"]), runAttempt="1",
        journeys=[dict(journeyId="FUNC-MSG-001", status="FAIL", attempts=1),
                  dict(journeyId="FUNC-NOTIF-001", status="PASS", attempts=1)])


def project_diagnostics(data, record):
    validate_diagnostics(data, lane(record))
    journeys = {item["journeyId"]: item for item in data["journeys"]}
    for owner, status, step, failure in (
            ("FUNC-MSG-001", "failed", "MSG-THREAD", "TIMEOUT"),
            ("FUNC-NOTIF-001", "passed", None, "NONE")):
        journey = journeys[owner]
        require(journey["evidenceState"] == "COMPLETE" and len(journey["attempts"]) == 1,
                "ATTEMPT_COMPLETENESS")
        attempt = journey["attempts"][0]
        require(attempt["retry"] == 0 and attempt["status"] == status
                and attempt["failedStepId"] == step and attempt["failureKind"] == failure
                and attempt["browserEvidenceState"] == "COMPLETE", "PINNED_FAILURE_CONTRACT")
    attempt = journeys["FUNC-MSG-001"]["attempts"][0]
    require("MSG-RECOVERY" not in attempt["completedStepIds"], "RECOVERY_NOT_REACHED")
    browser = attempt["browser"]
    require(browser["schemaVersion"] == 3 and browser["projectionCheckpoint"] == "failed-step",
            "FAILURE_CHECKPOINT")
    observation = browser["messaging"]
    messaging = None
    if observation is not None:
        messaging = {key: observation[key] for key in (
            "documentStartedAtMs", "storageReadable", "storageWritable", "webSocketObserved",
            "eventsTruncated", "controlsTruncated")}
        messaging["events"] = [{key: item[key] for key in (
            "atMs", "kind", "connection", "composerGeneration", "composerPresent",
            "draftEnabled", "sendEnabled", "draftFocused")} for item in observation["events"]]
        messaging["controls"] = [{key: item[key] for key in ("atMs", "openedAtMs", "kind")}
                                  for item in observation["controls"]]
    post_rows = [{key: item[key] for key in ("event", "requestSequence", "status")}
                 for item in browser["network"]
                 if item["operation"] == "message" and item["method"] == "POST"]
    return dict(functionalResult="FAIL", failedStep="MSG-THREAD", failureKind="TIMEOUT",
        location="SECOND_ROOT_POST_RESPONSE_WAIT", sourceLine=251, notificationResult="PASS",
        attempts=1, retries=0, recoveryBarrierReached=False,
        composerFill=browser["composerFill"], messaging=messaging,
        primaryContextMessagePostRows=post_rows, networkTruncated=browser["networkTruncated"],
        consoleCounts={key: browser["consoleCounts"][key]
                       for key in ("error", "warning", "info", "debug", "other", "pageError")},
        structuralSnapshotState=browser["structuralSnapshotState"],
        limits=["Network rows contain no timestamps and no exact resource/body identity.",
                "Standalone API contexts are excluded; truncated observations cannot prove absence.",
                "DOM flags and input events do not prove send-handler dispatch or a guard decision.",
                "No semantic cause, Thread-opening result, recovery-barrier result or release PASS is inferred."])


def inspect_subject(record, raw):
    require(len(raw) == record["size"] and len(raw) <= 262144
            and sha(raw) == record["digest"], "SUBJECT_ZIP_DIGEST")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        infos = archive.infolist()
        require(1 <= len(infos) <= 4 and not archive.comment, "ZIP_ENTRY_COUNT")
        names, expanded = [], 0
        for info in infos:
            require(info.filename == info.orig_filename and re.fullmatch(
                r"(?:diagnostics-collaboration\.json|structural-collaboration-FUNC-(?:MSG|NOTIF)-001-[0-9a-f]{40}-[1-9][0-9]{0,19}-1-0\.png)",
                info.filename) is not None, "ZIP_MEMBER_NAME")
            require(info.filename not in names and not info.extra and not info.comment
                and not info.flag_bits & 1 and info.compress_type in (0, 8)
                and info.external_attr >> 16 & 0o170000 in (0, 0o100000)
                and info.file_size <= 1048576
                and info.file_size <= max(1, info.compress_size) * 200, "ZIP_MEMBER_METADATA")
            expanded += info.file_size
            require(expanded <= 2097152, "ZIP_EXPANSION")
            names.append(info.filename)
        archive_tools.closed_container(raw, infos)
        require("diagnostics-collaboration.json" in names, "MISSING_DIAGNOSTICS")
        diagnostic = archive.read("diagnostics-collaboration.json")
        require(len(diagnostic) <= MAX_DIAGNOSTIC_BYTES, "DIAGNOSTIC_SIZE")
        data = archive_tools.closed_json(diagnostic)
        projection = project_diagnostics(data, record)
        referenced = {"diagnostics-collaboration.json"}
        for journey in data["journeys"]:
            snapshot = journey["attempts"][0]["browser"]["structuralSnapshot"]
            if snapshot is not None:
                require(snapshot["name"] in names, "MISSING_SNAPSHOT")
                png = archive.read(snapshot["name"])
                require(len(png) <= 262144 and sha(png) == snapshot["sha256"], "PNG_DIGEST")
                validate_png(png)
                referenced.add(snapshot["name"])
        require(set(names) == referenced, "UNREFERENCED_MEMBER")
        return projection


def inspect_log(record, raw):
    require(len(raw) <= 16777216, "NATIVE_LOG_SIZE")
    text = re.sub(r"\x1b\[[0-?]*[ -/]*[@-~]", "", raw.decode("utf-8-sig"))
    checkout = re.findall(r"HEAD is now at ([a-f0-9]{7,40}) Merge ([a-f0-9]{40}) into ([a-f0-9]{40})", text)
    require(len(checkout) == 1 and record["merge"].startswith(checkout[0][0])
            and checkout[0][1:] == (record["head"], record["base"]), "LOG_SOURCE")
    require("page.waitForResponse: Timeout 20000ms exceeded" in text
        and "collaboration-owner-journey.spec.ts:251:18" in text
        and "failed steps=MSG-THREAD / TIMEOUT" in text
        and text.count("Browser observation: COMPLETE; retry=0") == 2
        and re.findall(r"- FUNC-(MSG|NOTIF)-001: (PASS|FAIL); attempts=([0-9]+); duration=([0-9]+) ms", text)
            == [("MSG", "FAIL", "1", "24832"), ("NOTIF", "PASS", "1", "1100")],
        "NATIVE_FAILURE_LOCATION")
    return dict(sha256=sha(raw), size=len(raw), retained="ORIGINAL_PROVIDER_ONLY")


def main():
    try:
        require(os.environ.get("GITHUB_EVENT_NAME") == "pull_request"
            and os.environ.get("GITHUB_REPOSITORY") == archive_tools.REPO
            and os.environ.get("CALLER_PR_NUMBER") == "1447", "EXECUTION_CONTEXT")
        for name in ("GITHUB_SHA", "CALLER_HEAD_SHA", "CALLER_BASE_SHA"):
            require(re.fullmatch(r"[a-f0-9]{40}", os.environ.get(name, "")) is not None, "CALLER_SOURCE")
        for name in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT"):
            require(re.fullmatch(r"[1-9][0-9]{0,19}", os.environ.get(name, "")) is not None, "CALLER_RUN")
        require(subprocess.run(["git", "hash-object", str(HERE / "verify-composer-diagnostic-evidence.py")],
            capture_output=True, check=True).stdout.decode().strip()
            == "22bea9b3c0cd7e64e091fbcec6835110403610b9", "UNCHANGED_ARCHIVE_VERIFIER")
        client = archive_tools.GitHub(os.environ.get("READ_TOKEN", ""))
        run = client.json("actions/runs/" + str(SUBJECT["run"]))
        artifact = client.json("actions/artifacts/" + str(SUBJECT["artifact"]))
        commit = client.json("git/commits/" + SUBJECT["merge"])
        archive_tools.verify_metadata(SUBJECT, run, artifact, commit)
        require(type(run["run_attempt"]) is int, "RUN_ATTEMPT_TYPE")
        job = client.json("actions/jobs/" + str(SUBJECT["collaboration"]))
        require(job["id"] == SUBJECT["collaboration"] and job["run_id"] == SUBJECT["run"]
            and type(job["run_attempt"]) is int and job["run_attempt"] == 1
            and job["status"] == "completed" and job["conclusion"] == "failure", "JOB_IDENTITY")
        raw = client.download("actions/artifacts/" + str(SUBJECT["artifact"]) + "/zip", 262144)
        projection = inspect_subject(SUBJECT, raw)
        native = inspect_log(SUBJECT, client.download(
            "actions/jobs/" + str(SUBJECT["collaboration"]) + "/logs", 16777216))
        report = dict(schemaVersion=1, kind="BOUNDED_MESSAGE_SEND_FAILURE_TRIAGE",
            inspection="VERIFIED_METADATA", subject=SUBJECT, nativeLog=native, projection=projection,
            functionalResult="FAIL", releaseStatus="RELEASE_NO_GO",
            callerSourceSha=os.environ["GITHUB_SHA"], callerHeadSha=os.environ["CALLER_HEAD_SHA"],
            callerBaseSha=os.environ["CALLER_BASE_SHA"], callerRunId=os.environ["GITHUB_RUN_ID"],
            callerRunAttempt=os.environ["GITHUB_RUN_ATTEMPT"])
        OUTPUT.parent.mkdir(parents=True, exist_ok=True)
        with OUTPUT.open("x", encoding="utf-8") as output:
            json.dump(report, output, sort_keys=True)
            output.write("\n")
        print(json.dumps(report, sort_keys=True))
        return 0
    except Exception:  # A public diagnostic boundary never emits exception or child text.
        print("PINNED_SEND_FAILURE_TRIAGE_REFUSED", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
