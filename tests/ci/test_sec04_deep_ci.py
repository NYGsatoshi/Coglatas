"""Prove that topology/scanner failures cannot qualify the deep caller."""
from datetime import datetime, timedelta, timezone
from contextlib import ExitStack, redirect_stderr, redirect_stdout
import io
from types import SimpleNamespace
from unittest.mock import patch
import importlib.util
import json
import os
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location("sec04_deep_ci", ROOT / "scripts/ci/run-sec04-deep-ci.py")
caller = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(caller)
START = datetime(2026, 10, 27, tzinfo=timezone.utc)
FINISH = START + timedelta(seconds=30)
CANARY = "PRIVATE_COOKIE_PASSWORD_BODY_MUST_NOT_APPEAR"


def fixture(directory):
    directory.mkdir()
    contract = directory.parent / "contract.json"
    contract.write_text(json.dumps({"openapi": "3.1.0", "paths": {"/api/synthetic": {"get": {}}}}))
    for role in caller.ROLES:
        metadata = {"contract_sha256": caller.sha(contract.read_bytes()), "lane": "deep", "network_errors": 0,
                    "operation_count": 1, "operations": ["GET /api/synthetic"], "request_count": 3,
                    "role": role, "scanner_exit": 0, "schemathesis_version": "4.25.2", "seed": 574042}
        (directory / (role + ".metadata.json")).write_text(json.dumps(metadata))
        events = [
            {"Initialize": {"schemathesis_version": "4.25.2", "seed": 574042}},
            {"EngineFinished": {"stop_reason": "completed", "timestamp": (START + timedelta(seconds=10)).timestamp(),
                                "running_time": 5, "payload": {"reauth_broke": False}}},
        ]
        (directory / (role + ".ndjson")).write_text("\n".join(json.dumps(row) for row in events) + "\n")
    return contract


