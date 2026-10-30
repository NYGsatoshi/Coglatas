#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import json
import os
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
COMMON_PATH = ROOT / "scripts" / "performance" / "common.py"
spec = importlib.util.spec_from_file_location("performance_common", COMMON_PATH)
if spec is None or spec.loader is None:
    raise SystemExit("PERF-02: cannot load scripts/performance/common.py")
common = importlib.util.module_from_spec(spec)
spec.loader.exec_module(common)


def fail(message: str) -> None:
    raise SystemExit(f"PERF-02 contract invalid: {message}")


def require_text(text: str, token: str, source: str) -> None:
    if token not in text:
        fail(f"{source} missing required token: {token}")


def require_source_sdk_image(rendered_compose: dict, sdk_version: str) -> None:
    """Validate actual Compose service image values, not comments or other keys.

    The public PR runs its application from the SDK image; both the app and
    migration service must use the exact pinned SDK from global.json. A
    leftover licensed production build in the merged override is forbidden.
    """
    services = rendered_compose.get("services")
    if not isinstance(services, dict):
        fail("rendered performance Compose has no services mapping")
    approved_tag = "11.0.100-rc.3" if sdk_version == "11.0.100-rc.3.26522.148" else sdk_version
    expected = re.compile(
        rf"mcr\.microsoft\.com/dotnet/sdk:{re.escape(approved_tag)}@sha256:[0-9a-f]{{64}}"
    )
    for service_name in ("app", "migrate"):
        service = services.get(service_name)
        if not isinstance(service, dict):
            fail(f"rendered performance Compose missing {service_name} service")
        image = service.get("image")
        if not isinstance(image, str) or not expected.fullmatch(image):
            fail(f"rendered performance {service_name} image must match global.json and use an immutable SHA-256 digest")
        if service_name == "app" and service.get("build") is not None:
            fail("public PR performance runtime must not have a licensed build")


def require_approved_sdk_version(sdk_config: object) -> str:
    """Keep both the production LTS and the explicitly pinned RC3 lab fail-closed."""
    if not isinstance(sdk_config, dict):
        fail("global.json must contain an SDK policy object")
    version = sdk_config.get("version")
    stable = (isinstance(version, str) and re.fullmatch(r"10\.0\.\d{3}", version)
              and sdk_config.get("allowPrerelease") is False)
    rc3 = (version == "11.0.100-rc.3.26522.148"
           and sdk_config.get("allowPrerelease") is True)
    if sdk_config.get("rollForward") != "disable" or not (stable or rc3):
        fail("global.json must pin an exact approved .NET 10 SDK or isolated .NET 11 RC3 SDK")
    return version


def render_source_compose(environment_path: Path, override_path: Path) -> dict:
    """Use Docker Compose's real !reset/merge semantics, without starting Docker.

    Only synthetic environment variables are passed. A successful textual
    pattern match in an unused comment is never accepted as an image pin.
    """
    environment = {
        "PATH": os.environ.get("PATH", "/usr/bin:/bin"),
        # Compose config must not depend on a publicly writable home directory.
        "HOME": os.environ.get("HOME", str(ROOT)),
        "COGLATAS_PERFORMANCE_PROFILE": "small",
        "COGLATAS_PERFORMANCE_PORT": "18080",
        "COGLATAS_PERFORMANCE_PASSWORD": "nonsecret-contract-verification",
        "COGLATAS_PERFORMANCE_EVIDENCE_DIR": str(ROOT / "artifacts/performance/contract-validation"),
    }
    command = [
        "docker", "compose", "--project-name", "coglatas-performance-validation",
        "-f", str(environment_path), "-f", str(override_path),
        "config", "--format", "json",
    ]
    try:
        result = subprocess.run(
            command, cwd=ROOT, env=environment, capture_output=True,
            text=True, check=False, timeout=30,
        )
    except (OSError, subprocess.TimeoutExpired):
        fail("cannot render the performance Compose configuration")
    if result.returncode != 0:
        fail("Docker Compose rejected the performance source-runtime override")
    try:
        config = json.loads(result.stdout)
    except json.JSONDecodeError:
        fail("Docker Compose returned invalid JSON configuration")
    if not isinstance(config, dict):
        fail("Docker Compose configuration must be a mapping")
    return config


