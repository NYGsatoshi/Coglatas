#!/usr/bin/env python3
"""Exercise the artifact restore boundary; hosted collectors verify the real runtime."""
from __future__ import annotations

import gzip
import hashlib
import io
import json
import os
import runpy
import shlex
import shutil
import subprocess
import sys
import tarfile
import tempfile
import unittest
from contextlib import nullcontext
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
SHA = "a" * 40
IMAGE_ID = "sha256:" + "b" * 64


def synthetic_runtime_environment():
    """Start each synthetic image scenario without inherited runtime identity."""
    environment = os.environ.copy()
    for name in (
        "COGLATAS_APP_IMAGE", "COGLATAS_REUSE_PREBUILT_APP_IMAGE",
        "COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID", "COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID",
        "COGLATAS_REUSE_PREBUILT_APP_IMAGE_ARCHIVE", "COGLATAS_REUSE_PREBUILT_APP_SOURCE_SHA",
        "COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING", "COGLATAS_REUSE_PREBUILT_DOTNET_STAMP",
        "COGLATAS_REUSE_PREBUILT_DOTNET_BUILD",
    ):
        environment.pop(name, None)
    return environment


HOSTILE_PARENT_RUNTIME_ENVIRONMENT = {
    "COGLATAS_APP_IMAGE": "synthetic-parent-runtime:" + "c" * 40,
    "COGLATAS_REUSE_PREBUILT_APP_IMAGE": "1",
    "COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID": "sha256:" + "d" * 64,
    "COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID": "sha256:" + "c" * 64,
    "COGLATAS_REUSE_PREBUILT_APP_IMAGE_ARCHIVE": "/synthetic-parent-runtime/archive.tar.gz",
    "COGLATAS_REUSE_PREBUILT_APP_SOURCE_SHA": "c" * 40,
    "COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING": "/synthetic-parent-runtime/binding.json",
    "COGLATAS_REUSE_PREBUILT_DOTNET_STAMP": "/synthetic-parent-runtime/dotnet-build-sha",
    "COGLATAS_REUSE_PREBUILT_DOTNET_BUILD": "true",
}

FAKE_ACCEPTANCE_DOCKER = r'''#!/usr/bin/env bash
set -euo pipefail
printf '%q ' "$@" >> "$TEST_TRACE"
printf '\n' >> "$TEST_TRACE"
changed_id="sha256:$(printf 'c%.0s' {1..64})"
if [[ "$1 $2" == 'image inspect' ]]; then
  [[ "$TEST_SCENARIO" != missing-image ]] || exit 1
  cat "$TEST_IMAGE_STATE"
  exit 0
fi
if [[ "$1" == inspect ]]; then
  if [[ "$3" == '{{.Image}}' ]]; then
    if [[ "$TEST_SCENARIO" == wrong-container ]]; then printf '%s\n' "$changed_id"; else printf '%s\n' "$TEST_ORIGINAL_IMAGE_ID"; fi
  else
    printf 'running healthy 0\n'
    if [[ "$TEST_SCENARIO" == retag-before-probe && "${@: -1}" == app-* ]]; then printf '%s\n' "$changed_id" > "$TEST_IMAGE_STATE"; fi
  fi
  exit 0
fi
[[ "$1" == compose ]] || exit 0
while (( $# > 0 )); do
  case "$1" in config|up|run|ps|exec|down|logs) command="$1"; shift; break ;; esac
  shift
done
case "$command" in
  config)
    if [[ " $* " == *' --format json '* ]]; then printf '{"services":{"app":{"image":"%s"}}}\n' "$TEST_COMPOSE_IMAGE"; fi
    ;;
  up)
    if [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" == 1 ]]; then
      [[ " $* " != *' --build '* && " $* " == *' --no-build '* && " $* " == *' --pull never '* ]] || exit 86
    fi
    count="$(cat "$TEST_APP_GENERATION")"; count=$((count + 1)); printf '%s\n' "$count" > "$TEST_APP_GENERATION"
    if [[ "$TEST_SCENARIO" == retag-up || ( "$TEST_SCENARIO" == retag-restart && "$count" == 2 ) ]]; then printf '%s\n' "$changed_id" > "$TEST_IMAGE_STATE"; fi
    ;;
  run)
    if [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" == 1 ]]; then [[ " $* " != *' --build '* && " $* " == *' --no-deps '* ]] || exit 87; fi
    if [[ " $* " == *' real-backend-playwright '* ]]; then
      if [[ "$TEST_SCENARIO" == retag-probe ]]; then printf '%s\n' "$changed_id" > "$TEST_IMAGE_STATE"; fi
      [[ "$TEST_SCENARIO" != failed-probe ]] || exit 22
    fi
    ;;
  ps)
    if [[ "${@: -1}" == app ]]; then printf 'app-%s\n' "$(cat "$TEST_APP_GENERATION")"; else printf 'postgres-fixture\n'; fi
    [[ "$TEST_SCENARIO" != ps-partial-failure ]] || exit 9
    ;;
  exec)
    if [[ " $* " == *' app '* ]]; then
      if [[ "$*" == *'find /app/data/protection-keys'* ]]; then printf 'anchor.xml|%s\n' "$TEST_DP_HASH"; else printf '%s\n' "$TEST_DP_HASH"; fi
    else
      sql="$(cat)"
      case "$TEST_LANE" in
        mbj01) printf '1|PlatformAdmin|1|1|1\n' ;;
        mbj02)
          if [[ "$sql" == *'INSERT INTO tenants'* ]]; then exit 0;
          elif [[ "$sql" == *'SELECT "Id"'* ]]; then printf '11111111-1111-1111-1111-111111111111\n';
          else printf '1|1|1|1|2|1|1|1|0|1|0|1|0|0|1|0|0\n'; fi ;;
        mbj03)
          if [[ "$sql" == *'WITH target AS'* ]]; then printf '1\n'; else printf '1|1|5|3|1|1|1|1|1|1|1|1|1\n'; fi ;;
      esac
    fi
    ;;
esac
'''

FAKE_FUNCTIONAL_DOCKER = r'''#!/usr/bin/env bash
set -euo pipefail
printf '%q ' "$@" >> "$TEST_TRACE"; printf '\n' >> "$TEST_TRACE"
changed="sha256:$(printf 'c%.0s' {1..64})"
if [[ "$1" == info || "$1 ${2:-}" == 'compose version' ]]; then exit 0; fi
if [[ "$1 $2" == 'image inspect' ]]; then
  [[ "$TEST_SCENARIO" != missing-image ]] || exit 1
  if [[ "$4" == '{{json .}}' ]]; then cat "$TEST_METADATA"; else cat "$TEST_IMAGE_STATE"; fi
  exit 0
fi
if [[ "$1" == inspect ]]; then
  if [[ "$3" == '{{.Image}}' ]]; then
    if [[ "$TEST_SCENARIO" == wrong-container ]]; then printf '%s\n' "$changed"; else printf '%s\n' "$TEST_ENGINE_ID"; fi
  elif [[ "${@: -1}" == migrate-fixture ]]; then printf 'exited none 0\n'
  else
    printf 'running healthy 0\n'
    if [[ "$TEST_SCENARIO" == retag-health && "${@: -1}" == app-fixture ]]; then printf '%s\n' "$changed" > "$TEST_IMAGE_STATE"; fi
  fi
  exit 0
fi
[[ "$1" == compose ]] || exit 1
while (( $# )); do
  case "$1" in config|build|up|ps|run|down|logs) command="$1"; shift; break;; esac
  shift
done
case "$command" in
  config) if [[ " $* " == *' --format json '* ]]; then printf '{"services":{"app":{"image":"%s"}}}\n' "$TEST_COMPOSE_IMAGE"; fi;;
  build) [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" != 1 ]];;
  up)
    if [[ "${@: -1}" == app ]]; then
      if [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" == 1 ]]; then
        [[ " $* " == *' --no-build '* && " $* " == *' --pull never '* && " $* " != *' --build '* ]] || exit 86
      fi
      touch "$TEST_APP_CREATED"
      if [[ "$TEST_SCENARIO" == retag-up ]]; then printf '%s\n' "$changed" > "$TEST_IMAGE_STATE"; fi
    fi;;
  ps) service="${@: -1}"; if [[ "$service" != app || -f "$TEST_APP_CREATED" ]]; then printf '%s-fixture\n' "$service"; fi;;
  run)
    if [[ "${COGLATAS_REUSE_PREBUILT_APP_IMAGE:-}" == 1 ]]; then [[ " $* " == *' --no-deps '* && " $* " != *' --build '* ]] || exit 87; fi
    if [[ "$TEST_SCENARIO" == retag-probe || "$TEST_SCENARIO" == retag-failed-probe ]]; then printf '%s\n' "$changed" > "$TEST_IMAGE_STATE"; fi
    if [[ "$TEST_SCENARIO" == failed-probe || "$TEST_SCENARIO" == retag-failed-probe ]]; then exit 22; fi;;
  down) rm -f "$TEST_APP_CREATED";;
  logs) printf 'synthetic fixture log\n';;
esac
'''


