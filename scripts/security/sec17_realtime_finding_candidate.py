"""Inert SEC-17 realtime finding-scope proposal; not a producer or policy adapter."""

from __future__ import annotations

import finding_policy as common

CASE = 'workspace-file-sharing-policy'
CATEGORIES = {
    'private-established': ('post-policy-commit', 'established'),
    'private-fresh': ('post-policy-commit', 'fresh'),
    'replay-established': ('immutable-replay', 'established'),
    'replay-fresh': ('immutable-replay', 'fresh'),
}
STATUSES = {'COMPLETE', 'EXECUTION_FAILURE', 'COVERAGE_INSUFFICIENT'}


def inspect_candidate(value: object) -> dict:
    """Validate synthetic review input without asserting trusted observation."""
    common.fields(value, {'schemaVersion', 'case', 'sourceSha', 'executionStatus',
                          'positiveControls', 'observations'}, 'CANDIDATE_INPUT_INVALID')
    common.require(type(value['schemaVersion']) is int and value['schemaVersion'] == 1
                   and value['case'] == CASE, 'CANDIDATE_CASE_INVALID')
    common.require(common.token(value['sourceSha'], r'[0-9a-f]{40}'), 'CANDIDATE_SOURCE_INVALID')
    common.require(isinstance(value['executionStatus'], str) and value['executionStatus'] in STATUSES
                   and isinstance(value['positiveControls'], str)
                   and value['positiveControls'] in {'CONFIRMED', 'UNCONFIRMED'},
                   'CANDIDATE_EXECUTION_INVALID')
    observations = value['observations']
    common.require(isinstance(observations, list) and len(observations) <= len(CATEGORIES),
                   'CANDIDATE_OBSERVATIONS_INVALID')
    seen, proposals = set(), []
    for observation in observations:
        common.fields(observation, {'category', 'observation'}, 'CANDIDATE_OBSERVATION_INVALID')
        category = observation['category']
        common.require(isinstance(category, str) and category in CATEGORIES and category not in seen
                       and isinstance(observation['observation'], str)
                       and observation['observation'] in {'EXCLUDED', 'DELIVERED'},
                       'CANDIDATE_OBSERVATION_INVALID')
        seen.add(category)
        if observation['observation'] != 'DELIVERED':
            continue
        phase, receiver = CATEGORIES[category]
        scope = {'kind': 'realtime', 'role': 'alpha-restricted', 'hub': '/hubs/app',
                 'eventType': 'Files.FileChanged.v1', 'resourceClass': 'WorkspaceFile',
                 'phase': phase, 'receiver': receiver}
        identity = {'tool': 'security-integration', 'ruleId': 'SEC17.FilePolicy.DeliveryAfterPrivate',
                    'scope': scope, 'findingClass': 'authorization'}
        proposals.append({'category': category, 'proposedScope': scope,
                          'proposedFingerprint': common.digest(identity, 'sec17-unadopted-realtime-v1')})
    complete = (value['executionStatus'] == 'COMPLETE'
                and value['positiveControls'] == 'CONFIRMED' and seen == set(CATEGORIES))
    # Even COMPLETE is declared metadata here, not authenticated native evidence.
    state = ('COMPLETE_OBSERVED_DELIVERY' if proposals else 'COMPLETE_NO_OBSERVED_DELIVERY') if complete else 'EXECUTION_FAILURE'
    return {'schemaVersion': 1, 'status': 'CANDIDATE_NOT_ADOPTED',
            'sourceSha': value['sourceSha'], 'executionState': state,
            'liveGateQualified': False, 'producerVerified': False,
            'severity': None, 'normalizedFinding': None, 'policyDecision': None,
            'proposals': sorted(proposals, key=lambda row: row['category'])}
