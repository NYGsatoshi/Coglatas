#!/usr/bin/env python3
"""Bind two sanitized same-job Architecture receipts without granting provenance.

The local native TRX and source/native name reports must agree on exact
source/run/attempt claims and both independent name digests. No output claims
GitHub job, artifact bytes, SDK TestCase IDs, or protected-gate authority.
"""
from __future__ import annotations

import argparse
import hmac
import json
import os
from pathlib import Path
import re
import stat
import sys

MAX_REPORT_BYTES = 8192
NATIVE_FILE = 'architecture-advisory-receipt.json'
PARITY_FILE = 'architecture-source-native-parity.json'
NATIVE_KEYS = frozenset((
    'schema', 'sourceSha', 'runId', 'runAttempt', 'caseCount',
    'caseIdsSha256', 'actualCasesPassed', 'executionState',
    'artifactTrust', 'requiredCheckQualified', 'qualification',
))
PARITY_KEYS = frozenset((
    'schema', 'sourceShaClaim', 'runIdClaim', 'runAttemptClaim', 'caseCount',
    'sourceNameRosterSha256', 'nativeNameRosterSha256',
    'nativeReceiptCaseIdsSha256', 'result', 'sourceEvidence', 'nativeEvidence',
    'sourceShaAuthenticated', 'nativeSdkCaseIdsVerified',
    'artifactProvenanceVerified', 'requiredCheckQualified',
))


class EvidencePairRefused(ValueError):
    """Bounded refusal code that never contains candidate receipt content."""


def require(valid: bool, code: str) -> None:
    """Fail closed if a required field or comparison is invalid."""
    if not valid:
        raise EvidencePairRefused(code)


def unique_pairs(pairs: list[tuple[str, object]]) -> dict:
    """Reject ambiguous JSON mappings with duplicate keys."""
    result: dict = {}
    for key, value in pairs:
        require(key not in result, 'JSON_DUPLICATE_KEY')
        result[key] = value
    return result


def reject_nonfinite(_value: str) -> None:
    """Reject nonstandard NaN/Infinity JSON constants."""
    raise EvidencePairRefused('JSON_NONFINITE')


def read_receipt(path: Path, directory: Path, filename: str) -> dict:
    """Read a single owned, bounded regular-file JSON object from one directory."""
    require(type(filename) is str and filename in (NATIVE_FILE, PARITY_FILE),
            'RECEIPT_NAME')
    try:
        require(path.name == filename and path.parent.resolve(strict=True) ==
                directory.resolve(strict=True), 'RECEIPT_PATH')
        metadata = path.lstat()
        require(stat.S_ISREG(metadata.st_mode) and not path.is_symlink() and
                0 < metadata.st_size <= MAX_REPORT_BYTES, 'RECEIPT_FILE')
        fd = os.open(path, os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0))
        try:
            actual = os.fstat(fd)
            require(stat.S_ISREG(actual.st_mode) and
                    actual.st_size == metadata.st_size and
                    actual.st_ino == metadata.st_ino and
                    actual.st_dev == metadata.st_dev, 'RECEIPT_FILE')
            with os.fdopen(fd, 'rb', closefd=False) as stream:
                raw = stream.read(MAX_REPORT_BYTES + 1)
        finally:
            os.close(fd)
    except OSError as exc:
        raise EvidencePairRefused('RECEIPT_FILE') from exc
    require(len(raw) == metadata.st_size and b'\x00' not in raw, 'RECEIPT_FILE')
    try:
        result = json.loads(raw.decode('utf-8'), object_pairs_hook=unique_pairs,
                            parse_constant=reject_nonfinite)
    except (UnicodeError, ValueError, TypeError) as exc:
        raise EvidencePairRefused('RECEIPT_JSON') from exc
    require(type(result) is dict, 'RECEIPT_JSON')
    return result


