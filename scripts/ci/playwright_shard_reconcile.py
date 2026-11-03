#!/usr/bin/env python3
"""Strict, nonpublishing Playwright two-shard receipt reconciliation.

This is a preparatory testable contract, NOT a CI shard launcher, a browser
coverage check, or an artifact API attestation. Host actions will separately
verify the GitHub producer Artifact ID and exact tested merged commit.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import stat
import sys
from pathlib import Path

SHA = re.compile(r"[0-9a-f]{40}\Z")
TEST_ID = re.compile(r"[0-9a-f]{64}\Z")
ROSTER_SCHEMA = "coglatas.playwright-roster.v1"
SHARD_SCHEMA = "coglatas.playwright-shard.v1"
OUTPUT_SCHEMA = "coglatas.playwright-shard-aggregate.v1"
SUITE = "angular-static"
MAX_BYTES = 512 * 1024
MAX_TESTS = 5000


class InvalidEvidence(ValueError):
    """Evidence is untrusted, incomplete, inconsistent or outside its bounds."""


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise InvalidEvidence(reason)


def unique_pairs(pairs):
    """Reject JSON objects with ambiguous or repeated keys."""
    row = {}
    for key, value in pairs:
        require(key not in row, "DUPLICATE_JSON_KEY")
        row[key] = value
    return row


def read_json(path: Path):
    """Read a regular file only, bounded and without symlink following."""
    flags = os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0)
    fd = os.open(path, flags)
    try:
        metadata = os.fstat(fd)
        require(stat.S_ISREG(metadata.st_mode) and 0 < metadata.st_size <= MAX_BYTES,
                "INVALID_EVIDENCE_FILE")
        chunks = []
        size = 0
        while True:
            chunk = os.read(fd, min(65536, MAX_BYTES + 1 - size))
            if not chunk:
                break
            chunks.append(chunk)
            size += len(chunk)
            require(size <= MAX_BYTES, "EVIDENCE_TOO_LARGE")
        require(os.fstat(fd).st_size == size, "EVIDENCE_FILE_CHANGED")
        return json.loads(b"".join(chunks).decode("utf-8"), object_pairs_hook=unique_pairs)
    finally:
        os.close(fd)


def fields(obj, names, reason):
    require(type(obj) is dict and set(obj) == set(names), reason)


def integer(value, reason):
    require(type(value) is int and 0 < value < 1_000_000_000_000_000_000, reason)


def sorted_ids(entries, kind):
    require(type(entries) is list and 0 < len(entries) <= MAX_TESTS, kind + "_COUNT")
    previous = ""
    for entry in entries:
        fields(entry, {"id", "status"} if kind == "ROSTER" else {"id", "status", "retries"},
               kind + "_FIELDS")
        identity = entry["id"]
        require(type(identity) is str and TEST_ID.fullmatch(identity) is not None,
                kind + "_IDENTITY")
        require(identity > previous, kind + "_ORDER_OR_DUPLICATE")
        previous = identity
        require(entry["status"] in ("passed", "skipped") and type(entry["status"]) is str,
                kind + "_STATUS")
        if kind != "ROSTER":
            require(type(entry["retries"]) is int and entry["retries"] == 0,
                    kind + "_RETRY_UNQUALIFIED")


def roster_digest(roster):
    """Hash a canonical source-bound full inventory; never include raw names."""
    payload = {"sourceSha": roster["sourceSha"], "tests": roster["tests"],
               "suite": roster["suite"]}
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":"),
                         ensure_ascii=True).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def reconcile(roster, shards, *, source_sha, run_id, attempt):
    """Require two complete, disjoint, successful receipts for the same producer."""
    require(type(source_sha) is str and SHA.fullmatch(source_sha) is not None, "SOURCE_SHA")
    integer(run_id, "RUN_ID")
    integer(attempt, "RUN_ATTEMPT")
    fields(roster, {"schema", "suite", "sourceSha", "frontendProducerSha",
                    "runId", "runAttempt", "frontendArtifactId", "tests"}, "ROSTER_SCHEMA")
    require(roster["schema"] == ROSTER_SCHEMA and roster["suite"] == SUITE, "ROSTER_TYPE")
    require(roster["sourceSha"] == source_sha and
            roster["frontendProducerSha"] == source_sha, "ROSTER_SOURCE_OR_PRODUCER")
    integer(roster["runId"], "ROSTER_RUN_ID")
    integer(roster["runAttempt"], "ROSTER_RUN_ATTEMPT")
    require(roster["runId"] == run_id and roster["runAttempt"] == attempt,
            "ROSTER_RUN_OR_ATTEMPT")
    integer(roster["frontendArtifactId"], "FRONTEND_ARTIFACT_ID")
    sorted_ids(roster["tests"], "ROSTER")
    require(len(shards) == 2, "SHARD_COUNT")
    expected = {record["id"]: record["status"] for record in roster["tests"]}
    digest = roster_digest(roster)
    combined = {}
    for requested_index, receipt in enumerate(shards, start=1):
        fields(receipt, {"schema", "suite", "sourceSha", "frontendProducerSha", "runId",
                         "runAttempt", "frontendArtifactId", "rosterSha256", "index",
                         "total", "status", "tests"}, "SHARD_SCHEMA")
        require(receipt["schema"] == SHARD_SCHEMA and receipt["suite"] == SUITE,
                "SHARD_TYPE")
        require(receipt["sourceSha"] == source_sha and
                receipt["frontendProducerSha"] == source_sha, "SHARD_SOURCE_OR_PRODUCER")
        integer(receipt["runId"], "SHARD_RUN_ID")
        integer(receipt["runAttempt"], "SHARD_RUN_ATTEMPT")
        integer(receipt["frontendArtifactId"], "SHARD_FRONTEND_ARTIFACT_ID")
        require(receipt["runId"] == run_id and receipt["runAttempt"] == attempt,
                "SHARD_RUN_OR_ATTEMPT")
        require(receipt["frontendArtifactId"] == roster["frontendArtifactId"],
                "SHARD_PRODUCER_ARTIFACT")
        require(receipt["rosterSha256"] == digest, "SHARD_ROSTER_DIGEST")
        require(type(receipt["index"]) is int and receipt["index"] == requested_index and
                type(receipt["total"]) is int and receipt["total"] == 2,
                "SHARD_INDEX_OR_TOTAL")
        require(receipt["status"] == "passed", "SHARD_FAILED_OR_INTERRUPTED")
        sorted_ids(receipt["tests"], "SHARD")
        for result in receipt["tests"]:
            identity = result["id"]
            require(identity not in combined, "SHARD_DUPLICATE_TEST")
            require(identity in expected, "SHARD_UNKNOWN_TEST")
            require(result["status"] == expected[identity], "SHARD_STATUS_MISMATCH")
            combined[identity] = result["status"]
    require(combined == expected, "SHARD_MISSING_TEST_OR_STATUS")
    return {
        "schema": OUTPUT_SCHEMA,
        "suite": SUITE,
        "sourceSha": source_sha,
        "frontendProducerSha": source_sha,
        "runId": run_id,
        "runAttempt": attempt,
        "frontendArtifactId": roster["frontendArtifactId"],
        "rosterSha256": digest,
        "shards": 2,
        "tests": len(combined),
        "passed": sum(x == "passed" for x in combined.values()),
        "skipped": sum(x == "skipped" for x in combined.values()),
        "outcome": "PASS",
        "artifactTrust": "EXTERNAL_VERIFICATION_REQUIRED",
        "requiredCheckQualified": False,
    }


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--roster", type=Path, required=True)
    parser.add_argument("--shard-one", type=Path, required=True)
    parser.add_argument("--shard-two", type=Path, required=True)
    parser.add_argument("--source-sha", required=True)
    parser.add_argument("--run-id", type=int, required=True)
    parser.add_argument("--attempt", type=int, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args(argv)
    try:
        require(args.output.name == "playwright-shard-aggregate.json" and
                args.output.parent == Path("artifacts/ci"), "OUTPUT_PATH")
        report = reconcile(read_json(args.roster),
                           [read_json(args.shard_one), read_json(args.shard_two)],
                           source_sha=args.source_sha,
                           run_id=args.run_id, attempt=args.attempt)
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, "O_NOFOLLOW", 0)
        fd = os.open(args.output, flags, 0o600)
        with os.fdopen(fd, "w", encoding="utf-8") as handle:
            json.dump(report, handle, sort_keys=True, separators=(",", ":"))
            handle.write("\n")
        print("Playwright shard union verified: exact two shards, no missing/duplicate IDs.")
    except (OSError, ValueError, TypeError, UnicodeError, RecursionError) as error:
        print("Playwright shard reconciliation refused: " + (
            str(error) if isinstance(error, InvalidEvidence) else "INVALID_INPUT"),
            file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
