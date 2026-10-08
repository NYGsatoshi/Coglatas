"""Observed local resources and immutable inputs; never infer hosted identity."""
from __future__ import annotations
import datetime as dt
import hashlib
import json
import os
import platform
import re
import shutil
import subprocess
from pathlib import Path
from diagnostic_json import load_json
from common import repository_root, write_json_atomic

ROOT = repository_root()
SHA = re.compile(r"[0-9a-f]{40}")
DIGEST = re.compile(r"[0-9a-f]{64}")
CONTRACT_PATHS = (
    "global.json", "performance/environment.json", "performance/datasets.json",
    "performance/scenarios.json", "performance/budgets.json", "performance/api-k6.json",
    "performance/db-scenarios.json", "performance/comparison-policy.json",
    "infra/compose/performance/environment.yml", "infra/compose/performance/pr.yml",
)

class LocalError(ValueError):
    def __init__(self, code, stage):
        self.code, self.stage = code, stage
        super().__init__(code)

def now():
    return dt.datetime.now(dt.timezone.utc).isoformat()

def utc(value):
    parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    if parsed.tzinfo is None or parsed.utcoffset() != dt.timedelta(0):
        raise LocalError("EVIDENCE_INVALID", "timestamp")
    return parsed

def sha256(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()

def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"),
                                     allow_nan=False).encode()).hexdigest()

def invoke(command, *, cwd=ROOT, env=None, timeout=60, input_bytes=None, new_session=False):
    """Process output stays private. Stop owned process groups on timeout/signal."""
    import signal
    try:
        process = subprocess.Popen(command, cwd=cwd, env=env, stdin=subprocess.PIPE,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                   start_new_session=new_session)
    except OSError as error:
        raise LocalError("TOOL_UNAVAILABLE", "process") from error
    try:
        stdout, _ = process.communicate(input_bytes, timeout=timeout)
    except (subprocess.TimeoutExpired, KeyboardInterrupt) as cause:
        if new_session and os.name == "posix":
            os.killpg(process.pid, signal.SIGTERM)
        else:
            process.terminate()
        try:
            process.communicate(timeout=10)
        except subprocess.TimeoutExpired:
            if new_session and os.name == "posix":
                os.killpg(process.pid, signal.SIGKILL)
            else:
                process.kill()
            process.communicate()
        error = LocalError("PROCESS_TIMEOUT" if isinstance(cause, subprocess.TimeoutExpired)
                           else "PROCESS_INTERRUPTED", "process")
        error.exit_status = 124 if isinstance(cause, subprocess.TimeoutExpired) else 130
        raise error from cause
    if process.returncode:
        error = LocalError("PROCESS_FAILED", "process")
        error.exit_status = process.returncode
        raise error
    return stdout.decode("utf-8").strip()

def optional(command, **kwargs):
    try:
        return invoke(command, **kwargs)
    except (LocalError, UnicodeError):
        return None

def identities(root=ROOT):
    scripts = [p.relative_to(root).as_posix() for p in (root / "scripts/performance").glob("*")
               if p.suffix in (".py", ".sh", ".js")]
    return {p: sha256(root / p) for p in sorted(set(CONTRACT_PATHS) | set(scripts))}

def source_identity(root=ROOT, base=None):
    value = {"headSha": invoke(["git", "rev-parse", "HEAD"], cwd=root),
             "treeSha": invoke(["git", "rev-parse", "HEAD^{tree}"], cwd=root),
             "workingTreeClean": not invoke(["git", "status", "--porcelain",
                                             "--untracked-files=all"], cwd=root),
             "worktree": str(root.resolve()), "contractAndToolDigests": identities(root)}
    if base:
        if not SHA.fullmatch(base):
            raise LocalError("SOURCE_INVALID", "source")
        value["baseSha"] = base
        try:
            invoke(["git", "merge-base", "--is-ancestor", base, value["headSha"]], cwd=root)
            value["baseIsAncestor"] = True
        except LocalError:
            value["baseIsAncestor"] = False
    return value

