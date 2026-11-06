"""Validate recovery documentation only; never grant CI/program acceptance."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
STATES = {
    'implementation', 'nativeExecution', 'coverage', 'provenance',
    'independentReview', 'governance', 'security', 'performance',
    'nightlyActivation', 'release', 'ownerApproval',
}
CONTRACT_FIELDS = {
    'schemaVersion', 'contractId', 'integrationBranch', 'mainModificationAuthorized',
    'governanceSettingsChangeAuthorized', 'requiredCheckPolicyChangeAuthorized',
    'releaseApprovalGranted', 'decisionsAlreadyApproved', 'decisions',
    'requiredDeliverables', 'requiredRejectionControls', 'completionStateNames',
    'completionStatesIndependent', 'automaticSuccessImplications',
    'mandatoryUnresolvedRequirementsBlock', 'exitCriterionCount',
    'finalEvidenceVerifierImplemented', 'validationScope', 'documents',
}
FIXED_STATUS = {
    'PROGRAM_STATUS': 'INCOMPLETE', 'SECURITY_BRANCH_INTEGRATION': 'HOLD',
    'MAIN_UNCHANGED': True, 'GOVERNANCE_IDENTITY_DRIFT': 'OWNER_HOLD',
    'TRUSTED_SONAR_IMPORT': 'NOT_VERIFIED', 'PR_J_TO_N_ACCEPTANCE': 'OPEN',
    'INDEPENDENT_REVIEW_COMPLETE': False,
}


class ContractError(ValueError):
    """The persisted recovery contract is incomplete or inconsistent."""


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ContractError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'Duplicate JSON field.')
        result[key] = value
    return result


def reject_constant(_value):
    raise ContractError('Nonfinite JSON value.')


def load(path: Path):
    require(path.is_file() and not path.is_symlink(), 'Missing or symlinked contract file.')
    with path.open('rb') as stream:
        data = stream.read(2 * 1024 * 1024 + 1)
    require(0 < len(data) <= 2 * 1024 * 1024, 'Empty or oversized contract file.')
    try:
        return json.loads(data.decode('utf-8'), object_pairs_hook=unique_object,
                          parse_constant=reject_constant)
    except (UnicodeError, json.JSONDecodeError, RecursionError) as error:
        raise ContractError('Malformed contract JSON.') from error


def local_file(folder: Path, name: str) -> Path:
    require(isinstance(name, str) and bool(name), 'Invalid local reference.')
    path = folder / name
    require(not path.is_symlink() and path.resolve().is_relative_to(folder.resolve()),
            'Contract reference escapes its directory.')
    require(path.is_file(), 'Contract reference is missing.')
    return path


def validate_contract(folder: Path = HERE, root: Path = ROOT,
                      verify_preservation: bool = False) -> None:
    contract = load(folder / 'program-contract.json')
    require(isinstance(contract, dict) and set(contract) == CONTRACT_FIELDS,
            'Unknown or missing contract fields.')
    require(type(contract['schemaVersion']) is int and contract['schemaVersion'] == 1,
            'Unsupported contract schema.')
    require(contract['contractId'] == 'CI-ASSURANCE-DECISIONS-PR-N'
            and contract['integrationBranch'] == 'SecurityTestImplimation', 'Wrong contract identity.')
    for key in ['mainModificationAuthorized', 'governanceSettingsChangeAuthorized',
                'requiredCheckPolicyChangeAuthorized', 'releaseApprovalGranted',
                'automaticSuccessImplications', 'finalEvidenceVerifierImplemented']:
        require(contract[key] is False, 'Recovery contract cannot grant execution or approval authority.')
    for key in ['decisionsAlreadyApproved', 'completionStatesIndependent',
                'mandatoryUnresolvedRequirementsBlock']:
        require(contract[key] is True, 'Required recovery boundary is absent.')
    require(contract['validationScope'] == 'DOCUMENT_CONTRACT_AND_PRESERVATION_ONLY_NOT_RUNTIME_OR_INTEGRATION_ACCEPTANCE',
            'Documentation validation scope changed.')
    names = contract['completionStateNames']
    require(isinstance(names, list) and len(names) == 11 and set(names) == STATES,
            'Independent completion states are missing or duplicated.')
    documents = contract['documents']
    require(documents == ['README.md', 'approved-decisions.md', 'pr-n-contract.md',
                          'program-status.json', 'evidence-preservation.json'],
            'Required document references changed.')
    for name in documents:
        local_file(folder, name)
    for document in ['approved-decisions.md', 'pr-n-contract.md']:
        text = local_file(folder, document).read_text(encoding='utf-8')
        for target in re.findall(r'\[[^\]]+\]\(([^)]+)\)', text):
            local_file(folder, target.split('#', 1)[0])

    decisions = contract['decisions']
    require(isinstance(decisions, list) and len(decisions) == 15,
            'Approved decision set is incomplete.')
    text = local_file(folder, 'approved-decisions.md').read_text(encoding='utf-8')
    declared = re.findall(r'^### (D\d\d)\n\n([^\n]+)', text, re.M)
    require([identity for identity, _ in declared] == [f'D{i:02}' for i in range(1, 16)],
            'Approved decision headings are missing, duplicated or reordered.')
    require(decisions == [{'id': identity, 'wordingSha256': hashlib.sha256(wording.encode()).hexdigest()}
                          for identity, wording in declared], 'Approved decision wording changed.')

    text = local_file(folder, 'pr-n-contract.md').read_text(encoding='utf-8')
    declared = re.findall(r'^### (N\d\d) — ([^\n]+)', text, re.M)
    require([identity for identity, _ in declared] == [f'N{i:02}' for i in range(1, 13)],
            'PR-N deliverables are missing, duplicated or reordered.')
    require(contract['requiredDeliverables'] == [{'id': identity, 'title': title} for identity, title in declared],
            'PR-N deliverable references do not match.')
    controls = re.findall(r'^\| (R\d\d) \|', text, re.M)
    require(controls == [f'R{i:02}' for i in range(1, 13)]
            and contract['requiredRejectionControls'] == controls,
            'Mandatory final-verifier rejection controls are missing or duplicated.')
    exits = text.split('## Program exit criteria\n', 1)
    require(len(exits) == 2, 'Program exit criteria section is missing.')
    numbers = re.findall(r'^(\d+)\. ', exits[1].split('\n## ', 1)[0], re.M)
    require(numbers == [str(i) for i in range(1, 13)]
            and type(contract['exitCriterionCount']) is int and contract['exitCriterionCount'] == 12,
            'Program exit criteria are incomplete.')

    status = load(folder / 'program-status.json')
    require(isinstance(status, dict), 'Invalid program state register.')
    for key, expected in FIXED_STATUS.items():
        require(key in status and type(status[key]) is type(expected) and status[key] == expected,
                'Recovery checkpoint cannot imply final acceptance or remove a hold.')
    states = status.get('completionStates')
    require(isinstance(states, dict) and set(states) == STATES, 'Completion-state register is incomplete.')
    for value in states.values():
        require(isinstance(value, dict) and set(value) == {'complete', 'state'}
                and value['complete'] is False and value['state'] in {'INCOMPLETE', 'NOT_VERIFIED'},
                'Recovery checkpoint must preserve independent incomplete states.')
    holds = status.get('holds')
    require(isinstance(holds, list) and {h.get('id') for h in holds if isinstance(h, dict)} == {
        'GOVERNANCE_IDENTITY', 'SECURITY_BRANCH_PROTECTION', 'INDEPENDENT_REVIEW',
        'TRUSTED_SONAR_IMPORT', 'PR_J_TO_N', 'NIGHTLY_ACTIVATION', 'FINAL_OWNER_APPROVAL'},
        'Mandatory hold register is incomplete.')
    require(len(holds) == 7 and all(set(h) == {'id', 'owner', 'resolution'}
            and all(isinstance(v, str) and v.strip() for v in h.values()) for h in holds),
            'Hold ownership or resolution is missing.')

    preservation = load(folder / 'evidence-preservation.json')
    ledger = preservation.get('originalLedger', {})
    require(ledger.get('entryCount') == 45 and type(ledger.get('entryCount')) is int
            and ledger.get('sourceSha') == 'b34a0503ca0637634b3f492b69f3d3491c12c6d2'
            and ledger.get('path') == 'docs/verification/ci-assurance-program/gap-ledger.json',
            'Original capability ledger identity changed.')
    anchors = preservation.get('anchors')
    require(isinstance(anchors, list) and len(anchors) == 15, 'Preservation anchors are incomplete.')
    require(len({a['path'] for a in anchors}) == len(anchors), 'Duplicate preservation path.')
    for anchor in anchors:
        require(set(anchor) == {'path', 'sourceSha', 'canonicalBlobSha256', 'mode'}
                and re.fullmatch(r'[0-9a-f]{40}', anchor['sourceSha']) is not None
                and re.fullmatch(r'[0-9a-f]{64}', anchor['canonicalBlobSha256']) is not None
                and anchor['mode'] in {'historical-source-only', 'unchanged', 'append-only'},
                'Malformed preservation anchor.')
        path = root / anchor['path']
        require(path.resolve().is_relative_to(root.resolve()), 'Preservation path escapes repository.')
        if not verify_preservation:
            continue
        original = subprocess.run(['git', 'show', anchor['sourceSha'] + ':' + anchor['path']],
                                  cwd=root, capture_output=True, check=True).stdout
        require(hashlib.sha256(original).hexdigest() == anchor['canonicalBlobSha256'],
                'Pinned historical source digest differs.')
        if anchor['mode'] == 'unchanged':
            expected_oid = subprocess.run(['git', 'rev-parse', anchor['sourceSha'] + ':' + anchor['path']],
                                          cwd=root, capture_output=True, check=True).stdout.strip()
            # Canonical Git text identity must be stable for a Windows CRLF
            # checkout inspected by Linux Git without the Windows global config.
            # This per-command clean conversion respects path attributes and
            # does not change source files, raw artifacts or repository settings.
            current_oid = subprocess.run(['git', '-c', 'core.autocrlf=input', 'hash-object',
                                          '--path=' + anchor['path'], anchor['path']],
                                         cwd=root, capture_output=True, check=True).stdout.strip()
            require(current_oid == expected_oid, 'Retained evidence bytes changed.')
        elif anchor['mode'] == 'append-only':
            require(path.read_text(encoding='utf-8').startswith(original.decode('utf-8')),
                    'Original checkpoint prose was replaced rather than appended.')
    if verify_preservation:
        data = subprocess.run(['git', 'show', ledger['sourceSha'] + ':' + ledger['path']],
                              cwd=root, capture_output=True, check=True).stdout
        require(len(json.loads(data)['entries']) == 45, 'Original capability inventory differs.')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--verify-preservation', action='store_true')
    args = parser.parse_args()
    try:
        validate_contract(verify_preservation=args.verify_preservation)
    except (ContractError, OSError, ValueError, KeyError, TypeError, subprocess.SubprocessError):
        print('Recovery documentation contract REJECTED; no assurance acceptance granted.')
        return 1
    print('Recovery documentation contract PASS; PROGRAM_STATUS=INCOMPLETE; runtime assurance NOT_VERIFIED.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
