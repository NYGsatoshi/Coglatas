from __future__ import annotations

import contextlib
import copy
import datetime as dt
import io
import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
import zipfile

from test_performance_db_campaign import ROOT, manifest, identity, group, rehash, campaign, approval, PerformanceContractError

REPOSITORY = "NYGsatoshi/Coglatas"


class AssignmentTransitionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.predecessor = manifest()
        self.groups = [group(self.predecessor, n) for n in (1, 2, 3)]
        self.declaration = identity(self.predecessor)
        self.rule_sha = "c" * 40
        self.rule_on_main = True
        self.workflow_digest = campaign.file_digest(ROOT / campaign.WORKFLOW)
        self.predecessor_workflow_digest = self.workflow_digest
        workflow = self.root / campaign.WORKFLOW
        workflow.parent.mkdir(parents=True)
        workflow.write_bytes((ROOT / campaign.WORKFLOW).read_bytes())
        self.rule_reference = "https://github.com/NYGsatoshi/Coglatas/issues/1105#issuecomment-789"
        self.policy = {"schemaVersion": 1, "ruleVersion": campaign.ASSIGNMENT_RULE_VERSION,
                       "decisionProposalHeadSha": "d" * 40,
                       "authorization": {"approver": "reviewer", "reference": self.rule_reference, "approvedAtUtc": "2026-10-05T00:04:00Z"}}
        self.rule_review = {"user": {"login": "reviewer"}, "created_at": "2026-10-05T00:04:00Z", "updated_at": "2026-10-05T00:04:00Z",
                            "body": "OWNER_RULE_APPROVAL_RECORDED\nRULE_VERSION " + campaign.ASSIGNMENT_RULE_VERSION + "\n" + "d" * 40}
        self.artifact = {"id": 77, "name": "perf05-campaign-" + self.predecessor["campaignId"], "digest": "sha256:" + "e" * 64,
                         "workflowRunId": self.declaration["workflowRunId"]}
        self.value = copy.deepcopy(self.predecessor)
        self.value["schemaVersion"] = 3
        self.value[campaign.PUBLIC_DIGEST_FIELD] = self.value.pop("environmentCompatibilityKey")
        self.value["campaignId"] = "db-campaign-assignment-002"
        self.value["createdAtUtc"] = "2026-10-05T01:00:00Z"
        self.value["expiresAtUtc"] = "2026-10-07T01:00:00Z"
        self.value["authorization"] |= {"cause": campaign.ASSIGNMENT_CAUSE, "supersedesCampaignId": self.predecessor["campaignId"],
                                       "reference": "https://github.com/NYGsatoshi/Coglatas/issues/606#issuecomment-456"}
        self.value["environmentAssignmentTransition"] = {"ruleVersion": campaign.ASSIGNMENT_RULE_VERSION, "ruleSourceSha": self.rule_sha,
                                                        "ruleApprovalReference": self.rule_reference, "predecessorArtifact": self.artifact.copy(),
                                                        "predecessorRawGroupsSha256": "f" * 64}
        self.refresh()

    def refresh(self, *, unstable=False, mixed=False, structural_failure=False):
        self.groups = [group(self.predecessor, n, unstable=unstable) for n in (1, 2, 3)]
        for raw in self.groups:
            raw["fingerprints"]["medium"]["runner"]["cpuModel"] = "Assigned CPU" + (str(raw["ordinal"]) if mixed else "")
            if structural_failure:
                raw["profiles"]["medium"]["plans"][0]["requiredKeyLookupPresent"] = False
                raw["profiles"]["medium"]["plans"][0]["decision"] = "regression"
            rehash(raw)
        result = campaign.select_campaign(self.predecessor, self.declaration, self.groups, ROOT)
        self.documents = {"manifest.json": copy.deepcopy(self.predecessor), "declaration.json": copy.deepcopy(self.declaration),
                          "raw-groups.json": {"groups": copy.deepcopy(self.groups)}, "campaign-result.json": result}
        self.evidence = self.root / "performance/baseline-campaigns/evidence" / self.predecessor["campaignId"]
        self.evidence.mkdir(parents=True, exist_ok=True)
        for name, value in self.documents.items():
            (self.evidence / name).write_text(json.dumps(value), encoding="utf-8")
        (self.evidence / "archive-metadata.json").write_text(json.dumps(self.artifact), encoding="utf-8")
        self.base_documents = copy.deepcopy(self.documents)
        self.value[campaign.PUBLIC_DIGEST_FIELD] = campaign.environment_compatibility_key(self.groups[0]["fingerprints"]["medium"])
        self.value["environmentAssignmentTransition"]["predecessorRawGroupsSha256"] = campaign.digest(self.groups)

    def api(self, repository, path):
        self.assertEqual(REPOSITORY, repository)
        if path == "":
            return {"owner": {"login": "reviewer"}}
        if path == "/issues/comments/789":
            return self.rule_review
        raise AssertionError("Unexpected API endpoint")

    def git_document(self, root, sha, path):
        if sha == self.rule_sha and path == campaign.ASSIGNMENT_POLICY_PATH:
            return self.policy
        self.assertEqual("approved-main", sha)
        return self.base_documents[Path(path).name]

    def git(self, root, *arguments):
        if arguments[0] == "rev-list":
            return self.rule_sha if self.rule_on_main else "b" * 40
        if arguments[0] == "diff":
            return campaign.ASSIGNMENT_POLICY_PATH
        if arguments[:3] == ("show", "-s", "--format=%ct"):
            return str(int(dt.datetime(2026, 10, 5, 0, 5, tzinfo=dt.timezone.utc).timestamp()))
        if arguments[0] == "show":
            return 'ASSIGNMENT_RULE_VERSION = "' + campaign.ASSIGNMENT_RULE_VERSION + '"'
        raise AssertionError("Unexpected Git operation")

    @contextlib.contextmanager
    def context(self, *, archive=None):
        with patch.object(campaign, "ancestor"), patch.object(campaign, "git_document", side_effect=self.git_document), \
             patch.object(campaign, "git", side_effect=self.git), patch.object(campaign, "github_api", side_effect=self.api), \
             patch.object(campaign, "git_file_digest", side_effect=lambda root, sha, path: self.workflow_digest if sha == self.rule_sha else self.predecessor_workflow_digest), \
             patch.object(campaign, "source_snapshot", return_value=contextlib.nullcontext(ROOT)), \
             patch.object(campaign, "failed_archive", return_value=self.documents if archive is None else archive):
            yield

    def validate(self, *, archive=None, extra=None, historical=False):
        campaign.validate_manifest(self.value, ROOT)
        with self.context(archive=archive):
            campaign.validate_registry(self.root, [self.predecessor, self.value] + (extra or []),
                                       base_ref="approved-main", introduced_ids=set() if historical else {self.value["campaignId"]})

    def test_consistent_assigned_scope_allows_one_prospective_transition(self):
        original = copy.deepcopy(self.groups)
        self.validate()
        self.assertEqual(original, self.groups)
        self.assertEqual("BASELINE_UNAVAILABLE", self.documents["campaign-result.json"]["decision"])
        self.assertTrue(all(not d["eligible"] for d in self.documents["campaign-result.json"]["groups"]))

    def test_timing_and_mad_do_not_select_assignment_scope(self):
        target = self.value[campaign.PUBLIC_DIGEST_FIELD]
        self.refresh(unstable=True)
        self.assertTrue(all(not d["stable"] for d in self.documents["campaign-result.json"]["groups"]))
        self.validate()
        self.assertEqual(target, self.value[campaign.PUBLIC_DIGEST_FIELD])

    def test_fresh_schema3_capture_keeps_separate_public_digest_baseline_identity(self):
        declaration = identity(self.value)
        declaration["declarationSha"] = "e" * 40
        declaration["runCreatedAtUtc"] = "2026-10-05T01:00:30Z"
        fresh = [group(self.value, n) for n in (1, 2, 3)]
        for raw in fresh:
            raw["startedAtUtc"] = raw["startedAtUtc"].replace("T00:", "T01:")
            raw["endedAtUtc"] = raw["endedAtUtc"].replace("T00:", "T01:")
            for fingerprint in raw["fingerprints"].values():
                fingerprint["capturedAtUtc"] = raw["startedAtUtc"]
            raw["fingerprints"]["medium"]["runner"]["cpuModel"] = "Assigned CPU"
            rehash(raw)
        result = campaign.select_campaign(self.value, declaration, fresh, ROOT)
        self.assertEqual("BASELINE_CANDIDATE", result["decision"])
        self.assertEqual(1, result["selectedGroupOrdinal"])
        self.assertIs(False, result["approved"])
        self.assertNotEqual(campaign.digest(self.groups), campaign.digest(fresh))
        artifact = self.artifact | {"id": 88, "name": "perf05-campaign-" + self.value["campaignId"]}
        documents = approval.baseline_documents(self.value, result, fresh, artifact)
        self.assertEqual(9, len(documents))
        for document in documents.values():
            self.assertIs(False, document["approved"])
            self.assertEqual(self.value[campaign.PUBLIC_DIGEST_FIELD], document[campaign.PUBLIC_DIGEST_FIELD])
            self.assertEqual(self.value["campaignId"], document["provenance"]["campaignId"])
            self.assertEqual(artifact["name"], document["provenance"]["artifactName"])
            self.assertEqual(campaign.digest(fresh), document["provenance"]["rawGroupsSha256"])

    def test_structural_outcome_does_not_select_assignment_scope(self):
        target = self.value[campaign.PUBLIC_DIGEST_FIELD]
        self.refresh(structural_failure=True)
        self.assertTrue(all(d["structuralDecision"] == "regression" for d in self.documents["campaign-result.json"]["groups"]))
        self.validate()
        self.assertEqual(target, self.value[campaign.PUBLIC_DIGEST_FIELD])

    def test_mixed_assignments_cannot_choose_one_favorable_group(self):
        self.refresh(mixed=True)
        with self.assertRaisesRegex(PerformanceContractError, "entire-consistent-observed-scope"):
            self.validate()

    def test_unobserved_target_cannot_be_declared(self):
        self.value[campaign.PUBLIC_DIGEST_FIELD] = "f" * 64
        with self.assertRaisesRegex(PerformanceContractError, "entire-consistent-observed-scope"):
            self.validate()

    def test_initial_cause_cannot_reset_profile_under_another_key(self):
        self.value["schemaVersion"] = 2
        self.value.pop("environmentAssignmentTransition")
        self.value["authorization"] |= {"cause": "initial-governance-campaign", "supersedesCampaignId": None}
        with self.assertRaisesRegex(PerformanceContractError, "reset-profile-epoch"):
            self.validate()

    def test_historical_distinct_initial_scopes_are_not_rewritten(self):
        self.value["schemaVersion"] = 2
        self.value.pop("environmentAssignmentTransition")
        self.value["authorization"] |= {"cause": "initial-governance-campaign", "supersedesCampaignId": None}
        self.validate(historical=True)

    def test_backdated_initial_cause_cannot_reset_existing_profile(self):
        self.value["schemaVersion"] = 2
        self.value.pop("environmentAssignmentTransition")
        self.value["authorization"] |= {"cause": "initial-governance-campaign", "supersedesCampaignId": None}
        self.value["createdAtUtc"] = (campaign.utc(self.predecessor["createdAtUtc"]) - dt.timedelta(seconds=1)).strftime("%Y-%m-%dT%H:%M:%SZ")
        with self.assertRaisesRegex(PerformanceContractError, "reset-profile-epoch"):
            self.validate()

    def test_missing_original_group_archive_cannot_transition(self):
        (self.evidence / "raw-groups.json").unlink()
        with self.assertRaisesRegex(PerformanceContractError, "predecessor-evidence-missing"):
            self.validate()

    def test_incomplete_bounded_predecessor_cannot_transition(self):
        self.documents["raw-groups.json"]["groups"].pop()
        (self.evidence / "raw-groups.json").write_text(json.dumps(self.documents["raw-groups.json"]), encoding="utf-8")
        with self.assertRaises(PerformanceContractError):
            self.validate()

    def test_changed_retained_sample_cannot_be_enrolled(self):
        self.documents["raw-groups.json"]["groups"][0]["profiles"]["medium"]["measurements"][0]["samples"][0] += 1
        (self.evidence / "raw-groups.json").write_text(json.dumps(self.documents["raw-groups.json"]), encoding="utf-8")
        with self.assertRaisesRegex(PerformanceContractError, "already-be-retained-main"):
            self.validate()

    def test_live_original_archive_must_equal_retained_files(self):
        foreign = copy.deepcopy(self.documents)
        foreign["declaration.json"]["workflowRunId"] += 1
        with self.assertRaisesRegex(PerformanceContractError, "not-authenticated-archive"):
            self.validate(archive=foreign)

    def test_archive_identity_cannot_be_rebound(self):
        self.value["environmentAssignmentTransition"]["predecessorArtifact"]["digest"] = "sha256:" + "f" * 64
        with self.assertRaisesRegex(PerformanceContractError, "archive-identity-changed"):
            self.validate()

    def test_measurement_source_cannot_change_under_assignment_cause(self):
        self.value["sourceSha"] = "e" * 40
        with self.assertRaisesRegex(PerformanceContractError, "measurement-contract-changed"):
            self.validate()

    def test_bound_early_stop_and_sample_count_cannot_change(self):
        for field, value in (("maxCaptureGroups", 2), ("earlyStopPolicy", "first-eligible-stable-complete"), ("sampleCount", 4)):
            with self.subTest(field=field):
                original = self.value[field]
                self.value[field] = value
                with self.assertRaises(PerformanceContractError):
                    self.validate()
                self.value[field] = original

    def test_prior_or_rule_approval_cannot_transfer(self):
        for reference in (self.predecessor["authorization"]["reference"], self.rule_reference):
            with self.subTest(reference=reference):
                self.value["authorization"]["reference"] = reference
                with self.assertRaisesRegex(PerformanceContractError, "approval-cannot-transfer"):
                    self.validate()

    def test_predecessor_expiry_cannot_be_reused(self):
        self.value["expiresAtUtc"] = self.predecessor["expiresAtUtc"]
        with self.assertRaisesRegex(PerformanceContractError, "new-expiry"):
            self.validate()

    def test_rule_must_be_main_before_declaration(self):
        self.value["createdAtUtc"] = "2026-10-05T00:04:00Z"
        with self.assertRaisesRegex(PerformanceContractError, "before-rule-rollout"):
            self.validate()

    def test_branch_rule_commit_cannot_stand_in_for_main_rollout(self):
        self.rule_on_main = False
        with self.assertRaisesRegex(PerformanceContractError, "must-be-main-rollout"):
            self.validate()

    def test_current_workflow_cannot_change_runner_selection_or_schedule(self):
        workflow = self.root / campaign.WORKFLOW
        workflow.write_bytes(workflow.read_bytes() + b"\n# changed runner selection or schedule\n")
        with self.assertRaisesRegex(PerformanceContractError, "capture-workflow-changed"):
            self.validate()

    def test_predecessor_must_use_the_same_normally_assigned_workflow(self):
        self.predecessor_workflow_digest = "f" * 64
        with self.assertRaisesRegex(PerformanceContractError, "capture-workflow-changed"):
            self.validate()

    def test_historical_replay_uses_original_workflow_after_future_changes(self):
        workflow = self.root / campaign.WORKFLOW
        workflow.write_bytes(workflow.read_bytes() + b"\n# legitimate future workflow change\n")
        self.validate(historical=True)

    def test_rule_reference_and_version_are_fixed(self):
        self.value["environmentAssignmentTransition"]["ruleApprovalReference"] = self.rule_reference.replace("789", "790")
        with self.assertRaisesRegex(PerformanceContractError, "rule-reference-mismatch"):
            self.validate()
        self.value["environmentAssignmentTransition"]["ruleApprovalReference"] = self.rule_reference
        self.value["environmentAssignmentTransition"]["ruleVersion"] = "unapproved-rule-v2"
        with self.assertRaisesRegex(PerformanceContractError, "assignment-transition-identity-invalid"):
            self.validate()

    def test_assignment_cannot_chain_to_another_transition(self):
        predecessor = copy.deepcopy(self.value)
        next_value = copy.deepcopy(self.value)
        next_value["campaignId"] = "db-campaign-assignment-003"
        next_value["authorization"]["supersedesCampaignId"] = predecessor["campaignId"]
        with self.assertRaisesRegex(PerformanceContractError, "transition-chain-forbidden"):
            campaign.validate_assignment_transition(self.root, next_value, predecessor, base_ref="approved-main", introduced=True)

    def test_one_epoch_cannot_create_two_successors(self):
        next_value = copy.deepcopy(self.value)
        next_value["campaignId"] = "db-campaign-assignment-003"
        next_value["createdAtUtc"] = "2026-10-05T02:00:00Z"
        next_value["environmentCompatibilityDigest"] = "f" * 64
        with self.assertRaisesRegex(PerformanceContractError, "latest-profile-predecessor"):
            self.validate(extra=[next_value])

    def test_previous_scope_cannot_be_revisited(self):
        self.value[campaign.PUBLIC_DIGEST_FIELD] = self.predecessor["environmentCompatibilityKey"]
        with self.assertRaisesRegex(PerformanceContractError, "cycle-or-epoch-reset"):
            self.validate()

    def test_old_manifest_cannot_smuggle_new_transition_cause(self):
        self.value["schemaVersion"] = 2
        self.value.pop("environmentAssignmentTransition")
        with self.assertRaisesRegex(PerformanceContractError, "result-only-retry-forbidden"):
            campaign.validate_manifest(self.value, ROOT)

    def test_unapproved_rule_and_late_edit_cannot_authorize(self):
        for edit in ({"body": "NOT APPROVED\n" + "d" * 40}, {"updated_at": "2026-10-05T01:00:00Z"},
                     {"user": {"login": "another-user"}}):
            with self.subTest(edit=edit), patch.dict(self.rule_review, edit):
                with self.assertRaisesRegex(PerformanceContractError, "owner-approval-missing-or-late"):
                    campaign.validate_assignment_policy(self.policy, repository=REPOSITORY, api=self.api)

    def test_new_campaign_requires_positive_exact_owner_authorization(self):
        review = {"user": {"login": "reviewer"}, "created_at": "2026-10-05T01:00:30Z", "updated_at": "2026-10-05T01:00:30Z",
                  "body": "APPROVED_PREMEASUREMENT\nCAMPAIGN_ID " + self.value["campaignId"] + "\nMANIFEST_SHA256 " + campaign.digest(self.value)}
        run = {"created_at": "2026-10-05T01:01:00Z"}
        campaign.validate_transition_review(self.value, review, run, "reviewer")
        narrative = copy.deepcopy(review)
        narrative["body"] += "\n- predecessor result remains REJECTED historical evidence"
        campaign.validate_transition_review(self.value, narrative, run, "reviewer")
        for edit in ({"body": "NOT APPROVED " + self.value["campaignId"] + " " + campaign.digest(self.value)},
                     {"updated_at": "2026-10-05T01:02:00Z"}, {"user": {"login": "another-user"}},
                     {"body": review["body"] + "\nREVOKED"}, {"body": review["body"] + "\nREJECTED successor"},
                     {"body": review["body"].replace(self.value["campaignId"], "foreign-campaign")}):
            with self.subTest(edit=edit), patch.dict(review, edit):
                with self.assertRaisesRegex(PerformanceContractError, "explicit-owner-premeasurement-approval-missing"):
                    campaign.validate_transition_review(self.value, review, run, "reviewer")

    def test_failed_archive_reader_never_changes_baseline_success_requirement(self):
        raw_stream = io.BytesIO()
        with zipfile.ZipFile(raw_stream, "w") as archive:
            for name, value in self.documents.items():
                archive.writestr(name, json.dumps(value))
        import hashlib
        artifact = self.artifact | {"digest": "sha256:" + hashlib.sha256(raw_stream.getvalue()).hexdigest()}
        def api(repository, path):
            if path.startswith("/actions/artifacts/"):
                return artifact | {"expired": False, "workflow_run": {"id": 123, "head_sha": self.declaration["declarationSha"]}}
            return {"head_sha": self.declaration["declarationSha"], "head_branch": "main", "event": "push",
                    "path": campaign.WORKFLOW, "run_attempt": 1, "status": "completed", "conclusion": "failure",
                    "created_at": self.declaration["runCreatedAtUtc"]}
        with self.assertRaisesRegex(PerformanceContractError, "workflow-provenance-invalid"):
            approval.trusted_artifact(REPOSITORY, {"artifact": artifact}, self.declaration, api)
        with patch.object(approval.urllib.request, "build_opener") as opener, patch.dict(approval.os.environ, {"GH_TOKEN": "synthetic-test"}):
            opener.return_value.open.return_value.__enter__.return_value.read.return_value = raw_stream.getvalue()
            self.assertEqual(self.documents, approval.trusted_failed_artifact(REPOSITORY, artifact, self.declaration, api))

