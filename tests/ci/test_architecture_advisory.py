"""Positive and deliberate false-green regression controls for advisory TRX proof."""
from __future__ import annotations

import importlib.util
import os
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('architecture_advisory', Path(__file__).parents[2] / 'scripts/ci/architecture_advisory.py')
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

SHA = 'a' * 40
N = module.NAMESPACE


def trx(*, missing=False, duplicate=False, failed=False, incorrect_counters=False, extra=False, dtd=False):
    """Build a bounded synthetic native TRX with configurable negative outcomes."""
    root = ET.Element(N + 'TestRun')
    results = ET.SubElement(root, N + 'Results')
    names = sorted(module.EXPECTED)
    if missing:
        names.pop()
    if duplicate:
        names.append(names[0])
    if extra:
        names.append('UnexpectedTests.Unauthorized')
    for index, name in enumerate(names):
        ET.SubElement(results, N + 'UnitTestResult', {
            'testId': str(index), 'testName': module.PREFIX + name,
            'outcome': 'Failed' if failed and index == 0 else 'Passed',
        })
    summary = ET.SubElement(root, N + 'ResultSummary')
    keys = ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive',
            'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed',
            'inProgress', 'pending', 'passedButRunAborted')
    values = {key: str(len(names) if key in ('total', 'executed', 'passed') else 0) for key in keys}
    if incorrect_counters:
        values['notExecuted'] = '1'
    ET.SubElement(summary, N + 'Counters', values)
    xml = ET.tostring(root, encoding='utf-8', xml_declaration=True)
    if dtd:
        # Place the DTD after the XML declaration so the DTD-specific guard,
        # not the unrelated encoding guard, must reject the input.
        xml = xml.replace(b'?>', b'?>\n<!DOCTYPE foo [<!ENTITY x "evil">]>\n', 1)
    return xml


class ArchitectureAdvisoryTests(unittest.TestCase):
    def test_native_fixture_reconciles_exact_case_names(self):
        """Validate all thirteen original Architecture names without gate promotion."""
        result = module.verify(trx(), SHA, '123', 1)
        self.assertEqual(result['caseCount'], 13)
        self.assertFalse(result['requiredCheckQualified'])
        self.assertEqual(result['artifactTrust'], 'EXTERNAL_VERIFICATION_REQUIRED')

    def test_rejects_case_missing_or_duplicate_or_extra(self):
        """Reject missing, repeated and unauthorized native case identities."""
        for variant in ('missing', 'duplicate', 'extra'):
            with self.subTest(variant=variant), self.assertRaises(module.ArchitectureEvidenceError):
                module.verify(trx(**{variant: True}), SHA, '123', 1)

    def test_rejects_failure_and_counter_drift(self):
        """Prevent failed outcomes or false counters from qualifying a run."""
        for variant in ('failed', 'incorrect_counters'):
            with self.subTest(variant=variant), self.assertRaises(module.ArchitectureEvidenceError):
                module.verify(trx(**{variant: True}), SHA, '123', 1)

    def test_rejects_missing_xml_dtd_and_invalid_source(self):
        """Reject malformed encodings, DTD input and invalid source claims."""
        invalid = (
            (b'', 'TRX_SIZE'),
            (b'garbage', 'TRX_ENCODING'),
            (b"<?xml version='1.0' encoding='utf-8'?><broken", 'TRX_XML'),
            (trx(dtd=True), 'TRX_DTD'),
            (trx().replace(b'Passed', b'Skipped', 1), 'CASE_NOT_PASSED'),
            (b'\xff\xfe' + trx().decode('utf-8').encode('utf-16le'), 'TRX_ENCODING'),
            (trx().replace(b'<?xml', b'<?xm\x00l', 1), 'TRX_ENCODING'),
        )
        for data, code in invalid:
            with self.subTest(code=code):
                with self.assertRaises(module.ArchitectureEvidenceError) as caught:
                    module.verify(data, SHA, '123', 1)
                self.assertEqual(caught.exception.args[0], code)
        for kwargs in ({'source_sha': 'bad'}, {'run_id': '0'}, {'attempt': 0}):
            args = dict(source_sha=SHA, run_id='123', attempt=1)
            args.update(kwargs)
            with self.subTest(args=args), self.assertRaises(module.ArchitectureEvidenceError):
                module.verify(trx(), **args)

    def test_descriptor_read_uses_actual_bounded_trx_bytes(self):
        """Accept a regular TRX through the same validated descriptor."""
        source_bytes = trx()
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'architecture-tests.trx'
            path.write_bytes(source_bytes)
            self.assertEqual(module.read_trx_file(path), source_bytes)
            self.assertEqual(module.verify(module.read_trx_file(path), SHA, '123', 1)['actualCasesPassed'], 13)
            path.write_bytes(b'')
            with self.assertRaises(module.ArchitectureEvidenceError) as failure:
                module.read_trx_file(path)
            self.assertEqual(failure.exception.args[0], 'TRX_FILE')

    def test_descriptor_read_rejects_swapped_path_after_lstat(self):
        """An equal-sized path replacement cannot inherit the checked identity."""
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'architecture-tests.trx'
            replacement = Path(folder) / 'replacement.trx'
            source_bytes = trx()
            path.write_bytes(source_bytes)
            replacement.write_bytes(source_bytes)
            real_open = os.open

            def swap_before_open(target, flags, *args, **kwargs):
                """Replace the checked pathname before the actual descriptor opens."""
                if Path(target) == path:
                    os.replace(replacement, path)
                return real_open(target, flags, *args, **kwargs)

            with mock.patch.object(module.os, 'open', side_effect=swap_before_open):
                with self.assertRaises(module.ArchitectureEvidenceError) as failure:
                    module.read_trx_file(path)
            self.assertEqual(failure.exception.args[0], 'TRX_FILE')

    def test_descriptor_read_rejects_symlinked_leaf(self):
        """A symlink is never a qualifying native TRX leaf."""
        with tempfile.TemporaryDirectory() as folder:
            target = Path(folder) / 'actual.trx'
            link = Path(folder) / 'architecture-tests.trx'
            target.write_bytes(trx())
            try:
                link.symlink_to(target)
            except (OSError, NotImplementedError):
                self.fail('This advisory CI environment cannot qualify its symlink guard')
            with self.assertRaises(module.ArchitectureEvidenceError) as failure:
                module.read_trx_file(link)
            self.assertEqual(failure.exception.args[0], 'TRX_FILE')

    def test_native_utf8_bom_is_accepted(self):
        """Accept a single native UTF-8 BOM without weakening the encoding guard."""
        result = module.verify(b'\xef\xbb\xbf' + trx(), SHA, '123', 1)
        self.assertEqual(result['actualCasesPassed'], 13)

    def test_deterministic_identity_digest(self):
        """Require reproducible bounded hashes for the same native case set."""
        left = module.verify(trx(), SHA, '123', 1)
        right = module.verify(trx(), SHA, '123', 1)
        self.assertEqual(left, right)
        self.assertEqual(len(left['caseIdsSha256']), 64)


if __name__ == '__main__':
    unittest.main()