def hardware_observation():
    value = {"os": platform.system(), "osVersion": platform.version(),
             "architecture": platform.machine(), "logicalCpus": os.cpu_count(),
             "physicalCores": None, "cpuModel": None, "physicalMemoryBytes": None,
             "availableMemoryBytes": None, "linuxKernel": None, "wsl": None,
             "cgroupCpuQuota": None, "cgroupMemoryLimit": None}
    if platform.system() == "Windows":
        command = ("$ErrorActionPreference='Stop'; $o=Get-CimInstance Win32_OperatingSystem; "
                   "$c=Get-CimInstance Win32_Processor; $s=Get-CimInstance Win32_ComputerSystem; "
                   "[PSCustomObject]@{os=$o.Caption;osVersion=$o.Version;cpuModel=($c.Name -join '; ');"
                   "physicalCores=($c.NumberOfCores|Measure-Object -Sum).Sum;"
                   "logicalCpus=($c.NumberOfLogicalProcessors|Measure-Object -Sum).Sum;"
                   "physicalMemoryBytes=$s.TotalPhysicalMemory;"
                   "availableMemoryBytes=([long]$o.FreePhysicalMemory*1024)}|ConvertTo-Json -Compress")
        raw = optional(["powershell.exe", "-NoProfile", "-Command", command])
        if raw:
            value.update(json.loads(raw))
        try:
            result = subprocess.run(["wsl.exe", "--list", "--quiet"], capture_output=True, timeout=30)
            value["wsl"] = {"registeredDistributions": [
                n.strip() for n in result.stdout.decode("utf-16-le").strip().splitlines()],
                "inventoryExitStatus": result.returncode}
        except (OSError, UnicodeError, subprocess.TimeoutExpired):
            value["wsl"] = {"registeredDistributions": [], "inventoryExitStatus": None}
    elif platform.system() == "Linux":
        value["linuxKernel"] = platform.release()
        info = Path("/proc/cpuinfo").read_text()
        value["cpuModel"] = next((s.split(":", 1)[1].strip() for s in info.splitlines()
                                  if s.startswith("model name")), None)
        pairs = set()
        for block in info.split("\n\n"):
            fields = {k.strip(): v.strip() for k, v in
                      (s.split(":", 1) for s in block.splitlines() if ":" in s)}
            if "physical id" in fields and "core id" in fields:
                pairs.add((fields["physical id"], fields["core id"]))
        value["physicalCores"] = len(pairs) or None
        memory = {s.split(":")[0]: int(s.split()[1]) * 1024
                  for s in Path("/proc/meminfo").read_text().splitlines()
                  if s.startswith(("MemTotal:", "MemAvailable:"))}
        value["physicalMemoryBytes"] = memory.get("MemTotal")
        value["availableMemoryBytes"] = memory.get("MemAvailable")
        value["linuxGuestMemoryBytes"] = value["physicalMemoryBytes"]
        if "microsoft" in platform.release().lower():
            value["linuxGuestLogicalCpus"] = value["logicalCpus"]
            value["linuxGuestPhysicalCoresObserved"] = value["physicalCores"]
            value["linuxGuestAvailableMemoryBytes"] = value["availableMemoryBytes"]
            host = optional(["powershell.exe", "-NoProfile", "-Command",
                "$c=Get-CimInstance Win32_Processor;$s=Get-CimInstance Win32_ComputerSystem;"
                "$o=Get-CimInstance Win32_OperatingSystem;"
                "[PSCustomObject]@{physicalMemoryBytes=$s.TotalPhysicalMemory;"
                "physicalCores=($c.NumberOfCores|Measure-Object -Sum).Sum;"
                "logicalCpus=($c.NumberOfLogicalProcessors|Measure-Object -Sum).Sum;"
                "cpuModel=($c.Name -join '; ');availableMemoryBytes=([long]$o.FreePhysicalMemory*1024)}"
                "|ConvertTo-Json -Compress"])
            if host:
                value.update(json.loads(host))
            else:
                value.update(physicalMemoryBytes=None, physicalCores=None, logicalCpus=None)
            value["wsl"] = {"version": 2, "physicalHostMemoryObserved":
                            value["physicalMemoryBytes"] is not None}
        for key, path in (("cgroupCpuQuota", "/sys/fs/cgroup/cpu.max"),
                          ("cgroupMemoryLimit", "/sys/fs/cgroup/memory.max")):
            if Path(path).exists():
                value[key] = Path(path).read_text().strip()
    return value

