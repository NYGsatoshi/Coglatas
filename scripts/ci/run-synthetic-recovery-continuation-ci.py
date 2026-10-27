#!/usr/bin/env python3
"""Additive, held synthetic recovery continuation; never a target/strategy authority."""
from __future__ import annotations
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import subprocess
import sys
import tempfile
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/ci"))
import importlib.util


def module(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    loaded = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(loaded)
    return loaded


logical = module(ROOT / "scripts/ci/run-synthetic-logical-recovery-ci.py", "continuation_preparation")
support, probe = logical.support, logical.probe
PUBLIC = ROOT / "artifacts/synthetic-recovery-continuation-ci/result.json"
TOOL_PROJECT = ROOT / "tools/synthetic-recovery-acceptance/Coglatas.SyntheticRecoveryAcceptance.csproj"
TOOL_PROGRAM = TOOL_PROJECT.parent / "Program.cs"
TOOL_DLL = TOOL_PROJECT.parent / "bin/Release/net10.0/Coglatas.SyntheticRecoveryAcceptance.dll"
AUTH = {key: "PASS" for key in ("authenticatedOwnersPositive", "crossTenantReadDenied",
                               "readOnlyViewPositiveWriteDenied", "filePositiveAndCrossTenantDenial")}
TOOL_CONTROLS = {
    "seed": {"preBackupFixture": "PASS", "pendingEnvelopeSource": "SUPPLIED_CANONICAL_TEST_EVENT"},
    "source-authority": AUTH, "restored-authority": AUTH, "rollback-authority": AUTH,
    "continuation": {**AUTH, **{key: "PASS" for key in (
        "recoveredPendingEnvelopeDelivered", "crossTenantTransportWithPositivePeer",
        "requiredAuditFailureAtomicRollback", "actualHttpMessageAuditOutbox", "clientRequestIdBusinessIdempotence")}},
    "restart": {**AUTH, **{key: "PASS" for key in (
        "deliveredBusinessStateSurvivesRestart", "noAutomaticRedeliveryObservedFor15SecondsWithPositiveControls")}},
}
TOOL_ASSEMBLIES = (*support.ASSEMBLIES, "Coglatas.SyntheticRecoveryAcceptance.dll")


def validate_tool(raw: bytes, mode: str, exit_code: int, expected: dict) -> dict:
    value = support.closed_json(raw)
    support.require(isinstance(value, dict) and set(value) == {
        "schemaVersion", "mode", "status", "phase", "controls", "assemblies",
        "databaseAuthorityScope", "formalRecoveryAcceptance", "transportObservationSeconds", "webSocketDeliveryGuarantee"})
    support.require(type(exit_code) is int and exit_code == 0
                    and type(value["schemaVersion"]) is int and value["schemaVersion"] == 1
                    and value["mode"] == mode and mode in TOOL_CONTROLS
                    and value["status"] == "PASS" and value["phase"] == "COMPLETE"
                    and value["databaseAuthorityScope"] == "SYNTHETIC_POSTGRES_BOOTSTRAP_ROLE_ONLY"
                    and value["formalRecoveryAcceptance"] == "NOT_GRANTED"
                    and type(value["transportObservationSeconds"]) is int
                    and value["transportObservationSeconds"] == (15 if mode in ("continuation", "restart") else 0)
                    and value["webSocketDeliveryGuarantee"] == "BOUNDED_CONTROL_OBSERVATION_ONLY"
                    and value["controls"] == TOOL_CONTROLS[mode]
                    and value["assemblies"] == expected
                    and set(expected) == set(TOOL_ASSEMBLIES)
                    and all(support.digest_value(item) for item in expected.values()))
    return dict(value["controls"])


class SessionApplication(probe.SyntheticApplication):
    """Existing SEC-02 Session-mode configuration, using actual product services."""

    def start(self, socket_dir, *, seed, state, phase):
        support.require(seed is False)
        state.mkdir(mode=0o700, exist_ok=True)
        environment = {key: os.environ[key] for key in
                       ("PATH", "DOTNET_ROOT", "HOME", "TMPDIR", "LANG", "LD_LIBRARY_PATH") if key in os.environ}
        environment.update({
            "ASPNETCORE_ENVIRONMENT": "Test", "DOTNET_ENVIRONMENT": "Test", "ASPNETCORE_URLS": self.origin,
            "ConnectionStrings__DefaultConnection": f"Host={socket_dir};Database=recovery;Username=postgres",
            "DataProtection__KeysPath": str(state / "keys"), "FileStorage__RootPath": str(state / "files"),
            "Tenancy__AppMode": "SaaS", "Tenancy__TenantResolutionStrategy": "Session",
            "Tenancy__SeedOnStartup": "false", "SecurityCiFixture__Enabled": "false",
            "COGLATAS_BROWSER_SMOKE_SEED_ENABLED": "false", "COGLATAS_SEED_ADMIN_ENABLED": "false",
            "LocalAdmin__SeedOnStartup": "false", "UiShell__SeedOnStartup": "false",
            "DemoDataset__Enabled": "false", "COGLATAS_DEMO_DATASET_ENABLED": "false",
            "BrowserSmokeSeed__Enabled": "false", "Security__RequireHttps": "false",
            "Security__CookieSecurePolicy": "SameAsRequest", "Security__EnableHsts": "false",
            "Security__EnableRateLimiting": "false", "Security__EnableCsrfProtection": "true",
            "Security__EvaluationMode": "Disabled", "Realtime__DispatcherPollSeconds": "1",
            "FileStorage__AllowedExtensions__0": ".txt", "FileStorage__AllowedContentTypes__0": "text/plain",
            "Logging__LogLevel__Default": "Warning", "Logging__LogLevel__Microsoft": "Warning"})
        dll = self.root / "src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll"
        self.log = (self.evidence / (phase + "-application.log")).open("wb")
        self.process = subprocess.Popen(["dotnet", str(dll)], env=environment, cwd=self.root / "src/Coglatas.Web",
                                        stdout=self.log, stderr=self.log)
        for _ in range(900):  # Existing probe's 90-second startup budget, not acceptance retries.
            if self.process.poll() is not None:
                raise support.Rejected("APPLICATION_START_FAILED")
            try:
                if self.request("/health/ready")[0] == 200:
                    mappings = Path(f"/proc/{self.process.pid}/maps").read_text()
                    self.loaded_assemblies = {}
                    for name in support.ASSEMBLIES:
                        path = dll.parent / name
                        support.require(str(path) in mappings)
                        self.loaded_assemblies[name] = support.sha(path.read_bytes())
                    return
            except (OSError, probe.urllib.error.URLError):
                pass
            time.sleep(0.1)
        raise support.Rejected("APPLICATION_READINESS_FAILED")


def ready(container):
    for _ in range(100):
        try:
            support.require(probe.sql(container, "SELECT 1;") == b"1\n")
            return
        except probe.ProbeFailure:
            time.sleep(0.1)
    raise support.Rejected("DATABASE_NOT_READY")


def execute(private, ddl, image, expected, result, environment, tool_hashes):
    evidence = private / "e"
    evidence.mkdir(mode=0o700)
    source_socket, target_socket = evidence / "source-socket", evidence / "target-socket"
    for path in (source_socket, target_socket):
        support.require(len(str(path)) <= 85)
        path.mkdir(mode=0o777)
        path.chmod(0o777)
    identifier = "coglatas-synthetic-recovery-" + uuid.uuid4().hex[:12]
    source, target = identifier + "-source", identifier + "-target"
    volumes = [identifier + suffix for suffix in ("-source", "-backup", "-target", "-corrupt-clone")]
    admitted = []
    application = SessionApplication(ROOT, evidence)
    pending = str(uuid.uuid4())
    tool_receipts = {}
    def tool(mode, socket_path, state):
        result["phase"] = "TOOL_" + mode.upper().replace("-", "_")
        config = private / ("config-" + mode + ".json")
        receipt = private / ("tool-" + mode + ".json")
        support.require(mode not in result["toolExitCodes"] and not config.exists() and not receipt.exists())
        config.write_text(json.dumps({
            "mode": mode, "connectionString": f"Host={socket_path};Database=recovery;Username=postgres",
            "origin": application.origin + "/", "storageRoot": str(state / "files"),
            "password": application.password, "pendingEventId": pending, "receiptPath": str(receipt)}))
        process = subprocess.run(["dotnet", str(TOOL_DLL), str(config)], cwd=ROOT, env=environment,
                                 capture_output=True, timeout=180, check=False)
        (private / (mode + ".stdout")).write_bytes(process.stdout)
        (private / (mode + ".stderr")).write_bytes(process.stderr)
        result["toolExitCodes"][mode] = process.returncode
        raw = support.read_private(receipt)
        result["toolReceiptSha256"][mode] = support.sha(raw)
        tool_receipts[mode] = validate_tool(raw, mode, process.returncode, tool_hashes)
        result["toolControls"][mode] = tool_receipts[mode]
    try:
        result["phase"] = "SOURCE_ADMISSION"
        probe.assert_fresh_resources([source, target], volumes)
        build = probe.build_application(ROOT, evidence, ddl.read_bytes())
        support.require(all(build[key] == expected[key] for key in ("sourceSha", "sourceTree", "sdkVersion", "assemblies"))
                        and build["generatedDdlSha256"] == expected["ddlSha256"] and build["buildExitCode"] == 0)
        for name in volumes:
            probe.docker("volume", "create", "--label", "coglatas.synthetic-recovery=" + identifier, name)
            probe.validate_volume(name, identifier)
        source_mounts = {"/var/lib/postgresql": volumes[0], "/backup": volumes[1],
                         "/restored-target": volumes[2], "/failure-clone": volumes[3],
                         "/var/run/postgresql": str(source_socket)}
        source_id = probe.docker("run", "-d", "--name", source, "--network", "none",
            "--label", "coglatas.synthetic-recovery=" + identifier,
            "-e", "POSTGRES_PASSWORD=" + secrets.token_hex(24), "-e", "POSTGRES_DB=recovery",
            "--mount", f"type=volume,source={volumes[0]},target=/var/lib/postgresql",
            "--mount", f"type=volume,source={volumes[1]},target=/backup",
            "--mount", f"type=volume,source={volumes[2]},target=/restored-target",
            "--mount", f"type=volume,source={volumes[3]},target=/failure-clone",
            "--mount", f"type=bind,source={source_socket},target=/var/run/postgresql", image).decode().strip()
        probe.validate_container(source_id, identifier, image, source_mounts, source_id)
        admitted.append((source_id, source_mounts))
        ready(source_id)
        probe.sql(source_id, ddl.read_text())
        version = probe.sql(source_id, "SHOW server_version;").decode().strip()
        support.require(re.fullmatch(r"18\.6(?: \([A-Za-z0-9 ._+:/-]{1,160}\))?", version) is not None)
        result["bindings"]["postgresServerVersion"] = version
        source_guards = probe.trigger_controls(source_id, ROOT)
        source_state, restored_state = evidence / "source-state", evidence / "restored-state"
        source_state.mkdir(mode=0o700)
        tool("seed", source_socket, source_state)
        application.start(source_socket, seed=False, state=source_state, phase="source")
        support.require(application.loaded_assemblies == expected["assemblies"])
        tool("source-authority", source_socket, source_state)
        application.stop()
        shutil.copytree(source_state, restored_state)
        probe.sql(source_id, "CREATE SCHEMA recovery_control; CREATE SEQUENCE recovery_control.sequence_probe; "
                  "CREATE TABLE recovery_control.oracle_probe (id integer PRIMARY KEY, marker text NOT NULL); "
                  "INSERT INTO recovery_control.oracle_probe VALUES (1,'checkpoint'); "
                  "SELECT setval('recovery_control.sequence_probe',37,false);")
        before = probe.snapshot(source_id)
        support.require(probe.oracle_negative_controls(source_id, before) == support.ORACLE)
        result["phase"] = "HEALTHY_BACKUP_ONCE"
        probe.docker("exec", source_id, "chown", "postgres:postgres", "/backup")
        probe.docker("exec", "--user", "postgres", source_id, "pg_basebackup", "-h", "/var/run/postgresql",
                     "-U", "postgres", "-D", "/backup/recovered", "-X", "stream", "--checkpoint=fast")
        probe.docker("exec", "--user", "postgres", source_id, "pg_verifybackup", "/backup/recovered")
        manifest = support.sha(probe.docker("exec", source_id, "cat", "/backup/recovered/backup_manifest"))
        result["backupManifestSha256"] = manifest
        probe.docker("exec", source_id, "mkdir", "/restored-target/recovered")
        probe.docker("exec", source_id, "cp", "-a", "/backup/recovered/.", "/restored-target/recovered/")
        target_mounts = {"/var/lib/postgresql": volumes[2], "/var/run/postgresql": str(target_socket)}
        target_id = probe.docker("run", "-d", "--name", target, "--network", "none",
            "--label", "coglatas.synthetic-recovery=" + identifier, "-e", "PGDATA=/var/lib/postgresql/recovered",
            "--mount", f"type=volume,source={volumes[2]},target=/var/lib/postgresql",
            "--mount", f"type=bind,source={target_socket},target=/var/run/postgresql", image).decode().strip()
        probe.validate_container(target_id, identifier, image, target_mounts, target_id)
        admitted.append((target_id, target_mounts))
        ready(target_id)
        support.require(probe.sql(target_id, "SHOW server_version;").decode().strip() == version)
        result["phase"] = "STRICT_RESTORED_BASELINE"
        result["comparisons"] = logical.strict_comparisons(before, probe.snapshot(target_id))
        support.require(logical.comparisons_pass(result["comparisons"]))
        support.require(probe.oracle_negative_controls(target_id, before) == support.ORACLE
                        and probe.trigger_controls(target_id, ROOT) == source_guards)
        result["baseControls"] = {key: "PASS" for key in ("strictSevenBeforeInjections", "oracleNegatives",
                                 "sourceGuards", "healthyBackupVerified")}
        application.start(target_socket, seed=False, state=restored_state, phase="restored")
        support.require(application.loaded_assemblies == expected["assemblies"])
        result["baseControls"]["sourceAndRestoredAssemblyBinding"] = "PASS"
        tool("restored-authority", target_socket, restored_state)
        tool("continuation", target_socket, restored_state)
        application.stop()
        application.start(target_socket, seed=False, state=restored_state, phase="restart")
        support.require(application.loaded_assemblies == expected["assemblies"])
        tool("restart", target_socket, restored_state)

        result["phase"] = "DISTINCT_CORRUPT_CLONE_NEGATIVE"
        probe.docker("exec", source_id, "mkdir", "/failure-clone/recovered")
        probe.docker("exec", source_id, "cp", "-a", "/backup/recovered/.", "/failure-clone/recovered/")
        probe.docker("exec", "--user", "postgres", source_id, "pg_verifybackup", "/failure-clone/recovered")
        result["baseControls"]["freshCloneVerifiedBeforeCorruption"] = "PASS"
        support.require(probe.docker("exec", source_id, "cat", "/failure-clone/recovered/PG_VERSION") == b"18\n")
        probe.docker("exec", source_id, "sh", "-c",
                     "printf X | dd of=/failure-clone/recovered/PG_VERSION bs=1 seek=0 count=1 conv=notrunc")
        support.require(probe.docker("exec", source_id, "cat", "/failure-clone/recovered/PG_VERSION") == b"X8\n")
        probe.run(probe.DOCKER + ["exec", "--user", "postgres", source_id, "pg_verifybackup", "/failure-clone/recovered"], expected=1)
        result["baseControls"]["deliberatelyCorruptFreshCloneRejected"] = "PASS"
        support.require(application.request("/health/ready")[0] == 200 and application.process.poll() is None)
        application.process.kill()
        support.require(application.process.wait(timeout=10) == -9)
        application.stop()
        result["baseControls"]["ownedApplicationCrashInjected"] = "PASS"
        result["phase"] = "LOCAL_CHECKPOINT_FALLBACK"
        result["rollbackComparisons"] = logical.strict_comparisons(before, probe.snapshot(source_id))
        support.require(logical.comparisons_pass(result["rollbackComparisons"]))
        application.start(source_socket, seed=False, state=source_state, phase="rollback")
        support.require(application.loaded_assemblies == expected["assemblies"])
        tool("rollback-authority", source_socket, source_state)
        application.stop()
        probe.docker("exec", "--user", "postgres", source_id, "pg_verifybackup", "/backup/recovered")
        support.require(support.sha(probe.docker("exec", source_id, "cat", "/backup/recovered/backup_manifest")) == manifest)
        result["baseControls"]["preservedBackupUnchanged"] = "PASS"
        result["baseControls"]["boundedLocalSourceCheckpointFallback"] = "PASS"
    finally:
        app_stopped = True
        try:
            application.stop()
        except (OSError, subprocess.SubprocessError):
            app_stopped = False
        stopped = 0
        for container, mounts in admitted:
            try:
                probe.validate_container(container, identifier, image, mounts, container)
                probe.docker("stop", "--time", "10", container)
                stopped += 1
            except (probe.ProbeFailure, OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
                pass
        result["admittedContainers"], result["stoppedContainers"] = len(admitted), stopped
        result["cleanupState"] = "VERIFIED" if (len(admitted) == 2 and len({item[0] for item in admitted}) == 2
                                                and stopped == 2 and app_stopped) else "INCOMPLETE"


BASE_CONTROLS = {key: "PASS" for key in (
    "strictSevenBeforeInjections", "oracleNegatives", "sourceGuards", "healthyBackupVerified",
    "sourceAndRestoredAssemblyBinding", "freshCloneVerifiedBeforeCorruption", "deliberatelyCorruptFreshCloneRejected", "ownedApplicationCrashInjected",
    "preservedBackupUnchanged", "boundedLocalSourceCheckpointFallback")}


def complete(result):
    return (result.get("baseControls") == BASE_CONTROLS
            and set(result.get("toolControls", {})) == set(TOOL_CONTROLS)
            and all(result["toolControls"][mode] == expected for mode, expected in TOOL_CONTROLS.items())
            and set(result.get("toolReceiptSha256", {})) == set(TOOL_CONTROLS)
            and all(support.digest_value(value) for value in result["toolReceiptSha256"].values())
            and set(result.get("toolExitCodes", {})) == set(TOOL_CONTROLS)
            and all(type(value) is int and value == 0 for value in result["toolExitCodes"].values())
            and logical.comparisons_pass(result.get("comparisons"))
            and logical.comparisons_pass(result.get("rollbackComparisons"))
            and support.digest_value(result.get("backupManifestSha256"))
            and type(result.get("admittedContainers")) is int and result["admittedContainers"] == 2
            and type(result.get("stoppedContainers")) is int and result["stoppedContainers"] == 2
            and result.get("cleanupState") == "VERIFIED")


def main():
    os.umask(0o077)
    result = {"schemaVersion": 1, "kind": "ADVISORY_SYNTHETIC_RECOVERY_CONTINUATION",
              "status": "FAIL", "phase": "ADMISSION", "failureCategory": "CALLER_REJECTED",
              "sourceSha": None, "sourceHeadSha": None, "baseSha": None, "runId": None, "runAttempt": None,
              "bindings": {}, "baseControls": {}, "toolControls": {}, "toolReceiptSha256": {}, "toolExitCodes": {},
              "comparisons": {}, "rollbackComparisons": {}, "backupManifestSha256": None,
              "admittedContainers": 0, "stoppedContainers": 0, "cleanupState": "INCOMPLETE",
              "databaseAuthorityScope": "SYNTHETIC_POSTGRES_BOOTSTRAP_ROLE_ONLY",
              "formalStrategyApproved": False, "formalRecoveryAcceptance": "NOT_GRANTED", "publicGo": False,
              "targetHttps": "NOT_EXECUTED", "productionRollbackRehearsal": "NOT_EXECUTED",
              "globalExactlyOnceDelivery": "NOT_CLAIMED", "privateEvidenceAfterHostedTeardown": "NOT_RETAINED"}
    try:
        support.require(os.environ.get("COGLATAS_RECOVERY_RUNNER_ENVIRONMENT") == "github-hosted"
                        and os.environ.get("GITHUB_EVENT_NAME") == "pull_request"
                        and os.environ.get("RUNNER_OS") == "Linux"
                        and not PUBLIC.exists() and not PUBLIC.is_symlink())
        for variable, key in (("GITHUB_RUN_ID", "runId"), ("GITHUB_RUN_ATTEMPT", "runAttempt")):
            support.require(re.fullmatch(r"[1-9][0-9]*", os.environ.get(variable, "")) is not None)
            result[key] = int(os.environ[variable])
        for variable, key in (("COGLATAS_PR_HEAD_SHA", "sourceHeadSha"), ("COGLATAS_PR_BASE_SHA", "baseSha")):
            support.require(re.fullmatch(r"[a-f0-9]{40}", os.environ.get(variable, "")) is not None)
            result[key] = os.environ[variable]
        private = Path(tempfile.mkdtemp(prefix="cga-", dir=os.environ["RUNNER_TEMP"]))
        environment = os.environ.copy()
        for key in ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH"):
            environment.pop(key, None)
        result["failureCategory"] = "PREPARATION_FAILED"
        ddl, image, expected = logical.prepare(private, result, environment)
        def command(args, phase):
            result["phase"] = phase
            process = subprocess.run(args, cwd=ROOT, env=environment, capture_output=True, check=False, timeout=600)
            (private / (phase + ".stdout")).write_bytes(process.stdout)
            (private / (phase + ".stderr")).write_bytes(process.stderr)
            support.require(process.returncode == 0)
            return process.stdout
        support.require(command(["git", "status", "--porcelain", "--untracked-files=all", "--",
            "tools/synthetic-recovery-acceptance", "scripts/ci/run-synthetic-recovery-continuation-ci.py"],
            "HELPER_SOURCE_CLEAN") == b"")
        command(["dotnet", "restore", str(TOOL_PROJECT), "--configfile", str(private / "NuGet.Config"),
                 "--disable-parallel", "--verbosity", "minimal"], "TOOL_RESTORE")
        command(["dotnet", "build", str(TOOL_PROJECT), "--configuration", "Release", "--no-restore",
                 "--disable-build-servers", "-m:1"], "TOOL_BUILD")
        binaries = ROOT / "src/Coglatas.Web/bin/Release/net10.0"
        expected["assemblies"] = {name: support.sha((binaries / name).read_bytes()) for name in support.ASSEMBLIES}
        tool_hashes = {name: support.sha((TOOL_DLL.parent / name).read_bytes()) for name in TOOL_ASSEMBLIES}
        support.require(all(tool_hashes[name] == expected["assemblies"][name] for name in support.ASSEMBLIES))
        result["bindings"] = {key: expected[key] for key in
            ("sourceSha", "sourceTree", "sdkVersion", "probeSha256", "ddlSha256", "postgresImageId", "postgresRepoDigests", "assemblies")}
        result["bindings"].update({
            "continuationCallerSha256": support.sha(Path(__file__).read_bytes()),
            "logicalPreparationSha256": expected["callerSha256"],
            "publicationSupportSha256": expected["publicationSupportSha256"],
            "toolSourceSha256": support.sha(TOOL_PROGRAM.read_bytes()),
            "toolProjectSha256": support.sha(TOOL_PROJECT.read_bytes()),
            "syntheticSeedSourceSha256": support.sha((ROOT / "src/Coglatas.Infrastructure/Persistence/SecurityCiFixtureSeed.cs").read_bytes()),
            "toolAssemblies": tool_hashes,
            "sourceRootTree": command(["git", "rev-parse", "HEAD^{tree}"], "ROOT_TREE").decode().strip(),
            "toolSourceTree": command(["git", "rev-parse", "HEAD:tools/synthetic-recovery-acceptance"], "TOOL_TREE").decode().strip()})
        result["failureCategory"] = "ISOLATED_CONTINUATION_FAILED"
        execute(private, ddl, image, expected, result, environment, tool_hashes)
        if complete(result):
            result["status"] = "SYNTHETIC_RECOVERY_CONTINUATION_PASS"
            result["phase"] = "COMPLETE"
            result["failureCategory"] = None
        else:
            result["failureCategory"] = "INCOMPLETE_REQUIRED_CONTROLS"
    except (probe.ProbeFailure, support.Rejected, OSError, ValueError, TypeError, KeyError, subprocess.SubprocessError):
        pass  # No exception values, commands, private paths, bodies, cookies, keys or raw SQL are serialized.
    published = False
    try:
        support.publish_once(PUBLIC, result)
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write("published=true\n")
        published = True
    except (OSError, ValueError, KeyError):
        print("PUBLICATION_FAILED", file=sys.stderr)
    print(json.dumps(result, sort_keys=True))
    return 0 if published and result["status"] == "SYNTHETIC_RECOVERY_CONTINUATION_PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