class ZeroCaptureRecoveryTests(unittest.TestCase):
    def setUp(self):
        self.consumed = manifest()
        self.consumed["schemaVersion"] = 3
        self.consumed[campaign.PUBLIC_DIGEST_FIELD] = self.consumed.pop("environmentCompatibilityKey")
        self.consumed["campaignId"] = "db-campaign-assignment-consumed"
        self.consumed["createdAtUtc"] = "2026-10-07T08:55:41Z"
        self.consumed["expiresAtUtc"] = "2026-10-10T08:55:41Z"
        self.consumed["authorization"] |= {
            "cause": campaign.ASSIGNMENT_CAUSE,
            "supersedesCampaignId": "db-campaign-intel-predecessor",
            "reference": "https://github.com/NYGsatoshi/Coglatas/issues/606#issuecomment-1001",
        }
        self.consumed["environmentAssignmentTransition"] = {
            "ruleVersion": campaign.ASSIGNMENT_RULE_VERSION,
            "ruleSourceSha": "a" * 40,
            "ruleApprovalReference": "https://github.com/NYGsatoshi/Coglatas/issues/1105#issuecomment-1002",
            "predecessorArtifact": {
                "id": 77,
                "name": "perf05-campaign-db-campaign-intel-predecessor",
                "digest": "sha256:" + "b" * 64,
                "workflowRunId": 123,
            },
            "predecessorRawGroupsSha256": "c" * 64,
        }
        self.recovery = copy.deepcopy(self.consumed)
        self.recovery["schemaVersion"] = 4
        self.recovery["campaignId"] = "db-campaign-assignment-recovery"
        self.recovery["createdAtUtc"] = "2026-10-07T11:00:00Z"
        self.recovery["expiresAtUtc"] = "2026-10-10T11:00:00Z"
        self.recovery["authorization"] |= {
            "cause": campaign.RECOVERY_CAUSE,
            "supersedesCampaignId": self.consumed["campaignId"],
            "reference": "https://github.com/NYGsatoshi/Coglatas/issues/606#issuecomment-1003",
        }
        self.recovery["premeasurementRecovery"] = {
            "ruleVersion": campaign.RECOVERY_RULE_VERSION,
            "ruleSourceSha": "d" * 40,
            "failedRunId": 37603796183,
            "failedDeclarationSha": "e" * 40,
            "failedManifestSha256": campaign.digest(self.consumed),
            "failedRunCreatedAtUtc": "2026-10-07T09:54:23Z",
            "failedRunCompletedAtUtc": "2026-10-07T09:54:48Z",
            "failureStep": campaign.RECOVERY_FAILURE_STEP,
        }
        self.run = {
            "id": 37603796183,
            "head_sha": "e" * 40,
            "head_branch": "main",
            "event": "push",
            "path": campaign.WORKFLOW,
            "run_attempt": 1,
            "status": "completed",
            "conclusion": "failure",
            "created_at": "2026-10-07T09:54:23Z",
            "updated_at": "2026-10-07T09:54:48Z",
        }
        self.jobs = [{
            "name": campaign.RECOVERY_JOB_NAME,
            "status": "completed",
            "conclusion": "failure",
            "steps": [
                {"name": "Set up job", "conclusion": "success"},
                {"name": "Checkout independently rolled-out main policy and declaration", "conclusion": "success"},
                {"name": "Test fail-closed campaign and DB validators", "conclusion": "success"},
                {"name": campaign.RECOVERY_FAILURE_STEP, "conclusion": "failure"},
                {"name": campaign.RECOVERY_MEASUREMENT_STEP, "conclusion": "skipped"},
            ],
        }]

    def test_schema4_recovery_manifest_preserves_assignment_contract(self):
        campaign.validate_manifest(self.recovery, ROOT)
        self.assertEqual(
            campaign.campaign_environment_digest(self.consumed),
            campaign.campaign_environment_digest(self.recovery),
        )
        self.assertEqual(
            self.consumed["environmentAssignmentTransition"],
            self.recovery["environmentAssignmentTransition"],
        )

    def test_zero_capture_validator_failure_is_recoverable(self):
        campaign.validate_zero_capture_recovery_run(
            self.recovery["premeasurementRecovery"], self.run, self.jobs, []
        )

    def test_started_measurement_cannot_use_zero_capture_recovery(self):
        jobs = copy.deepcopy(self.jobs)
        jobs[0]["steps"][-1]["conclusion"] = "success"
        with self.assertRaisesRegex(PerformanceContractError, "measurement-already-started"):
            campaign.validate_zero_capture_recovery_run(
                self.recovery["premeasurementRecovery"], self.run, jobs, []
            )

    def test_any_artifact_blocks_zero_capture_recovery(self):
        with self.assertRaisesRegex(PerformanceContractError, "artifacts-exist"):
            campaign.validate_zero_capture_recovery_run(
                self.recovery["premeasurementRecovery"], self.run, self.jobs, [{"id": 1}]
            )

    def test_rerun_attempt_cannot_be_recovered(self):
        run = self.run | {"run_attempt": 2}
        with self.assertRaisesRegex(PerformanceContractError, "run-provenance-invalid"):
            campaign.validate_zero_capture_recovery_run(
                self.recovery["premeasurementRecovery"], run, self.jobs, []
            )

    def test_recovery_chain_is_forbidden(self):
        chained = copy.deepcopy(self.recovery)
        chained["campaignId"] = "db-campaign-assignment-recovery-2"
        chained["authorization"]["supersedesCampaignId"] = self.recovery["campaignId"]
        with self.assertRaisesRegex(PerformanceContractError, "predecessor-invalid"):
            campaign.validate_premeasurement_recovery(
                ROOT, chained, self.recovery, base_ref="approved-main", introduced=False
            )

    def test_assignment_cannot_chain_after_recovery(self):
        with self.assertRaisesRegex(PerformanceContractError, "transition-chain-forbidden"):
            campaign.validate_assignment_transition(
                ROOT, self.consumed, self.recovery, base_ref="approved-main", introduced=False
            )

    def test_narrative_rejected_word_is_not_a_revocation_directive(self):
        self.assertFalse(campaign.has_negative_approval_directive(
            ["- predecessor result remains REJECTED historical evidence"]
        ))
        for directive in ("REJECTED", "REVOKED", "NOT_APPROVED", "NOT APPROVED campaign"):
            with self.subTest(directive=directive):
                self.assertTrue(campaign.has_negative_approval_directive([directive]))

