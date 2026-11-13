"""Synthetic positive and negative controls for bounded C# source inventory."""
from __future__ import annotations

import importlib.util
import hashlib
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock

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

    def test_lf_and_crlf_preserve_case_identity_and_raw_source_hashes(self) -> None:
        """Line endings may vary without normalizing the recorded source bytes."""
        for profile in ('CORE', 'WITH_NEGATIVE_ORACLE'):
            if profile == 'WITH_NEGATIVE_ORACLE':
                self.negative.write_bytes(fixture('LayerDependencyNegativeControls', 1).encode('utf-8'))
            filenames, _ = module.PROFILES[profile]
            raw_sources = {name: (self.project / name).read_bytes().replace(b'\r\n', b'\n')
                           for name in filenames}
            reports = []
            for ending in (b'\n', b'\r\n'):
                for name, raw in raw_sources.items():
                    (self.project / name).write_bytes(raw.replace(b'\n', ending))
                report = module.discover(self.root, profile)
                for name, raw in raw_sources.items():
                    self.assertEqual(report['sourceFileSha256'][name],
                                     hashlib.sha256(raw.replace(b'\n', ending)).hexdigest())
                reports.append(report)
            self.assertEqual(reports[0]['caseIdHashes'], reports[1]['caseIdHashes'])
            self.assertEqual(reports[0]['rosterSha256'], reports[1]['rosterSha256'])
            self.assertNotEqual(reports[0]['sourceFileSha256'], reports[1]['sourceFileSha256'])
        with self.assertRaises(module.SourceRosterError):
            module.parse_source(fixture('LayerDependencyTests', 6).replace('\n', '\r').encode('utf-8'),
                                'LayerDependencyTests')

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
        # Preserve the strict namespace controls already qualified in #1880:
        # comments and duplicate declarations cannot supply namespace authority.
        for declaration in (
            'namespace Other;\n// namespace Coglatas.Architecture.Tests;',
            'namespace Coglatas.Architecture.Tests;\nnamespace Other;',
        ):
            self.core.write_text(fixture('LayerDependencyTests', 6).replace(
                'namespace Coglatas.Architecture.Tests;', declaration), encoding='utf-8')
            with self.assertRaises(module.SourceRosterError) as failure:
                module.discover(self.root, 'CORE')
            self.assertEqual(failure.exception.args[0], 'SOURCE_NAMESPACE')

    def test_source_swap_between_check_and_open_is_denied(self) -> None:
        """A replacement leaf must not masquerade as the previously checked inode."""
        replacement = self.project / 'replacement.tmp'
        replacement.write_text(fixture('LayerDependencyTests', 6), encoding='utf-8')
        original_open = os.open

        def replace_then_open(path, flags, *args, **kwargs):
            """Swap the checked C# file before opening to test inode rejection."""
            if Path(path) == self.core:
                os.replace(replacement, self.core)
            return original_open(path, flags, *args, **kwargs)

        with mock.patch.object(module.os, 'open', side_effect=replace_then_open):
            with self.assertRaises(module.SourceRosterError) as caught:
                module.discover(self.root, 'CORE')
        self.assertEqual(caught.exception.args[0], 'SOURCE_FILE')

    def test_symlinked_source_is_denied(self) -> None:
        """Reject symlinked leaves; unsupported CI platforms must fail, never skip."""
        self.core.unlink()
        try:
            self.core.symlink_to(self.projectide)
        except (OSError, NotImplementedError):
            self.fail('Symlink control unavailable: this environment cannot qualify the guard')
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
