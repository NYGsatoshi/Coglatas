#!/usr/bin/env python3
"""Offline, bounded Architecture Artifact archive/report-byte admission.

The caller must independently supply GitHub API metadata. This module does not
fetch API authority or authenticate a GitHub producer, even on a content match.
"""
from __future__ import annotations

import argparse
from io import BytesIO
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
import zipfile
import zlib

MAX_ARCHIVE = 65536
MAX_MEMBER = 8192
FILES = frozenset({'architecture-advisory-receipt.json',
                   'architecture-source-native-parity.json'})
NATIVE_FIELDS = frozenset({
    'schema', 'sourceSha', 'runId', 'runAttempt', 'caseCount',
    'caseIdsSha256', 'actualCasesPassed', 'executionState',
    'artifactTrust', 'requiredCheckQualified', 'qualification',
})
PARITY_FIELDS = frozenset({
    'schema', 'sourceShaClaim', 'runIdClaim', 'runAttemptClaim',
    'caseCount', 'sourceNameRosterSha256', 'nativeNameRosterSha256',
    'nativeReceiptCaseIdsSha256', 'result', 'sourceEvidence',
    'nativeEvidence', 'sourceShaAuthenticated', 'nativeSdkCaseIdsVerified',
    'artifactProvenanceVerified', 'requiredCheckQualified',
})


class ArchiveRefused(ValueError):
    """Bounded fixed-code refusal, never untrusted archive content."""


def require(ok: bool, code: str) -> None:
    """Reject missing or unverified evidence."""
    if not ok:
        raise ArchiveRefused(code)


def hex_digest(value: object, digits: int) -> bool:
    """Check a strict lowercase hexadecimal digest or revision field."""
    return type(value) is str and re.fullmatch(r'[a-f0-9]{' + str(digits) + '}', value) is not None


def unique_object(pairs: list[tuple[str, object]]) -> dict:
    """Refuse ambiguous JSON duplicate properties."""
    output: dict = {}
    for key, value in pairs:
        require(key not in output, 'DUPLICATE_JSON_KEY')
        output[key] = value
    return output


def no_constants(_value: str) -> None:
    """Reject nonstandard JSON NaN and Infinity tokens."""
    raise ArchiveRefused('NONFINITE_JSON')


def parse_document(raw: bytes, name: str) -> dict:
    """Parse a small strict JSON object with the exact native/parity schema."""
    require(type(raw) is bytes and 0 < len(raw) <= MAX_MEMBER, 'REPORT_SIZE')
    try:
        record = json.loads(raw.decode('utf-8'), object_pairs_hook=unique_object,
                            parse_constant=no_constants)
    except (UnicodeError, ValueError, TypeError) as exc:
        raise ArchiveRefused('REPORT_JSON') from exc
    required = NATIVE_FIELDS if name == 'architecture-advisory-receipt.json' else PARITY_FIELDS
    require(type(record) is dict and set(record) == required, 'REPORT_FIELDS')
    return record


def read_bounded(path: Path) -> bytes:
    """Read a bounded ZIP and revalidate its opened descriptor after reading."""
    try:
        absolute = path.absolute()
        current = absolute.parent
        while current != current.parent:
            require(not current.is_symlink(), 'ARCHIVE_PATH')
            current = current.parent
        info = path.lstat()
        require(stat.S_ISREG(info.st_mode) and not path.is_symlink() and
                0 < info.st_size <= MAX_ARCHIVE, 'ARCHIVE_FILE')
        # A replaced FIFO must never block between lstat and fstat.
        require(getattr(os, 'O_NONBLOCK', 0) != 0, 'ARCHIVE_FILE')
        fd = os.open(path, os.O_RDONLY | os.O_NONBLOCK | getattr(os, 'O_NOFOLLOW', 0))
        try:
            opened = os.fstat(fd)
            require(stat.S_ISREG(opened.st_mode) and opened.st_ino == info.st_ino
                    and opened.st_dev == info.st_dev and opened.st_size == info.st_size,
                    'ARCHIVE_FILE')
            with os.fdopen(fd, 'rb', closefd=False) as stream:
                raw = stream.read(MAX_ARCHIVE + 1)
            after = os.fstat(fd)
            require(os.path.samestat(opened, after) and
                    opened.st_size == after.st_size and
                    opened.st_mtime_ns == after.st_mtime_ns and
                    opened.st_ctime_ns == after.st_ctime_ns,
                    'ARCHIVE_FILE')
        finally:
            os.close(fd)
    except (OSError, ValueError) as exc:
        raise ArchiveRefused('ARCHIVE_FILE') from exc
    require(len(raw) == info.st_size, 'ARCHIVE_FILE')
    return raw


def unpack(raw: bytes, digest: str, size: int) -> dict[str, dict]:
    """Validate exact hashed ZIP bytes and read exactly two sanitized reports."""
    require(type(raw) is bytes and 0 < len(raw) <= MAX_ARCHIVE and
            type(size) is int and size == len(raw) and
            hex_digest(digest, 64), 'ARCHIVE_ARGUMENTS')
    require(hashlib.sha256(raw).hexdigest() == digest, 'ARCHIVE_DIGEST')
    require(raw[:4] == b'PK\x03\x04' and len(raw) >= 22 and
            raw[-22:-18] == b'PK\x05\x06' and raw[-2:] == b'\x00\x00',
            'ARCHIVE_FORMAT')
    try:
        with zipfile.ZipFile(BytesIO(raw), 'r') as z:
            entries = z.infolist()
            require(len(entries) == 2 and {x.filename for x in entries} == FILES,
                    'ARCHIVE_MEMBERS')
            require(not z.comment and min(x.header_offset for x in entries) == 0,
                    'ARCHIVE_FORMAT')
            reports = {}
            for member in entries:
                mode = member.external_attr >> 16
                require(not member.is_dir() and not member.flag_bits & 1 and
                        stat.S_IFMT(mode) in (0, stat.S_IFREG) and
                        member.compress_type in (zipfile.ZIP_STORED, zipfile.ZIP_DEFLATED)
                        and 0 < member.file_size <= MAX_MEMBER and
                        member.file_size <= 100 * max(member.compress_size, 1),
                        'ARCHIVE_MEMBER')
                body = z.read(member)
                require(len(body) == member.file_size, 'ARCHIVE_MEMBER')
                reports[member.filename] = parse_document(body, member.filename)
    except (zipfile.BadZipFile, zlib.error, RuntimeError, EOFError, OSError, ValueError) as exc:
        raise ArchiveRefused('ARCHIVE_FORMAT') from exc
    return reports


