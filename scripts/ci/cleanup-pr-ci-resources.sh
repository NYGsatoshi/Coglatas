#!/usr/bin/env bash
# Bounded runner-finalization fallback; never a workflow cancellation controller.
set -Eeuo pipefail

# Keep a hard bound even if filesystem cleanup stalls after Docker times out.
exec timeout --signal=TERM --kill-after=5s 100s python3 - "$@" <<'PY'
from __future__ import annotations

import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import time


class CleanupError(Exception):
    pass


class DockerFailure(CleanupError):
    pass


def require(condition: bool, code: str) -> None:
    if not condition:
        raise CleanupError(code)


def identity() -> tuple[str, list[tuple[str, Path | None]]]:
    require(len(sys.argv) == 2, "invalid-mode")
    mode = sys.argv[1]
    require(mode in {"frontend-test", "security-scan"}, "invalid-mode")
    require(os.environ.get("GITHUB_ACTIONS") == "true", "not-github-actions")
    require(os.environ.get("GITHUB_JOB") == mode, "job-mismatch")
    run = os.environ.get("GITHUB_RUN_ID", "")
    attempt = os.environ.get("GITHUB_RUN_ATTEMPT", "")
    require(re.fullmatch(r"[1-9][0-9]{0,19}", run) is not None, "invalid-run")
    require(re.fullmatch(r"[1-9][0-9]{0,5}", attempt) is not None, "invalid-attempt")
    if mode == "frontend-test":
        project = f"coglatas-playwright-{run}-{attempt}-frontend-test"
        require(os.environ.get("COMPOSE_PROJECT_NAME", project) in {"", project}, "project-override")
        return mode, [(project, None)]

    require(not os.environ.get("COMPOSE_PROJECT_NAME"), "project-override")
    require(not os.environ.get("COGLATAS_SECURITY_CI_PROJECT"), "project-override")
    require(not os.environ.get("COGLATAS_SECURITY_RUNTIME_STATE_DIR"), "state-override")
    root = Path(os.environ.get("RUNNER_TEMP", ""))
    require(root.is_absolute() and root != Path("/"), "invalid-runner-temp")
    require(root.is_dir() and root.resolve() == root, "unsafe-runner-temp")
    require(shutil.rmtree.avoids_symlink_attacks, "unsafe-filesystem-cleanup")
    projects = []
    for lane in ("core", "schemathesis", "zap"):
        project = f"coglatas-security-{run}-{attempt}-{lane}"
        state = root / project
        require(not state.is_symlink(), "state-symlink")
        require(not state.exists() or state.is_dir(), "invalid-state")
        projects.append((project, state))
    return mode, projects


deadline = time.monotonic() + 90


def docker(*arguments: str) -> str:
    remaining = deadline - time.monotonic()
    require(remaining > 0, "cleanup-deadline")
    try:
        result = subprocess.run(
            ["docker", *arguments], capture_output=True, text=True,
            timeout=min(10, remaining), check=False,
        )
    except subprocess.TimeoutExpired as exc:
        raise CleanupError("docker-timeout") from exc
    except OSError as exc:
        raise CleanupError("docker-unavailable") from exc
    if result.returncode != 0:
        # All callers supply a fixed resource kind and operation. Do not emit
        # raw arguments, identifiers, paths, daemon stderr or credential data.
        raise DockerFailure(f"docker-{arguments[0]}-{arguments[1]}-failed")
    require(len(result.stdout) <= 262144, "docker-output-limit")
    return result.stdout


def listed(kind: str, label: str) -> list[str]:
    arguments = [kind, "ls", "--quiet", "--filter", f"label={label}"]
    if kind == "container":
        arguments += ["--all", "--no-trunc"]
    elif kind == "network":
        arguments += ["--no-trunc"]
    values = docker(*arguments).splitlines()
    require(len(values) <= 100 and len(values) == len(set(values)), "resource-list-limit")
    pattern = r"[a-f0-9]{64}" if kind != "volume" else r"[a-zA-Z0-9][a-zA-Z0-9_.-]{0,254}"
    require(all(re.fullmatch(pattern, value) for value in values), "invalid-resource-id")
    return values


def absent(kind: str, resource: str) -> bool:
    # A failed inspect/remove alone does not prove absence. Require a successful
    # complete inventory, including stopped containers, and exact immutable ID
    # or volume-name comparison. Do not infer absence from daemon error text or
    # a name/ID filter whose matching semantics could omit the target.
    arguments = [kind, "ls", "--quiet"]
    if kind == "container":
        arguments += ["--all", "--no-trunc"]
    elif kind == "network":
        arguments += ["--no-trunc"]
    values = docker(*arguments).splitlines()
    require(len(values) <= 1000 and len(values) == len(set(values)), "absence-inventory-limit")
    pattern = r"[a-f0-9]{64}" if kind != "volume" else r"[a-zA-Z0-9][a-zA-Z0-9_.-]{0,254}"
    require(all(re.fullmatch(pattern, value) for value in values), "invalid-absence-inventory")
    return resource not in values


