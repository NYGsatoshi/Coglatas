#!/usr/bin/env python3
"""Review-only, isolated PostgreSQL physical-recovery candidate for issue #1355.

This creates fresh named Docker resources on the local managed daemon. It never
accepts a database endpoint, existing volume, SSH target or production fixture.
The approved logical backup runbook is deliberately not changed by this probe.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import http.cookiejar
import json
import os
from pathlib import Path
import re
import secrets
import shutil
import socket
import subprocess
import sys
import textwrap
import time
import uuid
import urllib.error
import urllib.request


class ProbeFailure(RuntimeError):
    """A safe failure category, without database rows or child-process output."""


def digest(value: bytes) -> str:
    return hashlib.sha256(value).hexdigest()


def run(args: list[str], *, data: bytes | None = None, expected: int = 0,
        expected_sqlstate: str | None = None, cwd: Path | None = None) -> bytes:
    environment = os.environ.copy()
    for key in ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH"):
        environment.pop(key, None)
    result = subprocess.run(args, input=data, capture_output=True, timeout=120, check=False, env=environment, cwd=cwd)
    if result.returncode != expected:
        raise ProbeFailure("CHILD_PROCESS_FAILED")
    if expected_sqlstate and not re.search(rb"\b" + expected_sqlstate.encode() + rb"\b", result.stderr):
        raise ProbeFailure("EXPECTED_SQLSTATE_NOT_OBSERVED")
    return result.stdout


DOCKER = ["docker", "--host=unix:///var/run/docker.sock"]


def docker(*args: str, data: bytes | None = None) -> bytes:
    # Ignore inherited remote-context/TLS selectors, preserving Docker credentials.
    return run(DOCKER + list(args), data=data)


def sql(container: str, statement: str, database: str = "recovery") -> bytes:
    return docker("exec", "-i", container, "psql", "-X", "-qAt", "-U", "postgres",
                  "-d", database, "-v", "ON_ERROR_STOP=1", data=statement.encode())


def quote_identifier(value: str) -> str:
    return '"' + value.replace('"', '""') + '"'


def build_application(root: Path, evidence: Path, expected_ddl: bytes) -> dict[str, object]:
    source_paths = ["src", "global.json", "Directory.Build.props", "Directory.Build.targets",
                    "Directory.Packages.props", "NuGet.config", ".config/dotnet-tools.json"]
    if run(["git", "status", "--porcelain", "--untracked-files=all", "--"] + source_paths, cwd=root):
        raise ProbeFailure("PRODUCT_BUILD_SOURCE_NOT_CLEAN")
    source_sha = run(["git", "rev-parse", "HEAD"], cwd=root).decode().strip()
    source_tree = run(["git", "rev-parse", "HEAD:src"], cwd=root).decode().strip()
    log = evidence / "application-build.log"
    with log.open("wb") as output:
        result = subprocess.run(["dotnet", "build", "src/Coglatas.Web/Coglatas.Web.csproj",
                                 "--configuration", "Release", "--no-restore", "--disable-build-servers", "-m:1"],
                                cwd=root, stdout=output, stderr=output, timeout=180, check=False)
    if result.returncode:
        raise ProbeFailure("APPLICATION_BUILD_FAILED")
    generated = evidence / "generated-original-ef-ddl.sql"
    environment = os.environ.copy()
    environment["ConnectionStrings__DefaultConnection"] = "Host=unused;Database=unused;Username=unused"
    with (evidence / "ddl-generation.log").open("wb") as output:
        result = subprocess.run(["dotnet", "ef", "migrations", "script", "--project", "src/Coglatas.Infrastructure",
                                 "--startup-project", "src/Coglatas.Web", "--configuration", "Release", "--no-build",
                                 "--output", str(generated)], cwd=root, env=environment,
                                stdout=output, stderr=output, timeout=60, check=False)
    if result.returncode or generated.read_bytes() != expected_ddl:
        raise ProbeFailure("CURRENT_BUILD_DDL_IDENTITY_MISMATCH")
    if run(["git", "status", "--porcelain", "--untracked-files=all", "--"] + source_paths, cwd=root):
        raise ProbeFailure("PRODUCT_BUILD_SOURCE_CHANGED")
    return {"sourceSha": source_sha, "sourceTree": source_tree, "buildExitCode": 0,
            "sdkVersion": run(["dotnet", "--version"], cwd=root).decode().strip(),
            "buildLogSha256": digest(log.read_bytes()), "generatedDdlSha256": digest(generated.read_bytes()),
            "assemblies": {name: digest((root / "src/Coglatas.Web/bin/Release/net10.0" / name).read_bytes())
                           for name in ("Coglatas.Web.dll", "Coglatas.Infrastructure.dll",
                                        "Coglatas.Application.dll", "Coglatas.Domain.dll")}}


def assert_fresh_resources(names: list[str], volumes: list[str]) -> None:
    for name in names:
        if docker("ps", "-a", "--filter", "name=^/" + name + "$", "--format", "{{.Names}}").strip():
            raise ProbeFailure("GENERATED_CONTAINER_NAME_COLLISION")
    existing_volumes = set(docker("volume", "ls", "--format", "{{.Name}}").decode().splitlines())
    if any(name in existing_volumes for name in volumes):
        raise ProbeFailure("GENERATED_VOLUME_NAME_COLLISION")


def validate_volume(name: str, identifier: str) -> None:
    metadata = json.loads(docker("volume", "inspect", name, "--format",
                                '{"labels":{{json .Labels}},"driver":{{json .Driver}},"scope":{{json .Scope}}}'))
    if metadata != {"labels": {"coglatas.synthetic-recovery": identifier}, "driver": "local", "scope": "local"}:
        raise ProbeFailure("CREATED_VOLUME_OWNERSHIP_MISMATCH")


def validate_container(name: str, identifier: str, image: str, mounts: dict[str, str], expected_id: str) -> None:
    metadata = json.loads(docker("inspect", name, "--format",
        '{"id":{{json .Id}},"labels":{{json .Config.Labels}},"image":{{json .Image}},"network":{{json .HostConfig.NetworkMode}},"mounts":{{json .Mounts}}}'))
    if metadata["id"] != expected_id or metadata["labels"].get("coglatas.synthetic-recovery") != identifier or metadata["image"] != image or metadata["network"] != "none":
        raise ProbeFailure("CREATED_CONTAINER_OWNERSHIP_MISMATCH")
    observed = {item["Destination"]: item.get("Name") if item["Type"] == "volume" else item["Source"]
                for item in metadata["mounts"]}
    if observed != mounts:
        raise ProbeFailure("CREATED_CONTAINER_MOUNT_MISMATCH")


class SyntheticApplication:
    """The existing Test-only application fixture, never the public pilot gate."""

    def __init__(self, root: Path, evidence: Path):
        self.root, self.evidence = root, evidence
        self.password = secrets.token_urlsafe(32) + "Aa1!"
        self.email = "physical-recovery@example.test"
        self.jar = http.cookiejar.CookieJar()
        self.client = urllib.request.build_opener(urllib.request.ProxyHandler({}),
                                                  urllib.request.HTTPCookieProcessor(self.jar))
        with socket.socket() as reservation:
            reservation.bind(("127.0.0.1", 0))
            self.port = reservation.getsockname()[1]
        self.origin = f"http://127.0.0.1:{self.port}"
        self.process = None
        self.log = None
        self.expected_file = None

    def request(self, path: str, body: object = None, *, csrf: bool = False) -> tuple[int, bytes]:
        headers = {}
        if csrf:
            status, payload = self.request("/api/security/csrf-token")
            if status != 200:
                raise ProbeFailure("CSRF_TOKEN_UNAVAILABLE")
            token = json.loads(payload)
            headers[token["headerName"]] = token["token"]
        data = None if body is None else json.dumps(body).encode()
        if data is not None:
            headers["Content-Type"] = "application/json"
        request = urllib.request.Request(self.origin + path, data=data, headers=headers)
        try:
            with self.client.open(request, timeout=15) as response:
                return response.status, response.read()
        except urllib.error.HTTPError as response:
            return response.code, response.read()

    def start(self, socket_dir: Path, *, seed: bool, state: Path, phase: str) -> None:
        state.mkdir(mode=0o700, exist_ok=True)
        environment = os.environ.copy()
        environment.update({
            "ASPNETCORE_ENVIRONMENT": "Test", "ASPNETCORE_URLS": self.origin,
            "ConnectionStrings__DefaultConnection": f"Host={socket_dir};Database=recovery;Username=postgres",
            "DataProtection__KeysPath": str(state / "keys"),
            "FileStorage__RootPath": str(state / "files"),
            "Tenancy__AppMode": "SaaS", "Tenancy__TenantResolutionStrategy": "ConfigDefault",
            "Tenancy__DefaultTenantSlug": "default", "Tenancy__SeedOnStartup": str(seed),
            "COGLATAS_BROWSER_SMOKE_SEED_ENABLED": str(seed),
            "COGLATAS_BROWSER_SMOKE_EMAIL": self.email,
            "COGLATAS_BROWSER_SMOKE_PASSWORD": self.password,
            "COGLATAS_SEED_ADMIN_ENABLED": "false", "LocalAdmin__SeedOnStartup": "false",
            "UiShell__SeedOnStartup": "false", "DemoDataset__Enabled": "false",
            "COGLATAS_DEMO_DATASET_ENABLED": "false", "BrowserSmokeSeed__Enabled": "false",
            "Logging__LogLevel__Default": "Warning", "Logging__LogLevel__Microsoft": "Warning",
        })
        # Prevent a retained environment from accidentally enabling another seed.
        environment.pop("COGLATAS_BOOTSTRAP_ADMIN_EMAIL", None)
        environment.pop("BootstrapAdmin__Email", None)
        dll = self.root / "src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll"
        self.log = (self.evidence / (phase + "-application.log")).open("wb")
        self.process = subprocess.Popen(["dotnet", str(dll)], env=environment,
                                        cwd=self.root / "src/Coglatas.Web", stdout=self.log, stderr=self.log)
        # A bounded 90-second startup window is stricter than security runtime's
        # existing 90 x 2-second budget and Functional's 240-second setup budget.
        # This polls readiness and never retries an acceptance operation.
        for _ in range(900):
            if self.process.poll() is not None:
                raise ProbeFailure("APPLICATION_START_FAILED")
            try:
                status, _ = self.request("/health/ready")
                if status == 200:
                    mappings = Path(f"/proc/{self.process.pid}/maps").read_text()
                    self.loaded_assemblies = {}
                    for name in ("Coglatas.Web.dll", "Coglatas.Infrastructure.dll", "Coglatas.Application.dll", "Coglatas.Domain.dll"):
                        path = self.root / "src/Coglatas.Web/bin/Release/net10.0" / name
                        if str(path) not in mappings:
                            raise ProbeFailure("EXPECTED_APPLICATION_ASSEMBLY_NOT_MAPPED")
                        self.loaded_assemblies[name] = digest(path.read_bytes())
                    return
            except (OSError, urllib.error.URLError):
                pass
            time.sleep(0.1)
        raise ProbeFailure("APPLICATION_READINESS_FAILED")

    def stop(self) -> None:
        if self.process is not None and self.process.poll() is None:
            self.process.terminate()
            try:
                self.process.wait(timeout=20)
            except subprocess.TimeoutExpired:
                self.process.kill()
                self.process.wait(timeout=10)
        if self.log:
            self.log.close()

    def login(self) -> None:
        status, _ = self.request("/api/auth/login", {"email": self.email, "password": self.password}, csrf=True)
        if status != 200:
            raise ProbeFailure("SYNTHETIC_LOGIN_FAILED")

    def check_file(self, container: str) -> dict[str, object]:
        file_id = sql(container, 'SELECT "Id" FROM file_objects ORDER BY "Id" LIMIT 1;').decode().strip()
        if not re.fullmatch(r"[a-f0-9-]{36}", file_id):
            raise ProbeFailure("SYNTHETIC_FILE_MISSING")
        status, content = self.request(f"/api/files/{file_id}/download")
        if status != 200 or not content:
            raise ProbeFailure("SYNTHETIC_FILE_DOWNLOAD_FAILED")
        if self.expected_file is None:
            self.expected_file = content
        if content != self.expected_file:
            raise ProbeFailure("RESTORED_FILE_BYTE_MISMATCH")
        return {"status": "PASS", "bytes": len(content), "sha256": digest(content)}


def snapshot(container: str) -> dict[str, bytes]:
    schema = docker("exec", container, "pg_dump", "-U", "postgres", "-d", "recovery",
                    "--schema-only", "--restrict-key=SyntheticRecoveryProbe1355")
    tables = json.loads(sql(container, "SELECT coalesce(json_agg(ARRAY[schemaname,tablename] "
                            "ORDER BY schemaname,tablename),'[]') FROM pg_tables "
                            "WHERE schemaname NOT IN ('pg_catalog','information_schema');"))
    row_queries = []
    for namespace, name in tables:
        identity = quote_identifier(namespace) + "." + quote_identifier(name)
        namespace_literal = "'" + namespace.replace("'", "''") + "'"
        name_literal = "'" + name.replace("'", "''") + "'"
        row_queries.append(f"SELECT json_build_array({namespace_literal},{name_literal},count(*), "
                           "encode(sha256(convert_to(coalesce(string_agg(row_to_json(t)::text, E'\\n' "
                           "ORDER BY row_to_json(t)::text COLLATE \"C\"),''),'UTF8')),'hex'))::text AS item "
                           f"FROM {identity} t")
    manifest = sql(container, "SELECT item FROM (" + " UNION ALL ".join(row_queries) +
                   ') row_items ORDER BY item COLLATE "C";')
    # Raw parse trees are compared as bytes: no normalization, second dump or
    # equivalence oracle replaces the original strict catalog requirement.
    catalog = sql(container, """
