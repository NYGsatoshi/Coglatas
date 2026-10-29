#!/usr/bin/env python3
"""Static negative controls for the split, branch-protected frontend-test gate."""
from __future__ import annotations

import re
from pathlib import Path

WORKFLOW = Path(".github/workflows/ci.yml")
ROOT_JOB = re.compile(r"(?m)^  [a-z][a-z0-9-]*:\s*$")


def job_block(workflow: str, name: str) -> str:
    marker = f"  {name}:\n"
    if workflow.count(marker) != 1:
        raise AssertionError(f"Expected exactly one {name} job")
    start = workflow.index(marker)
    following = workflow[start + len(marker):]
    end = ROOT_JOB.search(following)
    return workflow[start:start + len(marker) + (end.start() if end else len(following))]


def require(contents: str, expected: str) -> None:
    if expected not in contents:
        raise AssertionError(f"Missing required frontend split marker: {expected}")


def check(workflow: str) -> None:
    producer = job_block(workflow, "frontend-build")
    unit = job_block(workflow, "frontend-unit-parallel")
    browser = job_block(workflow, "frontend-browser")
    required = job_block(workflow, "frontend-test")

    require(producer, "frontend_unit: ${{ needs.changes.outputs.frontend_unit }}")
    require(producer, "frontend_playwright: ${{ needs.changes.outputs.frontend_playwright }}")

    require(unit, "needs: frontend-build")
    require(unit, "if: needs.frontend-build.outputs.frontend_unit == 'true'")
    require(unit, "persist-credentials: false")
    require(unit, "package-manager-cache: false")
    require(unit, "npm@11.17.0 --ignore-scripts --no-audit --no-fund")
    require(unit, "bash scripts/ci/npm-ci-retry.sh frontend")
    require(unit, "COGLATAS_FRONTEND_VITEST_MAX_WORKERS: ${{ needs.frontend-build.outputs.frontend_playwright == 'true' && '2' || '' }}")
    require(unit, "run: npm --prefix frontend test")

    require(browser, "name: frontend-browser")
    if re.search(r"(?m)^    if:", browser):
        raise AssertionError("Browser prerequisite cannot use job-level condition")
    require(browser, "needs: frontend-build")
    require(browser, 'FRONTEND_UNIT: "false"')
    require(browser, "FRONTEND_PLAYWRIGHT: ${{ needs.frontend-build.outputs.frontend_playwright }}")
    require(browser, "run: bash scripts/ci/run-frontend-parallel.sh")
    require(browser, "persist-credentials: false")
    require(browser, "name: group-keyboard-observations")
    require(browser, "name: search-drawer-observations")
    require(browser, "name: playwright-ui-test-artifacts")

    require(required, "name: frontend-test")
    require(required, "if: ${{ always() }}")
    require(required, "needs: [frontend-build, frontend-browser, frontend-unit-parallel]")
    require(required, "persist-credentials: false")
    for name, key in (
        ("FRONTEND_PRODUCER_RESULT", "frontend-build.result"),
        ("FRONTEND_BROWSER_RESULT", "frontend-browser.result"),
        ("FRONTEND_UNIT_JOB_RESULT", "frontend-unit-parallel.result"),
        ("FRONTEND_UNIT_SELECTED", "frontend-build.outputs.frontend_unit"),
        ("FRONTEND_PLAYWRIGHT_SELECTED", "frontend-build.outputs.frontend_playwright"),
    ):
        require(required, f"{name}: " + "${{ needs." + key + " }}")
    require(required, "run: bash scripts/ci/verify-frontend-parallel-gate.sh")

    require(workflow, "run: bash scripts/ci/test-frontend-parallel-gate.sh")
    require(workflow, "run: python3 -B scripts/ci/test-frontend-split-workflow.py")


def main() -> None:
    source = WORKFLOW.read_text(encoding="utf-8")
    check(source)
    mutations = [
        ("needs: [frontend-build, frontend-browser, frontend-unit-parallel]",
         "needs: [frontend-build, frontend-browser]"),
        ("FRONTEND_UNIT_JOB_RESULT: ${{ needs.frontend-unit-parallel.result }}",
         "FRONTEND_UNIT_JOB_RESULT: success"),
        ("run: bash scripts/ci/verify-frontend-parallel-gate.sh", "run: echo pass"),
        ("if: needs.frontend-build.outputs.frontend_unit == 'true'",
         "if: needs.frontend-build.outputs.frontend_playwright == 'true'"),
        ('FRONTEND_UNIT: "false"',
         "FRONTEND_UNIT: ${{ needs.frontend-build.outputs.frontend_unit }}"),
        ("COGLATAS_FRONTEND_VITEST_MAX_WORKERS: ${{ needs.frontend-build.outputs.frontend_playwright == 'true' && '2' || '' }}",
         'COGLATAS_FRONTEND_VITEST_MAX_WORKERS: "3"'),
        ("run: bash scripts/ci/npm-ci-retry.sh frontend", "run: npm ci --prefix frontend"),
        ("name: group-keyboard-observations", "name: unvalidated-group-capture"),
    ]
    for before, after in mutations:
        if source.count(before) < 1:
            raise AssertionError(f"Mutation anchor missing: {before}")
        # Mutate every matching step; producer and unit installs can share text.
        mutated = source.replace(before, after)
        try:
            check(mutated)
        except AssertionError:
            continue
        raise AssertionError(f"Non-fail-closed workflow mutation was accepted: {before}")
    print(f"Frontend split workflow contract passed with {len(mutations)} negative controls.")


if __name__ == "__main__":
    main()
