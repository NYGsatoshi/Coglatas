"""Closed tests for the single logical attempt and strict failure publication."""
import copy
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SPEC = importlib.util.spec_from_file_location(
    "logical_recovery_ci", ROOT / "scripts/ci/run-synthetic-logical-recovery-ci.py")
caller = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(caller)
SOURCE, TARGET = "a" * 64, "b" * 64
CANARY = b"PRIVATE_SYNTHETIC_ROW_COOKIE_KEY_MUST_NOT_APPEAR"


def complete():
    commands = caller.logical_commands(SOURCE, TARGET)
    return {"resourceIds": [SOURCE, TARGET],
            "commands": {key: {"argv": value, "exitCode": 0, "timedOut": False}
                         for key, value in commands.items()},
            "dumpSha256": caller.support.sha(b"custom archive"),
            "comparisons": caller.strict_comparisons(
                {name: b"identical" for name in caller.COMPARISONS},
                {name: b"identical" for name in caller.COMPARISONS}),
            "controls": caller.CONTROLS.copy(), "cleanupState": "VERIFIED",
            "admittedContainers": 2, "stoppedContainers": 2}


class LogicalRecoveryContracts(unittest.TestCase):
    def test_complete_bounded_database_control(self):
        self.assertTrue(caller.candidate_complete(complete()))

    def test_every_strict_family_is_required(self):
        for name in caller.COMPARISONS:
            with self.subTest(name=name):
                result = complete()
                del result["comparisons"][name]
                self.assertFalse(caller.candidate_complete(result))

    def test_raw_bytes_are_never_normalized(self):
        before = {name: b"CHECK ((id > 0))\n" for name in caller.COMPARISONS}
        for name in caller.COMPARISONS:
            after = before.copy()
            after[name] = b"CHECK (id > 0)\n"
            observed = caller.strict_comparisons(before, after)
            self.assertIs(observed[name]["exactBytesEqual"], False)
            result = complete()
            result["comparisons"] = observed
            self.assertFalse(caller.candidate_complete(result))

    def test_unknown_missing_and_nonbyte_inputs_rejected(self):
        original = {name: b"unchanged" for name in caller.COMPARISONS}
        for value in ({}, {**original, "unexpected": CANARY},
                      {**original, "schema.sql": "text instead of bytes"}):
            with self.assertRaises(caller.support.Rejected):
                caller.strict_comparisons(original, value)

    def test_false_boolean_inconsistent_digests_and_extra_fields_fail(self):
        for mutation in (
            lambda item: item.update(exactBytesEqual=1),
            lambda item: item.update(exactBytesEqual=False),
            lambda item: item.update(restoredSha256="c" * 64),
            lambda item: item.update(sourceSha256="not-a-digest"),
            lambda item: item.update(rawBody=CANARY.decode()),
        ):
            result = complete()
            mutation(result["comparisons"]["schema.sql"])
            self.assertFalse(caller.candidate_complete(result))

    def test_nonzero_timeout_missing_and_boolean_exit_never_pass(self):
        for name in ("dump", "restore"):
            for exit_code in (1, -9, None, False):
                result = complete()
                result["commands"][name]["exitCode"] = exit_code
                self.assertFalse(caller.candidate_complete(result))
            result = complete()
            result["commands"][name]["timedOut"] = True
            self.assertFalse(caller.candidate_complete(result))

    def test_each_positive_and_cleanup_control_is_required(self):
        for key in caller.CONTROLS:
            result = complete()
            del result["controls"][key]
            self.assertFalse(caller.candidate_complete(result))
        for key, value in (("cleanupState", "INCOMPLETE"), ("admittedContainers", 1),
                           ("stoppedContainers", 1), ("admittedContainers", True),
                           ("dumpSha256", None), ("resourceIds", []),
                           ("resourceIds", [SOURCE]), ("resourceIds", [SOURCE, TARGET, "c" * 64])):
            result = complete()
            result[key] = value
            self.assertFalse(caller.candidate_complete(result))

    def test_exact_documented_commands_and_fresh_distinct_targets(self):
        commands = caller.logical_commands(SOURCE, TARGET)
        self.assertEqual(commands, {
            "dump": ["docker", "--host=unix:///var/run/docker.sock", "exec", SOURCE,
                     "pg_dump", "-U", "postgres", "-d", "recovery", "--format=custom",
                     "--no-owner", "--no-privileges"],
            "restore": ["docker", "--host=unix:///var/run/docker.sock", "exec", "-i", TARGET,
                        "pg_restore", "-U", "postgres", "-d", "recovery", "--no-owner",
                        "--no-privileges", "--exit-on-error"]})
        for source, target in ((SOURCE, SOURCE), ("external-host", TARGET), (SOURCE, "../target")):
            with self.assertRaises(caller.support.Rejected):
                caller.logical_commands(source, target)
        for name in commands:
            result = complete()
            result["commands"][name]["argv"].append("--disable-triggers")
            self.assertFalse(caller.candidate_complete(result))

    def run_transfer(self, codes):
        calls = []
        def run(args, **kwargs):
            calls.append((args, kwargs))
            index = len(calls) - 1
            return subprocess.CompletedProcess(args, codes[index], CANARY, CANARY)
        with tempfile.TemporaryDirectory() as directory:
            private = Path(directory)
            result = {}
            caller.transfer_once(SOURCE, TARGET, private, result, {}, runner=run)
            self.assertNotIn(CANARY.decode(), json.dumps(result))
            return result, calls

    def test_single_dump_and_restore_bind_exact_archive_bytes(self):
        result, calls = self.run_transfer([0, 0])
        self.assertEqual(len(calls), 2)
        self.assertIsNone(calls[0][1]["input"])
        self.assertTrue(calls[1][1]["input"] == CANARY)
        self.assertEqual(result["dumpSha256"], caller.support.sha(CANARY))

    def test_dump_failure_preserves_actual_nonzero_and_skips_restore(self):
        result, calls = self.run_transfer([17])
        self.assertEqual(len(calls), 1)
        self.assertEqual(result["commands"]["dump"]["exitCode"], 17)
        self.assertIsNone(result["commands"]["restore"]["exitCode"])

    def test_restore_failure_preserved_without_retry(self):
        result, calls = self.run_transfer([0, 23])
        self.assertEqual(len(calls), 2)
        self.assertEqual(result["commands"]["restore"]["exitCode"], 23)
        self.assertFalse(caller.candidate_complete(result))

    def test_timeout_does_not_retry_or_invent_exit_code(self):
        calls = []
        def timeout(args, **kwargs):
            calls.append(args)
            raise subprocess.TimeoutExpired(args, 120, output=CANARY, stderr=CANARY)
        with tempfile.TemporaryDirectory() as directory:
            result = {}
            caller.transfer_once(SOURCE, TARGET, Path(directory), result, {}, runner=timeout)
        self.assertEqual(len(calls), 1)
        self.assertTrue(result["commands"]["dump"]["timedOut"])
        self.assertIsNone(result["commands"]["dump"]["exitCode"])
        self.assertNotIn(CANARY.decode(), json.dumps(result))

    def test_same_attempt_cannot_be_executed_twice(self):
        result, _ = self.run_transfer([0, 23])
        def unexpected(*args, **kwargs):
            self.fail("A second acceptance command was executed")
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(caller.support.Rejected):
                caller.transfer_once(SOURCE, TARGET, Path(directory), result, {}, runner=unexpected)

    def test_first_failed_publication_cannot_be_overwritten(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.json"
            first = {"status": "FAIL", "restoreExitCode": 23}
            caller.support.publish_once(path, first)
            retained = path.read_bytes()
            with self.assertRaises(FileExistsError):
                caller.support.publish_once(path, {"status": "PASS"})
            self.assertTrue(path.read_bytes() == retained)
            self.assertEqual(json.loads(retained), first)

    def test_public_comparisons_never_contain_raw_snapshot_values(self):
        values = {name: CANARY for name in caller.COMPARISONS}
        observed = caller.strict_comparisons(values, values)
        self.assertNotIn(CANARY.decode(), json.dumps(observed))
        self.assertTrue(caller.comparisons_pass(observed))


if __name__ == "__main__":
    unittest.main()
