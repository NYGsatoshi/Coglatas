"""Versioned hard environment compatibility; hardware remains separate evidence."""
from __future__ import annotations

import hashlib
import copy
import json
import math
import os
import platform
import re
from pathlib import Path

POLICY_VERSION = "performance-environment-class-v1"
ROOT = Path(__file__).resolve().parents[2]
CLASS_FIELDS = {"schemaVersion", "policyVersion", "provider", "runnerClass", "osFamily", "osVersionClass",
                "architecture", "vcpuClass", "memoryClass", "runtimeClass", "toolchainClass",
                "benchmarkConfiguration", "workloadDefinition", "benchmarkImplementation", "benchmarkSchema",
                "runtimeConfiguration", "fixture"}
HARDWARE_FIELDS = {"cpuModel", "microcode", "hostGeneration", "physicalHostIdentity", "kernel", "runnerImage", "memoryBytes"}


class EnvironmentClassError(ValueError):
    pass


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(",", ":"), ensure_ascii=False).encode()).hexdigest()


def file_identities(root, paths):
    return {path: hashlib.sha256((root / path).read_bytes()).hexdigest() for path in paths}


def architecture(value):
    aliases = {"amd64": "x86_64", "x64": "x86_64", "x86_64": "x86_64", "arm64": "aarch64", "aarch64": "aarch64"}
    try:
        return aliases[value.lower()]
    except (KeyError, AttributeError):
        raise EnvironmentClassError("missing-or-unsupported-architecture") from None


def memory_class(value):
    # Provisioned GiB class, not exact available bytes. At most 1 GiB OS reservation.
    if type(value) is not int or value <= 0:
        raise EnvironmentClassError("missing-or-invalid-memory")
    gib = value / (1024 ** 3)
    provisioned = math.ceil(gib)
    if provisioned not in (4, 8, 16, 32, 64) or gib < provisioned - 1:
        raise EnvironmentClassError("unsupported-memory-class")
    return f"{provisioned}GiB"


def major(value):
    match = re.search(r"(?:^|[^0-9])([0-9]+)(?:\.|\b)", value or "")
    if not match:
        raise EnvironmentClassError("missing-toolchain-major")
    return int(match[1])


def sdk_major(value):
    match = re.search(r"\.NET SDK(?::|s installed:)?\s*(?:Version:\s*)?([0-9]+)", value or "", re.IGNORECASE)
    if not match:
        raise EnvironmentClassError("missing-compiler-major")
    return int(match[1])


def hardware_fingerprint(fp):
    runner = fp["runner"]
    return {"cpuModel": runner["cpuModel"], "microcode": runner.get("microcode"),
            "hostGeneration": runner.get("hostGeneration"), "physicalHostIdentity": runner.get("physicalHostIdentity"),
            "kernel": runner["os"], "runnerImage": runner["runnerImage"], "memoryBytes": runner["memoryBytes"]}


def live_runner_class_attributes():
    provider = os.environ.get("RUNNER_ENVIRONMENT") or "local"
    return {"provider": provider, "runnerClass": "standard" if provider == "github-hosted" else "custom",
            "architecture": platform.machine(), "osFamily": "ubuntu" if platform.system() == "Linux" else {"Windows":"windows", "Darwin":"macos"}.get(platform.system()),
            "osVersionClass": platform.freedesktop_os_release().get("VERSION_ID") if platform.system() == "Linux" else platform.release()}


def enrich_live_legacy_fingerprint(fp):
    """Bridge an old collector only while measuring on the same observed host."""
    if "provider" in fp["runner"]:
        return fp, None
    if fp["runner"]["os"] != platform.platform() or fp["runner"]["cpuCount"] != os.cpu_count():
        raise EnvironmentClassError("legacy-fingerprint-is-not-from-current-execution")
    if fp["commitSha"] != os.environ.get("GITHUB_SHA", fp["commitSha"]):
        raise EnvironmentClassError("legacy-source-is-not-current-collection-source")
    original = copy.deepcopy(fp)
    normalized = copy.deepcopy(fp)
    normalized["runner"].update(live_runner_class_attributes())
    normalized["environmentClass"] = environment_class(normalized)
    return normalized, original


