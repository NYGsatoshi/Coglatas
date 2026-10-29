#!/usr/bin/env python3
"""Guard reviewed setup-node cache ownership with offline negative controls.

An implicit setup-node cache writes ~/.npm across runs. These jobs install
dependencies via scripts/ci/npm-ci-retry.sh with a separate per-job cache, or
only run a Node verifier. Disable the redundant cross-run cache, not the
version-pinned npm installation, test execution or dependency protections.
"""
from __future__ import annotations

import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]

EXPECTED = {
    ".github/workflows/ci.yml": {
        "frontend-build": "scripts/ci/npm-ci-retry.sh frontend",
        "frontend-test": "scripts/ci/npm-ci-retry.sh .",
    },
    ".github/workflows/main-validation.yml": {
        "main-frontend-test": "scripts/ci/npm-ci-retry.sh frontend",
    },
    ".github/workflows/main-build-artifacts.yml": {
        "main-frontend-build": "scripts/ci/npm-ci-retry.sh frontend",
        "qodana-cloud": "scripts/quality/check-qodana-project-model.mjs",
    },
    ".github/workflows/node-toolchain-preflight.yml": {
        "node-toolchain-preflight": "scripts/ci/verify-node-toolchain.mjs",
    },
    ".github/workflows/qodana_trusted_gate.yml": {
        "qodana-dotnet-community": "scripts/quality/check-qodana-project-model.mjs",
    },
    ".github/workflows/qodana_cloud_quality.yml": {
        "qodana-cloud": "scripts/quality/check-qodana-project-model.mjs",
    },
    ".github/workflows/sonarcloud-ci.yml": {
        "sonarcloud": "scripts/ci/npm-ci-retry.sh frontend",
    },
    ".github/workflows/real-backend-p0-preflight.yml": {
        "real-backend-p0-preflight": "tests/ui/real-backend-smoke-compose-helpers.node-test.mjs",
    },
    ".github/workflows/thread-functional-typecheck.yml": {
        "strict-thread-contract": "scripts/ci/npm-ci-retry.sh frontend",
    },
    ".github/workflows/composer-functional-typecheck.yml": {
        "strict-composer-contract": "scripts/ci/npm-ci-retry.sh frontend",
    },
    ".github/workflows/licensed-real-backend-acceptance.yml": {
        "licensed-real-backend": "scripts/ci/npm-ci-retry.sh .",
    },
}

JOB = re.compile(r"(?m)^  ([A-Za-z][A-Za-z0-9_-]*):\s*$")
SETUP_NODE = re.compile(r"(?m)^        uses: actions/setup-node@([0-9a-f]{40})\s*(?:#.*)?$")
INPUT_CACHE = re.compile(r"(?m)^          package-manager-cache: (\S+)\s*$")
INPUT_EXPLICIT_CACHE = re.compile(r"(?m)^          cache:.*$")


def job_blocks(source: str) -> dict[str, str]:
    marker = re.search(r"(?m)^jobs:\s*$", source)
    if marker is None:
        return {}
    section = source[marker.end():]
    headings = list(JOB.finditer(section))
    return {
        header.group(1): section[header.end():headings[index + 1].start() if index + 1 < len(headings) else len(section)]
        for index, header in enumerate(headings)
    }