def preflight(output, *, root=ROOT, base=None):
    """Host/static preflight consumes no campaign and makes no capacity claim."""
    output = Path(output)
    output.mkdir(parents=True, exist_ok=True)
    probe = output / ".write-probe"
    probe.write_bytes(b"local-preflight")
    probe.unlink()
    hardware, source = hardware_observation(), source_identity(root, base)
    errors = []
    if platform.system() != "Linux":
        errors.append("LINUX_DOCKER_REQUIRED")
    if not source["workingTreeClean"]:
        errors.append("WORKING_TREE_DIRTY")
    if base and not source.get("baseIsAncestor"):
        errors.append("BASE_NOT_ANCESTOR")
    raw = optional(["docker", "info", "--format", "{{json .}}"])
    docker = None
    if raw:
        payload = json.loads(raw)
        docker = {k: payload.get(k) for k in (
            "ServerVersion", "OSType", "Architecture", "NCPU", "MemTotal", "CgroupVersion")}
        if docker["OSType"] != "linux":
            errors.append("LINUX_DOCKER_REQUIRED")
        if (hardware["physicalMemoryBytes"] is None
                or docker["MemTotal"] > hardware["physicalMemoryBytes"]
                or docker["NCPU"] > hardware["logicalCpus"]):
            errors.append("PHYSICAL_RESOURCE_BOUNDARY_UNVERIFIED")
    else:
        errors.append("DOCKER_UNAVAILABLE")
    compose = optional(["docker", "compose", "version", "--short"])
    if not compose or not re.fullmatch(r"v?2\..+", compose):
        errors.append("COMPOSE_V2_UNAVAILABLE")
    versions = {name: optional(cmd) for name, cmd in {
        "git": ["git", "--version"], "python": [__import__("sys").executable, "--version"],
        "dotnet": ["dotnet", "--version"], "node": ["node", "--version"]}.items()}
    if shutil.disk_usage(output).free < 1024 ** 3:
        errors.append("INSUFFICIENT_STORAGE")
    result = {"schemaVersion": 1, "phase": "host-preflight", "capturedAtUtc": now(),
              "acceptanceEligibility": "LOCAL_DIAGNOSTIC_ONLY", "hardware": hardware,
              "hardwareDigest": digest(hardware), "source": source, "docker": docker,
              "dockerCompose": compose, "toolVersions": versions,
              "freeStorageBytes": shutil.disk_usage(output).free,
              "environmentClass": None, "runtimeFingerprint": None,
              "postgresImageDigest": None, "k6ImageDigest": None,
              "errors": sorted(set(errors)), "measurementStarted": False,
              "measurementAttemptsConsumed": 0, "readyForRuntimePreflight": not errors}
    write_json_atomic(output / "host-preflight.json", result)
    return result

def failure(error, *, component, operation, sample_count=0):
    cause = error.__cause__ or error
    return {"failureStage": getattr(error, "stage", "collector"),
            "exceptionClass": type(cause).__name__,
            "sanitizedErrorCode": getattr(error, "code", "COLLECTOR_FAILED"),
            "failingComponent": component, "processExitStatus": getattr(error, "exit_status", None),
            "failingOperation": operation, "collectedSampleCount": sample_count,
            "classification": "collector-failure", "privateMessageRetained": False}
