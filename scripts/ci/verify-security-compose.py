#!/usr/bin/env python3
"""Fail closed when the resolved SEC-02/SEC-03/SEC-04/SEC-05/SEC-06/AUD-02 security profile drifts."""

from __future__ import annotations

import json
import os
import subprocess
import sys
from typing import Any


def fail(message: str) -> None:
    raise SystemExit(f"SEC-02 Compose invariant failed: {message}")


def require_mapping(value: Any, path: str) -> dict[str, Any]:
    if not isinstance(value, dict):
        fail(f"{path} must be an object")
    return value


def require_service(document: dict[str, Any], name: str) -> dict[str, Any]:
    services = require_mapping(document.get("services"), "services")
    service = services.get(name)
    if not isinstance(service, dict):
        fail(f"services.{name} is missing")
    return service


def require_environment(service: dict[str, Any], service_name: str) -> dict[str, Any]:
    environment = service.get("environment")
    if not isinstance(environment, dict):
        fail(f"services.{service_name}.environment must be an object")
    return environment


def require_env(environment: dict[str, Any], key: str, expected: str) -> None:
    actual = environment.get(key)
    if str(actual).lower() != expected.lower():
        fail(f"{key} expected {expected!r}, got {actual!r}")


def should_run_runtime_smoke() -> bool:
    # The public security-scan job intentionally provides only a synthetic
    # fixture credential. Requiring both GitHub Actions and that credential keeps
    # ordinary local `docker compose config | verify-security-compose.py` calls
    # side-effect free while making the required CI security gate exercise the
    # real PostgreSQL/runtime path.
    return (
        os.environ.get("GITHUB_ACTIONS", "").lower() == "true"
        and bool(os.environ.get("COGLATAS_SECURITY_CI_PASSWORD", "").strip())
        and os.environ.get("COGLATAS_SECURITY_CI_SKIP_RUNTIME_SMOKE", "").lower()
        not in {"1", "true", "yes"}
    )


def run_scanner_harness_contract_tests() -> None:
    # These tests are intentionally side-effect free: target validation happens
    # before network access and the redaction/identity/evidence contracts use
    # synthetic values only. Keeping them inside the required security gate
    # prevents a configuration edit from silently weakening scanner boundaries.
    subprocess.run(
        ["bash", "scripts/security/test-scanner-harness.sh"],
        check=True,
    )
    subprocess.run(
        ["bash", "scripts/security/test-schemathesis-contract.sh"],
        check=True,
    )
    subprocess.run(
        ["bash", "scripts/security/test-zap-contract.sh"],
        check=True,
    )
    subprocess.run(
        ["bash", "scripts/security/test-aud02-lifecycle.sh"],
        check=True,
    )


def main() -> None:
    try:
        document = json.load(sys.stdin)
    except json.JSONDecodeError as exc:
        fail(f"resolved Compose JSON is invalid: {exc}")

    document = require_mapping(document, "root")
    services = require_mapping(document.get("services"), "services")
    app = require_service(document, "app")
    app_env = require_environment(app, "app")

    expected_app = {
        "ASPNETCORE_ENVIRONMENT": "Test",
        "Tenancy__AppMode": "SaaS",
        "Tenancy__DefaultTenantSlug": "security-alpha",
        "Tenancy__TenantResolutionStrategy": "HeaderForDevelopmentOnly",
        "Tenancy__AllowTenantSwitching": "true",
        "Tenancy__AllowDevelopmentHeaderTenantResolution": "true",
        "Tenancy__AllowDevelopmentHeaderInProduction": "false",
        "Tenancy__DevelopmentTenantHeaderName": "X-Tenant-Slug",
        "Tenancy__SeedOnStartup": "false",
        "UiShell__SeedOnStartup": "false",
        "COGLATAS_BROWSER_SMOKE_SEED_ENABLED": "false",
        "COGLATAS_BROWSER_SMOKE_RESPONSE_GATE_ENABLED": "false",
        "COGLATAS_DEMO_DATASET_ENABLED": "false",
        "COGLATAS_SECURITY_CI_FIXTURE_ENABLED": "true",
        "Security__RequireHttps": "false",
        "Security__EnableHsts": "false",
        "Security__EnableCsrfProtection": "true",
        "Security__EnableRateLimiting": "false",
    }
    for key, expected in expected_app.items():
        require_env(app_env, key, expected)

    fixture_password = app_env.get("COGLATAS_SECURITY_CI_PASSWORD")
    if not isinstance(fixture_password, str) or not fixture_password.strip():
        fail("COGLATAS_SECURITY_CI_PASSWORD must resolve to a non-empty synthetic credential")

    if app.get("ports"):
        fail("security app must not publish a host port before a scanner explicitly needs one")

    migrate = require_service(document, "migrate")
    migrate_env = require_environment(migrate, "migrate")
    connection = str(migrate_env.get("ConnectionStrings__DefaultConnection", ""))
    if "Database=coglatas_security" not in connection:
        fail("migrate service must target the isolated coglatas_security database")

    postgres = require_service(document, "postgres")
    postgres_env = require_environment(postgres, "postgres")
    require_env(postgres_env, "POSTGRES_DB", "coglatas_security")

    # Docker Compose removes services behind inactive profiles from the default
    # resolved model. For the SEC-02 scanner stack, the inherited browser smoke
    # runner must therefore be absent unless the caller explicitly opts into its
    # `browser-smoke` profile. If it appears here, it would auto-start and pollute
    # the isolated security fixture with the unrelated browser-smoke workflow.
    if "real-backend-playwright" in services:
        fail("real-backend-playwright must be inactive in the default SEC-02 profile")

    print("SEC-02 resolved Compose invariants verified.")
    print("Running SEC-03/SEC-04/SEC-06/AUD-02 scanner contract tests inside the required security gate.")
    run_scanner_harness_contract_tests()

    if should_run_runtime_smoke():
        print("Generating the deterministic SEC-01 contract for the SEC-04/SEC-06 runtime gates.")
        subprocess.run(
            ["bash", "scripts/ci/generate-security-openapi-contract.sh"],
            check=True,
        )
        print("Running SEC-03/SEC-04/SEC-05/SEC-06/AUD-02 runtime gate.")
        subprocess.run(
            ["bash", "scripts/ci/run-security-runtime-smoke.sh"],
            check=True,
        )


if __name__ == "__main__":
    main()
