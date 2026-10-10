from __future__ import annotations

import argparse
import contextlib
import hashlib
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts' / 'ci'))
import release_assurance_advisory as advisory
import release_supply_chain as release
import test_verify_sbom_vulnerability_gate as native_fixture
from release_image_fixture import ImageFixture

SHA = 'a' * 40
IMAGE = ImageFixture()
CONFIG_DIGEST = IMAGE.config_digest
MANIFEST_DIGEST = IMAGE.subject_digest
SUBJECT = f'ghcr.io/nygsatoshi/coglatas@{MANIFEST_DIGEST}'
RUN = 'https://github.com/NYGsatoshi/Coglatas/actions/runs/123/attempts/2'


class ReleaseAssuranceAdvisoryTests(unittest.TestCase):
    def setUp(self):
        self.native = native_fixture.Sec10GateTests()
        self.native.setUp()
        self.root = self.native.root
        self.out = self.root / 'advisory-new'
        self.documents = {
            'cyclonedx': self.native.sbom, 'spdx': self.root / 'sbom.spdx.json',
            'sbomMetadata': self.native.metadata,
            'grypeReport': self.root / 'grype-vulnerabilities.json',
            'grypeVersion': self.native.grype_version,
            'grypeDb': self.root / 'grype-db-status.json',
            'trivyReport': self.root / 'trivy-vulnerabilities.json',
            'trivyVersion': self.native.trivy_version,
            'dockerObservation': self.root / 'docker-image-observation.json',
            'imageGraph': self.root / 'image-graph.json',
            'vulnerabilityPolicy': self.native.policy,
        }
        self.write(self.documents['spdx'], {'spdxVersion': 'SPDX-2.3',
                                          'packages': [{'name': 'demo-lib', 'versionInfo': '1.0.0'}]})
        IMAGE.write(self.root)
        self.write(self.documents['imageGraph'], IMAGE.resolve(self.root))
        self.write(self.documents['dockerObservation'], {
            'schema': 'coglatas-docker-image-observation-v2', 'observedDockerId': MANIFEST_DIGEST,
            'repoDigests': [SUBJECT], 'archiveFile': 'image.tar',
            'platform': {'os': 'linux', 'architecture': 'amd64', 'variant': ''}})
        self.write(self.documents['grypeDb'], self.read(self.native.grype_db))
        self.write(self.documents['grypeReport'], self.read(self.native.grype))
        self.mutate('grypeReport', lambda value: value.update(source={'type': 'sbom-file',
                                                                  'target': str(self.native.sbom.resolve())}))
        self.write(self.documents['trivyReport'], self.read(self.native.trivy))
        self.mutate('trivyReport', lambda value: value.update(
            ArtifactName=advisory.TRIVY_ARCHIVE_INPUT,
            Metadata={'ImageID': CONFIG_DIGEST, 'DiffIDs': IMAGE.config['rootfs']['diff_ids']}))
        self.mutate('sbomMetadata', lambda value: value.update(
            repositoryCommit=SHA, imageOrReleaseDigest=MANIFEST_DIGEST,
            runIdentity='123/2/publish-release-image', syftVersion='1.51.0'))
        self.mutate('cyclonedx', lambda value: value.update(metadata={'component': {
            'type': 'container', 'name': SUBJECT, 'version': MANIFEST_DIGEST,
            'bom-ref': 'opaque-artifact-reference'}}))
        self.rehash_sbom()
        self.args = argparse.Namespace(
            expected_repository_sha=SHA, expected_subject=SUBJECT, expected_run_identity=RUN,
            expected_platform='linux/amd64',
            sbom_directory=str(self.root), scan_directory=str(self.root), policy=str(self.native.policy),
            out_directory=str(self.out), functional_manifest=None, tier_b_run_identity=None,
            schemathesis_metadata=[], zap_metadata=[], open_api=None, now=native_fixture.NOW,
        )

    def tearDown(self):
        self.native.tearDown()

    @staticmethod
    def read(path):
        return json.loads(path.read_text(encoding='utf-8'))

    @staticmethod
    def write(path, value):
        path.write_text(json.dumps(value), encoding='utf-8')

    def mutate(self, label, action):
        path = self.documents[label]
        document = self.read(path)
        action(document)
        self.write(path, document)

    def rehash_sbom(self):
        self.mutate('sbomMetadata', lambda value: value['formats'].update({
            'cyclonedx-json': {'file': self.native.sbom.name,
                               'sha256': hashlib.sha256(self.native.sbom.read_bytes()).hexdigest()},
            'spdx-json': {'file': self.documents['spdx'].name,
                         'sha256': hashlib.sha256(self.documents['spdx'].read_bytes()).hexdigest()},
        }))

    def run_adapter(self):
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            code = advisory.command(self.args)
        return code, self.read(self.out / 'release-assurance-advisory.json')

    def assert_error(self, code=None):
        result, report = self.run_adapter()
        self.assertEqual(result, 1)
        self.assertEqual(report['integrityStatus'], 'ERROR')
        self.assertEqual(report['releaseAcceptance'], 'BLOCKED')
        self.assertEqual(report['acceptanceQualifiedControlCount'], 0)
        if code is not None:
            self.assertEqual(report['errorCode'], code)
        return report

    def add_high(self, *, fixed=False):
        self.mutate('grypeReport', lambda value: value.update(matches=[
            native_fixture.grype_finding(advisory='GHSA-vfj7-8cjw-p6xm', package='braces',
                                         version='3.0.3', fixed=('3.0.4',) if fixed else ())]))
        self.mutate('trivyReport', lambda value: value['Results'][0].update(Vulnerabilities=[
            native_fixture.trivy_finding(advisory='GHSA-vfj7-8cjw-p6xm', package='braces',
                                         version='3.0.3', fixed='3.0.4' if fixed else '')]))

    def add_functional(self):
        path = self.root / 'functional.json'
        lanes = []
        for suite in advisory.functional_evidence.OWNERS:
            lanes.append({
                'schemaVersion': 1, 'commitSha': SHA, 'gate': 'functional-full',
                'runId': '123', 'runAttempt': '2', 'suite': suite,
                'startedAt': '2026-09-04T05:00:00Z', 'completedAt': '2026-09-04T05:01:00Z',
                'setupSeconds': 1, 'testSeconds': 20,
                'journeys': [{'journeyId': owner, 'status': 'PASS', 'attempts': 1, 'durationMs': 100}
                             for owner in advisory.functional_evidence.expected_owners(suite, 'functional-full')],
            })
        self.write(path, {'schemaVersion': 1, 'commitSha': SHA, 'gate': 'functional-full',
                          'runId': '123', 'runAttempt': '2', 'lanes': lanes})
        self.args.functional_manifest = str(path)
        return path

    def contract(self):
        path = self.root / 'openapi.json'
        self.write(path, {'openapi': '3.0.1', 'paths': {'/demo': {'get': {}}}})
        self.args.open_api = str(path)
        return hashlib.sha256(path.read_bytes()).hexdigest()

    def add_schemathesis(self, role='anonymous'):
        contract_hash = self.contract()
        path = self.root / f'schemathesis-{role}.json'
        self.write(path, {
            'contract_sha256': contract_hash, 'lane': 'deep', 'network_errors': 0,
            'operation_count': 1, 'operations': ['GET /demo'], 'request_count': 2,
            'role': role, 'scanner_exit': 0, 'schemathesis_version': '4.25.2', 'seed': 574042,
        })
        self.args.schemathesis_metadata.append(str(path))
        return path

    def add_zap(self, role='alpha-owner'):
        contract_hash = self.contract()
        path = self.root / f'zap-{role}.json'
        self.write(path, {
            'control': 'SEC-06', 'role': role, 'status': 'passed', 'scannerVersion': '2.17.0',
            'scannerImage': advisory.ZAP_IMAGE, 'openApiSha256': contract_hash,
            'automationPlanSha256': 'd' * 64, 'policySha256': 'e' * 64, 'addonListSha256': 'f' * 64,
            'forbiddenValueCount': 1, 'unsanitizedAllowed': False,
            'highAlerts': 0, 'mediumAlerts': 1, 'lowAlerts': 0, 'informationalAlerts': 0,
        })
        self.args.zap_metadata.append(str(path))
        return path

    def test_distinct_manifest_and_configuration_positive_with_exact_byte_hashes(self):
        code, report = self.run_adapter()
        self.assertEqual(code, 0)
        self.assertEqual(report['subjectDigest'], MANIFEST_DIGEST)
        self.assertEqual(report['imageConfigurationDigest'], CONFIG_DIGEST)
        self.assertNotEqual(MANIFEST_DIGEST, CONFIG_DIGEST)
        self.assertEqual(report['integrityStatus'], 'CONSISTENT')
        self.assertEqual(report['nativeVulnerabilityDecision'], 'pass')
        self.assertEqual(report['outstandingAcceptanceControlCount'], 14)
        self.assertEqual(report['mechanicallyBoundControlCount'], 2)
        self.assertEqual(report['releaseAcceptance'], 'BLOCKED')
        self.assertEqual(report['missingAdvancedAdapterCount'], 4)
        for label, path in self.documents.items():
            self.assertEqual(report['inputSha256'][label], hashlib.sha256(path.read_bytes()).hexdigest())
        for label, file_name in [('normalized', 'vulnerabilities.normalized.json'),
                                 ('nativeSummary', 'vulnerability-gate-summary.json')]:
            self.assertEqual(report['outputSha256'][label], hashlib.sha256((self.out / file_name).read_bytes()).hexdigest())

    def test_wrong_candidate(self):
        self.args.expected_repository_sha = 'b' * 40
        self.assert_error('SBOM_CANDIDATE_MISMATCH')

    def test_wrong_subject_even_with_same_configuration(self):
        self.args.expected_subject = SUBJECT.replace(MANIFEST_DIGEST[7:], 'd' * 64)
        self.assert_error('SBOM_SUBJECT_MISMATCH')

    def test_wrong_workflow_run(self):
        self.args.expected_run_identity = RUN.replace('/123/', '/124/')
        self.assert_error('SBOM_PRODUCER_RUN_MISMATCH')

    def test_wrong_workflow_attempt(self):
        self.args.expected_run_identity = RUN.replace('/2', '/3')
        self.assert_error('SBOM_PRODUCER_RUN_MISMATCH')

    def test_wrong_sbom_producer(self):
        self.mutate('sbomMetadata', lambda value: value.update(runIdentity='123/2/sign-release-subject'))
        self.assert_error('SBOM_PRODUCER_RUN_MISMATCH')

    def test_wrong_repository(self):
        self.args.expected_run_identity = 'https://github.com/Example/Coglatas/actions/runs/123/attempts/2'
        self.assert_error('REPOSITORY_MISMATCH')

    def test_mutated_cyclonedx_bytes(self):
        self.native.sbom.write_bytes(self.native.sbom.read_bytes() + b' ')
        self.assert_error('SBOM_BYTES_MISMATCH')

    def test_mutated_spdx_bytes(self):
        self.documents['spdx'].write_bytes(self.documents['spdx'].read_bytes() + b' ')
        self.assert_error('SBOM_BYTES_MISMATCH')

    def test_empty_components(self):
        self.mutate('cyclonedx', lambda value: value.update(components=[]))
        self.rehash_sbom()
        self.assert_error('SBOM_COMPONENTS_MISSING')

    def test_missing_intrinsic_container_root(self):
        self.mutate('cyclonedx', lambda value: value.pop('metadata'))
        self.rehash_sbom()
        self.assert_error('SBOM_CONTAINER_ROOT_MISSING')

    def test_wrong_intrinsic_container_name_with_rehashed_bytes(self):
        self.mutate('cyclonedx', lambda value: value['metadata']['component'].update(name='unrelated:latest'))
        self.rehash_sbom()
        self.assert_error('SBOM_CONTAINER_SUBJECT_MISMATCH')

    def test_wrong_intrinsic_container_manifest_with_rehashed_bytes(self):
        self.mutate('cyclonedx', lambda value: value['metadata']['component'].update(version='sha256:' + 'd' * 64))
        self.rehash_sbom()
        self.assert_error('SBOM_CONTAINER_SUBJECT_MISMATCH')

    def test_wrong_syft_producer_version(self):
        self.mutate('sbomMetadata', lambda value: value.update(syftVersion='1.50.0'))
        self.assert_error('SBOM_PRODUCER_VERSION_MISMATCH')

    def test_stale_sbom(self):
        self.mutate('sbomMetadata', lambda value: value.update(generationTimestampUtc='2026-09-03T05:00:00Z'))
        self.assert_error('NATIVE_VULNERABILITY_INPUT_INVALID')

    def test_missing_scanner_input(self):
        self.documents['grypeDb'].unlink()
        self.assert_error('GRYPEDB_INPUT_INVALID')

    def test_wrong_docker_manifest(self):
        self.mutate('dockerObservation', lambda value: value.update(repoDigests=[SUBJECT.replace(MANIFEST_DIGEST[7:], 'd' * 64)]))
        self.assert_error('DOCKER_SUBJECT_MISMATCH')

    def test_wrong_trivy_manifest_even_when_config_matches(self):
        self.mutate('trivyReport', lambda value: value['Metadata'].update(RepoDigests=[SUBJECT]))
        self.assert_error('TRIVY_ARCHIVE_INPUT_MISMATCH')

    def test_wrong_trivy_subject_name(self):
        self.mutate('trivyReport', lambda value: value.update(ArtifactName='mutable:latest'))
        self.assert_error('TRIVY_ARCHIVE_INPUT_MISMATCH')

    def test_wrong_configuration_id(self):
        self.mutate('dockerObservation', lambda value: value.update(observedDockerId='sha256:' + 'd' * 64))
        self.assert_error('DOCKER_GRAPH_ID_MISMATCH')

    def test_index_observation_is_not_configuration_authority(self):
        self.mutate('trivyReport', lambda value: value['Metadata'].update(ImageID=MANIFEST_DIGEST))
        self.assert_error('TRIVY_CONFIGURATION_MISMATCH')

    def test_trivy_config_match_does_not_qualify_missing_layer_scope(self):
        self.mutate('trivyReport', lambda value: value['Metadata'].pop('DiffIDs'))
        self.assert_error('TRIVY_LAYER_SCOPE_MISMATCH')

    def test_trivy_config_match_does_not_qualify_wrong_layer_scope(self):
        self.mutate('trivyReport', lambda value: value['Metadata'].update(DiffIDs=['sha256:' + '0' * 64]))
        self.assert_error('TRIVY_LAYER_SCOPE_MISMATCH')

    def test_actual_config_observation_is_supported_without_changing_graph_authority(self):
        self.mutate('dockerObservation', lambda value: value.update(observedDockerId=CONFIG_DIGEST))
        code, report = self.run_adapter()
        self.assertEqual(code, 0)
        self.assertEqual(report['imageConfigurationDigest'], CONFIG_DIGEST)
        self.assertEqual(report['configurationGraphQualification'], 'MATCHED')

    def test_wrong_retained_image_graph_cannot_self_approve(self):
        self.mutate('imageGraph', lambda value: value.update(configurationDigest=MANIFEST_DIGEST,
                                                            registryAuthentication='VERIFIED'))
        self.assert_error('RETAINED_IMAGE_GRAPH_MISMATCH')

    def test_current_producer_refuses_legacy_docker_observation(self):
        self.write(self.documents['dockerObservation'], {'imageId': CONFIG_DIGEST, 'repoDigests': [SUBJECT]})
        self.assert_error('IMAGE_OBSERVATION_SCHEMA')

    def test_wrong_observed_docker_platform(self):
        self.mutate('dockerObservation', lambda value: value['platform'].update(architecture='arm64'))
        self.assert_error('DOCKER_PLATFORM_MISMATCH')

    def test_missing_archive_does_not_qualify_from_json(self):
        (self.root / 'image.tar').unlink()
        self.assert_error('ARCHIVE_OR_GRAPH_INPUT_INVALID')

    def test_grype_other_sbom_path(self):
        self.mutate('grypeReport', lambda value: value['source'].update(target=str(self.root / 'other.json')))
        self.assert_error('GRYPE_SBOM_SOURCE_MISMATCH')

    def test_grype_reconstructed_image_requires_all_exact_root_fields(self):
        self.mutate('cyclonedx', lambda value: value.update(metadata={'component': {
            'type': 'container', 'name': SUBJECT, 'version': MANIFEST_DIGEST, 'bom-ref': CONFIG_DIGEST}}))
        self.rehash_sbom()
        self.mutate('grypeReport', lambda value: value.update(source={'type': 'image', 'target': {
            'userInput': SUBJECT, 'manifestDigest': MANIFEST_DIGEST, 'imageID': CONFIG_DIGEST}}))
        code, _ = self.run_adapter()
        self.assertEqual(code, 0)

    def test_grype_reconstructed_other_manifest_rejected(self):
        self.mutate('cyclonedx', lambda value: value.update(metadata={'component': {
            'type': 'container', 'name': SUBJECT, 'version': MANIFEST_DIGEST, 'bom-ref': CONFIG_DIGEST}}))
        self.rehash_sbom()
        self.mutate('grypeReport', lambda value: value.update(source={'type': 'image', 'target': {
            'userInput': SUBJECT, 'manifestDigest': 'sha256:' + 'd' * 64, 'imageID': CONFIG_DIGEST}}))
        self.assert_error('GRYPE_SBOM_SOURCE_MISMATCH')

    def test_scanner_version_mismatch(self):
        self.mutate('grypeVersion', lambda value: value.update(version='0.117.0'))
        self.assert_error('NATIVE_VULNERABILITY_INPUT_INVALID')

    def test_stale_database(self):
        self.mutate('grypeDb', lambda value: value.update(built='2026-08-01T00:00:00Z'))
        self.assert_error('NATIVE_VULNERABILITY_INPUT_INVALID')

    def test_high_no_fix_remains_blocked_without_exception(self):
        self.add_high()
        code, report = self.run_adapter()
        self.assertEqual(code, 1)
        self.assertEqual(report['integrityStatus'], 'CONSISTENT')
        self.assertEqual(report['nativeVulnerabilityDecision'], 'block')
        self.assertEqual(report['normalizedFindingCounts']['high'], 1)
        self.assertEqual(report['blockedFindingCount'], 1)
        self.assertEqual(report['waivedFindingCount'], 0)

    def example_exception(self):
        return {
            'advisoryId': 'GHSA-vfj7-8cjw-p6xm', 'package': {'name': 'braces', 'version': '3.0.3'},
            'imageDigest': MANIFEST_DIGEST, 'scope': 'runtime-image', 'kind': 'accepted-risk',
            'rationale': 'Synthetic fixture only; no actual exception approval.',
            'compensatingControl': 'Synthetic test', 'owner': 'fixture-owner', 'expiresOn': '2026-09-10',
        }

    def test_expired_exception_is_visible_input_error(self):
        self.add_high()
        exception = self.example_exception()
        exception['expiresOn'] = '2026-09-03'
        self.mutate('vulnerabilityPolicy', lambda value: value.update(exceptions=[exception]))
        self.assert_error('NATIVE_VULNERABILITY_INPUT_INVALID')

    def test_self_declared_exception_is_never_personal_approval(self):
        self.add_high()
        self.mutate('vulnerabilityPolicy', lambda value: value.update(exceptions=[self.example_exception()]))
        code, report = self.run_adapter()
        self.assertEqual(code, 0)
        self.assertEqual(report['nativeVulnerabilityDecision'], 'pass')
        self.assertEqual(report['waivedFindingCount'], 1)
        self.assertEqual(report['normalizedFindingCounts']['high'], 1)
        self.assertEqual(report['exceptionApprovalAuthentication'], 'UNVERIFIED')
        self.assertEqual(report['personalOwnerApproval'], 'UNVERIFIED')
        self.assertEqual(report['releaseAcceptance'], 'BLOCKED')

    def verify_existing(self):
        self.args.verify_directory = str(self.out)
        with contextlib.redirect_stdout(io.StringIO()):
            return advisory.verify_record(self.args)

    def test_consumer_positive_is_read_only_and_still_unverified(self):
        self.run_adapter()
        before = {path.name: path.read_bytes() for path in self.out.iterdir()}
        self.assertEqual(self.verify_existing(), 0)
        self.assertEqual(before, {path.name: path.read_bytes() for path in self.out.iterdir()})

    def test_consumer_retains_policy_block_after_valid_consistency(self):
        self.add_high()
        self.run_adapter()
        self.assertEqual(self.verify_existing(), 1)

    def test_consumer_changed_scanner_bytes_even_without_semantic_change(self):
        self.run_adapter()
        path = self.documents['grypeReport']
        path.write_bytes(path.read_bytes() + b' ')
        with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_ADVISORY_MISMATCH'):
            self.verify_existing()

    def test_consumer_changed_verifier_source_fingerprint(self):
        self.run_adapter()
        hashes = advisory.verifier_source_hashes()
        hashes['release_assurance_advisory.py'] = '0' * 64
        with patch.object(advisory, 'verifier_source_hashes', return_value=hashes):
            with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_ADVISORY_MISMATCH'):
                self.verify_existing()

    def test_consumer_detects_actual_on_disk_verifier_source_change(self):
        self.run_adapter()
        directory = self.root / 'changed-verifier'
        directory.mkdir()
        source = directory / 'release_assurance_advisory.py'
        source.write_bytes(Path(advisory.__file__).read_bytes() + b'\n# Deliberate source mutation control.\n')
        with patch.object(advisory, '__file__', str(source)):
            with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_ADVISORY_MISMATCH'):
                self.verify_existing()

    def test_consumer_wrong_verifier_version(self):
        self.run_adapter()
        path = self.out / 'release-assurance-advisory.json'
        value = self.read(path)
        value['verifierVersion'] = '0'
        self.write(path, value)
        with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_ADVISORY_MISMATCH'):
            self.verify_existing()

    def test_consumer_preserves_v1_bytes_as_configuration_graph_unverified(self):
        self.run_adapter()
        path = self.out / 'release-assurance-advisory.json'
        value = self.read(path)
        value.update(schema=advisory.LEGACY_SCHEMA, verifierVersion='1')
        self.write(path, value)
        original = path.read_bytes()
        with self.assertRaisesRegex(advisory.AdvisoryError, 'HISTORICAL_V1_CONFIGURATION_GRAPH_UNVERIFIED'):
            self.verify_existing()
        self.assertEqual(path.read_bytes(), original)

    def test_consumer_forged_crypto_and_owner_approval(self):
        self.run_adapter()
        path = self.out / 'release-assurance-advisory.json'
        value = self.read(path)
        value.update(cryptographicVerification='VERIFIED', personalOwnerApproval='APPROVED')
        self.write(path, value)
        with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_ADVISORY_MISMATCH'):
            self.verify_existing()

    def test_consumer_closed_scope_shrinkage(self):
        self.run_adapter()
        path = self.out / 'release-assurance-advisory.json'
        value = self.read(path)
        value['tierBControls'].pop()
        value['outstandingAcceptanceControlCount'] = 13
        self.write(path, value)
        with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_ADVISORY_MISMATCH'):
            self.verify_existing()

    def test_consumer_missing_execution_result(self):
        self.run_adapter()
        (self.out / 'vulnerability-gate-summary.json').unlink()
        with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_SUMMARY_INPUT_INVALID'):
            self.verify_existing()

    def test_consumer_modified_summary_cannot_remove_high(self):
        self.add_high()
        self.run_adapter()
        path = self.out / 'vulnerability-gate-summary.json'
        value = self.read(path)
        value['blockedFindings'] = []
        value['decision'] = 'pass'
        self.write(path, value)
        with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_NATIVE_RESULT_MISMATCH'):
            self.verify_existing()

    def test_consumer_modified_normalized_result(self):
        self.add_high()
        self.run_adapter()
        path = self.out / 'vulnerabilities.normalized.json'
        value = self.read(path)
        value['findings'] = []
        self.write(path, value)
        with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_NATIVE_RESULT_MISMATCH'):
            self.verify_existing()

    def test_consumer_native_result_bytes_are_exact(self):
        self.run_adapter()
        path = self.out / 'vulnerabilities.normalized.json'
        path.write_bytes(path.read_bytes() + b' ')
        with self.assertRaisesRegex(advisory.AdvisoryError, 'RECORDED_NATIVE_BYTES_MISMATCH'):
            self.verify_existing()

    def test_consumer_wrong_candidate_context(self):
        self.run_adapter()
        self.args.expected_repository_sha = 'b' * 40
        with self.assertRaisesRegex(advisory.AdvisoryError, 'SBOM_CANDIDATE_MISMATCH'):
            self.verify_existing()

    def test_input_claims_do_not_authenticate_crypto_owner_or_github(self):
        self.mutate('sbomMetadata', lambda value: value.update(
            githubRunAuthentication='VERIFIED', personalOwnerApproval='APPROVED',
            cryptographicVerification='VERIFIED', releaseAcceptance='PASS', tierBControls=[]))
        _, report = self.run_adapter()
        for field in ('githubRunAuthentication', 'personalOwnerApproval', 'cryptographicVerification'):
            self.assertEqual(report[field], 'UNVERIFIED')
        self.assertEqual(report['acceptanceQualifiedControlCount'], 0)
        self.assertEqual(len(report['tierBControls']), 14)
        self.assertEqual(report['releaseAcceptance'], 'BLOCKED')

    def test_false_not_applicable_cannot_shrink_closed_control_inventory(self):
        self.mutate('sbomMetadata', lambda value: value.update(advancedAdapters='NOT_APPLICABLE'))
        _, report = self.run_adapter()
        self.assertEqual(report['advancedAdapters']['RESTler'], 'MISSING')
        self.assertEqual(report['advancedAdapters']['approvedOast'], 'OWNER_DISPOSITION_PENDING')
        self.assertEqual(next(item for item in report['tierBControls'] if item['control'] == 'SEC-18')['applicability'], 'UNDECIDED')

    def test_bounded_stream_read_rejects_overflow(self):
        self.documents['grypeReport'].write_bytes(b' ' * 2000)
        with patch.object(release, 'MAX_JSON_BYTES', 1024):
            self.assert_error()

    def test_duplicate_nonfinite_invalid_utf8_and_deep_json_rejected(self):
        for index, payload in enumerate((b'{"matches":[],"matches":[]}', b'{"x":NaN}', b'{"x":"\xff"}',
                                         b'{"x":' + b'[' * 129 + b'0' + b']' * 129 + b'}')):
            with self.subTest(index=index):
                self.args.out_directory = str(self.root / f'invalid-{index}')
                self.out = Path(self.args.out_directory)
                self.documents['grypeReport'].write_bytes(payload)
                self.assert_error('GRYPEREPORT_INPUT_INVALID')

    def test_error_receipt_does_not_export_malicious_values(self):
        malicious = 'ghp_' + 'q' * 30
        self.mutate('grypeVersion', lambda value: value.update(version=malicious))
        report = self.assert_error()
        self.assertNotIn(malicious, json.dumps(report))

    def test_prior_historical_output_is_not_overwritten(self):
        self.out.mkdir()
        old = self.out / 'release-assurance-advisory.json'
        old.write_bytes(b'historical bytes')
        with self.assertRaises(advisory.AdvisoryError):
            advisory.command(self.args)
        self.assertEqual(old.read_bytes(), b'historical bytes')

    def test_native_functional_scope_positive_still_image_unverified(self):
        self.add_functional()
        code, report = self.run_adapter()
        self.assertEqual(code, 0)
        observation = report['nativeTierBObservations']['functionalFull']
        self.assertEqual(observation['domainCount'], 4)
        self.assertEqual(observation['journeyCount'], 7)
        self.assertEqual(observation['candidateAndRunConsistency'], 'MATCHED')
        self.assertEqual(observation['imageSubjectBinding'], 'UNVERIFIED')
        self.assertEqual(report['outstandingAcceptanceControlCount'], 14)

    def test_functional_missing_domain_cannot_pass(self):
        path = self.add_functional()
        value = self.read(path)
        value['lanes'].pop()
        self.write(path, value)
        self.assert_error('FUNCTIONAL_SCOPE_OR_IDENTITY_INVALID')

    def test_functional_wrong_candidate_and_run(self):
        path = self.add_functional()
        value = self.read(path)
        value['commitSha'] = 'b' * 40
        value['runAttempt'] = '1'
        self.write(path, value)
        self.assert_error('FUNCTIONAL_SCOPE_OR_IDENTITY_INVALID')

    def test_functional_skipped_journey_cannot_pass(self):
        path = self.add_functional()
        value = self.read(path)
        value['lanes'][0]['journeys'][0]['status'] = 'SKIPPED'
        self.write(path, value)
        self.assert_error('FUNCTIONAL_SCOPE_OR_IDENTITY_INVALID')

    def test_native_schemathesis_and_zap_positive_remain_unbound(self):
        for role in advisory.SCHEMATHESIS_ROLES:
            self.add_schemathesis(role)
        for role in advisory.ZAP_ROLES:
            self.add_zap(role)
        code, report = self.run_adapter()
        self.assertEqual(code, 0)
        native = report['nativeTierBObservations']
        self.assertEqual(native['outstandingSchemathesisRoleCount'], 0)
        self.assertEqual(native['outstandingZapRoleCount'], 0)
        for observation in native['schemathesisDeep'] + native['zapApi']:
            self.assertEqual(observation['candidateAndRunConsistency'], 'UNVERIFIED')
            self.assertEqual(observation['imageSubjectBinding'], 'UNVERIFIED')
        self.assertEqual(report['acceptanceQualifiedControlCount'], 0)

    def test_schemathesis_zero_execution_rejected(self):
        path = self.add_schemathesis()
        document = self.read(path)
        document['request_count'] = 0
        self.write(path, document)
        self.assert_error('SCHEMATHESIS_EXECUTION_MISSING')

    def test_schemathesis_duplicate_roles_rejected(self):
        path = self.add_schemathesis()
        self.args.schemathesis_metadata.append(str(path))
        self.assert_error('SCHEMATHESIS_ROLE_DUPLICATE_OR_INVALID')

    def test_schemathesis_forged_binding_fields_rejected(self):
        path = self.add_schemathesis()
        document = self.read(path)
        document['candidateSha'] = SHA
        document['imageSubjectBinding'] = 'VERIFIED'
        self.write(path, document)
        self.assert_error('SCHEMATHESIS_SCHEMA_INVALID')

    def test_zap_unsanitized_metadata_rejected(self):
        path = self.add_zap()
        document = self.read(path)
        document['unsanitizedAllowed'] = True
        self.write(path, document)
        self.assert_error('ZAP_UNSANITIZED_INPUT')

    def test_zap_false_pass_with_high_alert_rejected(self):
        path = self.add_zap()
        document = self.read(path)
        document['highAlerts'] = 1
        self.write(path, document)
        self.assert_error('ZAP_OUTCOME_INCONSISTENT')

    def test_zap_scanner_failure_is_visible_with_consistent_summary(self):
        path = self.add_zap()
        document = self.read(path)
        document['status'] = 'scanner-failed'
        self.write(path, document)
        code, report = self.run_adapter()
        self.assertEqual(code, 1)
        self.assertEqual(report['integrityStatus'], 'CONSISTENT')
        self.assertEqual(report['nativeTierBObservations']['zapApi'][0]['nativeOutcome'], 'scanner-failed')

    def test_exact_cli_positive_and_missing_expected_candidate(self):
        command = [sys.executable, str(ROOT / 'scripts/ci/release_assurance_advisory.py'),
                   '--expected-repository-sha', SHA, '--expected-subject', SUBJECT,
                   '--expected-platform', 'linux/amd64',
                   '--expected-run-identity', RUN, '--sbom-directory', str(self.root),
                   '--scan-directory', str(self.root), '--policy', str(self.native.policy),
                   '--out-directory', str(self.out), '--now', native_fixture.NOW]
        positive = subprocess.run(command, text=True, capture_output=True, check=False)
        self.assertEqual(positive.returncode, 0, positive.stderr)
        consumer_command = ['--verify-directory' if value == '--out-directory' else value for value in command]
        consumer = subprocess.run(consumer_command, text=True, capture_output=True, check=False)
        self.assertEqual(consumer.returncode, 0, consumer.stderr)
        missing = subprocess.run(command[:2] + command[4:], text=True, capture_output=True, check=False)
        self.assertNotEqual(missing.returncode, 0)
        self.assertIn('--expected-repository-sha', missing.stderr)

    def test_malformed_or_unbounded_cli_identities(self):
        for index, (field, value) in enumerate((
            ('expected_repository_sha', 'A' * 40), ('expected_subject', 'x' * 513),
            ('expected_run_identity', RUN + '?ignored=1'), ('expected_run_identity', RUN.replace('/123/', '/0/')),
        )):
            with self.subTest(index=index):
                original = getattr(self.args, field)
                setattr(self.args, field, value)
                self.out = self.root / f'identity-{index}'
                self.args.out_directory = str(self.out)
                self.assert_error()
                setattr(self.args, field, original)


class ReleaseAdvisoryWorkflowTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        source = ROOT / '.github/workflows/release-supply-chain.yml'
        result = subprocess.run(['ruby', '-ryaml', '-rjson', '-e',
                                 'puts JSON.generate(YAML.load_file(ARGV[0]))', str(source)],
                                capture_output=True, text=True, check=True)
        cls.workflow = json.loads(result.stdout)

    def test_advisory_is_read_only_and_not_a_promotion_prerequisite(self):
        jobs = self.workflow['jobs']
        advisory_job = jobs['release-assurance-advisory']
        self.assertEqual(advisory_job['name'], 'sec14-release-assurance-advisory')
        self.assertEqual(advisory_job['needs'], 'publish-release-image')
        self.assertEqual(advisory_job['permissions'], {'contents': 'read', 'packages': 'read'})
        self.assertEqual(jobs['promote-release-tag']['needs'], ['publish-release-image', 'verify-release-subject'])
        self.assertNotIn('environment', advisory_job)
        self.assertNotIn('continue-on-error', advisory_job)

    def test_advisory_uses_original_published_subject_sbom_and_same_attempt(self):
        job = self.workflow['jobs']['release-assurance-advisory']
        download = next(step for step in job['steps'] if step['name'] == 'Download original published-image SBOM')
        self.assertEqual(download['with']['name'], 'sec11-release-inputs-${{ github.run_id }}-${{ github.run_attempt }}')
        scripts = '\n'.join(step.get('run', '') for step in job['steps'])
        self.assertIn('docker pull --platform "$PLATFORM" "$SUBJECT"', scripts)
        self.assertIn('grype "sbom:artifacts/release-inputs/sbom.cyclonedx.json"', scripts)
        self.assertIn('${GITHUB_RUN_ID}/attempts/${GITHUB_RUN_ATTEMPT}', scripts)
        self.assertNotIn('--ignore-unfixed', scripts)
        self.assertNotIn('--exit-code 0', scripts)
        self.assertNotIn('docker build \\', scripts)
        self.assertNotIn('docker push', scripts)
        self.assertNotIn('cosign ', scripts)
        self.assertNotIn('schedule', self.workflow)

    def test_archive_configuration_is_resolved_before_actual_trivy_input(self):
        steps = self.workflow['jobs']['release-assurance-advisory']['steps']
        graph_step = next(step for step in steps if step['name'] == 'Resolve immutable registry and archive image graph')
        scan = next(step for step in steps if step['name'] == 'Scan original SBOM and exact published image')
        self.assertIn('docker buildx imagetools inspect "$SUBJECT" --raw', graph_step['run'])
        self.assertIn('--select-platform', graph_step['run'])
        self.assertIn('docker image save --output artifacts/release-scan/image.tar "$SUBJECT"', graph_step['run'])
        self.assertIn('--input /repo/artifacts/release-scan/image.tar', scan['run'])
        self.assertNotIn('/var/run/docker.sock', scan['run'])
        self.assertNotIn('"imageId":"{{.Id}}"', graph_step['run'])
        upload = next(step for step in steps if step['name'] == 'Upload scoped SEC-14 Advisory observations')
        self.assertIn('!artifacts/release-scan/image.tar', upload['with']['path'])

    def test_advisory_errors_are_attempted_and_uploaded_after_scanner_failure(self):
        steps = self.workflow['jobs']['release-assurance-advisory']['steps']
        collector = next(step for step in steps if step['name'] == 'Record Advisory consistency and explicit acceptance gaps')
        upload = next(step for step in steps if step['name'] == 'Upload scoped SEC-14 Advisory observations')
        self.assertEqual(collector['if'], 'always()')
        self.assertEqual(upload['if'], 'always()')
        self.assertNotIn('continue-on-error', collector)
        self.assertEqual(upload['with']['if-no-files-found'], 'error')


if __name__ == '__main__':
    unittest.main()
