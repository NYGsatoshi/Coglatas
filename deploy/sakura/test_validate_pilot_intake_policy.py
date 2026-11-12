import copy
from datetime import datetime, timezone
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location("intake_policy", Path(__file__).with_name("validate-pilot-intake-policy.py"))
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)
TENANT = "d9000000-0000-4000-8000-000000000001"
COMMIT = "a" * 40
TREE = "b" * 40
NOW = datetime(2026, 10, 20, 0, 0, tzinfo=timezone.utc)


class IntakeCaptureTests(unittest.TestCase):
    def capture(self):
        settings = dict.fromkeys(policy.SETTINGS_FIELDS)
        settings.update(id=TENANT, tenantId=TENANT, displayName="Synthetic", defaultLocale="en", timeZone="UTC",
                        invitationMode=0, storageQuotaBytes=0, userLimit=0, projectLimit=0, fileUploadLimitBytes=0,
                        notificationSettingsJson="{}", createdAt="2026-10-19T23:59:00Z", featureFlagsJson='{"FileSharing":false,"Calendar":true}')
        return {"version": 1, "capturedAtUtc": "2026-10-19T23:59:00Z", "source": {"commit": COMMIT, "tree": TREE},
                "tenantId": TENANT, "settingsResponse": settings, "featuresResponse": {"tenantId": TENANT, "enabledFeatures": ["Calendar"]}}

    def validate(self, capture):
        return policy.validate(capture, TENANT, COMMIT, TREE, NOW)

    def test_accepts_only_local_contract_without_claiming_live_qualification(self):
        result = self.validate(self.capture())
        self.assertEqual("UNQUALIFIED", result["targetPolicy"])
        self.assertFalse(result["liveStateVerified"])
        self.assertNotIn("settingsResponse", result)
        self.assertNotIn("enabledFeatures", result)
        capture = self.capture()
        capture["settingsResponse"]["featureFlagsJson"] = '{" \\t\\r\\n\\u000b\\fFiLeShArInG ":false,"CALENDAR":true}'
        self.assertEqual("LOCAL_CAPTURE_CONTRACT_VALID", self.validate(capture)["result"])

    def test_rejects_missing_unknown_wrong_typed_or_ambiguous_saved_flags(self):
        for flags in ['{}', '{"FileSharing":true}', '{"FileSharing":"false"}', '{"FileSharing":0}',
                      '{"FileSharing":false,"Calendar":"true"}', '[]', 'null', '{',
                      '{"FileSharing":false,"filesharing":true}', '{"FileSharing":true,"FileSharing":false}',
                      '{"File\\u017fharing":false}', '{"FileSharing":false,"File\\u017fharing":true}',
                      '{"\\u001cFileSharing\\u001c":false}']:
            with self.subTest(flags=flags):
                capture = self.capture()
                capture["settingsResponse"]["featureFlagsJson"] = flags
                with self.assertRaises(policy.InvalidCapture):
                    self.validate(capture)

    def test_rejects_effective_enablement_or_invalid_feature_arrays(self):
        for enabled in [["FileSharing"], [" filesharing "], ["File\u017fharing"], ["Calendar", "calendar"], [False], None, "Calendar"]:
            with self.subTest(enabled=enabled):
                capture = self.capture()
                capture["featuresResponse"]["enabledFeatures"] = enabled
                with self.assertRaises(policy.InvalidCapture):
                    self.validate(capture)

    def test_rejects_unknown_shapes_versions_and_cross_tenant_or_source_capture(self):
        base = self.capture()
        examples = [None, [], {**base, "unknown": True}, {**base, "version": True}, {**base, "version": 2},
                    {**base, "tenantId": "d9000000-0000-4000-8000-000000000002"}, {**base, "source": {"commit": "c" * 40, "tree": TREE}}]
        for response in ("settingsResponse", "featuresResponse"):
            capture = copy.deepcopy(base)
            capture[response]["tenantId"] = "d9000000-0000-4000-8000-000000000002"
            examples.append(capture)
            examples.append({**base, response: {"data": base[response]}})
        for capture in examples:
            with self.subTest(capture=capture), self.assertRaises(policy.InvalidCapture):
                self.validate(capture)

    def test_rejects_stale_future_ambiguous_or_invalid_time(self):
        for timestamp in ["2026-10-19T23:44:59Z", "2026-10-20T00:00:01Z", "2026-10-20T00:00:00+00:00", "2026-99-19T23:59:00Z", "2026-10-19T23:59:00Z\n"]:
            with self.subTest(timestamp=timestamp), self.assertRaises(policy.InvalidCapture):
                self.validate({**self.capture(), "capturedAtUtc": timestamp})

    def test_rejects_raw_duplicate_properties_and_invalid_expected_identity(self):
        with self.assertRaises(policy.InvalidCapture):
            policy.parse_json('{"version":1,"version":1}')
        for source in [COMMIT + "\n", COMMIT.upper(), "unknown"]:
            with self.subTest(source=source), self.assertRaises(policy.InvalidCapture):
                policy.validate(self.capture(), TENANT, source, TREE, NOW)

    def test_rejects_wrong_typed_dto_fields(self):
        for key, value in [("id", None), ("createdAt", "invalid"), ("storageQuotaBytes", False), ("invitationMode", "AdminOnly"),
                           ("displayName", []), ("featureFlagsJson", {"FileSharing": False})]:
            with self.subTest(key=key), self.assertRaises(policy.InvalidCapture):
                capture = self.capture()
                capture["settingsResponse"][key] = value
                self.validate(capture)


