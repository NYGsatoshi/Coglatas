#!/usr/bin/env python3
"""One execution of the documented logical restore, with unchanged strict oracles.

Advisory synthetic qualification only. This never repairs a restore, retries an
acceptance operation, changes recovery strategy, or accepts an external target.
"""
from __future__ import annotations

import importlib.util
import json
import os
from pathlib import Path
import re
import secrets
import subprocess
import sys
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
PUBLIC = ROOT / "artifacts/synthetic-logical-recovery-ci/result.json"


def module(path: Path, name: str):
    spec = importlib.util.spec_from_file_location(name, path)
    loaded = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


support = module(ROOT / "scripts/ci/run-synthetic-physical-recovery-ci.py", "logical_publication_support")
probe = module(ROOT / "scripts/ci/synthetic-physical-recovery-probe.py", "logical_original_probe")
COMPARISONS = support.COMPARISONS
CONTROLS = {key: "PASS" for key in (
    "sourceApplication", "assemblyBinding", "sourceFile", "sourceGuards",
    "oracleNegatives", "targetInitiallyEmpty", "sourceQuiescent", "ownedResources")}


def strict_comparisons(before: dict, after: dict) -> dict:
    support.require(isinstance(before, dict) and isinstance(after, dict)
                    and set(before) == set(after) == set(COMPARISONS))
    result = {}
    for name in COMPARISONS:
        left, right = before[name], after[name]
        support.require(type(left) is bytes and type(right) is bytes)
        result[name] = {"sourceSha256": support.sha(left), "restoredSha256": support.sha(right),
                        "exactBytesEqual": left == right}
    return result


def comparisons_pass(values: dict) -> bool:
    if not isinstance(values, dict) or set(values) != set(COMPARISONS):
        return False
    return all(isinstance(item, dict)
               and set(item) == {"sourceSha256", "restoredSha256", "exactBytesEqual"}
               and support.digest_value(item["sourceSha256"])
               and support.digest_value(item["restoredSha256"])
               and item["sourceSha256"] == item["restoredSha256"]
               and item["exactBytesEqual"] is True for item in values.values())


def logical_commands(source: str, target: str) -> dict:
    support.require(all(isinstance(value, str) and re.fullmatch(r"[a-f0-9]{64}", value)
                        for value in (source, target)) and source != target)
    return {
        "dump": probe.DOCKER + ["exec", source, "pg_dump", "-U", "postgres", "-d", "recovery",
                                "--format=custom", "--no-owner", "--no-privileges"],
        "restore": probe.DOCKER + ["exec", "-i", target, "pg_restore", "-U", "postgres", "-d", "recovery",
                                   "--no-owner", "--no-privileges", "--exit-on-error"],
    }


def transfer_once(source: str, target: str, private: Path, result: dict, environment: dict,
                  runner=subprocess.run) -> None:
    """Exactly one dump and at most one restore; retain the actual first result."""
    commands = logical_commands(source, target)
    support.require(not result.get("commands"))
    result["commands"] = {name: {"argv": args, "exitCode": None, "timedOut": False}
                          for name, args in commands.items()}
    for name in ("dump", "restore"):
        result["phase"] = name.upper()
        data = None if name == "dump" else (private / "database.dump").read_bytes()
        try:
            process = runner(commands[name], input=data, cwd=ROOT, env=environment,
                             capture_output=True, check=False, timeout=120)
        except subprocess.TimeoutExpired:
            result["commands"][name]["timedOut"] = True
            return
        result["commands"][name]["exitCode"] = process.returncode
        (private / (name + ".stdout")).write_bytes(process.stdout)
        (private / (name + ".stderr")).write_bytes(process.stderr)
        if process.returncode != 0:
            return
        if name == "dump":
            support.require(bool(process.stdout))
            (private / "database.dump").write_bytes(process.stdout)
            result["dumpSha256"] = support.sha(process.stdout)


