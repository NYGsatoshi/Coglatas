#!/usr/bin/env python3
"""Secret-free, advisory hosted caller for the held physical-recovery probe."""
from __future__ import annotations

import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
PROBE = ROOT / "scripts/ci/synthetic-physical-recovery-probe.py"
PUBLIC = ROOT / "artifacts/synthetic-recovery-ci/result.json"
ASSEMBLIES = ("Coglatas.Web.dll", "Coglatas.Infrastructure.dll", "Coglatas.Application.dll", "Coglatas.Domain.dll")
COMPARISONS = ("schema.sql", "rows.jsonl", "catalog.txt", "roles.txt", "memberships.txt", "database-security.txt", "sequences.json")
APP = {"status": "PASS", "retainedKeysSession": "PASS", "missingKeysSession": "DENIED_401",
       "missingCsrf": "DENIED_403", "restartRetainedSession": "PASS", "freshLogin": "PASS"}
ORACLE = {"schemaAndRawCatalogChange": "DETECTED", "rowChange": "DETECTED",
          "sequenceChange": "DETECTED", "exactOriginalStateRestored": "PASS"}
PASS_KEYS = set(("schema sourceSha probeSha256 ddlSha256 postgresImageId resources status formalStrategyApproved "
                 "publicGo fullApplicationRecovery targetHttps rollbackRehearsal startedAtUtc applicationBuild "
                 "postgresServerVersion triggerControls sourceLoadedAssemblies sourceFile webDllSha256 basebackupSeconds "
                 "backupManifestVerification backupManifestSha256Before comparisons oracleNegativeControls "
                 "restoredTriggerControls restoredLoadedAssemblies restoredFile restartFile boundedApplicationControls "
                 "backupManifestSha256After backupManifestVerificationAfterControls stoppedContainers admittedContainerIds "
                 "cleanupState finishedAtUtc").split())


class Rejected(ValueError):
    """A closed, non-sensitive failure category."""


def require(condition: bool) -> None:
    if not condition:
        raise Rejected("RECEIPT_REJECTED")


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def closed_json(data: bytes):
    require(0 < len(data) <= 131072)
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result)
            result[key] = value
        return result
    def constant(_):
        raise Rejected("RECEIPT_REJECTED")
    return json.loads(data, object_pairs_hook=pairs, parse_constant=constant)


def digest_value(value) -> bool:
    return isinstance(value, str) and re.fullmatch(r"[a-f0-9]{64}", value) is not None