SELECT item FROM (
SELECT json_build_array('constraint',n.nspname,c.relname,k.conname,k.contype,
 k.convalidated,k.condeferrable,k.condeferred,k.conbin::text,pg_get_constraintdef(k.oid))::text AS item
FROM pg_constraint k JOIN pg_class c ON c.oid=k.conrelid
JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema')
UNION ALL
SELECT json_build_array('index',n.nspname,c.relname,i.indexprs::text,i.indpred::text,
 i.indisvalid,i.indisready,pg_get_indexdef(i.indexrelid))::text
FROM pg_index i JOIN pg_class c ON c.oid=i.indrelid
JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema')
UNION ALL
SELECT json_build_array('trigger',n.nspname,c.relname,t.tgname,t.tgenabled,t.tgisinternal,
 pg_get_triggerdef(t.oid))::text FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid
JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname NOT IN ('pg_catalog','information_schema')
UNION ALL
SELECT json_build_array('table-security',n.nspname,c.relname,c.relrowsecurity,
 c.relforcerowsecurity,c.relacl)::text FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE c.relkind IN ('r','p') AND n.nspname NOT IN ('pg_catalog','information_schema')
) catalog_items ORDER BY item COLLATE "C";
""")
    roles = sql(container, "SELECT json_build_array(rolname,rolsuper,rolinherit,rolcreaterole,"
                "rolcreatedb,rolcanlogin,rolreplication,rolbypassrls,rolconnlimit,rolvaliduntil,rolconfig)::text "
                "FROM pg_roles ORDER BY rolname COLLATE \"C\";")
    memberships = sql(container, "SELECT row_to_json(m)::text FROM pg_auth_members m "
                      "ORDER BY row_to_json(m)::text COLLATE \"C\";")
    database_security = sql(container, "SELECT json_build_array(datname,datdba,encoding,datlocprovider,"
                            "datcollate,datctype,datallowconn,datconnlimit,datacl)::text FROM pg_database "
                            "ORDER BY datname COLLATE \"C\";")
    sequences = []
    sequence_names = json.loads(sql(container, "SELECT coalesce(json_agg(ARRAY[schemaname,sequencename] "
                                      "ORDER BY schemaname,sequencename),'[]') FROM pg_sequences;"))
    for namespace, name in sequence_names:
        identity = quote_identifier(namespace) + "." + quote_identifier(name)
        sequences.append([namespace, name, sql(container, f"SELECT last_value,is_called FROM {identity};").decode()])
    return {"schema.sql": schema, "rows.jsonl": manifest,
            "catalog.txt": catalog, "roles.txt": roles,
            "memberships.txt": memberships, "database-security.txt": database_security,
            "sequences.json": json.dumps(sequences).encode()}


def oracle_negative_controls(container: str, original: dict[str, bytes]) -> dict[str, str]:
    # Only explicitly synthetic probe objects change. Product constraints,
    # triggers, RLS, roles/grants and comparison rules remain untouched.
    sql(container, "UPDATE recovery_control.oracle_probe SET marker='changed'; "
        "ALTER TABLE recovery_control.oracle_probe ADD CONSTRAINT oracle_extra CHECK (marker <> 'forbidden'); "
        "SELECT setval('recovery_control.sequence_probe',38,true);")
    changed = snapshot(container)
    for name in ("schema.sql", "catalog.txt", "rows.jsonl", "sequences.json"):
        if changed[name] == original[name]:
            raise ProbeFailure("STRICT_ORACLE_NEGATIVE_CONTROL_MISSED")
    sql(container, "ALTER TABLE recovery_control.oracle_probe DROP CONSTRAINT oracle_extra; "
        "UPDATE recovery_control.oracle_probe SET marker='checkpoint'; "
        "SELECT setval('recovery_control.sequence_probe',37,false);")
    reset = snapshot(container)
    if reset != original:
        raise ProbeFailure("SYNTHETIC_ORACLE_CONTROL_RESET_MISMATCH")
    return {"schemaAndRawCatalogChange": "DETECTED", "rowChange": "DETECTED",
            "sequenceChange": "DETECTED", "exactOriginalStateRestored": "PASS"}


def trigger_controls(container: str, root: Path) -> dict[str, str]:
    """Exercise exact current-source guard functions in a separate synthetic schema."""
    migration = root / "src/Coglatas.Infrastructure/Persistence/Migrations/20260924133000_AddCoglatasUiCanonicalChangeJournal.cs"
    source = migration.read_text()
    match = re.search(r"CREATE OR REPLACE FUNCTION coglatas_ui_validate_revision_head_insert\(\).*?\$\$;", source, re.S)
    if match is None:
        raise ProbeFailure("SOURCE_TRIGGER_NOT_FOUND")
    function = match.group(0)
    # Transaction is rolled back. The actual product function body and error
    # state are exercised without modifying product data, guards or roles.
    sql(container, "BEGIN; CREATE SCHEMA recovery_guard_control; SET LOCAL search_path=recovery_guard_control; "
        "CREATE TABLE coglatas_ui_canonical_revision_heads (\"Revision\" bigint NOT NULL); " + function + """
