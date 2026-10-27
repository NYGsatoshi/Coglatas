#!/usr/bin/env python3
"""Execute the existing isolated SEC-04 deep lane; topology alone cannot pass."""
from __future__ import annotations

from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import tempfile
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/ci"))
import sec14_main_security_evidence as native

QUALIFICATION_BRANCH = "refs/heads/fix/sec04-deep-execution-20261027"
PUBLIC = ROOT / "artifacts/sec04-deep-execution/result.json"
REPORTS = ROOT / "artifacts/security/schemathesis"
CONTRACT = ROOT / "artifacts/openapi/coglatas-openapi.json"
ROLES = ("anonymous", "alpha-restricted", "alpha-member", "alpha-owner", "beta-owner")
RUNTIME = "scripts/ci/run-security-runtime-smoke.sh"
METADATA_KEYS = {"contract_sha256", "lane", "network_errors", "operation_count", "operations",
                 "request_count", "role", "scanner_exit", "schemathesis_version", "seed"}
SOURCES = (".github/workflows/sec04-schemathesis-deep.yml", RUNTIME,
           "scripts/security/scanner-harness.sh", "scripts/security/schemathesis-runner.sh",
           "scripts/security/schemathesis_hooks.py", "scripts/security/schemathesis_policy.py",
           "scripts/security/schemathesis.toml", "scripts/security/process-schemathesis-report.py",
           "scripts/ci/generate-security-openapi-contract.sh", "scripts/ci/sec14_main_security_evidence.py",
           "infra/docker/security-runtime.Dockerfile", "infra/compose/test/real-backend-smoke.yml",
           "infra/compose/security/security.yml",
           "infra/compose/security/runtime.yml", "scripts/ci/run-sec04-deep-ci.py")
STAGES = ("prepare", "sec03", "sec04")


class Rejected(ValueError):
    """Only fixed diagnostics leave this caller."""


def require(value: bool) -> None:
    if not value:
        raise Rejected("EXECUTION_EVIDENCE_INVALID")


def sha(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def open_regular(path: Path, maximum: int):
    require(not path.is_symlink())
    descriptor = os.open(path, os.O_RDONLY | getattr(os, "O_NOFOLLOW", 0) | getattr(os, "O_NONBLOCK", 0))
    try:
        info = os.fstat(descriptor)
        require(stat.S_ISREG(info.st_mode) and 0 < info.st_size <= maximum)
        return os.fdopen(descriptor, "rb"), info.st_size
    except BaseException:
        os.close(descriptor)
        raise


def read_regular(path: Path, maximum: int) -> bytes:
    stream, size = open_regular(path, maximum)
    with stream:
        data = stream.read(maximum + 1)
    require(len(data) == size)
    return data


class Reports:
    """Only the five fixed native report paths can reach the existing parser."""

    def __init__(self, directory: Path):
        self.directory = directory

    def namelist(self):
        return ["artifacts/security/schemathesis/" + role + ".ndjson" for role in ROLES]

    def path(self, name):
        require(name in self.namelist())
        return self.directory / (name.rsplit("/", 1)[1])

    def getinfo(self, name):
        stream, size = open_regular(self.path(name), native.MAX_NDJSON)
        stream.close()
        return SimpleNamespace(file_size=size)

    def open(self, name):
        stream, _ = open_regular(self.path(name), native.MAX_NDJSON)
        return stream


def reserve_public_contract(root: Path) -> Path:
    """Create only a fresh public SEC-01 subtree; never adopt private state."""
    for directory in (root, root / "scripts", root / "scripts/security"):
        info = directory.lstat()
        require(stat.S_ISDIR(info.st_mode) and not directory.is_symlink()
                and info.st_uid == os.getuid() and stat.S_IMODE(info.st_mode) & 0o005 == 0o005)
    for name in ("test-schemathesis-query-wire.py", "test-schemathesis-input-attribution.py",
                 "schemathesis_hooks.py", "schemathesis_policy.py"):
        path = root / "scripts/security" / name
        info = path.lstat()
        require(stat.S_ISREG(info.st_mode) and not path.is_symlink()
                and info.st_uid == os.getuid() and stat.S_IMODE(info.st_mode) & 0o004 == 0o004)
    artifacts, public = root / "artifacts", root / "artifacts/openapi"
    require(not artifacts.exists() and not artifacts.is_symlink())
    for directory in (artifacts, public):
        directory.mkdir(mode=0o755)
        # umask077 remains in force for every other/private child.
        directory.chmod(0o755)
        info = directory.lstat()
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == os.getuid()
                and stat.S_IMODE(info.st_mode) == 0o755)
    return public / "coglatas-openapi.json"


