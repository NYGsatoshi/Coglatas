"""Contract controls for the real SEC-15 compiler caller; mocked execution is not runtime evidence."""
import copy
import contextlib
import io
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest import mock

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location("sec15_compile", ROOT / "scripts/ci/run-sec15-restler-compile.py")
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

def contract():
    return {"openapi": "3.1.0", "paths": {"/api/items": {"get": {}, "post": {}},
            "/api/items/{id}": {"get": {}, "delete": {}}}}

def grammar(expected):
    return {"Requests": [{"id": {"endpoint": route, "method": verb.title()}}
                         for verb, route in sorted(expected)]}

class CompileContracts(unittest.TestCase):
    def setUp(self):
        self.profile = json.loads((ROOT / m.PROFILE).read_text())
        uid = mock.patch.object(m.os, "getuid", return_value=1001)
        uid.start()
        self.addCleanup(uid.stop)

    def test_reviewed_profile_is_admitted(self):
        compiler, dictionary = m.admit_profile(self.profile)
        self.assertTrue(compiler["ResolveBodyDependencies"])
        self.assertEqual({}, dictionary["restler_custom_payload_header"])

    def test_profile_rejects_target_path_and_token_injection(self):
        mutations = [("SwaggerSpecFilePath", ["https://school.example/spec.json"]),
                     ("GrammarOutputDirectoryPath", "/host"), ("ReadOnlyFuzz", True),
                     ("UseRefreshableToken", False), ("JsonPropertyMaxDepth", 1),
                     ("ResolveBodyDependencies", 1)]
        for key, value in mutations:
            with self.subTest(key=key):
                changed = copy.deepcopy(self.profile)
                changed["compiler"][key] = value
                with self.assertRaises(m.Rejected):
                    m.admit_profile(changed)
        self.profile["dictionary"]["restler_custom_payload_header"]["Cookie"] = ["private-canary"]
        with self.assertRaises(m.Rejected):
            m.admit_profile(self.profile)

    def test_profile_rejects_moving_source_or_changed_tool(self):
        for key, value in [("commit", "main"), ("sdkImage", "mcr.microsoft.com/dotnet/sdk:latest"),
                           ("tree", "0" * 40), ("repository", "https://example.org/tool.git")]:
            changed = copy.deepcopy(self.profile)
            changed["tool"][key] = value
            with self.assertRaises(m.Rejected):
                m.admit_profile(changed)

    def test_current_openapi_31_patch_versions_are_preserved(self):
        original = m.operations(contract())
        for version in ("3.1.0", "3.1.1"):
            value = contract()
            value["openapi"] = version
            before = json.dumps(value).encode()
            self.assertEqual(version, m.contract_version(value))
            self.assertEqual(original, m.operations(value))
            self.assertEqual(before, json.dumps(value).encode())

    def test_contract_version_is_not_converted(self):
        for version in ("3.0.0", "3.2.0", "3.1.private-canary", "3.1.1\n", True, None):
            value = contract()
            value["openapi"] = version
            with self.assertRaises(m.Rejected):
                m.operations(value)

    def test_empty_or_invalid_contract_fails(self):
        for value in ({}, {"openapi": "3.1.0", "paths": {}},
                      {"openapi": "3.1.0", "paths": {"/api/a?private-canary": {"get": {}}}}):
            with self.assertRaises(m.Rejected):
                m.operations(value)

    def test_complete_actual_grammar_identity_set(self):
        expected = m.operations(contract())
        self.assertEqual(4, m.admit_grammar(json.dumps(grammar(expected)).encode(), expected))

    def test_missing_duplicate_foreign_grammar_operation_fails(self):
        expected = m.operations(contract())
        original = grammar(expected)
        variants = [copy.deepcopy(original) for _ in range(3)]
        variants[0]["Requests"].pop()
        variants[1]["Requests"][1] = variants[1]["Requests"][0]
        variants[2]["Requests"][0]["id"]["endpoint"] = "/foreign/private-canary"
        for value in variants:
            with self.assertRaises(m.Rejected):
                m.admit_grammar(json.dumps(value).encode(), expected)

    def test_base_digest_is_exact_official_repository(self):
        digest = "mcr.microsoft.com/dotnet/sdk@sha256:" + "a" * 64
        self.assertEqual(digest, m.admit_base(json.dumps([digest]).encode()))
        for value in ([digest, digest], [], ["mcr.microsoft.com/dotnet/sdk:8.0.413"],
                      ["example.org/sdk@sha256:" + "a" * 64], [True]):
            with self.assertRaises(m.Rejected):
                m.admit_base(json.dumps(value).encode())

    def test_image_identity_rejects_tag_or_extra_text(self):
        self.assertEqual("sha256:" + "b" * 64, m.image_id(("sha256:" + "b" * 64).encode()))
        for value in (b"image:latest", b"sha256:abc", b"private-canary"):
            with self.assertRaises(m.Rejected):
                m.image_id(value)

    def test_compiler_has_no_network_or_privileged_mount(self):
        args = m.compiler_command("sha256:" + "c" * 64, Path("/tmp/input"), Path("/tmp/output"))
        self.assertIn("--read-only", args)
        self.assertEqual("none", args[args.index("--network") + 1])
        self.assertEqual("ALL", args[args.index("--cap-drop") + 1])
        self.assertIn("no-new-privileges", args)
        self.assertIn("--user", args)
        self.assertFalse(any("docker.sock" in a or "privileged" in a or "host" == a for a in args))
        self.assertEqual("/input/config.json", args[-1])

    def test_mount_delimiter_injection_is_rejected(self):
        with self.assertRaises(m.Rejected):
            m.compiler_command("sha256:" + "d" * 64, Path("/tmp/input,readonly=false"), Path("/tmp/output"))

    def test_duplicate_json_and_nonfinite_are_rejected(self):
        for raw in (b'{"x":1,"x":2}', b'{"x":NaN}', b"", b"null" * 10):
            with self.assertRaises((ValueError, m.Rejected)):
                m.decode(raw)

    def test_symlink_or_fifo_input_rejected_before_open(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            (path / "target").write_bytes(b"private-canary")
            (path / "link").symlink_to(path / "target")
            os.mkfifo(path / "fifo")
            for item in ("link", "fifo"):
                with self.assertRaises(m.Rejected):
                    m.read_regular(path / item)

    def test_first_receipt_cannot_be_overwritten(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "receipt.json"
            m.publish_once({"decision": "FAIL"}, path)
            first = path.read_bytes()
            with self.assertRaises(FileExistsError):
                m.publish_once({"decision": "PASS_COMPILE_ONLY"}, path)
            self.assertEqual(first, path.read_bytes())

    def test_native_projection_is_exact_same_closed_receipt(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "receipt.json"
            stream = io.StringIO()
            result = {"decision": "FAIL", "failedOutputHashes": {"stderrSha256": m.sha(b"private-canary")}}
            with contextlib.redirect_stdout(stream):
                m.publish_once(result, path)
            block = stream.getvalue().split("SEC15_COMPILE_OUTCOME_BEGIN\n", 1)[1].split("SEC15_COMPILE_OUTCOME_END", 1)[0]
            self.assertEqual(path.read_text(), block)
            self.assertNotIn("private-canary", stream.getvalue())

    def test_native_failure_tokens_are_closed_metadata_only(self):
        value = m.compiler_failure_observation(
            b"Generating grammar... private-canary /private/path",
            b"Stack overflow. Restler.Compiler.SwaggerVisitors getActualSchema private-canary Cookie=secret")
        self.assertEqual("UNKNOWN", value["rootCause"])
        self.assertEqual(1, value["stderrTokenCounts"]["STACK_OVERFLOW_TOKEN"])
        self.assertEqual(1, value["stderrTokenCounts"]["ACTUAL_SCHEMA_TOKEN"])
        self.assertEqual(1, value["stdoutTokenCounts"]["GENERATING_GRAMMAR_TOKEN"])
        self.assertNotIn("private-canary", json.dumps(value))
        self.assertNotIn("/private/path", json.dumps(value))
        self.assertNotIn("Cookie", json.dumps(value))

    def test_actual_nullable_visitor_names_are_counted_without_exposing_diagnostics(self):
        diagnostic = (b"Unhandled exception. Restler.Compiler.UnsupportedType: "
                      b"Found unsupported type in body parameters: Null, String\n"
                      b"at Restler.Compiler.SwaggerVisitors.processProperty "
                      b"private-canary Cookie=do-not-print")
        result = m.compiler_failure_observation(b"Generating grammar...", diagnostic)
        self.assertEqual(1, result["stderrTokenCounts"]["RESTLER_VISITOR_TOKEN"])
        self.assertEqual(1, result["stderrTokenCounts"]["NULL_STRING_UNSUPPORTED_TOKEN"])
        self.assertEqual(1, result["stderrTokenCounts"]["UNHANDLED_EXCEPTION_TOKEN"])
        self.assertEqual("UNKNOWN", result["rootCause"])
        rendered = json.dumps(result)
        self.assertNotIn("private-canary", rendered)
        self.assertNotIn("Cookie", rendered)
        self.assertNotIn("processProperty", rendered)
        negative = m.compiler_failure_observation(b"", b"Restler.SwaggerVisitors")
        self.assertEqual(0, negative["stderrTokenCounts"]["RESTLER_VISITOR_TOKEN"])

    def test_unknown_or_empty_native_diagnostics_do_not_invent_cause(self):
        for stdout, stderr in ((b"", b""), (b"private-canary", b"unknown-private-error")):
            value = m.compiler_failure_observation(stdout, stderr)
            self.assertEqual("UNKNOWN", value["rootCause"])
            self.assertEqual(0, sum(value["stderrTokenCounts"].values()))
            self.assertEqual(0, sum(value["stdoutTokenCounts"].values()))

    def test_native_diagnostic_counts_are_capped_without_truncating_failure(self):
        value = m.compiler_failure_observation(b"", b"NSwag" * 1000001)
        self.assertEqual(1000000, value["stderrTokenCounts"]["NSWAG_TOKEN"])
        self.assertTrue(value["countsCapped"])
        self.assertEqual("UNKNOWN", value["rootCause"])

    def test_native_failure_input_bounds_and_types_are_enforced(self):
        for stdout, stderr in (("private-canary", b""), (b"", None)):
            with self.assertRaises(m.Rejected):
                m.compiler_failure_observation(stdout, stderr)
        with mock.patch.object(m, "LIMIT", 4):
            with self.assertRaises(m.Rejected):
                m.compiler_failure_observation(b"large", b"")

    def test_native_observation_never_changes_nonzero_compile_outcome(self):
        code, receipt, _ = self.trial("compile")
        self.assertEqual(1, code)
        self.assertEqual("FAIL", receipt["decision"])
        self.assertEqual(9, receipt["commandExitCodes"]["COMPILE"])
        self.assertEqual("UNKNOWN", receipt["nativeFailureObservation"]["rootCause"])
        self.assertEqual(0, sum(receipt["nativeFailureObservation"]["stderrTokenCounts"].values()))
        self.assertNotIn("private-canary", json.dumps(receipt))

    def trial(self, failing=None):
        """Exercise caller sequencing with controlled child processes, never claim a real compiler."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            for relative in m.SOURCES:
                path = root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(json.dumps(self.profile) if relative == m.PROFILE else "fixture")
            calls, receipts = [], []
            source, image = "1" * 40, "sha256:" + "2" * 64
            def child(args, **kwargs):
                calls.append(args)
                output = b""
                code = 0
                if args[:2] == ["bash", "scripts/ci/generate-security-openapi-contract.sh"]:
                    path = root / "artifacts/openapi/coglatas-openapi.json"
                    path.parent.mkdir(parents=True)
                    value = contract()
                    value["info"] = {"description": "private-canary"}
                    path.write_text(json.dumps(value))
                elif args[:3] == ["git", "rev-parse", "HEAD"]:
                    output = source.encode()
                elif args[:3] == ["git", "rev-parse", "HEAD^{tree}"]:
                    output = ("3" * 40).encode()
                elif args[0] == "git" and "-C" in args and args[-1] == "HEAD":
                    output = m.PIN.encode()
                elif args[0] == "git" and "-C" in args and args[-1] == "HEAD^{tree}":
                    output = m.TREE.encode()
                elif args[:3] == ["docker", "image", "inspect"]:
                    output = (json.dumps(["mcr.microsoft.com/dotnet/sdk@sha256:" + "4" * 64]).encode()
                              if ".RepoDigests" in args[-1] else image.encode())
                elif args[-1] == "--version":
                    output = b"8.0.413" if failing != "version" else b"9.0.0"
                elif args[-1] == "/input/config.json":
                    if failing == "compile":
                        code = 9
                        kwargs["stderr"].write(b"private-canary Cookie=do-not-print")
                    else:
                        mount = next(a for a in args if a.startswith("type=bind,src=") and a.endswith("dst=/output"))
                        directory = Path(mount.split("src=", 1)[1].split(",dst=", 1)[0]) / "compiled"
                        directory.mkdir()
                        expected = m.operations(contract())
                        value = grammar(expected)
                        if failing == "coverage":
                            value["Requests"].pop()
                        (directory / "grammar.json").write_text(json.dumps(value))
                        (directory / "grammar.py").write_text("VALUE = 1\n")
                        (directory / "dict.json").write_text("{}")
                if "stdout" in kwargs:
                    kwargs["stdout"].write(output)
                return subprocess.CompletedProcess(args, code, stdout=output, stderr=b"")
            env = {"GITHUB_SHA": source, "GITHUB_RUN_ID": "42", "GITHUB_RUN_ATTEMPT": "1",
                   "COGLATAS_SEC15_RUNNER_ENVIRONMENT": "github-hosted"}
            with mock.patch.object(m, "ROOT", root), mock.patch.dict(os.environ, env, clear=True), \
                 mock.patch.object(m.subprocess, "run", side_effect=child), \
                 mock.patch.object(m, "publish_once", side_effect=lambda r: receipts.append(copy.deepcopy(r))):
                code = m.execute()
            return code, receipts[0], calls

    def test_complete_controlled_sequence_is_compile_only(self):
        code, receipt, calls = self.trial()
        self.assertEqual(0, code)
        self.assertEqual("PASS_COMPILE_ONLY", receipt["decision"])
        self.assertEqual(4, receipt["compiledOperationCount"])
        self.assertEqual("NOT_EXECUTED", receipt["activeTargetTraffic"])
        self.assertEqual("NOT_EXECUTED", receipt["fuzzLean"])
        self.assertEqual("NOT_GRANTED", receipt["securityProgramAcceptance"])
        self.assertEqual(1, sum(a[-1] == "/input/config.json" for a in calls))
        self.assertNotIn("private-canary", json.dumps(receipt))

    def test_real_nonzero_compile_cannot_be_zero_finding_pass(self):
        code, receipt, calls = self.trial("compile")
        self.assertEqual(1, code)
        self.assertEqual("FAIL", receipt["decision"])
        self.assertEqual(9, receipt["commandExitCodes"]["COMPILE"])
        self.assertEqual({}, receipt["outputHashes"])
        self.assertEqual(1, sum(a[-1] == "/input/config.json" for a in calls))
        self.assertNotIn("private-canary", json.dumps(receipt))

    def test_zero_exit_with_partial_grammar_fails(self):
        code, receipt, _ = self.trial("coverage")
        self.assertEqual(1, code)
        self.assertEqual("FAIL", receipt["decision"])
        self.assertEqual(0, receipt["commandExitCodes"]["COMPILE"])

    def test_wrong_actual_sdk_prevents_compile(self):
        code, receipt, calls = self.trial("version")
        self.assertEqual(1, code)
        self.assertFalse(any(a[-1] == "/input/config.json" for a in calls))

if __name__ == "__main__":
    unittest.main()
