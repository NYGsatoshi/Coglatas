#!/usr/bin/env python3
"""Closed measurement-only image profile, separate from canonical runtime admission."""
import argparse
import json
import subprocess
import tarfile
from pathlib import Path

from runtime_image_identity import (DIGEST, MANIFEST, SMALL_LIMIT, digest_file, parse_json,
                                    require, sonar_measurement_archive_binding)


def loaded_identity(binding, image):
    """Prove both observed Docker identity representations against the same archive."""
    require(isinstance(image, dict), "Invalid measurement image metadata.")
    engine = image.get("Id")
    require(isinstance(engine, str) and DIGEST.fullmatch(engine) is not None and
            engine in (binding["producerImageId"], binding["engineImageId"]),
            "Measurement image identity differs from archive.")
    if engine == binding["engineImageId"]:
        descriptor = image.get("Descriptor", {})
        require(isinstance(descriptor, dict) and descriptor.get("digest") == engine and descriptor.get("mediaType") == MANIFEST and
                type(descriptor.get("size")) is int and descriptor["size"] == binding["manifestSize"],
                "Measurement image descriptor differs from archive.")
    require(isinstance(image.get("RepoTags"), list) and binding["imageName"] in image["RepoTags"] and
            image.get("Os") == "linux" and image.get("Architecture") == "amd64" and
            image.get("Variant") in (None, "") and image.get("RootFS", {}).get("Type") == "layers" and
            image.get("RootFS", {}).get("Layers") == binding["rootfsDiffIds"],
            "Measurement image tag, platform or filesystem differs from archive.")
    return engine


def measurement_binding(archive, source_sha, image_name, producer_observed_id, image):
    binding = sonar_measurement_archive_binding(archive, source_sha, image_name)
    require(isinstance(producer_observed_id, str) and DIGEST.fullmatch(producer_observed_id) is not None and
            producer_observed_id in (binding["producerImageId"], binding["engineImageId"]),
            "Observed producer identity differs from validated archive.")
    engine = loaded_identity(binding, image)
    binding.update(profile="SONAR_ACTOR_MEASUREMENT_ONLY", canonicalRuntime=False, deployable=False,
                   producerObservedImageId=producer_observed_id, archiveConfigImageId=binding["producerImageId"],
                   loadedEngineImageId=engine)
    return binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("mode", choices=("record", "verify"))
    for name in ("archive", "source-sha", "image-name", "engine-id", "binding", "dotnet-stamp"):
        parser.add_argument("--" + name, required=True)
    parser.add_argument("--config-id")
    args = parser.parse_args()
    try:
        archive, proof = Path(args.archive), Path(args.binding)
        require(archive.is_file() and not archive.is_symlink(), "Measurement archive is unavailable.")
        require(proof.absolute() == proof.resolve(), "Measurement binding is redirected.")
        for stamp, expected in ((archive.with_name("source-sha"), args.source_sha),
                                (archive.with_name("runtime-image-name"), args.image_name),
                                (Path(args.dotnet_stamp), args.source_sha)):
            require(stamp.is_file() and not stamp.is_symlink() and stamp.stat().st_size <= 1024 and
                    stamp.read_text(encoding="ascii").strip() == expected, "Measurement stamp differs.")
        observed_stamp = archive.with_name("runtime-image-id")
        require(observed_stamp.is_file() and not observed_stamp.is_symlink() and observed_stamp.stat().st_size <= 1024,
                "Observed producer stamp unavailable.")
        observed = observed_stamp.read_text(encoding="ascii").strip()
        result = subprocess.run(["/usr/bin/docker", "image", "inspect", "--format", "{{json .}}", args.image_name],
                                capture_output=True, check=False)
        require(result.returncode == 0 and len(result.stdout) <= 2 * SMALL_LIMIT,
                "Measurement image metadata unavailable.")
        binding = measurement_binding(archive, args.source_sha, args.image_name, observed, parse_json(result.stdout))
        require(binding["loadedEngineImageId"] == args.engine_id, "Measurement engine drift.")
        require((args.mode == "record" and args.config_id is None) or
                (args.mode == "verify" and args.config_id == binding["archiveConfigImageId"]),
                "Measurement config identity differs.")
        binding.update(measurementVerifierSha256=digest_file(Path(__file__)), observedStampSha256=digest_file(observed_stamp),
                       sourceStampSha256=digest_file(archive.with_name("source-sha")),
                       dotnetStampSha256=digest_file(Path(args.dotnet_stamp)))
        if args.mode == "record":
            require(not proof.exists(), "Measurement binding already exists.")
            with proof.open("x", encoding="utf-8") as output:
                output.write(json.dumps(binding, sort_keys=True) + "\n")
        else:
            require(proof.is_file() and not proof.is_symlink() and proof.stat().st_size <= 65536 and
                    parse_json(proof.read_bytes()) == binding, "Measurement binding differs from archive.")
        return 0
    except (ValueError, OSError, UnicodeError, TypeError, KeyError, AttributeError, EOFError, RecursionError, tarfile.TarError):
        print("Measurement-only runtime identity failed.")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