def stage_public_contract(root: Path) -> dict:
    path = root / "artifacts/openapi/coglatas-openapi.json"
    for directory in (root / "artifacts", path.parent):
        info = directory.lstat()
        require(stat.S_ISDIR(info.st_mode) and not directory.is_symlink()
                and info.st_uid == os.getuid() and stat.S_IMODE(info.st_mode) == 0o755)
    stream, size = open_regular(path, native.release.MAX_JSON_BYTES)
    with stream:
        require(os.fstat(stream.fileno()).st_uid == os.getuid())
        raw = stream.read(native.release.MAX_JSON_BYTES + 1)
        require(len(raw) == size)
        contract = native.release.parse_json_bytes(raw)
        require(isinstance(contract.get("openapi"), str) and contract["openapi"].startswith("3.")
                and isinstance(contract.get("paths"), dict) and bool(contract["paths"]))
        os.fchmod(stream.fileno(), 0o644)
        require(stat.S_IMODE(os.fstat(stream.fileno()).st_mode) == 0o644)
    require(sha(read_regular(path, native.release.MAX_JSON_BYTES)) == sha(raw))
    return {"classification": "PUBLIC_SEC01_GENERATED_CONTRACT", "sha256": sha(raw),
            "directoryMode": "0755", "fileMode": "0644"}


PROGRESS = ("role_auth_prepare_start", "role_auth_prepare_ok", "role_scan_start", "role_scan_complete",
            "post_role_readiness_start", "post_role_readiness_ok", "post_role_readiness_failed")
ERROR_CLASSES = {
    "PERMISSION_ERROR_OBSERVED": rb"(?m)^PermissionError:",
    "MISSING_FILE_ERROR_OBSERVED": rb"(?m)^FileNotFoundError:",
    "MISSING_MODULE_ERROR_OBSERVED": rb"(?m)^ModuleNotFoundError:",
    "DOCKER_ERROR_OBSERVED": rb"(?m)^Error response from daemon:",
    "PROCESSOR_REJECTION_OBSERVED": rb"(?m)^SEC-04 Schemathesis evidence failed:",
    "SCANNER_REJECTION_OBSERVED": rb"(?m)^SEC-04 Schemathesis failed:",
}


