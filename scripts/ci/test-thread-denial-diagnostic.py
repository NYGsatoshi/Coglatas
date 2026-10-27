"""Closed identity, privacy and native-failure controls for a historical Thread timeout."""
import importlib.util
import io
import json
from pathlib import Path
import unittest
from unittest.mock import patch
import zipfile

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("thread_denial_triage", HERE / "triage-thread-denial-diagnostic.py")
triage = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(triage)
CANARY = "PRIVATE_COOKIE_BODY_URL_IDENTIFIER_MUST_NOT_BE_PUBLISHED"


def browser():
    return dict(schemaVersion=3, scope="primary-browser-context", network=[
        dict(event="REQUEST", requestSequence=1, method="POST", operation="message", status=None),
        dict(event="RESPONSE", requestSequence=1, method="POST", operation="message", status=200)],
        networkTruncated=7, consoleCounts=dict(error=0, warning=0, info=0, debug=0, other=0, pageError=0),
        projection={key: None for key in ("filesPage", "uploaderEnabled", "inventoryRows",
            "selectedRows", "detailPane", "detailHeading", "downloadEnabled", "errorIndicator")},
        projectionCheckpoint="failed-step", structuralSnapshotState="NOT_REQUIRED",
        structuralSnapshot=None, composerFill=dict(startedAtMs=10, finishedAtMs=20, outcome="resolved"),
        messaging=dict(documentStartedAtMs=1, storageReadable=True, storageWritable=True,
            webSocketObserved=True, eventsTruncated=3, controlsTruncated=2,
            events=[dict(atMs=12, kind="input", connection="Connected", composerGeneration=1,
                composerPresent=True, draftEnabled=True, sendEnabled=True, draftFocused=True),
                dict(atMs=25, kind="replacement", connection="Connected", composerGeneration=2,
                composerPresent=True, draftEnabled=False, sendEnabled=False, draftFocused=False)],
            controls=[dict(atMs=24, openedAtMs=2, kind="AuthorizationInvalidated")]))


def probe():
    target = {key: True for key in ("singleTab", "visible", "enabled", "selected", "inViewport",
                                    "centerReceivesPointer", "workspaceExpanded")}
    kinds = [
        ("AUTHORIZED_OBSERVER_REGISTERED", None, None), ("REQUEST", 1, None),
        ("AUTHORIZED_SELECTED", 1, None), ("RESPONSE", 1, 200), ("AUTHORIZED_READ", None, None),
        ("ROOT_ASSERTION_STARTED", None, None), ("ROOT_ASSERTION_PASSED", None, None),
        ("SESSION_REPLACEMENT_STARTED", None, None), ("SESSION_REPLACEMENT_COMPLETED", None, None),
        ("SESSION_VERIFIED", None, None), ("REQUEST", 2, None),
        ("OBSERVER_REGISTERED", None, None), ("ACTION_STARTED", None, None),
        ("ACTION_COMPLETED", None, None), ("RESPONSE", 2, 400)]
    dom = {prefix + suffix: False for prefix in ("workspace", "loading", "error", "root")
           for suffix in ("Present", "Visible")}
    dom.update({key: False for key in ("pagePresent", "conversationPresent", "threadOpenClass", "groupClass")})
    shape = {key: True for key in ("objectShape", "rootMatches", "conversationMatches", "summaryMatches",
                                  "replyIdentitiesMatch", "countsConsistent")}
    return dict(state="AVAILABLE", data=dict(phases=["OBSERVER_REGISTERED", "ACTION_STARTED", "ACTION_COMPLETED"],
        target=target, observation=dict(events=[dict(kind=k, request=r, status=s) for k, r, s in kinds],
            elapsedMs=list(range(len(kinds))), truncated=False, orphanResponse=False, freshRequest=None,
            outcome="FAILED", classification="INCOMPLETE", actionFailure="NONE", failureTarget=target),
        initial=dict(responseShape=shape, failureDom=dom, preActionDeniedDom=dom)))