def validate(summary_bytes: bytes, receipt_bytes: bytes, exit_code: int, expected: dict) -> dict:
    summary, receipt = closed_json(summary_bytes), closed_json(receipt_bytes)
    require(isinstance(summary, dict) and set(summary) == {
        "status", "receiptSha256", "formalStrategyApproved", "fullApplicationRecovery"})
    require(isinstance(receipt, dict) and set(receipt) == PASS_KEYS)
    require(summary["formalStrategyApproved"] is False)
    require(type(exit_code) is int and exit_code == 0)
    require(summary == {"status": "SYNTHETIC_DATABASE_CANDIDATE_PASS", "receiptSha256": sha(receipt_bytes),
                        "formalStrategyApproved": False, "fullApplicationRecovery": "NOT_EXECUTED"})
    require(receipt["schema"] == "coglatas-synthetic-physical-recovery-v1")
    require(receipt["status"] == summary["status"] and receipt["formalStrategyApproved"] is False
            and receipt["publicGo"] is False)
    require(all(receipt[key] == "NOT_EXECUTED" for key in ("fullApplicationRecovery", "targetHttps", "rollbackRehearsal")))
    for key in ("sourceSha", "probeSha256", "ddlSha256", "postgresImageId"):
        require(receipt[key] == expected[key])
    build = receipt["applicationBuild"]
    require(isinstance(build, dict) and set(build) == {
        "sourceSha", "sourceTree", "buildExitCode", "sdkVersion", "buildLogSha256", "generatedDdlSha256", "assemblies"})
    require(build["sourceSha"] == expected["sourceSha"] and build["sourceTree"] == expected["sourceTree"]
            and type(build["buildExitCode"]) is int and build["buildExitCode"] == 0
            and build["sdkVersion"] == expected["sdkVersion"] and digest_value(build["buildLogSha256"])
            and build["generatedDdlSha256"] == expected["ddlSha256"])
    require(build["assemblies"] == expected["assemblies"] and set(build["assemblies"]) == set(ASSEMBLIES))
    require(all(digest_value(value) for value in build["assemblies"].values()))
    require(receipt["sourceLoadedAssemblies"] == build["assemblies"]
            and receipt["restoredLoadedAssemblies"] == build["assemblies"]
            and receipt["webDllSha256"] == build["assemblies"]["Coglatas.Web.dll"])
    require(re.fullmatch(r"18\.6(?: \([A-Za-z0-9 ._+:/-]{1,160}\))?", receipt["postgresServerVersion"]) is not None)
    comparisons = receipt["comparisons"]
    require(isinstance(comparisons, dict) and set(comparisons) == set(COMPARISONS))
    for value in comparisons.values():
        require(isinstance(value, dict) and set(value) == {"sourceSha256", "restoredSha256", "exactBytesEqual"})
        require(digest_value(value["sourceSha256"]) and value["sourceSha256"] == value["restoredSha256"]
                and value["exactBytesEqual"] is True)
    require(receipt["oracleNegativeControls"] == ORACLE)
    guards = {"revisionHeadNonzeroInsert": "DENIED_23514_WITH_ZERO_POSITIVE",
              "guardSourceSha256": expected["guardSourceSha256"],
              "fileVersionArchiveCopyAfterCapture": "DENIED_23505_WITH_CAPTURE_POSITIVE",
              "fileTriggerSourceSha256": expected["fileTriggerSourceSha256"]}
    require(receipt["triggerControls"] == guards and receipt["restoredTriggerControls"] == guards)
    require(receipt["backupManifestVerification"] == "PASS"
            and receipt["backupManifestVerificationAfterControls"] == "PASS"
            and digest_value(receipt["backupManifestSha256Before"])
            and receipt["backupManifestSha256Before"] == receipt["backupManifestSha256After"])
    require(receipt["boundedApplicationControls"] == APP)
    source_file = receipt["sourceFile"]
    require(isinstance(source_file, dict) and set(source_file) == {"status", "bytes", "sha256"})
    require(source_file["status"] == "PASS" and type(source_file["bytes"]) is int
            and source_file["bytes"] > 0 and digest_value(source_file["sha256"]))
    require(receipt["restoredFile"] == source_file and receipt["restartFile"] == source_file)
    resources = receipt["resources"]
    require(isinstance(resources, dict) and set(resources) == {"containers", "volumes"})
    names, volumes = resources["containers"], resources["volumes"]
    require(isinstance(names, list) and len(names) == 2 and isinstance(volumes, list) and len(volumes) == 3)
    require(isinstance(names[0], str) and re.fullmatch(r"coglatas-synthetic-recovery-[a-f0-9]{12}-source", names[0]) is not None)
    prefix = names[0][:-len("-source")]
    require(names == [prefix + "-source", prefix + "-target"]
            and volumes == [prefix + "-source", prefix + "-backup", prefix + "-target"])
    ids = receipt["admittedContainerIds"]
    require(isinstance(ids, list) and len(ids) == 2 and len(set(ids)) == 2 and all(digest_value(value) for value in ids))
    require(receipt["stoppedContainers"] == names and receipt["cleanupState"] == "VERIFIED")
    require(type(receipt["basebackupSeconds"]) in (int, float)
            and 0 <= receipt["basebackupSeconds"] <= 10000000 and math.isfinite(receipt["basebackupSeconds"]))
    require(all(isinstance(receipt[key], str) and re.fullmatch(r"\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ", receipt[key])
                for key in ("startedAtUtc", "finishedAtUtc")))
    return {"strictComparisons": "PASS", "oracleNegatives": "PASS", "sourceGuards": "PASS",
            "backupPreserved": "PASS", "assemblyBinding": "PASS", "boundedApplication": "PASS",
            "fileContinuity": "PASS", "ownedContainerStops": "PASS"}


def comparison_evidence(data: bytes, expected: dict):
    """Return only complete typed failure metadata, never a success verdict."""
    try:
        receipt = closed_json(data)
        require(isinstance(receipt, dict) and set(receipt) <= PASS_KEYS | {"failureCategory"})
        require(receipt["formalStrategyApproved"] is False and receipt["publicGo"] is False)
        for key in ("sourceSha", "probeSha256", "ddlSha256", "postgresImageId"):
            require(receipt[key] == expected[key])
        observed = receipt["comparisons"]
        require(isinstance(observed, dict) and set(observed) == set(COMPARISONS))
        output = {}
        for name in COMPARISONS:
            value = observed[name]
            require(isinstance(value, dict) and set(value) == {"sourceSha256", "restoredSha256", "exactBytesEqual"})
            require(digest_value(value["sourceSha256"]) and digest_value(value["restoredSha256"])
                    and type(value["exactBytesEqual"]) is bool
                    and value["exactBytesEqual"] == (value["sourceSha256"] == value["restoredSha256"]))
            output[name] = {key: value[key] for key in ("sourceSha256", "restoredSha256", "exactBytesEqual")}
        return output
    except (OSError, ValueError, TypeError, KeyError):
        return None