def inspected(kind: str, resource: str) -> dict | None:
    try:
        content = docker(kind, "inspect", resource)
    except DockerFailure:
        if absent(kind, resource):
            return None
        raise
    try:
        value = json.loads(content)
    except json.JSONDecodeError as exc:
        raise CleanupError("invalid-resource-metadata") from exc
    require(isinstance(value, list) and len(value) == 1 and isinstance(value[0], dict), "invalid-resource-metadata")
    record = value[0]
    require(record.get("Name" if kind == "volume" else "Id") == resource, "resource-identity-mismatch")
    return record


def removed(kind: str, resource: str) -> None:
    arguments = [kind, "rm"]
    if kind == "container":
        arguments += ["--force"]
    try:
        docker(*arguments, resource)
    except DockerFailure:
        # docker run --rm and existing worker EXIT cleanup can remove the
        # resource after our ownership check. Accept only proven disappearance;
        # a retained resource or failed inventory still fails the finalizer.
        if not absent(kind, resource):
            raise


def container_labels(record: dict) -> dict:
    config = record.get("Config")
    require(isinstance(config, dict), "invalid-container-metadata")
    labels = config.get("Labels") or {}
    require(isinstance(labels, dict), "invalid-container-labels")
    return labels


def owns_scanner_mount(record: dict, state: Path | None) -> bool:
    if state is None:
        return False
    mounts = record.get("Mounts")
    if not isinstance(mounts, list):
        return False
    for mount in mounts:
        if not isinstance(mount, dict) or mount.get("Type") != "bind":
            continue
        source = mount.get("Source")
        if not isinstance(source, str):
            continue
        path = Path(source)
        if path.is_absolute() and ".." not in path.parts and path.resolve().is_relative_to(state):
            return True
    return False


def cleanup_project(project: str, state: Path | None) -> None:
    owner = f"com.docker.compose.project={project}"
    containers = set(listed("container", owner))
    networks = set(listed("network", owner))
    if state is not None:
        networks.update(listed("network", f"coglatas.security.project={project}"))
    volumes = listed("volume", owner)

    # Validate the complete target set before deleting anything in this project.
    for resource in sorted(containers):
        record = inspected("container", resource)
        if record is None:
            containers.remove(resource)
            continue
        require(container_labels(record).get("com.docker.compose.project") == project, "container-owner-mismatch")
    for resource in sorted(networks):
        record = inspected("network", resource)
        if record is None:
            networks.remove(resource)
            continue
        labels = record.get("Labels") or {}
        require(isinstance(labels, dict), "invalid-network-labels")
        default_owned = record.get("Name") == f"{project}_default" and labels.get("com.docker.compose.project") == project
        scanner_owned = state is not None and record.get("Name") == f"{project}_sec06_zap" and labels.get("coglatas.security.project") == project and labels.get("coglatas.security.control") == "SEC-06"
        require(default_owned or scanner_owned, "network-owner-mismatch")
        endpoints = record.get("Containers") or {}
        require(isinstance(endpoints, dict) and len(endpoints) <= 100, "invalid-network-endpoints")
        for endpoint in endpoints:
            require(isinstance(endpoint, str) and re.fullmatch(r"[a-f0-9]{64}", endpoint) is not None, "invalid-endpoint-id")
            if endpoint not in containers:
                # Standalone scanner/curl containers lack Compose labels. They
                # must be attached to this owned network AND bind its exact
                # per-run credential subtree. Merely sharing a network is not
                # permission to remove an unrelated container.
                container = inspected("container", endpoint)
                if container is None:
                    continue
                require(owns_scanner_mount(container, state), "unowned-network-endpoint")
                containers.add(endpoint)
    retained_volumes = []
    for resource in volumes:
        record = inspected("volume", resource)
        if record is None:
            continue
        labels = record.get("Labels") or {}
        require(isinstance(labels, dict) and labels.get("com.docker.compose.project") == project, "volume-owner-mismatch")
        retained_volumes.append(resource)

    for resource in sorted(containers):
        removed("container", resource)
    for resource in sorted(networks):
        removed("network", resource)
    for resource in retained_volumes:
        removed("volume", resource)
    for kind in ("container", "network", "volume"):
        require(not listed(kind, owner), "residual-owned-resource")
    if state is not None:
        require(not listed("network", f"coglatas.security.project={project}"), "residual-scanner-network")


def main() -> int:
    try:
        mode, projects = identity()
    except (CleanupError, OSError) as exc:
        code = str(exc) if isinstance(exc, CleanupError) else "identity-filesystem-error"
        print(f"PR CI cleanup refused: {code}.", file=sys.stderr)
        return 1

    failed = False
    for project, state in projects:
        try:
            cleanup_project(project, state)
        except (CleanupError, OSError) as exc:
            failed = True
            code = str(exc) if isinstance(exc, CleanupError) else "cleanup-filesystem-error"
            print(f"PR CI cleanup failed: {code}.", file=sys.stderr)
        finally:
            if state is not None:
                try:
                    # Never load runtime-state.env as shell code. Cancellation
                    # can interrupt its creation; all scanner credentials live
                    # beneath this independently derived directory.
                    require(not state.is_symlink(), "state-symlink")
                    if state.exists():
                        shutil.rmtree(state)
                except (CleanupError, OSError):
                    failed = True
                    print("PR CI credential cleanup failed.", file=sys.stderr)
    print(f"PR CI resource cleanup: job={mode}; result={'failure' if failed else 'success'}.")
    return int(failed)


raise SystemExit(main())
PY
