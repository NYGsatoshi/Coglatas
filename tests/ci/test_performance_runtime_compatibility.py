from __future__ import annotations

import copy
import importlib.util
import contextlib
import io
import subprocess
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

import test_performance_comparator as base

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/performance'))
spec = importlib.util.spec_from_file_location('collect_environment', ROOT / 'scripts/performance/collect-environment.py')
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)


class BrowserFingerprintProbeTests(unittest.TestCase):
    def test_all_browser_version_probes_refuse_downloads_and_retain_sixty_second_timeout(self):
        responses = [subprocess.CompletedProcess([], 0, value, "")
                     for value in ("v24.0.0\n", "11.0.0\n", "Chromium synthetic\n")]
        with patch.object(collector.subprocess, "run", side_effect=responses) as invoked:
            actual = collector.browser_versions("coglatas-performance-control", Path("base.yml"), Path("override.yml"))
        self.assertEqual(("v24.0.0", "11.0.0", "Chromium synthetic"), actual)
        self.assertEqual(3, invoked.call_count)
        for call in invoked.call_args_list:
            command = call.args[0]
            self.assertEqual(["docker", "compose", "-p", "coglatas-performance-control",
                              "-f", "base.yml", "-f", "override.yml", "run", "--rm", "--no-deps",
                              "--pull", "never", "performance-browser"], command[:14])
            self.assertEqual(60.0, call.kwargs["timeout"])
            self.assertTrue(call.kwargs["check"])
        self.assertEqual(["node", "--version"], invoked.call_args_list[0].args[0][-2:])
        self.assertEqual(["npm", "--version"], invoked.call_args_list[1].args[0][-2:])

    def test_absent_image_failed_probe_and_timeout_remain_fatal(self):
        for failure in (subprocess.CalledProcessError(1, ["docker"]),
                        subprocess.TimeoutExpired(["docker"], 60), FileNotFoundError("docker")):
            with self.subTest(failure=type(failure).__name__), patch.object(
                    collector.subprocess, "run", side_effect=failure) as invoked:
                with self.assertRaises(collector.PerformanceContractError):
                    collector.browser_versions("coglatas-performance-control", Path("base.yml"), None)
                self.assertEqual(1, invoked.call_count)
                self.assertIn("never", invoked.call_args.args[0])

    def test_probe_failure_returns_nonzero_without_fingerprint_evidence(self):
        arguments = ["collect-environment.py", "--compose-project", "coglatas-performance-control",
                     "--compose-file", "base.yml", "--profile", "small", "--fixture-evidence", "fixture.json",
                     "--output", "environment.json"]
        with patch.object(sys, "argv", arguments), patch.object(collector, "load_json", return_value={}), \
                patch.object(collector, "validate_fixture_evidence", return_value={}), \
                patch.object(collector, "run", return_value="synthetic"), \
                patch.object(collector, "browser_versions", side_effect=collector.PerformanceContractError("probe failed")), \
                patch.object(collector, "write_json_atomic") as write, contextlib.redirect_stderr(io.StringIO()) as error:
            self.assertEqual(2, collector.main())
        write.assert_not_called()
        self.assertIn("environment fingerprint failed", error.getvalue())


