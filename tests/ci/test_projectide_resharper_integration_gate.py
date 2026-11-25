import copy
from hashlib import sha256
from io import BytesIO
import json
from pathlib import Path
import sys
from types import SimpleNamespace
import unittest
from unittest.mock import patch
from urllib.parse import quote
from zipfile import ZipFile

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "scripts/ci"))
import projectide_resharper_integration_gate as gate


HEAD, BASE, MERGE, TREE = (char * 40 for char in "abcd")
PR, RUN, JOB, ARTIFACT, REPO_ID, WORKFLOW, SUITE = 41, 42, 43, 44, 45, 46, 47
HEAD_REF = "projectide/review"
ROOT = f"repos/{gate.REPOSITORY}"
RUN_PATH = f"actions/runs/{RUN}"
INSPECTION_URL = f"https://api.github.com/{ROOT}/check-runs/{JOB}"
START, END = "2026-11-25T11:27:39Z", "2026-11-25T11:27:40Z"
SUCCESS = {"status": "completed", "conclusion": "success"}


def inspection_zip(revision=MERGE, changed=b"src/feature.cs\0"):
    sarif = dict(version="2.1.0", runs=[{
        "tool": {"driver": {"name": "InspectCode"}},
        "invocations": [{"executionSuccessful": True}],
        "results": [],
        "versionControlProvenance": [{"revisionId": revision}],
    }])
    stream = BytesIO()
    with ZipFile(stream, "w") as package:
        package.writestr("inspectcode.sarif.json", json.dumps(sarif))
        package.writestr("changed-files.nul", changed)
        package.writestr("projects.txt", "")
    return stream.getvalue()


