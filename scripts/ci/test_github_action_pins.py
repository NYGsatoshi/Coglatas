#!/usr/bin/env python3
from __future__ import annotations

import json
import tempfile
import unittest
from pathlib import Path

from github_action_pins import validate_repository


PIN = "a" * 40
OTHER_PIN = "b" * 40


class GithubActionPinsTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        (self.root / ".github/workflows").mkdir(parents=True)
        (self.root / "governance").mkdir(parents=True)
        self.policy = self.root / "governance/github-actions-allowlist.json"
        self.policy.write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "requiredWorkflows": [".github/workflows/required.yml"],
                    "actions": {
                        "actions/checkout": {
                            "sha": PIN,
                            "version": "v7",
                            "purpose": "checkout",
                            "privilegedAllowed": True,
                        },
                        "owner/reusable": {
                            "sha": PIN,
                            "version": "v1",
                            "purpose": "reusable workflow fixture",
                            "privilegedAllowed": True,
                            "path": ".github/workflows/check.yml",
                        },
                        "vendor/unprivileged": {
                            "sha": PIN,
                            "version": "v2",
                            "purpose": "unprivileged fixture",
                            "privilegedAllowed": False,
                        },
                    },
                },
                indent=2,
            )
            + "\n",
            encoding="utf-8",
        )

    def tearDown(self) -> None:
        self.temp.cleanup()

    def write_workflow(self, name: str, body: str) -> None:
        (self.root / ".github/workflows" / name).write_text(body, encoding="utf-8")

    def errors(self) -> list[str]:
        errors, _ = validate_repository(self.root, self.policy)
        return errors

    def test_reviewed_full_sha_passes_required_workflow(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\npermissions:\n  contents: read\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@{PIN} # v7\n""",
        )
        self.assertEqual(self.errors(), [])

    def test_mutable_major_tag_fails_protected_workflow(self) -> None:
        self.write_workflow(
            "required.yml",
            """name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v7\n""",
        )
        self.assertTrue(any("full immutable SHA" in error for error in self.errors()))

    def test_branch_ref_fails_protected_workflow(self) -> None:
        self.write_workflow(
            "required.yml",
            """name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@main # v7\n""",
        )
        self.assertTrue(any("full immutable SHA" in error for error in self.errors()))

    def test_short_sha_fails_protected_workflow(self) -> None:
        self.write_workflow(
            "required.yml",
            """name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@123abc # v7\n""",
        )
        self.assertTrue(any("full immutable SHA" in error for error in self.errors()))

    def test_unreviewed_full_sha_fails(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@{OTHER_PIN} # v7\n""",
        )
        self.assertTrue(any("does not match reviewed allowlist SHA" in error for error in self.errors()))

    def test_unknown_action_repository_fails_even_unprotected(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@{PIN} # v7\n""",
        )
        self.write_workflow(
            "helper.yml",
            """name: helper\non: workflow_dispatch\njobs:\n  helper:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: unknown/example@v1\n""",
        )
        self.assertTrue(any("is not allowlisted" in error for error in self.errors()))

    def test_missing_version_comment_fails_protected_workflow(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@{PIN}\n""",
        )
        self.assertTrue(any("requires version comment" in error for error in self.errors()))

    def test_local_action_does_not_require_allowlist_entry(self) -> None:
        self.write_workflow(
            "required.yml",
            """name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: ./.github/actions/local-check\n""",
        )
        self.assertEqual(self.errors(), [])

    def test_external_reusable_workflow_is_pinned(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\njobs:\n  reusable:\n    uses: owner/reusable/.github/workflows/check.yml@{PIN} # v1\n""",
        )
        self.assertEqual(self.errors(), [])

    def test_reusable_workflow_path_must_match_allowlist(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\njobs:\n  reusable:\n    uses: owner/reusable/.github/workflows/other.yml@{PIN} # v1\n""",
        )
        self.assertTrue(
            any("does not match reviewed allowlist path" in error for error in self.errors())
        )

    def test_secret_bearing_workflow_is_automatically_protected(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@{PIN} # v7\n""",
        )
        self.write_workflow(
            "secret.yml",
            """name: secret\non: workflow_dispatch\njobs:\n  privileged:\n    runs-on: ubuntu-latest\n    environment: protected\n    env:\n      TOKEN: ${{ secrets.TEST_TOKEN }}\n    steps:\n      - uses: actions/checkout@v7\n""",
        )
        self.assertTrue(any("secret.yml" in error and "full immutable SHA" in error for error in self.errors()))

    def test_write_permission_workflow_is_automatically_protected(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@{PIN} # v7\n""",
        )
        self.write_workflow(
            "write.yml",
            """name: write\non: workflow_dispatch\npermissions:\n  contents: write\njobs:\n  write:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@v7\n""",
        )
        self.assertTrue(any("write.yml" in error and "full immutable SHA" in error for error in self.errors()))

    def test_unprivileged_allowlisted_action_may_stay_mutable_outside_protected_scope(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@{PIN} # v7\n""",
        )
        self.write_workflow(
            "helper.yml",
            """name: helper\non: workflow_dispatch\npermissions:\n  contents: read\njobs:\n  helper:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: vendor/unprivileged@v2\n""",
        )
        self.assertEqual(self.errors(), [])

    def test_unprivileged_action_is_rejected_if_workflow_becomes_protected(self) -> None:
        self.write_workflow(
            "required.yml",
            f"""name: required\non: pull_request\njobs:\n  test:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: actions/checkout@{PIN} # v7\n""",
        )
        self.write_workflow(
            "helper.yml",
            f"""name: helper\non: workflow_dispatch\npermissions:\n  contents: write\njobs:\n  helper:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: vendor/unprivileged@{PIN} # v2\n""",
        )
        self.assertTrue(any("not approved for protected workflows" in error for error in self.errors()))


    def test_quoted_uses_keys_still_require_immutable_pins(self) -> None:
        for key in ("'uses'", '"uses"'):
            with self.subTest(key=key):
                self.write_workflow(
                    "required.yml",
                    "name: required\non: pull_request\njobs:\n  test:\n"
                    "    runs-on: ubuntu-latest\n    steps:\n"
                    f"      - {key}: actions/checkout@{PIN} # v7\n",
                )
                self.assertEqual([], self.errors())
                self.write_workflow(
                    "required.yml",
                    "name: required\non: pull_request\njobs:\n  test:\n"
                    "    runs-on: ubuntu-latest\n    steps:\n"
                    f"      - {key}: actions/checkout@v7\n",
                )
                self.assertTrue(
                    any("full immutable SHA" in e for e in self.errors())
                )

    def test_flow_mapping_steps_fail_closed_without_silent_uses_bypass(self) -> None:
        for step in (
            "- {uses: actions/checkout@v7}",
            "- {'uses': actions/checkout@v7}",
            '- {"uses": actions/checkout@v7}',
            "- {name: checkout, uses: actions/checkout@v7}",
            "- {name: noop, run: echo ok}",
        ):
            with self.subTest(step=step):
                self.write_workflow(
                    "required.yml",
                    "name: required\non: pull_request\njobs:\n  test:\n"
                    "    runs-on: ubuntu-latest\n    steps:\n"
                    f"      {step}\n",
                )
                self.assertTrue(
                    any("unsupported flow-style" in e
                        for e in self.errors())
                )

    def test_inline_flow_steps_and_job_reusable_uses_fail_closed(self) -> None:
        for job in (
            "  test:\n    runs-on: ubuntu-latest\n"
            "    steps: [{uses: actions/checkout@v7}]\n",
            "  reusable: {uses: owner/reusable/.github/workflows/check.yml@v1}\n",
        ):
            with self.subTest(job=job):
                self.write_workflow(
                    "required.yml",
                    "name: required\non: pull_request\njobs:\n" + job,
                )
                self.assertTrue(
                    any("unsupported flow-style" in e
                        for e in self.errors())
                )

    def test_root_flow_jobs_mapping_fails_closed(self) -> None:
        self.write_workflow(
            "required.yml",
            "name: required\non: pull_request\n"
            "jobs: {test: {runs-on: ubuntu-latest, "
            "steps: [{uses: actions/checkout@v7}]}}\n",
        )
        self.assertTrue(
            any("unsupported flow-style" in e
                for e in self.errors())
        )

    def test_unrelated_event_flow_mapping_does_not_hide_valid_steps(self) -> None:
        self.write_workflow(
            "required.yml",
            "name: required\non:\n"
            "  pull_request: {types: [opened]}\n"
            "jobs:\n  test:\n    runs-on: ubuntu-latest\n"
            f"    steps:\n      - uses: actions/checkout@{PIN} # v7\n",
        )
        self.assertEqual([], self.errors())

    def test_structural_parser_rejects_indent_quote_and_multiline_flow_bypasses(self) -> None:
        cases = (
            'jobs:\n    "test":\n        runs-on: ubuntu-latest\n'
            '        steps: [{uses: actions/checkout@v7}]\n',
            "'jobs':\n  test:\n    steps:\n      - {uses: actions/checkout@v7}\n",
            '"jobs":\n  test:\n    steps:\n      - {"uses": actions/checkout@v7}\n',
            'jobs:\n  "test":\n    steps:\n      [\n        {uses: actions/checkout@v7}\n      ]\n',
            'jobs:\n  test:\n    steps:\n      -\n        {uses: actions/checkout@v7}\n',
            'jobs:\n  "test": {runs-on: ubuntu-latest, steps: []}\n',
        )
        for jobs in cases:
            with self.subTest(jobs=jobs):
                self.write_workflow("required.yml", "on: pull_request\n" + jobs)
                self.assertTrue(
                    any("unsupported flow-style" in e for e in self.errors()),
                    self.errors(),
                )

    def test_encoded_uses_key_cannot_hide_from_structural_inventory(self) -> None:
        self.write_workflow(
            "required.yml",
            "on: pull_request\njobs:\n  test:\n    steps:\n"
            '      - "u\\u0073es": actions/checkout@v7\n',
        )
        self.assertTrue(any("cannot parse uses reference" in e for e in self.errors()))

    def test_yaml_merge_and_duplicate_uses_keys_fail_closed(self) -> None:
        cases = (
            '      - <<: *unreviewed\n',
            f'      - uses: actions/checkout@{PIN} # v7\n'
            '        "uses": actions/checkout@v7\n',
        )
        for step in cases:
            with self.subTest(step=step):
                self.write_workflow(
                    "required.yml",
                    "on: pull_request\njobs:\n  test:\n    steps:\n" + step,
                )
                self.assertNotEqual([], self.errors())

    def test_unrelated_flow_and_literal_shell_strings_still_pass(self) -> None:
        self.write_workflow(
            "required.yml",
            "on:\n  pull_request: {types: [opened]}\n"
            '"jobs":\n  "test":\n    steps:\n'
            "      - run: |\n"
            "          echo 'jobs: {fake: true}'\n"
            "          echo '- {uses: actions/checkout@v7}'\n"
            f"      - 'uses': actions/checkout@{PIN} # v7\n",
        )
        self.assertEqual([], self.errors())

    def test_reject_yaml_with_multiple_documents(self) -> None:
        self.write_workflow(
            "required.yml",
            f"on: pull_request\njobs:\n  test:\n    steps:\n"
            f"      - uses: actions/checkout@{PIN} # v7\n"
            "---\njobs:\n  hidden:\n    steps:\n      - uses: actions/checkout@v7\n",
        )
        self.assertTrue(any("expected one YAML document" in e for e in self.errors()))


if __name__ == "__main__":
    unittest.main()