def verify(native: dict, parity: dict, sha: str, run_id: str,
           attempt: int) -> dict:
    """Require exact paired receipt identity, without trusting its producer claims."""
    require(type(native) is dict and type(parity) is dict, 'RECEIPT_SHAPE')
    require(set(native) == NATIVE_KEYS and set(parity) == PARITY_KEYS,
            'RECEIPT_SCHEMA_FIELDS')
    require(type(sha) is str and re.fullmatch(r'[a-f0-9]{40}', sha) is not None
            and type(run_id) is str and
            re.fullmatch(r'[1-9][0-9]{0,18}', run_id) is not None
            and type(attempt) is int and 1 <= attempt <= 1000000,
            'IDENTITY_ARGUMENTS')
    require(native['schema'] == 'coglatas.architecture-advisory-receipt.v1' and
            parity['schema'] == 'coglatas.architecture-source-trx-name-parity.v1',
            'RECEIPT_VERSION')
    require(native['sourceSha'] == parity['sourceShaClaim'] == sha and
            native['runId'] == parity['runIdClaim'] == run_id and
            type(native['runAttempt']) is int and
            type(parity['runAttemptClaim']) is int and
            native['runAttempt'] == parity['runAttemptClaim'] == attempt,
            'RECEIPT_IDENTITY')
    require(type(native['caseCount']) is int and
            type(native['actualCasesPassed']) is int and
            type(parity['caseCount']) is int and
            native['caseCount'] == native['actualCasesPassed'] ==
            parity['caseCount'] == 13, 'RECEIPT_CASE_COUNT')
    require(native['executionState'] == 'NATIVE_TRX_CONTENT_VERIFIED' and
            native['artifactTrust'] == 'EXTERNAL_VERIFICATION_REQUIRED' and
            native['qualification'] ==
            'ADVISORY_ONLY_NOT_INDEPENDENT_PRODUCER_OR_ARTIFACT_ATTESTATION' and
            native['requiredCheckQualified'] is False,
            'NATIVE_QUALIFICATION_CLAIM')
    require(parity['result'] == 'SOURCE_TRX_NAME_SET_MATCH' and
            parity['sourceEvidence'] == 'BOUNDED_TEXT_NOT_COMPILED_CSHARP' and
            parity['nativeEvidence'] == 'LOCAL_TRX_CONTENT_ONLY' and
            all(parity[name] is False for name in
                ('sourceShaAuthenticated', 'nativeSdkCaseIdsVerified',
                 'artifactProvenanceVerified', 'requiredCheckQualified')),
            'PARITY_QUALIFICATION_CLAIM')
    digest_names = ('sourceNameRosterSha256', 'nativeNameRosterSha256',
                    'nativeReceiptCaseIdsSha256')
    require(all(type(parity[k]) is str and
                re.fullmatch(r'[a-f0-9]{64}', parity[k]) is not None
                for k in digest_names) and
            type(native['caseIdsSha256']) is str and
            re.fullmatch(r'[a-f0-9]{64}', native['caseIdsSha256']) is not None,
            'RECEIPT_DIGEST_SHAPE')
    require(hmac.compare_digest(parity['sourceNameRosterSha256'],
                                parity['nativeNameRosterSha256']) and
            hmac.compare_digest(native['caseIdsSha256'],
                                parity['nativeReceiptCaseIdsSha256']),
            'RECEIPT_DIGEST_MISMATCH')
    return {
        'schema': 'coglatas.architecture-advisory-pair.v1',
        'caseCount': 13, 'sourceShaClaim': sha,
        'runIdClaim': run_id, 'runAttemptClaim': attempt,
        'sourceNativeRosterSha256': parity['sourceNameRosterSha256'],
        'nativeReceiptNamesSha256': native['caseIdsSha256'],
        'status': 'SAME_JOB_RECEIPT_CONTENT_MATCH',
        'producerAuthenticated': False,
        'nativeSdkCaseIdsVerified': False,
        'artifactProvenanceVerified': False,
        'requiredCheckQualified': False,
    }


def main() -> int:
    """Compare two same-directory receipts and report sanitized content match."""
    p = argparse.ArgumentParser()
    p.add_argument('--native', type=Path, required=True)
    p.add_argument('--parity', type=Path, required=True)
    p.add_argument('--sha', required=True)
    p.add_argument('--run-id', required=True)
    p.add_argument('--attempt', type=int, required=True)
    args = p.parse_args()
    try:
        temp = Path(os.environ['RUNNER_TEMP']).resolve(strict=True)
        first = read_receipt(args.native, temp, NATIVE_FILE)
        second = read_receipt(args.parity, temp, PARITY_FILE)
        result = verify(first, second, args.sha, args.run_id, args.attempt)
        print(json.dumps(result, sort_keys=True, separators=(',', ':')))
        return 0
    except (EvidencePairRefused, OSError, KeyError, ValueError, TypeError):
        print('ARCHITECTURE_EVIDENCE_PAIR_REFUSED', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
