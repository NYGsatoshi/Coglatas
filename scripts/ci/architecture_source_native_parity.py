#!/usr/bin/env python3
"""Compare bounded C# Fact names against native TRX result names.

This is local content correlation, never GitHub producer authentication,
native SDK TestCase ID evidence, artifact provenance or Required Check status.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
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
    """Read one small regular TRX, rejecting leaf symlinks and file changes."""
    try:
        info = path.lstat()
        require(stat.S_ISREG(info.st_mode) and not path.is_symlink()
                and 0 < info.st_size <= native.MAX_BYTES, 'TRX_INPUT_INVALID')
        fd = os.open(path, os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0))
        try:
            opened = os.fstat(fd)
            require(stat.S_ISREG(opened.st_mode) and opened.st_size == info.st_size,
                    'TRX_INPUT_INVALID')
            with os.fdopen(fd, 'rb', closefd=False) as stream:
                raw = stream.read(native.MAX_BYTES + 1)
        finally:
            os.close(fd)
    except (OSError, ValueError) as exc:
        raise NameParityRefused('TRX_INPUT_INVALID') from exc
    require(len(raw) == info.st_size, 'TRX_INPUT_INVALID')
    return raw


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
        'nativeReceiptCaseIdsSha256': native_receipt['caseIdsSha256'],
        'result': 'SOURCE_TRX_NAME_SET_MATCH',
        'sourceEvidence': 'BOUNDED_TEXT_NOT_COMPILED_CSHARP',
        'nativeEvidence': 'LOCAL_TRX_CONTENT_ONLY',
        'sourceShaAuthenticated': False, 'nativeSdkCaseIdsVerified': False,
        'artifactProvenanceVerified': False, 'requiredCheckQualified': False,
    }


def write_report(report: dict, output: Path) -> None:
    """Create a sanitized sidecar exclusively in the producing runner's temp dir."""
    try:
        tmp = Path(os.environ['RUNNER_TEMP']).resolve(strict=True)
        valid = (output.name == 'architecture-source-native-parity.json'
                 and output.parent.resolve(strict=True) == tmp)
        require(valid, 'OUTPUT_PATH')
        fd = os.open(output, os.O_WRONLY | os.O_CREAT | os.O_EXCL
                     | getattr(os, 'O_NOFOLLOW', 0), 0o600)
        with os.fdopen(fd, 'w', encoding='utf-8') as stream:
            stream.write(json.dumps(report, sort_keys=True, separators=(',', ':')) + '\n')
    except (OSError, KeyError) as exc:
        raise NameParityRefused('OUTPUT_PATH') from exc


def main() -> int:
    """Emit only sanitized name parity or one fixed refusal message."""
    parser = argparse.ArgumentParser()
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--trx', type=Path, required=True)
    parser.add_argument('--sha', required=True)
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--attempt', type=int, required=True)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    try:
        result = verify(args.root, read_trx(args.trx), args.sha, args.run_id, args.attempt)
        if args.output is not None:
            write_report(result, args.output)
    except (NameParityRefused, source.SourceRosterError,
            native.ArchitectureEvidenceError, OSError, ValueError, RecursionError):
        print('ARCHITECTURE_SOURCE_TRX_PARITY_REFUSED', file=sys.stderr)
        return 1
    if args.output is None:
        print(json.dumps(result, sort_keys=True, separators=(',', ':')))
    else:
        print('ARCHITECTURE_SOURCE_TRX_PARITY_VERIFIED')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