class RuntimeCompatibilityTests(unittest.TestCase):
    def setUp(self):
        self.image = {'RootFS': {'Layers': ['sha256:' + character * 64 for character in 'abc']},
                      'Config': {'Env': ['PORT=8080'], 'Entrypoint': ['dotnet', 'Coglatas.Web.dll']},
                      'Os': 'linux', 'Architecture': 'amd64'}
        self.dockerfile = (ROOT / 'Dockerfile').read_text(encoding='utf-8')
        self.packages = 'libssl3t64\t3.0.1\nlibc6\t2.39'

    def fingerprint(self):
        value = base.fingerprint()
        value['applicationRuntime'] = collector.production_runtime_identity(self.image, self.dockerfile, self.packages)
        return value

    def test_source_layer_changes_preserve_runtime_compatibility_and_full_source_identity(self):
        before = self.fingerprint()
        self.image['RootFS']['Layers'][-1] = 'sha256:' + 'd' * 64
        after = self.fingerprint()
        after['containerImages']['app'] = 'sha256:new-source-image'
        after['commitSha'] = base.BASE
        self.assertNotEqual(before['containerImages']['app'], after['containerImages']['app'])
        self.assertEqual(base.compare.environment_compatibility_key(before), base.compare.environment_compatibility_key(after))

    def test_runtime_layer_config_recipe_resource_and_fixture_changes_are_incompatible(self):
        before = self.fingerprint()
        for mutation in ('package', 'config', 'recipe', 'vcpu', 'fixture', 'database'):
            after = copy.deepcopy(before)
            if mutation == 'package':
                after['applicationRuntime'] = collector.production_runtime_identity(self.image, self.dockerfile, self.packages + '\nextra\t1.0')
            elif mutation == 'config':
                image = copy.deepcopy(self.image)
                image['Config']['Env'] = ['PORT=9090']
                after['applicationRuntime'] = collector.production_runtime_identity(image, self.dockerfile, self.packages)
            elif mutation == 'recipe':
                after['applicationRuntime'] = collector.production_runtime_identity(self.image, self.dockerfile.replace('libssl3t64', 'libssl3t64-extra'), self.packages)
            elif mutation == 'vcpu':
                after['runner']['cpuCount'] = 2
            elif mutation == 'fixture':
                after['fixture']['version'] = 2
            else:
                after['postgresql']['version'] = 'PostgreSQL 19'
            self.assertNotEqual(base.compare.environment_compatibility_key(before), base.compare.environment_compatibility_key(after), mutation)

    def test_legacy_fingerprints_stay_strict_and_do_not_compare_to_new_runtime_identity(self):
        before = base.fingerprint()
        after = copy.deepcopy(before)
        after['containerImages']['app'] = 'different-app'
        self.assertNotEqual(base.compare.environment_compatibility_key(before), base.compare.environment_compatibility_key(after))
        self.assertNotEqual(base.compare.environment_compatibility_key(before), base.compare.environment_compatibility_key(self.fingerprint()))

    def test_missing_image_malformed_runtime_and_unknown_filesystem_boundary_fail(self):
        for runtime in ({}, {'schemaVersion': 1, 'mode': 'production', 'packageHash': 'x', 'configHash': 'y'}):
            value = self.fingerprint()
            value['applicationRuntime'] = runtime
            with self.assertRaises(base.compare.ComparatorError):
                base.compare.environment_compatibility_key(value)
        value = self.fingerprint()
        del value['containerImages']['app']
        with self.assertRaises(base.compare.ComparatorError):
            base.compare.environment_compatibility_key(value)
        for dockerfile in (self.dockerfile + '\nRUN touch /runtime-change\n', self.dockerfile.replace('COPY --from=build /app/publish .', 'COPY elsewhere .')):
            with self.assertRaises(collector.PerformanceContractError):
                collector.production_runtime_identity(self.image, dockerfile, self.packages)

    def test_main_prebuilt_and_full_production_recipes_have_the_same_runtime_boundary(self):
        prebuilt = (ROOT / 'infra/docker/runtime-prebuilt.Dockerfile').read_text(encoding='utf-8')
        self.assertEqual(collector.production_runtime_identity(self.image, self.dockerfile, self.packages),
                         collector.production_runtime_identity(self.image, prebuilt, self.packages))

    def test_baseline_preparation_remains_main_only_while_duration_assurance_is_suspended(self):
        capture = (ROOT / '.github/workflows/performance-db-baseline-capture.yml').read_text()
        gate = (ROOT / '.github/workflows/performance-db.yml').read_text()
        self.assertIn('test "$GITHUB_REF" = refs/heads/main', capture)
        self.assertIn('scripts/performance/db-ci.py resolve-build', capture)
        self.assertIn('ref: ${{ steps.declaration.outputs.source_sha }}', capture)
        self.assertIn('db_campaign.py prepare', capture)
        self.assertIn('db_campaign.py capture --source measured-source', capture)
        self.assertIn('db_campaign.py select', capture)
        self.assertIn('This collection does not grant duration acceptance.', capture)
        self.assertNotIn('continue-on-error', capture)
        # A captured candidate does not imply product duration acceptance.
        # The ordinary gate keeps the live structural comparator but intentionally
        # does not perform numerical duration comparison during suspension (#1128).
        self.assertIn('python3 scripts/performance/db-compare.py', gate)
        self.assertIn('assurance_policy.py receipt --suite db', gate)
        self.assertNotIn('args+=(--duration', gate)

    def test_same_head_unapproved_and_high_variance_baselines_remain_rejected(self):
        current = self.fingerprint()
        baseline = base.approved_baseline([100, 101, 102, 103, 104])
        baseline['environmentCompatibilityKey'] = base.compare.environment_compatibility_key(current)
        for mutation, decision in (('head', 'invalid'), ('approval', 'invalid'), ('variance', 'unstable')):
            document = copy.deepcopy(baseline)
            if mutation == 'head':
                document['baselineSha'] = base.HEAD
            elif mutation == 'approval':
                document['approved'] = False
            else:
                document['samples'] = [1, 10, 100, 1000, 10000]
            result = base.compare.compare_documents(base.measurement([100, 101, 102, 103, 104]), document, current,
                                                    base.scenarios(), base.budgets(), base.environment(), base.policy())
            self.assertEqual(decision, result['decision'], mutation)


if __name__ == '__main__':
    unittest.main()
