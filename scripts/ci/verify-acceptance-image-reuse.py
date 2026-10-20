#!/usr/bin/env python3
"""Require all four bounded producer-image continuity receipts for a reuse job."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import subprocess
import sys
from pathlib import Path

RECEIPT_DIRECTORY = Path("artifacts/acceptance-image-reuse")
RECEIPT_NAMES = tuple(name + "-image-reuse.json" for name in ("mvp-a-authz", "mbj01", "mbj02", "mbj03"))
FIELDS = {"schemaVersion", "expectedProducerImageId", "tagAndAppContainerMatched", "scope"}


def unique_object(pairs):
    result = {}
    for name, value in pairs:
        if name in result:
            raise ValueError("Duplicate receipt field.")
        result[name] = value
    return result


def receipt_directory() -> Path:
    workspace = Path.cwd().resolve()
    directory = workspace / RECEIPT_DIRECTORY
    for candidate in (workspace / "artifacts", directory):
        if candidate.is_symlink() or candidate.resolve() != candidate:
            raise ValueError("Acceptance receipt directory must stay within the workspace without redirected parents.")
    return directory


def verify_receipts(expected_id: str, engine_id: str, binding_sha: str | None) -> None:
    directory = receipt_directory()
    if {path.name for path in directory.iterdir()} != set(RECEIPT_NAMES):
        raise ValueError("Acceptance receipt directory must contain exactly the four canonical receipt files.")
    for name in RECEIPT_NAMES:
        path = directory / name
        try:
            metadata = path.stat()
            if path.is_symlink() or not stat.S_ISREG(metadata.st_mode) or metadata.st_size > 2048:
                raise ValueError("Receipt must be a bounded regular file.")
            receipt = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_object)
            fields = FIELDS if binding_sha is None else FIELDS | {"engineImageId", "bindingSha256"}
            if (not isinstance(receipt, dict) or set(receipt) != fields
                    or type(receipt["schemaVersion"]) is not int or receipt["schemaVersion"] != (1 if binding_sha is None else 2)
                    or receipt["expectedProducerImageId"] != expected_id
                    or receipt["tagAndAppContainerMatched"] is not True
                    or receipt["scope"] != "within-this-acceptance-wrapper"):
                raise ValueError("Receipt contract mismatch.")
            if binding_sha is not None and (receipt["engineImageId"] != engine_id or receipt["bindingSha256"] != binding_sha):
                raise ValueError("Receipt engine binding mismatch.")
        except (OSError, ValueError, UnicodeError) as error:
            raise ValueError(f"Missing or invalid acceptance image receipt: {name}") from error


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reset", action="store_true", help="Remove only the four prior reuse receipts before this job's lanes.")
    arguments = parser.parse_args()
    expected_id = os.environ.get("COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID", "")
    engine_id = os.environ.get("COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID", expected_id)
    if (os.environ.get("COGLATAS_REUSE_PREBUILT_APP_IMAGE") != "1"
            or re.fullmatch(r"sha256:[0-9a-f]{64}", expected_id) is None):
        print("Acceptance receipt validation requires the restored producer image ID and reuse mode.", file=sys.stderr)
        return 1
    try:
        binding_sha = None
        if engine_id != expected_id:
            require_binding = subprocess.run([
                sys.executable, str(Path(__file__).with_name("runtime_image_identity.py")), "verify",
                "--archive", os.environ.get("COGLATAS_REUSE_PREBUILT_APP_IMAGE_ARCHIVE", ""),
                "--source-sha", os.environ.get("COGLATAS_REUSE_PREBUILT_APP_SOURCE_SHA", ""),
                "--image-name", os.environ.get("COGLATAS_APP_IMAGE", ""),
                "--producer-id", expected_id, "--engine-id", engine_id,
                "--binding", os.environ.get("COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING", ""),
                "--dotnet-stamp", os.environ.get("COGLATAS_REUSE_PREBUILT_DOTNET_STAMP", ""),
            ], capture_output=True, check=False)
            if require_binding.returncode != 0:
                raise ValueError("Verified runtime image binding is unavailable.")
            binding_sha = hashlib.sha256(Path(os.environ["COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING"]).read_bytes()).hexdigest()
        if arguments.reset:
            directory = receipt_directory()
            for name in RECEIPT_NAMES:
                (directory / name).unlink(missing_ok=True)
            print("Cleared the four prior acceptance image receipts before reuse execution.")
        else:
            verify_receipts(expected_id, engine_id, binding_sha)
            print("Verified all four current producer-image continuity receipts.")
    except (OSError, ValueError) as error:
        print(str(error), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
