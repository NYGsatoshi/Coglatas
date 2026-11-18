#!/usr/bin/env python3
"""Git isolation regressions over retained native inputs; controls do not execute dotnet."""
from __future__ import annotations

import argparse
from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

HERE = Path(__file__).resolve().parent
SPEC = importlib.util.spec_from_file_location("scenario_core_candidate_runner", HERE / "compose_and_verify.py")
assert SPEC is not None and SPEC.loader is not None
RUNNER = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(RUNNER)
SOURCE_BYTES = (HERE / Path(RUNNER.SOURCE).name).read_bytes()
SOURCE_BLOB = "abdd8eb8d2975567602d22d19f969458587b3a04"
EXPECTED_COUNT = 20
PASS_STATUS = "SYNTHETIC_SCENARIO_CORE_PASSED"
NATIVE_TRX_BYTES = b""
NATIVE_DISCOVERY = ""


def fixture_environment() -> dict[str, str]:
    # Independent of the function under test, including before fixture writes.
    return {key: value for key, value in os.environ.items() if not key.upper().startswith("GIT_")}


def fixture_git(repository: Path, *args: str) -> str:
    completed = subprocess.run(
        ["git", "-c", "core.autocrlf=false", "-c", "commit.gpgsign=false",
         "-c", "core.hooksPath=" + str(repository / "empty-hooks"),
         "-c", "user.name=Git isolation controls",
         "-c", "user.email=git-isolation-controls@example.invalid", *args],
        cwd=repository, env=fixture_environment(), capture_output=True, text=True, check=True)
    return completed.stdout.strip()


def file_snapshot(directory: Path) -> dict[str, bytes]:
    return {str(path.relative_to(directory)): path.read_bytes()
            for path in directory.rglob("*") if path.is_file()}


def validate_retained_inputs() -> None:
    if not NATIVE_TRX_BYTES or not NATIVE_DISCOVERY:
        raise ValueError("Original native TRX and compiled discovery inputs are required")
    with tempfile.TemporaryDirectory(prefix="retained-git-controls-") as temporary:
        path = Path(temporary) / "candidate.trx"
        path.write_bytes(NATIVE_TRX_BYTES)
        native = RUNNER.native_inventory(path)
    if native != RUNNER.compiled_inventory(NATIVE_DISCOVERY):
        raise ValueError("Retained native TRX and compiled discovery identities differ")


