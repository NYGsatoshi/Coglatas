"""Select and reconcile the supplemental ProjectIDE browser pilot for #1959."""

import hashlib
import json
import os
import re
import subprocess
import sys
import xml.etree.ElementTree as ET
from collections import Counter
from pathlib import Path
from urllib.parse import quote
from urllib.request import urlopen

PROJECTIDE_SPEC = re.compile(r"tests/ui/projectide-[a-z0-9-]+\.spec\.ts")
PROJECTS = ("chromium-desktop", "chromium-mobile")
SHARD_NAMES = {f"pilot-shard-{project}-{number}" for project in PROJECTS for number in (1, 2)}
IDENTITY_FIELDS = ("id", "project", "file", "line", "title")
PASS_OUTCOMES = {"PASS", "RETRY_PASS"}
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


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def shared_projectide_specs(runner, tracked):
    selector = re.search(r"\bconst\s+staticSuiteFiles\s*=\s*\[(.*?)\];", runner, re.S)
    if selector is None:
        raise ValueError("The shared Playwright runner no longer exposes its static spec list")
    entries = []
    for item in selector.group(1).split(","):
        if not item.strip():
            continue
        match = re.fullmatch(r"""\s*(['"])(tests/ui/[\w./-]+\.spec\.ts)\1\s*""", item)
        if match is None:
            raise ValueError(f"Unsupported shared Playwright selector entry: {item.strip()}")
        entries.append(match[2])
    if not entries or len(entries) != len(set(entries)):
        raise ValueError("The shared Playwright selector has no entries or repeats a path")
    shared = {path for path in entries if PROJECTIDE_SPEC.fullmatch(path)}
    if missing := shared - set(tracked):
        raise ValueError(f"Shared ProjectIDE specs are missing from the source: {sorted(missing)}")
    return sorted(shared)


def spec_arguments(paths):
    return [f"(?:^|/){re.escape(path)}$" for path in paths]


def list_tests(path, specs, *options):
    if not specs:
        write_json(path, {"suites": [], "errors": []})
        return {}
    process = subprocess.run(
        [*PLAYWRIGHT, "--list", "--reporter=json", *options, *spec_arguments(specs)],
        stdout=subprocess.PIPE, check=False,
    )
    path.write_bytes(process.stdout)
    if process.returncode:
        raise ValueError(f"Playwright discovery exited {process.returncode}; see {path.name}")
    report = read_json(path)
    if report["errors"]:
        raise ValueError(f"Playwright discovery errors in {path.name}")
    return inventory(report)


