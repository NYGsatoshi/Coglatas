"""Fail-closed advisory TRX check for the existing architecture test assembly."""
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

MAX_BYTES = 4 * 1024 * 1024
NAMESPACE = '{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'
PREFIX = 'Coglatas.Architecture.Tests.'
EXPECTED = frozenset({
    'LayerDependencyTests.Domain_DoesNotDependOnApplication',
    'LayerDependencyTests.Domain_DoesNotDependOnInfrastructure',
    'LayerDependencyTests.Domain_DoesNotDependOnWeb',
    'LayerDependencyTests.Application_DoesNotDependOnInfrastructure',
    'LayerDependencyTests.Application_DoesNotDependOnWeb',
    'LayerDependencyTests.Infrastructure_DoesNotDependOnWeb',
    'ProjectIdeBoundaryTests.ShadowSeamCannotObtainExistingMutationOrMergeServices',
    'ProjectIdeBoundaryTests.SecurityEvaluationEngineCannotDependOnStateOrTransportServices',
    'ProjectIdeBoundaryTests.SecurityFoundationContractsRemainInsideThePureSourceBoundary',
    'ProjectIdeBoundaryTests.BoundaryRuleDetectsAnIntentionallyForbiddenPlatformDependency',
    'ProjectIdeBoundaryTests.BoundaryRuleDetectsAnIntentionallyForbiddenEnvironmentDependency',
    'ProjectIdeBoundaryTests.ProductSourceCoreUsesOnlyPureBclAndOwnedTypes',
    'LayerDependencyNegativeControls.LayerRulesRejectSyntheticForbiddenReferences',
})


class ArchitectureEvidenceError(ValueError):
    """Finite refusal code, without untrusted TRX text."""


def require(ok: bool, code: str) -> None:
    if not ok:
        raise ArchitectureEvidenceError(code)


def verify(raw: bytes, source_sha: str, run_id: str, attempt: int) -> dict:
    """Validate actual outcome identities; metadata is not producer authentication."""
    require(type(raw) is bytes and 0 < len(raw) <= MAX_BYTES, 'TRX_SIZE')
    require(type(source_sha) is str and re.fullmatch(r'[a-f0-9]{40}', source_sha) is not None, 'SOURCE_SHA')
    require(type(run_id) is str and re.fullmatch(r'[1-9][0-9]{0,18}', run_id) is not None, 'RUN_ID')
    require(type(attempt) is int and 1 <= attempt <= 1000000, 'ATTEMPT_INVALID')
    # TRX originates from the native dotnet logger. Reject alternate XML encodings
    # before the byte-level DTD guard, so UTF-16 NUL bytes cannot evade it.
    # The native .NET TRX writer may prefix UTF-8 with one BOM. Refuse UTF-16,
    # NUL-padded input and any other encoding before scanning for DTDs.
    utf8 = raw[3:] if raw.startswith(b'\xef\xbb\xbf') else raw
    require(utf8.startswith(b'<?xml') and b'\x00' not in utf8, 'TRX_ENCODING')
    try:
        utf8.decode('utf-8')
    except UnicodeDecodeError as exc:
        raise ArchitectureEvidenceError('TRX_ENCODING') from exc
    require(b'<!DOCTYPE' not in raw.upper() and b'<!ENTITY' not in raw.upper(), 'TRX_DTD')
    try:
        root = ET.fromstring(raw)
    except (ET.ParseError, RecursionError) as exc:
        raise ArchitectureEvidenceError('TRX_XML') from exc
    require(root.tag == NAMESPACE + 'TestRun', 'TRX_ROOT')
    results = root.findall('./' + NAMESPACE + 'Results/' + NAMESPACE + 'UnitTestResult')
    counters = root.find('./' + NAMESPACE + 'ResultSummary/' + NAMESPACE + 'Counters')
    require(counters is not None and results, 'TRX_EMPTY')
    require(len(results) <= 5000, 'TRX_TOO_MANY_CASES')
    ids: set[str] = set()
    names: set[str] = set()
    for record in results:
        name, test_id = record.get('testName'), record.get('testId')
        require(type(name) is str and name.startswith(PREFIX) and
                re.fullmatch(r'[A-Za-z0-9_.]+', name) is not None, 'CASE_NAME')
        require(type(test_id) is str and re.fullmatch(r'[A-Za-z0-9-]{1,100}', test_id) is not None,
                'CASE_ID')
        require(name not in names and test_id not in ids, 'DUPLICATE_CASE')
        names.add(name)
        ids.add(test_id)
        require(record.get('outcome') == 'Passed', 'CASE_NOT_PASSED')
    require({PREFIX + name for name in EXPECTED}.issubset(names), 'MISSING_NEGATIVE_OR_LAYER_CASE')
    require(len(names) == len(EXPECTED), 'UNEXPECTED_CASE_SCOPE')
    for key in ('total', 'executed', 'passed', 'failed', 'error', 'timeout', 'aborted', 'inconclusive',
                'notRunnable', 'notExecuted', 'disconnected', 'warning', 'completed',
                'inProgress', 'pending', 'passedButRunAborted'):
        value = counters.get(key)
        require(type(value) is str and re.fullmatch(r'[0-9]{1,8}', value) is not None, 'TRX_COUNTERS')
        require(int(value) == (len(results) if key in ('total', 'executed', 'passed') else 0),
                'TRX_COUNTER_MISMATCH')
    digest = hashlib.sha256(('coglatas.architecture-case-ids.v1\0' + '\n'.join(sorted(names))).encode('utf-8')).hexdigest()
    return {
        'schema': 'coglatas.architecture-advisory-receipt.v1', 'sourceSha': source_sha,
        'runId': run_id, 'runAttempt': attempt, 'caseCount': len(names), 'caseIdsSha256': digest,
        'actualCasesPassed': len(names), 'executionState': 'NATIVE_TRX_CONTENT_VERIFIED',
        'artifactTrust': 'EXTERNAL_VERIFICATION_REQUIRED', 'requiredCheckQualified': False,
        'qualification': 'ADVISORY_ONLY_NOT_INDEPENDENT_PRODUCER_OR_ARTIFACT_ATTESTATION',
    }


