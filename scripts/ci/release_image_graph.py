#!/usr/bin/env python3
"""Resolve a release's registry manifest/config graph from bounded native bytes.

No archive paths are extracted. Docker's observed .Id is not a config authority.
This proves local byte consistency, not registry, execution or signer authenticity.
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import os
import re
import stat
import sys
import tarfile
import tempfile
from pathlib import Path
from typing import Any

import release_supply_chain as release

SCHEMA = 'coglatas-release-image-graph-v2'
MAX_ARCHIVE_BYTES = 4 * 1024**3
MAX_MEMBER_BYTES = 2 * 1024**3
MAX_EXPANDED_BYTES = 8 * 1024**3
MAX_MEMBERS = 8192
CHUNK = 1024 * 1024
DIGEST = re.compile(r'sha256:[0-9a-f]{64}')
PLATFORM_PART = re.compile(r'[a-z0-9][a-z0-9._-]{0,31}')
INDEX_TYPES = {'application/vnd.oci.image.index.v1+json',
               'application/vnd.docker.distribution.manifest.list.v2+json'}
MANIFEST_TYPES = {'application/vnd.oci.image.manifest.v1+json',
                  'application/vnd.docker.distribution.manifest.v2+json'}
CONFIG_TYPES = {'application/vnd.oci.image.config.v1+json',
                'application/vnd.docker.container.image.v1+json'}
PLAIN_LAYERS = {'application/vnd.oci.image.layer.v1.tar'}
GZIP_LAYERS = {'application/vnd.oci.image.layer.v1.tar+gzip',
               'application/vnd.docker.image.rootfs.diff.tar.gzip'}


class GraphError(ValueError):
    pass


class BoundedTarInfo(tarfile.TarInfo):
    def _proc_pax(self, tar):
        require(0 <= self.size <= 64 * 1024, 'ARCHIVE_EXTENDED_HEADER_LIMIT')
        return super()._proc_pax(tar)

    def _proc_gnulong(self, tar):
        require(0 <= self.size <= 4096, 'ARCHIVE_LONG_NAME_LIMIT')
        return super()._proc_gnulong(tar)

    def _proc_sparse(self, tar):
        raise GraphError('ARCHIVE_SPARSE_MEMBER_UNSUPPORTED')


def require(condition: bool, code: str) -> None:
    if not condition:
        raise GraphError(code)


def digest(raw: bytes) -> str:
    return 'sha256:' + hashlib.sha256(raw).hexdigest()


def read_json(path: Path) -> tuple[dict[str, Any], bytes]:
    try:
        with path.open('rb') as handle:
            require(stat.S_ISREG(os.fstat(handle.fileno()).st_mode), 'INPUT_NOT_REGULAR')
            raw = handle.read(release.MAX_JSON_BYTES + 1)
        return release.parse_json_bytes(raw), raw
    except (OSError, release.ReleaseEvidenceError):
        raise GraphError('GRAPH_JSON_INVALID') from None


def platform(value: str) -> dict[str, str]:
    require(isinstance(value, str) and len(value) <= 98, 'PLATFORM_INVALID')
    parts = value.split('/')
    require(len(parts) in (2, 3) and all(PLATFORM_PART.fullmatch(part) for part in parts),
            'PLATFORM_INVALID')
    return {'os': parts[0], 'architecture': parts[1], 'variant': parts[2] if len(parts) == 3 else ''}


def descriptor(value: Any, allowed: set[str]) -> tuple[str, int]:
    require(isinstance(value, dict) and value.get('mediaType') in allowed, 'DESCRIPTOR_TYPE_INVALID')
    require(isinstance(value.get('digest'), str) and DIGEST.fullmatch(value['digest']) is not None,
            'DESCRIPTOR_DIGEST_INVALID')
    require(type(value.get('size')) is int and 0 < value['size'] <= MAX_MEMBER_BYTES,
            'DESCRIPTOR_SIZE_INVALID')
    require('urls' not in value and 'data' not in value, 'EXTERNAL_OR_EMBEDDED_DESCRIPTOR_UNSUPPORTED')
    return value['digest'], value['size']


def manifest_bytes(path: Path, expected: str, expected_size: int | None = None) -> tuple[dict[str, Any], bytes, str]:
    document, raw = read_json(path)
    # Buildx's raw printer may append one LF. Never canonicalize JSON: only accept
    # exact digest-bound payload bytes (or that payload plus one transport LF).
    payload = raw
    if digest(payload) != expected and payload.endswith(b'\n'):
        payload = payload[:-1]
    require(digest(payload) == expected, 'REGISTRY_MANIFEST_DIGEST_MISMATCH')
    require(expected_size is None or len(payload) == expected_size, 'REGISTRY_MANIFEST_SIZE_MISMATCH')
    require(type(document.get('schemaVersion')) is int and document['schemaVersion'] == 2,
            'REGISTRY_SCHEMA_INVALID')
    return document, payload, hashlib.sha256(raw).hexdigest()


def select_manifest(top: dict[str, Any], subject_digest: str,
                    expected_platform: dict[str, str]) -> tuple[str, int | None]:
    if top.get('mediaType') in MANIFEST_TYPES:
        return subject_digest, None
    require(top.get('mediaType') in INDEX_TYPES, 'REGISTRY_MEDIA_TYPE_UNSUPPORTED')
    manifests = top.get('manifests')
    require(isinstance(manifests, list) and 0 < len(manifests) <= 128, 'INDEX_SCOPE_INVALID')
    matches = []
    for item in manifests:
        entry_digest, entry_size = descriptor(item, MANIFEST_TYPES)
        entry_platform = item.get('platform')
        require(isinstance(entry_platform, dict), 'INDEX_PLATFORM_MISSING')
        observed = {key: entry_platform.get(key, '') for key in expected_platform}
        if observed == expected_platform:
            annotations = item.get('annotations', {})
            require(isinstance(annotations, dict), 'INDEX_ANNOTATIONS_INVALID')
            require(annotations.get('vnd.docker.reference.type') != 'attestation-manifest',
                    'ATTESTATION_NOT_RUNTIME_PLATFORM')
            matches.append((entry_digest, entry_size))
    require(len(matches) == 1, 'RUNTIME_PLATFORM_MISSING_OR_AMBIGUOUS')
    return matches[0]


def subject_digest(subject: str) -> str:
    try:
        require(isinstance(subject, str) and len(subject) <= 512, 'SUBJECT_INVALID')
        return release.parse_subject(subject)[1]
    except release.ReleaseEvidenceError:
        raise GraphError('SUBJECT_INVALID') from None


def safe_name(name: str) -> str:
    require(isinstance(name, str) and 0 < len(name) <= 4096 and '\\' not in name
            and ':' not in name and '\x00' not in name, 'ARCHIVE_PATH_INVALID')
    clean = name.rstrip('/')
    require(all(part not in ('', '.', '..') for part in clean.split('/')), 'ARCHIVE_PATH_INVALID')
    return clean


def stream_hash(handle: Any, limit: int) -> tuple[str, int]:
    hasher = hashlib.sha256()
    size = 0
    while True:
        chunk = handle.read(min(CHUNK, limit - size + 1))
        if not chunk:
            break
        size += len(chunk)
        require(size <= limit, 'ARCHIVE_EXPANSION_LIMIT')
        hasher.update(chunk)
    return 'sha256:' + hasher.hexdigest(), size


def archive_manifest(raw: bytes) -> dict[str, Any]:
    try:
        require(0 < len(raw) <= release.MAX_JSON_BYTES, 'ARCHIVE_MANIFEST_SIZE')
        text = raw.decode('utf-8')
        release._bounded_json_depth(text)
        value = json.loads(text, object_pairs_hook=release._unique_object,
                           parse_constant=release._invalid_constant)
        require(isinstance(value, list) and len(value) == 1 and isinstance(value[0], dict),
                'ARCHIVE_RUNTIME_SELECTION_AMBIGUOUS')
        return value[0]
    except (ValueError, UnicodeError, RecursionError) as error:
        if isinstance(error, GraphError):
            raise
        raise GraphError('ARCHIVE_MANIFEST_INVALID') from None


def resolve(archive: Path, top_path: Path, platform_path: Path, subject: str,
            expected_platform: str) -> dict[str, Any]:
    wanted = platform(expected_platform)
    root_digest = subject_digest(subject)
    top, top_payload, top_input_hash = manifest_bytes(top_path, root_digest)
    selected_digest, selected_size = select_manifest(top, root_digest, wanted)
    selected, selected_payload, platform_input_hash = manifest_bytes(platform_path, selected_digest, selected_size)
    require(selected.get('mediaType') in MANIFEST_TYPES and not selected.get('artifactType'),
            'RUNTIME_MANIFEST_INVALID')
    config_digest, config_size = descriptor(selected.get('config'), CONFIG_TYPES)
    layers = selected.get('layers')
    require(isinstance(layers, list) and len(layers) <= 512, 'LAYER_SCOPE_INVALID')
    layer_descriptors = [descriptor(item, PLAIN_LAYERS | GZIP_LAYERS) for item in layers]

    # Read a regular file into a private bounded snapshot before any parse/hash
    # comparisons. Replacement/growth cannot switch bytes between the two passes.
    try:
        with archive.open('rb') as source, tempfile.TemporaryFile() as snapshot:
            require(stat.S_ISREG(os.fstat(source.fileno()).st_mode), 'ARCHIVE_NOT_REGULAR')
            hasher = hashlib.sha256()
            archive_size = 0
            while True:
                raw = source.read(min(CHUNK, MAX_ARCHIVE_BYTES - archive_size + 1))
                if not raw:
                    break
                archive_size += len(raw)
                require(archive_size <= MAX_ARCHIVE_BYTES, 'ARCHIVE_SIZE_LIMIT')
                hasher.update(raw)
                snapshot.write(raw)
            require(archive_size > 0, 'ARCHIVE_EMPTY')
            snapshot.seek(0)
            members: dict[str, tuple[str, int]] = {}
            json_members: dict[str, bytes] = {}
            names: set[str] = set()
            retained = {'manifest.json', 'index.json', 'oci-layout', config_digest[7:] + '.json',
                        'blobs/sha256/' + config_digest[7:], 'blobs/sha256/' + root_digest[7:],
                        'blobs/sha256/' + selected_digest[7:]}
            with tarfile.open(fileobj=snapshot, mode='r|', tarinfo=BoundedTarInfo) as tar:
                for member in tar:
                    name = safe_name(member.name)
                    require(name not in names and len(names) < MAX_MEMBERS, 'ARCHIVE_DUPLICATE_OR_MEMBER_LIMIT')
                    names.add(name)
                    require((member.isdir() or member.isfile()) and not member.issparse(),
                            'ARCHIVE_LINK_OR_SPECIAL_MEMBER')
                    if member.isdir():
                        continue
                    require(0 <= member.size <= MAX_MEMBER_BYTES, 'ARCHIVE_MEMBER_SIZE_LIMIT')
                    handle = tar.extractfile(member)
                    require(handle is not None, 'ARCHIVE_MEMBER_UNREADABLE')
                    # Only bounded JSON candidates are retained; layers are streamed.
                    if name in retained:
                        require(member.size <= release.MAX_JSON_BYTES, 'ARCHIVE_JSON_SIZE_LIMIT')
                        raw = handle.read(release.MAX_JSON_BYTES + 1)
                        member_digest, member_size = digest(raw), len(raw)
                        json_members[name] = raw
                    else:
                        member_digest, member_size = stream_hash(handle, MAX_MEMBER_BYTES)
                    require(member_size == member.size, 'ARCHIVE_TRUNCATED_MEMBER')
                    if name.startswith('blobs/sha256/'):
                        require(name == 'blobs/sha256/' + member_digest[7:], 'ARCHIVE_BLOB_DIGEST_MISMATCH')
                    members[name] = member_digest, member_size
            require('manifest.json' in json_members, 'DOCKER_MANIFEST_MISSING')
            native = archive_manifest(json_members['manifest.json'])
            config_path = safe_name(native.get('Config'))
            layer_paths = native.get('Layers')
            require(isinstance(layer_paths, list) and len(layer_paths) == len(layers), 'DOCKER_LAYER_SCOPE_MISMATCH')
            layer_paths = [safe_name(item) for item in layer_paths]
            require(len(set(layer_paths)) == len(layer_paths), 'DOCKER_LAYER_DUPLICATE')
            require(members.get(config_path) == (config_digest, config_size), 'CONFIG_BYTES_MISMATCH')
            require(config_path in json_members, 'CONFIG_JSON_MISSING')
            config = release.parse_json_bytes(json_members[config_path])
            require({key: config.get(key, '') for key in wanted} == wanted, 'CONFIG_PLATFORM_MISMATCH')
            rootfs = config.get('rootfs')
            require(isinstance(rootfs, dict) and rootfs.get('type') == 'layers'
                    and isinstance(rootfs.get('diff_ids'), list) and len(rootfs['diff_ids']) == len(layers)
                    and all(isinstance(value, str) and DIGEST.fullmatch(value) for value in rootfs['diff_ids']),
                    'CONFIG_DIFF_ID_SCOPE_INVALID')
            oci = 'oci-layout' in json_members or 'index.json' in json_members
            if oci:
                require(release.parse_json_bytes(json_members.get('oci-layout', b''))
                        == {'imageLayoutVersion': '1.0.0'}, 'OCI_LAYOUT_INVALID')
                # The wrapper index may add tag annotations. Its exact referenced
                # root blob must still be the independently supplied subject.
                wrapper = release.parse_json_bytes(json_members.get('index.json', b''))
                require(type(wrapper.get('schemaVersion')) is int and wrapper['schemaVersion'] == 2
                        and isinstance(wrapper.get('manifests'), list)
                        and len(wrapper['manifests']) == 1, 'OCI_WRAPPER_SCOPE_INVALID')
                wrapper_digest, wrapper_size = descriptor(wrapper['manifests'][0], INDEX_TYPES | MANIFEST_TYPES)
                require((wrapper_digest, wrapper_size) == (root_digest, len(top_payload)), 'OCI_WRAPPER_SUBJECT_MISMATCH')
                for entry_digest, payload in ((root_digest, top_payload), (selected_digest, selected_payload)):
                    require(json_members.get('blobs/sha256/' + entry_digest[7:]) == payload,
                            'OCI_REGISTRY_GRAPH_MISMATCH')
                require(config_path == 'blobs/sha256/' + config_digest[7:], 'OCI_CONFIG_PATH_MISMATCH')
                for path, (entry_digest, entry_size) in zip(layer_paths, layer_descriptors):
                    require(path == 'blobs/sha256/' + entry_digest[7:]
                            and members.get(path) == (entry_digest, entry_size), 'OCI_LAYER_BYTES_MISMATCH')
            else:
                require(config_path == config_digest[7:] + '.json', 'LEGACY_CONFIG_PATH_MISMATCH')
                require(all(path.endswith('/layer.tar') for path in layer_paths), 'LEGACY_LAYER_PATH_INVALID')

            expanded = 0
            diff_hashes: dict[str, str] = {}
            snapshot.seek(0)
            with tarfile.open(fileobj=snapshot, mode='r|', tarinfo=BoundedTarInfo) as tar:
                for member in tar:
                    name = member.name.rstrip('/')
                    if name not in layer_paths:
                        continue
                    index = layer_paths.index(name)
                    handle = tar.extractfile(member)
                    require(handle is not None, 'LAYER_BYTES_MISSING')
                    if oci and layers[index]['mediaType'] in GZIP_LAYERS:
                        handle = gzip.GzipFile(fileobj=handle)
                    layer_hash, layer_size = stream_hash(handle, MAX_EXPANDED_BYTES - expanded)
                    expanded += layer_size
                    diff_hashes[name] = layer_hash
            require([diff_hashes.get(path) for path in layer_paths] == rootfs['diff_ids'],
                    'LAYER_DIFF_ID_MISMATCH')
            return {
                'schema': SCHEMA, 'subject': subject, 'subjectDigest': root_digest,
                'subjectMediaType': top['mediaType'], 'platformManifestDigest': selected_digest,
                'configurationDigest': config_digest, 'platform': wanted,
                'configurationDiffIds': rootfs['diff_ids'],
                'archiveSha256': hasher.hexdigest(), 'archiveSizeBytes': archive_size,
                'archiveRetention': 'RUNNER_ONLY_NOT_UPLOADED',
                'archiveKind': 'DOCKER_OCI_EXPORT' if oci else 'DOCKER_LEGACY_EXPORT',
                'graphConsistency': 'MATCHED', 'layerCount': len(layers),
                'layerBytesBinding': 'REGISTRY_BLOB_AND_CONFIG_DIFF_ID' if oci else 'CONFIG_DIFF_ID',
                'registryCompressedLayerBytesQualification': 'MATCHED' if oci else 'UNVERIFIED',
                'expandedLayerSizeBytes': expanded,
                'topManifestInputSha256': top_input_hash,
                'platformManifestInputSha256': platform_input_hash,
                'registryAuthentication': 'UNVERIFIED', 'scannerExecutionAuthentication': 'UNVERIFIED',
            }
    except (OSError, tarfile.TarError, EOFError, RecursionError, release.ReleaseEvidenceError):
        raise GraphError('ARCHIVE_OR_GRAPH_INPUT_INVALID') from None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--expected-subject', required=True)
    parser.add_argument('--expected-platform', required=True)
    parser.add_argument('--top-manifest', required=True)
    parser.add_argument('--select-platform', action='store_true')
    parser.add_argument('--platform-manifest')
    parser.add_argument('--archive')
    parser.add_argument('--output')
    args = parser.parse_args()
    try:
        require(all(value is None or len(value) <= 4096 for value in vars(args).values()
                    if not isinstance(value, bool)), 'CLI_INPUT_LIMIT')
        root_digest = subject_digest(args.expected_subject)
        wanted = platform(args.expected_platform)
        if args.select_platform:
            require(args.archive is None and args.platform_manifest is None and args.output is None,
                    'CLI_MODE_INVALID')
            top, _, _ = manifest_bytes(Path(args.top_manifest), root_digest)
            print(select_manifest(top, root_digest, wanted)[0])
        else:
            require(bool(args.archive and args.platform_manifest and args.output), 'CLI_MODE_INVALID')
            graph = resolve(Path(args.archive), Path(args.top_manifest), Path(args.platform_manifest),
                            args.expected_subject, args.expected_platform)
            with Path(args.output).open('xb') as handle:
                handle.write((json.dumps(graph, sort_keys=True, indent=2) + '\n').encode('utf-8'))
            print('SEC-14 release image graph=MATCHED authentication=UNVERIFIED')
        return 0
    except (GraphError, OSError) as error:
        code = str(error) if isinstance(error, GraphError) else 'GRAPH_INPUT_OR_OUTPUT_UNAVAILABLE'
        print(f'SEC-14 image graph error: {code}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