class RuntimeArtifactTests(unittest.TestCase):
    def restore(self, *, source=SHA, dotnet=SHA, image=None, identity=IMAGE_ID,
                actual=IMAGE_ID, event="push", missing=None):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            artifact, workspace, commands = (root / name for name in ("artifact", "workspace", "bin"))
            for path in (artifact, workspace, commands):
                path.mkdir()
            (artifact / "source-sha").write_text(source + "\n")
            family = "coglatas-pr-functional" if event == "pull_request" else "coglatas-main-runtime"
            (artifact / "runtime-image-name").write_text((image or family + ":" + SHA) + "\n")
            (artifact / "runtime-image-id").write_text(identity + "\n")
            with tarfile.open(artifact / "dotnet-release-build.tar", "w") as archive:
                content = (dotnet + "\n").encode()
                entry = tarfile.TarInfo("artifacts/ci/dotnet-build-sha")
                entry.size = len(content)
                archive.addfile(entry, io.BytesIO(content))
            with gzip.open(artifact / "runtime-image.tar.gz", "wb") as archive:
                archive.write(b"unit fixture for docker load")
            docker = commands / "docker"
            docker.write_text('#!/bin/sh\nif [ "$1" = load ]; then cat >/dev/null; exit 0; fi\n'
                              'if [ "$1" = image ] && [ "$2" = inspect ]; then\n'
                              '  printf "%s\\n" "$TEST_IMAGE_ID"; exit 0\nfi\nexit 1\n')
            docker.chmod(0o755)
            if missing:
                (artifact / missing).unlink()
            exported = root / "exported-env"
            result = subprocess.run(["bash", (ROOT / "scripts/ci/restore-main-build-artifacts.sh").as_posix(),
                                     "artifact", SHA, "workspace"], cwd=root,
                                    env=os.environ | {"PATH": str(commands) + os.pathsep + os.environ["PATH"],
                                                      "GITHUB_EVENT_NAME": event,
                                                      "GITHUB_ENV": str(exported), "TEST_IMAGE_ID": actual},
                                    capture_output=True, text=True)
            return result, exported.read_text() if exported.exists() else ""

    def test_matching_main_and_pr_artifacts_restore(self):
        for event in ("push", "pull_request"):
            result, exported = self.restore(event=event)
            with self.subTest(event=event):
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertIn("COGLATAS_REUSE_PREBUILT_APP_IMAGE=1", exported)
                self.assertIn(f"COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID={IMAGE_ID}", exported)

    def test_wrong_source_dotnet_tag_family_or_loaded_image_fails_before_export(self):
        changes = ({"source": "c" * 40}, {"dotnet": "c" * 40},
                   {"image": "coglatas-main-runtime:" + "c" * 40},
                   {"image": "coglatas-pr-functional:" + SHA},
                   {"actual": "sha256:" + "c" * 64}, {"identity": "invalid"})
        for change in changes:
            result, exported = self.restore(**change)
            with self.subTest(change=change):
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", exported)

    def test_missing_required_artifact_members_fail_before_export(self):
        for member in ("source-sha", "runtime-image-name", "runtime-image-id",
                       "runtime-image.tar.gz", "dotnet-release-build.tar"):
            result, exported = self.restore(missing=member)
            with self.subTest(member=member):
                self.assertNotEqual(0, result.returncode)
                self.assertEqual("", exported)

    def test_main_calls_exact_same_run_structural_collector_and_aggregates_its_result(self):
        workflow = (ROOT / ".github/workflows/main-build-artifacts.yml").read_text()
        call = workflow.split("  performance-db:\n", 1)[1].split("\n  licensed-real-backend:", 1)[0]
        for required in ("if: always()", "needs: main-build-artifacts", "actions: read",
                         "uses: ./.github/workflows/performance-db.yml",
                         "reuse_main_artifacts: true", "producer_run_id: ${{ github.run_id }}",
                         "target_sha: ${{ github.sha }}",
                         "producer_result: ${{ needs.main-build-artifacts.result }}"):
            self.assertIn(required, call)
        build = workflow.split("  build-test:\n", 1)[1].split("\n  frontend-test:", 1)[0]
        self.assertIn("needs: [main-dotnet-build, main-build-artifacts, main-validation, performance-db]", build)
        self.assertIn("if: always()", build)
        self.assertNotIn("--duration", call)
        for producer in (workflow, (ROOT / "scripts/ci/package-pr-functional-runtime.sh").read_text()):
            self.assertIn("runtime-image-id", producer)


