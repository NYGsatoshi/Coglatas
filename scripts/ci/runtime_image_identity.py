#!/usr/bin/env python3
"""Bind a single uncompressed OCI image graph to its unchanged producer config ID."""
from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import re
import subprocess
import sys
import tarfile
from pathlib import Path, PurePosixPath

DIGEST = re.compile(r"sha256:[0-9a-f]{64}\Z")
MANIFEST = "application/vnd.oci.image.manifest.v1+json"
CONFIG = "application/vnd.oci.image.config.v1+json"
LAYER = "application/vnd.oci.image.layer.v1.tar"
INDEX = "application/vnd.oci.image.index.v1+json"
SMALL_LIMIT = 1024 * 1024
EMPTY_CONTAINER_CONFIG = dict(Hostname="", Domainname="", User="", AttachStdin=False, AttachStdout=False,
                              AttachStderr=False, Tty=False, OpenStdin=False, StdinOnce=False, Env=None,
                              Cmd=None, Image="", Volumes=None, WorkingDir="", Entrypoint=None, OnBuild=None, Labels=None)
CONTAINER_FIELD_ORDER = tuple(EMPTY_CONTAINER_CONFIG)[:6] + ("ExposedPorts",) + tuple(EMPTY_CONTAINER_CONFIG)[6:]


class IdentityError(ValueError):
    """A finite, non-content-bearing verification failure."""


class StrictTarInfo(tarfile.TarInfo):
    def _proc_member(self, archive):
        # Reject extension/sparse headers before TarInfo can read their payload
        # or turn hidden metadata into an apparently ordinary returned member.
        require(self.type in (tarfile.REGTYPE, tarfile.AREGTYPE, tarfile.DIRTYPE) and
                0 <= self.size <= 8 * 1024**3 and
                (self.type != tarfile.DIRTYPE or self.size == 0), "Unsupported raw image archive header.")
        return super()._proc_member(archive)

    @classmethod
    def fromtarfile(cls, archive):
        try:
            return super().fromtarfile(archive)
        except tarfile.EOFHeaderError:
            archive.identity_end_seen = True
            raise
        except tarfile.HeaderError as error:
            raise IdentityError("Invalid image archive header.") from error


def require(condition, message):
    if not condition:
        raise IdentityError(message)


def unique_object(pairs):
    result = {}
    for name, value in pairs:
        require(name not in result, "Duplicate identity JSON field.")
        result[name] = value
    return result


def parse_json(content):
    require(content is not None, "Identity JSON is missing or oversized.")
    try:
        return json.loads(content, object_pairs_hook=unique_object, parse_constant=invalid_constant)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise IdentityError("Invalid identity JSON.") from error


def invalid_constant(_value):
    raise IdentityError("Non-finite identity JSON value.")


def digest_file(path):
    value = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            value.update(block)
    return "sha256:" + value.hexdigest()


def archive_members(path):
    members, names, retained, total_size = {}, set(), 0, 0
    # GzipFile validates trailers and concatenated members; tarfile's stream
    # decompressor stops at the first gzip member without that validation.
    with gzip.open(path, "rb") as compressed, tarfile.open(fileobj=compressed, mode="r|", tarinfo=StrictTarInfo) as archive:
        for member in archive:
            name = member.name.rstrip("/")
            parts = PurePosixPath(name).parts
            require(name and not name.startswith("/") and "\\" not in name and
                    all(part not in (".", "..") for part in parts) and name not in names,
                    "Unsafe or duplicate image archive member.")
            names.add(name)
            require(len(names) <= 4096, "Image archive member bound exceeded.")
            if member.isdir():
                require(name in ("blobs", "blobs/sha256"), "Unsupported image archive directory.")
                continue
            require(member.isfile() and 0 <= member.size <= 8 * 1024**3,
                    "Unsupported image archive member.")
            total_size += member.size
            require(total_size <= 16 * 1024**3, "Image archive byte bound exceeded.")
            blob = re.fullmatch(r"blobs/sha256/([0-9a-f]{64})", name)
            require(blob is not None or name in ("index.json", "manifest.json", "oci-layout", "repositories"),
                    "Unsupported image archive path.")
            source = archive.extractfile(member)
            require(source is not None, "Image archive member is unavailable.")
            digest, chunks, length = hashlib.sha256(), [], 0
            for block in iter(lambda: source.read(1024 * 1024), b""):
                digest.update(block)
                length += len(block)
                if member.size <= SMALL_LIMIT:
                    chunks.append(block)
            require(length == member.size, "Image archive member size mismatch.")
            actual = "sha256:" + digest.hexdigest()
            require(blob is None or actual == "sha256:" + blob[1], "Image blob digest mismatch.")
            content = b"".join(chunks) if member.size <= SMALL_LIMIT else None
            retained += len(content or b"")
            require(retained <= 32 * SMALL_LIMIT, "Identity JSON retention bound exceeded.")
            members[name] = {"digest": actual, "size": length, "content": content}
        require(getattr(archive, "identity_end_seen", False), "Image archive has no end marker.")
        # TarFile stops at the first zero header; every remaining decompressed
        # byte must be zero padding, and GzipFile must validate the full stream.
        padding = 0
        for block in iter(lambda: archive.fileobj.read(1024 * 1024), b""):
            require(not block.strip(b"\0"), "Image archive has nonzero trailing bytes.")
            padding += len(block)
            require(archive.fileobj.tell() <= 16 * 1024**3, "Image archive byte bound exceeded.")
        require(padding >= tarfile.BLOCKSIZE, "Image archive end marker is truncated.")
    return members


