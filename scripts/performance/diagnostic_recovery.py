"""Fail closed for the single authorized zero-measurement diagnostic replacement.

This validator never produces acceptance or baseline evidence. GitHub live run
and artifact inventories plus immutable archive/log bytes are mandatory inputs.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import re
import subprocess
import zipfile
from io import BytesIO
from pathlib import Path
from common import PerformanceContractError
from diagnostic_json import decode_json, load_json

ROOT = Path(__file__).resolve().parents[2]
RULE_PATH = ROOT / 'performance/diagnostic-zero-measurement-recovery.json'


def require(condition, reason):
    if not condition:
        raise PerformanceContractError('diagnostic recovery rejected: ' + reason)


def digest(value):
    return hashlib.sha256(value).hexdigest()


def declaration_digest(document):
    body = {key: value for key, value in document.items() if key != 'immutableDigest'}
    return digest(json.dumps(body, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode())


def validate(rule, proof, replacement, archive_bytes, log_bytes, phase='before-dispatch', current_run=None):
    require(rule['ruleId'] == 'perf05-diagnostic-zero-measurement-recovery-v1', 'rule identity')
    require(rule['scope'] == 'NON_ACCEPTANCE_DIAGNOSTIC_ONLY' and rule['maximumReplacements'] == 1, 'scope or bounds')
    predecessor = rule['predecessor']
    require(all(type(predecessor[key]) is int for key in ('measurements', 'validTimingSamples', 'runAttempt')),
            'predecessor scalar types')
    # Strict frozen proof fields cannot be changed or supplemented by a recovery.
    require(proof['predecessor'] == predecessor, 'predecessor or zero-measurement proof changed')
    require(predecessor['measurements'] == predecessor['validTimingSamples'] == 0
            and predecessor['loopStarted'] is False and predecessor['recoveryOf'] is None, 'zero measurement or chain')
    run = proof['run']
    require((run['id'], run['run_attempt'], run['head_sha'], run['head_branch'], run['workflow_id'],
             run['event'], run['status'], run['conclusion']) ==
            (predecessor['runId'], 1, predecessor['headSha'], predecessor['branch'], predecessor['workflowId'],
             'workflow_dispatch', 'completed', 'failure'), 'original live run')
    original_runs = proof['originalDispatchRuns']
    require(proof['originalDispatchTotal'] == len(original_runs) == 1
            and original_runs[0]['id'] == predecessor['runId']
            and original_runs[0]['run_attempt'] == 1, 'original was not first and only dispatch')
    artifacts = proof['artifacts']
    require(len(artifacts) == 1 and artifacts[0]['id'] == predecessor['artifactId']
            and artifacts[0]['expired'] is False
            and artifacts[0]['digest'] == 'sha256:' + predecessor['artifactSha256'], 'original artifact unavailable')
    require(archive_bytes and digest(archive_bytes) == predecessor['artifactSha256'], 'original archive digest')
    require(log_bytes and digest(log_bytes) == predecessor['failureLogSha256'], 'missing or changed failure log')
    log = log_bytes.decode('utf-8')
    require('announcement-diagnostic.py", line 60, in main' in log
            and 'fixture = json.loads(Path(' in log
            and 'JSONDecodeError: Unexpected UTF-8 BOM' in log, 'collector initialization failure not demonstrated')
    try:
        with zipfile.ZipFile(BytesIO(archive_bytes)) as archive:
            names = archive.namelist()
            require(len(names) == len(set(names)), 'duplicate archive member')
            hashes = {name: digest(archive.read(name)) for name in names}
            require(hashes == predecessor['archiveFiles'], 'zero-measurement archive contents')
            execution = decode_json(archive.read('diagnostic-execution.json'))
            require((execution['runId'], execution['attempt'], execution['sha']) ==
                    (predecessor['runId'], 1, predecessor['headSha']), 'original archive identity')
            require(archive.read('medium/fixture.json').startswith(b'\xef\xbb\xbf'), 'actual BOM fixture missing')
    except (zipfile.BadZipFile, KeyError) as error:
        raise PerformanceContractError('invalid original diagnostic archive') from error
    require(set(replacement) == {'schemaVersion', 'ruleId', 'purpose', 'diagnosticId', 'dispatchId',
            'predecessorRun', 'recoveryOf', 'recoveryDepth', 'fixedCollectorCommit', 'workload',
            'collectorFiles', 'branch', 'declarationReference', 'immutableDigest'}, 'replacement schema')
    require(replacement['schemaVersion'] == 1 and replacement['ruleId'] == rule['ruleId']
            and replacement['purpose'] == 'DIAGNOSTIC_ONLY_NOT_ACCEPTANCE_NOT_BASELINE', 'replacement scope')
    require(replacement['predecessorRun'] == f"{predecessor['runId']}/1"
            and replacement['recoveryOf'] == predecessor['diagnosticId'] and replacement['recoveryDepth'] == 1, 'recovery chain')
    require(replacement['diagnosticId'] != predecessor['diagnosticId']
            and re.fullmatch(r'perf05-announcement-diagnostic-recovery-[a-z0-9-]+', replacement['diagnosticId']), 'reused diagnostic ID')
    require(re.fullmatch(r'[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}', replacement['dispatchId'])
            and replacement['dispatchId'] != predecessor['dispatchId'], 'reused dispatch identity')
    require(replacement['workload'] == rule['workload'], 'diagnostic workload changed')
    require(replacement['collectorFiles'] == rule['fixedCollectorFiles']
            and bool(replacement['collectorFiles']), 'unvalidated collector')
    require(re.fullmatch(r'[0-9a-f]{40}', replacement['fixedCollectorCommit']), 'fixed collector commit')
    require(replacement['branch'] == 'diagnostics/' + replacement['diagnosticId']
            and replacement['branch'] != predecessor['branch'], 'replacement branch')
    require(replacement['declarationReference'].startswith('https://github.com/NYGsatoshi/Coglatas/pull/1046#issuecomment-'), 'new declaration reference')
    require(replacement['declarationReference'] != 'https://github.com/NYGsatoshi/Coglatas/pull/1046#issuecomment-6047988386', 'reused declaration reference')
    require(replacement['immutableDigest'] == declaration_digest(replacement), 'declaration digest')
    require(phase in {'before-dispatch', 'in-run'}, 'validation phase')
    recovery_runs = proof['replacementDispatchRuns']
    require(proof['replacementDispatchTotal'] == len(recovery_runs), 'incomplete replacement inventory')
    if phase == 'before-dispatch':
        require(not recovery_runs and current_run is None, 'replacement already consumed')
    else:
        require(len(recovery_runs) == 1 and current_run is not None, 'third dispatch or missing current run')
        require((current_run['id'], current_run['run_attempt'], current_run['head_branch'], current_run['event']) ==
                (recovery_runs[0]['id'], 1, replacement['branch'], 'workflow_dispatch'), 'replacement retry or identity')
        require(current_run['id'] != predecessor['runId'] and recovery_runs[0]['run_attempt'] == 1, 'reused run or retry')
    return {'eligible': True, 'ruleId': rule['ruleId'], 'predecessorRun': replacement['predecessorRun'],
            'diagnosticId': replacement['diagnosticId'], 'declarationDigest': replacement['immutableDigest'],
            'acceptanceCredit': False, 'baselineCredit': False, 'maximumReplacements': 1}


def validate_checkout(rule, root=ROOT):
    def git(*args):
        return subprocess.check_output(['git', '-C', str(root), *args]).decode().strip()
    require(git('rev-parse', 'HEAD:src') == rule['workload']['sourceTree'], 'source under investigation changed')
    require(git('rev-parse', 'HEAD:performance/baselines') == rule['workload']['baselineTree'], 'baseline changed')
    for name, expected in (rule['workload']['files'] | rule['fixedCollectorFiles']).items():
        require(digest((root / name).read_bytes()) == expected, 'changed contract file: ' + name)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--proof', required=True, type=Path)
    parser.add_argument('--declaration', required=True, type=Path)
    parser.add_argument('--original-archive', required=True, type=Path)
    parser.add_argument('--failure-log', required=True, type=Path)
    parser.add_argument('--phase', choices=['before-dispatch', 'in-run'], default='before-dispatch')
    parser.add_argument('--current-run', type=Path)
    parser.add_argument('--verify-checkout', action='store_true')
    args = parser.parse_args()
    rule = load_json(RULE_PATH)
    result = validate(rule, load_json(args.proof), load_json(args.declaration), args.original_archive.read_bytes(),
                      args.failure_log.read_bytes(), args.phase, load_json(args.current_run) if args.current_run else None)
    if args.verify_checkout:
        validate_checkout(rule)
    print(json.dumps(result, indent=2))


if __name__ == '__main__':
    main()