def read_trx_file(path: Path) -> bytes:
    """Bind a bounded TRX read to one opened regular-file identity."""
    try:
        leaf = path.lstat()
        require(stat.S_ISREG(leaf.st_mode) and 0 < leaf.st_size <= MAX_BYTES, 'TRX_FILE')
        flags = os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0) | getattr(os, 'O_BINARY', 0)
        with os.fdopen(os.open(path, flags), 'rb') as source:
            opened = os.fstat(source.fileno())
            require(stat.S_ISREG(opened.st_mode) and os.path.samestat(leaf, opened)
                    and 0 < opened.st_size <= MAX_BYTES, 'TRX_FILE')
            raw = source.read(MAX_BYTES + 1)
            final = os.fstat(source.fileno())
            require(os.path.samestat(opened, final)
                    and opened.st_size == final.st_size
                    and opened.st_mtime_ns == final.st_mtime_ns
                    and opened.st_ctime_ns == final.st_ctime_ns
                    and len(raw) == opened.st_size, 'TRX_FILE')
        return raw
    except OSError as exc:
        raise ArchitectureEvidenceError('TRX_FILE') from exc


def main() -> int:
    """Verify native TRX evidence and emit only a sanitized bounded receipt."""
    parser = argparse.ArgumentParser()
    parser.add_argument('--trx', required=True)
    parser.add_argument('--sha', required=True)
    parser.add_argument('--run-id', required=True)
    parser.add_argument('--attempt', type=int, required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    try:
        raw = read_trx_file(Path(args.trx))
        receipt = verify(raw, args.sha, args.run_id, args.attempt)
        temp = Path(os.environ['RUNNER_TEMP']).resolve(strict=True)
        output = Path(args.output)
        require(output.parent.resolve(strict=True) == temp and output.name == 'architecture-advisory-receipt.json',
                'OUTPUT_PATH')
        flags = os.O_WRONLY | os.O_CREAT | os.O_EXCL | getattr(os, 'O_NOFOLLOW', 0)
        fd = os.open(output, flags, 0o600)
        with os.fdopen(fd, 'w', encoding='utf-8') as destination:
            destination.write(json.dumps(receipt, sort_keys=True, separators=(',', ':')) + '\n')
        print('ARCHITECTURE_ADVISORY_TRX_VERIFIED')
        return 0
    except ArchitectureEvidenceError as error:
        # Print only fixed, repository-owned codes, never XML contents or paths.
        known = {
            'TRX_SIZE', 'SOURCE_SHA', 'RUN_ID', 'ATTEMPT_INVALID',
            'TRX_ENCODING', 'TRX_DTD', 'TRX_XML', 'TRX_ROOT', 'TRX_EMPTY',
            'TRX_TOO_MANY_CASES', 'CASE_NAME', 'CASE_ID', 'DUPLICATE_CASE',
            'CASE_NOT_PASSED', 'MISSING_NEGATIVE_OR_LAYER_CASE',
            'UNEXPECTED_CASE_SCOPE', 'TRX_COUNTERS', 'TRX_COUNTER_MISMATCH',
            'TRX_FILE', 'OUTPUT_PATH'
        }
        reason = error.args[0] if len(error.args) == 1 and error.args[0] in known else 'UNKNOWN'
        print('ARCHITECTURE_ADVISORY_REFUSED:' + reason, file=sys.stderr)
        return 1
    except (OSError, ValueError, KeyError):
        print('ARCHITECTURE_ADVISORY_REFUSED', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
