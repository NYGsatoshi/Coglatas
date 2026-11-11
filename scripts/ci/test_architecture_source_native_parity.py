"""Synthetic controls for C# Fact-source to native TRX test-name parity."""
from __future__ import annotations

import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET

import architecture_advisory as native
import architecture_source_native_parity as parity
import architecture_source_roster as source

SHA = 'a' * 40
RUN = '12345'


def csharp_source(klass: str, methods: list[str]) -> str:
    """Create supported source grammar with explicit, parameterless Facts."""
    cases = ''.join(f'    [Fact]\n    public void {method}()\n    {{\n    }}\n'
                    for method in methods)
    return ('using Xunit;\nnamespace Coglatas.Architecture.Tests;\n' +
            f'public sealed class {klass}\n{{\n{cases}}}\n')


def native_trx(names: list[str], outcome: str = 'Passed', duplicate_id: bool = False) -> bytes:
    """Create one bounded synthetic TRX run with all original counter keys."""
    root = ET.Element(native.NAMESPACE + 'TestRun')
    results = ET.SubElement(root, native.NAMESPACE + 'Results')
    for index, name in enumerate(names):
        ET.SubElement(results, native.NAMESPACE + 'UnitTestResult', {
            'testId': '1' if duplicate_id else str(index + 1), 'testName': name,
            'outcome': outcome if index == 0 else 'Passed',
        })
    summary = ET.SubElement(root, native.NAMESPACE + 'ResultSummary')
    keys = ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted',
            'inconclusive', 'notRunnable', 'notExecuted', 'disconnected', 'warning',
            'completed', 'inProgress', 'pending', 'passedButRunAborted')
    ET.SubElement(summary, native.NAMESPACE + 'Counters',
                  {key: str(len(names) if key in ('total', 'executed', 'passed') else 0)
                   for key in keys})
    return ET.tostring(root, encoding='utf-8', xml_declaration=True)


