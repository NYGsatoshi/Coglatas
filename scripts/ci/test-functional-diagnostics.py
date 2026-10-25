"""Check exact-run privacy boundaries and refusal of malformed diagnostic artifacts."""
import copy
import json
import hashlib
import struct
import zlib
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from functional_diagnostics import missing_diagnostics, validate_diagnostics, validate_snapshot_files
from thread_probe_diagnostics import validate_thread_projection

SHA = "a" * 40
PROTECTED = "private-protected-payload-must-not-appear"


def lane():
    return {"schemaVersion": 1, "commitSha": SHA, "gate": "functional-full", "suite": "core", "runId": "100", "runAttempt": "1",
            "startedAt": "2026-10-04T01:00:00Z", "completedAt": "2026-10-04T01:01:00Z", "setupSeconds": 1, "testSeconds": 2,
            "journeys": [{"journeyId": "FUNC-TASK-001", "status": "FAIL", "attempts": 1, "durationMs": 10}]}


def diagnostics():
    data = missing_diagnostics(lane())
    data["producer"] = "playwright-reporter"
    data["journeys"][0].update(evidenceState="COMPLETE", reason="FAIL", expectedStatus="passed", attempts=[{
        "retry": 0, "status": "failed", "failureKind": "ASSERTION", "failedStepId": "STEP-05", "completedStepIds": ["STEP-01", "STEP-02"],
        "browserEvidenceState": "COMPLETE", "browser": {
            "schemaVersion": 1, "scope": "primary-browser-context", "networkTruncated": 0,
            "network": [{"event": "RESPONSE", "requestSequence": 1, "method": "GET", "operation": "files.detail", "status": 200}],
            "consoleCounts": dict(error=1, warning=0, info=0, debug=0, other=0, pageError=0),
            "projection": dict(filesPage=True, uploaderEnabled=True, inventoryRows=True, selectedRows=False,
                               detailPane=True, detailHeading=False, downloadEnabled=False, errorIndicator=False),
            "projectionCheckpoint": "after-test-cleanup", "structuralSnapshotState": "UNAVAILABLE", "structuralSnapshot": None,
        },
    }])
    return data


