"""Read one immutable Thread-denial timeout diagnostic; never grants acceptance."""
import importlib.util
import json
import os
from pathlib import Path
import re
import subprocess
import sys

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("original_archive_verifier", HERE / "verify-composer-diagnostic-evidence.py")
archive_tools = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(archive_tools)
from functional_diagnostics import MAX_DIAGNOSTIC_BYTES, validate_diagnostics, validate_png
import io
import zipfile

BRANCH = "diagnostic/thread-denial-first-failure-20261027"
WORKFLOW = ".github/workflows/thread-denial-failure-triage.yml"
SUBJECT = dict(run=40451595006, head="f8016bc86a93339a782787aa656d5e89b4f3ed90",
    base="78d9d5e2c8d40009dead0eff75acc9b4db7dbc0f",
    merge="3bc6e1f112c049a760e1fcf7cf6cfbd9c5e3108b",
    tree="3bfdf5f74aa579ad4c9255c3f39cebe81c34263e",
    artifact=12709854624, size=142012,
    digest="6749303cb7fdff7e95e48f6147b7f686a16e1372d7fd777386e34536d0524afc",
    expires="2026-11-10T04:46:38Z", collaboration=122016231201)
PARSER_PINS = {
    "verify-composer-diagnostic-evidence.py": "22bea9b3c0cd7e64e091fbcec6835110403610b9",
    "functional_diagnostics.py": "33b2ea78d112208f9fb4f37aa950acab914fe7f9",
    "thread_probe_diagnostics.py": "365ac57facd8e6b97d6007ce22e86fc3956abca0"}
OUTPUT = Path("artifacts/thread-denial-triage/report.json")
require = archive_tools.require
sha = archive_tools.sha


def lane(record):
    return dict(commitSha=record["merge"], gate="functional-fast", suite="collaboration",
        runId=str(record["run"]), runAttempt="1",
        journeys=[dict(journeyId="FUNC-MSG-001", status="FAIL", attempts=1),
                  dict(journeyId="FUNC-NOTIF-001", status="PASS", attempts=1)])


def project_probe(attempt):
    source = attempt.get("threadProbe", dict(state="MISSING", data=None))
    result = dict(state=source["state"], data=None)
    if source["state"] == "AVAILABLE":
        data = source["data"]
        output = dict(phases=data["phases"], target=data["target"])
        if "observation" in data:
            observation = data["observation"]
            output["observation"] = {key: observation[key] for key in (
                "truncated", "orphanResponse", "freshRequest", "outcome", "classification",
                "actionFailure", "failureTarget")}
            output["observation"]["events"] = [{key: item[key] for key in ("kind", "request", "status")}
                                               for item in observation["events"]]
            if "elapsedMs" in observation:
                output["observation"]["elapsedMs"] = observation["elapsedMs"]
        if "initial" in data:
            output["initial"] = {key: data["initial"][key] for key in (
                "responseShape", "failureDom", "preActionDeniedDom") if key in data["initial"]}
        result["data"] = output
    secondary = attempt.get("threadProbeSecondary", dict(state="MISSING", stages=[]))
    return result, {key: secondary[key] for key in ("state", "stages")}