CREATE TRIGGER head_insert BEFORE INSERT ON coglatas_ui_canonical_revision_heads
 FOR EACH ROW EXECUTE FUNCTION coglatas_ui_validate_revision_head_insert();
INSERT INTO coglatas_ui_canonical_revision_heads VALUES (0);
DO $$ BEGIN
 BEGIN
  INSERT INTO coglatas_ui_canonical_revision_heads VALUES (1);
  RAISE EXCEPTION 'EXPECTED_REPLAY_DENIAL_MISSING';
 EXCEPTION WHEN check_violation THEN NULL;
 END;
END $$;
ROLLBACK;
""")
    file_migration = root / "src/Coglatas.Infrastructure/Persistence/Migrations/20260901150000_AddFileVersionsActivityProjection.cs"
    file_source = file_migration.read_text()
    file_match = re.search(r'migrationBuilder.Sql\("""\n(.*?)\n\s*"""\);', file_source, re.S)
    if file_match is None:
        raise ProbeFailure("FILE_TRIGGER_SOURCE_NOT_FOUND")
    setup = """\\set VERBOSITY sqlstate
BEGIN;
CREATE SCHEMA recovery_file_control;
SET LOCAL search_path=recovery_file_control;
CREATE TABLE users ("Id" uuid PRIMARY KEY);
CREATE TABLE file_objects (
 "Id" uuid PRIMARY KEY, "TenantId" uuid NOT NULL, "OriginalFileName" varchar(260) NOT NULL,
 "StorageKey" varchar(1024) NOT NULL, "ContentType" varchar(160) NOT NULL, "SizeBytes" bigint NOT NULL,
 "HashSha256" varchar(64), "UploadedByUserId" uuid NOT NULL, "CreatedAt" timestamptz NOT NULL);