def select(out):
    source = run("git", "rev-parse", "HEAD").decode().strip()
    if source != os.environ["GITHUB_SHA"]:
        raise ValueError(f"Unexpected test-merge SHA: {source}")
    source_manifest = run("git", "ls-tree", "-r", source, "--", "frontend", "tests/ui",
                          "package.json", "package-lock.json", ".npmrc", ".gitattributes",
                          "playwright.config.ts", "scripts/ci/npm-ci-retry.sh",
                          "scripts/ci/verify-npm-lockfile.mjs")
    (out / "source-git-tree.txt").write_bytes(source_manifest)
    spec_paths = run("git", "ls-files", "tests/ui/projectide-*.spec.ts").decode().splitlines()
    if not all(PROJECTIDE_SPEC.fullmatch(path) for path in spec_paths):
        raise ValueError("Unrecognized ProjectIDE spec path")
    shared_paths = shared_projectide_specs(
        Path("tests/ui/run-angular-playwright.mjs").read_text(encoding="utf-8"), spec_paths,
    )
    additional = sorted(set(spec_paths) - set(shared_paths))
    if not additional:
        raise ValueError("No supplemental ProjectIDE specs were selected")
    (out / "spec-arguments.txt").write_text(
        "\n".join(spec_arguments(additional)) + "\n", encoding="utf-8",
    )
    full = list_tests(out / "canonical-list.raw.json", spec_paths)
    shared = list_tests(out / "shared-list.raw.json", shared_paths)
    project = os.environ["BROWSER_PROJECT"]
    division = os.environ["BROWSER_SHARD"]
    shard = list_tests(out / "shard-list.raw.json", additional, f"--project={project}", f"--shard={division}")
    if {f"tests/ui/{row['file']}" for row in full.values()} != set(spec_paths):
        raise ValueError("Canonical discovery omitted a tracked ProjectIDE spec")
    if not shared.keys() <= full.keys():
        raise ValueError("Shared discovery is outside the canonical roster")
    if not shard or not shard.keys() <= full.keys() - shared.keys():
        raise ValueError("Shard is empty or contains cases outside the supplemental roster")
    if {row["project"] for row in shard.values()} != {project}:
        raise ValueError(f"Shard contains cases from outside the {project} project")
    write_json(out / "canonical-identities.json", list(full.values()))
    write_json(out / "shard-identities.json", list(shard.values()))
    write_json(out / "source.json", {
        "source_sha": source,
        "tree_sha": run("git", "rev-parse", "HEAD^{tree}").decode().strip(),
        "pr_head": os.environ["PR_HEAD"],
        "pr_base": os.environ["PR_BASE"],
        "canonical_specs": spec_paths,
        "shared_specs": shared_paths,
        "supplemental_specs": additional,
        "canonical_ids": len(full),
        "shared_ids": sorted(shared),
        "project": project,
        "shard": division,
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


def by_id(rows):
    tests = {row["id"]: row for row in rows}
    if len(tests) != len(rows):
        raise ValueError("Evidence repeats a Playwright test ID")
    return tests


def identities(row):
    return tuple(row[field] for field in IDENTITY_FIELDS)


def attempts_for_shard(expected, actual):
    if extra := actual.keys() - expected.keys():
        raise ValueError(f"Execution contains unexpected IDs: {sorted(extra)[:5]}")
    rows = []
    for test_id, selected in expected.items():
        row = actual.get(test_id, selected)
        if identities(row) != identities(selected):
            raise ValueError(f"Execution identity differs from discovery: {test_id}")
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
    return rows


def verify_partition(canonical, shared, shards):
    if not shared <= canonical:
        raise ValueError("Shared IDs are outside the canonical roster")
    remaining = canonical - shared
    covered = set()
    for shard in shards:
        if not shard or shard & covered:
            raise ValueError("Empty shard or duplicate IDs across shards")
        if shard - remaining:
            raise ValueError("Shard contains IDs outside the supplemental roster")
        covered |= shard
    if missing := remaining - covered:
        raise ValueError(f"Missing {len(missing)} supplemental IDs, including {sorted(missing)[:5]}")


def verify_case_coverage(rows):
    unresolved = Counter(row["outcome"] for row in rows if row["outcome"] not in PASS_OUTCOMES)
    if unresolved:
        raise ValueError(f"Test coverage has non-passing outcomes: {dict(unresolved)}")


def verify_junit_coverage(xml, selected):
    if (xml.tag != "testsuites" or xml.get("tests") != str(len(selected))
            or any(xml.get(key) != "0" for key in ("errors", "failures", "skipped"))):
        raise ValueError("JUnit shows incomplete or non-passing test execution")

    # Playwright's JUnit report omits raw IDs and line numbers.
    expected = {(row["project"], row["file"], row["title"]) for row in selected.values()}
    if len(expected) != len(selected):
        raise ValueError("Selected tests do not have distinct JUnit identities")

    actual = []
    for suite in xml.findall("testsuite"):
        for case in suite.findall("testcase"):
            if case.get("classname") != suite.get("name"):
                raise ValueError("JUnit testcase class differs from its suite file")
            if any(case.find(tag) is not None for tag in ("failure", "error", "skipped")):
                raise ValueError("JUnit contains a non-passing testcase outcome")
            actual.append((suite.get("hostname"), case.get("classname"), case.get("name")))
    if (len(actual) != len(expected) or set(actual) != expected
            or len(xml.findall(".//testcase")) != len(actual)):
        raise ValueError("JUnit testcase identities differ from the selected tests")


def report(out):
    expected = by_id(read_json(out / "shard-identities.json"))
    raw = read_json(out / "browser.json")
    rows = attempts_for_shard(expected, inventory(raw))
    counts = Counter(row["outcome"] for row in rows)
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
    if raw["errors"]:
        raise ValueError("Playwright reported runner errors")
    verify_case_coverage(rows)


def reconcile(root):
    artifacts = {path.name: path for path in root.iterdir() if path.is_dir()}
    if artifacts.keys() != SHARD_NAMES:
        raise ValueError(f"Unexpected shard artifacts: {sorted(artifacts.keys() ^ SHARD_NAMES)}")
    canonical = None
    source_identity = None
    asset_digest = None
    manifest_digest = None
    shared = set()
    shard_ids = []
    rows = []
    shard_evidence = {}
    for name, artifact in sorted(artifacts.items()):
        source = read_json(artifact / "source.json")
        identity = {key: value for key, value in source.items() if key not in {"project", "shard"}}
        if source["source_sha"] != os.environ["GITHUB_SHA"]:
            raise ValueError(f"Unexpected test merge for {name}")
        project = source["project"]
        division = source["shard"]
        if name != f"pilot-shard-{project}-{division[0]}" or division not in {"1/2", "2/2"}:
            raise ValueError(f"Shard artifact name does not match its source receipt: {name}")
        if source_identity is None:
            source_identity = identity
            canonical = by_id(read_json(artifact / "canonical-identities.json"))
            shared = set(source["shared_ids"])
            asset_digest = sha256((artifact / "served-assets.json").read_bytes())
            manifest_digest = sha256((artifact / "source-git-tree.txt").read_bytes())
        if identity != source_identity or canonical != by_id(read_json(artifact / "canonical-identities.json")):
            raise ValueError(f"Canonical source or original test identities disagree: {name}")
        if asset_digest != sha256((artifact / "served-assets.json").read_bytes()):
            raise ValueError(f"Served asset or fixture identity differs: {name}")
        if manifest_digest != sha256((artifact / "source-git-tree.txt").read_bytes()):
            raise ValueError(f"Git input manifest differs: {name}")
        selected = by_id(read_json(artifact / "shard-identities.json"))
        shard_ids.append(set(selected))
        if any(test_id not in canonical or identities(row) != identities(canonical[test_id])
               or row["project"] != project for test_id, row in selected.items()):
            raise ValueError(f"Shard IDs or project disagree with the canonical selection: {name}")
        receipt = read_json(artifact / "receipt.json")
        reports = {kind: (artifact / file).read_bytes()
                   for kind, file in (("json", "browser.json"), ("junit", "browser.xml"))}
        if any(sha256(data) != receipt[f"{kind}_sha256"] for kind, data in reports.items()):
            raise ValueError(f"Raw report digest differs: {name}")
        raw = json.loads(reports["json"])
        actual = attempts_for_shard(selected, inventory(raw))
        if receipt["cases"] != actual or raw["errors"]:
            raise ValueError(f"Raw Playwright report differs from the receipt or has runner errors: {name}")
        verify_junit_coverage(ET.fromstring(reports["junit"]), selected)
        rows.extend(actual)
        shard_evidence[name] = {kind: sha256(data) for kind, data in reports.items()}
    verify_partition(set(canonical), shared, shard_ids)
    verify_case_coverage(rows)
    projects = Counter(row["project"] for row in rows)
    locales = {project: Counter(locale for row in rows if row["project"] == project
                                for locale in set(re.findall(r"\b(en|ja)\b", row["title"])))
               for project in PROJECTS}
    if set(projects) != set(PROJECTS) or any(set(coverage) != {"en", "ja"} for coverage in locales.values()):
        raise ValueError("Both Chromium projects must cover English and Japanese")
    result = {
        "source": source_identity,
        "runner_minutes_cap": 88,
        "selected_ids": len(rows),
        "outcomes": Counter(row["outcome"] for row in rows),
        "projects": projects,
        "locale_labels": locales,
        "served_assets_sha256": asset_digest,
        "source_manifest_sha256": manifest_digest,
        "shard_reports_sha256": shard_evidence,
    }
    write_json(root / "pilot-receipt.json", result)
    print(json.dumps({key: result[key] for key in ("selected_ids", "outcomes", "projects", "locale_labels")}))


if __name__ == "__main__":
    mode, directory = sys.argv[1:]
    {"select": select, "serve": serve, "report": report, "reconcile": reconcile}[mode](Path(directory))