class ArchitectureSourceNativeParityTests(unittest.TestCase):
    """Deny equal-count substitutions and forbidden outcome/provenance claims."""

    def setUp(self) -> None:
        """Populate a fixed synthetic three-file Architecture source inventory."""
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        project = self.root / source.PROJECT
        project.mkdir(parents=True)
        self.files = {}
        for filename in source.PROFILES['WITH_NEGATIVE_ORACLE'][0]:
            klass = filename.removesuffix('.cs')
            methods = sorted(x.removeprefix(klass + '.')
                             for x in native.EXPECTED if x.startswith(klass + '.'))
            p = project / filename
            p.write_text(csharp_source(klass, methods), encoding='utf-8')
            self.files[klass] = p
        self.names = [native.PREFIX + name for name in sorted(native.EXPECTED)]
        self.raw = native_trx(self.names)

    def test_matching_source_and_native_names(self) -> None:
        """Matching all 13 names grants name-parity but not provenance."""
        report = parity.verify(self.root, self.raw, SHA, RUN, 1)
        self.assertEqual(report['result'], 'SOURCE_TRX_NAME_SET_MATCH')
        self.assertEqual(report['caseCount'], 13)
        self.assertEqual(report['sourceNameRosterSha256'], report['nativeNameRosterSha256'])
        for flag in ('sourceShaAuthenticated', 'nativeSdkCaseIdsVerified',
                     'artifactProvenanceVerified', 'requiredCheckQualified'):
            self.assertIs(report[flag], False)
        self.assertNotIn(self.names[0], str(report))
        self.assertEqual(report, parity.verify(self.root, self.raw, SHA, RUN, 1))

    def test_equal_count_source_rename_refuses(self) -> None:
        """A replaced source Fact cannot pass merely because counts remain 13."""
        file = self.files['LayerDependencyTests']
        existing = file.read_text(encoding='utf-8')
        method = sorted(name for name in native.EXPECTED if
                        name.startswith('LayerDependencyTests.'))[0].split('.')[1]
        signature = 'public void ' + method + '()'
        self.assertIn(signature, existing)
        file.write_text(existing.replace(signature, 'public void RenamedCase()', 1),
                        encoding='utf-8')
        with self.assertRaises(parity.NameParityRefused) as caught:
            parity.verify(self.root, self.raw, SHA, RUN, 1)
        self.assertEqual(caught.exception.args[0], 'CASE_NAME_SET_MISMATCH')

    def test_missing_or_unexpected_native_case_refuses(self) -> None:
        """The original TRX guard rejects missing and replaced result cases."""
        with self.assertRaises(native.ArchitectureEvidenceError):
            parity.verify(self.root, native_trx(self.names[:-1]), SHA, RUN, 1)
        altered = self.names.copy()
        altered[0] = native.PREFIX + 'OtherSuite.UnapprovedCase'
        with self.assertRaises(native.ArchitectureEvidenceError):
            parity.verify(self.root, native_trx(altered), SHA, RUN, 1)

    def test_duplicate_native_result_id_refuses(self) -> None:
        """Duplicate native result IDs never qualify."""
        with self.assertRaises(native.ArchitectureEvidenceError):
            parity.verify(self.root, native_trx(self.names, duplicate_id=True),
                          SHA, RUN, 1)

    def test_failures_and_skips_refuse(self) -> None:
        """Nonpassed outcomes cannot be hidden by a matching name set."""
        for status in ('Failed', 'Skipped', 'NotExecuted', 'Aborted'):
            with self.subTest(status=status):
                with self.assertRaises(native.ArchitectureEvidenceError):
                    parity.verify(self.root, native_trx(self.names, outcome=status),
                                  SHA, RUN, 1)

    def test_missing_negative_source_refuses(self) -> None:
        """Absent thirteenth source test is never an authorized twelve-case run."""
        self.files['LayerDependencyNegativeControls'].unlink()
        with self.assertRaises(source.SourceRosterError):
            parity.verify(self.root, self.raw, SHA, RUN, 1)

    def test_bad_identity_claims_refuse(self) -> None:
        """The underlying native reader still enforces SHA/run/attempt syntax."""
        for sha, run, attempt in ((SHA[:-1], RUN, 1), (SHA, '0', 1), (SHA, RUN, 0)):
            with self.subTest(sha=sha, run=run, attempt=attempt):
                with self.assertRaises(native.ArchitectureEvidenceError):
                    parity.verify(self.root, self.raw, sha, run, attempt)

    def test_xml_and_dtd_refuse(self) -> None:
        """The original XML bounds, encoding and DTD guards remain effective."""
        for raw in (b'', b'not xml', self.raw + b'extra',
                    self.raw.replace(b'?>', b'?>\n<!DOCTYPE root>\n', 1)):
            with self.subTest(size=len(raw)):
                with self.assertRaises(native.ArchitectureEvidenceError):
                    parity.verify(self.root, raw, SHA, RUN, 1)

    def test_direct_comparator_denies_duplicates_and_substitution(self) -> None:
        """Names, not just their number, must match source-derived hashes."""
        roster = source.discover(self.root, 'WITH_NEGATIVE_ORACLE')
        names = self.names.copy()
        names[0] = names[1]
        with self.assertRaises(parity.NameParityRefused) as caught:
            parity.reconcile_names(roster, names)
        self.assertEqual(caught.exception.args[0], 'NATIVE_NAME_DUPLICATE')
        names[0] = native.PREFIX + 'OtherSuite.UnapprovedCase'
        with self.assertRaises(parity.NameParityRefused) as caught:
            parity.reconcile_names(roster, names)
        self.assertEqual(caught.exception.args[0], 'CASE_NAME_SET_MISMATCH')

    def test_bounded_trx_file_and_symlink_refuse(self) -> None:
        """Only bounded regular bytes can enter the native evidence path."""
        path = self.root / 'native.trx'
        path.write_bytes(self.raw)
        self.assertEqual(parity.read_trx(path), self.raw)
        path.write_bytes(b'x' * (native.MAX_BYTES + 1))
        with self.assertRaises(parity.NameParityRefused):
            parity.read_trx(path)

        # Even a same-size replacement between lstat and open is not the
        # previously admitted TRX inode.
        path.write_bytes(self.raw)
        replacement = self.root / 'replacement.trx'
        replacement.write_bytes(self.raw)
        real_open = os.open

        def replace_before_open(target, flags, *args, **kwargs):
            if Path(target) == path:
                os.replace(replacement, path)
            return real_open(target, flags, *args, **kwargs)

        with mock.patch.object(native.os, 'open', side_effect=replace_before_open):
            with self.assertRaises(parity.NameParityRefused) as refused:
                parity.read_trx(path)
        self.assertEqual(refused.exception.args[0], 'TRX_INPUT_INVALID')

        link = self.root / 'linked.trx'
        try:
            link.symlink_to(path)
        except (OSError, NotImplementedError):
            self.fail('Symlink control unavailable: cannot qualify the guard')
        with self.assertRaises(parity.NameParityRefused):
            parity.read_trx(link)


if __name__ == '__main__':
    unittest.main()
