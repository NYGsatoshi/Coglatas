"""Bounded archive authenticity, expected RED, privacy and expiry regressions."""
import copy
from datetime import datetime, timedelta, timezone
import importlib.util
import io
import json
from pathlib import Path
import struct
import sys
import unittest
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/ci"))
SPEC = importlib.util.spec_from_file_location("composer_evidence", ROOT / "scripts/ci/verify-composer-diagnostic-evidence.py")
verifier = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(verifier)
CANARY = "PRIVATE_COOKIE_TOKEN_BODY_MUST_NOT_BE_REPUBLISHED"


def png():
    def chunk(kind, value):
        return struct.pack(">I", len(value)) + kind + value + struct.pack(">I", zlib.crc32(kind + value))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", 900, 1, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(b"\0" + b"\xff" * 2700)) + chunk(b"IEND", b""))


def diagnostic(record):
    snapshot_name = "structural-collaboration-FUNC-MSG-001-%s-%s-1-0.png" % (record["merge"], record["run"])
    picture = png()
    browser = dict(schemaVersion=1, scope="primary-browser-context", network=[], networkTruncated=0,
                   consoleCounts={key: 0 for key in ("error", "warning", "info", "debug", "other", "pageError")},
                   projection={key: None for key in ("filesPage", "uploaderEnabled", "inventoryRows", "selectedRows", "detailPane", "detailHeading", "downloadEnabled", "errorIndicator")},
                   projectionCheckpoint="after-test-cleanup", structuralSnapshotState="CAPTURED",
                   structuralSnapshot=dict(name=snapshot_name, sha256=verifier.sha(picture)))
    journeys = []
    for owner, status in (("FUNC-MSG-001", "FAIL"), ("FUNC-NOTIF-001", "PASS")):
        current = copy.deepcopy(browser)
        if status == "PASS":
            current.update(structuralSnapshotState="NOT_REQUIRED", structuralSnapshot=None)
        journeys.append(dict(journeyId=owner, status=status, expectedStatus="passed", evidenceState="COMPLETE",
                             reason="FAIL" if status == "FAIL" else "NONE", attempts=[
                                 dict(retry=0, status="failed" if status == "FAIL" else "passed",
                                      failureKind="ASSERTION" if status == "FAIL" else "NONE",
                                      failedStepId="MSG-RECOVERY" if status == "FAIL" else None,
                                      completedStepIds=[], browserEvidenceState="COMPLETE", browser=current)]))
    value = dict(schemaVersion=1, commitSha=record["merge"], gate="functional-fast", runId=str(record["run"]),
                 runAttempt="1", suite="collaboration", capturePolicy="allowlisted-functional-v1",
                 producer="playwright-reporter", artifactName=verifier.NAME, journeys=journeys)
    return value, snapshot_name, picture


def archive(value, name, picture, extra=None):
    output = io.BytesIO()
    with zipfile.ZipFile(output, "w", compression=zipfile.ZIP_STORED) as writer:
        writer.writestr("diagnostics-collaboration.json", json.dumps(value))
        writer.writestr(name, picture)
        if extra:
            writer.writestr(*extra)
    return output.getvalue()


def fixture():
    record = dict(verifier.RECORDS[0])
    value, name, picture = diagnostic(record)
    raw = archive(value, name, picture)
    record.update(size=len(raw), digest=verifier.sha(raw))
    return record, value, name, picture, raw


def log(record, kind, short=7):
    checkout = "HEAD is now at " + record["merge"][:short] + " Merge " + record["head"] + " into " + record["base"] + "\n"
    if kind == "frontend":
        return (checkout + "Tests  1 failed | 1869 passed (1870)\nmessage-catch-up-draft.spec.ts:113:36\n"
                "expect(textarea.isConnected).toBe(true);\nexpected false to be true\n").encode()
    return (checkout + "> 584 | expect(await originalComposerNode!.evaluate((node) => node.isConnected)).toBe(true);\nExpected: true\nReceived: false\n"
            "- FUNC-MSG-001: FAIL; attempts=1; duration=%s ms\n"
            "- FUNC-NOTIF-001: PASS; attempts=1; duration=%s ms\n"
            "- FUNC-MSG-001: FAIL; evidence=COMPLETE; reason=FAIL; failed steps=MSG-RECOVERY / ASSERTION\n"
            "Browser observation: COMPLETE; retry=0\nBrowser observation: COMPLETE; retry=0\n" %
            (record["msg_ms"], record["notif_ms"])).encode()