class DeepExecutionEvidenceTests(unittest.TestCase):
    def test_five_actual_native_controls_are_required(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "reports"
            contract = fixture(directory)
            observed = caller.validate_execution(directory, contract, START, FINISH)
        self.assertEqual(set(observed["roles"]), set(caller.ROLES))
        self.assertEqual(observed["openApiOperationCount"], 1)
        self.assertTrue(all(row["requestCount"] == 3 and row["nativeEventCount"] == 2
                            and row["engineOutcome"] == "COMPLETED" for row in observed["roles"].values()))
        self.assertNotIn(CANARY, json.dumps(observed))

    def test_topology_or_zero_reports_cannot_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            contract = root / "contract.json"
            contract.write_text('{"openapi":"3.1.0","paths":{"/api/synthetic":{"get":{}}}}')
            with self.assertRaises((OSError, ValueError)):
                caller.validate_execution(root / "missing", contract, START, FINISH)

    def test_each_role_and_native_report_is_mandatory(self):
        for role in caller.ROLES:
            for suffix in (".metadata.json", ".ndjson"):
                with tempfile.TemporaryDirectory() as temporary:
                    directory = Path(temporary) / "reports"
                    contract = fixture(directory)
                    (directory / (role + suffix)).unlink()
                    with self.assertRaises((OSError, ValueError)):
                        caller.validate_execution(directory, contract, START, FINISH)

    def test_wrong_lane_role_contract_version_failure_and_empty_execution_rejected(self):
        mutations = (("lane", "pr"), ("role", "unknown"), ("contract_sha256", "f" * 64),
                     ("schemathesis_version", "0.0"), ("scanner_exit", 1), ("scanner_exit", False),
                     ("network_errors", 1), ("request_count", 0), ("request_count", True),
                     ("operation_count", 0), ("seed", False))
        for key, value in mutations:
            with tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary) / "reports"
                contract = fixture(directory)
                path = directory / (caller.ROLES[0] + ".metadata.json")
                document = json.loads(path.read_bytes())
                document[key] = value
                path.write_text(json.dumps(document))
                with self.assertRaises(ValueError):
                    caller.validate_execution(directory, contract, START, FINISH)

    def test_unknown_fields_operations_and_duplicate_metadata_rejected(self):
        for mutate in (
            lambda row: row.update(rawBody=CANARY),
            lambda row: row.update(operations=["POST /api/unknown"]),
            lambda row: row.update(operations=["GET /api/synthetic"] * 2, operation_count=2),
        ):
            with tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary) / "reports"
                contract = fixture(directory)
                path = directory / (caller.ROLES[0] + ".metadata.json")
                document = json.loads(path.read_bytes())
                mutate(document)
                path.write_text(json.dumps(document))
                with self.assertRaises(ValueError):
                    caller.validate_execution(directory, contract, START, FINISH)
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary) / "reports"
            contract = fixture(directory)
            path = directory / (caller.ROLES[0] + ".metadata.json")
            path.write_text('{"role":"anonymous","role":"anonymous"}')
            with self.assertRaises(ValueError):
                caller.validate_execution(directory, contract, START, FINISH)

    def test_native_missing_finish_auth_failure_stale_time_and_post_finish_event_rejected(self):
        mutations = (
            lambda rows: rows.pop(),
            lambda rows: rows[-1]["EngineFinished"]["payload"].update(reauth_broke=True),
            lambda rows: rows[-1]["EngineFinished"].update(timestamp=(START - timedelta(seconds=1)).timestamp()),
            lambda rows: rows[-1]["EngineFinished"].update(stop_reason="interrupted"),
            lambda rows: rows[0]["Initialize"].update(seed=123),
            lambda rows: rows.append({"EngineStarted": {}}),
        )
        for mutate in mutations:
            with tempfile.TemporaryDirectory() as temporary:
                directory = Path(temporary) / "reports"
                contract = fixture(directory)
                path = directory / (caller.ROLES[0] + ".ndjson")
                rows = [json.loads(line) for line in path.read_text().splitlines()]
                mutate(rows)
                path.write_text("\n".join(json.dumps(row) for row in rows) + "\n")
                with self.assertRaises(ValueError):
                    caller.validate_execution(directory, contract, START, FINISH)

    def test_only_fixed_native_paths_can_be_opened(self):
        with tempfile.TemporaryDirectory() as temporary:
            archive = caller.Reports(Path(temporary))
            for value in ("../../private", "artifacts/security/schemathesis/unknown.ndjson"):
                with self.assertRaises(caller.Rejected):
                    archive.open(value)

    def test_unsafe_oversized_empty_and_fifo_inputs_fail_closed(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            regular = root / "regular"
            regular.write_bytes(b"data")
            link = root / "link"
            try:
                link.symlink_to(regular)
            except OSError:
                link = None
            if link is not None:
                with self.assertRaises(ValueError):
                    caller.read_regular(link, 100)
            with self.assertRaises(ValueError):
                caller.read_regular(regular, 1)
            regular.write_bytes(b"")
            with self.assertRaises(ValueError):
                caller.read_regular(regular, 100)
            if hasattr(os, "mkfifo"):
                fifo = root / "fifo"
                os.mkfifo(fifo)
                with self.assertRaises(ValueError):
                    caller.read_regular(fifo, 100)


class StageExecutionTests(unittest.TestCase):
    def execute(self, codes=None, raised=None):
        calls = []
        result = {"stageExitCodes": {}}
        def command(args, phase, environment):
            stage = args[-1]
            calls.append((stage, environment.copy()))
            if stage == raised:
                raise OSError(CANARY)
            return (codes or {}).get(stage, 0), b""
        return result, calls, command

    def test_actual_stage_sequence_and_cleanup_are_called_once(self):
        result, calls, command = self.execute()
        caller.execute_stages(command, {}, result)
        self.assertEqual([row[0] for row in calls], ["prepare", "sec03", "sec04", "cleanup"])
        self.assertEqual(result["stageExitCodes"], {"prepare": 0, "sec03": 0, "sec04": 0, "cleanup": 0})
        self.assertEqual(calls[-1][1]["COGLATAS_SECURITY_RUNTIME_FAILED"], "false")

    def test_each_scanner_preflight_and_cleanup_failure_remains_failure(self):
        for stage in ("prepare", "sec03", "sec04", "cleanup"):
            result, calls, command = self.execute({stage: 23})
            with self.assertRaises(ValueError):
                caller.execute_stages(command, {}, result)
            self.assertEqual(result["stageExitCodes"][stage], 23)
            self.assertEqual([row[0] for row in calls].count(stage), 1)
            self.assertEqual([row[0] for row in calls].count("cleanup"), 1)
            if stage != "cleanup":
                self.assertEqual(result["failedStage"], stage)
                self.assertEqual(calls[-1][1]["COGLATAS_SECURITY_RUNTIME_FAILED"], "true")

    def test_scanner_failure_and_cleanup_failure_preserve_both_first_codes(self):
        result, calls, command = self.execute({"sec04": 17, "cleanup": 23})
        with self.assertRaises(ValueError):
            caller.execute_stages(command, {}, result)
        self.assertEqual(result["stageExitCodes"]["sec04"], 17)
        self.assertEqual(result["stageExitCodes"]["cleanup"], 23)
        self.assertEqual(len(calls), 4)

    def test_process_exception_still_calls_cleanup_and_cannot_pass(self):
        result, calls, command = self.execute(raised="sec03")
        with self.assertRaises(OSError):
            caller.execute_stages(command, {}, result)
        self.assertEqual([row[0] for row in calls], ["prepare", "sec03", "cleanup"])
        self.assertEqual(calls[-1][1]["COGLATAS_SECURITY_RUNTIME_FAILED"], "true")
        self.assertNotIn(CANARY, json.dumps(result))

    def test_first_failed_receipt_cannot_be_replaced_by_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            path = Path(temporary) / "outcome.json"
            caller.publish_once(path, {"status": "FAIL"})
            first = path.read_bytes()
            with self.assertRaises(FileExistsError):
                caller.publish_once(path, {"status": "PASS"})
            self.assertTrue(path.read_bytes() == first)


@unittest.skipUnless(hasattr(os, "getuid"), "POSIX mode contract")
class PublicContractAndFailureMetadataTests(unittest.TestCase):
    def source_root(self, parent):
        root = parent / "source"
        root.mkdir(mode=0o755)
        root.chmod(0o755)
        for path in (root / "scripts", root / "scripts/security"):
            path.mkdir(mode=0o755)
            path.chmod(0o755)
        for name in ("test-schemathesis-query-wire.py", "test-schemathesis-input-attribution.py",
                     "schemathesis_hooks.py", "schemathesis_policy.py"):
            path = root / "scripts/security" / name
            path.write_text("# public source\n")
            path.chmod(0o644)
        return root

    def test_public_only_modes_preserve_bytes_and_every_private_canary(self):
        with tempfile.TemporaryDirectory() as temporary:
            parent = Path(temporary)
            root = self.source_root(parent)
            private = root / "private-state"
            private.mkdir(mode=0o700)
            private.chmod(0o700)
            secret = private / "credential"
            secret.write_text(CANARY)
            secret.chmod(0o600)
            old_umask = os.umask(0o077)
            try:
                contract = caller.reserve_public_contract(root)
                raw = b'{"openapi":"3.1.0","paths":{"/api/synthetic":{"get":{}}}}'
                contract.write_bytes(raw)
                self.assertEqual(contract.stat().st_mode & 0o777, 0o600)
                report_private = root / "artifacts/private-reports"
                report_private.mkdir(mode=0o700)
                report_file = report_private / "native.json"
                report_file.write_text(CANARY)
                observed = caller.stage_public_contract(root)
            finally:
                os.umask(old_umask)
            self.assertEqual((root / "artifacts").stat().st_mode & 0o777, 0o755)
            self.assertEqual(contract.parent.stat().st_mode & 0o777, 0o755)
            self.assertEqual(contract.stat().st_mode & 0o777, 0o644)
            self.assertTrue(contract.read_bytes() == raw)
            self.assertEqual(observed["sha256"], caller.sha(raw))
            self.assertEqual(private.stat().st_mode & 0o777, 0o700)
            self.assertEqual(secret.stat().st_mode & 0o777, 0o600)
            self.assertEqual(report_private.stat().st_mode & 0o777, 0o700)
            self.assertEqual(report_file.stat().st_mode & 0o777, 0o600)
            self.assertEqual(parent.stat().st_mode & 0o777, 0o700)
            self.assertNotIn(CANARY, json.dumps(observed))

    def test_existing_public_parent_and_nontraversable_source_are_not_adopted(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = self.source_root(Path(temporary))
            artifacts = root / "artifacts"
            artifacts.mkdir(mode=0o700)
            artifacts.chmod(0o700)
            with self.assertRaises(ValueError):
                caller.reserve_public_contract(root)
            self.assertEqual(artifacts.stat().st_mode & 0o777, 0o700)
        with tempfile.TemporaryDirectory() as temporary:
            root = self.source_root(Path(temporary))
            root.chmod(0o700)
            with self.assertRaises(ValueError):
                caller.reserve_public_contract(root)
            self.assertFalse((root / "artifacts").exists())

    def test_symlink_contract_cannot_change_private_target_modes(self):
        with tempfile.TemporaryDirectory() as temporary:
            parent = Path(temporary)
            root = self.source_root(parent)
            contract = caller.reserve_public_contract(root)
            private = parent / "private"
            private.write_text(CANARY)
            private.chmod(0o600)
            contract.symlink_to(private)
            with self.assertRaises(ValueError):
                caller.stage_public_contract(root)
            self.assertEqual(private.stat().st_mode & 0o777, 0o600)

    def test_failure_projection_is_fixed_and_never_echoes_child_values(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            reports = root / "reports"
            contract = fixture(reports)
            private = root / "private"
            private.mkdir()
            stdout = ("SECURITY_SCAN_PROGRESS role_scan_start role=anonymous\n"
                      "SECURITY_SCAN_PROGRESS role_scan_start role=" + CANARY + "\n"
                      "PermissionError: " + CANARY + "\n")
            (private / "SEC04.stdout").write_text(stdout)
            (private / "SEC04.stderr").write_text("SEC-04 Schemathesis evidence failed: " + CANARY)
            observed = caller.failure_observations(private, reports, contract)
            self.assertEqual(observed["qualification"], "NONE")
            self.assertEqual(observed["progress"], {"anonymous": {"role_scan_start": 1}})
            self.assertEqual(observed["errorClassesObserved"],
                             ["PERMISSION_ERROR_OBSERVED", "PROCESSOR_REJECTION_OBSERVED"])
            self.assertEqual(observed["logs"]["stdout"]["sha256"], caller.sha(stdout.encode()))
            self.assertNotIn(CANARY, json.dumps(observed))
            self.assertNotIn("PASS", json.dumps(observed))

    def test_missing_and_malformed_failure_roles_stay_explicit(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            reports = root / "reports"
            contract = fixture(reports)
            private = root / "private"
            private.mkdir()
            for role in caller.ROLES:
                (reports / (role + ".metadata.json")).unlink()
            observed = caller.failure_observations(private, reports, contract)
            self.assertEqual(set(observed["roles"]), set(caller.ROLES))
            self.assertTrue(all(row == {"availability": "MISSING_OR_INVALID"}
                                for row in observed["roles"].values()))
            (reports / (caller.ROLES[0] + ".metadata.json")).write_text('{"raw": "' + CANARY + '"}')
            observed = caller.failure_observations(private, reports, contract)
            self.assertEqual(observed["roles"][caller.ROLES[0]]["availability"], "MISSING_OR_INVALID")
            self.assertNotIn(CANARY, json.dumps(observed))

    def test_actual_nonzero_metadata_and_complete_native_file_do_not_become_qualification(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            reports = root / "reports"
            contract = fixture(reports)
            private = root / "private"
            private.mkdir()
            path = reports / (caller.ROLES[0] + ".metadata.json")
            metadata = json.loads(path.read_bytes())
            metadata["scanner_exit"] = 17
            path.write_text(json.dumps(metadata))
            observed = caller.failure_observations(private, reports, contract)
            self.assertEqual(observed["roles"][caller.ROLES[0]]["scannerExit"], 17)
            self.assertEqual(observed["qualification"], "NONE")
            self.assertNotIn("PASS", json.dumps(observed))
            with self.assertRaises(ValueError):
                caller.validate_execution(reports, contract, START, FINISH)

    def test_unsafe_log_or_wrong_contract_metadata_is_unavailable(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            reports = root / "reports"
            contract = fixture(reports)
            private = root / "private"
            private.mkdir()
            hidden = root / "hidden"
            hidden.write_text(CANARY)
            (private / "SEC04.stdout").symlink_to(hidden)
            path = reports / (caller.ROLES[0] + ".metadata.json")
            metadata = json.loads(path.read_bytes())
            metadata["contract_sha256"] = "a" * 64
            path.write_text(json.dumps(metadata))
            observed = caller.failure_observations(private, reports, contract)
            self.assertEqual(observed["logs"]["stdout"], {"availability": "UNAVAILABLE"})
            self.assertEqual(observed["roles"][caller.ROLES[0]]["availability"], "MISSING_OR_INVALID")
            self.assertNotIn(CANARY, json.dumps(observed))


class CallerCommands:
    """Finite fake child-process boundary for main-only unit controls."""

    def __init__(self, root, reports, contract, fault):
        self.root, self.reports, self.contract, self.fault = root, reports, contract, fault
        self.calls = []
        self.status_reads = 0

    def run(self, args, **kwargs):
        self.calls.append((args, kwargs['env'].copy()))
        if args[0] == 'git':
            code, data = self.git(args)
        elif args[0] == 'docker':
            code, data = self.docker(args)
        elif args[:2] == ['bash', 'scripts/ci/generate-security-openapi-contract.sh']:
            self.contract.parent.mkdir(parents=True, exist_ok=True)
            self.contract.write_text(json.dumps({'openapi': '3.1.0', 'paths': {'/api/synthetic': {'get': {}}}}))
            code, data = 0, CANARY.encode()
        elif args[:2] == ['bash', caller.RUNTIME]:
            code, data = self.runtime(args[-1]), CANARY.encode()
        else:
            raise AssertionError('Unexpected child command in isolated unit control')
        return SimpleNamespace(returncode=code, stdout=data, stderr=CANARY.encode())

    def git(self, args):
        if args[1] == 'rev-parse':
            if args[-1] == 'HEAD^{tree}':
                return 0, b'invalid' if self.fault == 'invalid-tree' else b'b' * 40
            return 0, (('d' if self.fault == 'source-mismatch' else 'a') * 40).encode()
        if args[1] != 'status':
            raise AssertionError('Unexpected Git command in isolated unit control')
        self.status_reads += 1
        dirty = self.fault == 'dirty-source' or (self.fault == 'dirty-after' and self.status_reads == 2)
        return 0, b'M synthetic-source' if dirty else b''

    def docker(self, args):
        if args[2] == 'image':
            return 0, b'sha256:' + b'c' * 64
        return (23 if self.fault == 'build' else 0), CANARY.encode()

    def runtime(self, stage):
        if stage == 'sec04':
            self.reports.parent.mkdir(parents=True, exist_ok=True)
            fixture(self.reports)
            return 17 if self.fault == 'sec04' else 0
        if stage == 'cleanup':
            return self.cleanup()
        return 0

    def cleanup(self):
        if self.fault == 'missing-native':
            (self.reports / (caller.ROLES[-1] + '.ndjson')).unlink()
        if self.fault == 'changed-binding':
            (self.root / caller.SOURCES[0]).write_bytes(b'changed synthetic source')
        return 23 if self.fault == 'cleanup' else 0


class CallerMainControlsTests(unittest.TestCase):
    """Exercise caller orchestration; no real child command or external target."""

    def exercise(self, fault=None, changes=None, arguments=None):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary) / 'source'
            root.mkdir()
            for name in caller.SOURCES:
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'synthetic source binding\n')
            public = root / 'artifacts/sec04-deep-execution/result.json'
            reports = root / 'artifacts/security/schemathesis'
            contract = root / 'artifacts/openapi/coglatas-openapi.json'
            runner = Path(temporary) / 'runner'
            runner.mkdir()
            output = Path(temporary) / 'github-output'
            environment = {
                'COGLATAS_SEC04_RUNNER_ENVIRONMENT': 'github-hosted', 'RUNNER_OS': 'Linux',
                'GITHUB_EVENT_NAME': 'workflow_dispatch', 'GITHUB_REF': 'refs/heads/synthetic',
                'GITHUB_SHA': 'a' * 40, 'GITHUB_RUN_ID': '123', 'GITHUB_RUN_ATTEMPT': '1',
                'COGLATAS_SECURITY_CI_PASSWORD': CANARY, 'RUNNER_TEMP': str(runner),
                'GITHUB_OUTPUT': str(output), 'DOCKER_HOST': 'tcp://synthetic.invalid:2375',
                'DOCKER_CONTEXT': 'synthetic', 'DOCKER_TLS': '1', 'DOCKER_TLS_VERIFY': '1',
                'DOCKER_CERT_PATH': CANARY, 'SECURITY_SCAN_TARGET': 'https://synthetic.invalid',
                'COGLATAS_SECURITY_SCHEMATHESIS_DEEP_EXAMPLES': '1',
                'COGLATAS_SECURITY_SCHEMATHESIS_PR_EXAMPLES': '1',
                'COGLATAS_SECURITY_SCHEMATHESIS_SEED': '1',
                'COGLATAS_SECURITY_CI_PROJECT': CANARY,
                'COGLATAS_SECURITY_RUNTIME_STATE_DIR': CANARY,
                'COGLATAS_SECURITY_RUNTIME_IMAGE': 'synthetic-unpinned',
            }
            for key, value in (changes or {}).items():
                if value is None:
                    environment.pop(key, None)
                else:
                    environment[key] = value
            commands = CallerCommands(root, reports, contract, fault)

            captured, errors = io.StringIO(), io.StringIO()
            with ExitStack() as stack:
                for name, value in [('ROOT', root), ('PUBLIC', public), ('REPORTS', reports), ('CONTRACT', contract)]:
                    stack.enter_context(patch.object(caller, name, value))
                stack.enter_context(patch.dict(os.environ, environment, clear=True))
                stack.enter_context(patch.object(caller.sys, 'argv', arguments or ['synthetic-caller']))
                stack.enter_context(patch.object(caller.os, 'umask', return_value=0o022))
                stack.enter_context(patch.object(caller.subprocess, 'run', side_effect=commands.run))
                stack.enter_context(patch.object(caller, 'reserve_public_contract', return_value=contract))
                stack.enter_context(patch.object(caller, 'stage_public_contract', return_value={'unitCollaborator': True}))
                clock = stack.enter_context(patch.object(caller, 'datetime'))
                clock.now.side_effect = [START, FINISH]
                if fault == 'publish':
                    stack.enter_context(patch.object(caller, 'publish_once', side_effect=PermissionError(CANARY)))
                with redirect_stdout(captured), redirect_stderr(errors):
                    code = caller.main()
                self.assertEqual(os.environ, environment, 'Child sanitization must not mutate caller environment')
            result = json.loads(captured.getvalue())
            retained = json.loads(public.read_bytes()) if public.exists() else None
            output_text = output.read_text() if output.exists() else ''
            self.assertNotIn(CANARY, captured.getvalue() + errors.getvalue() + output_text)
            self.assertEqual(result['securityProgramAcceptance'], 'NOT_GRANTED')
            self.assertFalse(result['scheduleActivated'])
            self.assertFalse(result['publicGo'])
            return code, result, commands.calls, retained, output_text, errors.getvalue()

    def test_positive_main_binds_source_image_and_actual_synthetic_native_reports_without_grant(self):
        code, result, calls, retained, output, errors = self.exercise()
        self.assertEqual(code, 0)
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(result['sourceSha'], 'a' * 40)
        self.assertEqual(result['sourceTree'], 'b' * 40)
        self.assertEqual(result['bindings']['runtimeImageId'], 'sha256:' + 'c' * 64)
        self.assertEqual(set(result['execution']['roles']), set(caller.ROLES))
        self.assertTrue(all(row['requestCount'] == 3 for row in result['execution']['roles'].values()))
        self.assertEqual(retained, result)
        self.assertEqual(output, 'published=true\n')
        self.assertEqual(errors, '')
        stages = [(args[-1], env) for args, env in calls if args[:2] == ['bash', caller.RUNTIME]]
        self.assertEqual([stage for stage, env in stages], ['prepare', 'sec03', 'sec04', 'cleanup'])
        self.assertTrue(all(env['COGLATAS_SECURITY_RUNTIME_IMAGE'] == 'sha256:' + 'c' * 64 for stage, env in stages))
        self.assertEqual(stages[-1][1]['COGLATAS_SECURITY_RUNTIME_FAILED'], 'false')

    def test_remote_docker_and_target_budget_overrides_never_reach_child_commands(self):
        code, _, calls, _, _, _ = self.exercise()
        self.assertEqual(code, 0)
        for args, environment in calls:
            if args[0] == 'docker':
                self.assertEqual(args[1], '--host=unix:///var/run/docker.sock')
            self.assertEqual(environment['DOCKER_HOST'], 'unix:///var/run/docker.sock')
            self.assertEqual(environment['COGLATAS_SECURITY_SCHEMATHESIS_LANE'], 'deep')
            for key in ('DOCKER_CONTEXT', 'DOCKER_TLS', 'DOCKER_TLS_VERIFY', 'DOCKER_CERT_PATH',
                        'SECURITY_SCAN_TARGET', 'COGLATAS_SECURITY_SCHEMATHESIS_DEEP_EXAMPLES',
                        'COGLATAS_SECURITY_SCHEMATHESIS_PR_EXAMPLES', 'COGLATAS_SECURITY_SCHEMATHESIS_SEED',
                        'COGLATAS_SECURITY_CI_PROJECT', 'COGLATAS_SECURITY_RUNTIME_STATE_DIR'):
                self.assertNotIn(key, environment)

    def test_bad_admission_is_rejected_before_any_child_command(self):
        cases = [{'COGLATAS_SEC04_RUNNER_ENVIRONMENT': 'local'}, {'RUNNER_OS': 'Windows'},
                 {'GITHUB_EVENT_NAME': 'pull_request'}, {'GITHUB_EVENT_NAME': 'push', 'GITHUB_REF': 'refs/heads/main'},
                 {'GITHUB_RUN_ID': '0'}, {'GITHUB_RUN_ATTEMPT': '01'}, {'COGLATAS_SECURITY_CI_PASSWORD': ''}]
        for changes in cases:
            with self.subTest(changes=tuple(changes)):
                code, result, calls, retained, _, _ = self.exercise(changes=changes)
                self.assertEqual(code, 1)
                self.assertEqual(result['status'], 'FAIL')
                self.assertEqual(result['phase'], 'ADMISSION')
                self.assertEqual(calls, [])
                self.assertEqual(retained, result)

    def test_existing_qualification_push_still_requires_complete_native_evidence(self):
        changes = {'GITHUB_EVENT_NAME': 'push', 'GITHUB_REF': caller.QUALIFICATION_BRANCH}
        code, result, _, _, _, _ = self.exercise(changes=changes)
        self.assertEqual(code, 0)
        self.assertEqual(result['status'], 'PASS')
        self.assertEqual(set(result['execution']['roles']), set(caller.ROLES))
        code, result, _, _, _, _ = self.exercise(changes=changes, fault='missing-native')
        self.assertEqual(code, 1)
        self.assertEqual(result['status'], 'FAIL')
        self.assertEqual(result['phase'], 'NATIVE_VALIDATION')
        self.assertEqual(set(result['stageExitCodes']), {'prepare', 'sec03', 'sec04', 'cleanup'})
        self.assertEqual(set(result['stageExitCodes'].values()), {0})

    def test_extra_cli_arguments_are_not_an_override_channel(self):
        code, result, calls, _, _, _ = self.exercise(arguments=['synthetic-caller', CANARY])
        self.assertEqual(code, 1)
        self.assertEqual(calls, [])
        self.assertEqual(result['failureCategory'], 'CALLER_REJECTED')

    def test_source_tree_and_dirty_source_fail_before_build_or_runtime(self):
        for fault in ('source-mismatch', 'invalid-tree', 'dirty-source'):
            with self.subTest(fault=fault):
                code, result, calls, _, _, _ = self.exercise(fault=fault)
                self.assertEqual(code, 1)
                self.assertEqual(result['status'], 'FAIL')
                self.assertEqual(result['failureCategory'], 'PREPARATION_FAILED')
                self.assertFalse(any(args[0] in ('docker', 'bash') for args, env in calls))

    def test_build_failure_cannot_start_scanners(self):
        code, result, calls, _, _, _ = self.exercise(fault='build')
        self.assertEqual(code, 1)
        self.assertEqual(result['commandExitCodes']['BUILD_IMAGE'], 23)
        self.assertFalse(any(args[:2] == ['bash', caller.RUNTIME] for args, env in calls))

    def test_stage_and_cleanup_failures_preserve_first_codes_and_never_grant_acceptance(self):
        for fault, expected in [('sec04', 17), ('cleanup', 23)]:
            with self.subTest(fault=fault):
                code, result, calls, retained, _, _ = self.exercise(fault=fault)
                self.assertEqual(code, 1)
                self.assertEqual(result['status'], 'FAIL')
                self.assertEqual(result['stageExitCodes'][fault], expected)
                cleanup = [env for args, env in calls if args[:2] == ['bash', caller.RUNTIME] and args[-1] == 'cleanup']
                self.assertEqual(len(cleanup), 1)
                self.assertEqual(cleanup[0]['COGLATAS_SECURITY_RUNTIME_FAILED'], 'true' if fault == 'sec04' else 'false')
                self.assertEqual(retained, result)

    def test_missing_native_or_source_change_cannot_inherit_successful_stage_exits(self):
        for fault in ('missing-native', 'changed-binding', 'dirty-after'):
            with self.subTest(fault=fault):
                code, result, _, retained, _, _ = self.exercise(fault=fault)
                self.assertEqual(code, 1)
                self.assertEqual(result['status'], 'FAIL')
                self.assertEqual(set(result['stageExitCodes']), {'prepare', 'sec03', 'sec04', 'cleanup'})
                self.assertEqual(set(result['stageExitCodes'].values()), {0})
                self.assertEqual(result['phase'], 'SOURCE_UNCHANGED' if fault == 'dirty-after' else 'NATIVE_VALIDATION')
                self.assertEqual(retained, result)

    def test_publication_failure_is_nonzero_without_published_flag_or_private_error(self):
        code, _, _, retained, output, errors = self.exercise(fault='publish')
        self.assertEqual(code, 1)
        self.assertIsNone(retained)
        self.assertEqual(output, '')
        self.assertEqual(errors.strip(), 'PUBLICATION_FAILED')


if __name__ == "__main__":
    unittest.main()