class FunctionalDiagnosticTests(unittest.TestCase):
    def test_actual_node_thread_projection_and_strict_consumer_reject_mutations(self):
        # Actual JS producer and Python consumer must agree; this is mocked metadata, not a product run.
        script = """
          import {EventEmitter} from 'node:events';
          import {observeThreadRequests} from './tests/functional/helpers/thread-probe-observation.mjs';
          import {THREAD_PROBE_ATTACHMENT} from './tests/functional/helpers/thread-probe-diagnostics.mjs';
          import {threadResponseShape} from './tests/functional/helpers/thread-initial-observation.mjs';
          import {attemptDiagnostic} from './tests/functional/fixtures/functional-diagnostics.mjs';
          import Reporter from './tests/functional/fixtures/functional-evidence-reporter.mjs';
          import {mkdirSync,mkdtempSync,writeFileSync,readFileSync,rmSync} from 'node:fs';
          import {tmpdir} from 'node:os';
          import {join} from 'node:path';
          const page=new EventEmitter(), observation=observeThreadRequests(page,()=>true), request={};
          observation.mark('AUTHORIZED_OBSERVER_REGISTERED');page.emit('request',request);observation.selectAuthorized(request);
          page.emit('response',{request:()=>request,status:()=>200});observation.mark('AUTHORIZED_READ');
          const value={phases:[],target:null,observation:observation.finish('FAILED'),initial:{failureDom:null,
            responseShape:threadResponseShape({rootMessage:{id:'private',conversationId:'private'},summary:{threadRootMessageId:'private',replyCount:0},replies:[],hasMore:false,maximumReplies:100},'private','private')}};
          const directory=mkdtempSync(join(tmpdir(),'thread-consumer-')),before=process.cwd();
          try {
            process.chdir(directory);mkdirSync('tests/functional',{recursive:true});mkdirSync('artifacts/functional',{recursive:true});
            writeFileSync('tests/functional/quarantine.json','{"entries":[]}');writeFileSync('artifacts/functional/setup-collaboration.json','{"setupSeconds":1}');
            Object.assign(process.env,{COGLATAS_FUNCTIONAL_DOMAIN:'collaboration',COGLATAS_FUNCTIONAL_SELECTED_GATES:'functional-fast',TARGET_SHA:'a'.repeat(40),GITHUB_RUN_ID:'100',GITHUB_RUN_ATTEMPT:'1'});
            const tests=['FUNC-MSG-001','FUNC-NOTIF-001'].map(id=>({expectedStatus:'passed',annotations:[{type:'journey',description:id},{type:'backend',description:'real'}],results:[{status:id==='FUNC-MSG-001'?'failed':'passed',retry:0,duration:1,steps:id==='FUNC-MSG-001'?[{category:'test.step',title:'FUNC-MSG-001 / MSG-THREAD-DENIAL',error:{matcherResult:{}}}]:[],attachments:id==='FUNC-MSG-001'?[{name:THREAD_PROBE_ATTACHMENT,body:Buffer.from(JSON.stringify(value))}]:[]}]}));
            const reporter=new Reporter();reporter.onBegin({}, {allTests:()=>tests});reporter.onEnd({status:'failed'});
            console.log(readFileSync('artifacts/functional/diagnostics-collaboration.json','utf8'));
          } finally {process.chdir(before);rmSync(directory,{recursive:true,force:true});}
        """
        result = subprocess.run(["node", "--input-type=module", "-e", script], check=True, capture_output=True, text=True)
        data = json.loads(result.stdout)
        attempt = data["journeys"][0]["attempts"][0]
        self.assertNotIn('private', result.stdout)
        validate_thread_projection(attempt["threadProbe"], attempt["threadProbeSecondary"])
        current_lane = lane()
        current_lane["suite"] = "collaboration"
        current_lane["gate"] = "functional-fast"
        current_lane["journeys"] = [{"journeyId": "FUNC-MSG-001", "status": "FAIL", "attempts": 1, "durationMs": 1},
                                    {"journeyId": "FUNC-NOTIF-001", "status": "PASS", "attempts": 1, "durationMs": 1}]
        self.assertEqual(data, validate_diagnostics(data, current_lane))
        probe = attempt["threadProbe"]["data"]
        mutations = [lambda value: value.update(private=PROTECTED),
                     lambda value: value["initial"].update(private=PROTECTED),
                     lambda value: value["initial"]["responseShape"].update(rootMatches=PROTECTED),
                     lambda value: value["observation"].update(classification="PASS"),
                     lambda value: value["observation"]["events"][0].update(url=PROTECTED),
                     lambda value: value["observation"].update(freshRequest=True),
                     lambda value: value["observation"].update(events=value["observation"]["events"] * 6),
                     lambda value: value["observation"]["events"][2].update(request=24)]
        for mutate in mutations:
            altered = copy.deepcopy(probe)
            mutate(altered)
            with self.assertRaises(ValueError):
                validate_thread_projection({"state": "AVAILABLE", "data": altered}, attempt["threadProbeSecondary"])
        for state in ("MISSING", "INVALID"):
            validate_thread_projection({"state": state, "data": None}, {"state": "MISSING", "stages": []})
            with self.assertRaises(ValueError):
                validate_thread_projection({"state": state, "data": probe}, {"state": "MISSING", "stages": []})

    def test_messaging_v2_preserves_failure_and_refuses_private_or_unbounded_fields(self):
        data = diagnostics()
        browser = data["journeys"][0]["attempts"][0]["browser"]
        browser["schemaVersion"] = 2
        browser["messaging"] = {"documentStartedAtMs": 1000, "storageReadable": True, "storageWritable": False,
                                "eventsTruncated": 0, "webSocketObserved": True, "controlsTruncated": 0,
                                "events": [{"atMs": 1001, "kind": "input", "composerPresent": True,
                                            "draftEnabled": True, "sendEnabled": False, "draftFocused": True,
                                            "connection": "Connected"}],
                                "controls": [{"atMs": 1002, "openedAtMs": 1000, "kind": "AuthorizationInvalidated"}]}
        self.assertEqual(data, validate_diagnostics(data, lane()))
        self.assertEqual("FAIL", data["journeys"][0]["status"])
        for kind in ("beforeinput", "change", "replacement"):
            with self.subTest(kind=kind):
                browser["messaging"]["events"][0]["kind"] = kind
                self.assertEqual(data, validate_diagnostics(data, lane()))
                self.assertEqual("FAIL", data["journeys"][0]["status"],
                                 "new diagnostic tokens must not promote a failing journey")
        browser["messaging"]["events"][0]["kind"] = "input"
        for altered in ({**browser["messaging"], "body": PROTECTED},
                        {**browser["messaging"], "storageReadable": PROTECTED},
                        {**browser["messaging"], "events": [{**browser["messaging"]["events"][0], "kind": PROTECTED}]},
                        {**browser["messaging"], "events": browser["messaging"]["events"] * 49},
                        {**browser["messaging"], "controls": [{"atMs": True, "openedAtMs": 1000, "kind": "AuthorizationInvalidated"}]},
                        {**browser["messaging"], "controls": [{"atMs": 1002, "openedAtMs": 1000, "kind": PROTECTED}]}):
            rejected = copy.deepcopy(data)
            rejected["journeys"][0]["attempts"][0]["browser"]["messaging"] = altered
            selected_lane = lane()
            with self.assertRaises(ValueError):
                validate_diagnostics(rejected, selected_lane)
        browser["messaging"] = None
        self.assertEqual(data, validate_diagnostics(data, lane()), "unobserved is not a false clean result")

    def test_composer_fill_v3_rejects_private_fields_invalid_clocks_and_generation(self):
        data = diagnostics()
        browser = data["journeys"][0]["attempts"][0]["browser"]
        browser["schemaVersion"] = 3
        browser["messaging"] = {
            "documentStartedAtMs": 1000, "storageReadable": True, "storageWritable": True,
            "events": [{"atMs": 1001, "kind": "replacement", "composerGeneration": 2,
                        "composerPresent": True, "draftEnabled": True, "sendEnabled": False,
                        "draftFocused": False, "connection": "Connected"}],
            "eventsTruncated": 0, "webSocketObserved": True, "controls": [], "controlsTruncated": 0,
        }
        browser["composerFill"] = {"startedAtMs": 1000, "finishedAtMs": 1002, "outcome": "resolved"}
        self.assertEqual(data, validate_diagnostics(data, lane()))
        self.assertEqual("FAIL", data["journeys"][0]["status"])
        for altered in [
            {"startedAtMs": 1000, "finishedAtMs": 999, "outcome": "resolved"},
            {"startedAtMs": True, "finishedAtMs": 1002, "outcome": "resolved"},
            {"startedAtMs": 1000, "finishedAtMs": 1002, "outcome": PROTECTED},
            {"startedAtMs": 1000, "finishedAtMs": 1002, "outcome": []},
            {"startedAtMs": 1000, "finishedAtMs": 1002, "outcome": "resolved", "body": PROTECTED},
        ]:
            with self.subTest(altered=altered):
                rejected = copy.deepcopy(data)
                rejected["journeys"][0]["attempts"][0]["browser"]["composerFill"] = altered
                with self.assertRaises(ValueError):
                    validate_diagnostics(rejected, lane())
        for bad_generation in (True, -1, 1000001, PROTECTED):
            with self.subTest(generation=bad_generation):
                rejected = copy.deepcopy(data)
                rejected["journeys"][0]["attempts"][0]["browser"]["messaging"]["events"][0]["composerGeneration"] = bad_generation
                with self.assertRaises(ValueError):
                    validate_diagnostics(rejected, lane())
        for malformed_version in ([], {"version": 3}):
            rejected = copy.deepcopy(data)
            rejected["journeys"][0]["attempts"][0]["browser"]["schemaVersion"] = malformed_version
            with self.assertRaises(ValueError):
                validate_diagnostics(rejected, lane())
        historical = copy.deepcopy(data)
        prior = historical["journeys"][0]["attempts"][0]["browser"]
        prior["schemaVersion"] = 2
        del prior["composerFill"]
        del prior["messaging"]["events"][0]["composerGeneration"]
        self.assertEqual(historical, validate_diagnostics(historical, lane()),
                         "historical v2 records remain readable with their original fields")
        prior["schemaVersion"] = 1
        del prior["messaging"]
        self.assertEqual(historical, validate_diagnostics(historical, lane()),
                         "historical v1 records remain readable without Messaging")

    def test_thread_operation_tokens_remain_finite_and_do_not_promote_failure(self):
        data = diagnostics()
        event = data["journeys"][0]["attempts"][0]["browser"]["network"][0]
        for operation in ("message.thread", "message.thread-replies"):
            with self.subTest(operation=operation):
                event["operation"] = operation
                self.assertEqual(data, validate_diagnostics(data, lane()))
                self.assertEqual("FAIL", data["journeys"][0]["status"])
        for operation in ("message.thread-" + PROTECTED, "message.thread-replies." + PROTECTED):
            with self.subTest(operation=operation):
                event["operation"] = operation
                with self.assertRaises(ValueError):
                    validate_diagnostics(data, lane())

    def test_complete_failed_owner_has_stable_reason_and_exact_provenance(self):
        data = diagnostics()
        self.assertEqual(data, validate_diagnostics(data, lane()))
        self.assertEqual("STEP-05", data["journeys"][0]["attempts"][0]["failedStepId"])
        self.assertEqual("ASSERTION", data["journeys"][0]["attempts"][0]["failureKind"])

    def test_messaging_failed_checkpoint_accepts_only_existing_bounded_thread_tokens(self):
        collaboration_lane = lane()
        collaboration_lane["suite"] = "collaboration"
        step_ids = {"FUNC-MSG-001": "MSG-THREAD", "FUNC-NOTIF-001": "NOTIF-01", "FUNC-ANN-001": "ANN-01"}
        collaboration_lane["journeys"] = [dict(collaboration_lane["journeys"][0], journeyId=owner) for owner in step_ids]
        data = missing_diagnostics(collaboration_lane)
        data["producer"] = "playwright-reporter"
        template = diagnostics()["journeys"][0]
        data["journeys"] = []
        for owner, step in step_ids.items():
            journey = copy.deepcopy(template)
            journey["journeyId"] = owner
            attempt = journey["attempts"][0]
            attempt.update(failedStepId=step, completedStepIds=[])
            attempt["browser"]["projectionCheckpoint"] = "failed-step"
            data["journeys"].append(journey)
        attempt = data["journeys"][0]["attempts"][0]
        for step in ("MSG-THREAD", "MSG-THREAD-DENIAL", "MSG-RECOVERY"):
            with self.subTest(step=step):
                attempt["failedStepId"] = step
                self.assertEqual(data, validate_diagnostics(data, collaboration_lane))
        for step in ("MSG-THREAD-" + PROTECTED, "MSG-RECOVERY-UNKNOWN", "MSG-THREAD-DENIAL-EXTRA"):
            with self.subTest(step=step):
                attempt["failedStepId"] = step
                with self.assertRaises(ValueError):
                    validate_diagnostics(data, collaboration_lane)

    def test_missing_diagnostics_remain_explicit_and_never_imply_acceptance(self):
        data = missing_diagnostics(lane())
        self.assertEqual(data, validate_diagnostics(data, lane()))
        self.assertEqual("MISSING", data["journeys"][0]["evidenceState"])
        self.assertEqual("MISSING_EVIDENCE", data["journeys"][0]["reason"])

    def test_stale_sha_run_attempt_domain_and_untrusted_provenance_are_refused(self):
        for field, value in (("commitSha", "b" * 40), ("runId", "99"), ("runAttempt", "2"),
                             ("suite", "files"), ("gate", "functional-fast"), ("producer", "untrusted"),
                             ("artifactName", PROTECTED), ("capturePolicy", "raw-payloads")):
            with self.subTest(field=field):
                data = diagnostics()
                data[field] = value
                with self.assertRaises(ValueError):
                    validate_diagnostics(data, lane())

    def test_unknown_fields_content_strings_and_unbounded_scalars_are_refused(self):
        mutations = [
            lambda data: data.update(protectedBody=PROTECTED),
            lambda data: data["journeys"][0].update(title=PROTECTED),
            lambda data: data["journeys"][0]["attempts"][0].update(error=PROTECTED),
            lambda data: data["journeys"][0]["attempts"][0].update(failedStepId=PROTECTED),
            lambda data: data["journeys"][0]["attempts"][0].update(retry=True),
            lambda data: data["journeys"][0]["attempts"][0]["browser"].update(cookie=PROTECTED),
            lambda data: data["journeys"][0]["attempts"][0]["browser"].update(networkTruncated=float("inf")),
            lambda data: data["journeys"][0]["attempts"][0]["browser"]["consoleCounts"].update(error=PROTECTED),
            lambda data: data["journeys"][0]["attempts"][0]["browser"]["projection"].update(detailHeading=PROTECTED),
            lambda data: data["journeys"][0]["attempts"][0]["browser"]["network"][0].update(url=PROTECTED),
            lambda data: data["journeys"][0]["attempts"][0]["browser"].update(network=[{}] * 81),
            lambda data: data["journeys"][0].update(attempts=[]),
        ]
        for index, mutate in enumerate(mutations):
            with self.subTest(case=index):
                data = diagnostics()
                mutate(data)
                with self.assertRaises(ValueError):
                    validate_diagnostics(data, lane())

    def test_outcome_reason_and_retry_sequence_cannot_conflict_with_attempts(self):
        for mutate in (lambda data: data["journeys"][0].update(reason="NONE"),
                       lambda data: data["journeys"][0]["attempts"][0].update(status="passed", failureKind="NONE", failedStepId=None),
                       lambda data: data["journeys"][0]["attempts"][0].update(retry=77)):
            data = diagnostics()
            mutate(data)
            with self.assertRaises(ValueError):
                validate_diagnostics(data, lane())
        data = diagnostics()
        data["journeys"][0].update(status="PASS", reason="NONE")
        changed_lane = lane()
        changed_lane["journeys"][0]["status"] = "PASS"
        with self.assertRaises(ValueError):
            validate_diagnostics(data, changed_lane)

    def test_captured_png_requires_exact_identity_digest_and_valid_bounded_png(self):
        def chunk(kind, payload):
            return struct.pack(">I", len(payload)) + kind + payload + struct.pack(">I", zlib.crc32(kind + payload))
        png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 900, 1, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(b"\x00" + b"\x00" * 2700)) + chunk(b"IEND", b"")
        data = diagnostics()
        browser = data["journeys"][0]["attempts"][0]["browser"]
        name = f"structural-core-FUNC-TASK-001-{SHA}-100-1-0.png"
        browser.update(structuralSnapshotState="CAPTURED", structuralSnapshot={"name": name, "sha256": hashlib.sha256(png).hexdigest()})
        validate_diagnostics(data, lane())
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(OSError):
                validate_snapshot_files(data, directory)
            path = Path(directory) / name
            path.write_bytes(png)
            self.assertEqual([(name, png)], validate_snapshot_files(data, directory))
            for raw in (png + PROTECTED.encode(), png.replace(b"IDAT", b"tEXt", 1), b"\x89PNG\r\n\x1a\n" + PROTECTED.encode()):
                path.write_bytes(raw)
                browser["structuralSnapshot"]["sha256"] = hashlib.sha256(raw).hexdigest()
                with self.assertRaises(ValueError):
                    validate_snapshot_files(data, directory)
        browser["structuralSnapshot"]["name"] = name.replace("-100-1-0.png", "-100-2-0.png")
        with self.assertRaises(ValueError):
            validate_diagnostics(data, lane())

    def execute(self, data=None, raw=None, *, lane_data=None, missing_lane=False, return_lane_publication=False):
        script = Path(__file__).with_name("finalize-functional-lane.py").resolve()
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            metadata = root / "artifacts/functional"
            metadata.mkdir(parents=True)
            if not missing_lane:
                (metadata / "lane-core.json").write_text(json.dumps(lane() if lane_data is None else lane_data), encoding="utf-8")
            if data is not None or raw is not None:
                (metadata / "diagnostics-core.json").write_bytes(raw if raw is not None else json.dumps(data).encode())
            result = subprocess.run([sys.executable, "-B", str(script)], cwd=root,
                                    env={**os.environ, "COGLATAS_FUNCTIONAL_DOMAIN": "core", "COGLATAS_FUNCTIONAL_SELECTED_GATES": "functional-full",
                                         "TARGET_SHA": SHA, "GITHUB_RUN_ID": "100", "GITHUB_RUN_ATTEMPT": "1", "GITHUB_STEP_SUMMARY": str(root / "summary.md")},
                                    capture_output=True, text=True, encoding="utf-8", timeout=15, check=False)
            published = json.loads((root / "artifacts/functional-diagnostics/diagnostics-core.json").read_text())
            if return_lane_publication:
                publication = root / "artifacts/functional-lane-publication/lane-core.json"
                lane_published = json.loads(publication.read_text()) if publication.is_file() else None
                source = json.loads((metadata / "lane-core.json").read_text())
                return result, published, lane_published, source
            return result, published

    def test_real_finalizer_reports_failed_step_and_never_promotes_failure(self):
        result, _ = self.execute(diagnostics())
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("STEP-05 / ASSERTION", result.stdout)
        self.assertIn("First-attempt qualification: NO-GO", result.stdout)
        self.assertIn("functional-execution-diagnostics-functional-full-core-1", result.stdout)

    def test_real_finalizer_writes_missing_diagnostics_with_identity(self):
        result, data = self.execute()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("evidence=MISSING; reason=MISSING_EVIDENCE", result.stdout)
        self.assertEqual(SHA, data["commitSha"])
        self.assertEqual("1", data["runAttempt"])

    def test_real_finalizer_refuses_malformed_and_protected_evidence_privately(self):
        protected = diagnostics()
        protected["protectedBody"] = PROTECTED
        stale = copy.deepcopy(diagnostics())
        stale["runAttempt"] = "2"
        for data, raw in ((protected, None), (stale, None), (None, b'{' + PROTECTED.encode()),
                          (None, json.dumps(diagnostics()).encode() + b" " * 131072),
                          (None, json.dumps(diagnostics()).replace('"runId": "100"', '"runId": "100", "runId": "99"').encode())):
            with self.subTest(malformed=raw is not None):
                result, published = self.execute(data, raw)
                self.assertEqual(1, result.returncode)
                self.assertIn("REFUSED", result.stdout)
                self.assertNotIn(PROTECTED, result.stdout + result.stderr)
                self.assertNotIn("Traceback", result.stdout + result.stderr)
                self.assertNotIn(PROTECTED, json.dumps(published))
                self.assertEqual("DIAGNOSTIC_REFUSAL", published["recordType"])
                self.assertEqual("STALE" if data is stale else "INCOMPLETE", published["evidenceState"])
                self.assertEqual(SHA, published["commitSha"])

    def test_real_finalizer_refuses_nondictionary_attempt_records_privately(self):
        for record in ([], PROTECTED, None):
            data = diagnostics()
            data["journeys"][0]["attempts"] = [record]
            result, published = self.execute(data)
            self.assertEqual(1, result.returncode)
            self.assertEqual("DIAGNOSTIC_REFUSAL", published["recordType"])
            self.assertEqual("INCOMPLETE", published["evidenceState"])
            self.assertNotIn(PROTECTED, json.dumps(published) + result.stdout + result.stderr)
            self.assertNotIn("Traceback", result.stdout + result.stderr)

    def test_rejected_private_lane_is_preserved_but_never_published(self):
        tainted_lane = lane()
        tainted_lane["protectedBody"] = PROTECTED
        result, diagnostic_publication, lane_publication, original = self.execute(
            diagnostics(), lane_data=tainted_lane, return_lane_publication=True)
        self.assertEqual(1, result.returncode)
        self.assertEqual(tainted_lane, original)
        self.assertIsNone(lane_publication)
        self.assertEqual("DIAGNOSTIC_REFUSAL", diagnostic_publication["recordType"])
        self.assertNotIn(PROTECTED, json.dumps(diagnostic_publication) + result.stdout + result.stderr)

    def test_valid_failed_lane_and_generated_blocked_fallback_publish_exact_identity(self):
        for missing in (False, True):
            result, diagnostic_publication, lane_publication, original = self.execute(
                None if missing else diagnostics(), missing_lane=missing, return_lane_publication=True)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertEqual(original, lane_publication)
            self.assertEqual(SHA, lane_publication["commitSha"])
            self.assertEqual("100", lane_publication["runId"])
            self.assertEqual("1", lane_publication["runAttempt"])
            self.assertEqual("BLOCKED" if missing else "FAIL", lane_publication["journeys"][0]["status"])
            self.assertIn("First-attempt qualification: NO-GO", result.stdout)
            self.assertNotIn(PROTECTED, json.dumps(diagnostic_publication) + json.dumps(lane_publication))


if __name__ == "__main__":
    unittest.main()
