#!/usr/bin/env python3
"""Validate local API captures only; never connect to or modify a deployment."""

import argparse
from datetime import datetime, timezone
import json
import os
from pathlib import Path
import re
import stat
import sys
from uuid import UUID


class InvalidCapture(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise InvalidCapture(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON property.")
        result[key] = value
    return result


def parse_json(text):
    try:
        return json.loads(text, object_pairs_hook=unique_object, parse_constant=lambda _: require(False, "Non-finite JSON value."))
    except (json.JSONDecodeError, TypeError, RecursionError):
        raise InvalidCapture("Malformed JSON.") from None


MAX_CAPTURE_BYTES = 262144


def read_capture(path):
    def bounded_regular(info):
        return stat.S_ISREG(info.st_mode) and 0 <= info.st_size <= MAX_CAPTURE_BYTES

    def unchanged(before, after):
        return (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns, before.st_ctime_ns) == (
            after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns, after.st_ctime_ns)

    initial = path.lstat()
    require(bounded_regular(initial), "Capture must be a regular file within the 256 KiB limit.")
    # A regular path can become a FIFO or symlink after lstat. Inspect the open
    # descriptor before reading; nonblocking/no-follow prevent that race hanging.
    descriptor = os.open(path, os.O_RDONLY | os.O_NONBLOCK | os.O_NOFOLLOW)
    try:
        opened = os.fstat(descriptor)
        require(bounded_regular(opened) and unchanged(initial, opened), "Capture changed before reading.")
        content = bytearray()
        while len(content) <= MAX_CAPTURE_BYTES:
            chunk = os.read(descriptor, min(65536, MAX_CAPTURE_BYTES + 1 - len(content)))
            if not chunk:
                break
            content.extend(chunk)
        final = os.fstat(descriptor)
        require(len(content) <= MAX_CAPTURE_BYTES and len(content) == opened.st_size and
                bounded_regular(final) and unchanged(opened, final), "Capture changed while reading.")
        return parse_json(content.decode("utf-8"))
    finally:
        os.close(descriptor)


def object_keys(value, expected, label):
    require(type(value) is dict and set(value) == set(expected), f"Unexpected or missing {label} fields.")


def identity(value):
    require(type(value) is str, "Invalid Tenant identity.")
    try:
        parsed = UUID(value)
    except ValueError:
        raise InvalidCapture("Invalid Tenant identity.") from None
    require(parsed.int != 0 and str(parsed) == value, "Tenant identity must be a canonical nonempty UUID.")
    return value


def utc_time(value):
    require(type(value) is str and re.fullmatch(r"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,6})?Z", value),
            "Capture time must be an explicit UTC instant ending in Z.")
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        raise InvalidCapture("Invalid UTC instant.") from None


def normalized_key(value):
    # The runtime uses .NET OrdinalIgnoreCase, not Unicode case folding. A
    # conservative ASCII subset avoids treating confusable keys as FileSharing.
    # Python's default strip also removes U+001C-U+001F, unlike .NET Trim.
    whitespace = " \t\r\n\v\f"
    require(type(value) is str and value.isascii() and bool(value.strip(whitespace)), "Feature keys must be nonempty ASCII strings.")
    return value.strip(whitespace).lower()


SETTINGS_FIELDS = {
    "id", "tenantId", "displayName", "logoFileId", "themeColor", "defaultLocale", "timeZone",
    "invitationMode", "storageQuotaBytes", "userLimit", "projectLimit", "fileUploadLimitBytes",
    "featureFlagsJson", "notificationSettingsJson", "createdAt", "updatedAt",
}


def settings_types(settings):
    identity(settings["id"])
    if settings["logoFileId"] is not None:
        identity(settings["logoFileId"])
    require(settings["themeColor"] is None or type(settings["themeColor"]) is str, "Invalid settings themeColor type.")
    for key in ("displayName", "defaultLocale", "timeZone", "notificationSettingsJson", "createdAt"):
        require(type(settings[key]) is str and bool(settings[key]), "Invalid settings string field.")
    require(settings["updatedAt"] is None or type(settings["updatedAt"]) is str, "Invalid settings updatedAt type.")
    require(type(settings["invitationMode"]) is int and settings["invitationMode"] in range(4), "Invalid settings invitationMode.")
    for key in ("storageQuotaBytes", "userLimit", "projectLimit", "fileUploadLimitBytes"):
        require(type(settings[key]) is int and settings[key] >= 0, "Invalid settings quota type.")
    for key in ("createdAt", "updatedAt"):
        if settings[key] is not None:
            try:
                instant = datetime.fromisoformat(settings[key].replace("Z", "+00:00"))
                require(instant.utcoffset() is not None and instant.utcoffset().total_seconds() == 0, "Settings timestamp must be UTC.")
            except ValueError:
                raise InvalidCapture("Invalid settings timestamp.") from None


def validate(capture, tenant_id, source_commit, source_tree, now, maximum_age_minutes=15):
    identity(tenant_id)
    require(type(maximum_age_minutes) is int and 1 <= maximum_age_minutes <= 60, "Maximum age must be 1 to 60 minutes.")
    for value in (source_commit, source_tree):
        require(type(value) is str and re.fullmatch(r"[0-9a-f]{40}", value), "Expected source identities must be lowercase Git SHA-1 values.")
    object_keys(capture, {"version", "capturedAtUtc", "source", "tenantId", "settingsResponse", "featuresResponse"}, "capture")
    require(type(capture["version"]) is int and capture["version"] == 1, "Unsupported input version.")
    object_keys(capture["source"], {"commit", "tree"}, "source")
    require(capture["source"] == {"commit": source_commit, "tree": source_tree}, "Source identity mismatch.")
    require(identity(capture["tenantId"]) == tenant_id, "Capture Tenant mismatch.")
    recorded = utc_time(capture["capturedAtUtc"])
    age = (now - recorded).total_seconds()
    require(0 <= age <= maximum_age_minutes * 60, "Capture is expired or in the future.")

    settings = capture["settingsResponse"]
    features = capture["featuresResponse"]
    # These are the existing direct TenantSettingsResponse/TenantFeaturesResponse
    # DTOs, not an invented envelope or an environment-variable feature switch.
    object_keys(settings, SETTINGS_FIELDS, "settings response")
    object_keys(features, {"tenantId", "enabledFeatures"}, "features response")
    settings_types(settings)
    require(identity(settings["tenantId"]) == tenant_id and identity(features["tenantId"]) == tenant_id,
            "Response Tenant mismatch.")
    require(type(settings["featureFlagsJson"]) is str, "Saved featureFlagsJson must be a JSON string.")
    overrides = parse_json(settings["featureFlagsJson"])
    require(type(overrides) is dict, "Saved feature overrides must be an object.")
    normalized = {}
    for key, enabled in overrides.items():
        normalized_name = normalized_key(key)
        require(normalized_name not in normalized, "Ambiguous normalized feature override.")
        require(type(enabled) is bool, "Every saved feature override must be boolean.")
        normalized[normalized_name] = enabled
    require(normalized.get("filesharing") is False, "Saved FileSharing=false is required.")
    enabled = features["enabledFeatures"]
    require(type(enabled) is list, "Effective enabledFeatures must be an array.")
    keys = [normalized_key(key) for key in enabled]
    require(len(keys) == len(set(keys)), "Ambiguous effective feature keys.")
    require("filesharing" not in keys, "Effective FileSharing must be disabled.")
    return {
        "result": "LOCAL_CAPTURE_CONTRACT_VALID",
        "version": 1,
        "sourceCommit": source_commit,
        "sourceTree": source_tree,
        "targetPolicy": "UNQUALIFIED",
        "liveStateVerified": False,
        "networkAccess": False,
        "mutations": False,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("capture", type=Path)
    parser.add_argument("--tenant-id", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--source-tree", required=True)
    parser.add_argument("--now-utc", help="Explicit validation instant; default is this machine's UTC clock.")
    parser.add_argument("--maximum-age-minutes", type=int, default=15)
    arguments = parser.parse_args()
    try:
        capture = read_capture(arguments.capture)
        now = utc_time(arguments.now_utc) if arguments.now_utc else datetime.now(timezone.utc)
        result = validate(capture, arguments.tenant_id, arguments.source_commit, arguments.source_tree, now,
                          arguments.maximum_age_minutes)
    except (OSError, UnicodeError):
        print("Intake policy capture rejected: file unavailable or invalid UTF-8.", file=sys.stderr)
        return 1
    except InvalidCapture as error:
        print(f"Intake policy capture rejected: {error}", file=sys.stderr)
        return 1
    print(json.dumps(result, separators=(",", ":")))
    return 0


if __name__ == "__main__":
    sys.exit(main())
