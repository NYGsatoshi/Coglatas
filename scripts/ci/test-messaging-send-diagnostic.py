"""Contract and privacy controls for one pinned Message POST timeout artifact."""
import importlib.util
import io
import json
from pathlib import Path
import unittest
import zipfile

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("send_failure_triage", HERE / "triage-messaging-send-diagnostic.py")
triage = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(triage)
CANARY = "PRIVATE_COOKIE_PAYLOAD_URL_MUST_NEVER_BE_PUBLISHED"


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


def diagnostic():
    data = {key: value for key, value in triage.lane(triage.SUBJECT).items() if key != "journeys"}
    data.update(schemaVersion=1, capturePolicy="allowlisted-functional-v1", producer="playwright-reporter",
        artifactName=triage.archive_tools.NAME, journeys=[])
    for owner, passed in (("FUNC-MSG-001", False), ("FUNC-NOTIF-001", True)):
        attempt = dict(retry=0, status="passed" if passed else "failed",
            failureKind="NONE" if passed else "TIMEOUT", failedStepId=None if passed else "MSG-THREAD",
            completedStepIds=["NOTIF-01"] if passed else ["MSG-01", "MSG-02", "MSG-03", "MSG-04"],
            browserEvidenceState="COMPLETE", browser=browser())
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
    record = {**triage.SUBJECT, "size": len(raw), "digest": triage.sha(raw)}
    return record, raw


def native():
    r = triage.SUBJECT
    return (f"HEAD is now at {r['merge'][:7]} Merge {r['head']} into {r['base']}\n"
        "page.waitForResponse: Timeout 20000ms exceeded\n"
        "collaboration-owner-journey.spec.ts:251:18\n"
        "- FUNC-MSG-001: FAIL; attempts=1; duration=24832 ms\n"
        "- FUNC-NOTIF-001: PASS; attempts=1; duration=1100 ms\n"
        "Browser observation: COMPLETE; retry=0\nBrowser observation: COMPLETE; retry=0\n"
        "failed steps=MSG-THREAD / TIMEOUT\n" + CANARY).encode()


class SendFailureTriageContracts(unittest.TestCase):
    def test_valid_closed_failure_preserves_limits_and_functional_fail(self):
        output = triage.project_diagnostics(diagnostic(), triage.SUBJECT)
        self.assertEqual(output["functionalResult"], "FAIL")
        self.assertEqual(output["sourceLine"], 251)
        self.assertFalse(output["recoveryBarrierReached"])
        self.assertEqual(output["networkTruncated"], 7)
        self.assertEqual(output["messaging"]["eventsTruncated"], 3)
        self.assertEqual(output["messaging"]["controlsTruncated"], 2)
        self.assertEqual(len(output["primaryContextMessagePostRows"]), 2)
        self.assertEqual(len(output["limits"]), 4)

    def test_missing_messaging_is_explicit_not_invented_success(self):
        data = diagnostic()
        data["journeys"][0]["attempts"][0]["browser"]["messaging"] = None
        output = triage.project_diagnostics(data, triage.SUBJECT)
        self.assertIsNone(output["messaging"])
        self.assertEqual(output["functionalResult"], "FAIL")

    def test_wrong_source_or_attempt_is_refused(self):
        for key, value in (("commitSha", "0" * 40), ("runId", "1"), ("runAttempt", "2")):
            data = diagnostic()
            data[key] = value
            with self.subTest(key=key), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_recovery_failure_cannot_be_substituted_for_prethread_timeout(self):
        data = diagnostic()
        data["journeys"][0]["attempts"][0].update(failedStepId="MSG-RECOVERY", failureKind="ASSERTION")
        with self.assertRaises(ValueError):
            triage.project_diagnostics(data, triage.SUBJECT)

    def test_recovery_completion_cannot_be_claimed(self):
        data = diagnostic()
        data["journeys"][0]["attempts"][0]["completedStepIds"].append("MSG-RECOVERY")
        with self.assertRaises(ValueError):
            triage.project_diagnostics(data, triage.SUBJECT)

    def test_retry_or_missing_owner_is_refused(self):
        for mutate in (
                lambda data: data["journeys"][0]["attempts"][0].update(retry=1),
                lambda data: data["journeys"].pop(),
                lambda data: data["journeys"][0].update(evidenceState="MISSING")):
            data = diagnostic()
            mutate(data)
            with self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_unknown_secret_field_at_every_observation_level_is_refused(self):
        for target in ("root", "browser", "messaging", "event", "control", "network"):
            data = diagnostic()
            view = data["journeys"][0]["attempts"][0]["browser"]
            objects = dict(root=data, browser=view, messaging=view["messaging"],
                event=view["messaging"]["events"][0], control=view["messaging"]["controls"][0],
                network=view["network"][0])
            objects[target]["rawPrivateValue"] = CANARY
            with self.subTest(target=target), self.assertRaises(ValueError):
                triage.project_diagnostics(data, triage.SUBJECT)

    def test_native_log_only_emits_hash_size_and_provider_retention(self):
        output = triage.inspect_log(triage.SUBJECT, native())
        self.assertEqual(set(output), {"sha256", "size", "retained"})
        self.assertNotIn(CANARY, json.dumps(output))

    def test_native_wrong_location_source_or_control_cannot_qualify(self):
        raw = native()
        for before, after in ((b"251:18", b"262:18"), (b"24832", b"24833"),
                              (b"NOTIF-001: PASS", b"NOTIF-001: FAIL"),
                              (triage.SUBJECT["base"].encode(), b"0" * 40),
                              (b"Timeout 20000ms", b"Timeout 15000ms")):
            with self.assertRaises(ValueError):
                triage.inspect_log(triage.SUBJECT, raw.replace(before, after))

    def test_valid_whole_zip_uses_same_closed_observation_contract(self):
        record, raw = zipped(diagnostic())
        output = triage.inspect_subject(record, raw)
        self.assertEqual(output["failureKind"], "TIMEOUT")
        self.assertEqual(output["attempts"], 1)

    def test_changed_digest_or_unaccounted_zip_suffix_is_refused(self):
        record, raw = zipped(diagnostic())
        with self.assertRaises(ValueError):
            triage.inspect_subject({**record, "digest": "0" * 64}, raw)
        changed = raw + b"UNACCOUNTED"
        with self.assertRaises(ValueError):
            triage.inspect_subject({**record, "size": len(changed), "digest": triage.sha(changed)}, changed)

    def test_unapproved_archive_member_is_refused_before_publication(self):
        record, raw = zipped(diagnostic(), extra=True)
        with self.assertRaises(ValueError):
            triage.inspect_subject(record, raw)

    def test_duplicate_json_keys_are_refused(self):
        with self.assertRaises(ValueError):
            triage.archive_tools.closed_json(b'{"status":"FAIL","status":"PASS"}')

    def test_png_reference_requires_real_matching_safe_bytes(self):
        data = diagnostic()
        view = data["journeys"][0]["attempts"][0]["browser"]
        view["structuralSnapshotState"] = "CAPTURED"
        view["structuralSnapshot"] = dict(name="structural-collaboration-FUNC-MSG-001-"
            + triage.SUBJECT["merge"] + "-" + str(triage.SUBJECT["run"]) + "-1-0.png", sha256="0" * 64)
        record, raw = zipped(data)
        with self.assertRaises(ValueError):
            triage.inspect_subject(record, raw)


if __name__ == "__main__":
    unittest.main()
