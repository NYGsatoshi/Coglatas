"""Reconcile immutable SEC-ARCH receipt and producer bytes without certifying acceptance."""

from __future__ import annotations

import argparse
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path, PurePosixPath
import re
import stat
import tarfile
import tempfile
import zipfile
import zlib

from sec_arch_assembly_binding import (LEGACY_ASSEMBLIES, LEGACY_SCOPE, SIX_ASSEMBLY_SCOPE,
                                      assembly_member, loaded_assembly_member, receipt_assemblies)

# Existing consumers retain their original historical fixture scope.
ASSEMBLIES = LEGACY_ASSEMBLIES
MAX_ARCHIVE = 512 * 1024 * 1024
MAX_EXPANDED = 1024 * 1024 * 1024
MAX_JSON = 8 * 1024 * 1024


class ReconciliationError(ValueError):
    """Only fixed, disclosure-safe diagnostics leave the command."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ReconciliationError(message)


def stream_digest(stream, limit: int) -> str:
    result, total = hashlib.sha256(), 0
    while chunk := stream.read(1024 * 1024):
        total += len(chunk)
        require(total <= limit, "Artifact exceeds the bounded input size.")
        result.update(chunk)
    return result.hexdigest()


def safe_name(name: str) -> str:
    path = PurePosixPath(name)
    require(bool(name) and not path.is_absolute() and ".." not in path.parts and
            "\\" not in name and "\x00" not in name and ":" not in name and
            path.as_posix() == name.rstrip("/"), "Archive member path is unsupported.")
    return path.as_posix()


@contextmanager
def verified_zip(path: Path, expected_digest: str):
    require(path.is_file() and path.stat().st_size <= MAX_ARCHIVE, "Artifact size is unsupported.")
    # Parse the exact hashed snapshot, even if the caller replaces its input path.
    with tempfile.TemporaryFile() as snapshot, path.open("rb") as source:
        observed, size = hashlib.sha256(), 0
        while chunk := source.read(1024 * 1024):
            size += len(chunk)
            require(size <= MAX_ARCHIVE, "Artifact exceeds the bounded input size.")
            snapshot.write(chunk)
            observed.update(chunk)
        require(observed.hexdigest() == expected_digest, "Independent artifact digest differs.")
        snapshot.seek(0)
        with zipfile.ZipFile(snapshot) as archive:
            members, seen, expanded = archive.infolist(), set(), 0
            require(0 < len(members) <= 256, "ZIP member count is unsupported.")
            for member in members:
                name = safe_name(member.filename)
                mode = member.external_attr >> 16
                require(name not in seen and not member.flag_bits & 1 and not stat.S_ISLNK(mode),
                        "Duplicate, encrypted or linked ZIP member.")
                seen.add(name)
                expanded += member.file_size
                require(0 <= member.file_size <= MAX_ARCHIVE and expanded <= MAX_EXPANDED,
                        "ZIP expansion exceeds the bounded input size.")
            yield archive


def read_member(archive: zipfile.ZipFile, name: str, maximum: int) -> bytes:
    require(name in archive.namelist() and archive.getinfo(name).file_size <= maximum,
            "Required bounded artifact member is missing.")
    with archive.open(name) as stream:
        data = stream.read(maximum + 1)
    require(len(data) <= maximum, "Artifact member exceeds the bounded input size.")
    return data


def unique_object(pairs: list[tuple]) -> dict:
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON property.")
        result[key] = value
    return result


def reject_constant(_value: str):
    raise ReconciliationError("Non-finite JSON value.")


def reconcile(producer_path: Path, execution_path: Path, candidate: str, run_id: str,
              attempt: str, producer_digest: str, execution_digest: str) -> dict:
    require(bool(re.fullmatch(r"[a-f0-9]{40}", candidate)) and
            bool(re.fullmatch(r"[1-9][0-9]*", run_id)) and bool(re.fullmatch(r"[1-9][0-9]*", attempt)) and
            all(re.fullmatch(r"[a-f0-9]{64}", value) for value in (producer_digest, execution_digest)),
            "Independent exact candidate, run, attempt and artifact digests are required.")
    with verified_zip(execution_path, execution_digest) as execution:
        receipt_bytes = read_member(execution, "execution.json", MAX_JSON)
        receipt = json.loads(receipt_bytes, object_pairs_hook=unique_object, parse_constant=reject_constant)
    require(isinstance(receipt, dict), "Unsupported execution receipt.")
    try:
        assemblies = receipt_assemblies(receipt, execution=True)
    except ValueError as error:
        raise ReconciliationError("Unsupported versioned execution assembly identity.") from error
    require(receipt.get("candidateSha") == candidate and receipt.get("runId") == run_id and
            receipt.get("runAttempt") == attempt and receipt.get("buildStampMatchesCandidate") is True,
            "Execution candidate, run, attempt or build binding differs.")
    environment = receipt.get("environment")
    require(isinstance(environment, dict) and
            hashlib.sha256(json.dumps(environment, sort_keys=True).encode()).hexdigest() ==
            receipt.get("environmentFingerprint"), "Receipt environment fingerprint differs.")
    claimed = receipt.get("assemblyDigests")
    wanted = {assembly_member(name): name for name in assemblies}
    copies = {loaded_assembly_member(name): name for name in assemblies} if receipt["schemaVersion"] == 2 else {}
    observed, loaded, stamp, total, seen = {}, {}, None, 0, set()
    with verified_zip(producer_path, producer_digest) as producer:
        require(read_member(producer, "source-sha", 100).decode("ascii").strip() == candidate,
                "Producer source revision differs.")
        require("dotnet-release-build.tar" in producer.namelist(), "Producer build archive is missing.")
        with producer.open("dotnet-release-build.tar") as stream, tarfile.open(fileobj=stream, mode="r|") as build:
            for member in build:
                name = safe_name(member.name)
                require(name not in seen and (member.isfile() or member.isdir()),
                        "Duplicate or non-regular TAR member.")
                seen.add(name)
                total += member.size
                require(len(seen) <= 10000 and 0 <= member.size <= MAX_ARCHIVE and total <= MAX_EXPANDED,
                        "TAR expansion exceeds the bounded input size.")
                if name in wanted or name in copies:
                    require(member.isfile() and 0 < member.size <= 32 * 1024 * 1024,
                            "Required compiled assembly is unsupported.")
                    with build.extractfile(member) as payload:
                        assembly_digest = stream_digest(payload, 32 * 1024 * 1024)
                        if name in wanted:
                            observed[wanted[name]] = assembly_digest
                        if name in copies:
                            loaded[copies[name]] = assembly_digest
                elif name == "artifacts/ci/dotnet-build-sha":
                    require(member.isfile() and member.size <= 100, "Build stamp is unsupported.")
                    with build.extractfile(member) as payload:
                        stamp = payload.read(101).decode("ascii").strip()
    require(stamp == candidate and observed == claimed, "Compiled producer assembly or build stamp differs.")
    require(receipt["schemaVersion"] == 1 or loaded == claimed,
            "Copied execution dependency is missing or differs from the producer build.")
    require(isinstance(receipt.get("observedExecution"), dict) and
            receipt["observedExecution"].get("outcome") in ("PASS", "FAIL", "ERROR", "UNVERIFIED"),
            "Observed execution outcome is missing or unsupported.")
    return {"schemaVersion": 2, "qualification": "PRODUCER_BYTES_RECONCILED",
            "inputReceiptSchemaVersion": receipt["schemaVersion"],
            "assemblyBindingScope": SIX_ASSEMBLY_SCOPE if receipt["schemaVersion"] == 2 else LEGACY_SCOPE,
            "boundAssemblyCount": len(assemblies),
            "fullDependencyQualification": "SIX_ASSEMBLY_BYTES_RECONCILED" if receipt["schemaVersion"] == 2 else "UNVERIFIED",
            "candidateSha": candidate, "runId": run_id, "runAttempt": attempt,
            "producerZipDigest": producer_digest, "executionZipDigest": execution_digest,
            "receiptDigest": hashlib.sha256(receipt_bytes).hexdigest(), "assemblyDigests": observed,
            "environmentFingerprint": receipt["environmentFingerprint"],
            "originalExecutionOutcome": receipt["observedExecution"]["outcome"],
            "trustedAttestation": "UNVERIFIED", "ownerApproval": None,
            "preAvaloniaVerdict": "PRE-AVALONIA SEC-ARCH: BLOCKED",
            "limits": ["Expected inputs must come independently from reviewed GitHub run/artifact provenance.",
                       "This offline check does not authenticate API exports, signatures, raw TRX or claimed coverage.",
                       "Original receipts, outcomes and source-binding qualifications remain unchanged.",
                       "Version 1 covers five historical assemblies and cannot qualify the verifier dependency or all loaded copies.",
                       "Mapping, policy, activation, full architecture coverage and #842/#614 remain separate gates."]}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--producer", required=True, type=Path)
    parser.add_argument("--execution", required=True, type=Path)
    parser.add_argument("--candidate-sha", required=True)
    parser.add_argument("--run-id", required=True)
    parser.add_argument("--run-attempt", required=True)
    parser.add_argument("--producer-digest", required=True)
    parser.add_argument("--execution-digest", required=True)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    try:
        result = reconcile(args.producer, args.execution, args.candidate_sha, args.run_id,
                           args.run_attempt, args.producer_digest, args.execution_digest)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        with args.output.open("x", encoding="utf-8", newline="\n") as destination:
            destination.write(json.dumps(result, indent=2) + "\n")
    except ReconciliationError as error:
        print(json.dumps({"outcome": "ERROR", "reason": str(error)}))
        return 1
    except (OSError, ValueError, TypeError, KeyError, RuntimeError, zipfile.BadZipFile,
            tarfile.TarError, EOFError, zlib.error):
        print(json.dumps({"outcome": "ERROR", "reason": "Artifact input or exclusive output is invalid."}))
        return 1
    print("SEC-ARCH producer bytes reconciled; trusted final attestation remains UNVERIFIED.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