""" + textwrap.dedent(file_match.group(1)) + """
INSERT INTO users VALUES ('00000000-0000-0000-0000-000000000001');
INSERT INTO file_objects VALUES ('00000000-0000-0000-0000-000000000002',
 '00000000-0000-0000-0000-000000000003','synthetic.txt','synthetic-key','text/plain',1,NULL,
 '00000000-0000-0000-0000-000000000001','2026-01-01T00:00:00Z');
SELECT count(*) FROM file_versions;
COPY file_versions FROM stdin;
00000000-0000-0000-0000-000000000002\t00000000-0000-0000-0000-000000000003\t00000000-0000-0000-0000-000000000002\t1\tsynthetic.txt\tsynthetic-key\ttext/plain\t1\t\\N\t00000000-0000-0000-0000-000000000001\t2026-01-01T00:00:00Z
\\.
"""
    output = run(DOCKER + ["exec", "-i", container, "psql", "-X", "-qAt", "-U", "postgres",
                          "-d", "recovery", "-v", "ON_ERROR_STOP=1"], data=setup.encode(),
                 expected=3, expected_sqlstate="23505")
    if output != b"1\n":
        raise ProbeFailure("INITIAL_FILE_VERSION_POSITIVE_FAILED")
    # The failed COPY aborts its transaction; disconnect rolls it back. No
    # trigger is disabled and no protected product table is changed.
    return {"revisionHeadNonzeroInsert": "DENIED_23514_WITH_ZERO_POSITIVE",
            "guardSourceSha256": digest(migration.read_bytes()),
            "fileVersionArchiveCopyAfterCapture": "DENIED_23505_WITH_CAPTURE_POSITIVE",
            "fileTriggerSourceSha256": digest(file_migration.read_bytes())}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute-isolated-synthetic", action="store_true", required=True)
    parser.add_argument("--postgres-image-id", required=True)
    parser.add_argument("--original-ef-ddl", type=Path, required=True)
    parser.add_argument("--evidence-dir", type=Path, required=True)
    parser.add_argument("--with-application-controls", action="store_true")
    args = parser.parse_args()
    if not re.fullmatch(r"sha256:[a-f0-9]{64}", args.postgres_image_id):
        parser.error("An existing immutable local image ID is required.")
    root = Path(__file__).resolve().parents[2]
    evidence = args.evidence_dir.resolve()
    if evidence.exists():
        parser.error("The evidence directory must be fresh; failures may not be overwritten.")
    evidence.mkdir(parents=True, mode=0o700)
    os.umask(0o077)
    source_socket, target_socket = evidence / "source-socket", evidence / "target-socket"
    if len(str(source_socket)) > 85 or len(str(target_socket)) > 85:
        parser.error("Use a shorter fresh evidence path for isolated Unix sockets.")
    for directory in (source_socket, target_socket):
        directory.mkdir(mode=0o777)
        directory.chmod(0o777)  # Only this fresh, isolated server socket directory.
    source_sha = run(["git", "-C", str(root), "rev-parse", "HEAD"]).decode().strip()
    ddl = args.original_ef_ddl.read_bytes()
    identifier = "coglatas-synthetic-recovery-" + uuid.uuid4().hex[:12]
    source = identifier + "-source"
    target = identifier + "-target"
    source_volume, backup_volume, target_volume = (identifier + suffix for suffix in ("-source", "-backup", "-target"))
    containers = []
    container_ids, container_mounts = {}, {}
    application = SyntheticApplication(root, evidence) if args.with_application_controls else None
    receipt = {"schema": "coglatas-synthetic-physical-recovery-v1", "sourceSha": source_sha,
               "probeSha256": digest(Path(__file__).read_bytes()),
               "ddlSha256": digest(ddl), "postgresImageId": args.postgres_image_id,
               "resources": {"containers": [source, target], "volumes": [source_volume, backup_volume, target_volume]},
               "status": "FAIL", "formalStrategyApproved": False, "publicGo": False,
               "fullApplicationRecovery": "NOT_EXECUTED", "targetHttps": "NOT_EXECUTED",
               "rollbackRehearsal": "NOT_EXECUTED", "startedAtUtc": time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())}
    try:
        docker("info", "--format", "{{.ServerVersion}}")
        assert_fresh_resources([source, target], [source_volume, backup_volume, target_volume])
        if application:
            receipt["applicationBuild"] = build_application(root, evidence, ddl)
        for name in (source_volume, backup_volume, target_volume):
            docker("volume", "create", "--label", "coglatas.synthetic-recovery=" + identifier, name)
            validate_volume(name, identifier)
        source_id = docker("run", "-d", "--name", source, "--network", "none",
               "--label", "coglatas.synthetic-recovery=" + identifier,
               "-e", "POSTGRES_PASSWORD=" + secrets.token_hex(24), "-e", "POSTGRES_DB=recovery",
               "--mount", f"type=volume,source={source_volume},target=/var/lib/postgresql",
               "--mount", f"type=volume,source={backup_volume},target=/backup",
               "--mount", f"type=volume,source={target_volume},target=/restored-target",
               "--mount", f"type=bind,source={source_socket},target=/var/run/postgresql",
               args.postgres_image_id).decode().strip()
        source_mounts = {
            "/var/lib/postgresql": source_volume, "/backup": backup_volume,
            "/restored-target": target_volume,
            "/var/run/postgresql": str(source_socket)}
        validate_container(source_id, identifier, args.postgres_image_id, source_mounts, source_id)
        containers.append(source)
        container_ids[source], container_mounts[source] = source_id, source_mounts
        for _ in range(100):
            try:
                sql(source, "SELECT 1;")
                break
            except ProbeFailure:
                time.sleep(0.1)
        else:
            raise ProbeFailure("SOURCE_DATABASE_NOT_READY")
        sql(source, ddl.decode())
        receipt["postgresServerVersion"] = sql(source, "SHOW server_version;").decode().strip()
        receipt["triggerControls"] = trigger_controls(source, root)
        if application:
            application.start(source_socket, seed=True, state=evidence / "source-state", phase="source")
            if application.loaded_assemblies != receipt["applicationBuild"]["assemblies"]:
                raise ProbeFailure("SOURCE_LOADED_ASSEMBLY_BUILD_MISMATCH")
            receipt["sourceLoadedAssemblies"] = application.loaded_assemblies
            application.login()
            status, _ = application.request("/api/auth/me")
            if status != 200:
                raise ProbeFailure("SOURCE_SESSION_POSITIVE_FAILED")
            receipt["sourceFile"] = application.check_file(source)
            application.stop()
            shutil.copytree(evidence / "source-state", evidence / "restored-state")
            receipt["webDllSha256"] = digest((root / "src/Coglatas.Web/bin/Release/net10.0/Coglatas.Web.dll").read_bytes())
        # A real sequence with a non-default, uncalled state provides a physical
        # sequence preservation control even if the product only uses UUIDs.
        sql(source, "CREATE SCHEMA recovery_control; CREATE SEQUENCE recovery_control.sequence_probe; "
            "CREATE TABLE recovery_control.oracle_probe (id integer PRIMARY KEY, marker text NOT NULL); "
            "INSERT INTO recovery_control.oracle_probe VALUES (1,'checkpoint'); "
            "SELECT setval('recovery_control.sequence_probe',37,false);")
        before = snapshot(source)
        for name, data in before.items():
            (evidence / ("source-" + name)).write_bytes(data)
        docker("exec", source, "chown", "postgres:postgres", "/backup")
        began = time.monotonic()
        docker("exec", "--user", "postgres", source, "pg_basebackup", "-h", "/var/run/postgresql",
               "-U", "postgres", "-D", "/backup/recovered", "-X", "stream", "--checkpoint=fast")
        receipt["basebackupSeconds"] = round(time.monotonic() - began, 3)
        docker("exec", "--user", "postgres", source, "pg_verifybackup", "/backup/recovered")
        receipt["backupManifestVerification"] = "PASS"
        manifest_before = digest(docker("exec", source, "cat", "/backup/recovered/backup_manifest"))
        receipt["backupManifestSha256Before"] = manifest_before
        # Preserve the archive: server startup and app operations can mutate only
        # a separate, freshly admitted target volume, never the backup volume.
        docker("exec", source, "mkdir", "/restored-target/recovered")
        docker("exec", source, "cp", "-a", "/backup/recovered/.", "/restored-target/recovered/")
        # Source remains online but has no network and no application writers.
        target_id = docker("run", "-d", "--name", target, "--network", "none",
               "--label", "coglatas.synthetic-recovery=" + identifier,
               "-e", "PGDATA=/var/lib/postgresql/recovered",
               "--mount", f"type=volume,source={target_volume},target=/var/lib/postgresql",
               "--mount", f"type=bind,source={target_socket},target=/var/run/postgresql",
               args.postgres_image_id).decode().strip()
        target_mounts = {"/var/lib/postgresql": target_volume, "/var/run/postgresql": str(target_socket)}
        validate_container(target_id, identifier, args.postgres_image_id, target_mounts, target_id)
        containers.append(target)
        container_ids[target], container_mounts[target] = target_id, target_mounts
        for _ in range(100):
            try:
                sql(target, "SELECT 1;")
                break
            except ProbeFailure:
                time.sleep(0.1)
        else:
            raise ProbeFailure("RESTORED_DATABASE_NOT_READY")
        after = snapshot(target)
        comparisons = {}
        for name, data in after.items():
            (evidence / ("restored-" + name)).write_bytes(data)
            comparisons[name] = {"sourceSha256": digest(before[name]), "restoredSha256": digest(data),
                                 "exactBytesEqual": data == before[name]}
        receipt["comparisons"] = comparisons
        if not all(value["exactBytesEqual"] for value in comparisons.values()):
            raise ProbeFailure("STRICT_RESTORED_STATE_MISMATCH")
        receipt["oracleNegativeControls"] = oracle_negative_controls(target, before)
        receipt["restoredTriggerControls"] = trigger_controls(target, root)
        if application:
            original_cookies = [copy.copy(cookie) for cookie in application.jar]
            application.start(target_socket, seed=False, state=evidence / "restored-state", phase="restored")
            if application.loaded_assemblies != receipt["applicationBuild"]["assemblies"]:
                raise ProbeFailure("RESTORED_LOADED_ASSEMBLY_BUILD_MISMATCH")
            receipt["restoredLoadedAssemblies"] = application.loaded_assemblies
            status, _ = application.request("/api/auth/me")
            if status != 200:
                raise ProbeFailure("RETAINED_KEYS_SESSION_POSITIVE_FAILED")
            receipt["restoredFile"] = application.check_file(target)
            status, _ = application.request("/api/auth/change-password", {
                "currentPassword": "synthetic-wrong-password", "newPassword": "SyntheticReplacement1!"})
            if status != 403:
                raise ProbeFailure("RESTORED_CSRF_NEGATIVE_FAILED")
            application.stop()
            missing = evidence / "missing-key-state"
            missing.mkdir()
            shutil.copytree(evidence / "restored-state/files", missing / "files")
            application.start(target_socket, seed=False, state=missing, phase="missing-keys")
            status, _ = application.request("/api/auth/me")
            if status != 401:
                raise ProbeFailure("MISSING_KEYS_SESSION_DENIAL_FAILED")
            application.stop()
            application.jar.clear()
            for cookie in original_cookies:
                application.jar.set_cookie(cookie)
            application.start(target_socket, seed=False, state=evidence / "restored-state", phase="restart")
            status, _ = application.request("/api/auth/me")
            if status != 200:
                raise ProbeFailure("RESTART_RETAINED_KEYS_SESSION_FAILED")
            receipt["restartFile"] = application.check_file(target)
            application.login()
            application.stop()
            receipt["boundedApplicationControls"] = {
                "status": "PASS", "retainedKeysSession": "PASS", "missingKeysSession": "DENIED_401",
                "missingCsrf": "DENIED_403", "restartRetainedSession": "PASS", "freshLogin": "PASS"}
        docker("exec", "--user", "postgres", source, "pg_verifybackup", "/backup/recovered")
        manifest_after = digest(docker("exec", source, "cat", "/backup/recovered/backup_manifest"))
        receipt["backupManifestSha256After"] = manifest_after
        if manifest_after != manifest_before:
            raise ProbeFailure("PRESERVED_BACKUP_MANIFEST_CHANGED")
        receipt["backupManifestVerificationAfterControls"] = "PASS"
        receipt["status"] = "SYNTHETIC_DATABASE_CANDIDATE_PASS"
    except ProbeFailure as error:
        receipt["failureCategory"] = str(error)
    except (OSError, subprocess.SubprocessError, ValueError, TypeError, KeyError):
        receipt["failureCategory"] = "ISOLATED_PROBE_FAILED"
    finally:
        if application:
            application.stop()
        stopped = []
        for name in containers:
            try:
                validate_container(container_ids[name], identifier, args.postgres_image_id,
                                   container_mounts[name], container_ids[name])
                docker("stop", "--time", "10", container_ids[name])
                stopped.append(name)
            except (ProbeFailure, OSError, subprocess.SubprocessError):
                pass
        receipt["stoppedContainers"] = stopped
        receipt["finishedAtUtc"] = time.strftime('%Y-%m-%dT%H:%M:%SZ', time.gmtime())
        (evidence / "receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    print(json.dumps({"status": receipt["status"], "receiptSha256": digest((evidence / "receipt.json").read_bytes()),
                      "formalStrategyApproved": False, "fullApplicationRecovery": "NOT_EXECUTED"}))
    return 0 if receipt["status"] == "SYNTHETIC_DATABASE_CANDIDATE_PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
