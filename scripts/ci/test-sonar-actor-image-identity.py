#!/usr/bin/env python3
"""Portable pure-data controls; no Docker, SDK, browser or subprocess is invoked."""
import copy
import importlib.util
import io
import json
import sys
import unittest
from contextlib import redirect_stdout
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import patch

from runtime_image_identity import IdentityError, archive_binding, sonar_measurement_archive_binding
from sonar_actor_image_identity import loaded_identity, main, measurement_binding

spec = importlib.util.spec_from_file_location("existing_runtime_fixtures", Path(__file__).with_name("test-main-runtime-artifact.py"))
fixtures = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixtures)


class MeasurementIdentityTests(unittest.TestCase):
    def setUp(self):
        # Reuse the independently transcribed archive fixtures. Their fake CLI is never executed.
        self.fixture = fixtures.RuntimeImageIdentityTests()
        self.fixture.setUp()
        self.addCleanup(self.fixture.tearDown)
        self.fixture.image = "coglatas-sonar-actor-measurement:" + fixtures.SHA
        self.fixture.build_archive()

    def binding(self):
        value = self.fixture
        return sonar_measurement_archive_binding(value.archive, fixtures.SHA, value.image)

    def test_canonical_namespace_remains_closed_and_measurement_namespace_is_exact(self):
        value = self.fixture
        with self.assertRaises(IdentityError):
            archive_binding(value.archive, fixtures.SHA, value.image, value.producer)
        for image in ("coglatas-main-runtime:" + fixtures.SHA, "coglatas-pr-functional:" + fixtures.SHA,
                      "coglatas-sonar-actor-measurement:" + "c" * 40, "other:" + fixtures.SHA):
            with self.subTest(image=image), self.assertRaises(IdentityError):
                sonar_measurement_archive_binding(value.archive, fixtures.SHA, image)
        for prefix in ("coglatas-main-runtime:", "coglatas-pr-functional:"):
            value.image = prefix + fixtures.SHA
            value.build_archive()
            self.assertEqual(value.image, archive_binding(value.archive, fixtures.SHA, value.image, value.producer)["imageName"])
            for wrong_config in (value.engine, "sha256:" + "c" * 64):
                with self.assertRaises(IdentityError):
                    archive_binding(value.archive, fixtures.SHA, value.image, wrong_config)

    def test_both_engine_representations_require_the_same_validated_archive_graph(self):
        value, binding = self.fixture, self.binding()
        self.assertEqual(value.engine, loaded_identity(binding, value.metadata))
        config_engine = copy.deepcopy(value.metadata)
        config_engine["Id"] = value.producer
        del config_engine["Descriptor"]
        self.assertEqual(value.producer, loaded_identity(binding, config_engine))
        for key, replacement in (("Id", "sha256:" + "c" * 64), ("RepoTags", ["other:" + fixtures.SHA]),
                                 ("Architecture", "arm64"), ("Os", "windows"),
                                 ("RootFS", {"Type": "layers", "Layers": []}), ("Descriptor", {})):
            with self.subTest(key=key), self.assertRaises(IdentityError):
                loaded_identity(binding, value.metadata | {key: replacement})

    def test_observed_config_and_manifest_producers_are_distinct_from_derived_config(self):
        value = self.fixture
        config_engine = copy.deepcopy(value.metadata)
        config_engine["Id"] = value.producer
        del config_engine["Descriptor"]
        for observed in (value.producer, value.engine):
            for metadata in (config_engine, value.metadata):
                with self.subTest(observed=observed, loaded=metadata["Id"]):
                    proof = measurement_binding(value.archive, fixtures.SHA, value.image, observed, metadata)
                    self.assertEqual(value.producer, proof["archiveConfigImageId"])
                    self.assertEqual(observed, proof["producerObservedImageId"])
                    self.assertEqual(metadata["Id"], proof["loadedEngineImageId"])
                    self.assertFalse(proof["canonicalRuntime"])
        with self.assertRaises(IdentityError):
            measurement_binding(value.archive, fixtures.SHA, value.image, "sha256:" + "c" * 64, value.metadata)
        # Same tag and valid alternate graph cannot satisfy the original observed identity.
        original = value.producer
        value.marker += "-different-config"
        value.build_archive()
        with self.assertRaises(IdentityError):
            measurement_binding(value.archive, fixtures.SHA, value.image, original, value.metadata)
        with self.assertRaises(IdentityError):
            measurement_binding(value.archive, fixtures.SHA, value.image, value.producer,
                                value.metadata | {"RootFS": {"Type": "layers", "Layers": ["sha256:" + "d" * 64]}})

    def test_existing_archive_negative_controls_are_not_weakened_by_the_separate_profile(self):
        for change in ("platform", "index-platform", "order", "size", "compression", "multi", "index-size", "legacy-tag",
                       "legacy-order", "duplicate-json", "layer-bytes", "config-bytes", "missing", "extra-blob", "unsafe",
                       "duplicate-member", "symlink", "trailing-graph", "truncated-gzip", "invalid-crc", "trailing-garbage",
                       "trailing-gzip-garbage", "pax", "global-pax", "gnu-longname", "gnu-longlink", "gnu-sparse"):
            with self.subTest(change=change):
                self.fixture.build_archive(change)
                with self.assertRaises((ValueError, OSError, EOFError)):
                    self.binding()
                self.assertFalse((self.fixture.root / "outside").exists())

    def test_record_verify_rereads_observed_stamp_and_keeps_config_bound_without_executing_docker(self):
        value = self.fixture
        arguments = ["measurement", "record", "--archive", str(value.archive), "--source-sha", fixtures.SHA,
                     "--image-name", value.image, "--engine-id", value.engine,
                     "--binding", str(value.proof), "--dotnet-stamp", str(value.dotnet_stamp)]
        metadata = SimpleNamespace(returncode=0, stdout=json.dumps(value.metadata).encode())
        with patch("sonar_actor_image_identity.subprocess.run", return_value=metadata) as docker:
            with patch.object(sys, "argv", arguments):
                self.assertEqual(0, main())
            proof = json.loads(value.proof.read_text())
            self.assertEqual(value.producer, proof["producerObservedImageId"])
            self.assertEqual(value.producer, proof["archiveConfigImageId"])
            self.assertEqual(value.engine, proof["loadedEngineImageId"])
            arguments[1] = "verify"
            arguments += ["--config-id", value.producer]
            with patch.object(sys, "argv", arguments):
                self.assertEqual(0, main())
            self.assertEqual(["/usr/bin/docker", "image", "inspect", "--format", "{{json .}}", value.image], docker.call_args.args[0])
            for observed, config in ((value.engine, value.producer), (value.producer, value.engine)):
                (value.artifact / "runtime-image-id").write_text(observed + "\n")
                changed = arguments[:-1] + [config]
                output = io.StringIO()
                with patch.object(sys, "argv", changed), redirect_stdout(output):
                    self.assertEqual(1, main())
                self.assertEqual("Measurement-only runtime identity failed.\n", output.getvalue())
                self.assertNotIn(value.marker, output.getvalue())


if __name__ == "__main__":
    unittest.main()
