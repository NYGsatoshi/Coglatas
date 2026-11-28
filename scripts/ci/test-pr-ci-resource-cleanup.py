#!/usr/bin/env python3
"""Execute the cleanup helper against an isolated stateful Docker test double.

This proves target selection, native process deadlines and failure propagation.
It is not proof of Docker teardown or GitHub runner cancellation.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
import subprocess
import tempfile
import time
import unittest

ROOT = Path(__file__).resolve().parents[2]
HELPER = ROOT / "scripts/ci/cleanup-pr-ci-resources.sh"
RUN = "12345"
ATTEMPT = "2"

DOCKER = r'''#!/usr/bin/env python3
import json, os, sys, time
from pathlib import Path
p = Path(os.environ["FAKE_DOCKER_STATE"])
state = json.loads(p.read_text())
args = sys.argv[1:]
state["calls"].append(args)
p.write_text(json.dumps(state))
kind, action = args[:2]
if state.get("hang") == kind:
    Path(os.environ["FAKE_DOCKER_PID"]).write_text(str(os.getpid()))
    time.sleep(60)
if state.get("fail_project") and any(state["fail_project"] in arg for arg in args):
    print("sensitive-docker-error-must-not-be-published", file=sys.stderr)
    sys.exit(17)
resources = state[kind]
if action in ("inspect", "rm") and state.get("vanish_on") == [kind, action]:
    identifier = args[-1]
    resources.pop(identifier, None)
    if kind == "container":
        for network in state["network"].values():
            network.get("Containers", {}).pop(identifier, None)
    p.write_text(json.dumps(state))
    sys.exit(1)
if action in ("inspect", "rm") and state.get("reject_on") == [kind, action]:
    print("sensitive-docker-error-must-not-be-published", file=sys.stderr)
    sys.exit(17)
if action == "ls":
    if "--filter" not in args:
        if state.get("inventory_error"):
            sys.exit(19)
        if state.get("inventory_malformed"):
            print("not a valid resource identifier")
        else:
            for identifier in resources:
                print(identifier)
        sys.exit(0)
    label = args[args.index("--filter") + 1][len("label="):]
    key, expected = label.split("=", 1)
    for identifier, record in resources.items():
        labels = record.get("Config", {}).get("Labels", {}) if kind == "container" else record.get("Labels", {})
        if labels.get(key) == expected or state.get("corrupt_listing") == identifier:
            print(identifier)
elif action == "inspect":
    record = resources.get(args[2])
    if record is None:
        sys.exit(1)
    print(json.dumps([record]))
elif action == "rm":
    identifier = args[-1]
    if state.get("retain") != identifier:
        resources.pop(identifier)
    if kind == "container":
        for network in state["network"].values():
            network.get("Containers", {}).pop(identifier, None)
    p.write_text(json.dumps(state))
else:
    sys.exit(9)
'''


class CleanupTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.bin = self.root / "bin"
        self.bin.mkdir()
        executable = self.bin / "docker"
        executable.write_text(DOCKER, encoding="utf-8")
        executable.chmod(0o755)
        self.runtime = self.root / "runtime"
        self.runtime.mkdir()
        self.file = self.root / "docker.json"
        self.pid = self.root / "docker.pid"
        self.state = {"calls": [], "container": {}, "network": {}, "volume": {}}
        self.env = dict(os.environ)
        for name in ("COMPOSE_PROJECT_NAME", "COGLATAS_SECURITY_CI_PROJECT", "COGLATAS_SECURITY_RUNTIME_STATE_DIR"):
            self.env.pop(name, None)
        self.env.update({
            "PATH": str(self.bin) + os.pathsep + self.env["PATH"],
            "GITHUB_ACTIONS": "true", "GITHUB_RUN_ID": RUN,
            "GITHUB_RUN_ATTEMPT": ATTEMPT, "GITHUB_JOB": "frontend-test",
            "RUNNER_TEMP": str(self.runtime), "FAKE_DOCKER_STATE": str(self.file),
            "FAKE_DOCKER_PID": str(self.pid),
        })

    def invoke(self, mode="frontend-test", **environment):
        self.file.write_text(json.dumps(self.state), encoding="utf-8")
        result = subprocess.run(
            ["bash", str(HELPER), mode], cwd=ROOT,
            env={**self.env, **environment}, capture_output=True, text=True,
            timeout=25, check=False,
        )
        self.state = json.loads(self.file.read_text(encoding="utf-8"))
        return result

    def add_project(self, project: str, offset: int = 1, scanner=False):
        container = f"{offset:064x}"
        network = f"{offset + 1:064x}"
        volume = project + "_temporary-data"
        owner = {"com.docker.compose.project": project}
        self.state["container"][container] = {"Id": container, "Config": {"Labels": owner}}
        self.state["network"][network] = {
            "Id": network, "Name": project + "_default", "Labels": owner,
            "Containers": {container: {}},
        }
        self.state["volume"][volume] = {"Name": volume, "Labels": owner}
        if scanner:
            endpoint = f"{offset + 2:064x}"
            self.state["container"][endpoint] = {
                "Id": endpoint, "Config": {"Labels": {}},
                "Mounts": [{"Type": "bind", "Source": str(self.runtime / project / "coglatas-security-scanner.fixture")}],
            }
            self.state["network"][network]["Containers"][endpoint] = {}
        return container, network, volume

    def test_frontend_removes_only_current_project_and_preserves_cache_and_other_attempt(self):
        current = f"coglatas-playwright-{RUN}-{ATTEMPT}-frontend-test"
        targets = self.add_project(current)
        unrelated = self.add_project(f"coglatas-playwright-{RUN}-1-frontend-test", 20)
        self.state["volume"]["shared-cache"] = {"Name": "shared-cache", "Labels": {}}
        result = self.invoke()
        self.assertEqual(0, result.returncode, result.stderr)
        for kind, identifier in zip(("container", "network", "volume"), targets):
            self.assertNotIn(identifier, self.state[kind])
        for kind, identifier in zip(("container", "network", "volume"), unrelated):
            self.assertIn(identifier, self.state[kind])
        self.assertIn("shared-cache", self.state["volume"])

    def test_identity_rejection_precedes_every_docker_call(self):
        for environment in (
            {"GITHUB_ACTIONS": "false"}, {"GITHUB_JOB": "other-job"},
            {"GITHUB_RUN_ID": "../123"}, {"GITHUB_RUN_ID": "0"},
            {"GITHUB_RUN_ATTEMPT": "-1"}, {"GITHUB_RUN_ATTEMPT": "2\n"},
            {"COMPOSE_PROJECT_NAME": "production"},
        ):
            with self.subTest(environment=environment):
                result = self.invoke(**environment)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual([], self.state["calls"])
        self.assertNotEqual(0, self.invoke("other-job").returncode)
        self.assertEqual([], self.state["calls"])

    def test_resource_metadata_must_confirm_filtered_ownership(self):
        container, _, _ = self.add_project("unrelated-project")
        self.state["corrupt_listing"] = container
        result = self.invoke()
        self.assertNotEqual(0, result.returncode)
        self.assertIn(container, self.state["container"])
        self.assertFalse(any(call[1] == "rm" for call in self.state["calls"]))

    def test_security_removes_three_lanes_and_nested_credentials_without_sourcing_state(self):
        unrelated = self.runtime / "unrelated"
        unrelated.mkdir()
        sentinel = unrelated / "keep.txt"
        sentinel.write_text("keep", encoding="utf-8")
        for index, lane in enumerate(("core", "schemathesis", "zap")):
            project = f"coglatas-security-{RUN}-{ATTEMPT}-{lane}"
            directory = self.runtime / project
            directory.mkdir()
            (directory / "runtime-state.env").write_text(f"rm -f {sentinel}\n", encoding="utf-8")
            child = directory / "coglatas-security-scanner.fixture"
            child.mkdir()
            (child / "cookie.txt").write_text("synthetic-secret", encoding="utf-8")
            (child / "outside-link").symlink_to(unrelated, target_is_directory=True)
            self.add_project(project, index * 10 + 1, scanner=True)
        result = self.invoke("security-scan", GITHUB_JOB="security-scan")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(sentinel.exists())
        self.assertEqual([unrelated], list(self.runtime.iterdir()))
        self.assertTrue(all(not self.state[kind] for kind in ("container", "network", "volume")))
        self.assertNotIn("synthetic-secret", result.stdout + result.stderr)

    def test_security_rejects_state_symlink_before_mutating_any_lane(self):
        outside = self.root / "outside"
        outside.mkdir()
        (self.runtime / f"coglatas-security-{RUN}-{ATTEMPT}-zap").symlink_to(outside, target_is_directory=True)
        result = self.invoke("security-scan", GITHUB_JOB="security-scan")
        self.assertNotEqual(0, result.returncode)
        self.assertEqual([], self.state["calls"])
        self.assertTrue(outside.exists())

    def test_security_scanner_network_requires_exact_name_and_both_ownership_labels(self):
        project = f"coglatas-security-{RUN}-{ATTEMPT}-zap"
        endpoint = "e" * 64
        network = "d" * 64
        self.state["container"][endpoint] = {
            "Id": endpoint, "Config": {"Labels": {}},
            "Mounts": [{"Type": "bind", "Source": str(self.runtime / project)}],
        }
        self.state["network"][network] = {
            "Id": network, "Name": project + "_sec06_zap",
            "Labels": {"coglatas.security.project": project, "coglatas.security.control": "SEC-06"},
            "Containers": {endpoint: {}},
        }
        original = json.loads(json.dumps(self.state))
        for field in ("name", "control"):
            with self.subTest(field=field):
                self.state = json.loads(json.dumps(original))
                if field == "name":
                    self.state["network"][network]["Name"] = "unrelated-network"
                else:
                    self.state["network"][network]["Labels"]["coglatas.security.control"] = "unknown"
                result = self.invoke("security-scan", GITHUB_JOB="security-scan")
                self.assertNotEqual(0, result.returncode)
                self.assertIn(network, self.state["network"])
                self.assertIn(endpoint, self.state["container"])
        self.state = original
        result = self.invoke("security-scan", GITHUB_JOB="security-scan")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn(network, self.state["network"])
        self.assertNotIn(endpoint, self.state["container"])

    def test_unrelated_network_endpoint_is_not_removed(self):
        project = f"coglatas-security-{RUN}-{ATTEMPT}-core"
        container, network, _ = self.add_project(project)
        endpoint = "f" * 64
        self.state["container"][endpoint] = {"Id": endpoint, "Config": {"Labels": {}}, "Mounts": []}
        self.state["network"][network]["Containers"][endpoint] = {}
        result = self.invoke("security-scan", GITHUB_JOB="security-scan")
        self.assertNotEqual(0, result.returncode)
        self.assertIn(endpoint, self.state["container"])
        self.assertIn(container, self.state["container"])

    def test_one_lane_error_does_not_skip_other_lanes_or_credential_removal(self):
        failed = f"coglatas-security-{RUN}-{ATTEMPT}-core"
        self.state["fail_project"] = failed
        for index, lane in enumerate(("core", "schemathesis", "zap")):
            project = f"coglatas-security-{RUN}-{ATTEMPT}-{lane}"
            directory = self.runtime / project
            directory.mkdir()
            (directory / "cookie.txt").write_text("synthetic-secret", encoding="utf-8")
            self.add_project(project, index * 10 + 1)
        result = self.invoke("security-scan", GITHUB_JOB="security-scan")
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(list(self.runtime.iterdir()))
        self.assertEqual(1, len(self.state["container"]))
        self.assertNotIn("sensitive-docker-error", result.stdout + result.stderr)
        self.assertIn("result=failure", result.stdout)

    def test_residual_resources_cannot_be_reported_as_success(self):
        project = f"coglatas-playwright-{RUN}-{ATTEMPT}-frontend-test"
        container, _, _ = self.add_project(project)
        self.state["retain"] = container
        result = self.invoke()
        self.assertNotEqual(0, result.returncode)
        self.assertIn("residual-owned-resource", result.stderr)

    def test_concurrent_disappearance_requires_successful_complete_inventory(self):
        for kind in ("container", "network", "volume"):
            for action in ("inspect", "rm"):
                with self.subTest(kind=kind, action=action):
                    self.state = {"calls": [], "container": {}, "network": {}, "volume": {}}
                    self.add_project(f"coglatas-playwright-{RUN}-{ATTEMPT}-frontend-test")
                    self.state["vanish_on"] = [kind, action]
                    result = self.invoke()
                    self.assertEqual(0, result.returncode, result.stderr)
                    inventories = [call for call in self.state["calls"] if call[:2] == [kind, "ls"] and "--filter" not in call]
                    self.assertEqual(1, len(inventories))
                    if kind == "container":
                        self.assertIn("--all", inventories[0])
                        self.assertIn("--no-trunc", inventories[0])

    def test_failed_inspect_or_remove_keeps_a_present_resource_blocking(self):
        for kind in ("container", "network", "volume"):
            for action in ("inspect", "rm"):
                with self.subTest(kind=kind, action=action):
                    self.state = {"calls": [], "container": {}, "network": {}, "volume": {}}
                    self.add_project(f"coglatas-playwright-{RUN}-{ATTEMPT}-frontend-test")
                    self.state["reject_on"] = [kind, action]
                    result = self.invoke()
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn(f"docker-{kind}-{action}-failed", result.stderr)
                    self.assertTrue(self.state[kind])
                    self.assertNotIn("sensitive-docker-error", result.stdout + result.stderr)

    def test_failed_or_malformed_inventory_cannot_prove_disappearance(self):
        for failure in ("inventory_error", "inventory_malformed"):
            with self.subTest(failure=failure):
                self.state = {"calls": [], "container": {}, "network": {}, "volume": {}}
                self.add_project(f"coglatas-playwright-{RUN}-{ATTEMPT}-frontend-test")
                self.state["vanish_on"] = ["container", "rm"]
                self.state[failure] = True
                result = self.invoke()
                self.assertNotEqual(0, result.returncode)
                self.assertIn("result=failure", result.stdout)

    def test_standalone_scanner_disappearance_requires_absence_inventory(self):
        project = f"coglatas-security-{RUN}-{ATTEMPT}-zap"
        self.add_project(project, scanner=True)
        self.state["vanish_on"] = ["container", "inspect"]
        result = self.invoke("security-scan", GITHUB_JOB="security-scan")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(any(call[:2] == ["container", "ls"] and "--filter" not in call for call in self.state["calls"]))

    def test_hung_docker_process_is_killed_with_failure_within_fixed_deadline(self):
        self.state["hang"] = "container"
        started = time.monotonic()
        result = self.invoke()
        self.assertLess(time.monotonic() - started, 18)
        self.assertNotEqual(0, result.returncode)
        self.assertIn("docker-timeout", result.stderr)
        pid = int(self.pid.read_text(encoding="utf-8"))
        with self.assertRaises(ProcessLookupError):
            os.kill(pid, 0)


if __name__ == "__main__":
    unittest.main()
