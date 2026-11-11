"""Positive/negative controls for exact same-job sanitized Architecture receipts."""
from __future__ import annotations

import json
import os
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import architecture_evidence_pair as pair
import architecture_source_native_parity as parity

SHA = 'a' * 40
RUN = '42813652448'
NAME_DIGEST = 'b' * 64
LEGACY_DIGEST = 'c' * 64


def native_receipt() -> dict:
    """Create the exact existing native receipt schema without raw names."""
    return {
        'schema': 'coglatas.architecture-advisory-receipt.v1',
        'sourceSha': SHA, 'runId': RUN, 'runAttempt': 1,
        'caseCount': 13, 'actualCasesPassed': 13,
        'caseIdsSha256': LEGACY_DIGEST,
        'executionState': 'NATIVE_TRX_CONTENT_VERIFIED',
        'artifactTrust': 'EXTERNAL_VERIFICATION_REQUIRED',
        'requiredCheckQualified': False,
        'qualification': 'ADVISORY_ONLY_NOT_INDEPENDENT_PRODUCER_OR_ARTIFACT_ATTESTATION',
    }


def parity_receipt() -> dict:
    """Create a matching independently derived source/native name receipt."""
    return {
        'schema': 'coglatas.architecture-source-trx-name-parity.v1',
        'sourceShaClaim': SHA, 'runIdClaim': RUN, 'runAttemptClaim': 1,
        'caseCount': 13, 'sourceNameRosterSha256': NAME_DIGEST,
        'nativeNameRosterSha256': NAME_DIGEST,
        'nativeReceiptCaseIdsSha256': LEGACY_DIGEST,
        'result': 'SOURCE_TRX_NAME_SET_MATCH',
        'sourceEvidence': 'BOUNDED_TEXT_NOT_COMPILED_CSHARP',
        'nativeEvidence': 'LOCAL_TRX_CONTENT_ONLY',
        'sourceShaAuthenticated': False,
        'nativeSdkCaseIdsVerified': False,
        'artifactProvenanceVerified': False,
        'requiredCheckQualified': False,
    }


