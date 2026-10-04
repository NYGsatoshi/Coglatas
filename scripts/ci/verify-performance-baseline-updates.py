#!/usr/bin/env python3
"""Fail-closed governance for PERF-03 baseline and budget changes."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import subprocess
import sys
from pathlib import Path
from typing import Any

SHA_RE = re.compile(r"^[0-9a-f]{40}$")
HASH_RE = re.compile(r"^[0-9a-f]{64}$")
INITIAL_BASELINE = "initial-baseline"
DB_BASELINE_PATH_RE = re.compile(r"^performance/baselines/db/(small|medium)/([a-z][a-z0-9.-]*)\.json$")
ALLOWED_CAUSES = {
    "fixture-change",
    "environment-change",
    "accepted-product-change",
    "measurement-correction",
    "contract-recalibration",
}
REQUIRED_BASELINE_FIELDS = {
    "oldBaselineSha", "newBaselineSha", "scenarioId", "metric", "reason",
    "cause", "beforeEvidence", "afterEvidence", "budgetChanged",
}
REQUIRED_RELAXATION_FIELDS = {
    "budgetId", "scenarioId", "metric", "reason", "cause",
    "beforeEvidence", "afterEvidence", "oldValue", "newValue",
}


class BaselineUpdateError(ValueError):
    pass


def fail(message: str) -> None:
    raise BaselineUpdateError(message)


def _load_json_text(text: str, label: str) -> dict[str, Any]:
    try:
        value = json.loads(text)
    except json.JSONDecodeError as exc:
        fail(f"invalid JSON in {label}: {exc}")
    if not isinstance(value, dict):
        fail(f"{label} must contain a JSON object")
    return value


def load_json(path: Path) -> dict[str, Any]:
    try:
        return _load_json_text(path.read_text(encoding="utf-8-sig"), str(path))
    except (OSError, UnicodeError) as exc:
        fail(f"cannot read {path}: {exc}")


def require_sha(value: Any, field: str) -> str:
    if not isinstance(value, str) or not SHA_RE.fullmatch(value):
        fail(f"{field} must be a full lowercase 40-character SHA")
    return value


def require_text(value: Any, field: str, *, min_length: int = 1) -> str:
    if not isinstance(value, str) or len(value.strip()) < min_length:
        fail(f"{field} must contain at least {min_length} non-whitespace characters")
    return value.strip()


def require_evidence(value: Any, field: str) -> list[str]:
    if not isinstance(value, list) or not value:
        fail(f"{field} must be a non-empty evidence array")
    result: list[str] = []
    for index, item in enumerate(value):
        result.append(require_text(item, f"{field}[{index}]"))
    return result


def samples_sha256(samples: list[int | float]) -> str:
    """Bind the complete, ordered samples without rounding or filtering."""
    return hashlib.sha256(json.dumps(samples, separators=(",", ":"), allow_nan=False).encode("utf-8")).hexdigest()


def _initial_baseline_provenance(record: dict[str, Any], label: str) -> dict[str, Any]:
    if "oldBaselineSha" not in record or record["oldBaselineSha"] is not None:
        fail(f"{label} initial baseline requires explicit oldBaselineSha: null")
    if record.get("budgetChanged") is not False:
        fail(f"{label} initial baseline cannot change budgets")
    path = record.get("baselinePath")
    match = DB_BASELINE_PATH_RE.fullmatch(path) if isinstance(path, str) else None
    if match is None or match[2] != record.get("scenarioId") or record.get("metric") != "db.total_time_ms":
        fail(f"{label} initial baseline requires a canonical DB scenario/profile path")
    provenance = record.get("provenance")
    if not isinstance(provenance, dict):
        fail(f"{label}.provenance must be an object")
    if provenance.get("sourceRef") != "refs/heads/main" or provenance.get("headSha") != record.get("newBaselineSha"):
        fail(f"{label}.provenance must bind the exact approved main SHA")
    if provenance.get("workflowPath") != ".github/workflows/performance-db-baseline-capture.yml":
        fail(f"{label}.provenance must identify the DB baseline capture workflow")
    for field in ("workflowRunId", "workflowRunAttempt", "artifactId", "fixtureVersion", "sampleCount"):
        if type(provenance.get(field)) is not int or provenance[field] < 1:
            fail(f"{label}.provenance.{field} must be a positive integer")
    if provenance.get("profile") != match[1] or provenance.get("artifactName") != f"perf05-{match[1]}":
        fail(f"{label}.provenance profile/artifact identity mismatch")
    for field in ("environmentCompatibilityKey", "fixtureHash", "samplesSha256"):
        if not isinstance(provenance.get(field), str) or not HASH_RE.fullmatch(provenance[field]):
            fail(f"{label}.provenance.{field} must be a lowercase SHA-256 digest")
    digest = provenance.get("artifactDigest")
    if not isinstance(digest, str) or not re.fullmatch(r"sha256:[0-9a-f]{64}", digest):
        fail(f"{label}.provenance.artifactDigest must be the GitHub artifact SHA-256 digest")
    artifact_url = require_text(provenance.get("artifactUrl"), f"{label}.provenance.artifactUrl")
    suffix = f"/actions/runs/{provenance['workflowRunId']}/artifacts/{provenance['artifactId']}"
    if not re.fullmatch(r"https://github\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+" + re.escape(suffix), artifact_url):
        fail(f"{label}.provenance artifact URL/run/id mismatch")
    if type(provenance.get("pageSize")) is not int or provenance["pageSize"] not in (0, 5):
        fail(f"{label}.provenance must select the canonical page-5/unpaged duration stream")
    require_text(provenance.get("samplesEvidence"), f"{label}.provenance.samplesEvidence")
    return provenance


def _budget_map(document: dict[str, Any]) -> dict[str, dict[str, Any]]:
    if document.get("schemaVersion") != 1:
        fail("budgets.schemaVersion must be 1")
    budgets = document.get("budgets")
    if not isinstance(budgets, list):
        fail("budgets.budgets must be an array")
    result: dict[str, dict[str, Any]] = {}
    for index, item in enumerate(budgets):
        if not isinstance(item, dict):
            fail(f"budget[{index}] must be an object")
        budget_id = require_text(item.get("id"), f"budget[{index}].id")
        if budget_id in result:
            fail(f"duplicate budget id: {budget_id}")
        result[budget_id] = item
    return result


def validate_ledger(document: dict[str, Any]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    if document.get("schemaVersion") != 1:
        fail("baseline-updates.schemaVersion must be 1")
    policy = document.get("policy")
    if not isinstance(policy, dict):
        fail("baseline-updates.policy is required")
    if policy.get("approvedSourceRef") != "refs/heads/main":
        fail("baseline update policy must bind approved baselines to refs/heads/main")
    if policy.get("regressionOnlyCauseForbidden") is not True:
        fail("baseline update policy must forbid regression-only baseline movement")
    causes = policy.get("allowedCauses")
    if not isinstance(causes, list) or set(causes) != ALLOWED_CAUSES:
        fail("baseline update allowed causes do not match the PERF-03 policy")
    required_baseline = policy.get("requiredBaselineFields")
    required_relaxation = policy.get("requiredBudgetRelaxationFields")
    if not isinstance(required_baseline, list) or set(required_baseline) != REQUIRED_BASELINE_FIELDS:
        fail("baseline update required field policy does not match PERF-03")
    if not isinstance(required_relaxation, list) or set(required_relaxation) != REQUIRED_RELAXATION_FIELDS:
        fail("budget relaxation required field policy does not match PERF-03")

    updates = document.get("baselineUpdates")
    relaxations = document.get("budgetRelaxations")
    if not isinstance(updates, list) or not isinstance(relaxations, list):
        fail("baselineUpdates and budgetRelaxations must be arrays")

    seen_updates: set[tuple[Any, ...]] = set()
    for index, record in enumerate(updates):
        if not isinstance(record, dict):
            fail(f"baselineUpdates[{index}] must be an object")
        initial = record.get("changeType") == INITIAL_BASELINE
        old_sha = None if initial else require_sha(record.get("oldBaselineSha"), f"baselineUpdates[{index}].oldBaselineSha")
        new_sha = require_sha(record.get("newBaselineSha"), f"baselineUpdates[{index}].newBaselineSha")
        if old_sha == new_sha:
            fail(f"baselineUpdates[{index}] old/new SHA must differ")
        scenario = require_text(record.get("scenarioId"), f"baselineUpdates[{index}].scenarioId")
        metric = require_text(record.get("metric"), f"baselineUpdates[{index}].metric")
        require_text(record.get("reason"), f"baselineUpdates[{index}].reason", min_length=20)
        cause = record.get("cause")
        if cause not in ALLOWED_CAUSES:
            fail(f"baselineUpdates[{index}].cause is not allowed: {cause!r}")
        require_evidence(record.get("beforeEvidence"), f"baselineUpdates[{index}].beforeEvidence")
        require_evidence(record.get("afterEvidence"), f"baselineUpdates[{index}].afterEvidence")
        if not isinstance(record.get("budgetChanged"), bool):
            fail(f"baselineUpdates[{index}].budgetChanged must be boolean")
        if initial:
            _initial_baseline_provenance(record, f"baselineUpdates[{index}]")
        key = (scenario, metric, old_sha, new_sha, record["baselinePath"] if initial else None)
        if key in seen_updates:
            fail(f"duplicate baseline update record: {scenario}/{metric} {old_sha}->{new_sha}")
        seen_updates.add(key)

    seen_relaxations: set[str] = set()
    for index, record in enumerate(relaxations):
        if not isinstance(record, dict):
            fail(f"budgetRelaxations[{index}] must be an object")
        budget_id = require_text(record.get("budgetId"), f"budgetRelaxations[{index}].budgetId")
        require_text(record.get("scenarioId"), f"budgetRelaxations[{index}].scenarioId")
        require_text(record.get("metric"), f"budgetRelaxations[{index}].metric")
        require_text(record.get("reason"), f"budgetRelaxations[{index}].reason", min_length=20)
        cause = record.get("cause")
        if cause not in ALLOWED_CAUSES:
            fail(f"budgetRelaxations[{index}].cause is not allowed: {cause!r}")
        require_evidence(record.get("beforeEvidence"), f"budgetRelaxations[{index}].beforeEvidence")
        require_evidence(record.get("afterEvidence"), f"budgetRelaxations[{index}].afterEvidence")
        for field in ("oldValue", "newValue"):
            value = record.get(field)
            if not isinstance(value, (int, float)) or isinstance(value, bool) or not math.isfinite(float(value)):
                fail(f"budgetRelaxations[{index}].{field} must be finite numeric evidence")
        if float(record["newValue"]) <= float(record["oldValue"]):
            fail(f"budgetRelaxations[{index}] must describe an actual relaxation (newValue > oldValue)")
        if budget_id in seen_relaxations:
            fail(f"duplicate budget relaxation record: {budget_id}")
        seen_relaxations.add(budget_id)
    return updates, relaxations


def _baseline_sha(budget: dict[str, Any], budget_id: str) -> str:
    baseline = budget.get("baseline")
    if not isinstance(baseline, dict):
        fail(f"{budget_id}.baseline must remain an object")
    return require_sha(baseline.get("sha"), f"{budget_id}.baseline.sha")


def _threshold(budget: dict[str, Any], budget_id: str) -> tuple[str, float]:
    gate = budget.get("gate")
    if gate == "hard-ceiling":
        limit = budget.get("limit")
        if not isinstance(limit, dict):
            fail(f"{budget_id}.limit missing")
        value = limit.get("value")
        field = "hard-ceiling"
    elif gate == "relative-regression":
        comparison = budget.get("comparison")
        if not isinstance(comparison, dict):
            fail(f"{budget_id}.comparison missing")
        value = comparison.get("maxIncreasePercent")
        field = "relative-regression"
    else:
        fail(f"{budget_id}.gate must be blocking")
    if not isinstance(value, (int, float)) or isinstance(value, bool) or not math.isfinite(float(value)):
        fail(f"{budget_id} threshold must be finite numeric")
    return field, float(value)


def validate_transition(
    old_budgets_document: dict[str, Any],
    new_budgets_document: dict[str, Any],
    ledger_document: dict[str, Any],
    *,
    head_sha: str | None = None,
) -> dict[str, int]:
    updates, relaxations = validate_ledger(ledger_document)
    old = _budget_map(old_budgets_document)
    new = _budget_map(new_budgets_document)
    update_records = {
        (item["scenarioId"], item["metric"], item["oldBaselineSha"], item["newBaselineSha"]): item
        for item in updates
    }
    relaxation_records = {item["budgetId"]: item for item in relaxations}
    head = require_sha(head_sha, "headSha") if head_sha is not None else None

    baseline_changes = 0
    threshold_relaxations = 0

    removed = sorted(old.keys() - new.keys())
    if removed:
        fail(f"blocking budget removal is not allowed: {', '.join(removed)}")

    # A newly introduced blocking budget is still forbidden from making the
    # candidate commit its own baseline. Existing-budget-only checks would miss
    # exactly that injection path.
    if head is not None:
        for budget_id, budget in new.items():
            if _baseline_sha(budget, budget_id) == head:
                fail(f"{budget_id} baseline cannot use the PR head SHA")

    for budget_id in sorted(old.keys() & new.keys()):
        before = old[budget_id]
        after = new[budget_id]
        before_sha = _baseline_sha(before, budget_id)
        after_sha = _baseline_sha(after, budget_id)
        scenario = require_text(after.get("scenarioId"), f"{budget_id}.scenarioId")
        metric = require_text(after.get("metric"), f"{budget_id}.metric")
        if before.get("scenarioId") != scenario or before.get("metric") != metric:
            fail(f"{budget_id} cannot silently change scenario/metric identity")

        if before_sha != after_sha:
            baseline_changes += 1
            if head is not None and after_sha == head:
                fail(f"{budget_id} baseline cannot be changed to the PR head SHA")
            record = update_records.get((scenario, metric, before_sha, after_sha))
            if record is None:
                fail(f"baseline change for {budget_id} requires review metadata in performance/baseline-updates.json")

        before_gate, before_value = _threshold(before, budget_id)
        after_gate, after_value = _threshold(after, budget_id)
        if before_gate != after_gate:
            fail(f"{budget_id} cannot silently switch blocking gate class")
        if after_value > before_value:
            threshold_relaxations += 1
            record = relaxation_records.get(budget_id)
            if record is None:
                fail(f"budget relaxation for {budget_id} requires review metadata in performance/baseline-updates.json")
            if record.get("scenarioId") != scenario or record.get("metric") != metric:
                fail(f"budget relaxation metadata identity mismatch for {budget_id}")
            if float(record["oldValue"]) != before_value or float(record["newValue"]) != after_value:
                fail(f"budget relaxation metadata value mismatch for {budget_id}")

        if before_sha != after_sha:
            record = update_records[(scenario, metric, before_sha, after_sha)]
            if record.get("budgetChanged") is not (after_value != before_value):
                fail(f"baseline update budgetChanged mismatch for {budget_id}")

    return {"baselineChanges": baseline_changes, "budgetRelaxations": threshold_relaxations}


def _git(root: Path, *args: str) -> str:
    completed = subprocess.run(
        ["git", *args], cwd=root, check=False, capture_output=True, text=True
    )
    if completed.returncode != 0:
        fail(f"git {' '.join(args)} failed: {completed.stderr.strip()}")
    return completed.stdout.strip()


def _git_is_ancestor(root: Path, ancestor: str, descendant: str) -> bool:
    completed = subprocess.run(
        ["git", "merge-base", "--is-ancestor", ancestor, descendant],
        cwd=root,
        check=False,
        capture_output=True,
        text=True,
    )
    if completed.returncode == 0:
        return True
    if completed.returncode == 1:
        return False
    fail(f"git merge-base ancestry check failed: {completed.stderr.strip()}")


def validate_main_ancestry(root: Path, budgets_document: dict[str, Any], base_ref: str) -> None:
    base_sha = require_sha(_git(root, "rev-parse", base_ref), "baseRefSha")
    for budget_id, budget in _budget_map(budgets_document).items():
        baseline_sha = _baseline_sha(budget, budget_id)
        if not _git_is_ancestor(root, baseline_sha, base_sha):
            fail(
                f"{budget_id} baseline SHA {baseline_sha} is not an approved commit in base main history {base_sha}"
            )


def validate_initial_baselines(
    root: Path, old_ledger: dict[str, Any], ledger: dict[str, Any], base_ref: str, head_sha: str,
) -> int:
    """Keep introduction records immutable and verify their active documents."""
    updates, _ = validate_ledger(ledger)
    previous, _ = validate_ledger(old_ledger)
    for record in previous:
        if record.get("changeType") == INITIAL_BASELINE and record not in updates:
            fail(f"historical initial baseline record cannot be altered or removed: {record['baselinePath']}")
    recorded_paths = {record["baselinePath"] for record in updates if record.get("changeType") == INITIAL_BASELINE}
    baseline_paths = {
        path.relative_to(root).as_posix()
        for path in (root / "performance/baselines/db").glob("*/*.json")
        if DB_BASELINE_PATH_RE.fullmatch(path.relative_to(root).as_posix())
    }
    unrecorded = sorted(baseline_paths - recorded_paths)
    if unrecorded:
        fail(f"DB baseline documents require introduction review records: {', '.join(unrecorded)}")
    head = require_sha(head_sha, "headSha")
    base = require_sha(_git(root, "rev-parse", base_ref), "baseRefSha")
    count = 0
    for record in updates:
        if record.get("changeType") != INITIAL_BASELINE:
            continue
        historical = record in previous
        path = record["baselinePath"]
        if not historical and _git(root, "ls-tree", "--name-only", base, "--", path):
            fail(f"initial baseline cannot replace an existing base document: {path}")
        source = record["newBaselineSha"]
        if source == head or not _git_is_ancestor(root, source, base):
            fail(f"initial baseline source must be distinct approved main history: {source}")
        document = load_json(root / path)
        provenance = record["provenance"]
        expected = {
            "schemaVersion": 1, "resultSchemaVersion": 1, "scenario": record["scenarioId"],
            "metric": record["metric"], "unit": "ms", "baselineSha": source,
            "sourceRef": "refs/heads/main", "approved": True,
            "environmentCompatibilityKey": provenance["environmentCompatibilityKey"],
            "fixtureHash": provenance["fixtureHash"], "fixtureVersion": provenance["fixtureVersion"],
            "provenance": provenance,
        }
        for field, value in expected.items():
            if document.get(field) != value or (field == "approved" and document[field] is not True):
                fail(f"initial baseline document/provenance mismatch: {path}.{field}")
        environment = load_json(root / "performance/environment.json")
        samples = document.get("samples")
        minimum = environment.get("measurement", {}).get("minimumSamples")
        if (type(minimum) is not int or minimum < 1 or not isinstance(samples, list)
                or len(samples) < minimum or len(samples) != provenance["sampleCount"]
                or any(type(sample) not in (int, float) or not math.isfinite(sample) or sample < 0 for sample in samples)):
            fail(f"initial baseline must preserve all valid repeated samples: {path}")
        if samples_sha256(samples) != provenance["samplesSha256"]:
            fail(f"initial baseline complete sample digest mismatch: {path}")
        if provenance["fixtureVersion"] != environment.get("dbFixtureVersion"):
            fail(f"initial baseline DB fixture version mismatch: {path}")
        count += not historical
    return count


def main() -> int:
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser(description="Validate PERF-03 baseline update and budget-relaxation governance")
    parser.add_argument("--base-ref", help="Git ref used to compare performance/budgets.json on PRs")
    parser.add_argument("--head-sha", help="Candidate head SHA paired with --base-ref")
    args = parser.parse_args()
    try:
        ledger = load_json(root / "performance" / "baseline-updates.json")
        validate_ledger(ledger)
        summary = {"schemaVersion": 1, "baselineChanges": 0, "budgetRelaxations": 0}
        if bool(args.base_ref) != bool(args.head_sha):
            fail("--base-ref and --head-sha must be supplied together")
        if args.base_ref:
            old_text = _git(root, "show", f"{args.base_ref}:performance/budgets.json")
            old_budgets = _load_json_text(old_text, f"{args.base_ref}:performance/budgets.json")
            new_budgets = load_json(root / "performance" / "budgets.json")
            head_sha = require_sha(args.head_sha, "--head-sha")
            validate_main_ancestry(root, new_budgets, args.base_ref)
            summary.update(validate_transition(old_budgets, new_budgets, ledger, head_sha=head_sha))
            old_ledger = _load_json_text(_git(root, "show", f"{args.base_ref}:performance/baseline-updates.json"), "base baseline ledger")
            summary["initialBaselines"] = validate_initial_baselines(root, old_ledger, ledger, args.base_ref, head_sha)
        else:
            current_sha = require_sha(_git(root, "rev-parse", "HEAD"), "currentHeadSha")
            summary["initialBaselines"] = validate_initial_baselines(root, ledger, ledger, current_sha, current_sha)
        print("PERF-03 baseline governance valid: " + json.dumps(summary, sort_keys=True, separators=(",", ":")))
        return 0
    except BaselineUpdateError as exc:
        print(f"PERF-03 baseline governance invalid: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
