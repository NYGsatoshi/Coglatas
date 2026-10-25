#!/usr/bin/env python3
"""Allowlisted, read-only Docker migration setup snapshot.

Never serialize effective Compose configuration, image references, command
output, environment values, exception strings, raw URLs or credentials.
This is a diagnostic only; it cannot make a failed Functional owner pass.
"""
from __future__ import annotations

import json
import re
import subprocess
import sys
from typing import Callable

MAX_CONFIG_BYTES = 1024 * 1024
PINNED_IMAGE = re.compile(r"^\S+@sha256:[0-9a-f]{64}$")

def _run(argv: list[str], capture: bool = False) -> tuple[bool, bytes]:
    try:
        result = subprocess.run(
            argv, stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE if capture else subprocess.DEVNULL,
            stderr=subprocess.DEVNULL, check=False, timeout=4,
        )
    except (OSError, subprocess.TimeoutExpired, ValueError):
        return False, b""
    return result.returncode == 0, result.stdout if capture else b""

def unknown() -> dict[str, str | int]:
    return {
        "schemaVersion": 1,
        "phase": "apply-migrations",
        "daemonReachable": "unknown",
        "migrateImagePinned": "unknown",
        "migrateImageCache": "unknown",
    }

def observe(compose: list[str], run: Callable = _run) -> dict[str, str | int]:
    output = unknown()
    # Only the harness's own statically chosen Compose executables are admitted.
    if not (compose[:2] == ["docker", "compose"] or compose[:1] == ["docker-compose"]):
        return output
    available, _ = run(["docker", "info"])
    output["daemonReachable"] = "yes" if available else "not_reachable"
    supported, payload = run([*compose, "config", "--format", "json"], capture=True)
    if not supported or not isinstance(payload, bytes) or len(payload) > MAX_CONFIG_BYTES:
        return output
    try:
        document = json.loads(payload.decode("utf-8"))
        image = document["services"]["migrate"]["image"]
    except (UnicodeError, json.JSONDecodeError, KeyError, TypeError, AttributeError):
        return output
    if not isinstance(image, str) or not image or len(image) > 512:
        return output
    pinned = PINNED_IMAGE.fullmatch(image) is not None
    output["migrateImagePinned"] = "yes" if pinned else "no"
    if available and pinned:
        in_cache, _ = run(["docker", "image", "inspect", image])
        # Inspect errors are ambiguous (absent image, daemon or proxy error).
        output["migrateImageCache"] = "verified" if in_cache else "not_verified"
    return output

def main() -> None:
    try:
        snapshot = observe(sys.argv[1:])
    except Exception:
        # Never serialize arbitrary exception content. Original setup failure
        # remains the controlling failure even if this auxiliary probe fails.
        snapshot = unknown()
    print(json.dumps(snapshot, sort_keys=True, separators=(",", ":")))

if __name__ == "__main__":
    main()
