#!/usr/bin/env python3
"""Compare bounded C# Fact names against native TRX result names.

This is local content correlation, never GitHub producer authentication,
native SDK TestCase ID evidence, artifact provenance or Required Check status.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET

import architecture_advisory as native
import architecture_source_roster as source


class NameParityRefused(ValueError):
    """Finite local refusal code, without raw untrusted test data."""


def require(value: bool, code: str) -> None:
    """Reject incomplete or contradictory evidence."""
    if not value:
        raise NameParityRefused(code)


def read_trx(path: Path) -> bytes:
    """Reuse the native verifier's descriptor-bound TRX admission and bounded read."""
    try:
        return native.read_trx_file(path)
    except (native.ArchitectureEvidenceError, OSError, ValueError) as exc:
        raise NameParityRefused('TRX_INPUT_INVALID') from exc


def reconcile_names(roster: dict, names: list[str]) -> str:
    """Require exact name-set equality, not aggregate case count alone."""
    expected = roster.get('caseIdHashes')
    count = roster.get('caseCount')
    require(type(count) is int and type(expected) is list and type(names) is list
            and 0 < count <= source.MAX_CASES and len(expected) == count
            and len(names) == count, 'CASE_COUNT_MISMATCH')
    require(all(type(name) is str and name.startswith(native.PREFIX)
                and re.fullmatch(r'[A-Za-z0-9_.]+', name) is not None for name in names),
            'NATIVE_NAME_INVALID')
    require(len(names) == len(set(names)), 'NATIVE_NAME_DUPLICATE')
    hashes = sorted(hashlib.sha256(source.CASE_HASH_DOMAIN + name.encode('utf-8')).hexdigest()
                    for name in names)
    require(all(type(item) is str and re.fullmatch(r'[a-f0-9]{64}', item)
                for item in expected), 'SOURCE_DIGEST_INVALID')
    require(hashes == expected, 'CASE_NAME_SET_MISMATCH')
    return hashlib.sha256(('coglatas.architecture-source-roster.v1\n' +
                           '\n'.join(hashes)).encode('ascii')).hexdigest()


def verify(root: Path, raw: bytes, sha: str, run_id: str, attempt: int) -> dict:
    """Require original native truth guard and source-derived case-name parity."""
    native_receipt = native.verify(raw, sha, run_id, attempt)
    roster = source.discover(root, 'WITH_NEGATIVE_ORACLE')
    parsed = ET.fromstring(raw)
    names = [row.get('testName')
             for row in parsed.findall('./' + native.NAMESPACE + 'Results/' +
                                       native.NAMESPACE + 'UnitTestResult')]
    require(all(type(value) is str for value in names), 'NATIVE_NAME_INVALID')
    name_digest = reconcile_names(roster, names)
    require(native_receipt['caseCount'] == roster['caseCount']
            and name_digest == roster['rosterSha256'], 'ROSTER_DIGEST_MISMATCH')
    return {
        'schema': 'coglatas.architecture-source-trx-name-parity.v1',
        'sourceShaClaim': sha, 'runIdClaim': run_id, 'runAttemptClaim': attempt,
        'caseCount': roster['caseCount'],
        'sourceNameRosterSha256': roster['rosterSha256'],
        'nativeNameRosterSha256': name_digest,
        'result': 'SOURCE_TRX_NAME_SET_MATCH',
        'sourceEvidence': 'BOUNDED_TEXT_NOT_COMPILED_CSHARP',
        'nativeEvidence': 'LOCAL_TRX_CONTENT_ONLY',
        'sourceShaAuthenticated': False, 'nativeSdkCaseIdsVerified': False,
        'artifactProvenanceVerified': False, 'requiredCheckQualified': False,
    }


def main() -> int:
    """Emit only sanitized name parity or one fixed refusal message."""
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--trx', type=Path, required=True)
    parser.add_argument('--sha', required=True)
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--attempt', type=int, required=True)
    args = parser.parse_args()
    try:
        result = verify(args.root, read_trx(args.trx), args.sha, args.run_id, args.attempt)
    except (NameParityRefused, source.SourceRosterError,
            native.ArchitectureEvidenceError, OSError, ValueError, RecursionError):
        print('ARCHITECTURE_SOURCE_TRX_PARITY_REFUSED', file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True, separators=(',', ':')))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