def candidate_complete(result: dict) -> bool:
    commands = result.get("commands")
    if not isinstance(commands, dict) or set(commands) != {"dump", "restore"}:
        return False
    identifiers = result.get("resourceIds")
    if not isinstance(identifiers, list) or len(identifiers) != 2:
        return False
    try:
        expected = logical_commands(identifiers[0], identifiers[1])
    except (support.Rejected, KeyError, IndexError, TypeError):
        return False
    return (all(isinstance(commands[name], dict)
                and set(commands[name]) == {"argv", "exitCode", "timedOut"}
                and commands[name]["argv"] == expected[name]
                and type(commands[name]["exitCode"]) is int and commands[name]["exitCode"] == 0
                and commands[name]["timedOut"] is False for name in expected)
            and support.digest_value(result.get("dumpSha256"))
            and comparisons_pass(result.get("comparisons"))
            and result.get("controls") == CONTROLS
            and result.get("cleanupState") == "VERIFIED"
            and type(result.get("admittedContainers")) is int and result["admittedContainers"] == 2
            and type(result.get("stoppedContainers")) is int and result["stoppedContainers"] == 2)


def prepare(private: Path, result: dict, environment: dict) -> tuple[Path, str, dict]:
    def command(args, phase, extra=None):
        result["phase"] = phase
        selected = environment.copy()
        if extra:
            selected.update(extra)
        process = subprocess.run(args, cwd=ROOT, env=selected, capture_output=True, check=False, timeout=600)
        (private / (phase + ".stdout")).write_bytes(process.stdout)
        (private / (phase + ".stderr")).write_bytes(process.stderr)
        support.require(process.returncode == 0)
        return process.stdout
    source = command(["git", "rev-parse", "HEAD"], "SOURCE").decode().strip()
    support.require(re.fullmatch(r"[a-f0-9]{40}", source) is not None and source == os.environ.get("GITHUB_SHA"))
    result["sourceSha"] = source
    source_tree = command(["git", "rev-parse", "HEAD:src"], "SOURCE_TREE").decode().strip()
    sdk = support.closed_json((ROOT / "global.json").read_bytes())["sdk"]["version"]
    support.require(command(["dotnet", "--version"], "SDK").decode().strip() == sdk)
    (ROOT / "src/Coglatas.Web/wwwroot").mkdir(exist_ok=True)
    config = private / "NuGet.Config"
    config.write_text('<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>')
    command(["dotnet", "restore", "src/Coglatas.Web/Coglatas.Web.csproj", "--configfile", str(config),
             "--disable-parallel", "--verbosity", "minimal"], "RESTORE_PACKAGES")
    command(["dotnet", "tool", "restore", "--configfile", str(config)], "TOOLS")
    command(["dotnet", "build", "src/Coglatas.Web/Coglatas.Web.csproj", "--configuration", "Release",
             "--no-restore", "--disable-build-servers", "-m:1"], "BUILD")
    ddl = private / "original.sql"
    command(["dotnet", "ef", "migrations", "script", "--project", "src/Coglatas.Infrastructure",
             "--startup-project", "src/Coglatas.Web", "--configuration", "Release", "--no-build",
             "--output", str(ddl)], "DDL", {"ConnectionStrings__DefaultConnection": "Host=unused;Database=unused;Username=unused"})
    command(probe.DOCKER + ["info", "--format", "{{.ServerVersion}}"], "DOCKER")
    command(probe.DOCKER + ["pull", "postgres:18.6-alpine"], "IMAGE_PULL")
    image = support.closed_json(command(probe.DOCKER + ["image", "inspect", "--format",
                                '{"id":{{json .Id}},"digests":{{json .RepoDigests}}}', "postgres:18.6-alpine"], "IMAGE_ID"))
    support.require(isinstance(image, dict) and set(image) == {"id", "digests"}
                    and isinstance(image["id"], str) and re.fullmatch(r"sha256:[a-f0-9]{64}", image["id"]) is not None)
    support.require(isinstance(image["digests"], list) and bool(image["digests"]) and all(
        isinstance(item, str) and re.fullmatch(r"(?:docker.io/library/)?postgres@sha256:[a-f0-9]{64}", item)
        for item in image["digests"]))
    binaries = ROOT / "src/Coglatas.Web/bin/Release/net10.0"
    expected = {"sourceSha": source, "sourceTree": source_tree, "sdkVersion": sdk,
                "probeSha256": support.sha(Path(probe.__file__).read_bytes()),
                "callerSha256": support.sha(Path(__file__).read_bytes()),
                "publicationSupportSha256": support.sha(Path(support.__file__).read_bytes()),
                "ddlSha256": support.sha(ddl.read_bytes()),
                "postgresImageId": image["id"], "postgresRepoDigests": image["digests"],
                "assemblies": {name: support.sha((binaries / name).read_bytes()) for name in support.ASSEMBLIES}}
    result["bindings"] = expected
    return ddl, image["id"], expected


