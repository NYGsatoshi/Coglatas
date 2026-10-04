from __future__ import annotations

import copy
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/performance"))
import db_campaign as campaign
from common import PerformanceContractError, fixture_hash, load_json
from test_performance_comparator import fingerprint

spec = importlib.util.spec_from_file_location("campaign_approval_test", ROOT / "scripts/ci/verify-performance-db-campaign-baselines.py")
approval = importlib.util.module_from_spec(spec)
spec.loader.exec_module(approval)

CONTRACT = load_json(ROOT / "performance/db-scenarios.json")
SOURCE = "a" * 40


def environment(profile="medium"):
    fp = fingerprint()
    fp["commitSha"] = SOURCE
    fp["capturedAtUtc"] = "2026-10-05T00:01:00Z"
    fp["fixture"] = {"profile": profile, "seed": 22, "hash": fixture_hash(profile, fixture_version=2), "version": 2}
    return fp


def manifest():
    fp = environment()
    return {"schemaVersion": 1, "policyVersion": campaign.POLICY_VERSION, "campaignId": "db-campaign-test-001",
            "createdAtUtc": "2026-10-05T00:00:00Z", "expiresAtUtc": "2026-10-06T00:00:00Z", "profile": "medium",
            "environmentCompatibilityKey": campaign.environment_compatibility_key(fp), "sourceSha": SOURCE, "sourceRef": "refs/heads/main",
            "fixtureIdentity": {"profile": "medium", "hash": fp["fixture"]["hash"], "version": 2,
                                "manifestSha256": campaign.file_digest(ROOT / "performance/datasets.json")},
            "toolVersions": campaign.tools_from_fingerprint(fp),
            "toolSourceDigests": {p: campaign.file_digest(ROOT / p) for p in campaign.TOOL_FILES},
            "contractDigests": {p: campaign.file_digest(ROOT / p) for p in campaign.CONTRACT_FILES},
            "scenarioSet": campaign.scenario_set(CONTRACT), "sampleCount": 5, "maxCaptureGroups": 3,
            "stabilityRule": {"indicator": "relative-mad", "maximum": .2, "comparatorSha256": campaign.file_digest(ROOT / "scripts/performance/compare.py")},
            "selectionAlgorithm": "earliest-eligible-stable-complete", "earlyStopPolicy": "never",
            "eligibilityRule": "exact-source-environment-fixture-toolchain-complete-structural-pass",
            "authorization": {"approver": "reviewer", "reference": "https://github.com/NYGsatoshi/Coglatas/issues/606#issuecomment-123",
                              "reason": "An independently reviewed bounded initial governance campaign, without retrospective sample enrollment.",
                              "cause": "initial-governance-campaign", "supersedesCampaignId": None}}


def identity(value):
    return {"schemaVersion": 1, "campaignId": value["campaignId"], "manifestSha256": campaign.digest(value),
            "declarationSha": "b" * 40, "workflowPath": campaign.WORKFLOW, "workflowRunId": 123,
            "workflowRunAttempt": 1, "sourceSha": value["sourceSha"], "runCreatedAtUtc": "2026-10-05T00:00:30Z"}


def raw_profile(name, values):
    output = {"schemaVersion": 1, "headSha": SOURCE, "profile": name, "fixtureHash": fixture_hash(name, fixture_version=2),
              "fixtureVersion": 2, "warmupSamplesExcluded": True, "collectionComplete": True, "scenarios": [], "plans": [], "measurements": []}
    for scenario in CONTRACT["scenarios"]:
        raw = {"id": scenario["id"], "cardinality": 100 if name == "small" else 3000, "samples": [], "failures": []}
        for size in ([5, 10] if scenario["paged"] else [0]):
            for iteration in range(1, 6):
                for page in ([1, 2] if scenario["paged"] else [1]):
                    total = values[iteration - 1] if scenario["id"] == "workspace.list" else 10
                    commands = [{"fingerprint": "e" * 64, "durationMs": total / 4, "readOperations": 6,
                                 "failed": False, "rootTable": scenario["rootTable"], "bounded": True, "ordered": True} for _ in range(4)]
                    capture = {"schemaVersion": 1, "status": 200, "commandCount": 4, "totalDurationMs": total,
                               "commands": commands, "slowestCommands": commands}
                    raw["samples"].append({"pageSize": size, "page": page, "iteration": iteration, "returnedCount": 5,
                                           "requestDurationMs": total + 10, "dbTimeFraction": total / (total + 10),
                                           "capture": capture, "fingerprintCounts": {"e" * 64: 4}})
            output["measurements"].append({"schemaVersion": 1, "scenario": scenario["id"], "metric": "db.total_time_ms", "unit": "ms",
                                            "headSha": SOURCE, "samples": [s["capture"]["totalDurationMs"] for s in raw["samples"] if s["pageSize"] == size and s["page"] == 1],
                                            "attempt": 1, "pageSize": size, "measurementEnvelope": {"warmupSamplesExcluded": True,
                                              "environmentStable": True, "benchmarkExitCode": 0, "timedOut": False}})
        output["scenarios"].append(raw)
    if name == "medium":
        output["plans"] = [{"id": p["id"], "tableRows": 3000, "requiredKeyLookupPresent": True,
                            "observedNodeTypes": ["Index Scan"], "observedIndexKinds": ["primary-key"], "decision": "pass"} for p in CONTRACT["planChecks"]]
    return output