class CaptureFileCliTests(unittest.TestCase):
    capture = IntakeCaptureTests.capture
    def run_cli(self, path):
        return subprocess.run([
            sys.executable, str(Path(policy.__file__)), str(path),
            "--tenant-id", TENANT, "--source-commit", COMMIT, "--source-tree", TREE,
            "--now-utc", "2026-10-20T00:00:00Z",
        ], capture_output=True, text=True, timeout=2, check=False)

    def test_fifo_rejects_without_waiting_for_a_writer(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "capture.json"
            os.mkfifo(path)
            result = self.run_cli(path)
            self.assertEqual(1, result.returncode)
            self.assertEqual("", result.stdout)
            self.assertIn("capture rejected", result.stderr)

    def test_symlink_rejects_instead_of_reading_the_target(self):
        with tempfile.TemporaryDirectory() as directory:
            target = Path(directory) / "target.json"
            target.write_text(json.dumps(self.capture()), encoding="utf-8")
            path = Path(directory) / "capture.json"
            path.symlink_to(target)
            result = self.run_cli(path)
            self.assertEqual(1, result.returncode)
            self.assertEqual("", result.stdout)
            self.assertIn("capture rejected", result.stderr)

    def test_small_and_exact_limit_regular_files_keep_local_only_result(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "capture.json"
            text = json.dumps(self.capture())
            for size in (len(text), policy.MAX_CAPTURE_BYTES):
                with self.subTest(size=size):
                    path.write_text(text.ljust(size), encoding="utf-8")
                    result = self.run_cli(path)
                    self.assertEqual(0, result.returncode)
                    self.assertEqual("", result.stderr)
                    receipt = json.loads(result.stdout)
                    self.assertEqual("LOCAL_CAPTURE_CONTRACT_VALID", receipt["result"])
                    self.assertEqual("UNQUALIFIED", receipt["targetPolicy"])
                    self.assertFalse(receipt["liveStateVerified"])
                    self.assertFalse(receipt["networkAccess"])
                    self.assertFalse(receipt["mutations"])

    def test_unavailable_directory_oversize_and_invalid_utf8_fail_privately(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "capture.json"
            for content in (b" " * (policy.MAX_CAPTURE_BYTES + 1), b"\xffprivate-capture"):
                path.write_bytes(content)
                result = self.run_cli(path)
                self.assertEqual(1, result.returncode)
                self.assertEqual("", result.stdout)
                self.assertNotIn("private-capture", result.stderr)
            for rejected in (Path(directory), path.with_name("missing.json")):
                result = self.run_cli(rejected)
                self.assertEqual(1, result.returncode)
                self.assertEqual("", result.stdout)

    def test_open_time_replacement_or_growth_rejects_without_reading(self):
        original_open = os.open
        for replacement in ("fifo", "symlink", "inode", "growth"):
            with self.subTest(replacement=replacement), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "capture.json"
                path.write_text(json.dumps(self.capture()), encoding="utf-8")

                def replace_then_open(name, flags):
                    if replacement == "growth":
                        with path.open("ab") as stream:
                            stream.write(b" ")
                    else:
                        saved = path.with_name("original.json")
                        path.rename(saved)
                        if replacement == "fifo":
                            os.mkfifo(path)
                        elif replacement == "symlink":
                            path.symlink_to(saved)
                        else:
                            path.write_bytes(saved.read_bytes())
                    self.assertTrue(flags & os.O_NONBLOCK)
                    self.assertTrue(flags & os.O_NOFOLLOW)
                    return original_open(name, flags)

                with patch.object(policy.os, "open", side_effect=replace_then_open), \
                        patch.object(policy.os, "read") as read:
                    with self.assertRaises((policy.InvalidCapture, OSError)):
                        policy.read_capture(path)
                    read.assert_not_called()

    def test_read_time_growth_and_shrink_reject_with_bounded_reads_and_closed_descriptor(self):
        original_read = os.read
        original_close = os.close
        for mutation in ("growth", "shrink"):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "capture.json"
                path.write_text(json.dumps(self.capture()), encoding="utf-8")
                bytes_read = 0
                descriptors = []

                def mutate_then_read(descriptor, amount):
                    nonlocal bytes_read
                    if not descriptors:
                        descriptors.append(descriptor)
                        if mutation == "growth":
                            with path.open("ab") as stream:
                                stream.write(b" " * (policy.MAX_CAPTURE_BYTES * 2))
                        else:
                            path.write_bytes(b"")
                    chunk = original_read(descriptor, amount)
                    bytes_read += len(chunk)
                    self.assertLessEqual(bytes_read, policy.MAX_CAPTURE_BYTES + 1)
                    return chunk

                with patch.object(policy.os, "read", side_effect=mutate_then_read), \
                        patch.object(policy.os, "close", wraps=original_close) as close:
                    with self.assertRaises(policy.InvalidCapture):
                        policy.read_capture(path)
                    close.assert_called_once_with(descriptors[0])


if __name__ == "__main__":
    unittest.main()