def descriptor_member(descriptor, members, media_type):
    require(isinstance(descriptor, dict) and descriptor.get("mediaType") == media_type,
            "Unsupported image descriptor.")
    digest, size = descriptor.get("digest"), descriptor.get("size")
    require(isinstance(digest, str) and DIGEST.fullmatch(digest) is not None and
            type(size) is int and size >= 0, "Invalid image descriptor identity.")
    name = "blobs/sha256/" + digest.removeprefix("sha256:")
    member = members.get(name)
    require(member is not None and member["digest"] == digest and member["size"] == size,
            "Image descriptor bytes do not match.")
    return name, member


def typed_equal(left, right):
    return json.dumps(left, sort_keys=True) == json.dumps(right, sort_keys=True)


def go_json(value):
    text = json.dumps(value, separators=(",", ":"), ensure_ascii=False, allow_nan=False)
    for character, escaped in (("&", "\\u0026"), ("<", "\\u003c"), (">", "\\u003e"),
                               ("\u2028", "\\u2028"), ("\u2029", "\\u2029")):
        text = text.replace(character, escaped)
    return text.encode("utf-8")


def sorted_maps(value):
    if isinstance(value, dict):
        return {key: sorted_maps(item) for key, item in sorted(value.items())}
    if isinstance(value, list):
        return [sorted_maps(item) for item in value]
    return value


def v1_identifier(document, chain_id, parent_id, top):
    # Moby 89c5e8fd66634b6128fc4c0e6f1236e2540e46e0:
    # image/v1/imagev1.go:44-68 (SHA256 73dc429bb823e30d14ea25e2e9c852aaa7786f0db8b2f5c905c98f4fb9dc62b3).
    # Sort the outer RawMessage map; preserve nested field order and Go HTML escaping.
    value = {key: item for key, item in sorted(document.items()) if key != "id" and (top or key != "os")}
    value["layer_id"] = chain_id
    if parent_id:
        value["parent"] = "sha256:" + parent_id
    return hashlib.sha256(go_json(dict(sorted(value.items())))).hexdigest()


def legacy_auxiliaries(members, graph, legacy, config, image_name, layers):
    extra = set(members) - graph
    if not extra:
        require(set(legacy) == {"Config", "RepoTags", "Layers"}, "Unsupported legacy image manifest.")
        return []
    require("repositories" in extra and len(extra) == len(layers) + 1 and
            set(legacy) == {"Config", "RepoTags", "Layers", "LayerSources"}, "Unsupported legacy image graph.")
    require(typed_equal(legacy["LayerSources"], dict(zip(config["rootfs"]["diff_ids"], layers))),
            "Legacy layer sources differ from authenticated layers.")
    repository, tag = image_name.split(":")
    require(parse_json(members["repositories"]["content"]) ==
            {repository: {tag: config["rootfs"]["diff_ids"][-1].removeprefix("sha256:")}},
            "Legacy repository differs from authenticated image.")
    require(isinstance(config.get("config"), dict) and
            set(config["config"]) <= set(EMPTY_CONTAINER_CONFIG) | {"ExposedPorts"} and
            isinstance(config.get("created"), str), "Unsupported legacy config projection.")
    # Reconstruct bytes, rather than accepting arbitrary auxiliary JSON/IDs.
    # Moby same commit: image/tarexport/save.go:438-472 and
    # api/types/container/config.go:44-73 (SHA256 08df9dce6147eb2a4edb9859d81fedbdb13f2af2e249db38bf79361f67db4bcf).
    projected = EMPTY_CONTAINER_CONFIG | config["config"]
    projected = {key: sorted_maps(projected[key]) for key in CONTAINER_FIELD_ORDER if key in projected}
    parent_id, chain_id, proof, expected_names = "", "", [], {"repositories"}
    for index, diff_id in enumerate(config["rootfs"]["diff_ids"]):
        top = index == len(layers) - 1
        document = {"created": config["created"] if top else "1970-01-01T00:00:00Z",
                    "container_config": EMPTY_CONTAINER_CONFIG}
        if top:
            document |= {"config": projected, "architecture": "amd64", "os": "linux"}
        chain_id = diff_id if not chain_id else "sha256:" + hashlib.sha256((chain_id + " " + diff_id).encode()).hexdigest()
        identity = v1_identifier(document, chain_id, parent_id, top)
        saved = {"id": identity}
        if parent_id:
            saved["parent"] = parent_id
        saved |= document
        saved["os"] = "linux"
        content = go_json(saved)
        digest = "sha256:" + hashlib.sha256(content).hexdigest()
        name = "blobs/sha256/" + digest.removeprefix("sha256:")
        require(name in extra and members[name]["digest"] == digest and members[name]["size"] == len(content) and
                members[name]["content"] == content, "Legacy auxiliary bytes differ from authenticated projection.")
        expected_names.add(name)
        parent_id = identity
        proof.append({"digest": digest, "size": len(content)})
    require(extra == expected_names, "Legacy image graph contains unexpected metadata.")
    proof.append({"digest": members["repositories"]["digest"], "size": members["repositories"]["size"]})
    return proof


