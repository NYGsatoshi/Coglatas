from __future__ import annotations

import copy
import importlib.util
import sys
import unittest
from pathlib import Path

import test_performance_comparator as base

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/performance'))
spec = importlib.util.spec_from_file_location('collect_environment', ROOT / 'scripts/performance/collect-environment.py')
collector = importlib.util.module_from_spec(spec)
spec.loader.exec_module(collector)


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

    def test_runtime_layer_config_recipe_cpu_and_fixture_changes_are_incompatible(self):
        before = self.fingerprint()
        for mutation in ('package', 'config', 'recipe', 'cpu', 'fixture', 'database'):
            after = copy.deepcopy(before)
            if mutation == 'package':
                after['applicationRuntime'] = collector.production_runtime_identity(self.image, self.dockerfile, self.packages + '\nextra\t1.0')
            elif mutation == 'config':
                image = copy.deepcopy(self.image)
                image['Config']['Env'] = ['PORT=9090']
                after['applicationRuntime'] = collector.production_runtime_identity(image, self.dockerfile, self.packages)
            elif mutation == 'recipe':
                after['applicationRuntime'] = collector.production_runtime_identity(self.image, self.dockerfile.replace('libssl3t64', 'libssl3t64-extra'), self.packages)
            elif mutation == 'cpu':
                after['runner']['cpuModel'] = 'Different CPU'
            elif mutation == 'fixture':
                after['fixture']['version'] = 2
            else:
                after['containerImages']['postgres'] = 'different-postgres'
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

    def test_baseline_preparation_requires_main_and_does_not_replace_duration_acceptance(self):
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
        self.assertIn('--duration --baselines performance/baselines/db', gate)

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
