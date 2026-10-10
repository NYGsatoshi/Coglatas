"""Synthetic native-format archive bytes for mechanical graph controls only."""

import gzip
import io
import json
import tarfile
from pathlib import Path

import release_image_graph as graph


def json_bytes(document):
    return json.dumps(document, sort_keys=True, separators=(',', ':')).encode('utf-8')


def tar_bytes(files):
    result = io.BytesIO()
    with tarfile.open(fileobj=result, mode='w') as tar:
        for name, raw in files.items():
            member = tarfile.TarInfo(name)
            member.size = len(raw)
            tar.addfile(member, io.BytesIO(raw))
    return result.getvalue()


class ImageFixture:
    def __init__(self, *, legacy=False, direct=False, gzip_layer=True,
                 config_platform=None, config_diff_id=None):
        self.legacy = legacy
        self.layer = tar_bytes({'app.txt': b'isolated graph fixture\n'})
        self.layer_blob = gzip.compress(self.layer, mtime=0) if gzip_layer else self.layer
        self.config = {'os': 'linux', 'architecture': 'amd64', 'rootfs': {
            'type': 'layers', 'diff_ids': [config_diff_id or graph.digest(self.layer)]}}
        self.config.update(config_platform or {})
        self.config_raw = json_bytes(self.config)
        self.config_digest = graph.digest(self.config_raw)
        self.manifest = {
            'schemaVersion': 2, 'mediaType': 'application/vnd.oci.image.manifest.v1+json',
            'config': {'mediaType': 'application/vnd.oci.image.config.v1+json',
                       'digest': self.config_digest, 'size': len(self.config_raw)},
            'layers': [{'mediaType': 'application/vnd.oci.image.layer.v1.tar+gzip' if gzip_layer
                        else 'application/vnd.oci.image.layer.v1.tar',
                        'digest': graph.digest(self.layer_blob), 'size': len(self.layer_blob)}],
        }
        self.manifest_raw = json_bytes(self.manifest)
        self.manifest_digest = graph.digest(self.manifest_raw)
        self.top = self.manifest if direct else {
            'schemaVersion': 2, 'mediaType': 'application/vnd.oci.image.index.v1+json',
            'manifests': [{'mediaType': self.manifest['mediaType'], 'digest': self.manifest_digest,
                           'size': len(self.manifest_raw), 'platform': {'os': 'linux', 'architecture': 'amd64'}}],
        }
        self.top_raw = json_bytes(self.top)
        self.subject_digest = graph.digest(self.top_raw)
        self.subject = 'ghcr.io/nygsatoshi/coglatas@' + self.subject_digest
        if legacy:
            self.config_path = self.config_digest[7:] + '.json'
            self.layer_path = 'layer-fixture/layer.tar'
            self.files = {self.config_path: self.config_raw, self.layer_path: self.layer}
        else:
            self.config_path = 'blobs/sha256/' + self.config_digest[7:]
            self.layer_path = 'blobs/sha256/' + graph.digest(self.layer_blob)[7:]
            self.files = {
                self.config_path: self.config_raw, self.layer_path: self.layer_blob,
                'blobs/sha256/' + self.manifest_digest[7:]: self.manifest_raw,
                'blobs/sha256/' + self.subject_digest[7:]: self.top_raw,
                'oci-layout': json_bytes({'imageLayoutVersion': '1.0.0'}),
                'index.json': json_bytes({'schemaVersion': 2, 'manifests': [{
                    'mediaType': self.top['mediaType'], 'digest': self.subject_digest, 'size': len(self.top_raw)}]}),
            }
        self.files['manifest.json'] = json_bytes([{
            'Config': self.config_path, 'Layers': [self.layer_path], 'RepoTags': ['fixture:isolated']}])

    def write(self, root: Path):
        (root / 'image.tar').write_bytes(tar_bytes(self.files))
        (root / 'registry-manifest.json').write_bytes(self.top_raw)
        (root / 'platform-manifest.json').write_bytes(self.manifest_raw)

    def resolve(self, root: Path):
        return graph.resolve(root / 'image.tar', root / 'registry-manifest.json',
                             root / 'platform-manifest.json', self.subject, 'linux/amd64')