class AdmissionTests(unittest.TestCase):
    def setUp(self):
        repo = {"id": REPO_ID, "full_name": gate.REPOSITORY}
        head_ref = {"repo": repo, "ref": HEAD_REF, "sha": HEAD}
        base_ref = {"repo": repo, "ref": gate.BASE, "sha": BASE}
        pull = {"id": 100, "number": PR, "head": head_ref, "base": base_ref}
        self.pr = dict(pull, state="open", merged=False, draft=False, mergeable=True, merge_commit_sha=MERGE)
        self.run = dict(SUCCESS, id=RUN, run_attempt=1, workflow_id=WORKFLOW,
                        path=gate.WORKFLOW, name="PR CI", event="pull_request",
                        head_sha=HEAD, head_branch=HEAD_REF, head_repository=repo, repository=repo,
                        check_suite_id=SUITE, pull_requests=[copy.deepcopy(pull)])
        self.job = dict(SUCCESS, id=JOB, run_id=RUN, run_attempt=1, head_sha=HEAD,
                        name=gate.INSPECTION, check_run_url=INSPECTION_URL,
                        started_at=START, completed_at=END,
                        steps=[dict(SUCCESS, name=name, number=index, started_at=START,
                                    completed_at=END if index == len(gate.STEPS) else START)
                               for index, name in enumerate(gate.STEPS, 1)])
        self.checks = [dict(SUCCESS, name=name, head_sha=HEAD, app={"id": gate.APP})
                       for name in gate.TRACKED_CHECKS]
        self.checks.append(dict(SUCCESS, id=JOB, name=gate.INSPECTION, head_sha=HEAD, url=INSPECTION_URL,
                                check_suite={"id": SUITE}, app={"id": gate.APP}))
        self.artifact = {"id": ARTIFACT, "name": gate.ARTIFACT, "expired": False, "created_at": END,
                         "workflow_run": {"id": RUN, "head_sha": HEAD, "head_branch": HEAD_REF,
                                          "repository_id": REPO_ID, "head_repository_id": REPO_ID}}
        self.set_archive(inspection_zip())
        self.threads = {"totalCount": 1, "nodes": [{"isResolved": True}],
                        "pageInfo": {"hasNextPage": False}}
        self.review = {"data": {"repository": {"databaseId": REPO_ID, "pullRequest": {
            "number": PR, "state": "OPEN", "isDraft": False,
            "headRefOid": HEAD, "reviewDecision": None, "reviewThreads": self.threads,
        }}}}
        self.merge = {"sha": MERGE, "tree": {"sha": TREE}, "parents": [{"sha": BASE}, {"sha": HEAD}]}
        self.rules = []
        self.base = {"object": {"sha": BASE}}
        self.evidence = {
            f"pulls/{PR}": self.pr,
            f"git/ref/pull/{PR}/merge": {"object": {"sha": MERGE}},
            f"git/ref/heads/{gate.BASE}": self.base,
            f"git/commits/{MERGE}": self.merge,
            "actions/workflows/ci.yml": {"id": WORKFLOW, "name": "PR CI", "path": gate.WORKFLOW},
            RUN_PATH: self.run,
            f"{RUN_PATH}/attempts/1/jobs?per_page=100": {"total_count": 1, "jobs": [self.job]},
            f"commits/{HEAD}/check-runs?per_page=100": {"total_count": len(self.checks), "check_runs": self.checks},
            f"rules/branches/{quote(gate.BASE, safe='')}": self.rules,
            f"{RUN_PATH}/artifacts?per_page=100": {"total_count": 1, "artifacts": [self.artifact]},
        }
        self.args = SimpleNamespace(repository=gate.REPOSITORY, expected_head=HEAD, expected_base=BASE,
                                    expected_tree=TREE, pr=PR, run_id=RUN, attempt=1, artifact_zip=None)

    def set_archive(self, archive):
        self.archive = archive
        self.artifact.update(digest=f"sha256:{sha256(archive).hexdigest()}", size_in_bytes=len(archive))

    def api(self, *args):
        if args[0] == "graphql":
            payload = self.review
        else:
            path = args[-1].removeprefix(f"{ROOT}/")
            if path == f"actions/artifacts/{ARTIFACT}/zip":
                return self.archive
            payload = self.evidence[path]
        return json.dumps(payload).encode()

    def verify(self):
        with patch.object(gate, "gh", self.api):
            return gate.qualify(self.args)

    def assert_hold(self, reason):
        with self.assertRaisesRegex(gate.Hold, f"^{reason}$"):
            self.verify()

    def test_qualifies_only_with_current_provider_evidence(self):
        self.assertEqual("QUALIFIED", self.verify()["decision"])

    def test_draft_holds(self):
        self.pr["draft"] = True
        self.assert_hold("PR_NOT_OPEN_READY")

    def test_moved_base_ref_holds(self):
        self.base["object"]["sha"] = "e" * 40
        self.assert_hold("BASE_REF_DRIFT")

    def test_wrong_actual_merge_tree_holds(self):
        self.merge["tree"]["sha"] = "e" * 40
        self.assert_hold("TEST_MERGE_DRIFT")

    def test_reversed_actual_merge_parents_hold(self):
        self.merge["parents"].reverse()
        self.assert_hold("TEST_MERGE_DRIFT")

    def test_similarly_named_workflow_holds(self):
        self.run["workflow_id"] += 1
        self.assert_hold("CI_PROVENANCE")

    def test_run_for_other_pr_holds(self):
        self.run["pull_requests"][0]["number"] += 1
        self.assert_hold("CI_PR_IDENTITY")

    def test_newer_attempt_holds(self):
        self.run["run_attempt"] += 1
        self.assert_hold("CI_PROVENANCE")

    def test_old_run_base_holds(self):
        self.run["pull_requests"][0]["base"]["sha"] = "e" * 40
        self.assert_hold("CI_PR_SOURCE_DRIFT")

    def test_incomplete_inspection_step_holds_even_with_green_checks(self):
        self.job["steps"][3]["conclusion"] = "cancelled"
        self.assert_hold("INSPECTION_STEP_INCOMPLETE")

    def test_wrong_inspection_check_suite_holds(self):
        self.checks[-1]["check_suite"]["id"] += 1
        self.assert_hold("INSPECTION_CHECK_MISMATCH")

    def test_duplicate_tracked_check_holds(self):
        self.checks.append(copy.deepcopy(self.checks[0]))
        self.evidence[f"commits/{HEAD}/check-runs?per_page=100"]["total_count"] += 1
        self.assert_hold("REQUIRED_CHECK_AMBIGUOUS")

    def test_missing_effective_status_check_holds(self):
        self.rules.append({"type": "required_status_checks", "parameters": {
            "required_status_checks": [{"context": "new-check", "integration_id": gate.APP}],
        }})
        self.assert_hold("REQUIRED_CHECK_AMBIGUOUS")

    def test_artifact_from_another_attempt_holds(self):
        self.artifact["created_at"] = "2026-11-25T11:27:38Z"
        self.assert_hold("ARTIFACT_PROVENANCE")

    def test_wrong_artifact_sha_holds(self):
        self.artifact["digest"] = "sha256:" + "a" * 64
        self.assert_hold("ARTIFACT_INTEGRITY")

    def test_source_report_for_other_merge_holds(self):
        self.set_archive(inspection_zip(revision=HEAD))
        self.assert_hold("ARTIFACT_SARIF_SOURCE")

    def test_entry_change_requires_composition_receipt(self):
        self.set_archive(inspection_zip(changed=b"tests/Coglatas.ProjectIde.Entry.Tests/Feature.cs\0"))
        self.assert_hold("ARTIFACT_COMPOSITION_MISSING")

    def test_unresolved_review_thread_holds(self):
        self.threads["nodes"][0]["isResolved"] = False
        self.assert_hold("REVIEW_THREADS_UNRESOLVED")

    def test_incomplete_review_threads_hold(self):
        self.threads["pageInfo"]["hasNextPage"] = True
        self.assert_hold("REVIEW_THREADS_INCOMPLETE")


if __name__ == "__main__":
    unittest.main()
