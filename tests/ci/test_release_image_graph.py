from __future__ import annotations

import contextlib
import io
import json
import subprocess
import sys
import tarfile
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts' / 'ci'))
import release_image_graph as graph
from release_image_fixture import ImageFixture, json_bytes, tar_bytes


class ReleaseImageGraphTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.root = Path(self.temporary.name)
        self.fixture = ImageFixture()
        self.fixture.write(self.root)

    def tearDown(self):
        self.temporary.cleanup()

    def reject(self, code):
        with self.assertRaisesRegex(graph.GraphError, '^' + code + '$'):
            self.fixture.resolve(self.root)

    def files(self, action):
        action(self.fixture.files)
        (self.root / 'image.tar').write_bytes(tar_bytes(self.fixture.files))

    def test_oci_index_has_distinct_subject_platform_and_actual_config(self):
        result = self.fixture.resolve(self.root)
        self.assertEqual(result['configurationDigest'], self.fixture.config_digest)
        self.assertEqual(result['platformManifestDigest'], self.fixture.manifest_digest)
        self.assertEqual(len({result['subjectDigest'], result['platformManifestDigest'],
                              result['configurationDigest']}), 3)
        self.assertEqual(result['layerBytesBinding'], 'REGISTRY_BLOB_AND_CONFIG_DIFF_ID')
        self.assertEqual(result['registryAuthentication'], 'UNVERIFIED')
        self.assertEqual(result['archiveRetention'], 'RUNNER_ONLY_NOT_UPLOADED')

    def test_direct_manifest_and_plain_layer_supported(self):
        fixture = ImageFixture(direct=True, gzip_layer=False)
        fixture.write(self.root)
        result = fixture.resolve(self.root)
        self.assertEqual(result['subjectDigest'], result['platformManifestDigest'])
        self.assertEqual(result['configurationDigest'], fixture.config_digest)

    def test_digest_valid_configuration_with_wrong_platform_rejected(self):
        fixture = ImageFixture(config_platform={'architecture': 'arm64'})
        fixture.write(self.root)
        with self.assertRaisesRegex(graph.GraphError, 'CONFIG_PLATFORM_MISMATCH'):
            fixture.resolve(self.root)

    def test_digest_valid_configuration_with_wrong_diff_id_rejected(self):
        fixture = ImageFixture(config_diff_id='sha256:' + '0' * 64)
        fixture.write(self.root)
        with self.assertRaisesRegex(graph.GraphError, 'LAYER_DIFF_ID_MISMATCH'):
            fixture.resolve(self.root)

    def test_legacy_save_config_and_diff_id_supported_without_compressed_layer_claim(self):
        fixture = ImageFixture(legacy=True)
        fixture.write(self.root)
        result = fixture.resolve(self.root)
        self.assertEqual(result['layerBytesBinding'], 'CONFIG_DIFF_ID')
        self.assertEqual(result['registryCompressedLayerBytesQualification'], 'UNVERIFIED')
        self.assertEqual(result['configurationDigest'], fixture.config_digest)

    def test_single_transport_newline_is_checked_against_expected_payload_digest(self):
        for name in ('registry-manifest.json', 'platform-manifest.json'):
            path = self.root / name
            path.write_bytes(path.read_bytes() + b'\n')
        self.assertEqual(self.fixture.resolve(self.root)['configurationDigest'], self.fixture.config_digest)

    def test_changed_top_manifest_rejected(self):
        (self.root / 'registry-manifest.json').write_bytes(self.fixture.top_raw + b' ')
        self.reject('REGISTRY_MANIFEST_DIGEST_MISMATCH')

    def test_changed_platform_manifest_rejected(self):
        (self.root / 'platform-manifest.json').write_bytes(self.fixture.manifest_raw + b' ')
        self.reject('REGISTRY_MANIFEST_DIGEST_MISMATCH')

    def test_two_transport_newlines_do_not_canonicalize(self):
        (self.root / 'registry-manifest.json').write_bytes(self.fixture.top_raw + b'\n\n')
        self.reject('REGISTRY_MANIFEST_DIGEST_MISMATCH')

    def test_missing_archive_is_visible(self):
        (self.root / 'image.tar').unlink()
        self.reject('ARCHIVE_OR_GRAPH_INPUT_INVALID')

    def test_config_bytes_tamper_rejected(self):
        self.files(lambda files: files.update({self.fixture.config_path: self.fixture.config_raw + b' '}))
        self.reject('ARCHIVE_BLOB_DIGEST_MISMATCH')

    def test_layer_bytes_tamper_rejected(self):
        self.files(lambda files: files.update({self.fixture.layer_path: self.fixture.layer_blob + b' '}))
        self.reject('ARCHIVE_BLOB_DIGEST_MISMATCH')

    def test_missing_config_rejected(self):
        self.files(lambda files: files.pop(self.fixture.config_path))
        self.reject('CONFIG_BYTES_MISMATCH')

    def test_missing_layer_rejected(self):
        self.files(lambda files: files.pop(self.fixture.layer_path))
        self.reject('OCI_LAYER_BYTES_MISMATCH')

    def test_wrong_docker_config_selection_rejected(self):
        self.files(lambda files: files.update({'manifest.json': json_bytes([{
            'Config': 'wrong.json', 'Layers': [self.fixture.layer_path]}])}))
        self.reject('CONFIG_BYTES_MISMATCH')

    def test_docker_scope_shrinkage_rejected(self):
        self.files(lambda files: files.update({'manifest.json': json_bytes([{
            'Config': self.fixture.config_path, 'Layers': []}])}))
        self.reject('DOCKER_LAYER_SCOPE_MISMATCH')

    def test_multiple_docker_runtime_entries_rejected(self):
        value = json.loads(self.fixture.files['manifest.json'])
        self.files(lambda files: files.update({'manifest.json': json_bytes(value + value)}))
        self.reject('ARCHIVE_RUNTIME_SELECTION_AMBIGUOUS')

    def test_missing_root_graph_blob_rejected(self):
        self.files(lambda files: files.pop('blobs/sha256/' + self.fixture.subject_digest[7:]))
        self.reject('OCI_REGISTRY_GRAPH_MISMATCH')

    def test_missing_platform_graph_blob_rejected(self):
        self.files(lambda files: files.pop('blobs/sha256/' + self.fixture.manifest_digest[7:]))
        self.reject('OCI_REGISTRY_GRAPH_MISMATCH')

    def test_archive_path_escape_rejected_without_extraction(self):
        self.files(lambda files: files.update({'../escaped.json': b'{}'}))
        self.reject('ARCHIVE_PATH_INVALID')
        self.assertFalse((self.root.parent / 'escaped.json').exists())

    def test_windows_absolute_member_rejected_without_extraction(self):
        self.files(lambda files: files.update({'C:/outside/escaped.json': b'{}'}))
        self.reject('ARCHIVE_PATH_INVALID')

    def test_archive_symlink_rejected(self):
        path = self.root / 'image.tar'
        with tarfile.open(path, 'a') as tar:
            member = tarfile.TarInfo('link')
            member.type = tarfile.SYMTYPE
            member.linkname = '../../private'
            tar.addfile(member)
        self.reject('ARCHIVE_LINK_OR_SPECIAL_MEMBER')

    def test_duplicate_member_rejected(self):
        with tarfile.open(self.root / 'image.tar', 'a') as tar:
            raw = self.fixture.files['manifest.json']
            member = tarfile.TarInfo('manifest.json')
            member.size = len(raw)
            tar.addfile(member, io.BytesIO(raw))
        self.reject('ARCHIVE_DUPLICATE_OR_MEMBER_LIMIT')

    def test_bounded_archive_snapshot_rejects_growth(self):
        with patch.object(graph, 'MAX_ARCHIVE_BYTES', 100):
            self.reject('ARCHIVE_SIZE_LIMIT')

    def test_bounded_member_count(self):
        with patch.object(graph, 'MAX_MEMBERS', 2):
            self.reject('ARCHIVE_DUPLICATE_OR_MEMBER_LIMIT')

    def test_bounded_expanded_layer(self):
        with patch.object(graph, 'MAX_EXPANDED_BYTES', 100):
            self.reject('ARCHIVE_EXPANSION_LIMIT')

    def test_bounded_pax_header_before_parser_allocation(self):
        member = graph.BoundedTarInfo('extended')
        member.size = 64 * 1024 + 1
        with self.assertRaisesRegex(graph.GraphError, 'ARCHIVE_EXTENDED_HEADER_LIMIT'):
            member._proc_pax(None)

    def test_sparse_parser_is_not_an_unbounded_hidden_path(self):
        with self.assertRaisesRegex(graph.GraphError, 'ARCHIVE_SPARSE_MEMBER_UNSUPPORTED'):
            graph.BoundedTarInfo('sparse')._proc_sparse(None)

    def test_descriptor_size_and_remote_url_are_rejected(self):
        value = self.fixture.manifest['config'].copy()
        for change, code in (({'size': True}, 'DESCRIPTOR_SIZE_INVALID'),
                             ({'urls': ['https://outside.invalid/config']},
                              'EXTERNAL_OR_EMBEDDED_DESCRIPTOR_UNSUPPORTED')):
            with self.subTest(code=code), self.assertRaisesRegex(graph.GraphError, code):
                graph.descriptor(dict(value, **change), graph.CONFIG_TYPES)

    def test_wrong_selected_manifest_size_rejected(self):
        with self.assertRaisesRegex(graph.GraphError, 'REGISTRY_MANIFEST_SIZE_MISMATCH'):
            graph.manifest_bytes(self.root / 'platform-manifest.json',
                                 self.fixture.manifest_digest, len(self.fixture.manifest_raw) + 1)

    def test_unsupported_nested_index_is_explicit(self):
        value = self.fixture.top.copy()
        entry = value['manifests'][0].copy()
        entry['mediaType'] = 'application/vnd.oci.image.index.v1+json'
        value['manifests'] = [entry]
        with self.assertRaisesRegex(graph.GraphError, 'DESCRIPTOR_TYPE_INVALID'):
            graph.select_manifest(value, self.fixture.subject_digest, graph.platform('linux/amd64'))

    def test_strict_json_rejects_duplicate_nonfinite_and_excessive_nesting(self):
        for raw in (b'{"schemaVersion":2,"schemaVersion":2}', b'{"value":NaN}',
                    b'{"x":' + b'[' * 129 + b'0' + b']' * 129 + b'}'):
            with self.subTest(raw=raw[:30]):
                path = self.root / 'malicious.json'
                path.write_bytes(raw)
                with self.assertRaisesRegex(graph.GraphError, 'GRAPH_JSON_INVALID'):
                    graph.read_json(path)

    def test_cli_unbounded_input_fails_without_echoing_payload(self):
        marker = 'unsafe-marker-' + 'x' * 5000
        result = subprocess.run([sys.executable, str(ROOT / 'scripts/ci/release_image_graph.py'),
                                 '--expected-subject', marker, '--expected-platform', 'linux/amd64',
                                 '--top-manifest', str(self.root / 'registry-manifest.json'), '--select-platform'],
                                capture_output=True, text=True)
        self.assertEqual(result.returncode, 1)
        self.assertIn('CLI_INPUT_LIMIT', result.stderr)
        self.assertNotIn('unsafe-marker', result.stderr)

    def test_legacy_mutated_uncompressed_layer_rejected(self):
        fixture = ImageFixture(legacy=True)
        fixture.files[fixture.layer_path] += b'tamper'
        fixture.write(self.root)
        with self.assertRaisesRegex(graph.GraphError, 'LAYER_DIFF_ID_MISMATCH'):
            fixture.resolve(self.root)

    def test_wrong_platform_is_not_missing_fixture_success(self):
        with self.assertRaisesRegex(graph.GraphError, 'RUNTIME_PLATFORM_MISSING_OR_AMBIGUOUS'):
            graph.resolve(self.root / 'image.tar', self.root / 'registry-manifest.json',
                          self.root / 'platform-manifest.json', self.fixture.subject, 'linux/arm64')

    def test_ambiguous_platform_rejected(self):
        top = self.fixture.top.copy()
        top['manifests'] = top['manifests'] * 2
        with self.assertRaisesRegex(graph.GraphError, 'RUNTIME_PLATFORM_MISSING_OR_AMBIGUOUS'):
            graph.select_manifest(top, self.fixture.subject_digest, graph.platform('linux/amd64'))

    def test_attestation_cannot_be_selected_as_runtime(self):
        self.fixture.top['manifests'][0]['annotations'] = {'vnd.docker.reference.type': 'attestation-manifest'}
        with self.assertRaisesRegex(graph.GraphError, 'ATTESTATION_NOT_RUNTIME_PLATFORM'):
            graph.select_manifest(self.fixture.top, self.fixture.subject_digest, graph.platform('linux/amd64'))

    def test_unknown_platform_attestation_does_not_shrink_runtime(self):
        entry = self.fixture.top['manifests'][0].copy()
        entry['platform'] = {'os': 'unknown', 'architecture': 'unknown'}
        entry['annotations'] = {'vnd.docker.reference.type': 'attestation-manifest'}
        self.fixture.top['manifests'].append(entry)
        selected, _ = graph.select_manifest(self.fixture.top, self.fixture.subject_digest,
                                             graph.platform('linux/amd64'))
        self.assertEqual(selected, self.fixture.manifest_digest)

    def test_cli_selection_and_exclusive_output(self):
        command = [sys.executable, str(ROOT / 'scripts/ci/release_image_graph.py'),
                   '--expected-subject', self.fixture.subject, '--expected-platform', 'linux/amd64',
                   '--top-manifest', str(self.root / 'registry-manifest.json')]
        selected = subprocess.run(command + ['--select-platform'], capture_output=True, text=True)
        self.assertEqual(selected.returncode, 0, selected.stderr)
        self.assertEqual(selected.stdout.strip(), self.fixture.manifest_digest)
        command += ['--archive', str(self.root / 'image.tar'), '--platform-manifest',
                    str(self.root / 'platform-manifest.json'), '--output', str(self.root / 'new.json')]
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 0)
        original = (self.root / 'new.json').read_bytes()
        self.assertEqual(subprocess.run(command, capture_output=True).returncode, 1)
        self.assertEqual((self.root / 'new.json').read_bytes(), original)


if __name__ == '__main__':
    unittest.main()
