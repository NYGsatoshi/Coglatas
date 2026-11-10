"""Positive and deliberate false-green regression controls for advisory TRX proof."""
from __future__ import annotations

import importlib.util
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

spec = importlib.util.spec_from_file_location('architecture_advisory', Path(__file__).parents[2] / 'scripts/ci/architecture_advisory.py')
assert spec and spec.loader
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)

SHA = 'a' * 40
N = module.NAMESPACE


def trx(*, missing=False, duplicate=False, failed=False, incorrect_counters=False, extra=False, dtd=False):
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
        xml = b'<!DOCTYPE foo [<!ENTITY x "evil">]>' + xml
    return xml


class ArchitectureAdvisoryTests(unittest.TestCase):
    def test_native_fixture_reconciles_exact_case_names(self):
        result = module.verify(trx(), SHA, '123', 1)
        self.assertEqual(result['caseCount'], 13)
        self.assertFalse(result['requiredCheckQualified'])
        self.assertEqual(result['artifactTrust'], 'EXTERNAL_VERIFICATION_REQUIRED')

    def test_rejects_case_missing_or_duplicate_or_extra(self):
        for variant in ('missing', 'duplicate', 'extra'):
            with self.subTest(variant=variant), self.assertRaises(module.ArchitectureEvidenceError):
                module.verify(trx(**{variant: True}), SHA, '123', 1)

    def test_rejects_failure_and_counter_drift(self):
        for variant in ('failed', 'incorrect_counters'):
            with self.subTest(variant=variant), self.assertRaises(module.ArchitectureEvidenceError):
                module.verify(trx(**{variant: True}), SHA, '123', 1)

    def test_rejects_missing_xml_dtd_and_invalid_source(self):
        invalid = (
            (b'', 'TRX_SIZE'),
            (b'garbage', 'TRX_ENCODING'),
            (b"<?xml version='1.0' encoding='utf-8'?><broken", 'TRX_XML'),
            (trx(dtd=True), 'TRX_DTD'),
            (trx().replace(b'Passed', b'Skipped', 1), 'CASE_NOT_PASSED'),
            (b'\xff\xfe' + trx().decode('utf-8').encode('utf-16le'), 'TRX_ENCODING'),
            (trx().replace(b'<?xml', b'\xef\xbb\xbf<?xml', 1), 'TRX_ENCODING'),
            (trx().replace(b'<?xml', b'<?xm\x00l', 1), 'TRX_ENCODING'),
        )
        for data, code in invalid:
            with self.subTest(code=code), self.assertRaises(module.ArchitectureEvidenceError) as caught:
                module.verify(data, SHA, '123', 1)
            self.assertEqual(caught.exception.args[0], code)
        for kwargs in ({'source_sha': 'bad'}, {'run_id': '0'}, {'attempt': 0}):
            args = dict(source_sha=SHA, run_id='123', attempt=1)
            args.update(kwargs)
            with self.subTest(args=args), self.assertRaises(module.ArchitectureEvidenceError):
                module.verify(trx(), **args)

    def test_deterministic_identity_digest(self):
        left = module.verify(trx(), SHA, '123', 1)
        right = module.verify(trx(), SHA, '123', 1)
        self.assertEqual(left, right)
        self.assertEqual(len(left['caseIdsSha256']), 64)


if __name__ == '__main__':
    unittest.main()