class ComposerEvidenceContracts(unittest.TestCase):
    def test_whole_archive_and_closed_expected_red_positive(self):
        record, _, _, _, raw = fixture()
        result = verifier.inspect_archive(record, raw)
        self.assertEqual(result["message"], "FAIL")
        self.assertEqual(result["notification"], "PASS")
        self.assertEqual(result["retries"], 0)

    def test_actual_original_digest_and_size_required(self):
        record, _, _, _, raw = fixture()
        for data in (raw + b"hidden", raw[:-1], bytes([raw[0] ^ 1]) + raw[1:]):
            with self.assertRaises(verifier.Refused):
                verifier.inspect_archive(record, data)

    def test_hidden_zip_tails_extras_and_unreferenced_members_refused(self):
        record, value, name, picture, raw = fixture()
        for data in (raw + CANARY.encode(), archive(value, name, picture, ("hidden.txt", CANARY))):
            current = {**record, "size": len(data), "digest": verifier.sha(data)}
            with self.assertRaises(verifier.Refused):
                verifier.inspect_archive(current, data)

    def test_closed_json_private_fields_duplicates_and_nonfinite_refused(self):
        for raw in (b'{"x":1,"x":2}', b'{"x":NaN}', b'{"x":Infinity}'):
            with self.assertRaises(verifier.Refused):
                verifier.closed_json(raw)
        record, value, name, picture, _ = fixture()
        value["rawCookie"] = CANARY
        raw = archive(value, name, picture)
        record.update(size=len(raw), digest=verifier.sha(raw))
        with self.assertRaises(ValueError):
            verifier.inspect_archive(record, raw)

    def test_wrong_step_retry_failure_kind_or_missing_positive_refused(self):
        for key, value in (("retry", 1), ("failureKind", "TIMEOUT"), ("failedStepId", "MSG-THREAD-DENIAL")):
            record, data, name, picture, _ = fixture()
            data["journeys"][0]["attempts"][0][key] = value
            raw = archive(data, name, picture)
            record.update(size=len(raw), digest=verifier.sha(raw))
            with self.assertRaises(ValueError):
                verifier.inspect_archive(record, raw)
        record, data, name, picture, _ = fixture()
        data["journeys"].pop()
        raw = archive(data, name, picture)
        record.update(size=len(raw), digest=verifier.sha(raw))
        with self.assertRaises(ValueError):
            verifier.inspect_archive(record, raw)

    def test_snapshot_digest_and_source_binding_refused(self):
        for mutation in ("digest", "source"):
            record, value, name, picture, _ = fixture()
            if mutation == "digest":
                value["journeys"][0]["attempts"][0]["browser"]["structuralSnapshot"]["sha256"] = "0" * 64
            else:
                value["commitSha"] = "0" * 40
            raw = archive(value, name, picture)
            record.update(size=len(raw), digest=verifier.sha(raw))
            with self.assertRaises(ValueError):
                verifier.inspect_archive(record, raw)

    def test_native_seven_eight_and_full_checkout_lengths(self):
        for record in verifier.RECORDS:
            for short in (7, 8, 40):
                for kind in ("frontend", "collaboration"):
                    result = verifier.verify_log(record, log(record, kind, short), kind)
                    self.assertEqual(result["sha256"], verifier.sha(log(record, kind, short)))
                    self.assertNotIn(CANARY, json.dumps(result))

    def test_wrong_checkout_unit_counts_duration_or_retry_rejected(self):
        record = verifier.RECORDS[0]
        mutations = [
            ("frontend", lambda raw: raw.replace(record["merge"][:7].encode(), b"0000000", 1)),
            ("frontend", lambda raw: raw.replace(b"1869", b"1868")),
            ("collaboration", lambda raw: raw.replace(str(record["msg_ms"]).encode(), b"1")),
            ("collaboration", lambda raw: raw.replace(b"retry=0", b"retry=1", 1)),
            ("collaboration", lambda raw: raw.replace(b"MSG-RECOVERY", b"MSG-THREAD")),
        ]
        for kind, mutate in mutations:
            with self.assertRaises(verifier.Refused):
                verifier.verify_log(record, mutate(log(record, kind)), kind)

    def test_raw_log_canary_is_not_projected(self):
        record = verifier.RECORDS[0]
        raw = log(record, "collaboration") + CANARY.encode()
        result = verifier.verify_log(record, raw, "collaboration")
        self.assertNotIn(CANARY, json.dumps(result))
        self.assertEqual(set(result), {"sha256", "size", "retained"})

    def test_finite_retention_identity_and_actual_expiry(self):
        now = datetime.now(timezone.utc).replace(microsecond=0)
        created = now - timedelta(minutes=2)
        artifact = dict(id=123, name="fixed", expired=False, digest="sha256:" + "a" * 64,
                        workflow_run=dict(id=456, head_sha="b" * 40),
                        created_at=created.strftime("%Y-%m-%dT%H:%M:%SZ"),
                        expires_at=(created + timedelta(days=14)).strftime("%Y-%m-%dT%H:%M:%SZ"))
        result = verifier.retention_projection(artifact, 123, "fixed", "a" * 64, 456, "b" * 40)
        self.assertEqual(result["actualExpiresAt"], artifact["expires_at"])
        self.assertIs(result["permanent"], False)
        for key, value in (("id", 124), ("expired", True), ("digest", "sha256:" + "c" * 64),
                           ("expires_at", (now + timedelta(days=90)).strftime("%Y-%m-%dT%H:%M:%SZ")),
                           ("expires_at", CANARY)):
            changed = copy.deepcopy(artifact)
            changed[key] = value
            with self.assertRaises(verifier.Refused):
                verifier.retention_projection(changed, 123, "fixed", "a" * 64, 456, "b" * 40)


    def test_read_token_is_never_forwarded_to_storage(self):
        calls = []
        class Response:
            status = 200
            def __enter__(self): return self
            def __exit__(self, *args): pass
            def read(self, bound): return b"verified bytes"
        class Opener:
            def open(self, request, timeout):
                calls.append(request)
                if len(calls) == 1:
                    raise verifier.urllib.error.HTTPError(request.full_url, 302, "redirect",
                        {"Location": "https://productionresultssa1.blob.core.windows.net/fixed?sig=" + CANARY}, None)
                return Response()
        client = verifier.GitHub("PRIVATE_READ_TOKEN")
        client.opener = Opener()
        self.assertEqual(client.download("actions/artifacts/123/zip", 100), b"verified bytes")
        self.assertEqual(calls[0].get_header("Authorization"), "Bearer PRIVATE_READ_TOKEN")
        self.assertIsNone(calls[1].get_header("Authorization"))

    def test_storage_origin_refused(self):
        for origin in ("http://productionresultssa1.blob.core.windows.net/path",
                       "https://productionresultssa1.blob.core.windows.net.evil.invalid/path",
                       "https://user@productionresultssa1.blob.core.windows.net/path",
                       "https://productionresultssa1.blob.core.windows.net:444/path"):
            calls = []
            class Opener:
                def open(self, request, timeout):
                    calls.append(request)
                    raise verifier.urllib.error.HTTPError(request.full_url, 302, "redirect", {"Location": origin}, None)
            client = verifier.GitHub("PRIVATE_READ_TOKEN")
            client.opener = Opener()
            with self.assertRaises(verifier.Refused):
                client.download("actions/artifacts/123/zip", 100)
            self.assertEqual(len(calls), 1)

    def test_stale_run_artifact_and_merge_metadata_refused(self):
        record = verifier.RECORDS[0]
        run = dict(id=record["run"], run_attempt=1, head_sha=record["head"], event="pull_request",
                   path=".github/workflows/ci.yml", status="completed", conclusion="failure",
                   repository=dict(full_name=verifier.REPO))
        artifact = dict(id=record["artifact"], name=verifier.NAME, size_in_bytes=record["size"],
                        expired=False, digest="sha256:" + record["digest"], expires_at=record["expires"],
                        workflow_run=dict(id=record["run"], head_sha=record["head"]))
        commit = dict(sha=record["merge"], tree=dict(sha=record["tree"]),
                      parents=[dict(sha=record["base"]), dict(sha=record["head"])])
        verifier.verify_metadata(record, run, artifact, commit)
        for target, key, value in ((run, "run_attempt", 2), (run, "head_sha", "0" * 40),
                                    (artifact, "expired", True), (artifact, "digest", "sha256:" + "0" * 64),
                                    (commit, "parents", list(reversed(commit["parents"])))):
            changed = copy.deepcopy(target)
            changed[key] = value
            values = [changed if target is run else run, changed if target is artifact else artifact,
                      changed if target is commit else commit]
            with self.assertRaises(verifier.Refused):
                verifier.verify_metadata(record, *values)


if __name__ == "__main__":
    unittest.main()
