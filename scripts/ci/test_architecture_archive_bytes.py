"""Offline synthetic controls for hashed Architecture JSON ZIP admission."""
from __future__ import annotations

from io import BytesIO
import hashlib
import json
import os
from pathlib import Path
import stat
import tempfile
import unittest
from unittest.mock import patch
import warnings
import zipfile

import architecture_archive_bytes as reader

SHA = 'a' * 40
RUN = '42815795815'
ROSTER = 'b' * 64
NATIVE_DIGEST = 'c' * 64


def fixtures() -> dict[str, dict]:
    """Create both strictly versioned sanitized receipts."""
    return {
        'architecture-advisory-receipt.json': {
            'schema': 'coglatas.architecture-advisory-receipt.v1',
            'sourceSha': SHA, 'runId': RUN, 'runAttempt': 1,
            'caseCount': 13, 'actualCasesPassed': 13,
            'caseIdsSha256': NATIVE_DIGEST,
            'executionState': 'NATIVE_TRX_CONTENT_VERIFIED',
            'artifactTrust': 'EXTERNAL_VERIFICATION_REQUIRED',
            'requiredCheckQualified': False,
            'qualification': 'ADVISORY_ONLY_NOT_INDEPENDENT_PRODUCER_OR_ARTIFACT_ATTESTATION',
        },
        'architecture-source-native-parity.json': {
            'schema': 'coglatas.architecture-source-trx-name-parity.v1',
            'sourceShaClaim': SHA, 'runIdClaim': RUN, 'runAttemptClaim': 1,
            'caseCount': 13, 'sourceNameRosterSha256': ROSTER,
            'nativeNameRosterSha256': ROSTER,
            'nativeReceiptCaseIdsSha256': NATIVE_DIGEST,
            'result': 'SOURCE_TRX_NAME_SET_MATCH',
            'sourceEvidence': 'BOUNDED_TEXT_NOT_COMPILED_CSHARP',
            'nativeEvidence': 'LOCAL_TRX_CONTENT_ONLY',
            'sourceShaAuthenticated': False, 'nativeSdkCaseIdsVerified': False,
            'artifactProvenanceVerified': False, 'requiredCheckQualified': False,
        }
    }


