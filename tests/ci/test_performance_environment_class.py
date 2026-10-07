import contextlib
import copy
import json
import importlib.util
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import test_performance_comparator as base
from test_performance_db_campaign import ROOT, group, identity, manifest, rehash
from db_class_baselines import qualify
from environment_class import EnvironmentClassError, compatibility, environment_class, hardware_fingerprint, enrich_live_legacy_fingerprint


class EnvironmentClassTests(unittest.TestCase):
    def test_same_environment_class_different_cpu_is_compatible_and_preserves_evidence(self):
        before = base.fingerprint()
        before['runner']['cpuModel'] = 'AMD EPYC 9V45 96-Core Processor'
        after = copy.deepcopy(before)
        after['runner']['cpuModel'] = 'AMD EPYC 9V74 80-Core Processor'
        original = copy.deepcopy(after)
        self.assertEqual(base.compare.environment_compatibility_key(before), base.compare.environment_compatibility_key(after))
        self.assertEqual('HARDWARE_VARIANT', compatibility(environment_class(after), environment_class(before),
                                                         hardware_fingerprint(after), hardware_fingerprint(before)))
        self.assertEqual(original, after)
        self.assertEqual('AMD EPYC 9V74 80-Core Processor', hardware_fingerprint(after)['cpuModel'])

    def test_architecture_os_resources_provider_runtime_and_compiler_mismatches_are_incompatible(self):
        before = base.fingerprint()
        mutations = [('architecture', 'aarch64'), ('osVersionClass', '22.04'), ('cpuCount', 2),
                     ('memoryBytes', 16 * 1024**3), ('provider', 'self-hosted'), ('runnerClass', 'larger')]
        for field, value in mutations:
            with self.subTest(field=field):
                after = copy.deepcopy(before)
                after['runner'][field] = value
                self.assertNotEqual(base.compare.environment_compatibility_key(before), base.compare.environment_compatibility_key(after))
        for section, field, value in [('dotnet', 'runtimeInfo', '.NET 11 runtime'), ('dotnet', 'sdkInfo', '.NET SDK 11')]:
            after = copy.deepcopy(before)
            after[section][field] = value
            self.assertNotEqual(base.compare.environment_compatibility_key(before), base.compare.environment_compatibility_key(after))

    def test_benchmark_schema_mismatch_and_forged_class_fail_closed(self):
        before = base.fingerprint()
        original_class = environment_class(before)
        other = copy.deepcopy(original_class)
        other['benchmarkSchema']['result'] = 2
        self.assertEqual('INCOMPATIBLE', compatibility(other, original_class, hardware_fingerprint(before), hardware_fingerprint(before)))
        before['environmentClass'] = other
        with self.assertRaises(base.compare.ComparatorError):
            base.compare.environment_compatibility_key(before)

    def test_missing_provider_architecture_and_memory_cannot_be_inferred_for_active_execution(self):
        for field in ('provider', 'architecture', 'memoryBytes'):
            value = base.fingerprint()
            value['runner'].pop(field)
            with self.subTest(field=field), self.assertRaises((EnvironmentClassError, KeyError)):
                environment_class(value)

    def test_kernel_microcode_and_cpu_sku_are_evidence_without_hard_rejection(self):
        before = base.fingerprint()
        after = copy.deepcopy(before)
        after['runner'].update(cpuModel='Other SKU', microcode='different', os='Linux other kernel')
        self.assertEqual(base.compare.environment_compatibility_key(before), base.compare.environment_compatibility_key(after))
        evidence = hardware_fingerprint(after)
        self.assertEqual('different', evidence['microcode'])
        self.assertEqual('Linux other kernel', evidence['kernel'])

    def test_old_api_collector_is_bridged_only_from_same_live_execution_with_original_preserved(self):
        value = base.fingerprint()
        attrs = {key:value['runner'].pop(key) for key in ('provider','runnerClass','architecture','osFamily','osVersionClass')}
        original = copy.deepcopy(value)
        with patch('environment_class.platform.platform',return_value=value['runner']['os']), patch('environment_class.os.cpu_count',return_value=4), \
             patch('environment_class.live_runner_class_attributes',return_value=attrs), patch.dict('os.environ',{'GITHUB_SHA':value['commitSha']}):
            normalized, retained = enrich_live_legacy_fingerprint(value)
        self.assertEqual(original, retained)
        self.assertEqual(original, value)
        self.assertEqual('github-hosted', normalized['environmentClass']['provider'])
        self.assertEqual(original['runner']['cpuModel'], normalized['runner']['cpuModel'])
        with patch('environment_class.platform.platform',return_value='other host'),self.assertRaises(EnvironmentClassError):
            enrich_live_legacy_fingerprint(value)

    def test_requalification_preserves_historical_unavailable_result_and_selects_first_group(self):
        value = manifest()
        declaration = identity(value)
        groups = [group(value, ordinal) for ordinal in (1,2,3)]
        target = copy.deepcopy(groups[0]['fingerprints']['medium'])
        for raw in groups:
            raw['fingerprints']['medium']['runner']['cpuModel'] = 'AMD EPYC 9V74 80-Core Processor'
            rehash(raw)
        from db_campaign import select_campaign
        original = select_campaign(value, declaration, groups, ROOT)
        self.assertEqual('BASELINE_UNAVAILABLE', original['decision'])
        documents = {'manifest.json': value, 'declaration.json': declaration,
                     'raw-groups.json': {'groups':groups}, 'campaign-result.json':original}
        retained = copy.deepcopy(documents)
        with patch('db_class_baselines.source_snapshot', lambda root, sha: contextlib.nullcontext(ROOT)):
            result = qualify(ROOT, documents, target)
        self.assertEqual('BASELINE_CANDIDATE', result['decision'])
        self.assertEqual(1, result['selectedGroupOrdinal'])
        self.assertFalse(result['approved'])
        self.assertEqual('BASELINE_UNAVAILABLE', result['originalDecision'])
        self.assertTrue(all(d['environmentCompatibility'] == 'HARDWARE_VARIANT' for d in result['groups']))
        self.assertEqual(retained, documents)

    def test_class_change_does_not_rescue_unstable_samples_or_structural_failure(self):
        value = manifest()
        declaration = identity(value)
        groups = [group(value, ordinal, unstable=True) for ordinal in (1,2,3)]
        target = copy.deepcopy(groups[0]['fingerprints']['medium'])
        for raw in groups:
            raw['fingerprints']['medium']['runner']['cpuModel'] = 'Other CPU'
            rehash(raw)
        from db_campaign import select_campaign
        original = select_campaign(value, declaration, groups, ROOT)
        documents = {'manifest.json':value, 'declaration.json':declaration,'raw-groups.json':{'groups':groups},'campaign-result.json':original}
        with patch('db_class_baselines.source_snapshot', lambda root, sha: contextlib.nullcontext(ROOT)):
            result = qualify(ROOT, documents, target)
        self.assertEqual('BASELINE_UNAVAILABLE', result['decision'])
        self.assertIsNone(result['selectedGroupOrdinal'])

    def test_class_selector_requires_one_complete_approved_unmixed_enrollment(self):
        from common import PerformanceContractError
        from environment_class import digest, POLICY_VERSION
        spec = importlib.util.spec_from_file_location('class_selector_test', ROOT / 'scripts/performance/db-compare.py')
        adapter = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(adapter)
        fp = base.fingerprint()
        value = environment_class(fp)
        key = digest(value)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            baselines = root / 'performance/baselines/db'
            directory = baselines / 'environment-class/medium' / key
            directory.mkdir(parents=True)
            contract = json.loads((ROOT / 'performance/db-scenarios.json').read_text())
            (root / 'performance/db-scenarios.json').write_text(json.dumps(contract))
            record = {'campaignId':'test-campaign', 'baselineDirectory': f'performance/baselines/db/environment-class/medium/{key}',
                      'artifact':{'digest':'sha256:'+'a'*64}, 'qualificationSha256':'b'*64}
            ledger = root / 'performance/environment-class-baselines.json'
            ledger.write_text(json.dumps({'enrollments':[record]}))
            for scenario in contract['scenarios']:
                document = {'scenario':scenario['id'],'approved':True,'environmentCompatibilityDigest':key,'environmentClass':value,
                            'hardwareFingerprint':hardware_fingerprint(fp),'baselineSha':base.BASE,'fixtureHash':base.FIXTURE_HASH,'fixtureVersion':1,
                            'provenance':{'policyVersion':POLICY_VERSION,'profile':'medium','campaignId':'test-campaign','artifactDigest':record['artifact']['digest'],
                                          'qualificationSha256':'b'*64,'artifactId':123,'rawGroupsSha256':'c'*64,'selectedGroupOrdinal':1,
                                          'workflowRunId':456,'workflowRunAttempt':1}}
                (directory / (scenario['id']+'.json')).write_text(json.dumps(document))
            self.assertEqual(9,len(adapter.duration_baselines('medium',key,root,baselines)))
            selected = directory / 'workspace.list.json'
            original = selected.read_text()
            for mutation in ('unapproved','mixed-hardware','altered-qualification'):
                document=json.loads(original)
                if mutation=='unapproved': document['approved']=False
                elif mutation=='mixed-hardware': document['hardwareFingerprint']['cpuModel']='Mixed hardware'
                else: document['provenance']['qualificationSha256']='d'*64
                selected.write_text(json.dumps(document))
                with self.subTest(mutation=mutation),self.assertRaises(PerformanceContractError):
                    adapter.duration_baselines('medium',key,root,baselines)
            selected.write_text(original)
            ledger.write_text(json.dumps({'enrollments':[]}))
            with self.assertRaises(PerformanceContractError):
                adapter.duration_baselines('medium',key,root,baselines)


if __name__ == '__main__':
    unittest.main()