def project_diagnostics(data, record):
    validate_diagnostics(data, lane(record))
    journeys = {item["journeyId"]: item for item in data["journeys"]}
    for owner, status, step, failure in (
            ("FUNC-MSG-001", "timedOut", "MSG-THREAD-DENIAL", "UNCLASSIFIED_FAILURE"),
            ("FUNC-NOTIF-001", "passed", None, "NONE")):
        journey = journeys[owner]
        require(journey["evidenceState"] == "COMPLETE" and len(journey["attempts"]) == 1,
                "ATTEMPT_COMPLETENESS")
        attempt = journey["attempts"][0]
        require(attempt["retry"] == 0 and attempt["status"] == status
                and attempt["failedStepId"] == step and attempt["failureKind"] == failure
                and attempt["browserEvidenceState"] == "COMPLETE", "PINNED_FAILURE_CONTRACT")
    attempt = journeys["FUNC-MSG-001"]["attempts"][0]
    require("MSG-THREAD" in attempt["completedStepIds"]
            and "MSG-THREAD-DENIAL" not in attempt["completedStepIds"]
            and "MSG-RECOVERY" not in attempt["completedStepIds"], "PRIOR_AND_UNREACHED_STEPS")
    browser = attempt["browser"]
    require(browser["schemaVersion"] == 3 and browser["projectionCheckpoint"] == "failed-step",
            "FAILURE_CHECKPOINT")
    probe, secondary = project_probe(attempt)
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
    network = [{key: item[key] for key in ("event", "requestSequence", "method", "operation", "status")}
               for item in browser["network"]
               if item["operation"] in {"message", "message.thread", "message.thread-replies"}]
    return dict(functionalResult="FAIL", attemptStatus=attempt["status"], failedStep="MSG-THREAD-DENIAL",
        failureKind="UNCLASSIFIED_FAILURE", nativeWait="OUTER_TEST_TIMEOUT_WAITING_FRESH_THREAD_REQUEST",
        sourceLine=433, nativeTestTimeoutMs=120000, notificationResult="PASS",
        attempts=1, retries=0, priorThreadStepCompleted=True, recoveryBarrierReached=False,
        threadProbe=probe, threadProbeSecondary=secondary, messaging=messaging,
        primaryContextMessagingNetwork=network, networkTruncated=browser["networkTruncated"],
        consoleCounts={key: browser["consoleCounts"][key]
                       for key in ("error", "warning", "info", "debug", "other", "pageError")},
        structuralSnapshotState=browser["structuralSnapshotState"],
        limits=["This is the original failed attempt, not a new Functional or authorization execution.",
                "Thread probe ordinals and primary-browser network sequence numbers are separate namespaces.",
                "Network rows contain no timestamps or exact resource identity; other API contexts are excluded.",
                "Truncated or missing observations cannot prove that a request never occurred.",
                "An earlier response, DOM flags or click phase does not prove fresh request selection or a handler decision.",
                "The native timeout is the whole-test deadline, not a separately established request deadline.",
                "No initiating cause, denial success, recovery-barrier result or release PASS is inferred."])


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
    require("Error: page.waitForRequest: Test timeout of 120000ms exceeded." in text
        and "collaboration-owner-journey.spec.ts:433:38" in text
        and "failed steps=MSG-THREAD-DENIAL / UNCLASSIFIED_FAILURE" in text
        and text.count("Browser observation: COMPLETE; retry=0") == 2
        and re.findall(r"- FUNC-(MSG|NOTIF)-001: (PASS|FAIL); attempts=([0-9]+); duration=([0-9]+) ms", text)
            == [("MSG", "FAIL", "1", "120201"), ("NOTIF", "PASS", "1", "1068")],
        "NATIVE_FAILURE_LOCATION")
    return dict(sha256=sha(raw), size=len(raw), retained="ORIGINAL_PROVIDER_ONLY")


def validate_caller(env, run, pr, commit):
    require(env.get("GITHUB_EVENT_NAME") == "pull_request"
        and env.get("GITHUB_REPOSITORY") == archive_tools.REPO
        and env.get("GITHUB_HEAD_REF") == BRANCH, "EXECUTION_CONTEXT")
    for name in ("GITHUB_SHA", "CALLER_HEAD_SHA", "CALLER_BASE_SHA"):
        require(re.fullmatch(r"[a-f0-9]{40}", env.get(name, "")) is not None, "CALLER_SOURCE")
    for name in ("GITHUB_RUN_ID", "GITHUB_RUN_ATTEMPT", "CALLER_PR_NUMBER"):
        require(re.fullmatch(r"[1-9][0-9]{0,19}", env.get(name, "")) is not None, "CALLER_RUN")
    require(type(run["id"]) is int and run["id"] == int(env["GITHUB_RUN_ID"])
        and type(run["run_attempt"]) is int and run["run_attempt"] == int(env["GITHUB_RUN_ATTEMPT"])
        and run["head_sha"] == env["CALLER_HEAD_SHA"] and run["event"] == "pull_request"
        and run["path"] == WORKFLOW and run["repository"]["full_name"] == archive_tools.REPO,
        "CALLER_RUN_IDENTITY")
    require(type(pr["number"]) is int and pr["number"] == int(env["CALLER_PR_NUMBER"])
        and pr["state"] == "open" and pr["draft"] is True and pr["merged"] is False
        and pr["auto_merge"] is None and pr["head"]["sha"] == env["CALLER_HEAD_SHA"]
        and pr["head"]["ref"] == BRANCH and pr["head"]["repo"]["full_name"] == archive_tools.REPO
        and pr["base"]["ref"] == "main", "CALLER_PR_IDENTITY")
    require(commit["sha"] == env["GITHUB_SHA"]
        and [item["sha"] for item in commit["parents"]] == [env["CALLER_BASE_SHA"], env["CALLER_HEAD_SHA"]],
        "CALLER_MERGE_IDENTITY")