def execute(private: Path, ddl: Path, image: str, expected: dict, result: dict, environment: dict) -> None:
    result["phase"] = "SOURCE_CONTROL"
    sockets = [private / "s", private / "t"]
    support.require(all(len(str(path)) <= 85 for path in sockets))
    for path in sockets:
        path.mkdir(mode=0o777)
        path.chmod(0o777)  # Fresh Unix socket directories under a private 0700 parent.
    identifier = "coglatas-synthetic-recovery-" + uuid.uuid4().hex[:12]
    names = [identifier + "-logical-source", identifier + "-logical-target"]
    volumes = [identifier + "-logical-source", identifier + "-logical-target"]
    admitted = []
    application = probe.SyntheticApplication(ROOT, private)
    result["failureCategory"] = "SOURCE_CONTROL_FAILED"
    try:
        probe.assert_fresh_resources(names, volumes)
        build = probe.build_application(ROOT, private, ddl.read_bytes())
        support.require(all(build[key] == expected[key] for key in ("sourceSha", "sourceTree", "sdkVersion", "assemblies"))
                        and build["buildExitCode"] == 0 and build["generatedDdlSha256"] == expected["ddlSha256"])
        for name in volumes:
            probe.docker("volume", "create", "--label", "coglatas.synthetic-recovery=" + identifier, name)
            probe.validate_volume(name, identifier)
        for name, volume, socket in zip(names, volumes, sockets):
            mounts = {"/var/lib/postgresql": volume, "/var/run/postgresql": str(socket)}
            container = probe.docker("run", "-d", "--name", name, "--network", "none",
                                     "--label", "coglatas.synthetic-recovery=" + identifier,
                                     "-e", "POSTGRES_PASSWORD=" + secrets.token_hex(24), "-e", "POSTGRES_DB=recovery",
                                     "--mount", f"type=volume,source={volume},target=/var/lib/postgresql",
                                     "--mount", f"type=bind,source={socket},target=/var/run/postgresql",
                                     image).decode().strip()
            support.require(re.fullmatch(r"[a-f0-9]{64}", container) is not None)
            probe.validate_container(container, identifier, image, mounts, container)
            admitted.append((container, mounts))
            for _ in range(100):
                try:
                    support.require(probe.sql(container, "SELECT 1;") == b"1\n")
                    break
                except probe.ProbeFailure:
                    time.sleep(0.1)
            else:
                raise support.Rejected("DATABASE_NOT_READY")
        source, target = [item[0] for item in admitted]
        result["resourceIds"] = [source, target]  # Fresh synthetic IDs only, never connection credentials.
        result["controls"]["ownedResources"] = "PASS"
        version = probe.sql(source, "SHOW server_version;").decode().strip()
        support.require(re.fullmatch(r"18\.6(?: \([A-Za-z0-9 ._+:/-]{1,160}\))?", version) is not None
                        and probe.sql(target, "SHOW server_version;").decode().strip() == version)
        result["bindings"]["postgresServerVersion"] = version
        support.require(probe.sql(target, "SELECT count(*) FROM pg_tables WHERE schemaname NOT IN "
                                  "('pg_catalog','information_schema');") == b"0\n")
        result["controls"]["targetInitiallyEmpty"] = "PASS"
        probe.sql(source, ddl.read_text())
        guards = probe.trigger_controls(source, ROOT)
        migration = ROOT / "src/Coglatas.Infrastructure/Persistence/Migrations"
        support.require(guards == {
            "revisionHeadNonzeroInsert": "DENIED_23514_WITH_ZERO_POSITIVE",
            "guardSourceSha256": support.sha((migration / "20260924133000_AddCoglatasUiCanonicalChangeJournal.cs").read_bytes()),
            "fileVersionArchiveCopyAfterCapture": "DENIED_23505_WITH_CAPTURE_POSITIVE",
            "fileTriggerSourceSha256": support.sha((migration / "20260901150000_AddFileVersionsActivityProjection.cs").read_bytes())})
        result["controls"]["sourceGuards"] = "PASS"
        application.start(sockets[0], seed=True, state=private / "source-state", phase="source")
        support.require(application.loaded_assemblies == expected["assemblies"])
        result["controls"]["assemblyBinding"] = "PASS"
        application.login()
        status, _ = application.request("/api/auth/me")
        support.require(status == 200)
        result["controls"]["sourceApplication"] = "PASS"
        source_file = application.check_file(source)
        support.require(source_file["status"] == "PASS" and type(source_file["bytes"]) is int
                        and source_file["bytes"] > 0 and support.digest_value(source_file["sha256"]))
        result["sourceFile"] = source_file
        result["controls"]["sourceFile"] = "PASS"
        application.stop()  # All app writers are stopped before capture.
        probe.sql(source, "CREATE SCHEMA recovery_control; CREATE SEQUENCE recovery_control.sequence_probe; "
                  "CREATE TABLE recovery_control.oracle_probe (id integer PRIMARY KEY, marker text NOT NULL); "
                  "INSERT INTO recovery_control.oracle_probe VALUES (1,'checkpoint'); "
                  "SELECT setval('recovery_control.sequence_probe',37,false);")
        before = probe.snapshot(source)
        support.require(probe.oracle_negative_controls(source, before) == support.ORACLE)
        result["controls"]["oracleNegatives"] = "PASS"
        result["failureCategory"] = "LOGICAL_COMMAND_FAILED"
        transfer_once(source, target, private, result, environment)
        # Preserve first command failure even if a partial target cannot be read.
        commands_ok = all(type(result["commands"][key]["exitCode"]) is int
                          and result["commands"][key]["exitCode"] == 0
                          and result["commands"][key]["timedOut"] is False for key in ("dump", "restore"))
        result["phase"] = "STRICT_COMPARISON"
        try:
            result["comparisons"] = strict_comparisons(before, probe.snapshot(target))
        except (probe.ProbeFailure, support.Rejected, OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
            if commands_ok:
                result["failureCategory"] = "STRICT_SNAPSHOT_UNAVAILABLE"
            return
        if commands_ok:
            result["failureCategory"] = "SOURCE_CHANGED_DURING_CAPTURE"
        support.require(probe.snapshot(source) == before)
        result["controls"]["sourceQuiescent"] = "PASS"
        if not commands_ok:
            return
        result["failureCategory"] = "STRICT_RESTORED_STATE_MISMATCH"
        if not comparisons_pass(result["comparisons"]):
            return
        result["failureCategory"] = "CLEANUP_NOT_VERIFIED"
    finally:
        application_stopped = True
        try:
            application.stop()
        except (OSError, subprocess.SubprocessError):
            application_stopped = False
        stopped = 0
        for container, mounts in admitted:
            try:
                probe.validate_container(container, identifier, image, mounts, container)
                probe.docker("stop", "--time", "10", container)
                stopped += 1
            except (probe.ProbeFailure, OSError, subprocess.SubprocessError, ValueError, TypeError, KeyError):
                pass
        result["admittedContainers"], result["stoppedContainers"] = len(admitted), stopped
        verified = (len(admitted) == 2 and len({item[0] for item in admitted}) == 2
                    and stopped == 2 and application_stopped)
        result["cleanupState"] = "VERIFIED" if verified else "INCOMPLETE"


def main() -> int:
    os.umask(0o077)
    result = {"schemaVersion": 1, "kind": "ADVISORY_SYNTHETIC_LOGICAL_RECOVERY",
              "status": "FAIL", "phase": "ADMISSION", "failureCategory": "CALLER_REJECTED",
              "sourceSha": None, "sourceHeadSha": None, "baseSha": None, "runId": None, "runAttempt": None,
              "bindings": {}, "controls": {}, "comparisons": {}, "commands": {}, "dumpSha256": None,
              "resourceIds": [], "admittedContainers": 0, "stoppedContainers": 0, "cleanupState": "INCOMPLETE",
              "formalStrategyChanged": False, "formalRecoveryAcceptance": "NOT_GRANTED", "publicGo": False,
              "databaseAuthorityScope": "SYNTHETIC_POSTGRES_BOOTSTRAP_ROLE_ONLY",
              "fullApplicationRecovery": "NOT_EXECUTED", "targetHttps": "NOT_EXECUTED",
              "rollbackRehearsal": "NOT_EXECUTED", "privateEvidenceAfterHostedTeardown": "NOT_RETAINED"}
    try:
        support.require(os.environ.get("COGLATAS_RECOVERY_RUNNER_ENVIRONMENT") == "github-hosted"
                        and os.environ.get("GITHUB_EVENT_NAME") == "pull_request"
                        and os.environ.get("RUNNER_OS") == "Linux")
        support.require(not PUBLIC.exists() and not PUBLIC.is_symlink())
        for variable, key in (("GITHUB_RUN_ID", "runId"), ("GITHUB_RUN_ATTEMPT", "runAttempt")):
            support.require(re.fullmatch(r"[1-9][0-9]*", os.environ.get(variable, "")) is not None)
            result[key] = int(os.environ[variable])
        for variable, key in (("COGLATAS_PR_HEAD_SHA", "sourceHeadSha"), ("COGLATAS_PR_BASE_SHA", "baseSha")):
            support.require(re.fullmatch(r"[a-f0-9]{40}", os.environ.get(variable, "")) is not None)
            result[key] = os.environ[variable]
        private = Path(tempfile.mkdtemp(prefix="cgl-", dir=os.environ["RUNNER_TEMP"]))
        environment = os.environ.copy()
        for key in ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH"):
            environment.pop(key, None)
        result["failureCategory"] = "PREPARATION_FAILED"
        ddl, image, expected = prepare(private, result, environment)
        execute(private, ddl, image, expected, result, environment)
        if candidate_complete(result):
            result["status"] = "SYNTHETIC_LOGICAL_DATABASE_CANDIDATE_PASS"
            result["failureCategory"] = None
            result["phase"] = "COMPLETE"
    except (probe.ProbeFailure, support.Rejected, OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
        pass  # Only closed categories are published, never exception/child text.
    published = False
    try:
        support.publish_once(PUBLIC, result)
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write("published=true\n")
        published = True
    except (OSError, ValueError, KeyError):
        print("PUBLICATION_FAILED", file=sys.stderr)
    print(json.dumps(result, sort_keys=True))
    return 0 if published and result["status"] == "SYNTHETIC_LOGICAL_DATABASE_CANDIDATE_PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