def main() -> int:
    environment_path = ROOT / "performance" / "environment.json"
    compose_path = ROOT / "infra/compose/performance/environment.yml"
    harness_path = ROOT / "scripts" / "performance" / "with-environment.sh"
    pr_compose_path = ROOT / "infra/compose/performance/pr.yml"
    warmup_path = ROOT / "scripts" / "performance" / "warmup.py"
    seed_path = ROOT / "src" / "Coglatas.Infrastructure" / "Persistence" / "PerformanceCiFixtureSeed.cs"
    hosting_path = ROOT / "src" / "Coglatas.Web" / "Testing" / "PerformanceCiHostingStartup.cs"
    boundary_path = ROOT / "src" / "Coglatas.Web" / "Testing" / "PerformanceCiTestBoundary.cs"

    for path in (environment_path, compose_path, pr_compose_path, harness_path, warmup_path, seed_path, hosting_path, boundary_path):
        if not path.is_file():
            fail(f"missing required file: {path.relative_to(ROOT)}")

    environment = json.loads(environment_path.read_text(encoding="utf-8"))
    if environment.get("schemaVersion") != 1 or environment.get("fixtureVersion") != common.FIXTURE_VERSION:
        fail("environment schemaVersion/fixtureVersion must match the current fixture contract")
    require_text(seed_path.read_text(encoding="utf-8"), f"public const int FixtureVersion = {common.FIXTURE_VERSION};", str(seed_path.relative_to(ROOT)))
    if environment.get("dbFixtureVersion") != common.DB_FIXTURE_VERSION:
        fail("DB fixture version must match the current DB scenario contract")
    require_text(seed_path.read_text(encoding="utf-8"), f"public const int DbFixtureVersion = {common.DB_FIXTURE_VERSION};", str(seed_path.relative_to(ROOT)))
    target = environment.get("target")
    if not isinstance(target, dict):
        fail("target contract is missing")
    allowed_hosts = set(target.get("allowedHosts") or [])
    if allowed_hosts != set(common.SAFE_TARGET_HOSTS):
        fail("environment target allowlist must exactly match common.py SAFE_TARGET_HOSTS")
    if target.get("scheme") != "http" or target.get("requireExplicitPort") is not True:
        fail("target must be isolated HTTP with an explicit port")

    warmup = environment.get("warmup")
    if not isinstance(warmup, dict) or warmup.get("measured") is not False:
        fail("warm-up must be explicitly non-measured")
    if not isinstance(warmup.get("iterations"), int) or warmup["iterations"] <= 0:
        fail("warm-up iterations must be positive")
    routes = warmup.get("routes")
    if not isinstance(routes, list) or not routes or any(not isinstance(route, str) or not route.startswith("/") for route in routes):
        fail("warm-up routes must be a non-empty list of application-relative paths")
    cache = warmup.get("browserAssetCachePolicy")
    if not isinstance(cache, dict) or set(cache) != {"cold", "warm"}:
        fail("browser cache policy must state both cold and warm behavior")

    measurement = environment.get("measurement")
    if not isinstance(measurement, dict) or measurement.get("warmupSamplesExcluded") is not True:
        fail("measurement contract must exclude warm-up samples")
    if not isinstance(measurement.get("minimumSamples"), int) or measurement["minimumSamples"] <= 0:
        fail("measurement minimumSamples must be positive")

    failure_policy = environment.get("failurePolicy")
    required_failures = {
        "unhealthyApp", "incompleteFixture", "benchmarkExitOrTimeout", "insufficientSamples",
        "targetMismatch", "missingFingerprint", "unstableEnvironment",
    }
    if not isinstance(failure_policy, dict) or any(failure_policy.get(key) != "fail" for key in required_failures):
        fail("all PERF-02 failure/noise guards must fail closed")

    hashes = {}
    for profile in ("small", "medium", "large"):
        _, loaded = common.load_profile(profile)
        first_hash = common.fixture_hash(profile)
        second_hash = common.fixture_hash(profile)
        if first_hash != second_hash or not re.fullmatch(r"[0-9a-f]{64}", first_hash):
            fail(f"fixture hash for {profile} is not deterministic SHA-256")
        if loaded["counts"]["tasks"] != loaded["counts"]["workItems"]:
            fail(f"{profile} tasks/workItems contract drifted")
        hashes[profile] = first_hash
    if len(set(hashes.values())) != 3:
        fail("fixture hashes must distinguish small/medium/large profiles")

    compose = compose_path.read_text(encoding="utf-8")
    for token in (
        "image: postgres:18-alpine",
        "Database=coglatas_performance",
        '127.0.0.1:${COGLATAS_PERFORMANCE_PORT:-18080}:8080',
        "dockerfile: Dockerfile",
        "ASPNETCORE_ENVIRONMENT: Test",
        'COGLATAS_BROWSER_SMOKE_SEED_ENABLED: "false"',
        'COGLATAS_DEMO_DATASET_ENABLED: "false"',
        'COGLATAS_SECURITY_CI_FIXTURE_ENABLED: "false"',
        'COGLATAS_PERFORMANCE_CI_FIXTURE_ENABLED: "true"',
        "condition: service_completed_successfully",
        "/health/ready",
    ):
        require_text(compose, token, "infra/compose/performance/environment.yml")
    if re.search(r"https?://(?!0\.0\.0\.0|localhost|127\.0\.0\.1)", compose, re.IGNORECASE):
        fail("performance Compose must not contain a public benchmark target")

    # Read the SDK from the reviewed source of truth instead of pinning a
    # duplicate literal that would block every otherwise-compatible SDK patch.
    sdk_config = json.loads((ROOT / "global.json").read_text(encoding="utf-8")).get("sdk", {})
    sdk_version = require_approved_sdk_version(sdk_config)

    pr_compose = pr_compose_path.read_text(encoding="utf-8")
    for token in (
        "build: !reset null",
        "dotnet run --project src/Coglatas.Web/Coglatas.Web.csproj",
        "healthcheck: !reset null",
    ):
        require_text(pr_compose, token, "infra/compose/performance/pr.yml")
    if "SYNCFUSION_LICENSE" in pr_compose or "syncfusion_license" in pr_compose:
        fail("public PR performance runtime must not reference the Syncfusion credential")
    require_source_sdk_image(render_source_compose(compose_path, pr_compose_path), sdk_version)


    harness = harness_path.read_text(encoding="utf-8")
    for token in (
        "coglatas-performance-",
        "down --volumes --remove-orphans",
        "preflight.py",
        "warmup.py",
        "collect-environment.py",
        "COGLATAS_PERFORMANCE_RUNTIME_MODE",
        "infra/compose/performance/pr.yml",
        "verify-samples.py",
        'timeout "$COMMAND_TIMEOUT"',
    ):
        require_text(harness, token, "with-environment.sh")

    warmup_script = warmup_path.read_text(encoding="utf-8")
    for token in (
        "/api/security/csrf-token",
        "csrf_header: csrf_token",
        "\"csrfBootstrap\"",
    ):
        require_text(warmup_script, token, "warmup.py")

    seed = seed_path.read_text(encoding="utf-8")
    for token in (
        'DatabaseName = "coglatas_performance"',
        "NpgsqlConnectionStringBuilder",
        "AllowedDatabaseDataSources.Contains(configuredHost)",
        "GetPendingMigrationsAsync",
        "TRUNCATE TABLE",
        "StableGuid",
        "VerifyFixtureAsync",
        "fixtureHash",
        "migrationStatus = \"current\"",
    ):
        require_text(seed, token, "PerformanceCiFixtureSeed.cs")

    hosting = hosting_path.read_text(encoding="utf-8")
    for token in (
        "PerformanceCiTestBoundary.IsEnabled",
        "IHostedLifecycleService",
        "StartingAsync",
        "SetPlatformScope",
        "PerformanceCiFixtureSeed.SeedAsync",
    ):
        require_text(hosting, token, "PerformanceCiHostingStartup.cs")

    boundary = boundary_path.read_text(encoding="utf-8")
    require_text(boundary, 'string.Equals(environmentName, "Test"', "PerformanceCiTestBoundary.cs")

    print(json.dumps({
        "schemaVersion": 1,
        "profiles": hashes,
        "warmupRoutes": len(routes),
        "minimumSamples": measurement["minimumSamples"],
        "status": "ok",
    }, sort_keys=True))
    return 0


if __name__ == "__main__":
    sys.exit(main())
