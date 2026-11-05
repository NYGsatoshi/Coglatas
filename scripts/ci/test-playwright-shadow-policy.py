#!/usr/bin/env python3
"""Fail-closed CI policy tests for the isolated Playwright A/B shadow."""
from __future__ import annotations
import re
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = ROOT / ".github/workflows/ci.yml"
ADMISSION = (
    "    if: >-\n"
    "      github.event_name == 'pull_request' &&\n"
    "      github.base_ref == 'SecurityTestImplimation' &&\n"
    "      github.head_ref == 'ci-opt/playwright-shard-ab-20261105' &&\n"
    "      needs.frontend-build.result == 'success' &&\n"
    "      needs.frontend-build.outputs.frontend_playwright == 'true'\n"
)


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def job(document, name):
    match = re.search(r"(?m)^  " + re.escape(name) + r":\n", document)
    require(match is not None, "MISSING_JOB")
    tail = re.search(r"(?m)^  [a-z0-9-]+:\n", document[match.end():])
    return document[match.start():match.end() + tail.start()] if tail else document[match.start():]


def validate(document):
    require(type(document) is str and
            document.count("  playwright-shard-shadow:\n") == 1,
            "SHADOW_JOB")
    shadow = job(document, "playwright-shard-shadow")
    protected = job(document, "frontend-test")
    producer = job(document, "frontend-build")
    require(ADMISSION in shadow and shadow.count("    if: ") == 1,
            "SHADOW_ADMISSION")
    for token in (
        "    needs: frontend-build\n", "    runs-on: ubuntu-latest\n",
        "    permissions:\n      contents: read\n", "    timeout-minutes: 45\n",
        "      fail-fast: false\n", "        shard: ['1/2', '2/2']\n",
        "          persist-credentials: false\n",
        "          name: frontend-build-artifacts\n",
        '          COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: "1"\n',
        '          COGLATAS_STATIC_PLAYWRIGHT_WORKERS: "2"\n',
        '          COGLATAS_PLAYWRIGHT_REUSE_HOST_BUILD: "true"\n',
        "          npm run test:ui:angular:docker\n",
        "python3 -B scripts/ci/check-shadow-junit.py",
        "          COGLATAS_STATIC_PLAYWRIGHT_SHARD: $" + "{{ matrix.shard }}\n",
    ):
        require(token in shadow, "SHADOW_CONTROL")
    require(shadow.count("actions/download-artifact@") == 1,
            "SHADOW_SINGLE_ARTIFACT_DOWNLOAD")
    require("$(git rev-parse HEAD)" in shadow and
            "artifacts/ci/frontend-build-sha" in shadow and
            "frontend/dist/coglatas-web/angular-app.marker" in shadow,
            "SHADOW_SOURCE_BINDING")
    for forbidden in ("actions/upload-artifact@", "continue-on-error:",
                      "secrets.", "pull_request_target", "--grep", "--project",
                      "|| true", "npm --prefix frontend run build"):
        require(forbidden not in shadow, "SHADOW_UNSAFE")
    require("    name: frontend-test\n" in protected and
            "    if: $" + "{{ always() }}\n" in protected and
            "    needs: frontend-build\n" in protected and
            "        run: bash scripts/ci/run-frontend-parallel.sh\n" in protected,
            "ORIGINAL_FULL_FRONTEND")
    require("COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH:" not in protected and
            "COGLATAS_STATIC_PLAYWRIGHT_SHARD:" not in protected,
            "SHARD_LEAKED_INTO_PROTECTED_FRONTEND")
    require("        name: frontend-build-artifacts\n" in producer
            and producer.count("actions/upload-artifact@") == 1,
            "FRONTEND_ARTIFACT_OWNER")
    require(document.count("    name: frontend-test\n") == 1,
            "DUPLICATE_REQUIRED_CONTEXT")
    require("playwright-shard-shadow" not in
            (ROOT / "governance/required-checks.json").read_text(encoding="utf-8"),
            "SHADOW_BECAME_REQUIRED")


class ShadowPolicyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = WORKFLOW.read_text(encoding="utf-8")

    def test_exact_experiment_keeps_protected_full_suite(self):
        validate(self.source)

    def test_weakening_admission_is_rejected(self):
        for old, new in (
            ("github.event_name == 'pull_request'", "true"),
            ("github.base_ref == 'SecurityTestImplimation'", "true"),
            ("github.head_ref == 'ci-opt/playwright-shard-ab-20261105'", "true"),
            ("needs.frontend-build.result == 'success'", "true"),
            ("needs.frontend-build.outputs.frontend_playwright == 'true'", "true"),
        ):
            with self.subTest(old=old):
                block = job(self.source, "playwright-shard-shadow")
                change = self.source.replace(block, block.replace(old, new, 1), 1)
                self.assertNotEqual(change, self.source)
                with self.assertRaises(ValueError):
                    validate(change)

    def test_injecting_shard_authorization_into_required_gate_fails(self):
        injection = ("  frontend-test:\n"
                     "    env:\n"
                     "      COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: '1'\n")
        candidate = self.source.replace("  frontend-test:\n", injection, 1)
        self.assertNotEqual(candidate, self.source)
        with self.assertRaises(ValueError):
            validate(candidate)

    def test_missing_shards_or_worker_overprovisioning_fail(self):
        for old, new in (
            ("        shard: ['1/2', '2/2']", "        shard: ['1/2']"),
            ("        shard: ['1/2', '2/2']", "        shard: ['1/2', '1/2']"),
            ('COGLATAS_STATIC_PLAYWRIGHT_WORKERS: "2"',
             'COGLATAS_STATIC_PLAYWRIGHT_WORKERS: "4"'),
            ('COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: "1"',
             'COGLATAS_STATIC_PLAYWRIGHT_SHARD_AUTH: "0"'),
        ):
            with self.subTest(old=old):
                with self.assertRaises(ValueError):
                    validate(self.source.replace(old, new, 1))

    def test_duplicate_artifacts_and_missing_full_gate_fail(self):
        for addition in (
            "      - name: Duplicate\n        uses: actions/upload-artifact@latest\n",
            "      - name: Bypass\n        continue-on-error: true\n",
            "      - name: Build again\n        run: npm --prefix frontend run build\n",
        ):
            candidate = self.source.replace(
                "  playwright-shard-shadow:\n",
                "  playwright-shard-shadow:\n" + addition, 1)
            with self.assertRaises(ValueError):
                validate(candidate)
        with self.assertRaises(ValueError):
            validate(self.source.replace(
                "        run: bash scripts/ci/run-frontend-parallel.sh\n",
                "        run: echo omitted\n", 1))


if __name__ == "__main__":
    unittest.main()
