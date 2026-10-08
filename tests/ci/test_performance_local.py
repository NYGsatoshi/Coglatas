"""Synthetic contracts only. These tests grant no benchmark or baseline credit."""
import copy
import datetime as dt
import importlib.util
import json
import shutil
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/performance"))
import api_k6
import local_evidence as evidence
import local_support as support
from local_api_samples import summary
from common import load_json, write_json_atomic, fixture_hash, PerformanceContractError
from environment_class import environment_class, hardware_fingerprint
from test_performance_comparator import fingerprint
from test_performance_db import capture
import test_performance_db_ci as db_fixture_tests
spec = importlib.util.spec_from_file_location("performance_local_cli", ROOT / "scripts/performance/performance-local.py")
cli = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cli)

HEAD, BASE, TREE = "1" * 40, "2" * 40, "3" * 40
INSTANT = dt.datetime(2026, 10, 8, 10, 0, tzinfo=dt.timezone.utc)
APPROVAL = {"approver": "NYGsatoshi", "reference": "https://github.com/NYGsatoshi/Coglatas/pull/1",
            "approvedAtUtc": "2026-10-08T08:00:00Z"}

class LocalInputTests(unittest.TestCase):
    def test_bom_accepted_duplicates_and_nonfinite_rejected(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "input.json"
            p.write_bytes(b'\xef\xbb\xbf{"valid":true}')
            self.assertTrue(load_json(p)["valid"])
            for raw in (b'{"x":1,"x":2}', b'{"x":NaN}', b'{"x":Infinity}', b'{"x":1}\xef\xbb\xbf', b'[]'):
                p.write_bytes(raw)
                with self.subTest(raw=raw), self.assertRaises(PerformanceContractError):
                    load_json(p)

    def test_preflight_failure_consumes_no_measurement(self):
        source = {"workingTreeClean": True, "baseIsAncestor": True}
        hardware = {"logicalCpus": 4, "physicalMemoryBytes": 8 * 1024 ** 3}
        with tempfile.TemporaryDirectory() as d, patch.object(support, "source_identity", return_value=source), \
             patch.object(support, "hardware_observation", return_value=hardware), \
             patch.object(support, "optional", return_value=None):
            result = support.preflight(Path(d))
            self.assertFalse(result["readyForRuntimePreflight"])
            self.assertEqual(0, result["measurementAttemptsConsumed"])
            self.assertFalse((Path(d) / "measurement-started.json").exists())

    def test_static_audit_remains_blocking_before_activation(self):
        report = cli.audit_workflows()
        self.assertFalse(report["migrationComplete"])
        self.assertEqual(5, len(report["hostedMeasurementsRemaining"]))
        self.assertEqual("performance-fast", report["requiredContextRetained"])

    def test_scalar_stream_allowlist_retains_all_ordered_samples_without_tags(self):
        from local_api_samples import read_samples, SUFFIXES
        contract = api_k6.load_contract()
        events = []
        for _ in range(20):
            for scenario in contract["scenarios"]:
                key = __import__("re").sub(r"[^a-zA-Z0-9]", "_", scenario["id"])
                for suffix, value in zip(SUFFIXES, (1, 0, 0, 100, 100000)):
                    events.append({"type": "Point", "metric": "perf_" + key + "_" + suffix,
                        "data": {"value": value, "tags": {"password": "protected-value", "url": "private"}}})
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "private.jsonl"
            p.write_text("\n".join(json.dumps(e) for e in events), encoding="utf-8")
            projected = read_samples(p, contract)
            self.assertTrue(projected["complete"])
            self.assertEqual(260, len(projected["samples"]))
            self.assertNotIn("protected-value", json.dumps(projected))
            self.assertEqual(13, len(summary(projected, contract)["scenarios"]))
            p.write_text("\n".join(json.dumps(e) for e in events[:-1]), encoding="utf-8")
            partial = read_samples(p, contract)
            self.assertFalse(partial["complete"])
            self.assertEqual(259, len(partial["samples"]))
            with self.assertRaises(PerformanceContractError):
                summary(partial, contract)

    def test_private_error_message_is_never_exported(self):
        error = support.LocalError("PROCESS_FAILED", "auth")
        error.__cause__ = RuntimeError("password=private-secret user@example.invalid")
        result = support.failure(error, component="api", operation="login", sample_count=17)
        self.assertEqual("RuntimeError", result["exceptionClass"])
        self.assertEqual(17, result["collectedSampleCount"])
        self.assertNotIn("private-secret", json.dumps(result))

class RuntimePreflightTests(unittest.TestCase):
    def run_preflight(self, directory, fail_at=None):
        import local_runner
        calls = []
        def command(argv, **kwargs):
            if argv[:2] == ["docker", "run"]:
                return "k6 v1.0.0 (synthetic)"
            if argv[:3] == ["docker", "image", "inspect"]:
                return "sha256:" + "a" * 64
            if argv[:2] == ["git", "rev-parse"]:
                return HEAD
            return ""
        def stack(source, folder, **kwargs):
            calls.append(kwargs["command"])
            if fail_at is not None and len(calls) == fail_at:
                raise support.LocalError("PREFLIGHT_FAILED", "runtime")
            folder.mkdir(parents=True)
            write_json_atomic(folder / "runtime-preflight.json", {"measuredSamples": 0})
            return {"environmentClass": None, "containerImages": {}}
        host = {"readyForRuntimePreflight": True, "hardware": {"cpuModel": "Synthetic"}}
        with patch.object(local_runner, "preflight", return_value=host), \
             patch.object(local_runner, "invoke", side_effect=command), \
             patch.object(local_runner, "local_environment", return_value={}), \
             patch.object(local_runner, "stack", side_effect=stack):
            result = local_runner.collect(Path(directory), source_sha=HEAD, base_sha=BASE,
                evidence_id="synthetic-preflight", scope="all", mode="LOCAL_DIAGNOSTIC",
                preflight_only=True, db_runtime="production")
        return result, calls

    def test_full_preflight_does_not_launch_a_collector_or_consume_an_attempt(self):
        with tempfile.TemporaryDirectory() as d:
            result, calls = self.run_preflight(d)
            self.assertEqual(4, len(calls))
            self.assertTrue(all(c[-1].endswith("local-runtime-preflight.py") for c in calls))
            self.assertEqual(0, result["measurementAttemptsConsumed"])
            self.assertFalse((Path(d) / "measurement-started.json").exists())

    def test_runtime_setup_failure_preserves_zero_consumed_attempts(self):
        with tempfile.TemporaryDirectory() as d:
            with self.assertRaises(support.LocalError):
                self.run_preflight(d, fail_at=3)
            self.assertFalse((Path(d) / "measurement-started.json").exists())
            self.assertEqual(0, load_json(Path(d) / "failure.json")["measurementAttemptsConsumed"])



class ImmutableDataFetchTests(unittest.TestCase):
    def setUp(self):
        spec = importlib.util.spec_from_file_location("immutable_data_fetch", ROOT / "scripts/performance/local-fetch-evidence.py")
        self.fetch = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(self.fetch)
        self.records = [{"path": "bundle/manifest.json", "type": "blob", "mode": "100644",
                         "sha": "4" * 40, "size": 2}]

    def fetch_bundle(self, directory, records=None):
        def api(path):
            if path.startswith("git/commits/"):
                return {"sha": HEAD, "tree": {"sha": TREE}}
            if path.startswith("git/trees/"):
                return {"truncated": False, "tree": records if records is not None else self.records}
            return {"encoding": "base64", "sha": "4" * 40, "content": "e30="}
        with patch.dict(__import__("os").environ, {"GITHUB_REPOSITORY": "NYGsatoshi/Coglatas",
                "GITHUB_REF": "refs/heads/main", "EVIDENCE_COMMIT_SHA": HEAD, "RUNNER_TEMP": directory}), \
             patch.object(self.fetch, "get", side_effect=api), patch("builtins.print"):
            self.fetch.main()

    def test_public_json_data_is_fetched_outside_source_without_execution(self):
        with tempfile.TemporaryDirectory() as d:
            self.fetch_bundle(d)
            self.assertEqual(b"{}", (Path(d) / "local-performance-evidence/manifest.json").read_bytes())
            with self.assertRaises(FileExistsError):
                self.fetch_bundle(d)

    def test_unsafe_paths_executables_and_oversized_data_are_blocking(self):
        variants = [dict(self.records[0], path="bundle/../manifest.json"),
                    dict(self.records[0], path="bundle/script.py"),
                    dict(self.records[0], mode="100755"),
                    dict(self.records[0], size=self.fetch.MAX_BYTES + 1),
                    dict(self.records[0], size=-1)]
        for record in variants:
            with self.subTest(record=record), tempfile.TemporaryDirectory() as d:
                rows = [self.records[0], record] if record["path"] != "bundle/manifest.json" else [record]
                with self.assertRaises(support.LocalError):
                    self.fetch_bundle(d, rows)


class SignedBundleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not shutil.which("ssh-keygen"):
            raise RuntimeError("SSH signature integration tests require ssh-keygen")

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="coglatas-synthetic-evidence-")
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name) / "trusted"
        self.bundle = Path(self.tmp.name) / "bundle"
        self.bundle.mkdir()
        shutil.copytree(ROOT / "scripts/performance", self.root / "scripts/performance",
                        ignore=shutil.ignore_patterns("__pycache__"))
        for name in (*support.CONTRACT_PATHS, "performance/local-allowed-signers"):
            p = self.root / name
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_bytes((ROOT / name).read_bytes())
        self.key = Path(self.tmp.name) / "ephemeral-test-key"
        support.invoke(["ssh-keygen", "-q", "-t", "ed25519", "-N", "", "-f", str(self.key)])
        self.trusted = copy.deepcopy(evidence.policy())
        public_certificate = " ".join(support.invoke(["ssh-keygen", "-y", "-f", str(self.key)]).split()[:2])
        self.trusted["allowedSigners"] = [{"identity": "synthetic-test", "publicKey":
            public_certificate, "revoked": False,
            "validAfterUtc": "2026-10-08T08:00:00Z", "validBeforeUtc": "2026-10-09T08:00:00Z",
            "approval": APPROVAL}]
        self.contract = api_k6.load_contract()
        self.fp = {}
        for name, profile, version in (("api", "small", 1), ("db-small", "small", 2), ("db-medium", "medium", 2)):
            fp = fingerprint()
            fp["runner"].update(provider="local", runnerClass="custom")
            fp["fixture"].update(profile=profile, version=version, hash=fixture_hash(profile, fixture_version=version))
            fp["k6Version"] = self.contract["k6Version"]
            fp["environmentClass"] = environment_class(fp)
            self.fp[name] = fp
            self.trusted["environmentApprovals"].append({"digest": evidence.class_digest(fp["environmentClass"]),
                "environmentClass": fp["environmentClass"], "approval": APPROVAL})
        for side in ("baseline", "current"):
            for ordinal in range(1, 6):
                folder = self.bundle / "api" / (side + "-" + str(ordinal))
                folder.mkdir(parents=True)
                scalars = {"schemaVersion": 1, "warmupSamplesExcluded": True, "complete": True,
                    "latencyPointOrder": [s["id"] for _ in range(20) for s in self.contract["scenarios"]],
                    "samples": [{"scenario": s["id"], "ordinal": i, "requests": 1, "errors": 0,
                                 "timeouts": 0, "latency": 100, "microseconds": 100000}
                                for i in range(1, 21) for s in self.contract["scenarios"]]}
                raw = summary(scalars, self.contract)
                fp = copy.deepcopy(self.fp["api"])
                sha = HEAD if side == "current" else self.contract["baseline"]["sha"]
                fp["commitSha"] = sha
                run = {"schemaVersion": 1, "headSha": sha, "fingerprint": fp,
                       "trialId": "coglatas-performance-synthetic-" + side + "-" + str(ordinal),
                       "contractHash": support.sha256(ROOT / "performance/api-k6.json"),
                       "profile": self.contract["profile"], "measurements": api_k6.normalize(raw, self.contract),
                       "k6ImageId": "sha256:" + "a" * 64}
                self.write(folder / "sample.json", run)
                self.write(folder / "raw-summary.json", raw)
                self.write(folder / "raw-samples.json", scalars)
                self.write(folder / "status.json", {"group": ordinal, "side": side, "exitStatus": 0})
        ids = ["api-local-test"]
        self.trusted["baselineEnrollments"].append({"id": ids[0], "kind": "api", "profile": "small",
            "environmentDigest": evidence.class_digest(self.fp["api"]["environmentClass"]),
            "baselineSha": self.contract["baseline"]["sha"], "files": {}, "approval": APPROVAL})
        db_contract = load_json(ROOT / "performance/db-scenarios.json")
        for profile in ("small", "medium"):
            data = db_fixture_tests.PerformanceDbComparisonIdentityTests().profile(profile, db_contract)
            data["headSha"] = HEAD
            data["measurements"] = []
            for scenario, record in zip(db_contract["scenarios"], data["scenarios"]):
                record["samples"].sort(key=lambda r: (r["pageSize"], r["iteration"], r["page"]))
                for size in ([5, 10] if scenario["paged"] else [0]):
                    data["measurements"].append({"schemaVersion": 1, "scenario": scenario["id"],
                        "metric": "db.total_time_ms", "unit": "ms", "headSha": HEAD, "attempt": 1,
                        "pageSize": size, "samples": [8] * 5,
                        "measurementEnvelope": {"warmupSamplesExcluded": True, "environmentStable": True,
                                                "benchmarkExitCode": 0, "timedOut": False}})
            self.write(self.bundle / "db" / profile / "samples.json", data)
            fp = self.fp["db-" + profile]
            self.write(self.bundle / "db" / profile / "environment.json", fp)
            key = evidence.class_digest(fp["environmentClass"])
            files = {}
            for scenario in db_contract["scenarios"]:
                name = "performance/baselines/db/local/" + profile + "/" + key + "/" + scenario["id"] + ".json"
                document = {"schemaVersion": 1, "resultSchemaVersion": 1, "scenario": scenario["id"],
                    "metric": "db.total_time_ms", "unit": "ms", "baselineSha": BASE,
                    "sourceRef": "refs/heads/main", "approved": True, "samples": [8] * 5,
                    "fixtureHash": fp["fixture"]["hash"], "fixtureVersion": 2,
                    "environmentCompatibilityDigest": key, "environmentClass": fp["environmentClass"],
                    "hardwareFingerprint": hardware_fingerprint(fp)}
                self.write(self.root / name, document)
                files[name] = support.sha256(self.root / name)
            ids.append("db-" + profile + "-local-test")
            self.trusted["baselineEnrollments"].append({"id": ids[-1], "kind": "db", "profile": profile,
                "environmentDigest": key, "baselineSha": BASE, "files": files, "approval": APPROVAL})
        hardware = {"cpuModel": "Synthetic CPU", "physicalMemoryBytes": 8_000_000_000, "logicalCpus": 4}
        order = [{"group": i, "side": side} for i in range(1, 6) for side in ("baseline", "current")]
        self.campaign = {"id": "synthetic-campaign", "evidenceId": "synthetic-evidence", "repository": self.trusted["repository"],
            "prNumber": 1046, "candidateSha": HEAD, "baseSha": BASE, "treeSha": TREE,
            "baselineSha": self.contract["baseline"]["sha"], "declaredAtUtc": "2026-10-08T08:30:00Z",
            "expiresAtUtc": "2026-10-09T08:00:00Z", "contractAndToolDigests": support.identities(self.root),
            "environmentDigests": {n: evidence.class_digest(f["environmentClass"]) for n, f in self.fp.items()},
            "hardwareDigest": support.digest(hardware), "baselineEnrollmentIds": ids, "k6ImageId": "sha256:" + "a" * 64, "approval": APPROVAL}
        self.trusted["campaigns"] = [self.campaign]
        self.manifest = {k: self.campaign[k] for k in ("evidenceId", "repository", "prNumber", "candidateSha", "baseSha",
                         "treeSha", "baselineSha", "expiresAtUtc", "baselineEnrollmentIds", "contractAndToolDigests", "k6ImageId")}
        self.manifest.update({"schemaVersion": 1, "mode": "LOCAL_ACCEPTANCE", "campaignId": self.campaign["id"],
            "signerIdentity": "synthetic-test", "startedAtUtc": "2026-10-08T09:00:00Z",
            "endedAtUtc": "2026-10-08T09:30:00Z", "hardware": hardware, "runtimeFingerprints": self.fp,
            "groupOrder": order, "complete": True, "attempt": 1, "previousEvidenceIds": []})
        self.write(self.bundle / "completion.json", {"mode": "LOCAL_ACCEPTANCE", "sourceSha": HEAD,
            "baseSha": BASE, "treeSha": TREE, "preflightComplete": True, "allCollectorsComplete": True,
            "startedAtUtc": self.manifest["startedAtUtc"], "endedAtUtc": self.manifest["endedAtUtc"],
            "attempt": 1, "previousEvidenceIds": [], "k6ImageId": "sha256:" + "a" * 64})
        self.write(self.bundle / "runtime-preflight.json", {name: {
            "phase": "runtime-preflight", "measuredSamples": 0, "readiness": True, "authentication": True,
            "volumeIsolation": True, "networkIsolation": True, "queryCaptureVerified": name.startswith("db-"),
            "sourceSha": self.contract["baseline"]["sha"] if name == "api-baseline" else HEAD}
            for name in ("api-current", "api-baseline", "db-small-current", "db-medium-current")})
        with patch.object(evidence, "ROOT", self.root):
            result = evidence.replay(self.bundle, self.manifest, self.trusted, self.campaign, check_claim=False)
        self.write(self.bundle / "results.json", result)
        self.persist_policy()
        self.resign()

    def write(self, path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        write_json_atomic(path, value)

    def persist_policy(self):
        approved_certificates = self.trusted["allowedSigners"]
        registry = self.root / "performance/local-allowed-signers"
        registry.write_text("\n".join(s["identity"] + " " + s["publicKey"] for s in approved_certificates) + "\n", encoding="utf-8")
        self.write(self.root / "performance/local-policy.json", self.trusted)

    def resign(self):
        signature = self.bundle / "manifest.json.sig"
        signature.unlink(missing_ok=True)
        self.manifest["files"] = {p.relative_to(self.bundle).as_posix(): support.sha256(p)
            for p in self.bundle.rglob("*") if p.is_file() and p.name != "manifest.json"}
        self.write(self.bundle / "manifest.json", self.manifest)
        support.invoke(["ssh-keygen", "-Y", "sign", "-f", str(self.key), "-n",
                        self.trusted["signatureNamespace"], str(self.bundle / "manifest.json")])

    def verify(self, **kwargs):
        with patch.object(evidence, "ROOT", self.root):
            return evidence.verify(self.bundle, expected_sha=kwargs.get("sha", HEAD),
                expected_base=BASE, expected_tree=TREE, pr_number=1046, root=self.root,
                instant=kwargs.get("instant", INSTANT))

    def test_full_signed_synthetic_bundle_replays_all_counts_without_required_credit(self):
        result = self.verify()
        self.assertTrue(result["signatureValid"])
        self.assertEqual({"api": 78, "structural": 28, "small": 9, "medium": 9}, result["counts"])
        self.assertFalse(result["requiredCheckCredit"])

    def test_unreviewed_public_registry_change_is_blocking(self):
        registry = self.root / "performance/local-allowed-signers"
        registry.write_text("# approved certificates removed\n", encoding="utf-8")
        with self.assertRaises(evidence.LocalError):
            self.verify()

    def test_different_sha_is_blocking(self):
        with self.assertRaises(evidence.LocalError):
            self.verify(sha="f" * 40)

    def test_tampering_and_extra_files_are_blocking(self):
        (self.bundle / "api/current-1/raw-summary.json").write_text("{}")
        with self.assertRaises(evidence.LocalError):
            self.verify()

    def test_expired_or_revoked_evidence_is_blocking(self):
        with self.assertRaises(evidence.LocalError):
            self.verify(instant=INSTANT + dt.timedelta(days=2))
        self.trusted["revokedEvidenceIds"] = [self.manifest["evidenceId"]]
        self.persist_policy()
        with self.assertRaises(evidence.LocalError):
            self.verify()

    def test_revoked_signer_is_blocking(self):
        self.trusted["allowedSigners"][0]["revoked"] = True
        self.persist_policy()
        with self.assertRaises(evidence.LocalError):
            self.verify()

    def test_invalid_signature_is_blocking(self):
        (self.bundle / "manifest.json.sig").write_text("invalid")
        with self.assertRaises(evidence.LocalError):
            self.verify()

    def test_diagnostic_and_second_attempt_cannot_be_promoted(self):
        for field, value in (("mode", "LOCAL_DIAGNOSTIC"), ("attempt", 2)):
            original = self.manifest[field]
            self.manifest[field] = value
            self.resign()
            with self.assertRaises(evidence.LocalError):
                self.verify()
            self.manifest[field] = original

    def test_signed_missing_or_reordered_scalar_sample_is_blocking(self):
        p = self.bundle / "api/current-1/raw-samples.json"
        raw = load_json(p)
        raw["samples"][0], raw["samples"][1] = raw["samples"][1], raw["samples"][0]
        self.write(p, raw)
        self.resign()
        with self.assertRaises(PerformanceContractError):
            self.verify()

    def test_db_duration_must_replay_from_command_captures(self):
        p = self.bundle / "db/medium/samples.json"
        raw = load_json(p)
        raw["measurements"][0]["samples"][0] += 1
        self.write(p, raw)
        self.resign()
        with self.assertRaises(evidence.LocalError):
            self.verify()

    def test_local_environment_and_baselines_require_separate_approval(self):
        self.trusted["environmentApprovals"] = []
        self.persist_policy()
        with self.assertRaises(evidence.LocalError) as error:
            self.verify()
        self.assertEqual("ENVIRONMENT_INCOMPATIBLE", error.exception.code)
        self.trusted["environmentApprovals"] = [{"digest": evidence.class_digest(f["environmentClass"]),
            "environmentClass": f["environmentClass"], "approval": APPROVAL} for f in self.fp.values()]
        self.trusted["baselineEnrollments"] = []
        self.persist_policy()
        with self.assertRaises(evidence.LocalError) as error:
            self.verify()
        self.assertEqual("BASELINE_UNAVAILABLE", error.exception.code)


    def test_signed_missing_group_is_blocking(self):
        shutil.rmtree(self.bundle / "api/current-5")
        self.resign()
        with self.assertRaises(PerformanceContractError):
            self.verify()

    def test_correct_signature_cannot_turn_unstable_api_into_pass(self):
        for ordinal, latency in enumerate((100, 200, 500, 600, 1000), 1):
            folder = self.bundle / "api" / ("current-" + str(ordinal))
            raw = load_json(folder / "raw-samples.json")
            for row in raw["samples"]:
                if row["scenario"] == "mutation.kanban-move":
                    row["latency"] = latency
            derived = summary(raw, self.contract)
            self.write(folder / "raw-samples.json", raw)
            self.write(folder / "raw-summary.json", derived)
            run = load_json(folder / "sample.json")
            run["measurements"] = api_k6.normalize(derived, self.contract)
            self.write(folder / "sample.json", run)
        with patch.object(evidence, "ROOT", self.root):
            rejected = evidence.replay(self.bundle, self.manifest, self.trusted, self.campaign,
                                       check_claim=False, require_pass=False)
        self.assertNotEqual("pass", rejected["api"]["decision"])
        self.write(self.bundle / "results.json", rejected)
        self.resign()
        with self.assertRaises(evidence.LocalError) as error:
            self.verify()
        self.assertEqual("PERFORMANCE_REJECTED", error.exception.code)

    def test_phase_toggle_cannot_activate_a_required_context(self):
        self.trusted["phase"] = "active"
        self.persist_policy()
        with self.assertRaises(evidence.LocalError):
            self.verify()