def archive_binding(archive, source_sha, image_name, producer_id):
    require(image_name in ("coglatas-main-runtime:" + source_sha, "coglatas-pr-functional:" + source_sha),
            "Invalid source-derived image tag.")
    return _archive_binding(archive, source_sha, image_name, producer_id)


def sonar_measurement_archive_binding(archive, source_sha, image_name, producer_id):
    """Separate noncanonical namespace; never admitted by archive_binding or its CLI."""
    require(image_name == "coglatas-sonar-actor-measurement:" + source_sha,
            "Invalid measurement-only image tag.")
    return _archive_binding(archive, source_sha, image_name, producer_id)


def _archive_binding(archive, source_sha, image_name, producer_id):
    require(re.fullmatch(r"[0-9a-f]{40}", source_sha) is not None and
            DIGEST.fullmatch(producer_id) is not None, "Invalid producer identity.")
    members = archive_members(archive)
    def document(name):
        return parse_json(members.get(name, {}).get("content"))
    require(document("oci-layout") == {"imageLayoutVersion": "1.0.0"}, "Unsupported OCI layout.")
    index = document("index.json")
    require(isinstance(index, dict) and type(index.get("schemaVersion")) is int and index["schemaVersion"] == 2 and
            index.get("mediaType", INDEX) == INDEX and
            isinstance(index.get("manifests"), list) and len(index["manifests"]) == 1,
            "Image index must select exactly one OCI manifest.")
    selected = index["manifests"][0]
    platform = selected.get("platform")
    require(platform is None or platform == {"architecture": "amd64", "os": "linux"},
            "Unsupported image index platform.")
    manifest_name, manifest_member = descriptor_member(selected, members, MANIFEST)
    manifest = parse_json(manifest_member["content"])
    require(isinstance(manifest, dict) and type(manifest.get("schemaVersion")) is int and manifest["schemaVersion"] == 2 and
            manifest.get("mediaType") == MANIFEST and not manifest.get("artifactType") and
            not manifest.get("subject"), "Unsupported runnable image manifest.")
    config_name, config_member = descriptor_member(manifest.get("config"), members, CONFIG)
    require(config_member["digest"] == producer_id, "Image config differs from producer identity.")
    config = parse_json(config_member["content"])
    require(isinstance(config, dict) and config.get("os") == "linux" and
            config.get("architecture") == "amd64" and config.get("variant") in (None, ""),
            "Unsupported image config platform.")
    rootfs, layers = config.get("rootfs"), manifest.get("layers")
    require(isinstance(rootfs, dict) and rootfs.get("type") == "layers" and
            isinstance(rootfs.get("diff_ids"), list) and isinstance(layers, list) and
            0 < len(layers) == len(rootfs["diff_ids"]) <= 128, "Invalid image layer graph.")
    layer_names, layer_proof = [], []
    for layer, diff_id in zip(layers, rootfs["diff_ids"]):
        # Only Docker-save's observed uncompressed layer representation is supported.
        name, member = descriptor_member(layer, members, LAYER)
        require(member["digest"] == diff_id, "Image layer order or DiffID mismatch.")
        layer_names.append(name)
        layer_proof.append({"digest": member["digest"], "size": member["size"], "mediaType": LAYER})
    legacy = document("manifest.json")
    require(isinstance(legacy, list) and len(legacy) == 1 and isinstance(legacy[0], dict) and
            legacy[0].get("Config") == config_name and legacy[0].get("Layers") == layer_names and
            legacy[0].get("RepoTags") == [image_name], "Docker-save and OCI image graphs differ.")
    auxiliaries = legacy_auxiliaries(members,
        {"index.json", "manifest.json", "oci-layout", manifest_name, config_name, *layer_names},
        legacy[0], config, image_name, layers)
    return {"schemaVersion": 1, "format": "single-oci-manifest-containerd", "sourceSha": source_sha,
            "verifierSha256": digest_file(Path(__file__)),
            "imageName": image_name, "producerImageId": producer_id,
            "engineImageId": manifest_member["digest"], "manifestSize": manifest_member["size"],
            "archiveSha256": digest_file(archive), "archiveSize": archive.stat().st_size,
            "platform": {"os": "linux", "architecture": "amd64"},
            "rootfsDiffIds": rootfs["diff_ids"], "layers": layer_proof, "legacyAuxiliaries": auxiliaries}