class AcceptanceImageReuseTests(unittest.TestCase):
    wrappers = {
        "authz": ("run-mvp-a-authz-boundary-acceptance.sh", 1, 1, "mvp-a-authz"),
        "mbj01": ("run-mbj01-bootstrap-acceptance.sh", 2, 2, "mbj01"),
        "mbj02": ("run-mbj02-invite-acceptance.sh", 1, 1, "mbj02"),
        "mbj03": ("run-mbj03-session-acceptance.sh", 2, 3, "mbj03"),
    }

    def run_harness(self, lane, *, reuse="1", scenario="matching", expected=IMAGE_ID,
                    image="coglatas-main-runtime:" + SHA, configured=None, helper_command=None, workspace=None):
        with (tempfile.TemporaryDirectory() if workspace is None else nullcontext(str(workspace))) as directory:
            root = Path(directory)
            commands = root / "bin"
            commands.mkdir(exist_ok=True)
            docker = commands / "docker"
            docker.write_text(FAKE_ACCEPTANCE_DOCKER, encoding="utf-8", newline="\n")
            docker.chmod(0o755)
            python = commands / "python3"
            python.write_text('#!/bin/sh\nexec "$TEST_PYTHON" "$@"\n', encoding="utf-8", newline="\n")
            python.chmod(0o755)
            trace = root / "docker-trace"
            image_state = root / "image-state"
            image_state.write_text(IMAGE_ID + "\n")
            generation = root / "app-generation"
            generation.write_text("0\n")
            environment = synthetic_runtime_environment() | {
                "PATH": str(commands) + os.pathsep + os.environ["PATH"],
                "GITHUB_ACTIONS": "false", "COMPOSE_PROJECT_NAME": "acceptance-unit-fixture",
                "SYNCFUSION_LICENSE": "artifact-reuse-no-build",
                "TEST_PYTHON": Path(sys.executable).as_posix(), "TEST_TRACE": trace.as_posix(),
                "TEST_IMAGE_STATE": image_state.as_posix(), "TEST_APP_GENERATION": generation.as_posix(),
                "TEST_ORIGINAL_IMAGE_ID": IMAGE_ID, "TEST_SCENARIO": scenario,
                "TEST_COMPOSE_IMAGE": configured or image, "TEST_LANE": lane, "TEST_DP_HASH": "d" * 64,
                "COGLATAS_APP_IMAGE": image, "COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID": expected,
                "COGLATAS_MBJ01_BOOTSTRAP_PASSWORD": "synthetic-bootstrap-unit-only",
                "COGLATAS_MBJ02_ADMIN_PASSWORD": "synthetic-admin-unit-only",
                "COGLATAS_MBJ02_INVITEE_PASSWORD": "synthetic-invite-unit-only",
                "COGLATAS_MBJ02_CROSS_TENANT_TOKEN": "a" * 64,
                "COGLATAS_MBJ03_ADMIN_PASSWORD": "synthetic-session-admin-unit-only",
                "COGLATAS_MBJ03_OLD_PASSWORD": "synthetic-old-unit-only",
                "COGLATAS_MBJ03_NEW_PASSWORD": "synthetic-new-unit-only",
            }
            environment.pop("COGLATAS_REUSE_PREBUILT_APP_IMAGE", None)
            if reuse is not None:
                environment["COGLATAS_REUSE_PREBUILT_APP_IMAGE"] = reuse
            if helper_command:
                helper = (ROOT / "scripts/ci/acceptance-image-reuse.sh").as_posix()
                command = ["bash", "-c", 'set -euo pipefail; compose=(docker compose); source "$1"; acceptance_prepare_image_reuse; ' + helper_command, "test", helper]
            else:
                command = ["bash", str(ROOT / "scripts/ci" / self.wrappers[lane][0])]
            result = subprocess.run(command, cwd=root, env=environment, input="", capture_output=True,
                                    text=True, timeout=30)
            traces = [shlex.split(line) for line in trace.read_text().splitlines()] if trace.exists() else []
            receipt_path = root / "artifacts/acceptance-image-reuse" / (self.wrappers[lane][3] + "-image-reuse.json")
            receipt = json.loads(receipt_path.read_text()) if receipt_path.exists() else None
            return result, traces, receipt

    @staticmethod
    def compose_commands(traces, command):
        return [arguments[arguments.index(command) + 1:] for arguments in traces
                if arguments[0] == "compose" and command in arguments]

    def assert_stopped(self, result, traces, receipt, *, before_start=False):
        self.assertNotEqual(0, result.returncode, result.stdout)
        self.assertNotIn("acceptance passed", result.stdout)
        self.assertIsNone(receipt)
        if before_start:
            self.assertEqual([], self.compose_commands(traces, "up"))
            self.assertEqual([], self.compose_commands(traces, "run"))

    def test_reuse_preserves_each_wrapper_phase_without_any_build(self):
        for lane, (_, startup_count, probe_count, _) in self.wrappers.items():
            with self.subTest(lane=lane):
                result, traces, receipt = self.run_harness(lane)
                self.assertEqual(0, result.returncode, result.stderr)
                starts = self.compose_commands(traces, "up")
                self.assertEqual(startup_count, len(starts))
                for arguments in starts:
                    self.assertIn("--no-build", arguments)
                    self.assertNotIn("--build", arguments)
                    self.assertEqual("never", arguments[arguments.index("--pull") + 1])
                probes = [args for args in self.compose_commands(traces, "run") if "real-backend-playwright" in args]
                self.assertEqual(probe_count, len(probes))
                for arguments in probes:
                    self.assertIn("--no-deps", arguments)
                    self.assertNotIn("--build", arguments)
                if lane == "mbj02":
                    self.assertEqual(1, len([args for args in self.compose_commands(traces, "run") if "migrate" in args]))
                self.assertEqual(1, len(self.compose_commands(traces, "down")))
                self.assertFalse(any("--build" in args or "build" == args[0] for args in traces))
                self.assertFalse(any("artifact-reuse-no-build" in arg for args in traces for arg in args))
                self.assertEqual(IMAGE_ID, receipt["expectedProducerImageId"])
                self.assertTrue(receipt["tagAndAppContainerMatched"])

    def test_default_and_disabled_reuse_keep_local_build_and_restart_behavior(self):
        for lane, (_, startup_count, probe_count, _) in self.wrappers.items():
            for reuse in (None, "0"):
                with self.subTest(lane=lane, reuse=reuse):
                    result, traces, receipt = self.run_harness(lane, reuse=reuse, expected="")
                    self.assertEqual(0, result.returncode, result.stderr)
                    starts = self.compose_commands(traces, "up")
                    self.assertEqual(startup_count, len(starts))
                    self.assertIn("--build", starts[0])
                    for restart in starts[1:]:
                        self.assertNotIn("--build", restart)
                        self.assertNotIn("--no-build", restart)
                        self.assertIn("--force-recreate", restart)
                    probes = [args for args in self.compose_commands(traces, "run") if "real-backend-playwright" in args]
                    self.assertEqual(probe_count, len(probes))
                    for arguments in probes:
                        self.assertIn("--build", arguments)
                        self.assertEqual(lane == "mbj03", "--no-deps" in arguments)
                    self.assertFalse(any(args[:2] == ["image", "inspect"] for args in traces))
                    self.assertIsNone(receipt)

    def test_missing_invalid_or_wrong_producer_binding_fails_before_start(self):
        for lane in self.wrappers:
            for binding in ({"expected": ""}, {"expected": "not-an-image-id"},
                            {"expected": "sha256:" + "c" * 64}, {"image": ""}):
                with self.subTest(lane=lane, binding=binding):
                    self.assert_stopped(*self.run_harness(lane, **binding), before_start=True)

    def test_missing_image_or_wrong_compose_reference_fails_before_start(self):
        for lane in self.wrappers:
            for change in ({"scenario": "missing-image"}, {"configured": "different-app:fixture"}):
                with self.subTest(lane=lane, change=change):
                    self.assert_stopped(*self.run_harness(lane, **change), before_start=True)

    def test_retag_during_start_or_probe_is_not_accepted(self):
        for lane in self.wrappers:
            for scenario in ("retag-up", "retag-probe", "retag-before-probe"):
                with self.subTest(lane=lane, scenario=scenario):
                    self.assert_stopped(*self.run_harness(lane, scenario=scenario))

    def test_running_container_must_match_producer_image_before_probe(self):
        for lane in self.wrappers:
            with self.subTest(lane=lane):
                result, traces, receipt = self.run_harness(lane, scenario="wrong-container")
                self.assert_stopped(result, traces, receipt)
                self.assertEqual([], self.compose_commands(traces, "run"))

    def test_bootstrap_and_session_restarts_cannot_retag_the_producer_image(self):
        for lane in ("mbj01", "mbj03"):
            with self.subTest(lane=lane):
                result, traces, receipt = self.run_harness(lane, scenario="retag-restart")
                self.assert_stopped(result, traces, receipt)
                self.assertEqual(2, len(self.compose_commands(traces, "up")))
                self.assertEqual(1, len(self.compose_commands(traces, "run")))

    def test_reuse_cannot_override_build_or_pull_policy(self):
        for command in ("acceptance_compose_up build --build app",
                        "acceptance_compose_up build --build=true app",
                        "acceptance_compose_up build --no-build=false app",
                        "acceptance_compose_up build --pull=always app",
                        "acceptance_compose_up build --pull always app",
                        "acceptance_compose_run dependencies --build real-backend-playwright",
                        "acceptance_compose_run dependencies --build=true real-backend-playwright",
                        "acceptance_compose_run dependencies --no-deps real-backend-playwright",
                        "acceptance_compose_run dependencies --no-deps=false real-backend-playwright"):
            with self.subTest(command=command):
                result, traces, receipt = self.run_harness("authz", helper_command=command)
                self.assert_stopped(result, traces, receipt, before_start=True)
                self.assertIn("Acceptance reuse cannot", result.stderr)

    def test_failed_receipt_verification_propagates_in_conditional_context(self):
        for scenario in ("matching", "ps-partial-failure"):
            with self.subTest(scenario=scenario):
                command = 'mkdir -p test-results; '
                if scenario == "matching":
                    command += 'printf "%s\\n" "sha256:' + "c" * 64 + '" > "$TEST_IMAGE_STATE"; '
                command += ('if acceptance_record_image_reuse artifacts/acceptance-image-reuse/mvp-a-authz-image-reuse.json; '
                            'then exit 0; else exit 41; fi')
                result, traces, receipt = self.run_harness("authz", scenario=scenario, helper_command=command)
                self.assert_stopped(result, traces, receipt, before_start=True)
                self.assertEqual(41, result.returncode)

    def test_hosted_upload_is_scoped_to_the_four_sanitized_receipts(self):
        workflow = (ROOT / ".github/workflows/licensed-real-backend-acceptance.yml").read_text()
        upload = workflow.split("      - name: Upload prebuilt acceptance image continuity receipts\n", 1)[1]
        upload = upload.split("\n      - name:", 1)[0]
        self.assertIn("if: always() && inputs.reuse_main_artifacts == true", upload)
        self.assertIn("name: acceptance-image-reuse-${{ github.sha }}", upload)
        paths = upload.split("          path: |\n", 1)[1].split("          if-no-files-found:", 1)[0]
        self.assertEqual({"artifacts/acceptance-image-reuse/" + item[3] + "-image-reuse.json" for item in self.wrappers.values()},
                         {line.strip() for line in paths.splitlines()})
        self.assertIn("if-no-files-found: ignore", upload)
        self.assertIn("retention-days: 7", upload)
        reset = workflow.split("      - name: Reset current prebuilt acceptance image receipts\n", 1)[1].split("\n      - name:", 1)[0]
        verify = workflow.split("      - name: Verify all four current prebuilt acceptance image receipts\n", 1)[1].split("\n      - name:", 1)[0]
        self.assertIn("if: inputs.reuse_main_artifacts == true", reset)
        self.assertIn("run: python3 scripts/ci/verify-acceptance-image-reuse.py --reset", reset)
        self.assertIn("if: success() && inputs.reuse_main_artifacts == true", verify)
        self.assertIn("run: python3 scripts/ci/verify-acceptance-image-reuse.py", verify)
        self.assertNotIn("continue-on-error", verify)
        self.assertLess(workflow.index(reset), workflow.index("      - name: Run P0 acceptance"))
        cleanup = workflow.split("      - name: Check receipt retention across actual Playwright output cleanup\n", 1)[1].split("\n      - name:", 1)[0]
        self.assertIn("if: inputs.reuse_main_artifacts == true", cleanup)
        self.assertIn("COGLATAS_RECEIPT_TEST_PLAYWRIGHT_CLI: ${{ github.workspace }}/node_modules/@playwright/test/cli.js", cleanup)
        self.assertIn("run: python3 scripts/ci/test-main-runtime-artifact.py AcceptanceImageReuseTests.test_shared_workspace_receipts_survive_the_runner_output_cleanup_boundary", cleanup)
        self.assertLess(workflow.index(reset), workflow.index(cleanup))
        self.assertLess(workflow.index(cleanup), workflow.index("      - name: Run P0 acceptance"))
        self.assertLess(workflow.index("      - name: Run MBJ-03 session acceptance"), workflow.index(verify))
        self.assertLess(workflow.index(verify), workflow.index(upload))
        contract = (ROOT / ".github/workflows/performance-contract.yml").read_text()
        triggers = contract.split("  pull_request:\n    paths:\n", 1)[1].split("\nconcurrency:", 1)[0]
        for path in (".github/workflows/licensed-real-backend-acceptance.yml",
                     "scripts/ci/acceptance-image-reuse.sh", "scripts/ci/restore-main-build-artifacts.sh",
                     "scripts/ci/test-main-runtime-artifact.py",
                     "scripts/ci/verify-acceptance-image-reuse.py",
                     *("scripts/ci/" + wrapper[0] for wrapper in self.wrappers.values())):
            self.assertIn('      - "' + path + '"', triggers)
        self.assertIn("python3 scripts/ci/test-main-runtime-artifact.py", contract)

    def test_failed_probe_keeps_failure_status_without_a_reuse_receipt(self):
        for lane in self.wrappers:
            with self.subTest(lane=lane):
                result, traces, receipt = self.run_harness(lane, scenario="failed-probe")
                self.assert_stopped(result, traces, receipt)
                self.assertEqual(22, result.returncode)

    def run_receipt_validator(self, workspace, *, reset=False, expected=IMAGE_ID, reuse="1"):
        return subprocess.run([sys.executable, str(ROOT / "scripts/ci/verify-acceptance-image-reuse.py"),
                               *(["--reset"] if reset else [])], cwd=workspace,
                              env=synthetic_runtime_environment() | {"COGLATAS_REUSE_PREBUILT_APP_IMAGE": reuse,
                                                "COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID": expected},
                              capture_output=True, text=True, timeout=10)

    def write_receipt_fixture(self, workspace):
        directory = workspace / "artifacts/acceptance-image-reuse"
        directory.mkdir(parents=True)
        receipt = {"schemaVersion": 1, "expectedProducerImageId": IMAGE_ID,
                   "tagAndAppContainerMatched": True, "scope": "within-this-acceptance-wrapper"}
        for _, _, _, prefix in self.wrappers.values():
            (directory / (prefix + "-image-reuse.json")).write_text(json.dumps(receipt))
        return directory, receipt

    def test_shared_workspace_receipts_survive_the_runner_output_cleanup_boundary(self):
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            self.assertEqual(0, self.run_receipt_validator(workspace, reset=True).returncode)
            result, _, receipt = self.run_harness("authz", workspace=workspace)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertIsNotNone(receipt)
            old_receipt = workspace / "test-results/mvp-a-authz-image-reuse.json"
            old_receipt.write_text(json.dumps(receipt))
            playwright_cli = os.environ.get("COGLATAS_RECEIPT_TEST_PLAYWRIGHT_CLI")
            if playwright_cli:
                # Use the installed CLI's default output cleanup without launching a browser.
                package = Path(playwright_cli).resolve().parent.as_posix()
                (workspace / "cleanup.spec.cjs").write_text(
                    'const { test, expect } = require(' + json.dumps(package) + ');\n'
                    'test("default output cleanup boundary", () => expect(true).toBe(true));\n')
                (workspace / "cleanup.config.cjs").write_text(
                    'module.exports = { testDir: ".", testMatch: "cleanup.spec.cjs", '
                    'reporter: "line", retries: 0, workers: 1 };\n')
                cleanup = subprocess.run(["node", playwright_cli, "test", "--config", "cleanup.config.cjs"],
                                         cwd=workspace, capture_output=True, text=True, timeout=30)
                self.assertEqual(0, cleanup.returncode, cleanup.stdout + cleanup.stderr)
            else:
                # Dependency-free contract CI models the runner's same directory removal.
                shutil.rmtree(workspace / "test-results")
            self.assertFalse(old_receipt.exists())
            self.assertNotEqual(0, self.run_receipt_validator(workspace).returncode)
            for lane in ("mbj01", "mbj02", "mbj03"):
                result, _, _ = self.run_harness(lane, workspace=workspace)
                self.assertEqual(0, result.returncode, result.stderr)
            result = self.run_receipt_validator(workspace)
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(4, len(list((workspace / "artifacts/acceptance-image-reuse").glob("*.json"))))

    def test_synthetic_acceptance_fixture_owns_inherited_runtime_identity(self):
        with tempfile.TemporaryDirectory() as directory, patch.dict(os.environ, HOSTILE_PARENT_RUNTIME_ENVIRONMENT):
            workspace = Path(directory)
            reset = self.run_receipt_validator(workspace, reset=True)
            self.assertEqual(0, reset.returncode, reset.stderr)
            for lane in self.wrappers:
                with self.subTest(lane=lane):
                    result, traces, receipt = self.run_harness(lane, workspace=workspace)
                    self.assertEqual(0, result.returncode, result.stderr)
                    self.assertEqual(IMAGE_ID, receipt["expectedProducerImageId"])
                    self.assertEqual(1, receipt["schemaVersion"])
                    self.assertTrue(receipt["tagAndAppContainerMatched"])
                    self.assertFalse(any("--build" in args or "build" == args[0] for args in traces))
            verified = self.run_receipt_validator(workspace)
            self.assertEqual(0, verified.returncode, verified.stderr)
            self.assertNotEqual(0, self.run_receipt_validator(workspace, expected="sha256:" + "c" * 64).returncode)

    def test_all_four_receipts_are_required_and_reset_does_not_touch_other_artifacts(self):
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            receipts, _ = self.write_receipt_fixture(workspace)
            self.assertEqual(0, self.run_receipt_validator(workspace).returncode)
            for path in receipts.iterdir():
                original = path.read_text()
                path.unlink()
                result = self.run_receipt_validator(workspace)
                self.assertNotEqual(0, result.returncode)
                self.assertIn("exactly the four canonical receipt files", result.stderr)
                path.write_text(original)
            other = workspace / "artifacts/unrelated.json"
            other.write_text("unrelated fixture")
            self.assertEqual(0, self.run_receipt_validator(workspace, reset=True).returncode)
            self.assertEqual("unrelated fixture", other.read_text())
            self.assertEqual([], list(receipts.iterdir()))
            self.assertNotEqual(0, self.run_receipt_validator(workspace).returncode)

    def test_invalid_or_wrong_producer_receipts_fail_without_echoing_contents(self):
        changes = [{"schemaVersion": True}, {"schemaVersion": 2}, {"expectedProducerImageId": "sha256:" + "c" * 64},
                   {"tagAndAppContainerMatched": False}, {"tagAndAppContainerMatched": 1},
                   {"scope": "global-runtime"}, {"unexpected": "synthetic-content-must-not-be-echoed"}]
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            receipts, receipt = self.write_receipt_fixture(workspace)
            path = receipts / "mvp-a-authz-image-reuse.json"
            invalid_documents = [json.dumps(receipt | change) for change in changes]
            invalid_documents += ["not-json", "[]", "null", "x" * 2049,
                                  json.dumps(receipt).replace('"schemaVersion": 1', '"schemaVersion": 1, "schemaVersion": 1'),
                                  json.dumps({name: value for name, value in receipt.items() if name != "scope"}), '\udcff']
            for content in invalid_documents:
                with self.subTest(content=content[:80]):
                    path.write_bytes(content.encode("utf-8", errors="surrogateescape"))
                    result = self.run_receipt_validator(workspace)
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn(path.name, result.stderr)
                    self.assertNotIn("synthetic-content-must-not-be-echoed", result.stderr)
            path.write_text(json.dumps(receipt))
            for change in ({"expected": ""}, {"expected": IMAGE_ID + "\n"},
                           {"expected": "sha256:" + "c" * 64}, {"reuse": "0"}):
                self.assertNotEqual(0, self.run_receipt_validator(workspace, **change).returncode)

    def test_redirected_receipt_parents_fail_before_reset_or_verify(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            outside = root / "outside"
            outside.mkdir()
            receipts, _ = self.write_receipt_fixture(outside)
            original = {path.name: path.read_text() for path in receipts.iterdir()}
            for parent in ("artifacts", "artifacts/acceptance-image-reuse"):
                with self.subTest(parent=parent):
                    workspace = root / ("workspace-" + str(len(parent)))
                    workspace.mkdir()
                    link = workspace / parent
                    link.parent.mkdir(parents=True, exist_ok=True)
                    target = outside / parent
                    if os.name == "nt":
                        result = subprocess.run(["cmd", "/c", "mklink", "/J", str(link), str(target)],
                                                capture_output=True, text=True)
                        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                    else:
                        link.symlink_to(target, target_is_directory=True)
                    for reset in (False, True):
                        result = self.run_receipt_validator(workspace, reset=reset)
                        self.assertNotEqual(0, result.returncode)
                        self.assertIn("without redirected parents", result.stderr)
                    self.assertEqual(original, {path.name: path.read_text() for path in receipts.iterdir()})

    def test_extra_entries_and_non_regular_receipts_are_not_qualified(self):
        with tempfile.TemporaryDirectory() as directory:
            workspace = Path(directory)
            receipts, _ = self.write_receipt_fixture(workspace)
            extra = receipts / "unknown-image-reuse.json"
            extra.write_text("unknown fixture")
            self.assertNotEqual(0, self.run_receipt_validator(workspace).returncode)
            extra.unlink()
            extra.mkdir()
            self.assertNotEqual(0, self.run_receipt_validator(workspace).returncode)
            extra.rmdir()
            path = receipts / "mvp-a-authz-image-reuse.json"
            original = path.read_text()
            path.unlink()
            path.mkdir()
            self.assertNotEqual(0, self.run_receipt_validator(workspace).returncode)
            path.rmdir()
            if hasattr(os, "mkfifo"):
                os.mkfifo(path)
                self.assertNotEqual(0, self.run_receipt_validator(workspace).returncode)
                path.unlink()
                outside = workspace / "outside.json"
                outside.write_text(original)
                path.symlink_to(outside)
                self.assertNotEqual(0, self.run_receipt_validator(workspace).returncode)
                self.assertEqual(original, outside.read_text())
                path.unlink()
            path.write_text(original)
            extra.write_text("unknown fixture")
            self.assertEqual(0, self.run_receipt_validator(workspace, reset=True).returncode)
            self.assertEqual("unknown fixture", extra.read_text())
            self.assertNotEqual(0, self.run_receipt_validator(workspace).returncode)


class FunctionalImageReuseTests(unittest.TestCase):
    def run_harness(self, *, reuse="1", scenario="matching", changes=None, workspace=None):
        with (tempfile.TemporaryDirectory() if workspace is None else nullcontext(str(workspace))) as directory:
            root = Path(directory)
            commands = root / "functional-unit-bin"
            commands.mkdir()
            docker = commands / "docker"
            docker.write_text(FAKE_FUNCTIONAL_DOCKER, encoding="utf-8", newline="\n")
            docker.chmod(0o755)
            git = commands / "git"
            git.write_text('''#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == status ]]; then
  [[ "$TEST_SCENARIO" != git-status-failure ]] || exit 7
  if [[ "$TEST_SCENARIO" == dirty-source ]]; then printf ' M source-fixture\n'; fi
elif [[ "$1" == cat-file ]]; then
  if [[ "$TEST_SCENARIO" == unavailable-tool ]]; then exit 7; fi
  if [[ "$TEST_SCENARIO" == altered-tool ]]; then printf 'wrong tool bytes'; else cat "$TEST_TOOL_DIRECTORY/${3##*/}"; fi
elif [[ "$1" == -C ]]; then printf '%s\n' "$COGLATAS_FUNCTIONAL_TOOL_SHA"
else printf '%s\n' "$TEST_SOURCE_SHA"; fi
''', encoding="utf-8", newline="\n")
            git.chmod(0o755)
            stamp = root / "artifacts/ci/dotnet-build-sha"
            stamp.parent.mkdir(parents=True, exist_ok=True)
            if not stamp.exists():
                stamp.write_text(SHA + "\n")
            if scenario == "wrong-dotnet-stamp":
                stamp.write_text("c" * 40 + "\n")
            state, trace = root / "functional-image-state", root / "functional-docker-trace"
            environment = synthetic_runtime_environment() | {"PATH": str(commands) + os.pathsep + os.environ["PATH"],
                "TEST_TRACE": str(trace), "TEST_IMAGE_STATE": str(state), "TEST_APP_CREATED": str(root / "app-created"),
                "TEST_ENGINE_ID": IMAGE_ID, "TEST_SCENARIO": scenario, "TEST_SOURCE_SHA": SHA,
                "TEST_TOOL_DIRECTORY": str(ROOT / "scripts/ci"),
                "TEST_COMPOSE_IMAGE": "coglatas-pr-functional:" + SHA,
                "COGLATAS_APP_IMAGE": "coglatas-pr-functional:" + SHA,
                "COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID": IMAGE_ID,
                "COGLATAS_REUSE_PREBUILT_APP_SOURCE_SHA": SHA, "COGLATAS_REUSE_PREBUILT_DOTNET_STAMP": str(stamp),
                "COGLATAS_FUNCTIONAL_TOOL_SHA": "d" * 40, "FUNCTIONAL_COMPOSE_PROJECT_NAME": "functional-unit-fixture"}
            environment.pop("COGLATAS_REUSE_PREBUILT_APP_IMAGE", None)
            if reuse is not None:
                environment["COGLATAS_REUSE_PREBUILT_APP_IMAGE"] = reuse
            environment |= changes or {}
            state.write_text(environment["TEST_ENGINE_ID"] + "\n")
            result = subprocess.run(["bash", str(ROOT / "scripts/ci/functional-compose-harness.sh")], cwd=root,
                                    env=environment, capture_output=True, text=True, timeout=30)
            traces = [shlex.split(line) for line in trace.read_text().splitlines()] if trace.exists() else []
            records = [json.loads(line.removeprefix("FUNCTIONAL_IMAGE_REUSE ")) for line in result.stdout.splitlines()
                       if line.startswith("FUNCTIONAL_IMAGE_REUSE ")]
            self.assertFalse((root / "artifacts/acceptance-image-reuse").exists())
            return result, traces, records

    def test_synthetic_functional_fixture_owns_inherited_runtime_identity(self):
        with patch.dict(os.environ, HOSTILE_PARENT_RUNTIME_ENVIRONMENT):
            result, traces, records = self.run_harness()
            self.assertEqual(0, result.returncode, result.stderr)
            self.assertEqual(1, len(records))
            self.assertEqual(IMAGE_ID, records[0]["expectedProducerImageId"])
            self.assertFalse(any("--build" in args or "build" == args[0] for args in traces))
            result, _, records = self.run_harness(changes={"COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID": "sha256:" + "c" * 64})
            self.assertNotEqual(0, result.returncode)
            self.assertEqual([], records)

    def test_reuse_checks_container_before_and_after_probe_and_records_separate_tool(self):
        result, traces, records = self.run_harness()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(1, len(records))
        self.assertEqual((SHA, "d" * 40, 0), (records[0]["sourceSha"], records[0]["toolSourceClaim"], records[0]["suiteExitCode"]))
        self.assertEqual(hashlib.sha256((ROOT / "scripts/ci/functional-compose-harness.sh").read_bytes()).hexdigest(), records[0]["harnessSha256"])
        starts = AcceptanceImageReuseTests.compose_commands(traces, "up")
        app = next(args for args in starts if args[-1] == "app")
        self.assertIn("--no-build", app)
        self.assertEqual("never", app[app.index("--pull") + 1])
        probe_index = next(index for index, args in enumerate(traces) if "run" in args)
        cleanup_index = next(index for index, args in enumerate(traces) if "down" in args)
        container_checks = [index for index, args in enumerate(traces) if args[:3] == ["inspect", "--format", "{{.Image}}"]]
        self.assertTrue(any(index < probe_index for index in container_checks))
        self.assertTrue(any(probe_index < index < cleanup_index for index in container_checks))
        self.assertIn("--no-deps", traces[probe_index])
        self.assertEqual(1, len(AcceptanceImageReuseTests.compose_commands(traces, "down")))

    def test_nonreuse_retains_original_build_start_and_probe_commands(self):
        for reuse in (None, "0"):
            with self.subTest(reuse=reuse):
                result, traces, records = self.run_harness(reuse=reuse)
                self.assertEqual(0, result.returncode, result.stderr)
                self.assertEqual([], records)
                self.assertEqual([["app"]], AcceptanceImageReuseTests.compose_commands(traces, "build"))
                app = next(args for args in AcceptanceImageReuseTests.compose_commands(traces, "up") if args[-1] == "app")
                self.assertEqual(["--detach", "--no-deps", "--no-build", "app"], app)
                self.assertEqual([["--rm", "real-backend-playwright"]], AcceptanceImageReuseTests.compose_commands(traces, "run"))
                self.assertFalse(any(args[:2] == ["image", "inspect"] for args in traces))

    def test_wrong_source_stamp_dirty_git_tool_or_image_fails_before_start(self):
        cases = (("matching", {"TEST_SOURCE_SHA": "c" * 40}), ("matching", {"COGLATAS_REUSE_PREBUILT_DOTNET_STAMP": "absent"}),
                 ("dirty-source", {}), ("git-status-failure", {}), ("wrong-dotnet-stamp", {}),
                 ("altered-tool", {}), ("unavailable-tool", {}), ("matching", {"COGLATAS_FUNCTIONAL_TOOL_SHA": "invalid"}),
                 ("missing-image", {}), ("matching", {"COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID": "sha256:" + "c" * 64}),
                 ("matching", {"TEST_COMPOSE_IMAGE": "another-app:tag"}))
        for scenario, changes in cases:
            with self.subTest(scenario=scenario, changes=changes):
                result, traces, records = self.run_harness(scenario=scenario, changes=changes)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual([], records)
                self.assertEqual([], AcceptanceImageReuseTests.compose_commands(traces, "up"))

    def test_wrong_container_or_retag_before_probe_prevents_owner_execution(self):
        for scenario in ("wrong-container", "retag-up", "retag-health"):
            with self.subTest(scenario=scenario):
                result, traces, records = self.run_harness(scenario=scenario)
                self.assertNotEqual(0, result.returncode)
                self.assertEqual([], records)
                self.assertEqual([], AcceptanceImageReuseTests.compose_commands(traces, "run"))

    def test_postprobe_binding_failure_is_checked_before_cleanup_without_success_record(self):
        for scenario, code in (("retag-probe", 1), ("retag-failed-probe", 22)):
            with self.subTest(scenario=scenario):
                result, traces, records = self.run_harness(scenario=scenario)
                self.assertEqual(code, result.returncode, result.stderr)
                self.assertEqual([], records)
                self.assertNotIn("completed successfully", result.stdout)
                probe = next(index for index, args in enumerate(traces) if "run" in args)
                cleanup = next(index for index, args in enumerate(traces) if "down" in args)
                self.assertTrue(any(probe < index < cleanup for index, args in enumerate(traces) if args[:2] == ["image", "inspect"]))

    def test_failed_owner_stays_failed_with_verified_binding_record_before_cleanup(self):
        result, traces, records = self.run_harness(scenario="failed-probe")
        self.assertEqual(22, result.returncode, result.stderr)
        self.assertEqual(1, len(records))
        self.assertEqual(22, records[0]["suiteExitCode"])
        self.assertTrue(records[0]["beforeProbeVerified"] and records[0]["afterProbeVerified"])
        self.assertNotIn("completed successfully", result.stdout)
        probe = next(index for index, args in enumerate(traces) if "run" in args)
        cleanup = next(index for index, args in enumerate(traces) if "down" in args)
        checks = [index for index, args in enumerate(traces) if args[:3] == ["inspect", "--format", "{{.Image}}"]]
        self.assertTrue(any(probe < index < cleanup for index in checks))


class RuntimeImageIdentityTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.artifact, self.workspace, self.bin = (self.root / name for name in ("artifact", "workspace", "bin"))
        for path in (self.artifact, self.workspace, self.bin):
            path.mkdir()
        self.image = "coglatas-pr-functional:" + SHA
        self.marker = "synthetic-config-content-must-not-be-echoed"
        self.helper = ROOT / "scripts/ci/runtime_image_identity.py"
        self.archive = self.artifact / "runtime-image.tar.gz"
        self.proof = self.workspace / "artifacts/ci/runtime-image-binding.json"
        self.build_archive()
        self.dotnet_stamp = self.workspace / "artifacts/ci/dotnet-build-sha"
        self.dotnet_stamp.parent.mkdir(parents=True)
        self.dotnet_stamp.write_text(SHA + "\n")
        docker = self.bin / "docker"
        docker.write_text('''#!/usr/bin/env bash
set -euo pipefail
if [[ "$1" == load ]]; then cat >/dev/null; exit 0; fi
if [[ "$1 $2" == 'image inspect' ]]; then
  if [[ "$4" == '{{json .}}' ]]; then cat "$TEST_METADATA"; else printf '%s\\n' "$TEST_ENGINE_ID"; fi
  exit 0
fi
if [[ "$1" == inspect ]]; then printf '%s\\n' "$TEST_CONTAINER_ENGINE_ID"; exit 0; fi
if [[ "$1" == compose ]]; then
  while (( $# )); do
    case "$1" in config) printf '{"services":{"app":{"image":"%s"}}}\\n' "$TEST_IMAGE_NAME"; exit 0;;
      ps) printf 'synthetic-app-container\\n'; exit 0;; esac
    shift
  done
fi
exit 1
''', encoding="utf-8")
        docker.chmod(0o755)

    def tearDown(self):
        self.temporary.cleanup()

    @staticmethod
    def encoded(value):
        return json.dumps(value, separators=(",", ":")).encode()

    @staticmethod
    def digest(value):
        return "sha256:" + hashlib.sha256(value).hexdigest()

    def build_archive(self, change="valid"):
        layer_bytes = []
        for index in range(2):
            output = io.BytesIO()
            with tarfile.open(fileobj=output, mode="w") as archive:
                entry = tarfile.TarInfo(f"synthetic-{index}.txt")
                payload = f"synthetic layer {index}".encode()
                entry.size = len(payload)
                archive.addfile(entry, io.BytesIO(payload))
            layer_bytes.append(output.getvalue())
        config = {"os": "linux", "architecture": "arm64" if change == "platform" else "amd64",
                  "config": {"Env": [self.marker], "Cmd": [self.marker]},
                  "rootfs": {"type": "layers", "diff_ids": [self.digest(value) for value in layer_bytes]}}
        if change.startswith("v1-"):
            config["created"] = "2026-01-02T03:04:05Z"
            config["config"]["Env"] = [self.marker + "<&>\u2028"]
            config["config"]["ExposedPorts"] = {"8080/tcp": {}}
        config_bytes = self.encoded(config)
        self.producer = self.digest(config_bytes)
        layers = [{"mediaType": "application/vnd.oci.image.layer.v1.tar", "digest": self.digest(value), "size": len(value)}
                  for value in layer_bytes]
        if change == "order":
            layers.reverse()
        if change == "size":
            layers[0]["size"] += 1
        if change == "compression":
            layers[0]["mediaType"] += "+gzip"
        manifest = {"schemaVersion": 2, "mediaType": "application/vnd.oci.image.manifest.v1+json",
                    "config": {"mediaType": "application/vnd.oci.image.config.v1+json", "digest": self.producer, "size": len(config_bytes)},
                    "layers": layers}
        manifest_bytes = self.encoded(manifest)
        self.engine = self.digest(manifest_bytes)
        index = {"schemaVersion": 2, "manifests": [{"mediaType": manifest["mediaType"], "digest": self.engine, "size": len(manifest_bytes)}]}
        if change == "multi":
            index["manifests"].append(dict(index["manifests"][0]))
        if change == "index-platform":
            index["manifests"][0]["platform"] = {"os": "windows", "architecture": "amd64"}
        if change == "index-size":
            index["manifests"][0]["size"] += 1
        path = lambda digest: "blobs/sha256/" + digest.removeprefix("sha256:")
        legacy = [{"Config": path(self.producer), "RepoTags": [self.image], "Layers": [path(layer["digest"]) for layer in layers]}]
        if change == "legacy-tag":
            legacy[0]["RepoTags"] = ["different-runtime:" + SHA]
        if change == "legacy-order":
            legacy[0]["Layers"].reverse()
        members = {"oci-layout": self.encoded({"imageLayoutVersion": "1.0.0"}), "index.json": self.encoded(index),
                   "manifest.json": self.encoded(legacy), path(self.producer): config_bytes, path(self.engine): manifest_bytes}
        members.update({path(self.digest(value)): value for value in layer_bytes})
        if change.startswith("v1-"):
            # Independent fixture transcription of the pinned Moby v1 exporter;
            # do not call the verifier to generate its own expected graph.
            empty = dict(Hostname="", Domainname="", User="", AttachStdin=False, AttachStdout=False,
                         AttachStderr=False, Tty=False, OpenStdin=False, StdinOnce=False, Env=None,
                         Cmd=None, Image="", Volumes=None, WorkingDir="", Entrypoint=None, OnBuild=None, Labels=None)
            previous, chain, auxiliaries = "", "", []
            for number, diff in enumerate(config["rootfs"]["diff_ids"]):
                top = number == len(layer_bytes) - 1
                document = {"created": config["created"] if top else "1970-01-01T00:00:00Z",
                            "container_config": dict(empty)}
                if previous:
                    document["parent"] = previous
                if top:
                    projected = empty | config["config"]
                    order = list(empty)[:6] + ["ExposedPorts"] + list(empty)[6:]
                    document |= {"config": {key: projected[key] for key in order if key in projected},
                                 "architecture": "amd64", "os": "linux"}
                if change == "v1-config" and top:
                    document["config"]["Cmd"] = ["unwanted command"]
                if change == "v1-architecture" and top:
                    document["architecture"] = "arm64"
                if change == "v1-defaults" and not top:
                    document["container_config"]["User"] = "unwanted user"
                if change == "v1-default-type" and not top:
                    document["container_config"]["AttachStdin"] = 0
                if change == "v1-created" and not top:
                    document["created"] = config["created"]
                if change == "v1-extra-key" and top:
                    document["unrecognized"] = self.marker
                chain = diff if not chain else self.digest((chain + " " + diff).encode())
                identity_input = document | {"layer_id": chain}
                if previous:
                    identity_input["parent"] = "sha256:" + previous
                identity_json = json.dumps(dict(sorted(identity_input.items())), ensure_ascii=False, separators=(",", ":"))
                for character, escaped in (("&", "\\u0026"), ("<", "\\u003c"), (">", "\\u003e"), ("\u2028", "\\u2028")):
                    identity_json = identity_json.replace(character, escaped)
                document["id"] = self.digest(identity_json.encode()).removeprefix("sha256:")
                document["os"] = "windows" if change == "v1-os" and not top else "linux"
                if top and change == "v1-id":
                    document["id"] = "c" * 64
                if top and change == "v1-parent":
                    document["parent"] = "c" * 64
                if top and change == "v1-cycle":
                    document["parent"] = document["id"]
                previous = document["id"]
                saved = {"id": document["id"]}
                if "parent" in document:
                    saved["parent"] = document["parent"]
                saved |= {key: value for key, value in document.items() if key not in ("id", "parent")}
                encoded = json.dumps(saved, ensure_ascii=False, separators=(",", ":"))
                for character, escaped in (("&", "\\u0026"), ("<", "\\u003c"), (">", "\\u003e"), ("\u2028", "\\u2028")):
                    encoded = encoded.replace(character, escaped)
                encoded = encoded.encode()
                auxiliaries.append(path(self.digest(encoded)))
                members[auxiliaries[-1]] = encoded
            legacy[0]["LayerSources"] = dict(zip(config["rootfs"]["diff_ids"], json.loads(self.encoded(layers))))
            if change == "v1-layer-sources":
                legacy[0]["LayerSources"][config["rootfs"]["diff_ids"][0]]["size"] += 1
            members["manifest.json"] = self.encoded(legacy)
            repository = {self.image.split(":")[0]: {SHA: config["rootfs"]["diff_ids"][-1].removeprefix("sha256:")}}
            if change == "v1-repository":
                repository[self.image.split(":")[0]][SHA] = "c" * 64
            if change == "v1-repository-tag":
                repository[self.image.split(":")[0]]["c" * 40] = repository[self.image.split(":")[0]].pop(SHA)
            members["repositories"] = self.encoded(repository)
            if change == "v1-missing":
                del members[auxiliaries[0]]
            if change == "v1-extra":
                members[path(self.digest(b"unrelated metadata"))] = b"unrelated metadata"
        if change == "duplicate-json":
            members["index.json"] = members["index.json"].replace(b'"schemaVersion":2', b'"schemaVersion":2,"schemaVersion":2')
        if change == "layer-bytes":
            members[path(self.digest(layer_bytes[0]))] += b"changed"
        if change == "config-bytes":
            members[path(self.producer)] += b"changed"
        if change == "missing":
            del members[path(self.digest(layer_bytes[0]))]
        if change == "extra-blob":
            members[path(self.digest(b"extra"))] = b"extra"
        if change == "unsafe":
            members["../outside"] = b"must not extract"
        with tarfile.open(self.archive, "w:gz") as archive:
            if change in ("pax", "global-pax", "gnu-longname", "gnu-longlink", "gnu-sparse"):
                entry = tarfile.TarInfo("hidden-extension")
                entry.type = {"pax": tarfile.XHDTYPE, "global-pax": tarfile.XGLTYPE,
                              "gnu-longname": tarfile.GNUTYPE_LONGNAME,
                              "gnu-longlink": tarfile.GNUTYPE_LONGLINK,
                              "gnu-sparse": tarfile.GNUTYPE_SPARSE}[change]
                if change in ("pax", "global-pax"):
                    value = "comment=" + self.marker + "\n"
                    length = len(value) + 2
                    while len(f"{length} {value}".encode()) != length:
                        length = len(f"{length} {value}".encode())
                    payload = f"{length} {value}".encode()
                else:
                    payload = b"oci-layout\0"
                entry.size = len(payload)
                archive.addfile(entry, io.BytesIO(payload))
            for name, content in members.items():
                entry = tarfile.TarInfo(name)
                entry.size = len(content)
                archive.addfile(entry, io.BytesIO(content))
            if change == "duplicate-member":
                entry = tarfile.TarInfo("index.json")
                entry.size = len(members["index.json"])
                archive.addfile(entry, io.BytesIO(members["index.json"]))
            if change == "symlink":
                entry = tarfile.TarInfo("redirect")
                entry.type, entry.linkname = tarfile.SYMTYPE, "../outside"
                archive.addfile(entry)
        if change == "trailing-graph":
            with gzip.open(self.archive, "ab") as output:
                with tarfile.open(fileobj=output, mode="w|") as archive:
                    entry = tarfile.TarInfo("blobs/sha256/" + "f" * 64)
                    entry.size = 5
                    archive.addfile(entry, io.BytesIO(b"extra"))
        if change == "truncated-gzip":
            self.archive.write_bytes(self.archive.read_bytes()[:-16])
        if change == "invalid-crc":
            content = bytearray(self.archive.read_bytes())
            content[-8] ^= 1
            self.archive.write_bytes(content)
        if change == "trailing-garbage":
            with self.archive.open("ab") as output:
                output.write(b"unexpected nonzero archive tail")
        if change == "trailing-gzip-garbage":
            with gzip.open(self.archive, "ab") as output:
                output.write(b"unexpected nonzero decompressed tail")
        self.metadata = {"Id": self.engine, "Descriptor": {"digest": self.engine, "mediaType": manifest["mediaType"], "size": len(manifest_bytes)},
                         "RepoTags": [self.image], "Os": config["os"], "Architecture": config["architecture"],
                         "RootFS": {"Type": "layers", "Layers": config["rootfs"]["diff_ids"]},
                         "Config": {"Env": [self.marker], "Cmd": [self.marker]}}
        self.metadata_path = self.root / "loaded-metadata.json"
        self.metadata_path.write_text(json.dumps(self.metadata))
        for name, value in (("source-sha", SHA), ("runtime-image-name", self.image), ("runtime-image-id", self.producer)):
            (self.artifact / name).write_text(value + "\n")

    def environment(self):
        return os.environ | {"PATH": str(self.bin) + os.pathsep + os.environ["PATH"], "TEST_METADATA": str(self.metadata_path),
                             "TEST_ENGINE_ID": self.engine, "TEST_CONTAINER_ENGINE_ID": self.engine, "TEST_IMAGE_NAME": self.image}

    def invoke(self, mode="restore", **changes):
        values = {"archive": str(self.archive), "source-sha": SHA, "image-name": self.image,
                  "producer-id": self.producer, "engine-id": self.engine, "binding": str(self.proof),
                  "dotnet-stamp": str(self.dotnet_stamp)} | changes
        arguments = [sys.executable, str(self.helper), mode]
        for name, value in values.items():
            arguments += ["--" + name, value]
        return subprocess.run(arguments, capture_output=True, text=True, env=self.environment())

    def assert_denied(self, result):
        self.assertNotEqual(0, result.returncode)
        self.assertNotIn(self.marker, result.stdout + result.stderr)

    def test_valid_single_manifest_is_bound_and_reverified_without_config_content(self):
        result = self.invoke()
        self.assertEqual(0, result.returncode, result.stderr)
        proof = json.loads(self.proof.read_text())
        self.assertEqual(self.producer, proof["producerImageId"])
        self.assertEqual(self.engine, proof["engineImageId"])
        self.assertNotEqual(self.producer, self.engine)
        self.assertNotIn(self.marker, self.proof.read_text() + result.stdout + result.stderr)
        self.assertEqual(0, self.invoke("verify").returncode)

    def test_archive_mutants_are_rejected_without_extracting_or_logging_content(self):
        for change in ("platform", "index-platform", "order", "size", "compression", "multi", "index-size", "legacy-tag",
                       "legacy-order", "duplicate-json", "layer-bytes", "config-bytes", "missing", "extra-blob", "unsafe",
                       "duplicate-member", "symlink", "trailing-graph", "truncated-gzip", "invalid-crc",
                       "trailing-garbage", "trailing-gzip-garbage", "pax", "global-pax", "gnu-longname",
                       "gnu-longlink", "gnu-sparse"):
            with self.subTest(change=change):
                self.build_archive(change)
                self.assert_denied(self.invoke())
                self.assertFalse((self.root / "outside").exists())

    def test_special_or_oversized_headers_are_rejected_before_payload_processing(self):
        verifier = runpy.run_path(str(self.helper))

        class PayloadForbidden:
            def read(self, *_args):
                raise AssertionError("Unsupported header payload must not be read.")

        class Archive:
            fileobj = PayloadForbidden()

        for kind, size in ((tarfile.XHDTYPE, 1024**3), (tarfile.XGLTYPE, 1024**3),
                           (tarfile.GNUTYPE_LONGNAME, 1024**3), (tarfile.GNUTYPE_LONGLINK, 1024**3),
                           (tarfile.GNUTYPE_SPARSE, 1024**3), (tarfile.REGTYPE, 8 * 1024**3 + 1),
                           (tarfile.DIRTYPE, 1)):
            with self.subTest(kind=kind, size=size):
                entry = verifier["StrictTarInfo"]("unsupported-header")
                entry.type, entry.size = kind, size
                with self.assertRaises(verifier["IdentityError"]):
                    entry._proc_member(Archive())

    def test_producer_source_tag_and_engine_mismatch_fail(self):
        for changes in ({"source-sha": "c" * 40}, {"image-name": "wrong:" + SHA},
                        {"producer-id": "sha256:" + "c" * 64}, {"engine-id": "sha256:" + "c" * 64}):
            with self.subTest(changes=changes):
                self.assert_denied(self.invoke(**changes))

    def test_legacy_auxiliaries_are_bound_to_layers_config_and_repository(self):
        self.build_archive("v1-valid")
        result = self.invoke()
        self.assertEqual(0, result.returncode, result.stderr)
        proof = json.loads(self.proof.read_text())
        self.assertEqual(3, len(proof["legacyAuxiliaries"]))
        self.assertNotIn(self.marker, self.proof.read_text() + result.stdout + result.stderr)
        self.assertEqual(0, self.invoke("verify").returncode)

    def test_legacy_auxiliary_semantic_mutations_fail_even_with_rehashed_blobs(self):
        for change in ("v1-config", "v1-defaults", "v1-default-type", "v1-created", "v1-os", "v1-architecture",
                       "v1-extra-key", "v1-id", "v1-parent", "v1-cycle", "v1-layer-sources", "v1-repository",
                       "v1-repository-tag", "v1-missing", "v1-extra"):
            with self.subTest(change=change):
                self.build_archive(change)
                self.assert_denied(self.invoke())

    def test_loaded_descriptor_platform_and_filesystem_mismatch_fail(self):
        descriptor = self.metadata["Descriptor"]
        for field, value in (("Id", self.producer), ("Descriptor", None),
                             ("Descriptor", descriptor | {"digest": self.producer}),
                             ("Descriptor", descriptor | {"mediaType": "application/vnd.oci.image.index.v1+json"}),
                             ("Descriptor", descriptor | {"size": descriptor["size"] + 1}),
                             ("Os", "windows"), ("Architecture", "arm64"), ("Variant", "unsupported"),
                             ("RepoTags", ["other:" + SHA]), ("RootFS", {"Type": "layers", "Layers": []}),
                             ("RootFS", {"Type": "layers", "Layers": self.metadata["RootFS"]["Layers"][::-1]})):
            with self.subTest(field=field):
                self.metadata_path.write_text(json.dumps(self.metadata | {field: value}))
                self.assert_denied(self.invoke())

    def test_missing_altered_and_type_changed_binding_fail(self):
        self.assert_denied(self.invoke("verify"))
        self.assertEqual(0, self.invoke().returncode)
        proof = json.loads(self.proof.read_text())
        for change in ({"schemaVersion": True}, {"sourceSha": "c" * 40}, {"producerImageId": self.engine}, {"engineImageId": self.producer}):
            with self.subTest(change=change):
                self.proof.write_text(json.dumps(proof | change))
                self.assert_denied(self.invoke("verify"))

    def test_standalone_verifier_rechecks_source_and_dotnet_stamps(self):
        self.assertEqual(0, self.invoke().returncode)
        for path in (self.artifact / "source-sha", self.artifact / "runtime-image-name",
                     self.artifact / "runtime-image-id", self.dotnet_stamp):
            with self.subTest(path=path.name):
                original = path.read_bytes()
                path.write_text("c" * 40 + "\n")
                self.assert_denied(self.invoke("verify"))
                path.write_bytes(original)

    def test_redirected_proof_and_invalid_loaded_json_fail_without_content(self):
        outside = self.root / "outside-proof"
        outside.mkdir()
        redirected = self.root / "redirected-proof"
        redirected.symlink_to(outside, target_is_directory=True)
        self.assert_denied(self.invoke(binding=str(redirected / "proof.json")))
        self.assertEqual([], list(outside.iterdir()))
        self.metadata_path.write_text(self.marker)
        self.assert_denied(self.invoke())

    def restore(self, **changes):
        for name, content in {"source-sha": SHA, "runtime-image-name": self.image, "runtime-image-id": self.producer}.items():
            (self.artifact / name).write_text(changes.get(name, content) + "\n")
        with tarfile.open(self.artifact / "dotnet-release-build.tar", "w") as archive:
            content = (changes.get("dotnet-sha", SHA) + "\n").encode()
            entry = tarfile.TarInfo("artifacts/ci/dotnet-build-sha")
            entry.size = len(content)
            archive.addfile(entry, io.BytesIO(content))
        exported = self.root / "exported-env"
        result = subprocess.run(["bash", str(ROOT / "scripts/ci/restore-main-build-artifacts.sh"), str(self.artifact), SHA, str(self.workspace)],
                                capture_output=True, text=True, env=self.environment() | {"GITHUB_EVENT_NAME": "pull_request", "GITHUB_ENV": str(exported)})
        return result, dict(line.split("=", 1) for line in exported.read_text().splitlines()) if exported.exists() else {}

    def test_canonical_restore_preserves_producer_and_exports_distinct_binding(self):
        result, exported = self.restore()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(self.producer, exported["COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID"])
        self.assertEqual(self.engine, exported["COGLATAS_REUSE_PREBUILT_APP_ENGINE_ID"])
        self.assertTrue(Path(exported["COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING"]).is_file())
        self.assertEqual(self.producer, (self.artifact / "runtime-image-id").read_text().strip())

    def test_canonical_restore_rejects_wrong_stamps_before_export(self):
        for change in ({"source-sha": "c" * 40}, {"dotnet-sha": "c" * 40}, {"runtime-image-name": "coglatas-main-runtime:" + SHA}):
            with self.subTest(change=change):
                result, exported = self.restore(**change)
                self.assert_denied(result)
                self.assertEqual({}, exported)

    def acceptance(self, exported, **environment_changes):
        command = 'set -euo pipefail; compose=(docker compose); source "$1"; acceptance_prepare_image_reuse; acceptance_verify_image_reuse container; '
        command += 'for lane in mvp-a-authz mbj01 mbj02 mbj03; do acceptance_record_image_reuse "artifacts/acceptance-image-reuse/$lane-image-reuse.json"; done'
        return subprocess.run(["bash", "-c", command, "test", str(ROOT / "scripts/ci/acceptance-image-reuse.sh")], cwd=self.workspace,
                              capture_output=True, text=True, env=self.environment() | exported | environment_changes)

    def test_downstream_continuity_and_four_receipts_keep_both_identities(self):
        result, exported = self.restore()
        self.assertEqual(0, result.returncode, result.stderr)
        result = self.acceptance(exported)
        self.assertEqual(0, result.returncode, result.stderr)
        for receipt in (self.workspace / "artifacts/acceptance-image-reuse").iterdir():
            value = json.loads(receipt.read_text())
            self.assertEqual(2, value["schemaVersion"])
            self.assertEqual(self.producer, value["expectedProducerImageId"])
            self.assertEqual(self.engine, value["engineImageId"])
        result = subprocess.run([sys.executable, str(ROOT / "scripts/ci/verify-acceptance-image-reuse.py")], cwd=self.workspace,
                                capture_output=True, text=True, env=self.environment() | exported)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_downstream_rejects_retag_wrong_container_and_absent_proof(self):
        result, exported = self.restore()
        self.assertEqual(0, result.returncode, result.stderr)
        for changes in ({"TEST_ENGINE_ID": self.producer}, {"TEST_CONTAINER_ENGINE_ID": self.producer},
                        {"COGLATAS_REUSE_PREBUILT_APP_IMAGE_BINDING": str(self.root / "absent.json")}):
            with self.subTest(changes=changes):
                self.assert_denied(self.acceptance(exported, **changes))

    def test_downstream_receipt_rejects_changed_engine_or_binding_digest(self):
        result, exported = self.restore()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(0, self.acceptance(exported).returncode)
        path = self.workspace / "artifacts/acceptance-image-reuse/mvp-a-authz-image-reuse.json"
        original = json.loads(path.read_text())
        for change in ({"engineImageId": self.producer}, {"bindingSha256": "c" * 64}):
            with self.subTest(change=change):
                path.write_text(json.dumps(original | change))
                result = subprocess.run([sys.executable, str(ROOT / "scripts/ci/verify-acceptance-image-reuse.py")], cwd=self.workspace,
                                        capture_output=True, text=True, env=self.environment() | exported)
                self.assert_denied(result)

    def test_functional_harness_accepts_only_reverified_distinct_engine_binding(self):
        result, exported = self.restore()
        self.assertEqual(0, result.returncode, result.stderr)
        result, _traces, records = FunctionalImageReuseTests().run_harness(
            workspace=self.workspace, changes={key: value for key, value in self.environment().items() if key != "PATH"} | exported)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual((self.producer, self.engine), (records[0]["expectedProducerImageId"], records[0]["engineImageId"]))
        self.assertEqual(hashlib.sha256(self.proof.read_bytes()).hexdigest(), records[0]["bindingSha256"])


if __name__ == "__main__":
    unittest.main()