def verify(raw: bytes, *, api_digest: str, api_size: int,
           tested_merge_sha: str, run_id: str, attempt: int,
           independently_expected_roster_digest: str) -> dict:
    """Bind original two receipts to caller-pinned metadata without signing it."""
    require(hex_digest(tested_merge_sha, 40) and
            type(run_id) is str and re.fullmatch(r'[1-9][0-9]{0,18}', run_id) is not None and
            type(attempt) is int and 1 <= attempt <= 1000000 and
            hex_digest(independently_expected_roster_digest, 64), 'IDENTITY_ARGUMENTS')
    documents = unpack(raw, api_digest, api_size)
    n = documents['architecture-advisory-receipt.json']
    p = documents['architecture-source-native-parity.json']
    require(n['schema'] == 'coglatas.architecture-advisory-receipt.v1' and
            p['schema'] == 'coglatas.architecture-source-trx-name-parity.v1',
            'REPORT_VERSION')
    require(n['sourceSha'] == p['sourceShaClaim'] == tested_merge_sha and
            n['runId'] == p['runIdClaim'] == run_id and
            type(n['runAttempt']) is int and type(p['runAttemptClaim']) is int and
            n['runAttempt'] == p['runAttemptClaim'] == attempt, 'REPORT_IDENTITY')
    require(type(n['caseCount']) is int and type(n['actualCasesPassed']) is int and
            type(p['caseCount']) is int and n['caseCount'] == n['actualCasesPassed'] ==
            p['caseCount'] == 13, 'REPORT_CASE_COUNT')
    require(n['executionState'] == 'NATIVE_TRX_CONTENT_VERIFIED' and
            n['artifactTrust'] == 'EXTERNAL_VERIFICATION_REQUIRED' and
            n['qualification'] == 'ADVISORY_ONLY_NOT_INDEPENDENT_PRODUCER_OR_ARTIFACT_ATTESTATION' and
            n['requiredCheckQualified'] is False and
            p['result'] == 'SOURCE_TRX_NAME_SET_MATCH' and
            p['sourceEvidence'] == 'BOUNDED_TEXT_NOT_COMPILED_CSHARP' and
            p['nativeEvidence'] == 'LOCAL_TRX_CONTENT_ONLY' and
            all(p[f] is False for f in ('sourceShaAuthenticated', 'nativeSdkCaseIdsVerified',
                                        'artifactProvenanceVerified', 'requiredCheckQualified')),
            'REPORT_QUALIFICATION')
    digests = ('caseIdsSha256', 'sourceNameRosterSha256', 'nativeNameRosterSha256',
               'nativeReceiptCaseIdsSha256')
    observed = (n[digests[0]], p[digests[1]], p[digests[2]], p[digests[3]])
    require(all(hex_digest(v, 64) for v in observed), 'REPORT_DIGEST_SHAPE')
    require(observed[0] == observed[3] and observed[1] == observed[2] ==
            independently_expected_roster_digest, 'REPORT_DIGEST_MISMATCH')
    return {
        'schema': 'coglatas.architecture-archive-bytes.v1',
        'testedMergeShaClaim': tested_merge_sha, 'runIdClaim': run_id,
        'runAttemptClaim': attempt, 'caseCount': 13,
        'archiveSha256': api_digest,
        'sourceRosterSha256': independently_expected_roster_digest,
        'result': 'ARCHIVE_BYTES_AND_PAIR_CONTENT_MATCH',
        'gitHubMetadataAuthenticated': False,
        'producerAuthenticated': False, 'nativeSdkCaseIdsVerified': False,
        'artifactProvenanceVerified': False, 'requiredCheckQualified': False,
    }


def main() -> int:
    """Run the bounded offline ZIP comparison with externally supplied inputs."""
    parser = argparse.ArgumentParser()
    parser.add_argument('--archive', type=Path, required=True)
    parser.add_argument('--api-digest', required=True)
    parser.add_argument('--api-size', type=int, required=True)
    parser.add_argument('--tested-merge-sha', required=True)
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--attempt', type=int, required=True)
    parser.add_argument('--expected-roster-sha256', required=True)
    args = parser.parse_args()
    try:
        result = verify(read_bounded(args.archive), api_digest=args.api_digest,
                        api_size=args.api_size, tested_merge_sha=args.tested_merge_sha,
                        run_id=args.run_id, attempt=args.attempt,
                        independently_expected_roster_digest=args.expected_roster_sha256)
    except (ArchiveRefused, OSError, ValueError, KeyError, TypeError):
        print('ARCHITECTURE_ARCHIVE_BYTES_REFUSED', file=sys.stderr)
        return 1
    print(json.dumps(result, sort_keys=True, separators=(',', ':')))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