def diagnostic():
    data = {key: value for key, value in triage.lane(triage.SUBJECT).items() if key != "journeys"}
    data.update(schemaVersion=1, capturePolicy="allowlisted-functional-v1", producer="playwright-reporter",
        artifactName=triage.archive_tools.NAME, journeys=[])
    for owner, passed in (("FUNC-MSG-001", False), ("FUNC-NOTIF-001", True)):
        attempt = dict(retry=0, status="passed" if passed else "timedOut",
            failureKind="NONE" if passed else "UNCLASSIFIED_FAILURE",
            failedStepId=None if passed else "MSG-THREAD-DENIAL",
            completedStepIds=["NOTIF-01"] if passed else ["MSG-01", "MSG-02", "MSG-03", "MSG-04", "MSG-THREAD"],
            browserEvidenceState="COMPLETE", browser=browser())
        if not passed:
            attempt.update(threadProbe=probe(), threadProbeSecondary=dict(state="MISSING", stages=[]))
            attempt["browser"]["network"] = [
                dict(event="REQUEST", requestSequence=44, method="GET", operation="message.thread", status=None),
                dict(event="RESPONSE", requestSequence=44, method="GET", operation="message.thread", status=400)]
        data["journeys"].append(dict(journeyId=owner, status="PASS" if passed else "FAIL",
            expectedStatus="passed", evidenceState="COMPLETE", reason="NONE" if passed else "FAIL",
            attempts=[attempt]))
    return data


def zipped(data, extra=False):
    stream = io.BytesIO()
    with zipfile.ZipFile(stream, "w", compression=zipfile.ZIP_DEFLATED) as archive:
        archive.writestr("diagnostics-collaboration.json", json.dumps(data))
        if extra:
            archive.writestr("unapproved-private.txt", CANARY)
    raw = stream.getvalue()
    return {**triage.SUBJECT, "size": len(raw), "digest": triage.sha(raw)}, raw


def native():
    r = triage.SUBJECT
    return (f"HEAD is now at {r['merge'][:7]} Merge {r['head']} into {r['base']}\n"
        "Error: page.waitForRequest: Test timeout of 120000ms exceeded.\n"
        "collaboration-owner-journey.spec.ts:433:38\n"
        "- FUNC-MSG-001: FAIL; attempts=1; duration=120201 ms\n"
        "- FUNC-NOTIF-001: PASS; attempts=1; duration=1068 ms\n"
        "Browser observation: COMPLETE; retry=0\nBrowser observation: COMPLETE; retry=0\n"
        "failed steps=MSG-THREAD-DENIAL / UNCLASSIFIED_FAILURE\n" + CANARY).encode()


def caller():
    env = dict(GITHUB_EVENT_NAME="pull_request", GITHUB_REPOSITORY=triage.archive_tools.REPO,
        GITHUB_HEAD_REF=triage.BRANCH, GITHUB_SHA="a" * 40, CALLER_HEAD_SHA="b" * 40,
        CALLER_BASE_SHA="c" * 40, GITHUB_RUN_ID="900001", GITHUB_RUN_ATTEMPT="1", CALLER_PR_NUMBER="900002")
    run = dict(id=900001, run_attempt=1, head_sha=env["CALLER_HEAD_SHA"], event="pull_request",
        path=triage.WORKFLOW, repository=dict(full_name=triage.archive_tools.REPO))
    pr = dict(number=900002, state="open", draft=True, merged=False, auto_merge=None,
        head=dict(sha=env["CALLER_HEAD_SHA"], ref=triage.BRANCH, repo=dict(full_name=triage.archive_tools.REPO)),
        base=dict(ref="main", sha="d" * 40))
    commit = dict(sha=env["GITHUB_SHA"], parents=[dict(sha=env["CALLER_BASE_SHA"]), dict(sha=env["CALLER_HEAD_SHA"])])
    return env, run, pr, commit