def failure_observations(private: Path, directory: Path, contract: Path) -> dict:
    """Untrusted closed failure metadata; never qualification or a cause verdict."""
    output = {"interpretation": "FAILURE_OBSERVATIONS_ONLY", "qualification": "NONE",
              "logs": {}, "errorClassesObserved": [], "progress": {}, "roles": {}}
    errors = set()
    for name in ("stdout", "stderr"):
        try:
            raw = read_regular(private / ("SEC04." + name), 16 * 1024 * 1024)
            output["logs"][name] = {"availability": "OBSERVED", "sha256": sha(raw)}
            for category, pattern in ERROR_CLASSES.items():
                if re.search(pattern, raw):
                    errors.add(category)
            for line in raw.splitlines():
                match = re.fullmatch(rb"SECURITY_SCAN_PROGRESS ([a-z_]+) role=([a-z-]+)", line)
                if match is None:
                    continue
                progress, role = (value.decode("ascii") for value in match.groups())
                if progress in PROGRESS and role in ROLES:
                    counts = output["progress"].setdefault(role, {})
                    counts[progress] = counts.get(progress, 0) + 1
        except (OSError, ValueError, UnicodeError):
            output["logs"][name] = {"availability": "UNAVAILABLE"}
    output["errorClassesObserved"] = sorted(errors)
    try:
        contract_digest = sha(read_regular(contract, native.release.MAX_JSON_BYTES))
    except (OSError, ValueError):
        contract_digest = None
    for role in ROLES:
        observation = {"availability": "MISSING_OR_INVALID"}
        try:
            raw = read_regular(directory / (role + ".metadata.json"), 131072)
            row = native.release.parse_json_bytes(raw)
            require(set(row) == METADATA_KEYS and row["role"] == role and row["lane"] == "deep"
                    and row["schemathesis_version"] == "4.25.2" and row["contract_sha256"] == contract_digest)
            require(all(native.number(row[key]) for key in ("request_count", "operation_count", "network_errors"))
                    and native.number(row["seed"], True)
                    and type(row["scanner_exit"]) is int and -255 <= row["scanner_exit"] <= 255)
            require(isinstance(row["operations"], list)
                    and len(row["operations"]) == row["operation_count"]
                    and all(isinstance(value, str) for value in row["operations"]))
            observation = {"availability": "BOUND_PRODUCER_METADATA_OBSERVED", "metadataSha256": sha(raw),
                           "requestCount": row["request_count"], "operationCount": row["operation_count"],
                           "networkErrorCount": row["network_errors"], "scannerExit": row["scanner_exit"],
                           "seed": row["seed"]}
            try:
                report = read_regular(directory / (role + ".ndjson"), native.MAX_NDJSON)
                observation["nativeReportSha256"] = sha(report)
            except (OSError, ValueError):
                observation["nativeReportSha256"] = None
        except (OSError, ValueError, KeyError, TypeError):
            pass
        output["roles"][role] = observation
    return output


def validate_execution(directory: Path, contract: Path, started: datetime, finished: datetime) -> dict:
    require(started.tzinfo is not None and finished.tzinfo is not None and finished > started)
    contract_bytes = read_regular(contract, native.release.MAX_JSON_BYTES)
    document = native.release.parse_json_bytes(contract_bytes)
    require(isinstance(document.get("openapi"), str) and document["openapi"].startswith("3.")
            and isinstance(document.get("paths"), dict) and bool(document["paths"]))
    operations = {method.upper() + " " + path for path, item in document["paths"].items()
                  if isinstance(item, dict) for method in native.METHODS if method in item}
    require(bool(operations) and len(operations) <= 10000)
    contract_digest = sha(contract_bytes)
    observations = {}
    archive = Reports(directory)
    for role in ROLES:
        raw = read_regular(directory / (role + ".metadata.json"), 131072)
        metadata = native.release.parse_json_bytes(raw)
        require(set(metadata) == METADATA_KEYS and metadata["role"] == role
                and metadata["lane"] == "deep" and metadata["schemathesis_version"] == "4.25.2"
                and metadata["contract_sha256"] == contract_digest)
        require(all(type(metadata[key]) is int and metadata[key] == 0 for key in ("scanner_exit", "network_errors"))
                and all(native.number(metadata[key], True) for key in ("request_count", "operation_count", "seed")))
        selected = metadata["operations"]
        require(isinstance(selected, list) and len(selected) == metadata["operation_count"]
                and all(isinstance(item, str) for item in selected)
                and len(set(selected)) == len(selected) and set(selected) <= operations)
        report_digest, events = native.schemathesis_execution(
            archive, role, metadata, {"start": started, "finish": finished})
        observations[role] = {"requestCount": metadata["request_count"],
                              "operationCount": metadata["operation_count"],
                              "unobservedOpenApiOperationCount": len(operations - set(selected)),
                              "seed": metadata["seed"], "metadataSha256": sha(raw),
                              "nativeReportSha256": report_digest, "nativeEventCount": events,
                              "engineOutcome": "COMPLETED"}
    return {"openApiSha256": contract_digest, "openApiOperationCount": len(operations), "roles": observations}


