"""Record exact selection, served bytes, and attempts for the #1959 diagnostic."""

import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path
from urllib.parse import quote
from urllib.request import urlopen

PROJECTIDE_SPEC = re.compile(r"tests/ui/projectide-[a-z0-9-]+\.spec\.ts")
ADDITIONAL_SPECS = (
    "compiler-outputs", "compiler-recordings", "dependency-analysis",
    "diagnostic-basis", "diagnostic-premises", "local-tests",
    "operator-recordings", "pass-boundary", "pass-recordings",
    "pass-source-navigation", "plan-definition", "predicate-recordings",
    "recorded-debug-admission", "recorded-debug", "regional-operator-recordings",
    "review-admission", "root-source", "scenario-schedule", "scenarios",
    "scoped-publication", "scoped-sources", "semantic-index",
    "simulation-comparison", "simulation-trace-profile", "simulation", "test-definitions",
)
PLAYWRIGHT = ["node", "node_modules/@playwright/test/cli.js", "test"]


def run(*args):
    return subprocess.check_output(args)


def write_json(path, value):
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def inventory(report):
    def visit(suite, parents):
        for spec in suite.get("specs", []):
            for test in spec["tests"]:
                yield {
                    "id": spec["id"],
                    "project": test["projectName"],
                    "file": spec["file"],
                    "line": spec["line"],
                    "title": " › ".join((*parents, spec["title"])),
                    "expected": test["expectedStatus"],
                    "status": test["status"],
                    "attempts": [{
                        "retry": attempt["retry"],
                        "status": attempt["status"],
                        "duration_ms": attempt["duration"],
                    } for attempt in test["results"]],
                }
        for child in suite.get("suites", []):
            yield from visit(child, (*parents, child["title"]))

    rows = {}
    for suite in report["suites"]:
        for row in visit(suite, ()):
            if row["id"] in rows:
                raise ValueError(f"Duplicate Playwright identity: {row['id']}")
            rows[row["id"]] = row
    return rows


def list_tests(path, specs, *options):
    path.write_bytes(run(*PLAYWRIGHT, "--list", "--reporter=json", *options, *specs))
    report = json.loads(path.read_text(encoding="utf-8"))
    if report["errors"]:
        raise ValueError(f"Playwright discovery errors in {path.name}")
    return inventory(report)