def verify_loaded(binding):
    result = subprocess.run(["docker", "image", "inspect", "--format", "{{json .}}", binding["imageName"]],
                            capture_output=True, check=False)
    require(result.returncode == 0 and len(result.stdout) <= 2 * SMALL_LIMIT,
            "Loaded image metadata is unavailable.")
    image = parse_json(result.stdout)
    require(isinstance(image, dict), "Invalid loaded image metadata.")
    descriptor = image.get("Descriptor")
    require(isinstance(descriptor, dict) and image.get("Id") == binding["engineImageId"] and
            descriptor.get("digest") == binding["engineImageId"] and descriptor.get("mediaType") == MANIFEST and
            type(descriptor.get("size")) is int and descriptor["size"] == binding["manifestSize"],
            "Loaded image descriptor differs from archive.")
    require(isinstance(image.get("RepoTags"), list) and binding["imageName"] in image["RepoTags"] and image.get("Os") == "linux" and
            image.get("Architecture") == "amd64" and image.get("Variant") in (None, "") and
            image.get("RootFS", {}).get("Type") == "layers" and
            image.get("RootFS", {}).get("Layers") == binding["rootfsDiffIds"],
            "Loaded image tag, platform or filesystem differs from archive.")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("restore", "verify"))
    for name in ("archive", "source-sha", "image-name", "producer-id", "engine-id", "binding", "dotnet-stamp"):
        parser.add_argument("--" + name, required=True)
    args = parser.parse_args()
    try:
        binding_path, archive = Path(args.binding), Path(args.archive)
        require(archive.is_file() and not archive.is_symlink(), "Runtime archive must be a regular file.")
        require(binding_path.absolute() == binding_path.resolve(), "Image binding path is redirected.")
        stamps = {archive.with_name("source-sha"): args.source_sha,
                  archive.with_name("runtime-image-name"): args.image_name,
                  archive.with_name("runtime-image-id"): args.producer_id,
                  Path(args.dotnet_stamp): args.source_sha}
        for stamp, expected in stamps.items():
            require(stamp.is_file() and not stamp.is_symlink() and stamp.stat().st_size <= 1024,
                    "Producer identity stamp is unavailable.")
            require(stamp.read_text(encoding="ascii").replace("\r", "").replace("\n", "") == expected,
                    "Producer identity stamp differs from expected source.")
        binding = archive_binding(archive, args.source_sha, args.image_name, args.producer_id)
        binding["sourceStampSha256"] = digest_file(archive.with_name("source-sha"))
        binding["dotnetStampSha256"] = digest_file(Path(args.dotnet_stamp))
        require(binding["engineImageId"] == args.engine_id and args.engine_id != args.producer_id,
                "Unsupported image identity translation.")
        if args.mode == "verify":
            require(binding_path.is_file() and not binding_path.is_symlink() and binding_path.stat().st_size <= 65536,
                    "Verified image binding is unavailable.")
            require(json.dumps(parse_json(binding_path.read_bytes()), sort_keys=True) == json.dumps(binding, sort_keys=True),
                    "Image binding proof differs from archive.")
        verify_loaded(binding)
        if args.mode == "restore":
            binding_path.parent.mkdir(parents=True, exist_ok=True)
            require(not binding_path.is_symlink(), "Image binding path is redirected.")
            binding_path.write_text(json.dumps(binding, sort_keys=True) + "\n", encoding="utf-8")
        return 0
    except (ValueError, OSError, UnicodeError, tarfile.TarError, TypeError, KeyError, AttributeError,
            EOFError, RecursionError):
        print("Runtime image content binding failed.", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