def group(value, ordinal, *, unstable=False):
    profiles = {p: raw_profile(p, [1, 100, 200, 300, 400] if unstable and p == "medium" else [10] * 5) for p in ("small", "medium")}
    fingerprints = {p: environment(p) for p in ("small", "medium")}
    return {"ordinal": ordinal, "startedAtUtc": f"2026-10-05T00:0{ordinal}:00Z", "endedAtUtc": f"2026-10-05T00:0{ordinal}:30Z",
            "manifestSha256": campaign.digest(value), "sourceSha": value["sourceSha"], "workflowRunId": 123, "workflowRunAttempt": 1,
            "captureExitCodes": {"small": 0, "medium": 0}, "profiles": profiles, "fingerprints": fingerprints,
            "rawDigests": {p: {"profileSha256": campaign.digest(profiles[p]), "fingerprintSha256": campaign.digest(fingerprints[p])} for p in profiles}}


def rehash(raw):
    raw["rawDigests"] = {p: {"profileSha256": campaign.digest(v), "fingerprintSha256": campaign.digest(raw["fingerprints"][p])} for p, v in raw["profiles"].items()}


class DbCampaignTests(unittest.TestCase):
    def setUp(self):
        self.manifest = manifest()
        self.identity = identity(self.manifest)
        self.groups = [group(self.manifest, n) for n in (1, 2, 3)]

    def select(self, groups=None, selected=None):
        return campaign.select_campaign(self.manifest, self.identity, groups if groups is not None else self.groups, ROOT, selected_ordinal=selected)

    def test_no_manifest_rejected(self):
        with self.assertRaises(PerformanceContractError):
            campaign.select_campaign(None, self.identity, self.groups, ROOT)

    def test_first_stable_group_selected_and_capture_does_not_approve(self):
        result = self.select()
        self.assertEqual(1, result["selectedGroupOrdinal"])
        self.assertFalse(result["approved"])
        self.assertEqual(28, result["groups"][0]["structuralChecks"])

    def test_second_stable_selected_with_first_unstable_retained(self):
        self.groups[0] = group(self.manifest, 1, unstable=True)
        before = copy.deepcopy(self.groups)
        result = self.select()
        self.assertEqual(2, result["selectedGroupOrdinal"])
        self.assertIn("unstable-duration", result["priorRejectedGroups"][0]["reasons"])
        self.assertEqual(before, self.groups)

    def test_all_unstable_exhausts_exact_fixed_bound(self):
        groups = [group(self.manifest, n, unstable=True) for n in (1, 2, 3)]
        result = self.select(groups)
        self.assertEqual("BASELINE_UNAVAILABLE", result["decision"])
        self.assertEqual(3, len(result["priorRejectedGroups"]))

    def test_result_dependent_declared_count_change_rejected(self):
        self.manifest["maxCaptureGroups"] = 2
        with self.assertRaises(PerformanceContractError):
            self.select(self.groups[:2])

    def test_max_groups_exceeded_rejected(self):
        with self.assertRaises(PerformanceContractError):
            self.select(self.groups + [group(self.manifest, 4)])

    def test_incomplete_group_cannot_be_baseline_and_safe_partial_preserved(self):
        first = self.groups[0]
        first["profiles"]["medium"]["collectionComplete"] = False
        first["captureExitCodes"]["medium"] = 2
        rehash(first)
        result = self.select()
        self.assertEqual(2, result["selectedGroupOrdinal"])
        self.assertIn("incomplete-profile", result["groups"][0]["reasons"])
        self.assertEqual(5, len(first["profiles"]["medium"]["measurements"][0]["samples"]))
        with self.assertRaises(PerformanceContractError):
            self.select(selected=1)

    def test_wrong_environment_is_retained_and_never_selected(self):
        for raw in self.groups:
            raw["fingerprints"]["medium"]["runner"]["cpuModel"] = "Other CPU"
            rehash(raw)
        result = self.select()
        self.assertEqual("BASELINE_UNAVAILABLE", result["decision"])
        self.assertTrue(all("wrong-environment" in d["reasons"] for d in result["groups"]))

    def test_changed_fixture_rejected(self):
        self.manifest["fixtureIdentity"]["hash"] = "d" * 64
        with self.assertRaises(PerformanceContractError):
            self.select()

    def test_later_group_cannot_replace_earlier_eligible_stable_group(self):
        with self.assertRaises(PerformanceContractError):
            self.select(selected=2)

    def test_unstable_group_cannot_be_selected(self):
        self.groups[0] = group(self.manifest, 1, unstable=True)
        with self.assertRaises(PerformanceContractError):
            self.select(selected=1)

    def test_sample_removal_duplicate_and_reordering_rejected_even_after_rehash(self):
        for kind in ("remove", "duplicate", "reorder"):
            with self.subTest(kind=kind):
                groups = copy.deepcopy(self.groups)
                samples = groups[0]["profiles"]["medium"]["scenarios"][1]["samples"]
                if kind == "remove":
                    samples.pop()
                elif kind == "duplicate":
                    samples[1] = copy.deepcopy(samples[0])
                else:
                    samples[0], samples[1] = samples[1], samples[0]
                rehash(groups[0])
                with self.assertRaises(PerformanceContractError):
                    self.select(groups)

    def test_measurement_samples_cannot_be_changed_without_original_capture(self):
        self.groups[0]["profiles"]["medium"]["measurements"][0]["samples"][0] = 1
        rehash(self.groups[0])
        with self.assertRaises(PerformanceContractError):
            self.select()

    def test_campaign_created_after_measurement_rejected(self):
        self.groups[0]["startedAtUtc"] = "2026-10-04T23:59:00Z"
        with self.assertRaises(PerformanceContractError):
            self.select()

    def test_retries_are_rejected(self):
        self.groups[0]["workflowRunAttempt"] = 2
        with self.assertRaises(PerformanceContractError):
            self.select()

    def test_declared_early_stop_is_enforced_without_result_dependent_change(self):
        with self.assertRaises(PerformanceContractError):
            self.select(self.groups[:1])
        self.manifest["earlyStopPolicy"] = "first-eligible-stable-complete"
        self.identity = identity(self.manifest)
        self.groups = [group(self.manifest, n) for n in (1, 2, 3)]
        self.assertEqual(1, self.select(self.groups[:1])["selectedGroupOrdinal"])
        with self.assertRaises(PerformanceContractError):
            self.select()

    def test_extra_sql_token_body_or_fingerprint_fields_rejected(self):
        for field in ("sql", "token", "body", "authorization"):
            groups = copy.deepcopy(self.groups)
            groups[0]["fingerprints"]["medium"][field] = "protected"
            rehash(groups[0])
            with self.assertRaises(PerformanceContractError):
                self.select(groups)

    def test_new_campaign_cannot_reset_same_environment_without_correction(self):
        second = copy.deepcopy(self.manifest)
        second["campaignId"] = "db-campaign-test-002"
        second["createdAtUtc"] = "2026-10-05T01:00:00Z"
        with self.assertRaises(PerformanceContractError):
            campaign.validate_registry(ROOT, [self.manifest, second])
        second["authorization"]["cause"] = "accepted-product-change"
        second["authorization"]["supersedesCampaignId"] = self.manifest["campaignId"]
        with self.assertRaises(PerformanceContractError):
            campaign.validate_registry(ROOT, [self.manifest, second])

    def test_candidate_baseline_docs_preserve_all_samples_and_rejections(self):
        self.groups[0] = group(self.manifest, 1, unstable=True)
        result = self.select()
        artifact = {"id": 1, "name": "perf05-campaign-" + self.manifest["campaignId"], "digest": "sha256:" + "d" * 64, "workflowRunId": 123}
        docs = approval.baseline_documents(self.manifest, result, self.groups, artifact)
        self.assertEqual(9, len(docs))
        self.assertFalse(docs["workspace.list"]["approved"])
        self.assertEqual([10] * 5, docs["workspace.list"]["samples"])
        self.assertEqual([1, 2, 3, 4, 5], docs["workspace.list"]["provenance"]["sampleOrder"])
        self.assertEqual(1, docs["workspace.list"]["provenance"]["priorRejectedGroups"][0]["ordinal"])

    def test_candidate_self_baseline_rejected_before_network_or_promotion(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = root / campaign.MANIFEST_DIRECTORY / (self.manifest["campaignId"] + ".json")
            path.parent.mkdir(parents=True)
            path.write_text(json.dumps(self.manifest))
            record = {"campaignId": self.manifest["campaignId"], "approver": "reviewer", "reference": self.manifest["authorization"]["reference"],
                      "approvedAtUtc": "2026-10-05T01:00:00Z", "artifact": {}, "evidenceDirectory": "", "baselineDirectory": ""}
            with self.assertRaisesRegex(PerformanceContractError, "candidate-self-baseline-forbidden"):
                approval.validate_approval(root, record, SOURCE, "main", historical=False)

    def test_cross_host_archive_redirect_strips_authorization(self):
        import urllib.request
        request = urllib.request.Request("https://api.github.com/archive", headers={"Authorization": "Bearer synthetic-test"})
        redirected = approval.SafeArtifactRedirect().redirect_request(request, None, 302, "Found", {}, "https://artifact.example/archive")
        self.assertIsNone(redirected.get_header("Authorization"))

    def test_untrusted_artifact_digest_is_rejected_before_archive_download(self):
        record = {"artifact": {"id": 12, "name": "perf05-campaign-test", "digest": "sha256:" + "a" * 64}}
        def api(repository, path):
            return {"id": 12, "name": "perf05-campaign-test", "digest": "sha256:" + "b" * 64, "expired": False,
                    "workflow_run": {"id": 123, "head_sha": "b" * 40}}
        with self.assertRaisesRegex(PerformanceContractError, "campaign-artifact-provenance-invalid"):
            approval.trusted_artifact("NYGsatoshi/Coglatas", record, self.identity, api)

    def test_dispatch_and_rerun_cannot_enroll_historical_capture(self):
        path = ROOT / campaign.MANIFEST_DIRECTORY / (self.manifest["campaignId"] + ".json")
        for event, attempt in (("workflow_dispatch", 1), ("push", 2)):
            run = {"head_sha": "b" * 40, "head_branch": "main", "event": event, "path": campaign.WORKFLOW, "run_attempt": attempt}
            with patch.object(campaign, "load_json", return_value=self.manifest):
                with self.assertRaisesRegex(PerformanceContractError, "campaign-requires-first-main-push-attempt"):
                    campaign.validate_declaration(ROOT, path, "b" * 40, run)

    def test_historical_run_with_no_new_manifest_cannot_join_campaign(self):
        path = ROOT / campaign.MANIFEST_DIRECTORY / (self.manifest["campaignId"] + ".json")
        run = {"head_sha": "b" * 40, "head_branch": "main", "event": "push", "path": campaign.WORKFLOW, "run_attempt": 1}
        with patch.object(campaign, "load_json", return_value=self.manifest), patch.object(campaign, "git", return_value="other-file.json"):
            with self.assertRaisesRegex(PerformanceContractError, "campaign-not-predeclared-by-this-main-push"):
                campaign.validate_declaration(ROOT, path, "b" * 40, run)

    def test_pinned_source_manifest_still_validates_after_current_tool_changes(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for path in set(campaign.TOOL_FILES) | set(campaign.CONTRACT_FILES):
                target = root / path
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes((ROOT / path).read_bytes())
            subprocess.run(["git", "init", "-q"], cwd=root, check=True)
            subprocess.run(["git", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "add", "."], cwd=root, check=True)
            subprocess.run(["git", "-c", "user.name=Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "Pinned source"], cwd=root, check=True)
            source_sha = campaign.git(root, "rev-parse", "HEAD")
            (root / "scripts/performance/compare.py").write_text("# Later legitimate comparator revision\n")
            with self.assertRaises(PerformanceContractError):
                campaign.validate_manifest(self.manifest, root)
            with campaign.source_snapshot(root, source_sha) as snapshot:
                campaign.validate_manifest(self.manifest, snapshot)

    def test_missing_fingerprint_rejects_group_without_erasing_safe_raw(self):
        self.groups[0]["fingerprints"].pop("medium")
        self.groups[0]["rawDigests"]["medium"]["fingerprintSha256"] = None
        result = self.select()
        self.assertEqual(2, result["selectedGroupOrdinal"])
        self.assertIn("incomplete-group", result["groups"][0]["reasons"])
        self.assertEqual(9, len(self.groups[0]["profiles"]["medium"]["scenarios"]))


if __name__ == "__main__":
    unittest.main()