class ArchitectureEvidencePairTests(unittest.TestCase):
    """Prevent apparently green content from granting provenance authority."""

    def setUp(self) -> None:
        """Create a private directory for same-job synthetic JSON inputs."""
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)

    def test_success_has_no_provenance_claim(self) -> None:
        """Exact binding grants local content parity and no gate credit."""
        result = pair.verify(native_receipt(), parity_receipt(), SHA, RUN, 1)
        self.assertEqual(result['status'], 'SAME_JOB_RECEIPT_CONTENT_MATCH')
        self.assertEqual(result['caseCount'], 13)
        self.assertEqual(result['sourceNativeRosterSha256'], NAME_DIGEST)
        self.assertEqual(result['nativeReceiptNamesSha256'], LEGACY_DIGEST)
        for flag in ('producerAuthenticated', 'nativeSdkCaseIdsVerified',
                     'artifactProvenanceVerified', 'requiredCheckQualified'):
            self.assertIs(result[flag], False)
        self.assertNotIn('LayerDependencyTests', str(result))

    def test_metadata_mismatch_refused(self) -> None:
        """A foreign source, run or retry cannot be paired by digest alone."""
        for where, key, value in (
            ('native', 'sourceSha', 'f' * 40),
            ('parity', 'sourceShaClaim', 'f' * 40),
            ('native', 'runId', '222'),
            ('parity', 'runIdClaim', '222'),
            ('native', 'runAttempt', 2),
            ('parity', 'runAttemptClaim', 2),
        ):
            native, candidate = native_receipt(), parity_receipt()
            (native if where == 'native' else candidate)[key] = value
            with self.subTest(where=where, key=key):
                with self.assertRaises(pair.EvidencePairRefused) as err:
                    pair.verify(native, candidate, SHA, RUN, 1)
                self.assertEqual(err.exception.args[0], 'RECEIPT_IDENTITY')

    def test_distinct_source_and_native_digests_refused(self) -> None:
        """A mismatched name set cannot be replaced by a count of thirteen."""
        for key in ('sourceNameRosterSha256', 'nativeNameRosterSha256',
                    'nativeReceiptCaseIdsSha256'):
            candidate = parity_receipt()
            candidate[key] = 'd' * 64
            with self.subTest(key=key), self.assertRaises(pair.EvidencePairRefused) as err:
                pair.verify(native_receipt(), candidate, SHA, RUN, 1)
            self.assertEqual(err.exception.args[0], 'RECEIPT_DIGEST_MISMATCH')
        native = native_receipt()
        native['caseIdsSha256'] = 'e' * 64
        with self.assertRaises(pair.EvidencePairRefused):
            pair.verify(native, parity_receipt(), SHA, RUN, 1)

    def test_unknown_keys_or_schema_refused(self) -> None:
        """Extra fields and unsupported versions cannot gain acceptance."""
        for key in ('native', 'parity'):
            native, candidate = native_receipt(), parity_receipt()
            (native if key == 'native' else candidate)['trustedProducer'] = True
            with self.subTest(key=key), self.assertRaises(pair.EvidencePairRefused) as err:
                pair.verify(native, candidate, SHA, RUN, 1)
            self.assertEqual(err.exception.args[0], 'RECEIPT_SCHEMA_FIELDS')
        native = native_receipt()
        native['schema'] = 'coglatas.architecture-advisory-receipt.v2'
        with self.assertRaises(pair.EvidencePairRefused):
            pair.verify(native, parity_receipt(), SHA, RUN, 1)

    def test_malicious_success_and_boolean_count_refused(self) -> None:
        """True cannot masquerade as an integer count or authorized result."""
        for item, field, value in [
            ('native', 'caseCount', True),
            ('native', 'actualCasesPassed', 12),
            ('native', 'requiredCheckQualified', True),
            ('native', 'artifactTrust', 'VERIFIED'),
            ('parity', 'caseCount', 12),
            ('parity', 'result', 'PROVENANCE_VERIFIED'),
            ('parity', 'sourceShaAuthenticated', True),
            ('parity', 'artifactProvenanceVerified', True),
            ('parity', 'nativeSdkCaseIdsVerified', True),
        ]:
            native, candidate = native_receipt(), parity_receipt()
            (native if item == 'native' else candidate)[field] = value
            with self.subTest(item=item, field=field):
                with self.assertRaises(pair.EvidencePairRefused):
                    pair.verify(native, candidate, SHA, RUN, 1)

    def test_invalid_arguments_refused(self) -> None:
        """Malformed source, Run ID, and attempt arguments fail closed."""
        for sha, run, attempt in (
            (SHA[:-1], RUN, 1), (SHA, '0', 1), (SHA, RUN, 0),
            (SHA, RUN, True), (SHA, RUN, 1000001)
        ):
            with self.subTest(sha=sha, run=run, attempt=attempt):
                with self.assertRaises(pair.EvidencePairRefused):
                    pair.verify(native_receipt(), parity_receipt(), sha, run, attempt)

    def test_read_exact_bounded_reports(self) -> None:
        """Accept exactly named JSON objects from the same producing directory."""
        for filename, data in (
            (pair.NATIVE_FILE, native_receipt()),
            (pair.PARITY_FILE, parity_receipt())
        ):
            path = self.root / filename
            path.write_text(json.dumps(data), encoding='utf-8')
            self.assertEqual(pair.read_receipt(path, self.root, filename), data)
        self.assertEqual(pair.verify(
            pair.read_receipt(self.root / pair.NATIVE_FILE, self.root, pair.NATIVE_FILE),
            pair.read_receipt(self.root / pair.PARITY_FILE, self.root, pair.PARITY_FILE),
            SHA, RUN, 1
        )['caseCount'], 13)

    def test_duplicate_or_malformed_json_refused(self) -> None:
        """JSON duplicate keys, non-finite values and invalid bytes are rejected."""
        path = self.root / pair.NATIVE_FILE
        for content in (
            b'{"schema":"a","schema":"b"}', b'{"x":NaN}',
            b'\xff', b'garbage', b'[1,2]', b'{}' * 5000
        ):
            path.write_bytes(content)
            with self.subTest(length=len(content)):
                with self.assertRaises(pair.EvidencePairRefused):
                    pair.read_receipt(path, self.root, pair.NATIVE_FILE)

    def test_wrong_file_path_and_symlink_refused(self) -> None:
        """The receipt reader must not follow redirected file endpoints."""
        path = self.root / pair.NATIVE_FILE
        path.write_text(json.dumps(native_receipt()), encoding='utf-8')
        with self.assertRaises(pair.EvidencePairRefused):
            pair.read_receipt(path, self.root, pair.PARITY_FILE)

        # An equally sized replacement between lstat and open must not
        # borrow the identity of the file admitted by the first check.
        replacement = self.root / 'replacement.json'
        replacement.write_bytes(path.read_bytes())
        actual_open = os.open

        def swap_before_open(target, flags, *args, **kwargs):
            """Swap an already checked receipt pathname before descriptor opening."""
            if Path(target) == path:
                os.replace(replacement, path)
            return actual_open(target, flags, *args, **kwargs)

        with patch.object(pair.os, 'open', side_effect=swap_before_open):
            with self.assertRaises(pair.EvidencePairRefused) as failure:
                pair.read_receipt(path, self.root, pair.NATIVE_FILE)
        self.assertEqual(failure.exception.args[0], 'RECEIPT_FILE')

        link = self.root / pair.PARITY_FILE
        try:
            link.symlink_to(path)
        except (OSError, NotImplementedError):
            self.fail('Symlink control unavailable: cannot qualify the guard')
        with self.assertRaises(pair.EvidencePairRefused):
            pair.read_receipt(link, self.root, pair.PARITY_FILE)

    def test_parity_report_output_is_exclusive_and_scoped(self) -> None:
        """A source/native sidecar is created once and only in RUNNER_TEMP."""
        path = self.root / pair.PARITY_FILE
        with patch.dict(os.environ, {'RUNNER_TEMP': str(self.root)}):
            parity.write_report(parity_receipt(), path)
            self.assertEqual(pair.read_receipt(path, self.root, pair.PARITY_FILE),
                             parity_receipt())
            with self.assertRaises(parity.NameParityRefused):
                parity.write_report(parity_receipt(), path)
            with self.assertRaises(parity.NameParityRefused):
                parity.write_report(parity_receipt(), self.root / 'unreviewed.json')


if __name__ == '__main__':
    unittest.main()