class ThreadDenialTriageContracts(unittest.TestCase):
    def test_valid_partial_observation_keeps_original_failure_and_separate_sequence_namespaces(self):
        output = triage.project_diagnostics(diagnostic(), triage.SUBJECT)
        self.assertEqual(output["functionalResult"], "FAIL")
        self.assertEqual(output["failureKind"], "UNCLASSIFIED_FAILURE")
        self.assertEqual(output["attemptStatus"], "timedOut")
        self.assertTrue(output["priorThreadStepCompleted"])
        self.assertFalse(output["recoveryBarrierReached"])
        observation = output["threadProbe"]["data"]["observation"]
        self.assertIsNone(observation["freshRequest"])
        self.assertEqual(observation["classification"], "INCOMPLETE")
        self.assertEqual(output["primaryContextMessagingNetwork"][1]["requestSequence"], 44)
        self.assertEqual(observation["events"][-1]["request"], 2)
        self.assertEqual(output["networkTruncated"], 7)
        self.assertEqual(output["messaging"]["eventsTruncated"], 3)
        self.assertEqual(len(output["limits"]), 7)
        self.assertNotIn(CANARY, json.dumps(output))

    def test_actual_outer_timeout_status_is_preserved_and_other_statuses_refused(self):
        for status in ("failed", "passed", "interrupted", "skipped"):
            data = diagnostic()
            data["journeys"][0]["attempts"][0]["status"] = status
            with self.subTest(status=status), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_checkout_verifier_binds_the_actual_fixed_git_head_without_emitting_bytes(self):
        expected = "a" * 40
        result = triage.subprocess.CompletedProcess([], 0, stdout=(expected + "\n").encode())
        with patch.object(triage.subprocess, "run", return_value=result) as command:
            triage.verify_checkout(expected)
            command.assert_called_once_with(["git", "rev-parse", "HEAD"], capture_output=True, check=True)
        for output in ((("b" * 40) + "\n").encode(), (expected + "\n" + CANARY).encode()):
            wrong = triage.subprocess.CompletedProcess([], 0, stdout=output)
            with patch.object(triage.subprocess, "run", return_value=wrong), self.assertRaises(ValueError):
                triage.verify_checkout(expected)

    def test_missing_probe_and_messaging_remain_missing_not_invented_positive(self):
        data = diagnostic()
        attempt = data["journeys"][0]["attempts"][0]
        del attempt["threadProbe"]
        del attempt["threadProbeSecondary"]
        attempt["browser"]["messaging"] = None
        output = triage.project_diagnostics(data, triage.SUBJECT)
        self.assertEqual(output["threadProbe"], dict(state="MISSING", data=None))
        self.assertIsNone(output["messaging"])
        self.assertEqual(output["functionalResult"], "FAIL")

    def test_exact_diagnostic_source_and_attempt_are_required(self):
        for key, value in (("commitSha", "0" * 40), ("runId", "1"), ("runAttempt", "2")):
            data = diagnostic()
            data[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_old_post_recovery_or_timeout_classification_cannot_substitute(self):
        for step, kind in (("MSG-THREAD", "TIMEOUT"), ("MSG-RECOVERY", "ASSERTION"),
                           ("MSG-THREAD-DENIAL", "TIMEOUT")):
            data = diagnostic()
            data["journeys"][0]["attempts"][0].update(failedStepId=step, failureKind=kind)
            with self.subTest(step=step, kind=kind), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_prior_thread_positive_and_unreached_recovery_contract_required(self):
        for mode in ("missing-positive", "denial-completed", "recovery-completed"):
            data = diagnostic()
            steps = data["journeys"][0]["attempts"][0]["completedStepIds"]
            if mode == "missing-positive":
                steps.remove("MSG-THREAD")
            else:
                steps.append("MSG-RECOVERY" if mode == "recovery-completed" else "MSG-THREAD-DENIAL")
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_retry_missing_owner_incomplete_browser_and_notification_fail_refused(self):
        for mode in ("retry", "owner", "incomplete", "browser", "notification"):
            data = diagnostic()
            if mode == "retry":
                data["journeys"][0]["attempts"][0]["retry"] = 1
            elif mode == "owner":
                data["journeys"].pop()
            elif mode == "incomplete":
                data["journeys"][0]["evidenceState"] = "MISSING"
            elif mode == "browser":
                data["journeys"][0]["attempts"][0]["browserEvidenceState"] = "MISSING"
            else:
                data["journeys"][1]["attempts"][0].update(status="failed", failureKind="ASSERTION")
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_unknown_private_fields_rejected_at_each_projection_boundary(self):
        for target in ("root", "attempt", "probe", "observation", "thread-event", "initial", "dom",
                       "browser", "messaging", "event", "control", "network"):
            data = diagnostic()
            attempt = data["journeys"][0]["attempts"][0]
            view, probe_data = attempt["browser"], attempt["threadProbe"]["data"]
            objects = dict(root=data, attempt=attempt, probe=probe_data, observation=probe_data["observation"],
                initial=probe_data["initial"], dom=probe_data["initial"]["failureDom"], browser=view,
                messaging=view["messaging"], event=view["messaging"]["events"][0],
                control=view["messaging"]["controls"][0], network=view["network"][0])
            objects["thread-event"] = probe_data["observation"]["events"][0]
            objects[target]["rawPrivateValue"] = CANARY
            with self.subTest(target=target), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_unapproved_network_category_or_raw_header_is_refused(self):
        for key, value in (("operation", "private.internal.route"), ("rawHeaders", CANARY)):
            data = diagnostic()
            data["journeys"][0]["attempts"][0]["browser"]["network"][0][key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_observer_timing_order_and_fresh_selection_cannot_be_fabricated(self):
        for mode in ("timing", "fresh", "interleaving"):
            data = diagnostic()
            observation = data["journeys"][0]["attempts"][0]["threadProbe"]["data"]["observation"]
            if mode == "timing":
                observation["elapsedMs"][0] = 100
            elif mode == "fresh":
                observation["freshRequest"] = 2
            else:
                observation["events"][-1]["status"] = 200
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_native_returns_only_whole_log_digest_size_and_provider_custody(self):
        output = triage.inspect_log(triage.SUBJECT, native())
        self.assertEqual(set(output), {"sha256", "size", "retained"})
        self.assertNotIn(CANARY, json.dumps(output))

    def test_native_wrong_location_source_timeout_kind_or_owner_result_refused(self):
        for before, after in ((b"433:38", b"251:18"), (b"120201", b"120202"),
                              (b"NOTIF-001: PASS", b"NOTIF-001: FAIL"),
                              (triage.SUBJECT["base"].encode(), b"0" * 40),
                              (b"120000ms", b"20000ms"), (b"UNCLASSIFIED_FAILURE", b"TIMEOUT")):
            with self.subTest(before=before), self.assertRaises(ValueError):
                triage.inspect_log(triage.SUBJECT, native().replace(before, after))

    def test_closed_whole_zip_uses_same_original_admission(self):
        record, raw = zipped(diagnostic())
        output = triage.inspect_subject(record, raw)
        self.assertEqual(output["failedStep"], "MSG-THREAD-DENIAL")
        self.assertEqual(output["attempts"], 1)

    def test_changed_digest_or_unaccounted_zip_suffix_refused(self):
        record, raw = zipped(diagnostic())
        with self.assertRaises(ValueError):
            triage.inspect_subject({**record, "digest": "0" * 64}, raw)
        appended = raw + b"UNACCOUNTED"
        with self.assertRaises(ValueError):
            triage.inspect_subject({**record, "size": len(appended), "digest": triage.sha(appended)}, appended)

    def test_unapproved_zip_member_and_duplicate_json_keys_refused(self):
        record, raw = zipped(diagnostic(), extra=True)
        with self.assertRaises(ValueError):
            triage.inspect_subject(record, raw)
        with self.assertRaises(ValueError):
            triage.archive_tools.closed_json(b'{"status":"FAIL","status":"PASS"}')

    def test_png_reference_requires_matching_real_safe_bytes(self):
        data = diagnostic()
        view = data["journeys"][0]["attempts"][0]["browser"]
        view.update(structuralSnapshotState="CAPTURED", structuralSnapshot=dict(
            name="structural-collaboration-FUNC-MSG-001-" + triage.SUBJECT["merge"]
                 + "-" + str(triage.SUBJECT["run"]) + "-1-0.png", sha256="0" * 64))
        record, raw = zipped(data)
        with self.assertRaises(ValueError):
            triage.inspect_subject(record, raw)

    def test_failure_projection_accepts_only_fixed_phase_and_exact_literal_refusal(self):
        output = triage.failure_record("SUBJECT_ZIP_INSPECTION", triage.archive_tools.Refused("PINNED_FAILURE_CONTRACT"))
        self.assertEqual(output["inspection"], "NOT_VERIFIED")
        self.assertEqual(output["phase"], "SUBJECT_ZIP_INSPECTION")
        self.assertEqual(output["code"], "PINNED_FAILURE_CONTRACT")
        self.assertEqual(set(output), {"schemaVersion", "kind", "inspection", "subjectResult", "releaseStatus", "phase", "code"})

    def test_unknown_exception_args_and_known_looking_other_errors_never_leak(self):
        errors = [ValueError(CANARY), ValueError("RUN_IDENTITY"), triage.archive_tools.Refused(CANARY),
                  triage.archive_tools.Refused(), triage.archive_tools.Refused("RUN_IDENTITY", CANARY)]
        for error in errors:
            output = triage.failure_record("SUBJECT_ZIP_INSPECTION", error)
            self.assertEqual(output["code"], "UNKNOWN")
            self.assertNotIn(CANARY, json.dumps(output))
            self.assertEqual(output["inspection"], "NOT_VERIFIED")

    def test_unknown_phase_is_never_echoed(self):
        output = triage.failure_record(CANARY, triage.archive_tools.Refused("API_STATUS"))
        self.assertEqual(output["phase"], "UNKNOWN")
        self.assertNotIn(CANARY, json.dumps(output))

    def test_exception_stringification_is_not_used_even_on_refusal(self):
        class PoisonedError(Exception):
            def __str__(self):
                raise AssertionError("Exception text must not be accessed.")
            def __repr__(self):
                raise AssertionError("Exception representation must not be accessed.")
        output = triage.failure_record("SUBJECT_BINDING", PoisonedError(CANARY))
        self.assertEqual(output["code"], "UNKNOWN")
        self.assertNotIn(CANARY, json.dumps(output))

    def test_subject_metadata_keeps_original_integer_attempt_admission(self):
        record = triage.SUBJECT
        run = dict(id=record["run"], run_attempt=1, head_sha=record["head"], event="pull_request",
            path=".github/workflows/ci.yml", status="completed", conclusion="failure",
            repository=dict(full_name=triage.archive_tools.REPO))
        artifact = dict(id=record["artifact"], name=triage.archive_tools.NAME, size_in_bytes=record["size"],
            expired=False, digest="sha256:" + record["digest"], expires_at=record["expires"],
            workflow_run=dict(id=record["run"], head_sha=record["head"]))
        commit = dict(sha=record["merge"], tree=dict(sha=record["tree"]),
            parents=[dict(sha=record["base"]), dict(sha=record["head"])])
        triage.verify_subject_metadata(record, run, artifact, commit)
        for attempt in (True, "1", 0, 2):
            run["run_attempt"] = attempt
            with self.subTest(attempt=attempt), self.assertRaises(ValueError):
                triage.verify_subject_metadata(record, run, artifact, commit)

    def test_caller_binds_own_source_not_mutable_historical_subject_pr_head(self):
        args = caller()
        triage.validate_caller(*args)
        args[2]["base"]["sha"] = "e" * 40
        triage.validate_caller(*args)
        args[1]["pull_requests"] = [dict(head=dict(sha="f" * 40))]
        triage.validate_caller(*args)

    def test_wrong_caller_branch_repo_pr_state_or_source_refused(self):
        for mode in ("branch", "repo", "ready", "auto", "head", "parent", "run", "attempt"):
            args = caller()
            env, run, pr, commit = args
            if mode == "branch":
                env["GITHUB_HEAD_REF"] = "another-branch"
            elif mode == "repo":
                pr["head"]["repo"]["full_name"] = "other/repository"
            elif mode == "ready":
                pr["draft"] = False
            elif mode == "auto":
                pr["auto_merge"] = {}
            elif mode == "head":
                pr["head"]["sha"] = "f" * 40
            elif mode == "parent":
                commit["parents"].reverse()
            elif mode == "run":
                run["path"] = ".github/workflows/another.yml"
            else:
                run["run_attempt"] = True
            with self.subTest(mode=mode), self.assertRaises(ValueError):
                triage.validate_caller(*args)


if __name__ == "__main__":
    unittest.main()
