#!/usr/bin/env python3
"""Portable pure-data controls; no Docker, SDK, browser or subprocess is invoked."""
import copy
import importlib.util
import unittest
from pathlib import Path

from runtime_image_identity import IdentityError, archive_binding, sonar_measurement_archive_binding
from sonar_actor_image_identity import loaded_identity

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
        return sonar_measurement_archive_binding(value.archive, fixtures.SHA, value.image, value.producer)

    def test_canonical_namespace_remains_closed_and_measurement_namespace_is_exact(self):
        value = self.fixture
        with self.assertRaises(IdentityError):
            archive_binding(value.archive, fixtures.SHA, value.image, value.producer)
        for image in ("coglatas-main-runtime:" + fixtures.SHA, "coglatas-pr-functional:" + fixtures.SHA,
                      "coglatas-sonar-actor-measurement:" + "c" * 40, "other:" + fixtures.SHA):
            with self.subTest(image=image), self.assertRaises(IdentityError):
                sonar_measurement_archive_binding(value.archive, fixtures.SHA, image, value.producer)
        for prefix in ("coglatas-main-runtime:", "coglatas-pr-functional:"):
            value.image = prefix + fixtures.SHA
            value.build_archive()
            self.assertEqual(value.image, archive_binding(value.archive, fixtures.SHA, value.image, value.producer)["imageName"])

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


if __name__ == "__main__":
    unittest.main()
