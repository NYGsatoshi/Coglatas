import copy
from datetime import datetime, timezone
import importlib.util
from pathlib import Path
import unittest


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
        capture["settingsResponse"]["featureFlagsJson"] = '{" filesharing ":false,"CALENDAR":true}'
        self.assertEqual("LOCAL_CAPTURE_CONTRACT_VALID", self.validate(capture)["result"])

    def test_rejects_missing_unknown_wrong_typed_or_ambiguous_saved_flags(self):
        for flags in ['{}', '{"FileSharing":true}', '{"FileSharing":"false"}', '{"FileSharing":0}',
                      '{"FileSharing":false,"Calendar":"true"}', '[]', 'null', '{',
                      '{"FileSharing":false,"filesharing":true}', '{"FileSharing":true,"FileSharing":false}',
                      '{"File\\u017fharing":false}', '{"FileSharing":false,"File\\u017fharing":true}']:
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


if __name__ == "__main__":
    unittest.main()
