#!/usr/bin/env python3
from __future__ import annotations

import argparse
import datetime as dt
import json
import sys
import urllib.error
import urllib.request
from pathlib import Path

from common import (
    PerformanceContractError,
    fixture_hash,
    load_json,
    load_profile,
    performance_opener,
    validate_fixture_evidence,
    validate_target,
    write_json_atomic,
)


def request_ok(url: str, tenant_slug: str, timeout_seconds: float) -> tuple[int, str]:
    request = urllib.request.Request(url, headers={"X-Tenant-Slug": tenant_slug})
    try:
        with performance_opener().open(request, timeout=timeout_seconds) as response:
            body = response.read(4096).decode("utf-8", errors="replace")
            return response.status, body
    except urllib.error.HTTPError as exc:
        return exc.code, exc.read(4096).decode("utf-8", errors="replace")


def main() -> int:
    parser = argparse.ArgumentParser(description="Fail-closed PERF-02 environment preflight.")
    parser.add_argument("--base-url", required=True)
    parser.add_argument("--profile", choices=("small", "medium", "large"), required=True)
    parser.add_argument("--fixture-evidence", type=Path, required=True)
    parser.add_argument("--manifest", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--timeout-seconds", type=float, default=10.0)
    args = parser.parse_args()

    try:
        base_url = validate_target(args.base_url)
        manifest_path = args.manifest
        _, profile = load_profile(args.profile, manifest_path)
        evidence = validate_fixture_evidence(
            load_json(args.fixture_evidence),
            args.profile,
            manifest_path,
        )
        identities = evidence["identities"]
        status, body = request_ok(
            f"{base_url}/health/ready",
            identities["tenantSlug"],
            args.timeout_seconds,
        )
        if status != 200:
            raise PerformanceContractError(
                f"application health precheck failed with HTTP {status}: {body[:200]!r}"
            )

        output = {
            "schemaVersion": 1,
            "phase": "preflight",
            "passed": True,
            "checkedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
            "target": base_url,
            "profile": args.profile,
            "seed": profile["seed"],
            "fixtureHash": fixture_hash(args.profile, manifest_path),
            "fixtureEvidence": str(args.fixture_evidence),
            "health": {"status": status, "path": "/health/ready"},
        }
        write_json_atomic(args.output, output)
        print(json.dumps(output, sort_keys=True))
        return 0
    except (PerformanceContractError, OSError, ValueError) as exc:
        print(f"PERF-02 preflight failed: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
