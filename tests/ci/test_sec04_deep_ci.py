"""Prove that topology/scanner failures cannot qualify the deep caller."""
from datetime import datetime, timedelta, timezone
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


if __name__ == "__main__":
    unittest.main()