def pack(data: dict[str, object] | None = None,
         *, filenames: list[str] | None = None,
         symlink_member: bool = False) -> bytes:
    """Construct an in-memory small ZIP for deterministic mutation tests."""
    items = data if data is not None else fixtures()
    names = list(items) if filenames is None else filenames
    buf = BytesIO()
    with zipfile.ZipFile(buf, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for index, name in enumerate(names):
            payload = items.get(name, fixtures()['architecture-advisory-receipt.json'])
            text = payload if isinstance(payload, bytes) else json.dumps(payload).encode('utf-8')
            member = zipfile.ZipInfo(name)
            member.compress_type = zipfile.ZIP_DEFLATED
            member.create_system = 3
            member.external_attr = ((stat.S_IFLNK if symlink_member and index == 0
                                     else stat.S_IFREG) | 0o600) << 16
            with warnings.catch_warnings():
                warnings.simplefilter('ignore', UserWarning)
                archive.writestr(member, text)
    return buf.getvalue()


def check(raw: bytes, **changed: object) -> dict:
    """Inspect one ZIP with explicitly supplied independent expectation values."""
    kwargs = dict(api_digest=hashlib.sha256(raw).hexdigest(), api_size=len(raw),
                  tested_merge_sha=SHA, run_id=RUN, attempt=1,
                  independently_expected_roster_digest=ROSTER)
    kwargs.update(changed)
    return reader.verify(raw, **kwargs)


class ArchitectureArchiveBytesTests(unittest.TestCase):
    """No false-green from altered bytes, names, identities, or report claims."""

    def test_valid_bounded_zip_matches_without_provenance_claim(self) -> None:
        """A positive local byte match never claims source/producer authority."""
        z = pack()
        result = check(z)
        self.assertEqual(result['result'], 'ARCHIVE_BYTES_AND_PAIR_CONTENT_MATCH')
        self.assertEqual(result['caseCount'], 13)
        self.assertEqual(result['testedMergeShaClaim'], SHA)
        for key in ('gitHubMetadataAuthenticated', 'producerAuthenticated',
                    'nativeSdkCaseIdsVerified', 'artifactProvenanceVerified',
                    'requiredCheckQualified'):
            self.assertIs(result[key], False)
        self.assertEqual(check(z), result)

    def test_mutated_zip_and_size_refuse(self) -> None:
        """Byte tampering cannot reuse independently supplied digest/size pins."""
        z = pack()
        for other in (z[:-1] + b'X', z + b'extra'):
            with self.subTest(size=len(other)), self.assertRaises(reader.ArchiveRefused):
                check(other, api_digest=hashlib.sha256(z).hexdigest(), api_size=len(z))
        with self.assertRaises(reader.ArchiveRefused):
            check(z, api_size=len(z) + 1)

    def test_wrong_merge_run_and_attempt_refuse(self) -> None:
        """Candidate report identities must agree with all operator pins."""
        z = pack()
        for key, value in [('tested_merge_sha', 'f' * 40), ('run_id', '123'),
                           ('attempt', 2), ('attempt', True),
                           ('independently_expected_roster_digest', 'e' * 64)]:
            with self.subTest(key=key), self.assertRaises(reader.ArchiveRefused):
                check(z, **{key: value})

    def test_foreign_receipt_versus_pinned_merge_refuses(self) -> None:
        """Correct ZIP SHA cannot validate mismatched internal source claims."""
        modified = fixtures()
        modified['architecture-advisory-receipt.json']['sourceSha'] = 'f' * 40
        with self.assertRaises(reader.ArchiveRefused):
            check(pack(modified))

    def test_equal_counts_but_name_digests_differ_refuses(self) -> None:
        """Count-preserving name drift cannot satisfy pair and source digests."""
        for field in ('sourceNameRosterSha256', 'nativeNameRosterSha256',
                      'nativeReceiptCaseIdsSha256'):
            modified = fixtures()
            modified['architecture-source-native-parity.json'][field] = 'd' * 64
            with self.subTest(field=field), self.assertRaises(reader.ArchiveRefused):
                check(pack(modified))

    def test_unauthorized_qualification_flags_refuse(self) -> None:
        """Reports cannot self-assert trusted gate, artifact, or SDK ID status."""
        for field in ('sourceShaAuthenticated', 'nativeSdkCaseIdsVerified',
                      'artifactProvenanceVerified', 'requiredCheckQualified'):
            modified = fixtures()
            modified['architecture-source-native-parity.json'][field] = True
            with self.subTest(field=field), self.assertRaises(reader.ArchiveRefused):
                check(pack(modified))
        modified = fixtures()
        modified['architecture-advisory-receipt.json']['requiredCheckQualified'] = True
        with self.assertRaises(reader.ArchiveRefused):
            check(pack(modified))

    def test_extra_or_duplicate_zip_member_refuses(self) -> None:
        """ZIP archives must contain exactly the two named JSON documents."""
        with self.assertRaises(reader.ArchiveRefused):
            check(pack(filenames=list(fixtures()) + ['stray.json']))
        with self.assertRaises(reader.ArchiveRefused):
            check(pack(filenames=[reader.FILES.__iter__().__next__()] * 2))
        with self.assertRaises(reader.ArchiveRefused):
            check(pack(filenames=['../evil.json', 'architecture-advisory-receipt.json']))

    def test_symlink_entry_and_bad_zip_refuse(self) -> None:
        """No linked member or malformed/trailing ZIP archive may be accepted."""
        with self.assertRaises(reader.ArchiveRefused):
            check(pack(symlink_member=True))
        for raw in (b'not zip', pack() + b'trailing'):
            with self.subTest(length=len(raw)), self.assertRaises(reader.ArchiveRefused):
                check(raw)

    def test_json_duplicate_keys_unknown_fields_nonfinite_refuse(self) -> None:
        """Only strict JSON schemas without hidden duplicate keys may pass."""
        for payload in (b'{"schema":"x","schema":"y"}', b'{"x":NaN}', b'{"x":1}',
                        b'not json', b'\xff'):
            records = fixtures()
            records['architecture-advisory-receipt.json'] = payload
            with self.subTest(payload=payload[:30]), self.assertRaises(reader.ArchiveRefused):
                check(pack(records))

    def test_boolean_count_invalid_fields_and_no_valid_report(self) -> None:
        """One boolean count or unexpected field must invalidate a receipt."""
        for key, value in [('caseCount', True), ('actualCasesPassed', 12),
                           ('qualification', 'SUCCESS')]:
            records = fixtures()
            records['architecture-advisory-receipt.json'][key] = value
            with self.subTest(key=key), self.assertRaises(reader.ArchiveRefused):
                check(pack(records))
        records = fixtures()
        records['architecture-advisory-receipt.json']['extra'] = True
        with self.assertRaises(reader.ArchiveRefused):
            check(pack(records))

    def test_regular_file_reader_rejects_symlink_and_oversize(self) -> None:
        """Reject oversized, replaced, mutated, or symlinked archive leaves."""
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            good = root / 'actual.zip'
            raw = pack()
            good.write_bytes(raw)
            self.assertEqual(reader.read_bounded(good), raw)
            good.write_bytes(b'x' * (reader.MAX_ARCHIVE + 1))
            with self.assertRaises(reader.ArchiveRefused):
                reader.read_bounded(good)

            # Identical byte length cannot authorize a different inode.
            good.write_bytes(raw)
            replacement_zip = root / 'replacement.zip'
            replacement_zip.write_bytes(raw)
            real_open = os.open

            def swap_before_open(target, flags, *args, **kwargs):
                """Replace the checked ZIP name before descriptor acquisition."""
                if Path(target) == good:
                    os.replace(replacement_zip, good)
                return real_open(target, flags, *args, **kwargs)

            with patch.object(reader.os, 'open', side_effect=swap_before_open):
                with self.assertRaises(reader.ArchiveRefused) as refused:
                    reader.read_bounded(good)
            self.assertEqual(refused.exception.args[0], 'ARCHIVE_FILE')

            # An in-place metadata mutation during a descriptor read also fails.
            good.write_bytes(raw)
            real_fstat = os.fstat
            fstat_calls = 0

            def change_metadata_on_post_read(fd):
                """Mutate the actual open archive after its first file check."""
                nonlocal fstat_calls
                fstat_calls += 1
                if fstat_calls == 2:
                    original_info = real_fstat(fd)
                    os.utime(good, ns=(original_info.st_atime_ns,
                                       original_info.st_mtime_ns + 2_000_000_000))
                return real_fstat(fd)

            with patch.object(reader.os, 'fstat', side_effect=change_metadata_on_post_read):
                with self.assertRaises(reader.ArchiveRefused) as refused:
                    reader.read_bounded(good)
            self.assertEqual(refused.exception.args[0], 'ARCHIVE_FILE')
            self.assertEqual(fstat_calls, 2)

            symlink = root / 'link.zip'
            try:
                symlink.symlink_to(good)
            except (OSError, NotImplementedError):
                self.fail('Cannot qualify this symlink guard on the current platform')
            with self.assertRaises(reader.ArchiveRefused):
                reader.read_bounded(symlink)

    def test_expected_roster_pin_must_be_hex_and_not_zero(self) -> None:
        """Malformed expected roster digest is never accepted silently."""
        z = pack()
        for value in ('X' * 64, '', 123, None):
            with self.subTest(value=value), self.assertRaises(reader.ArchiveRefused):
                check(z, independently_expected_roster_digest=value)


if __name__ == '__main__':
    unittest.main()