class GitIsolationControls(unittest.TestCase):
    """Actual main and real Git; only dotnet returns controlled retained outputs."""

    @classmethod
    def setUpClass(cls) -> None:
        validate_retained_inputs()

    def setUp(self) -> None:
        temporary = tempfile.TemporaryDirectory(prefix="scenario_core-git-controls-")
        self.addCleanup(temporary.cleanup)
        self.directory = Path(temporary.name)
        self.repository, self.product, self.product_tree, self.head = self.make_repository("intended", SOURCE_BYTES)
        self.foreign, _, _, self.foreign_head = self.make_repository("foreign", b"// Foreign source canary.\n")
        self.assertNotEqual(self.head, self.foreign_head)

    def make_repository(self, name: str, source_bytes: bytes) -> tuple[Path, str, str, str]:
        repository = self.directory / name
        repository.mkdir()
        fixture_git(repository, "init", "--quiet", "--template=")
        (repository / "product.txt").write_text(name + " immutable Product fixture\n", encoding="utf-8")
        fixture_git(repository, "add", "--", "product.txt")
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable Product fixture")
        product = fixture_git(repository, "rev-parse", "HEAD")
        product_tree = fixture_git(repository, "rev-parse", "HEAD^{tree}")
        source = repository / RUNNER.SOURCE
        source.parent.mkdir(parents=True)
        source.write_bytes(source_bytes)
        fixture_git(repository, "add", "--", RUNNER.SOURCE)
        fixture_git(repository, "commit", "--quiet", "-m", "Disposable Test fixture")
        return repository, product, product_tree, fixture_git(repository, "rev-parse", "HEAD")

    def run_main(self, overrides: dict[str, str]) -> None:
        worktree, evidence = self.directory / "composed", self.directory / "evidence"
        original_foreign = file_snapshot(self.foreign)
        original_source = (self.repository / RUNNER.SOURCE).read_bytes()
        real_command = RUNNER.command
        stages = []

        def controlled_command(args, cwd, log=None, *, preserve_output=False):
            if args[0] != "dotnet":
                return real_command(args, cwd, log, preserve_output=preserve_output)
            stage = "discovery" if "--list-tests" in args else args[1]
            stages.append(stage)
            if stage not in ("--version", "restore", "test", "discovery"):
                raise AssertionError("Unexpected controlled command")
            output = "Controlled output; no native execution.\n"
            if stage == "--version":
                output = "10.0.403\n"
            elif stage == "test":
                (evidence / "candidate.trx").write_bytes(NATIVE_TRX_BYTES)
            elif stage == "discovery":
                output = NATIVE_DISCOVERY
            if log is not None:
                log.write_text(output, encoding="utf-8")
            return output if preserve_output else output.strip()

        environment = {**fixture_environment(), **overrides}
        try:
            with patch.dict(os.environ, environment, clear=True), \
                    patch.object(RUNNER, "ROOT", self.repository), \
                    patch.object(RUNNER, "PRODUCT_HEAD", self.product, create=True), \
                    patch.object(RUNNER, "PRODUCT_TREE", self.product_tree, create=True), \
                    patch.object(RUNNER, "command", controlled_command), \
                    patch.object(sys, "argv", ["compose_and_verify.py", "--product-head", self.product,
                                              "--worktree", str(worktree), "--evidence", str(evidence)]), \
                    redirect_stdout(io.StringIO()):
                RUNNER.main()
                self.assertEqual(environment, dict(os.environ))
            receipt = json.loads((evidence / "receipt.json").read_text(encoding="utf-8"))
            self.assertEqual(PASS_STATUS, receipt["status"])
            self.assertEqual(self.head, receipt["testHead"])
            self.assertEqual(self.product, receipt["productHead"])
            self.assertEqual(self.product_tree, receipt["productTree"])
            self.assertEqual(SOURCE_BLOB, receipt["sourceBlob"])
            self.assertEqual(hashlib.sha256(SOURCE_BYTES).hexdigest(), receipt["sourceSha256"])
            self.assertEqual(SOURCE_BYTES, (worktree / RUNNER.DESTINATION).read_bytes())
            self.assertEqual(self.product, fixture_git(worktree, "rev-parse", "HEAD"))
            self.assertEqual(receipt["composedTree"], fixture_git(worktree, "write-tree"))
            self.assertEqual(sorted(RUNNER.EXPECTED_IDENTITIES), receipt["nativeXunit"]["identities"])
            self.assertEqual(["--version", "restore", "test", "discovery"], stages)
            for flag in ("requiredCheckQualified", "independentReviewComplete", "nativeUiQualified",
                         "productionQualified", "trustQualified", "releaseQualified"):
                self.assertIs(False, receipt[flag])
            self.assertEqual(original_source, (self.repository / RUNNER.SOURCE).read_bytes())
            self.assertEqual(self.head, fixture_git(self.repository, "rev-parse", "HEAD"))
            self.assertEqual("", fixture_git(self.repository, "status", "--porcelain", "--untracked-files=all"))
        finally:
            if worktree.exists():
                fixture_git(self.repository, "worktree", "remove", "--force", str(worktree))
            self.assertEqual(original_foreign, file_snapshot(self.foreign))

    def test_source_and_original_native_roster_remain_frozen(self) -> None:
        blob = hashlib.sha1(b"blob " + str(len(SOURCE_BYTES)).encode() + b"\0" + SOURCE_BYTES).hexdigest()
        self.assertEqual(SOURCE_BLOB, blob)
        methods = re.findall(r"\[Fact\]\s+public (?:async )?(?:Task|void) (\w+)\(", SOURCE_BYTES.decode())
        self.assertEqual(EXPECTED_COUNT, len(methods))
        self.assertEqual(RUNNER.EXPECTED_IDENTITIES,
                         frozenset(RUNNER.TEST_CLASS_FQN + "." + method for method in methods))
        validate_retained_inputs()

    def test_git_child_environment_preserves_non_git_settings(self) -> None:
        supplied = {
            "GIT_DIR": "foreign", "git_work_tree": "foreign", "GiT_INDEX_FILE": "foreign",
            "GIT_OBJECT_DIRECTORY": "foreign", "GIT_ALTERNATE_OBJECT_DIRECTORIES": "foreign",
            "GIT_COMMON_DIR": "foreign", "GIT_CONFIG_COUNT": "1", "GIT_CONFIG_KEY_0": "core.bare",
            "GIT_CONFIG_VALUE_0": "true", "GIT_CONFIG_PARAMETERS": "'core.bare=true'",
            "PATH": "host-path", "DOTNET_CLI_UI_LANGUAGE": "en-US", "CONTROL_CANARY": "retained",
        }
        with patch.dict(os.environ, supplied, clear=True):
            before = dict(os.environ)
            self.assertEqual({key: supplied[key] for key in ("PATH", "DOTNET_CLI_UI_LANGUAGE", "CONTROL_CANARY")},
                             RUNNER.local_git_environment())
            self.assertEqual(before, dict(os.environ))

    def test_main_admits_original_inputs_without_git_overrides(self) -> None:
        self.run_main({})

    def test_repository_redirect_affects_only_unprotected_read(self) -> None:
        overrides = {"GIT_DIR": str(self.foreign / ".git"), "GIT_WORK_TREE": str(self.foreign)}
        before = file_snapshot(self.foreign)
        with patch.dict(os.environ, {**fixture_environment(), **overrides}, clear=True):
            unprotected = subprocess.run(["git", "rev-parse", "HEAD"], cwd=self.repository,
                                         capture_output=True, text=True, check=True).stdout.strip()
            self.assertEqual(self.foreign_head, unprotected)
            self.assertEqual(self.head, RUNNER.git(self.repository, "rev-parse", "HEAD"))
        self.assertEqual(before, file_snapshot(self.foreign))
        self.run_main(overrides)

    def test_foreign_index_is_not_read_or_modified(self) -> None:
        self.run_main({"GIT_INDEX_FILE": str(self.foreign / ".git" / "index")})

    def test_foreign_object_and_common_directories_are_ignored(self) -> None:
        self.run_main({"GIT_OBJECT_DIRECTORY": str(self.foreign / ".git" / "objects"),
                       "GIT_ALTERNATE_OBJECT_DIRECTORIES": str(self.foreign / ".git" / "objects"),
                       "GIT_COMMON_DIR": str(self.foreign / ".git")})

    def test_counted_git_configuration_cannot_replace_worktree(self) -> None:
        self.run_main({"GIT_CONFIG_COUNT": "2", "GIT_CONFIG_KEY_0": "core.worktree",
                       "GIT_CONFIG_VALUE_0": str(self.foreign),
                       "GIT_CONFIG_KEY_1": "core.bare", "GIT_CONFIG_VALUE_1": "true"})

    def test_parameter_git_configuration_cannot_disable_worktree(self) -> None:
        self.run_main({"GIT_CONFIG_PARAMETERS": "'core.bare=true'"})


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--native-trx", type=Path, required=True)
    parser.add_argument("--compiled-discovery", type=Path, required=True)
    arguments, unittest_arguments = parser.parse_known_args()
    NATIVE_TRX_BYTES = arguments.native_trx.read_bytes()
    NATIVE_DISCOVERY = arguments.compiled_discovery.read_bytes().decode("utf-8")
    validate_retained_inputs()
    print("Admission controls reuse retained TRX SHA256 " + hashlib.sha256(NATIVE_TRX_BYTES).hexdigest()
          + "; they do not execute dotnet.", flush=True)
    unittest.main(argv=[sys.argv[0], *unittest_arguments])
