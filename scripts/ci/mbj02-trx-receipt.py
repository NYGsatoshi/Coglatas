#!/usr/bin/env python3
"""Fixed MBJ-02 Issue527 evidence projection. Raw test output stays private."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

NS = '{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}'
ASSEMBLY = 'tests/Coglatas.Tests/bin/Release/net10.0/Coglatas.Tests.dll'
MANIFEST = 'scripts/ci/mbj02-issue527-required-tests.txt'
TRX = 'mbj02-issue527-postgresql.trx'
COUNTERS = ('total executed passed failed error timeout aborted inconclusive '
            'passedButRunAborted notRunnable notExecuted disconnected warning '
            'completed inProgress pending').split()
SHA = re.compile(r'[0-9a-f]{40}')
DIGEST = re.compile(r'[0-9a-f]{64}')
UUID = re.compile(r'[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}')
OUTCOMES = {'Passed': 'PASS', 'Failed': 'FAIL', 'NotExecuted': 'SKIP'}


class Invalid(Exception):
    pass


def require(condition):
    if not condition:
        raise Invalid()


def read(path, limit):
    # Refuse links, including parent directories, rather than following an
    # artifact path into another result directory or a sensitive host file.
    for parent in (path, *path.parents):
        require(not parent.is_symlink())
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd, 'rb') as stream:
        require(stat.S_ISREG(os.fstat(stream.fileno()).st_mode))
        data = stream.read(limit + 1)
    require(len(data) <= limit)
    return data


def digest(data):
    return hashlib.sha256(data).hexdigest()


def decode_json(data):
    def unique(pairs):
        result = {}
        for key, value in pairs:
            require(key not in result)
            result[key] = value
        return result
    return json.loads(data, object_pairs_hook=unique)


def same(left, right):
    return json.dumps(left, sort_keys=True) == json.dumps(right, sort_keys=True)


def write_json(path, value):
    for parent in path.parents:
        require(not parent.is_symlink())
    path.parent.mkdir(parents=True, exist_ok=True)
    require(not path.parent.is_symlink() and not path.is_symlink())
    data = (json.dumps(value, indent=2, sort_keys=True) + '\n').encode()
    require(len(data) <= 16384)
    with path.open('xb') as stream:
        stream.write(data)


def identity(root):
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
    require(SHA.fullmatch(source) is not None)
    require(os.environ.get('TARGET_SHA', source) == source)
    # A dirty checkout is not the source named by HEAD. Untracked raw results
    # are deliberately outside the tracked-source comparison.
    require(subprocess.run(['git', 'diff', '--quiet', 'HEAD', '--'], cwd=root).returncode == 0)
    run = os.environ.get('GITHUB_RUN_ID', 'local')
    attempt = os.environ.get('GITHUB_RUN_ATTEMPT', '1')
    require(re.fullmatch(r'local|[1-9][0-9]{0,19}', run) is not None)
    require(re.fullmatch(r'[1-9][0-9]{0,5}', attempt) is not None)
    return {'sourceSha': source, 'runId': run, 'runAttempt': int(attempt)}


def paths(root, ident):
    return root / 'artifacts/mbj02-issue527-private' / f"{ident['runId']}-{ident['runAttempt']}"


def names(root):
    lines = read(root / MANIFEST, 4096).decode('ascii').splitlines()
    require(len(lines) == len(set(lines)) == 8)
    require(all(re.fullmatch(r'Coglatas\.Tests\.PostgreSql\.InviteAcceptance(?:MembershipRace)?PostgreSqlTests\.[A-Za-z]+', n) for n in lines))
    return lines


def one(node, tag):
    found = node.findall(NS + tag)
    require(len(found) == 1)
    return found[0]


def parse_trx(data, required, assembly_roots):
    require(b'<!DOCTYPE' not in data.upper() and b'<!ENTITY' not in data.upper())
    root = ET.fromstring(data)
    require(root.tag == NS + 'TestRun')
    result_nodes = list(one(root, 'Results'))
    definitions = list(one(root, 'TestDefinitions'))
    entries = list(one(root, 'TestEntries'))
    require(len(result_nodes) == len(definitions) == len(entries) == 8)
    definitions_by_id = {}
    execution_ids = set()
    for definition in definitions:
        require(definition.tag == NS + 'UnitTest')
        tid = definition.get('id', '')
        eid = one(definition, 'Execution').get('id', '')
        method = one(definition, 'TestMethod')
        name = method.get('className', '') + '.' + method.get('name', '')
        require(UUID.fullmatch(tid) and UUID.fullmatch(eid))
        require(tid not in definitions_by_id and eid not in execution_ids)
        require(name in required and definition.get('name') == name)
        require(definition.get('storage', '') in {str(p).lower() for p in assembly_roots})
        require(method.get('codeBase', '') in {str(p) for p in assembly_roots})
        definitions_by_id[tid] = (eid, name)
        execution_ids.add(eid)
    observed = {}
    result_links = set()
    for node in result_nodes:
        require(node.tag == NS + 'UnitTestResult')
        tid, eid = node.get('testId'), node.get('executionId')
        require(tid in definitions_by_id and definitions_by_id[tid][0] == eid)
        name = definitions_by_id[tid][1]
        require(name == node.get('testName') and name not in observed)
        require(node.get('outcome') in OUTCOMES)
        require(re.fullmatch(r'[0-9]+:[0-5][0-9]:[0-5][0-9](?:\.[0-9]{1,7})?', node.get('duration', '')))
        observed[name] = OUTCOMES[node.get('outcome')]
        result_links.add((tid, eid, node.get('testListId')))
    require(set(observed) == set(required))
    entry_links = {(n.get('testId'), n.get('executionId'), n.get('testListId')) for n in entries}
    require(all(n.tag == NS + 'TestEntry' for n in entries))
    require(len(result_links) == len(entry_links) == 8 and entry_links == result_links)
    summary = one(root, 'ResultSummary')
    counters = one(summary, 'Counters').attrib
    require(set(counters) == set(COUNTERS))
    require(all(re.fullmatch(r'[0-9]{1,2}', v) for v in counters.values()))
    counters = {k: int(v) for k, v in counters.items()}
    passed = list(observed.values()).count('PASS')
    failed = list(observed.values()).count('FAIL')
    skipped = list(observed.values()).count('SKIP')
    require(counters == {k: {'total': 8, 'executed': passed + failed,
                            'passed': passed, 'failed': failed, 'notExecuted': skipped}.get(k, 0)
                         for k in COUNTERS})
    require(summary.get('outcome') == ('Completed' if passed == 8 else 'Failed'))
    return observed


def projection(root, private, ident, acceptance_exit):
    required = names(root)
    receipt = {'schemaVersion': 1, 'suite': 'MBJ-02-Issue527', **ident,
               'filter': 'Scope=Issue527', 'status': 'FAIL',
               'acceptanceExitCode': acceptance_exit, 'testExitCode': None,
               'verification': 'NOT_EXECUTED', 'trxSha256': None, 'testAssemblySha256': None,
               'postgresServerVersionNum': None,
               'assemblyEvidence': 'POST_TEST_ON_DISK_NOT_LOADED_BINARY_ATTESTATION',
               'facts': [{'name': n, 'outcome': 'NOT_EVALUATED'} for n in required]}
    try:
        context = decode_json(read(private / 'context.json', 1024))
        require(set(context) == {*ident, 'startedNs'})
        require(same({k: context[k] for k in ident}, ident))
        require(type(context['startedNs']) is int and 0 < context['startedNs'] <= time.time_ns())
        state = decode_json(read(private / 'test-exit.json', 64))
        require(set(state) == {'exitCode'} and type(state['exitCode']) is int and 0 <= state['exitCode'] <= 255)
        receipt['testExitCode'] = state['exitCode']
        receipt['verification'] = 'INVALID_EVIDENCE'
        trx = private / TRX
        require(trx.stat().st_mtime_ns >= context['startedNs'])
        data = read(trx, 2 * 1024 * 1024)
        receipt['trxSha256'] = digest(data)
        observed = parse_trx(data, required, [root / ASSEMBLY, Path('/src') / ASSEMBLY])
        receipt['facts'] = [{'name': n, 'outcome': observed[n]} for n in required]
        receipt['testAssemblySha256'] = digest(read(root / ASSEMBLY, 32 * 1024 * 1024))
        before = read(private / 'postgres-before.txt', 16).decode('ascii').strip()
        after = read(private / 'postgres-after.txt', 16).decode('ascii').strip()
        require(before == after and re.fullmatch(r'[1-9][0-9]{4,5}', before))
        receipt['postgresServerVersionNum'] = int(before)
        # Reuse the repository's required-name/counter gate as well as the
        # stricter fixed-set XML linkage validation above. Its raw diagnostics
        # and step-summary paths must never project untrusted TRX text.
        env = {k: v for k, v in os.environ.items() if k != 'GITHUB_STEP_SUMMARY'}
        checked = subprocess.run(['bash', 'scripts/ci/verify-trx-results.sh', str(trx),
                                  '--minimum-total', '8', '--required-tests', MANIFEST,
                                  '--label', 'MBJ-02 Issue527'], cwd=root, env=env,
                                 stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL).returncode == 0
        receipt['verification'] = 'VERIFIED' if checked and state['exitCode'] == 0 else 'TEST_FAILED'
        if receipt['verification'] == 'VERIFIED' and acceptance_exit == 0:
            receipt['status'] = 'PASS'
    except (Invalid, OSError, ValueError, ET.ParseError, UnicodeError):
        pass
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('command', choices=['begin', 'test-complete', 'finish', 'publish'])
    parser.add_argument('--exit-code', type=int, default=0)
    parser.add_argument('--acceptance-outcome', choices=['success', 'failure'])
    args = parser.parse_args()
    try:
        require(0 <= args.exit_code <= 255)
        root = Path(subprocess.check_output(['git', 'rev-parse', '--show-toplevel'], text=True).strip())
        ident = identity(root)
        private = paths(root, ident)
        if args.command == 'begin':
            # Never adopt an old PASS or overwrite another attempt's raw data.
            for parent in private.parents:
                require(not parent.is_symlink())
            private.mkdir(parents=True, mode=0o700, exist_ok=False)
            write_json(private / 'context.json', {**ident, 'startedNs': time.time_ns()})
            print(private.relative_to(root))
            return 0
        if args.command == 'test-complete':
            write_json(private / 'test-exit.json', {'exitCode': args.exit_code})
            return 0
        if args.command == 'finish':
            receipt = projection(root, private, ident, args.exit_code)
            write_json(private / 'receipt.json', receipt)
            return 0 if receipt['status'] == 'PASS' else 1
        # A closed, regenerated projection is the only public artifact. Unknown
        # fields, stale run/source metadata, or edited raw evidence fail closed.
        candidate = decode_json(read(private / 'receipt.json', 16384))
        require(type(candidate) is dict and type(candidate.get('acceptanceExitCode')) is int)
        require(0 <= candidate['acceptanceExitCode'] <= 255)
        expected = projection(root, private, ident, candidate['acceptanceExitCode'])
        require(same(candidate, expected))
        require(args.acceptance_outcome == ('success' if expected['status'] == 'PASS' else 'failure'))
        public = root / 'artifacts/mbj02-issue527/receipt.json'
        write_json(public, expected)
        if os.environ.get('GITHUB_OUTPUT'):
            with open(os.environ['GITHUB_OUTPUT'], 'a', encoding='utf-8') as output:
                output.write('validated=true\n')
        print('MBJ-02 fixed eight-fact receipt validated: ' + expected['status'])
        return 0 if expected['status'] == 'PASS' else 1
    except (Invalid, OSError, ValueError, subprocess.SubprocessError):
        print('MBJ-02 evidence validation failed; no unvalidated receipt published.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    sys.exit(main())
