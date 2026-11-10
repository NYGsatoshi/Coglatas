"""Synthetic positive and negative controls for bounded C# source inventory."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('architecture_source_roster.py')
SPEC = importlib.util.spec_from_file_location('architecture_source_roster', SCRIPT)
assert SPEC is not None and SPEC.loader is not None
module = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(module)


def fixture(klass: str, count: int) -> str:
    """Produce a synthetic file using the exact supported C# declaration form."""
    methods = '\n'.join(f'    [Fact]\n    public void Case{i}()\n    {{\n    }}\n' for i in range(count))
    return f'using Xunit;\nnamespace Coglatas.Architecture.Tests;\npublic sealed class {klass}\n{{\n{methods}}}\n'


class ArchitectureSourceRosterTests(unittest.TestCase):
    """Reject incomplete or ambiguous source classifications without native credit."""

    def setUp(self) -> None:
        """Construct an isolated architecture project with the original twelve cases."""
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.project = self.root / module.PROJECT
        self.project.mkdir(parents=True)
        self.core = self.project / module.SOURCES[0]
        self.projectide = self.project / module.SOURCES[1]
        self.negative = self.project / module.OPTIONAL_NEGATIVE
        self.core.write_text(fixture('LayerDependencyTests', 6), encoding='utf-8')
        self.projectide.write_text(fixture('ProjectIdeBoundaryTests', 6), encoding='utf-8')

    def test_core_case_set_and_private_identity_digest(self) -> None:
        """The original twelve cases produce deterministic hashed IDs only."""
        a = module.discover(self.root, 'CORE')
        b = module.discover(self.root, 'CORE')
        self.assertEqual(a, b)
        self.assertEqual(a['caseCount'], 12)
        self.assertEqual(len(a['caseIdHashes']), 12)
        self.assertTrue(all(len(x) == 64 for x in a['caseIdHashes']))
        self.assertFalse(a['nativeCaseIdsVerified'])
        self.assertFalse(a['artifactProvenanceVerified'])
        self.assertFalse(a['requiredCheckQualified'])
        self.assertNotIn('LayerDependencyTests.Case0', str(a))

    def test_advisory_requires_thirteenth_source_case(self) -> None:
        """The negative-oracle profile rejects absent or extra source files."""
        with self.assertRaises(module.SourceRosterError) as caught:
            module.discover(self.root, 'WITH_NEGATIVE_ORACLE')
        self.assertEqual(caught.exception.args[0], 'SOURCE_FILE_SET')
        self.negative.write_text(fixture('LayerDependencyNegativeControls', 1), encoding='utf-8')
        result = module.discover(self.root, 'WITH_NEGATIVE_ORACLE')
        self.assertEqual(result['caseCount'], 13)
        with self.assertRaises(module.SourceRosterError):
            module.discover(self.root, 'CORE')

    def test_unexpected_source_or_duplicate_case_is_refused(self) -> None:
        """Additional files and method identity collisions must fail closed."""
        self.core.write_text(fixture('LayerDependencyTests', 5) +
                             '    [Fact]\n    public void Case0()\n    {}\n', encoding='utf-8')
        with self.assertRaises(module.SourceRosterError):
            module.discover(self.root, 'CORE')
        self.core.write_text(fixture('LayerDependencyTests', 6), encoding='utf-8')
        (self.project / 'SurpriseTests.cs').write_text(fixture('SurpriseTests', 1), encoding='utf-8')
        with self.assertRaises(module.SourceRosterError) as caught:
            module.discover(self.root, 'CORE')
        self.assertEqual(caught.exception.args[0], 'SOURCE_FILE_SET')

    def test_unsupported_attributes_and_signatures_refuse(self) -> None:
        """No dynamic, parameterized, or unknown Fact syntax is interpreted."""
        original = self.core.read_text()
        for candidate in [original.replace('[Fact]', '[Theory]', 1),
                          original.replace('public void Case0()', 'public void Case0(int x)', 1),
                          original.replace('[Fact]', '[Fact(Skip = "x")]', 1),
                          original.replace('using Xunit;', 'using System;', 1)]:
            self.core.write_text(candidate, encoding='utf-8')
            with self.subTest(candidate=candidate[:45]), self.assertRaises(module.SourceRosterError):
                module.discover(self.root, 'CORE')
        self.core.write_text(original, encoding='utf-8')

    def test_added_case_and_changed_namespace_refuse(self) -> None:
        """Counts and qualified namespace are both mandatory."""
        self.core.write_text(fixture('LayerDependencyTests', 7), encoding='utf-8')
        with self.assertRaises(module.SourceRosterError) as caught:
            module.discover(self.root, 'CORE')
        self.assertEqual(caught.exception.args[0], 'CASE_COUNT')
        self.core.write_text(fixture('LayerDependencyTests', 6).replace(
            'namespace Coglatas.Architecture.Tests;', 'namespace Other;'), encoding='utf-8')
        with self.assertRaises(module.SourceRosterError):
            module.discover(self.root, 'CORE')

    def test_symlinked_source_is_denied(self) -> None:
        """Do not follow a source-file symlink outside the pinned project."""
        self.core.unlink()
        try:
            self.core.symlink_to(self.projectide)
        except (OSError, NotImplementedError):
            self.skipTest('OS does not support this synthetic symlink control')
        with self.assertRaises(module.SourceRosterError):
            module.discover(self.root, 'CORE')

    def test_malformed_text_and_unknown_profile_are_denied(self) -> None:
        """NUL, invalid UTF-8, and unknown profiles cannot be source evidence."""
        for bad in [b'\x00', b'\xff']:
            self.core.write_bytes(bad)
            with self.assertRaises(module.SourceRosterError):
                module.discover(self.root, 'CORE')
        with self.assertRaises(module.SourceRosterError) as caught:
            module.discover(self.root, 'UNQUALIFIED')
        self.assertEqual(caught.exception.args[0], 'PROFILE')


if __name__ == '__main__':
    unittest.main()