def environment_class(fp, root=ROOT, *, authenticated_legacy_github=False):
    """Legacy host attributes may be derived only after authenticating its GitHub run."""
    runner = fp["runner"]
    provider = runner.get("provider")
    arch = runner.get("architecture")
    os_family = runner.get("osFamily")
    os_version = runner.get("osVersionClass")
    runner_class = runner.get("runnerClass")
    if authenticated_legacy_github and provider is None:
        if runner["runnerImage"] not in ("ubuntu24", "ubuntu-24.04") or runner["runnerOs"] != "Linux":
            raise EnvironmentClassError("legacy-os-class-not-authenticated")
        match = re.search(r"(?:^|-)(x86_64|aarch64)(?:-|$)", runner["os"])
        if not match:
            raise EnvironmentClassError("legacy-architecture-not-observed")
        provider, runner_class, arch, os_family, os_version = "github-hosted", "standard", match[1], "ubuntu", "24.04"
    if provider not in ("github-hosted", "self-hosted", "local") or not isinstance(runner_class, str) or not runner_class:
        raise EnvironmentClassError("missing-runner-provider-class")
    if os_family not in ("ubuntu", "windows", "macos") or not isinstance(os_version, str) or not re.fullmatch(r"[0-9]+(?:\.[0-9]+)?", os_version):
        raise EnvironmentClassError("missing-os-family-version-class")
    if type(runner["cpuCount"]) is not int or runner["cpuCount"] <= 0:
        raise EnvironmentClassError("invalid-vcpu-class")
    fixture = fp["fixture"]
    db = fixture["version"] == 2
    configuration = ("performance/environment.json", "performance/comparison-policy.json")
    workload = ("performance/datasets.json", "performance/db-scenarios.json") if db else ("performance/datasets.json", "performance/api-k6.json")
    implementation = ("scripts/performance/db-probe.py", "scripts/performance/db_gate.py", "src/Coglatas.Infrastructure/Persistence/PerformanceDbCapture.cs") if db else ("scripts/performance/api-k6.js",)
    # The policy adapter is separate from the measured workload implementation.
    value = {"schemaVersion": 1, "policyVersion": POLICY_VERSION, "provider": provider, "runnerClass": runner_class,
             "osFamily": os_family, "osVersionClass": os_version, "architecture": architecture(arch),
             "vcpuClass": runner["cpuCount"], "memoryClass": memory_class(runner["memoryBytes"]),
             "runtimeClass": {"dotnet": fp["dotnet"]["runtimeInfo"], "postgresql": fp["postgresql"]["version"],
                              "postgresImage": fp["containerImages"]["postgres"], "browserImage": fp["containerImages"]["performanceBrowser"]},
             "toolchainClass": {"dotnetCompilerMajor": sdk_major(fp["dotnet"]["sdkInfo"]), "nodeMajor": major(fp["node"]["version"]),
                                "playwright": fp["browser"]["playwrightVersion"], "browserMajor": major(fp["browser"]["version"])},
             "benchmarkConfiguration": file_identities(root, configuration), "workloadDefinition": file_identities(root, workload),
             "benchmarkImplementation": file_identities(root, implementation),
             "benchmarkSchema": {"collector": 1, "result": 1, "fixture": fixture["version"], "kind": "db" if db else "api"},
             "runtimeConfiguration": fp.get("applicationRuntime", {"legacyApplicationImage": fp["containerImages"]["app"]}),
             "fixture": {key: fixture[key] for key in ("profile", "hash", "version")}}
    validate_class(value)
    return value


def validate_class(value):
    if not isinstance(value, dict) or set(value) != CLASS_FIELDS or value["schemaVersion"] != 1 or value["policyVersion"] != POLICY_VERSION:
        raise EnvironmentClassError("invalid-environment-class-schema")
    if value["provider"] not in ("github-hosted", "self-hosted", "local") or architecture(value["architecture"]) != value["architecture"]:
        raise EnvironmentClassError("invalid-environment-class-provider-architecture")
    if type(value["vcpuClass"]) is not int or value["vcpuClass"] <= 0 or value["memoryClass"] not in ("4GiB", "8GiB", "16GiB", "32GiB", "64GiB"):
        raise EnvironmentClassError("invalid-environment-class-resources")
    for field in ("benchmarkConfiguration", "workloadDefinition", "benchmarkImplementation"):
        if not isinstance(value[field], dict) or not value[field] or any(not re.fullmatch(r"[0-9a-f]{64}", v or "") for v in value[field].values()):
            raise EnvironmentClassError("invalid-benchmark-identity")
    return value


def compatibility(current_class, baseline_class, current_hardware, baseline_hardware):
    validate_class(current_class)
    validate_class(baseline_class)
    if current_class != baseline_class:
        return "INCOMPATIBLE"
    return "HARD_COMPATIBLE" if current_hardware == baseline_hardware else "HARDWARE_VARIANT"


def class_name(value):
    return f"{value['provider']}-{value['osFamily']}{value['osVersionClass']}-{value['architecture']}-{value['vcpuClass']}C-{value['memoryClass']}"
