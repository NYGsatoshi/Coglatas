#!/usr/bin/env python3
"""Validate independently supplied GitHub API metadata for Architecture runs.

The snapshots are externally supplied, not independently authenticated. Native
checkout, ZIP contents, producer attestation and governance remain unverified.
"""
from __future__ import annotations

import argparse
from datetime import datetime
import json
import os
from pathlib import Path
import re
import stat
import sys

REPOSITORY = 'NYGsatoshi/Coglatas'
REPOSITORY_ID = 1261244608
BASE_REF = 'SecurityTestImplimation'
WORKFLOW = '.github/workflows/architecture-contract-advisory.yml'
WORKFLOW_ID = 410981136
JOB_NAME = 'architecture-contract-advisory'
ACTION_APP_ID = 15368
MAX_METADATA = 128 * 1024
MAX_ARCHIVE_SIZE = 65536
INPUT_KEYS = frozenset(('pr', 'base_branch', 'head_commit', 'merge_commit',
                        'run', 'jobs', 'check_run', 'artifacts'))


class MetadataRefused(ValueError):
    """One fixed refusal code rather than data-dependent diagnostics."""


def require(condition: bool, code: str) -> None:
    """Reject incomplete, conflicting, or out-of-policy metadata."""
    if not condition:
        raise MetadataRefused(code)


def sha(value: object, count: int) -> bool:
    """Accept only fixed-length lowercase hexadecimal values."""
    return type(value) is str and re.fullmatch(r'[a-f0-9]{' + str(count) + '}', value) is not None


def positive(value: object) -> bool:
    """Reject Boolean-as-integer substitution."""
    return type(value) is int and 0 < value <= 2**63 - 1


def moment(value: object) -> datetime:
    """Parse GitHub's UTC timestamp representation, rejecting naive times."""
    require(type(value) is str and re.fullmatch(r'\d{4}-\d\d-\d\dT\d\d:\d\d:\d\dZ', value)
            is not None, 'TIME_FORMAT')
    try:
        result = datetime.fromisoformat(value.replace('Z', '+00:00'))
    except ValueError as exc:
        raise MetadataRefused('TIME_FORMAT') from exc
    require(result.tzinfo is not None and result.utcoffset().total_seconds() == 0,
            'TIME_FORMAT')
    return result


def unique_object(pairs: list[tuple[str, object]]) -> dict:
    """Reject duplicate JSON properties, including inside nested metadata."""
    out = {}
    for key, value in pairs:
        require(key not in out, 'DUPLICATE_JSON_KEY')
        out[key] = value
    return out


def reject_constant(_value: str) -> None:
    """Reject nonstandard NaN/Infinity values."""
    raise MetadataRefused('NONFINITE_JSON')


def read_snapshot(path: Path) -> dict:
    """Open without following links or blocking on swapped FIFOs, then revalidate."""
    try:
        current = path.absolute()
        while current != current.parent:
            require(not current.is_symlink(), 'METADATA_PATH')
            current = current.parent
        info = path.lstat()
        require(stat.S_ISREG(info.st_mode) and 0 < info.st_size <= MAX_METADATA,
                'METADATA_FILE')
        fd = os.open(path, os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0)
                     | getattr(os, 'O_NONBLOCK', 0))
        try:
            opened = os.fstat(fd)
            require(stat.S_ISREG(opened.st_mode) and opened.st_size == info.st_size
                    and opened.st_ino == info.st_ino and opened.st_dev == info.st_dev,
                    'METADATA_FILE')
            with os.fdopen(fd, 'rb', closefd=False) as stream:
                raw = stream.read(MAX_METADATA + 1)
            after = os.fstat(fd)
            require(os.path.samestat(opened, after)
                    and opened.st_size == after.st_size
                    and opened.st_mtime_ns == after.st_mtime_ns
                    and opened.st_ctime_ns == after.st_ctime_ns,
                    'METADATA_FILE')
        finally:
            os.close(fd)
        require(len(raw) == info.st_size and b'\x00' not in raw, 'METADATA_FILE')
        value = json.loads(raw.decode('utf-8'), object_pairs_hook=unique_object,
                           parse_constant=reject_constant)
    except (OSError, UnicodeError, TypeError, ValueError) as exc:
        raise MetadataRefused('METADATA_INPUT') from exc
    require(type(value) is dict and set(value) == INPUT_KEYS, 'METADATA_KEYS')
    return value