def select(out):
    head = run("git", "rev-parse", "HEAD").decode().strip()
    if head != os.environ["SOURCE_SHA"]:
        raise ValueError(f"Unexpected source SHA: {head}")
    source_manifest = run("git", "ls-tree", "-r", head, "--", "frontend", "tests/ui",
                          "package.json", "package-lock.json", "playwright.config.ts")
    (out / "source-git-tree.txt").write_bytes(source_manifest)
    spec_paths = run("git", "ls-files", "tests/ui/projectide-*.spec.ts").decode().splitlines()
    if not all(PROJECTIDE_SPEC.fullmatch(path) for path in spec_paths):
        raise ValueError("Unrecognized ProjectIDE spec path")
    runner = Path("tests/ui/run-angular-playwright.mjs").read_text(encoding="utf-8")
    selector = re.search(r"const staticSuiteFiles = \[(.*?)\];", runner, re.S)
    if selector is None:
        raise ValueError("Shared Playwright selector could not be read")
    shared_paths = sorted(set(re.findall(r"'(tests/ui/projectide-[^']+\.spec\.ts)'",
                                        selector.group(1))))
    additional = sorted(f"tests/ui/projectide-{name}.spec.ts" for name in ADDITIONAL_SPECS)
    if shared_paths != ["tests/ui/projectide-workbench.spec.ts"]:
        raise ValueError(f"Unexpected shared ProjectIDE selection: {shared_paths}")
    if set(spec_paths) != set(additional).union(shared_paths) or set(additional).intersection(shared_paths):
        raise ValueError("Explicit diagnostic spec selection does not partition canonical source")
    (out / "additional-specs.txt").write_text("\n".join(additional) + "\n", encoding="utf-8")
    full = list_tests(out / "canonical-list.raw.json", spec_paths)
    shared = list_tests(out / "shared-list.raw.json", shared_paths)
    shard = list_tests(out / "shard-list.raw.json", additional,
                       f"--project={os.environ['BROWSER_PROJECT']}", f"--shard={os.environ['BROWSER_SHARD']}")
    if len(full) != 984 or len(shared) != 84 or len(shard) != 225:
        raise ValueError(f"Unexpected canonical/shared/shard counts: {len(full)}/{len(shared)}/{len(shard)}")
    if not shard.keys() <= full.keys() - shared.keys():
        raise ValueError("Shard contains cases outside the additional ProjectIDE roster")
    write_json(out / "canonical-identities.json", list(full.values()))
    write_json(out / "shard-identities.json", list(shard.values()))
    write_json(out / "source.json", {
        "source_sha": head,
        "tree_sha": run("git", "rev-parse", "HEAD^{tree}").decode().strip(),
        "workflow_sha": os.environ.get("GITHUB_SHA"),
        "canonical_specs": spec_paths,
        "shared_specs": shared_paths,
        "canonical_ids": len(full),
        "shared_ids": len(shared),
        "additional_ids": len(full) - len(shared),
        "project": os.environ["BROWSER_PROJECT"],
        "shard": os.environ["BROWSER_SHARD"],
        "shard_ids": len(shard),
        "playwright": run(*PLAYWRIGHT[:2], "--version").decode().strip(),
    })


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def serve(out):
    root = Path("frontend/dist/coglatas-web")
    fixture_root = "frontend/public/assets/projectide"
    source_fixtures = run("git", "ls-files", fixture_root + "/*.json").decode().splitlines()
    if not source_fixtures or not (root / "index.html").is_file():
        raise ValueError("The build or its fixture inputs are absent")
    fixtures = {f"assets/projectide/{Path(path).name}": path for path in source_fixtures}
    paths = {"index.html", *fixtures}
    for path in root.iterdir():
        if path.is_file() and path.suffix in {".js", ".css"}:
            paths.add(path.relative_to(root).as_posix())
    if not any(path.endswith(".js") for path in paths):
        raise ValueError("The built JavaScript is absent")
    rows = []
    for path in sorted(paths):
        built = (root / path).read_bytes()
        url_path = "" if path == "index.html" else quote(path)
        with urlopen(f"{os.environ['PLAYWRIGHT_BASE_URL']}/app/{url_path}", timeout=10) as response:
            if response.status != 200:
                raise ValueError(f"Unexpected HTTP status for {path}: {response.status}")
            served_digest = sha256(response.read())
        digest = sha256(built)
        if served_digest != digest:
            raise ValueError(f"Served bytes differ from build: {path}")
        row = {"path": path, "bytes": len(built), "build_sha256": digest, "served_sha256": served_digest}
        if path in fixtures:
            blob = run("git", "rev-parse", f"HEAD:{fixtures[path]}").decode().strip()
            if sha256(run("git", "cat-file", "blob", blob)) != digest:
                raise ValueError(f"Served fixture differs from its Git blob: {path}")
            row["source_blob"] = blob
        rows.append(row)
    write_json(out / "served-assets.json", rows)


def report(out):
    expected = {row["id"]: row for row in json.loads((out / "shard-identities.json").read_text())}
    raw = json.loads((out / "browser.json").read_text(encoding="utf-8"))
    actual = inventory(raw)
    if not actual.keys() <= expected.keys():
        raise ValueError("Execution contains an unexpected test ID")
    rows = []
    for test_id, selected in expected.items():
        row = actual.get(test_id, selected)
        statuses = [attempt["status"] for attempt in row["attempts"]]
        if not statuses:
            outcome = "SKIP" if test_id in actual and row["expected"] == "skipped" else "UNEXECUTED"
        elif all(status == "skipped" for status in statuses):
            outcome = "SKIP"
        elif statuses == ["passed"]:
            outcome = "PASS"
        elif statuses[-1] == "passed":
            outcome = "RETRY_PASS"
        elif statuses[-1] == "interrupted":
            outcome = "INTERRUPTED"
        else:
            outcome = "FAIL"
        rows.append({**row, "outcome": outcome})
    outcomes = ("PASS", "FAIL", "SKIP", "RETRY_PASS", "INTERRUPTED", "UNEXECUTED")
    counts = {key: sum(row["outcome"] == key for row in rows) for key in outcomes}
    receipt = {
        "counts": counts,
        "duration_ms": raw["stats"]["duration"],
        "retry_attempts": sum(attempt["retry"] > 0 for row in rows for attempt in row["attempts"]),
        "runner_errors": raw["errors"],
        "junit_sha256": sha256((out / "browser.xml").read_bytes()),
        "json_sha256": sha256((out / "browser.json").read_bytes()),
        "cases": rows,
    }
    write_json(out / "receipt.json", receipt)
    print(json.dumps({**counts, "retry_attempts": receipt["retry_attempts"],
                      "duration_ms": receipt["duration_ms"]}))
    if raw["errors"] or any(counts[key] for key in ("FAIL", "INTERRUPTED", "UNEXECUTED")):
        sys.exit(1)


if __name__ == "__main__":
    mode, directory = sys.argv[1:]
    {"select": select, "serve": serve, "report": report}[mode](Path(directory))
