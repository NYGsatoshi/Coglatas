#!/usr/bin/env python3
"""Regression controls for the credentialed SonarCloud push/PR routing boundary."""

from __future__ import annotations

import json
import os
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github" / "workflows" / "sonarcloud-ci.yml"
REPOSITORY = "NYGsatoshi/Coglatas"
OWNER = REPOSITORY.split("/", 1)[0]
BRANCH = "fix/sonarcloud-ci-based-20261027"
SHA = "a" * 40


def routing_shell() -> str:
    """Read the *actual* inline routing shell rather than a duplicated fixture."""
    lines = WORKFLOW.read_text(encoding="utf-8").splitlines()
    anchor = "      - name: Resolve main or exact same-repository PR"
    index = lines.index(anchor)
    start = next(
        i for i in range(index + 1, len(lines)) if lines[i] == "        run: |"
    )
    script = []
    for line in lines[start + 1 :]:
        if line.startswith("          "):
            script.append(line[10:])
        elif not line.strip():
            script.append("")
        else:
            break
    if not script or not any("jq -c" in line for line in script):
        raise AssertionError("Could not find the canonical GH API/JQ routing block")
    return "\n".join(script) + "\n"


def pull(*, number: int = 1470, head_repo: str = REPOSITORY,
         base_repo: str = REPOSITORY, head_branch: str = BRANCH,
         head_sha: str = SHA, base_branch: str = "main",
         state: str = "open") -> dict:
    return {
        "number": number,
        "state": state,
        "base": {"ref": base_branch, "repo": {"full_name": base_repo}},
        "head": {
            "ref": head_branch,
            "sha": head_sha,
            "repo": {"full_name": head_repo},
        },
    }


class SonarCloudRoutingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.script = routing_shell()

    def invoke(self, prs: list[dict] | str, *, branch: str = BRANCH,
               sha: str = SHA, actor: str = OWNER,
               repository: str = REPOSITORY) -> tuple[subprocess.CompletedProcess[str], dict[str, str]]:
        with tempfile.TemporaryDirectory() as work:
            folder = Path(work)
            payload = folder / "prs.json"
            payload.write_text(
                prs if isinstance(prs, str) else json.dumps(prs),
                encoding="utf-8",
            )
            output = folder / "github-output"
            command = folder / "gh"
            command.write_text(
                '#!/usr/bin/env bash\nset -euo pipefail\ncat "$MOCK_PR_DATA"\n',
                encoding="utf-8",
            )
            command.chmod(0o755)
            env = os.environ | {
                "PATH": f"{folder}:{os.environ['PATH']}",
                "MOCK_PR_DATA": str(payload),
                "TARGET_REPOSITORY": repository,
                "GITHUB_REPOSITORY_OWNER": OWNER,
                "TARGET_BRANCH": branch,
                "TARGET_SHA": sha,
                "TARGET_ACTOR": actor,
                "GITHUB_OUTPUT": str(output),
            }
            result = subprocess.run(
                ["bash", "-euo", "pipefail", "-c", self.script],
                text=True,
                capture_output=True,
                env=env,
                timeout=15,
                check=False,
            )
            values = {}
            if output.exists():
                for line in output.read_text(encoding="utf-8").splitlines():
                    key, value = line.split("=", 1)
                    values[key] = value
            return result, values

    def test_main_push_is_scanned_without_pr_lookup(self) -> None:
        result, values = self.invoke("not json", branch="main", actor="renovate[bot]")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(values, {"should_scan": "true", "analysis_kind": "main"})

    def test_owner_pushed_matching_pr(self) -> None:
        result, values = self.invoke([pull()])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(
            values,
            {
                "should_scan": "true",
                "analysis_kind": "pull_request",
                "pr_number": "1470",
                "pr_branch": BRANCH,
                "pr_base": "main",
            },
        )

    def test_no_pr_is_not_a_scan(self) -> None:
        result, values = self.invoke([])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(values, {"should_scan": "false"})

    def test_fork_head_is_excluded(self) -> None:
        result, values = self.invoke([pull(head_repo="someone/fork")])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(values, {"should_scan": "false"})

    def test_stale_revision_is_excluded(self) -> None:
        result, values = self.invoke([pull(head_sha="b" * 40)])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(values, {"should_scan": "false"})

    def test_different_pr_branch_is_excluded(self) -> None:
        result, values = self.invoke([pull(head_branch="wrong-branch")])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(values, {"should_scan": "false"})

    def test_wrong_base_branch_and_repo_are_excluded(self) -> None:
        for item in (
            pull(base_branch="develop"),
            pull(base_repo="someone/other"),
            pull(state="closed"),
        ):
            with self.subTest(item=item):
                result, values = self.invoke([item])
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertEqual(values, {"should_scan": "false"})

    def test_bot_pushed_pr_is_not_given_analysis_credentials(self) -> None:
        result, values = self.invoke([pull()], actor="renovate[bot]")
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(values, {"should_scan": "false"})

    def test_wrong_repo_fails_closed(self) -> None:
        result, values = self.invoke([pull()], repository="someone/other")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(values, {})

    def test_two_matching_prs_fail_closed(self) -> None:
        result, values = self.invoke([pull(), pull(number=1471)])
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(values, {})

    def test_invalid_api_response_fails_closed(self) -> None:
        result, values = self.invoke("not-json")
        self.assertNotEqual(result.returncode, 0)
        self.assertEqual(values, {})


if __name__ == "__main__":
    unittest.main()