def verify(record: dict, *, pr_number: int, head_sha: str, base_sha: str,
           merge_sha: str, run_id: int, job_id: int, artifact_id: int,
           attempt: int, artifact_sha256: str, artifact_size: int) -> dict:
    """Cross-check explicit GitHub API projections without certifying their origin."""
    require(type(record) is dict and set(record) == INPUT_KEYS, 'METADATA_KEYS')
    require(all(positive(v) for v in (pr_number, run_id, job_id, artifact_id))
            and type(attempt) is int and attempt == 1
            and sha(head_sha, 40) and sha(base_sha, 40) and sha(merge_sha, 40)
            and sha(artifact_sha256, 64) and type(artifact_size) is int
            and 0 < artifact_size <= MAX_ARCHIVE_SIZE, 'ARGUMENTS')
    pr, branch = record['pr'], record['base_branch']
    head, merge = record['head_commit'], record['merge_commit']
    run, jobs = record['run'], record['jobs']
    check, artifacts = record['check_run'], record['artifacts']
    require(type(pr) is dict and positive(pr.get('number'))
            and pr['number'] == pr_number and pr.get('state') == 'open'
            and pr.get('merged') is False and pr['head']['sha'] == head_sha
            and pr['head']['repo']['id'] == REPOSITORY_ID
            and pr['base']['sha'] == base_sha
            and pr['base']['ref'] == BASE_REF
            and pr['base']['repo']['id'] == REPOSITORY_ID
            and pr['merge_commit_sha'] == merge_sha, 'PR_MISMATCH')
    require(branch['commit']['sha'] == base_sha and type(branch['protected']) is bool,
            'BASE_MISMATCH')
    require(head['sha'] == head_sha and merge['sha'] == merge_sha
            and [x['sha'] for x in merge['parents']] == [base_sha, head_sha]
            and head['tree']['sha'] == merge['tree']['sha']
            and sha(head['tree']['sha'], 40), 'TREE_MISMATCH')
    # Actions may return either an unqualified workflow filename or a
    # ref-qualified path. Accept only the already-pinned Security base ref.
    run_path = run.get('path')
    require(type(run_path) is str, 'RUN_MISMATCH')
    workflow_path, separator, workflow_ref = run_path.partition('@')
    require(workflow_path == WORKFLOW
            and (not separator or workflow_ref == BASE_REF), 'RUN_MISMATCH')
    require(run['id'] == run_id and run['head_sha'] == head_sha
            and run['head_branch'] == pr['head']['ref'] and run['event'] == 'pull_request'
            and run['workflow_id'] == WORKFLOW_ID
            and type(run['run_attempt']) is int and run['run_attempt'] == attempt
            and run['status'] == 'completed'
            and run['conclusion'] == 'success' and positive(run['check_suite_id'])
            and run['repository']['id'] == run['head_repository']['id'] == REPOSITORY_ID,
            'RUN_MISMATCH')
    associations = run.get('pull_requests')
    require(type(associations) is list and len(associations) == 1
            and associations[0]['number'] == pr_number
            and associations[0]['head']['sha'] == head_sha
            and associations[0]['base']['sha'] == base_sha
            and associations[0]['base']['ref'] == BASE_REF,
            'RUN_PR_ASSOCIATION')
    require(type(run.get('referenced_workflows')) is list
            and not run['referenced_workflows'], 'REUSABLE_BINDING_UNSUPPORTED')
    require(type(jobs) is list and len(jobs) == 1, 'JOB_SCOPE')
    job = jobs[0]
    require(job['id'] == job_id and job['name'] == JOB_NAME
            and job['run_id'] == run_id and type(job['run_attempt']) is int
            and job['run_attempt'] == attempt
            and job['head_sha'] == head_sha and job['status'] == 'completed'
            and job['conclusion'] == 'success'
            and job['check_run_url'] ==
            f'https://api.github.com/repos/{REPOSITORY}/check-runs/{job_id}',
            'JOB_MISMATCH')
    require(check['id'] == job_id and check['name'] == JOB_NAME
            and check['head_sha'] == head_sha and check['status'] == 'completed'
            and check['conclusion'] == 'success'
            and check['app']['id'] == ACTION_APP_ID
            and check['app']['slug'] == 'github-actions'
            and check['check_suite']['id'] == run['check_suite_id'], 'CHECK_MISMATCH')
    require(type(artifacts) is list and len(artifacts) == 1, 'ARTIFACT_SCOPE')
    artifact = artifacts[0]
    require(artifact['id'] == artifact_id
            and artifact['name'] == f'architecture-advisory-{run_id}-{attempt}'
            and artifact['size_in_bytes'] == artifact_size
            and artifact['digest'] == 'sha256:' + artifact_sha256
            and artifact['expired'] is False, 'ARTIFACT_MISMATCH')
    origin = artifact['workflow_run']
    require(origin['id'] == run_id
            and origin['repository_id'] == REPOSITORY_ID
            and origin['head_repository_id'] == REPOSITORY_ID
            and origin['head_branch'] == pr['head']['ref']
            and origin['head_sha'] == head_sha, 'ARTIFACT_OWNERSHIP_CLAIM')
    created, started = moment(run['created_at']), moment(job['started_at'])
    artifact_created, finished = moment(artifact['created_at']), moment(job['completed_at'])
    artifact_updated, updated = moment(artifact['updated_at']), moment(run['updated_at'])
    require(created <= started <= artifact_created <= artifact_updated
            <= finished <= updated, 'TIMELINE_MISMATCH')
    return {
        'schema': 'coglatas.architecture-github-metadata-witness.v1',
        'status': 'SUPPLIED_GITHUB_METADATA_CONTENT_MATCH',
        'prNumberClaim': pr_number, 'headShaClaim': head_sha,
        'testedMergeShaClaim': merge_sha, 'baseShaClaim': base_sha,
        'runIdClaim': run_id, 'runAttemptClaim': attempt,
        'jobIdClaim': job_id, 'artifactIdClaim': artifact_id,
        'archiveSha256Claim': artifact_sha256,
        'baseProtectedObserved': branch['protected'],
        'gitHubResponseOriginAuthenticated': False,
        'testedMergeExecutionAuthenticated': False,
        'artifactBytesVerified': False,
        'producerAuthenticated': False,
        'artifactProvenanceVerified': False,
        'requiredCheckQualified': False,
    }


def main() -> int:
    """Read a caller-supplied API snapshot with independent identity arguments."""
    p = argparse.ArgumentParser()
    p.add_argument('--snapshot', required=True, type=Path)
    for key in ('pr-number', 'run-id', 'job-id', 'artifact-id', 'attempt', 'artifact-size'):
        p.add_argument('--' + key, required=True, type=int)
    for key in ('head-sha', 'base-sha', 'merge-sha', 'artifact-sha256'):
        p.add_argument('--' + key, required=True)
    args = p.parse_args()
    try:
        witness = verify(read_snapshot(args.snapshot), pr_number=args.pr_number,
                         head_sha=args.head_sha, base_sha=args.base_sha,
                         merge_sha=args.merge_sha, run_id=args.run_id,
                         job_id=args.job_id, artifact_id=args.artifact_id,
                         attempt=args.attempt, artifact_sha256=args.artifact_sha256,
                         artifact_size=args.artifact_size)
    except (MetadataRefused, OSError, ValueError, TypeError, KeyError, IndexError):
        print('ARCHITECTURE_GITHUB_METADATA_REFUSED', file=sys.stderr)
        return 1
    print(json.dumps(witness, sort_keys=True, separators=(',', ':')))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