def violations(workflows: dict[str, str], installer: str) -> list[str]:
    issues = []
    # The isolation contract has to remain true before disabling setup-node's
    # own global cache. Do not assume scripts remain unchanged forever.
    if (
        'export NPM_CONFIG_CACHE="$secure_cache"' not in installer
        or 'secure_cache="$secure_temp_root/npm-cache-' not in installer
        or "export NPM_CONFIG_STRICT_ALLOW_SCRIPTS=" not in installer
        or 'export NPM_CONFIG_REGISTRY="https://registry.npmjs.org/"' not in installer
    ):
        issues.append("Per-job authenticated npm install boundary is missing")
    for path, targets in EXPECTED.items():
        source = workflows.get(path)
        if not isinstance(source, str):
            issues.append(f"{path}: missing workflow")
            continue
        jobs = job_blocks(source)
        for job_id, owner in targets.items():
            job = jobs.get(job_id)
            if job is None:
                issues.append(f"{path}: missing job {job_id}")
                continue
            if owner not in job:
                issues.append(f"{path}/{job_id}: expected fixed package owner missing")
            uses = list(SETUP_NODE.finditer(job))
            if len(uses) != 1:
                issues.append(f"{path}/{job_id}: expected exactly one SHA-pinned setup-node")
                continue
            # Limit inspection to this specific setup-node step, not an
            # unrelated later step with a similar input spelling.
            offset = job.find("\n      - ", uses[0].end())
            step = job[uses[0].start(): offset if offset != -1 else len(job)]
            if not re.search(r"(?m)^        with:\s*$", step):
                issues.append(f"{path}/{job_id}: missing setup-node with block")
            versions = re.findall(r"(?m)^          node-version: (\S+)\s*$", step)
            if versions not in (['"24"'], ["'24'"]):
                issues.append(f"{path}/{job_id}: Node 24 toolchain not pinned")
            flags = INPUT_CACHE.findall(step)
            if flags != ["false"]:
                issues.append(f"{path}/{job_id}: implicit npm cache must be disabled exactly once")
            if INPUT_EXPLICIT_CACHE.search(step):
                issues.append(f"{path}/{job_id}: explicit setup-node cache not permitted")
    return issues


def one_replacement(
    workflows: dict[str, str], before: str, after: str, path: str = ".github/workflows/ci.yml"
) -> dict[str, str]:
    changed = workflows.copy()
    assert changed[path].count(before) >= 1, f"Mutation anchor missing: {before!r}"
    changed[path] = changed[path].replace(before, after, 1)
    return changed


def main() -> int:
    workflows = {path: (ROOT / path).read_text(encoding="utf-8") for path in EXPECTED}
    installer = (ROOT / "scripts/ci/npm-ci-retry.sh").read_text(encoding="utf-8")
    errors = violations(workflows, installer)
    if errors:
        print("Setup-node cache ownership rejected:\n" + "\n".join(errors))
        return 1

    controls = {
        "auto-cache enabled": one_replacement(
            workflows, "          package-manager-cache: false", "          package-manager-cache: true"
        ),
        "auto-cache omitted": one_replacement(
            workflows, "          package-manager-cache: false", "          # cache setting missing"
        ),
        "manual cache enabled": one_replacement(
            workflows, "          package-manager-cache: false",
            "          package-manager-cache: false\n          cache: npm",
        ),
        "duplicate cache input": one_replacement(
            workflows, "          package-manager-cache: false",
            "          package-manager-cache: false\n          package-manager-cache: false",
        ),
        "lost Node version": one_replacement(
            workflows, '          node-version: "24"', '          node-version: "23"'
        ),
        "lost fixed owner": one_replacement(
            workflows, "scripts/ci/npm-ci-retry.sh frontend",
            "scripts/ci/UNREVIEWED-INSTALL.sh frontend",
        ),
        "qodana guard cache regression": one_replacement(
            workflows, "          package-manager-cache: false", "          package-manager-cache: true",
            ".github/workflows/qodana_trusted_gate.yml",
        ),
        "single-quoted typecheck Node drift": one_replacement(
            workflows, "          node-version: '24'", "          node-version: '23'",
            ".github/workflows/thread-functional-typecheck.yml",
        ),
    }
    for label, changed in controls.items():
        if not violations(changed, installer):
            raise AssertionError(f"{label} mutation passed the cache ownership check")
    if not violations(workflows, installer.replace('export NPM_CONFIG_CACHE="$secure_cache"', '')):
        raise AssertionError("missing isolated npm cache boundary was accepted")
    if not violations(workflows, installer.replace('export NPM_CONFIG_STRICT_ALLOW_SCRIPTS=', '')):
        raise AssertionError("missing npm lifecycle policy boundary was accepted")

    print("Setup-node unused-cache policy PASS; 10 mutation controls rejected.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