def read_private(path: Path) -> bytes:
    info = path.lstat()
    require(stat.S_ISREG(info.st_mode) and not path.is_symlink() and 0 < info.st_size <= 131072)
    return path.read_bytes()


def publish_once(path: Path, result: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    payload = (json.dumps(result, sort_keys=True) + "\n").encode()
    temporary = path.with_name(path.name + ".pending")
    with temporary.open("xb") as handle:
        handle.write(payload)
        handle.flush()
        os.fsync(handle.fileno())
    try:
        os.link(temporary, path)  # Atomic creation, never overwrite a prior result.
    finally:
        temporary.unlink()


def main() -> int:
    os.umask(0o077)
    result = {"schemaVersion": 1, "kind": "ADVISORY_SYNTHETIC_PHYSICAL_RECOVERY",
              "status": "FAIL", "phase": "ADMISSION", "failureCategory": "CALLER_REJECTED",
              "sourceSha": None, "runId": None, "runAttempt": None, "receiptSha256": None, "probeExitCode": None,
              "bindings": {}, "controls": {}, "formalStrategyApproved": False, "publicGo": False,
              "fullApplicationRecovery": "NOT_EXECUTED", "targetHttps": "NOT_EXECUTED",
              "rollbackRehearsal": "NOT_EXECUTED",
              "privateEvidenceAfterHostedTeardown": "NOT_RETAINED"}
    private = None
    expected = None
    try:
        require(os.environ.get("COGLATAS_RECOVERY_RUNNER_ENVIRONMENT") == "github-hosted"
                and os.environ.get("GITHUB_EVENT_NAME") == "pull_request"
                and os.environ.get("RUNNER_OS") == "Linux")
        require(not PUBLIC.exists() and not PUBLIC.is_symlink())
        for name, key in (("GITHUB_RUN_ID", "runId"), ("GITHUB_RUN_ATTEMPT", "runAttempt")):
            require(re.fullmatch(r"[1-9][0-9]*", os.environ.get(name, "")) is not None)
            result[key] = int(os.environ[name])
        environment = os.environ.copy()
        for key in ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH"):
            environment.pop(key, None)
        private = Path(tempfile.mkdtemp(prefix="cgr-", dir=os.environ["RUNNER_TEMP"]))
        def command(args, phase, extra=None):
            result["phase"] = phase
            selected = environment.copy()
            if extra:
                selected.update(extra)
            process = subprocess.run(args, cwd=ROOT, env=selected, capture_output=True, check=False, timeout=600)
            (private / (phase + ".stdout")).write_bytes(process.stdout)
            (private / (phase + ".stderr")).write_bytes(process.stderr)
            if process.returncode:
                raise Rejected("PREPARATION_FAILED")
            return process.stdout
        source = command(["git", "rev-parse", "HEAD"], "SOURCE").decode().strip()
        require(re.fullmatch(r"[a-f0-9]{40}", source) is not None and source == os.environ.get("GITHUB_SHA"))
        result["sourceSha"] = source
        source_tree = command(["git", "rev-parse", "HEAD:src"], "SOURCE_TREE").decode().strip()
        sdk = closed_json((ROOT / "global.json").read_bytes())["sdk"]["version"]
        require(command(["dotnet", "--version"], "SDK").decode().strip() == sdk)
        (ROOT / "src/Coglatas.Web/wwwroot").mkdir(exist_ok=True)
        config = private / "NuGet.Config"
        config.write_text('<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
        command(["dotnet", "restore", "src/Coglatas.Web/Coglatas.Web.csproj", "--configfile", str(config),
                 "--disable-parallel", "--verbosity", "minimal"], "RESTORE")
        command(["dotnet", "tool", "restore", "--configfile", str(config)], "TOOLS")
        command(["dotnet", "build", "src/Coglatas.Web/Coglatas.Web.csproj", "--configuration", "Release",
                 "--no-restore", "--disable-build-servers", "-m:1"], "BUILD")
        ddl = private / "original.sql"
        command(["dotnet", "ef", "migrations", "script", "--project", "src/Coglatas.Infrastructure",
                 "--startup-project", "src/Coglatas.Web", "--configuration", "Release", "--no-build",
                 "--output", str(ddl)], "DDL", {"ConnectionStrings__DefaultConnection": "Host=unused;Database=unused;Username=unused"})
        docker = ["docker", "--host=unix:///var/run/docker.sock"]
        command(docker + ["info", "--format", "{{.ServerVersion}}"], "DOCKER")
        command(docker + ["pull", "postgres:18.6-alpine"], "IMAGE_PULL")
        image = closed_json(command(docker + ["image", "inspect", "--format",
                            '{"id":{{json .Id}},"digests":{{json .RepoDigests}}}', "postgres:18.6-alpine"], "IMAGE_ID"))
        require(set(image) == {"id", "digests"} and re.fullmatch(r"sha256:[a-f0-9]{64}", image["id"]) is not None)
        require(isinstance(image["digests"], list) and image["digests"] and all(
            isinstance(item, str) and re.fullmatch(r"(?:docker.io/library/)?postgres@sha256:[a-f0-9]{64}", item)
            for item in image["digests"]))
        binaries = ROOT / "src/Coglatas.Web/bin/Release/net10.0"
        expected = {"sourceSha": source, "sourceTree": source_tree, "sdkVersion": sdk,
                    "probeSha256": sha(PROBE.read_bytes()), "ddlSha256": sha(ddl.read_bytes()),
                    "postgresImageId": image["id"], "assemblies": {name: sha((binaries / name).read_bytes()) for name in ASSEMBLIES},
                    "guardSourceSha256": sha((ROOT / "src/Coglatas.Infrastructure/Persistence/Migrations/20260924133000_AddCoglatasUiCanonicalChangeJournal.cs").read_bytes()),
                    "fileTriggerSourceSha256": sha((ROOT / "src/Coglatas.Infrastructure/Persistence/Migrations/20260901150000_AddFileVersionsActivityProjection.cs").read_bytes())}
        result["bindings"] = {key: expected[key] for key in
                              ("sourceTree", "sdkVersion", "probeSha256", "ddlSha256", "postgresImageId", "assemblies")}
        result["bindings"]["postgresRepoDigests"] = image["digests"]
        evidence = private / "e"
        require(len(str(evidence / "source-socket")) <= 85 and len(str(evidence / "target-socket")) <= 85)
        result["phase"] = "PROBE"
        process = subprocess.run([sys.executable, str(PROBE), "--execute-isolated-synthetic",
                                  "--postgres-image-id", image["id"], "--original-ef-ddl", str(ddl),
                                  "--evidence-dir", str(evidence), "--with-application-controls"],
                                 cwd=ROOT, env=environment, capture_output=True, check=False)
        result["probeExitCode"] = process.returncode
        (private / "probe.stdout").write_bytes(process.stdout)
        (private / "probe.stderr").write_bytes(process.stderr)
        raw = read_private(evidence / "receipt.json")
        result["receiptSha256"] = sha(raw)
        result["phase"] = "VALIDATION"
        result["controls"] = validate(process.stdout, raw, process.returncode, expected)
        version = closed_json(raw)["postgresServerVersion"]
        result["bindings"]["postgresServerVersion"] = version
        result["bindings"]["postgresServerVersionSha256"] = sha(version.encode())
        result["status"] = "SYNTHETIC_DATABASE_CANDIDATE_PASS"
        result["failureCategory"] = None
        result["phase"] = "COMPLETE"
    except (Rejected, OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
        # No arbitrary exception text, child output, resource names, paths or credentials are published.
        if result["phase"] in ("PROBE", "VALIDATION") and private is not None:
            try:
                raw = read_private(private / "e/receipt.json")
                result["receiptSha256"] = sha(raw)
                observed = closed_json(raw)
                require(isinstance(observed, dict))
                if expected is not None:
                    comparisons = comparison_evidence(raw, expected)
                    if comparisons is not None:
                        result["comparisonEvidence"] = comparisons
                allowed = set(re.findall(r'ProbeFailure\("([A-Z0-9_]+)"\)', PROBE.read_text()))
                allowed.add("ISOLATED_PROBE_FAILED")
                category = observed.get("failureCategory")
                result["failureCategory"] = category if category in allowed else "INCOMPLETE_OR_INVALID_RECEIPT"
            except (OSError, ValueError, TypeError, KeyError):
                result["failureCategory"] = "INCOMPLETE_OR_INVALID_RECEIPT"
        elif result["phase"] != "ADMISSION":
            result["failureCategory"] = "PREPARATION_FAILED"
    publication_ok = False
    try:
        publish_once(PUBLIC, result)
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write("published=true\n")
        publication_ok = True
    except (OSError, ValueError, KeyError):
        print("PUBLICATION_FAILED", file=sys.stderr)
    print(json.dumps(result, sort_keys=True))
    return 0 if publication_ok and result["status"] == "SYNTHETIC_DATABASE_CANDIDATE_PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