def verify_subject_metadata(record, run, artifact, commit):
    archive_tools.verify_metadata(record, run, artifact, commit)
    require(type(run["run_attempt"]) is int, "RUN_ATTEMPT_TYPE")


def verify_checkout(expected):
    require(re.fullmatch(r"[a-f0-9]{40}", expected or "") is not None, "CHECKOUT_SOURCE")
    result = subprocess.run(["git", "rev-parse", "HEAD"], capture_output=True, check=True)
    require(result.stdout == (expected + "\n").encode("ascii"), "CHECKOUT_IDENTITY")


def main():
    try:
        # All HTTP reads remain on this fixed historical subject; the caller cannot select a target.
        for name in ("GITHUB_SHA", "GITHUB_RUN_ID", "CALLER_PR_NUMBER"):
            pattern = r"[a-f0-9]{40}" if name == "GITHUB_SHA" else r"[1-9][0-9]{0,19}"
            require(re.fullmatch(pattern, os.environ.get(name, "")) is not None, "CALLER_IDENTITY")
        for path, digest in PARSER_PINS.items():
            require(subprocess.run(["git", "hash-object", str(HERE / path)],
                capture_output=True, check=True).stdout.decode().strip() == digest, "UNCHANGED_PARSER")
        verify_checkout(os.environ["GITHUB_SHA"])
        client = archive_tools.GitHub(os.environ.get("READ_TOKEN", ""))
        caller_run = client.json("actions/runs/" + os.environ["GITHUB_RUN_ID"])
        caller_pr = client.json("pulls/" + os.environ["CALLER_PR_NUMBER"])
        caller_commit = client.json("git/commits/" + os.environ["GITHUB_SHA"])
        validate_caller(os.environ, caller_run, caller_pr, caller_commit)
        run = client.json("actions/runs/" + str(SUBJECT["run"]))
        artifact = client.json("actions/artifacts/" + str(SUBJECT["artifact"]))
        commit = client.json("git/commits/" + SUBJECT["merge"])
        verify_subject_metadata(SUBJECT, run, artifact, commit)
        job = client.json("actions/jobs/" + str(SUBJECT["collaboration"]))
        require(job["id"] == SUBJECT["collaboration"] and job["run_id"] == SUBJECT["run"]
            and type(job["run_attempt"]) is int and job["run_attempt"] == 1
            and job["status"] == "completed" and job["conclusion"] == "failure", "JOB_IDENTITY")
        projection = inspect_subject(SUBJECT, client.download(
            "actions/artifacts/" + str(SUBJECT["artifact"]) + "/zip", 262144))
        native = inspect_log(SUBJECT, client.download(
            "actions/jobs/" + str(SUBJECT["collaboration"]) + "/logs", 16777216))
        verify_checkout(os.environ["GITHUB_SHA"])
        report = dict(schemaVersion=1, kind="BOUNDED_THREAD_DENIAL_FIRST_FAILURE_TRIAGE",
            inspection="VERIFIED_METADATA_AND_BYTES", subject=SUBJECT, nativeLog=native, projection=projection,
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
    except Exception:
        print("PINNED_THREAD_DENIAL_TRIAGE_REFUSED", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
