#!/usr/bin/env python3
"""Exercise the artifact restore boundary; hosted collectors verify the real runtime."""
from __future__ import annotations

import gzip
import io
import json
import os
import shlex
import subprocess
import sys
import tarfile
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SHA = "a" * 40
IMAGE_ID = "sha256:" + "b" * 64

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
                    image="coglatas-main-runtime:" + SHA, configured=None, helper_command=None):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            commands = root / "bin"
            commands.mkdir()
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
            environment = os.environ | {
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
            receipt_path = root / "test-results" / (self.wrappers[lane][3] + "-image-reuse.json")
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
                command += ('if acceptance_record_image_reuse test-results/mvp-a-authz-image-reuse.json; '
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
        self.assertEqual({"test-results/" + item[3] + "-image-reuse.json" for item in self.wrappers.values()},
                         {line.strip() for line in paths.splitlines()})
        self.assertIn("if-no-files-found: ignore", upload)
        self.assertIn("retention-days: 7", upload)
        contract = (ROOT / ".github/workflows/performance-contract.yml").read_text()
        triggers = contract.split("  pull_request:\n    paths:\n", 1)[1].split("\nconcurrency:", 1)[0]
        for path in (".github/workflows/licensed-real-backend-acceptance.yml",
                     "scripts/ci/acceptance-image-reuse.sh", "scripts/ci/restore-main-build-artifacts.sh",
                     "scripts/ci/test-main-runtime-artifact.py",
                     *("scripts/ci/" + wrapper[0] for wrapper in self.wrappers.values())):
            self.assertIn('      - "' + path + '"', triggers)
        self.assertIn("python3 scripts/ci/test-main-runtime-artifact.py", contract)

    def test_failed_probe_keeps_failure_status_without_a_reuse_receipt(self):
        for lane in self.wrappers:
            with self.subTest(lane=lane):
                result, traces, receipt = self.run_harness(lane, scenario="failed-probe")
                self.assert_stopped(result, traces, receipt)
                self.assertEqual(22, result.returncode)


if __name__ == "__main__":
    unittest.main()