def execute_stages(command, environment: dict, result: dict) -> None:
    """No retries; cleanup never hides the original stage exit."""
    completed = False
    try:
        for stage in STAGES:
            result["phase"] = stage.upper()
            code, _ = command(["bash", RUNTIME, stage], stage.upper(), environment)
            result["stageExitCodes"][stage] = code
            if type(code) is not int or code != 0:
                result["failedStage"] = stage
                break
        else:
            completed = True
    except (OSError, subprocess.SubprocessError):
        result["failedStage"] = stage
        raise
    finally:
        cleanup_environment = environment.copy()
        cleanup_environment["COGLATAS_SECURITY_RUNTIME_FAILED"] = "false" if completed else "true"
        code, _ = command(["bash", RUNTIME, "cleanup"], "CLEANUP", cleanup_environment)
        result["stageExitCodes"]["cleanup"] = code
    require(completed and all(type(result["stageExitCodes"].get(stage)) is int
            and result["stageExitCodes"][stage] == 0 for stage in STAGES + ("cleanup",)))


def publish_once(path: Path, result: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    pending = path.with_name(path.name + ".pending")
    with pending.open("xb") as output:
        output.write((json.dumps(result, sort_keys=True) + "\n").encode())
        output.flush()
        os.fsync(output.fileno())
    try:
        os.link(pending, path)
    finally:
        pending.unlink()


def main() -> int:
    os.umask(0o077)
    result = {"schemaVersion": 1, "kind": "SEC04_DEEP_EXECUTION_ONLY", "status": "FAIL",
              "phase": "ADMISSION", "failureCategory": "CALLER_REJECTED",
              "sourceSha": None, "sourceTree": None, "runId": None, "runAttempt": None,
              "stageExitCodes": {}, "commandExitCodes": {}, "failedStage": None, "bindings": {}, "execution": {},
              "sourceBuildBinding": "CURRENT_SOURCE_DOCKER_BUILD_ONLY",
              "targetScope": "SEC02_ISOLATED_SYNTHETIC_ONLY", "securityProgramAcceptance": "NOT_GRANTED",
              "scheduleActivated": False, "publicGo": False,
              "privateChildLogsAfterHostedTeardown": "NOT_RETAINED"}
    private = None
    try:
        require(len(sys.argv) == 1 and os.environ.get("COGLATAS_SEC04_RUNNER_ENVIRONMENT") == "github-hosted"
                and os.environ.get("RUNNER_OS") == "Linux")
        event = os.environ.get("GITHUB_EVENT_NAME")
        require(event == "workflow_dispatch" or (event == "push" and os.environ.get("GITHUB_REF") == QUALIFICATION_BRANCH))
        require(not PUBLIC.exists() and not PUBLIC.is_symlink() and not REPORTS.exists() and not REPORTS.is_symlink())
        for variable, key in (("GITHUB_RUN_ID", "runId"), ("GITHUB_RUN_ATTEMPT", "runAttempt")):
            require(re.fullmatch(r"[1-9][0-9]*", os.environ.get(variable, "")) is not None)
            result[key] = int(os.environ[variable])
        require(bool(os.environ.get("COGLATAS_SECURITY_CI_PASSWORD")))
        environment = os.environ.copy()
        for key in ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH"):
            environment.pop(key, None)
        environment["DOCKER_HOST"] = "unix:///var/run/docker.sock"
        environment["COGLATAS_SECURITY_SCHEMATHESIS_LANE"] = "deep"
        # Keep the exact existing budgets, seeds, policies and targets.
        for key in ("COGLATAS_SECURITY_SCHEMATHESIS_PR_EXAMPLES", "COGLATAS_SECURITY_SCHEMATHESIS_DEEP_EXAMPLES",
                    "COGLATAS_SECURITY_SCHEMATHESIS_SEED", "COGLATAS_SECURITY_CI_PROJECT",
                    "COGLATAS_SECURITY_RUNTIME_STATE_DIR", "SECURITY_SCAN_TARGET"):
            environment.pop(key, None)
        private = Path(tempfile.mkdtemp(prefix="sec04-", dir=os.environ["RUNNER_TEMP"]))
        def command(args, phase, selected):
            result["phase"] = phase
            result["commandExitCodes"][phase] = None
            process = subprocess.run(args, cwd=ROOT, env=selected, capture_output=True, check=False)
            result["commandExitCodes"][phase] = process.returncode
            (private / (phase + ".stdout")).write_bytes(process.stdout)
            (private / (phase + ".stderr")).write_bytes(process.stderr)
            return process.returncode, process.stdout
        def checked(args, phase):
            code, output = command(args, phase, environment)
            require(type(code) is int and code == 0)
            return output
        result["failureCategory"] = "PREPARATION_FAILED"
        source = checked(["git", "rev-parse", "HEAD"], "SOURCE").decode().strip()
        require(re.fullmatch(r"[a-f0-9]{40}", source) is not None and source == os.environ.get("GITHUB_SHA"))
        result["sourceSha"] = source
        result["sourceTree"] = checked(["git", "rev-parse", "HEAD^{tree}"], "TREE").decode().strip()
        require(re.fullmatch(r"[a-f0-9]{40}", result["sourceTree"]) is not None)
        require(not checked(["git", "status", "--porcelain", "--untracked-files=all", "--",
                             "src", "scripts/ci", "scripts/security", "infra", ".github", "global.json"], "CLEAN_SOURCE"))
        result["bindings"] = {path: sha((ROOT / path).read_bytes()) for path in SOURCES}
        require(reserve_public_contract(ROOT) == CONTRACT)
        checked(["bash", "scripts/ci/generate-security-openapi-contract.sh"], "OPENAPI")
        result["publicContractStaging"] = stage_public_contract(ROOT)
        image_tag = "coglatas-sec04-deep:" + source
        checked(["docker", "--host=unix:///var/run/docker.sock", "build", "--file",
                 "infra/docker/security-runtime.Dockerfile", "--tag", image_tag, "."], "BUILD_IMAGE")
        image = checked(["docker", "--host=unix:///var/run/docker.sock", "image", "inspect", "--format",
                         "{{.Id}}", image_tag], "IMAGE_ID").decode().strip()
        require(re.fullmatch(r"sha256:[a-f0-9]{64}", image) is not None)
        result["bindings"]["runtimeImageId"] = image
        environment["COGLATAS_SECURITY_RUNTIME_IMAGE"] = image
        result["failureCategory"] = "RUNTIME_STAGE_FAILED"
        started = datetime.now(timezone.utc)
        execute_stages(command, environment, result)
        finished = datetime.now(timezone.utc)
        result["phase"], result["failureCategory"] = "NATIVE_VALIDATION", "NATIVE_EVIDENCE_INVALID"
        result["execution"] = validate_execution(REPORTS, CONTRACT, started, finished)
        require(result["bindings"] == {**{path: sha((ROOT / path).read_bytes()) for path in SOURCES},
                                        "runtimeImageId": image})
        require(not checked(["git", "status", "--porcelain", "--untracked-files=all", "--",
                             "src", "scripts/ci", "scripts/security", "infra", ".github", "global.json"], "SOURCE_UNCHANGED"))
        result["status"], result["phase"], result["failureCategory"] = "PASS", "COMPLETE", None
    except (Rejected, OSError, ValueError, TypeError, KeyError, IndexError, subprocess.SubprocessError):
        if private is not None and (result["failedStage"] == "sec04" or result["phase"] == "NATIVE_VALIDATION"):
            result["failureObservations"] = failure_observations(private, REPORTS, CONTRACT)
    published = False
    try:
        publish_once(PUBLIC, result)
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write("published=true\n")
        published = True
    except (OSError, ValueError, KeyError):
        print("PUBLICATION_FAILED", file=sys.stderr)
    print(json.dumps(result, sort_keys=True))
    return 0 if published and result["status"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
