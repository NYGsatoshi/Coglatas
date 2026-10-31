#!/usr/bin/env python3
"""Regression controls for the credentialed SonarCloud push/PR routing boundary."""

from __future__ import annotations

import ast
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



def python_unit_body(tree):
    imports = [alias.name for node in tree.body if isinstance(node, ast.Import) for alias in node.names]
    imports += [node.module or "" for node in tree.body if isinstance(node, ast.ImportFrom)]
    framework = any(name in {"unittest", "pytest"} or name.startswith("unittest.") for name in imports)
    methods = any(isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)) and node.name.startswith("test_") for node in ast.walk(tree))
    return framework and methods


def python_import_stems(tree):
    modules = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            modules.update(alias.name.split(".")[-1] for alias in node.names)
        elif isinstance(node, ast.ImportFrom):
            modules.add((node.module or "").split(".")[-1])
            modules.update(alias.name.split(".")[-1] for alias in node.names)
    return modules



class SonarCloudRoutingTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.script = routing_shell()

    def invoke(self, prs: list[dict] | str, *, branch: str = BRANCH,
               sha: str = SHA, actor: str = OWNER,
               repository: str = REPOSITORY, project_key: str = "test_project",
               organization_key: str = "test-org") -> tuple[subprocess.CompletedProcess[str], dict[str, str]]:
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
                "PROJECT_KEY": project_key,
                "ORGANIZATION_KEY": organization_key,
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

    def test_main_without_project_key_fails_closed(self) -> None:
        result, values = self.invoke([], branch="main", project_key="")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("SONAR_PROJECT_KEY", result.stdout)
        self.assertEqual(values, {})

    def test_pr_without_organization_key_fails_closed(self) -> None:
        result, values = self.invoke([pull()], organization_key="")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("SONAR_ORGANIZATION", result.stdout)
        self.assertEqual(values, {})

    def test_coverage_exclusions_are_limited_to_ci_test_code(self) -> None:
        properties = (ROOT / ".sonarcloud.properties").read_text(encoding="utf-8")
        settings = dict(
            line.split("=", 1) for line in properties.splitlines()
            if line and not line.startswith("#") and "=" in line
        )
        self.assertEqual(
            settings.get("sonar.coverage.exclusions"),
            'scripts/ci/test-*.py,**/*.node-test.mjs,deploy/sakura/test_*.py,scripts/ci/qodana-packets/test_*.py,scripts/ci/test_audit_*.py,scripts/ci/test_av_mig_contract_boundary_*.py,scripts/ci/test_github_*.py,scripts/ci/test_nuget_*.py,scripts/ci/test_sec*.py,scripts/ci/test_specification_*.py,scripts/ci/test_verify_*.py,scripts/quality/test_*.py,tests/ci/test_*.py,scripts/security/test-schemathesis-input-attribution.py,tests/ci/test_restore_data_dependency_advisor.mjs',
            "Do not exempt production code from the Quality Gate coverage metric.",
        )
        self.assertNotIn(
            "sonar.issue.ignore.multicriteria", settings,
            "Keep the reviewed Docker rule exception in SonarCloud project settings.",
        )

    def test_schemathesis_coverage_exception_is_only_live_unit_test_body(self) -> None:
        relative = "scripts/security/test-schemathesis-input-attribution.py"
        path = ROOT / relative
        self.assertTrue(path.is_file())
        self.assertFalse(path.is_symlink())
        tree = ast.parse(path.read_text(encoding="utf-8"))
        classes = [node for node in tree.body if isinstance(node, ast.ClassDef)]
        self.assertTrue(classes)
        self.assertTrue(all(
            len(node.bases) == 1
            and isinstance(node.bases[0], ast.Attribute)
            and isinstance(node.bases[0].value, ast.Name)
            and node.bases[0].value.id == "unittest"
            and node.bases[0].attr == "TestCase"
            for node in classes
        ))
        self.assertFalse(any(
            isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef))
            for node in tree.body
        ))
        tests = [method for node in classes for method in node.body
                 if isinstance(method, ast.FunctionDef)
                 and method.name.startswith("test_")]
        self.assertEqual(len(tests), 28)
        runner = (ROOT / "scripts/security/schemathesis-runner.sh").read_text(
            encoding="utf-8"
        )
        self.assertIn("/work/" + relative, runner)

    def test_scanner_imports_the_narrow_coverage_scope(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn(
            "sonar.sourceEncoding|sonar.exclusions|sonar.cpd.exclusions"
            "|sonar.coverage.exclusions)", workflow
        )


class SonarSourceScopeTests(unittest.TestCase):
    def ignored(self, paths: list[str], *, ignore_case: bool = False) -> set[str]:
        result = subprocess.run(
            ["git", "-c", f"core.ignorecase={str(ignore_case).lower()}",
             "-c", "core.excludesFile=", "check-ignore", "--no-index", "--stdin", "-z"],
            cwd=ROOT, input=b"\0".join(p.encode("utf-8") for p in paths) + b"\0",
            capture_output=True,
            check=False,
        )
        self.assertIn(result.returncode, (0, 1), result.stderr.decode("utf-8"))
        return {p.decode("utf-8") for p in result.stdout.split(b"\0") if p}

    def test_application_artifact_sources_remain_visible(self) -> None:
        result = subprocess.run(
            ["git", "ls-files", "--", "frontend/src/app/features/artifacts",
             "src/Coglatas.Application/Artifacts", "tests/Coglatas.Tests/Artifacts"],
            cwd=ROOT, text=True, capture_output=True, check=True,
        )
        paths = result.stdout.splitlines()
        self.assertGreaterEqual(len(paths), 20, "Source controls must not be vacuous.")
        for ignore_case in (False, True):
            with self.subTest(ignore_case=ignore_case):
                self.assertEqual(self.ignored(paths, ignore_case=ignore_case), set())

    def test_generated_and_sensitive_paths_remain_ignored(self) -> None:
        paths = ["artifacts/sonar-dotnet/verified.opencover.xml",
                 "nested-job/artifacts/private-evidence.json", ".env",
                 "frontend/src/app/features/artifacts/private.pfx",
                 "frontend/src/app/features/artifacts/.env", "nested/id_rsa"]
        for ignore_case in (False, True):
            with self.subTest(ignore_case=ignore_case):
                self.assertEqual(self.ignored(paths, ignore_case=ignore_case), set(paths))

    def test_regression_dependencies_and_measurement_precede_credentials(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        begin = workflow.index("      - name: Begin pinned SonarCloud analysis")
        for name in (
            "Measure existing CI regression controls without analysis credentials",
            "Install existing reporter dependencies without analysis credentials",
            "Measure existing Functional and roster Node controls before credentials",
        ):
            start = workflow.index("      - name: " + name)
            end = workflow.find("\n      - name:", start + 1)
            block = workflow[start:end]
            self.assertLess(start, begin)
            self.assertNotIn("SONAR_TOKEN", block)
            self.assertNotIn("secrets.", block)
        self.assertIn("run: bash scripts/ci/npm-ci-retry.sh .", workflow[:begin])

    def test_subprocess_measurement_is_confined_before_credentials(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        start = workflow.index("      - name: Measure existing CI regression controls without analysis credentials")
        end = workflow.index("\n      - name:", start + 1)
        block = workflow[start:end]
        self.assertNotIn("GITHUB_ENV", block)
        self.assertIn('export SONAR_PYTHON_SOURCE_ROOT="$PWD"', block)
        self.assertNotIn("export COVERAGE_PROCESS_START", block)
        self.assertNotIn("export PYTHONPATH", block)
        self.assertIn('COVERAGE_PROCESS_START="$COVERAGE_RCFILE"', block)
        self.assertIn('sonar-python-coverage-bootstrap:$PYTHONPATH', block)
        self.assertLess(block.index('python3 -B -m coverage run "$control"'), block.index("coverage combine"))
        self.assertIn("test-sonar-python-process-coverage.py", block)
        self.assertIn("test-workspace-icon-disclosure-policy.py", block)
        self.assertIn("scripts/ci/test-postgres-log-privacy.py", block)
        self.assertIn("scripts/ci/test-functional-setup-failure-probe.py", block)
        self.assertIn("tests/ci/test_security_zap_attribution.py", block)
        self.assertIn("tests/ci/test_performance_db_ci.py", block)

    def test_native_node_mapping_is_complete_before_credentials(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        start = workflow.index("      - name: Measure existing Functional and roster Node controls before credentials")
        end = workflow.index("\n      - name:", start + 1)
        block = workflow[start:end]
        self.assertIn("node --enable-source-maps --test --experimental-test-coverage", block)
        self.assertIn('thread-secondary-reporter-*/**', block)
        self.assertIn("--test-coverage-exclude='**/*.node-test.mjs'", block)
        self.assertIn('verify-sonar-node-coverage.node-test.mjs', block)
        self.assertNotIn('tests/ui/student-council-pilot-helpers.node-test.mjs', block)
        pilot = workflow.split('- name: Measure existing Pilot helpers in a separate native cohort', 1)[1].split('- name: Retain original Pilot helper native coverage', 1)[0]
        self.assertIn('tests/ui/student-council-pilot-helpers.node-test.mjs', pilot)
        self.assertEqual(pilot.count('--test-coverage-include='), 3)
        self.assertNotIn('--test-coverage-exclude=', pilot)
        self.assertIn('validateNodeCoverage(text)', pilot)
        self.assertIn('assert.equal(checked.sourceFiles, 3)', pilot)
        self.assertIn('artifacts/sonar-pilot-helper/lcov.info', workflow)
        self.assertLess(workflow.index('- name: Retain original Pilot helper native coverage'), workflow.index('- name: Begin pinned SonarCloud analysis'))
        self.assertIn('tests/ci/test_restore_data_dependency_advisor.mjs', block)
        self.assertIn("--test-coverage-exclude='**/tests/ci/test_restore_data_dependency_advisor.mjs'", block)
        self.assertNotIn("--test-coverage-exclude='**/scripts/ci/restore-data-dependency-advisor.mjs'", block)
        self.assertIn('node scripts/ci/verify-sonar-node-coverage.mjs artifacts/sonar-node/lcov.info', block)
        self.assertNotIn('GITHUB_ENV', block)

    def test_native_fixture_filters_remain_exact_and_external(self) -> None:
        import re
        workflow = WORKFLOW.read_text(encoding="utf-8")
        start = workflow.index("      - name: Measure existing Functional and roster Node controls before credentials")
        end = workflow.index("\n      - name:", start + 1)
        block = workflow[start:end]
        self.assertEqual(re.findall(r"--test-coverage-exclude=([^\n]+)", block), [
            "'**/*.node-test.mjs' \\",
            "'**/tests/ci/test_restore_data_dependency_advisor.mjs' \\",
            '"$coverage_temp_root/thread-secondary-reporter-*/**" \\',
            '"$coverage_temp_root/coglatas-thread-matrix-*/**" \\',
            '"$coverage_temp_root/group-keyboard-control-*/**" \\',
            '"$coverage_temp_root/search-drawer-control-*/**" \\',
        ])
        self.assertIn('coverage_temp_root="$(node -p \'require("node:os").tmpdir()\')"', block)
        self.assertNotIn("--test-coverage-include", block)
        self.assertNotIn("lcov.info |", block)
        self.assertNotIn("lcov.info >", block)

    def test_existing_string_order_controls_and_original_serializer_are_measured(self) -> None:
        workflow = WORKFLOW.read_text(encoding="utf-8")
        start = workflow.index("      - name: Measure existing Functional and roster Node controls before credentials")
        end = workflow.index("\n      - name:", start + 1)
        block = workflow[start:end]
        for control in (
            "tests/ci/test_restore_data_dependency_advisor.mjs",
            "tests/diagnostics/thread-matrix.node-test.mjs",
            "tests/ui/group-keyboard-observation.node-test.mjs",
            "tests/ui/search-drawer-observation.node-test.mjs",
            "tests/ui/channel-dialog-serializer-coverage.node-test.mjs",
        ):
            self.assertIn("            " + control + " \\", block)
        config = (ROOT / "tests/ui/channel-dialog-serializer-coverage.config.mjs").read_text(encoding="utf-8")
        adapter = (ROOT / "tests/ui/channel-dialog-serializer-coverage.node-test.mjs").read_text(encoding="utf-8")
        source = (ROOT / "tests/ui/student-council-workspace-channels.spec.ts").read_text(encoding="utf-8")
        title = "channel-dialog diagnostic attachment accepts only fixed bounded structural data"
        self.assertIn("import base from '../../playwright.config.ts';", config)
        self.assertIn("...base,", config)
        self.assertIn("student-council-workspace-channels", config)
        self.assertIn(title + "$/", config)
        self.assertIn("projects: base.projects.filter", config)
        for project in ("chromium-desktop", "chromium-mobile"):
            self.assertIn(project, config)
            self.assertIn(project, adapter)
        self.assertIn("webServer: undefined", config)
        for override in ("timeout:", "expect:", "retries:", "use:"):
            self.assertNotIn(override, config)
        self.assertIn("channel-dialog-serializer-coverage.config.mjs", adapter)
        self.assertIn("'test', '--config'", adapter)
        self.assertEqual(source.count("test('" + title + "', () => {"), 1)

    def test_exact_advisor_unit_body_does_not_exempt_its_operational_cli(self) -> None:
        import fnmatch
        import re
        relative = "tests/ci/test_restore_data_dependency_advisor.mjs"
        source = (ROOT / relative).read_text(encoding="utf-8")
        self.assertIn("import test from 'node:test';", source)
        self.assertEqual(len(re.findall(r"(?m)^void test\(", source)), 23)
        self.assertNotRegex(source, r"(?m)^\s*export\b")
        self.assertNotIn("process.argv", source)
        operational = "scripts/ci/restore-data-dependency-advisor.mjs"
        self.assertIn("from '../../" + operational + "'", source)
        self.assertIn("'--synthetic-manifest', input", source)
        settings = dict(line.split("=", 1) for line in
                        (ROOT / ".sonarcloud.properties").read_text(encoding="utf-8").splitlines()
                        if line and not line.startswith("#") and "=" in line)
        coverage = settings["sonar.coverage.exclusions"].split(",")
        self.assertEqual([pattern for pattern in coverage if fnmatch.fnmatchcase(relative, pattern)], [relative])
        self.assertFalse(any(fnmatch.fnmatchcase(operational, pattern) for pattern in coverage))
        self.assertFalse(any(fnmatch.fnmatchcase(relative, pattern) for pattern in settings["sonar.exclusions"].split(",")))
        workflow = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("            tests/ci/test_restore_data_dependency_advisor.mjs \\", workflow)
        self.assertIn('node scripts/ci/verify-sonar-node-coverage.mjs artifacts/sonar-node/lcov.info', workflow)
        executable = "C:/Program Files/Git/cmd/git.exe" if os.name == "nt" else "/usr/bin/git"
        self.assertTrue(Path(executable).is_absolute() and (os.name != "nt" or Path(executable).drive))
        files = subprocess.check_output([executable, "ls-files", "-z"], cwd=ROOT).decode("utf-8").split("\0")
        for file in files:
            if file.endswith((".mjs", ".cjs", ".js", ".ts", ".mts", ".tsx")) and file != relative and not file.endswith(".node-test.mjs"):
                body = (ROOT / file).read_text(encoding="utf-8")
                self.assertNotIn("test_restore_data_dependency_advisor.mjs", body,
                                 "Operational source must not import or launch the excluded unit body: " + file)

    def test_node_test_bodies_do_not_exempt_operational_source(self) -> None:
        import fnmatch
        import re
        files = subprocess.check_output(["git", "ls-files", "-z"], cwd=ROOT).decode("utf-8").split("\0")
        tests = [file for file in files if fnmatch.fnmatchcase(file, "**/*.node-test.mjs")]
        self.assertGreaterEqual(len(tests), 40, "Test-only classification must be non-vacuous.")
        for file in tests:
            self.assertRegex(file, r"^(?:frontend/scripts|scripts/ci|tests)/")
            self.assertRegex((ROOT / file).read_text(encoding="utf-8"), r"(?:from\s*|import\s*)['\"]node:test['\"]")
        operational = ["scripts/ci/build-functional-grep.mjs", "scripts/ci/functional-tags.mjs",
                       "tests/functional/fixtures/functional-diagnostics.mjs",
                       "tests/functional/fixtures/messaging-observation.mjs",
                       "tests/functional/helpers/thread-probe-observation.mjs",
                       "tests/functional/mobile/outcome.mjs", "tests/functional/mobile/reporter.mjs",
                       "tests/ui/run-real-backend-readonly-roster.mjs"]
        for file in operational:
            self.assertTrue((ROOT / file).is_file())
            self.assertFalse(fnmatch.fnmatchcase(file, "**/*.node-test.mjs"))
        for file in files:
            if file.endswith((".mjs", ".cjs", ".js", ".ts", ".mts", ".tsx")) and file not in tests:
                body = (ROOT / file).read_text(encoding="utf-8")
                self.assertIsNone(re.search(r"(?:from\s*|import\s*(?:\(\s*)?)['\"][^'\"]*\.node-test\.mjs['\"]", body),
                                  "An operational module must not consume an excluded test body: " + file)

    def test_python_unit_patterns_preserve_operational_drivers_and_imports(self) -> None:
        import fnmatch
        settings = {
            key: value
            for line in (ROOT / ".sonarcloud.properties").read_text(encoding="utf-8").splitlines()
            if line and not line.startswith("#") and "=" in line
            for key, value in [line.split("=", 1)]
        }
        patterns = settings["sonar.coverage.exclusions"].split(",")
        executable = os.environ.get("COGLATAS_TEST_GIT_EXECUTABLE", "C:/Program Files/Git/cmd/git.exe" if os.name == "nt" else "/usr/bin/git")
        self.assertTrue(Path(executable).is_absolute() and (os.name != "nt" or Path(executable).drive))
        files = subprocess.check_output([executable, "ls-files", "-z"], cwd=ROOT).decode("utf-8").split("\0")
        trees = {}
        for file in files:
            if file.endswith(".py"):
                path = ROOT / file
                self.assertFalse(path.is_symlink())
                path.resolve().relative_to(ROOT)
                trees[file] = ast.parse(path.read_text(encoding="utf-8"))
        classified = {file for file in trees if "/test_" in file and any(fnmatch.fnmatchcase(file, pattern) for pattern in patterns)}
        self.assertGreaterEqual(len(classified), 50, "Unit classification must be non-vacuous.")
        for file in classified:
            self.assertTrue(python_unit_body(trees[file]), "Never classify an operational Python helper as a unit body: " + file)
        protected = ["scripts/ci/test_av_mig_production_cli.py", "scripts/ci/test_av_mig_runtime_cli.py",
                     "scripts/ci/test_av_mig_contract_boundary.py", "scripts/ci/thread_probe_diagnostics.py",
                     "scripts/ci/functional_diagnostics.py", "scripts/ci/release_supply_chain.py"]
        for file in protected:
            self.assertIn(file, trees)
            self.assertFalse(any(fnmatch.fnmatchcase(file, pattern) for pattern in patterns), "Keep runtime/source contracts measured: " + file)
        stems = {Path(file).stem for file in classified}
        for file, tree in trees.items():
            if not python_unit_body(tree) or file in protected:
                self.assertFalse(stems & python_import_stems(tree), "Operational Python must not import an exempt unit body: " + file)

    def test_sonar_output_patterns_do_not_shadow_application_artifacts(self) -> None:
        settings = {}
        for line in (ROOT / ".sonarcloud.properties").read_text(encoding="utf-8").splitlines():
            if line and not line.startswith("#") and "=" in line:
                key, value = line.split("=", 1)
                settings[key] = value
        for key in ("sonar.exclusions", "sonar.cpd.exclusions"):
            patterns = settings[key].split(",")
            self.assertIn("artifacts/**", patterns)
            self.assertNotIn("**/artifacts/**", patterns)
        self.assertNotIn("sonar.scm.exclusions.disabled", settings)


if __name__ == "__main__":
    unittest.main()
