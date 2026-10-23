"""Offline SEC-08 finding normalization and policy mechanics; never a live gate."""

from __future__ import annotations

import argparse
from collections import Counter
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys
from typing import Any
from urllib.parse import quote, unquote

LIMIT = 2 * 1024 * 1024
MAX_FINDINGS = 1000
METHODS = {'GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'HEAD', 'OPTIONS'}
RISKS = {'Critical', 'High', 'Medium', 'Low', 'Informational'}
ROLES = {'alpha-owner', 'alpha-restricted', 'beta-owner'}
TOOLS = {'zap-api', 'zap-browser', 'schemathesis', 'security-integration'}
CONFIDENCE = {'unknown', 'low', 'medium', 'high', 'confirmed'}
CLASSES = {'generic', 'authorization', 'secret-disclosure', 'injection', 'security-control-regression'}
EXECUTION = {'PASS_EXECUTED', 'SCANNER_FAILURE', 'AUTH_FAILURE', 'TARGET_FAILURE', 'COVERAGE_INSUFFICIENT'}
SCOPE_FIELDS = {'role', 'method', 'routeClass', 'parameterCategory', 'parameterName'}
FINDING_FIELDS = {'ruleId', 'severity', 'confidence', 'findingClass', 'scope'}
EXCEPTION_FIELDS = {'id', 'tool', 'ruleId', 'severity', 'findingClass', 'scope', 'fingerprint',
                    'rationale', 'owner', 'createdAtUtc', 'expiresAtUtc', 'trackingIssue', 'approval'}
FORBIDDEN_ENV = 'COGLATAS_SECURITY_FINDING_FORBIDDEN_VALUES'


class PolicyError(ValueError):
    """Only finite repository-owned codes may reach the command line."""


def require(condition: bool, code: str) -> None:
    if not condition:
        raise PolicyError(code)


def fields(value: Any, expected: set[str], code: str) -> None:
    require(isinstance(value, dict) and set(value) == expected, code)


def token(value: Any, pattern: str) -> bool:
    return isinstance(value, str) and re.fullmatch(pattern, value) is not None


def canonical(value: Any) -> bytes:
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True).encode('ascii')


def digest(value: Any, domain: str) -> str:
    return hashlib.sha256(domain.encode('ascii') + b'\0' + canonical(value)).hexdigest()


def timestamp(value: Any) -> datetime:
    require(token(value, r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z'), 'TIME_INVALID')
    try:
        return datetime.strptime(value, '%Y-%m-%dT%H:%M:%SZ').replace(tzinfo=timezone.utc)
    except ValueError:
        raise PolicyError('TIME_INVALID') from None


def unique_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for key, value in pairs:
        require(key not in result, 'DUPLICATE_FIELD')
        result[key] = value
    return result


def read_json(path: Path) -> tuple[Any, str]:
    try:
        require(not path.is_symlink(), 'INPUT_NOT_REGULAR')
        with path.open('rb') as handle:
            require(stat.S_ISREG(os.fstat(handle.fileno()).st_mode), 'INPUT_NOT_REGULAR')
            raw = handle.read(LIMIT + 1)
        require(0 < len(raw) <= LIMIT, 'INPUT_SIZE_INVALID')
        value = json.loads(raw, object_pairs_hook=unique_object,
                           parse_constant=lambda _: (_ for _ in ()).throw(PolicyError('JSON_INVALID')))
        return value, hashlib.sha256(raw).hexdigest()
    except (OSError, UnicodeError, ValueError, RecursionError):
        raise PolicyError('INPUT_UNREADABLE') from None


def contract_operations(contract: Any) -> dict[tuple[str, str], dict[str, set[str]]]:
    require(isinstance(contract, dict) and isinstance(contract.get('paths'), dict), 'CONTRACT_INVALID')
    operations = {}
    for route, item in contract['paths'].items():
        require(token(route, r'/(?:[A-Za-z0-9_.~-]+|\{[A-Za-z_][A-Za-z0-9_]*\})(?:/(?:[A-Za-z0-9_.~-]+|\{[A-Za-z_][A-Za-z0-9_]*\}))*')
                and len(route) <= 256 and isinstance(item, dict), 'CONTRACT_ROUTE_INVALID')
        for method, operation in item.items():
            if method.upper() not in METHODS:
                continue
            require(method == method.lower() and isinstance(operation, dict), 'CONTRACT_OPERATION_INVALID')
            names = {category: set() for category in ('path', 'query', 'header', 'cookie', 'body')}
            inherited, local = item.get('parameters', []), operation.get('parameters', [])
            require(isinstance(inherited, list) and isinstance(local, list), 'CONTRACT_PARAMETER_INVALID')
            parameters = inherited + local
            for parameter in parameters:
                require(isinstance(parameter, dict) and parameter.get('in') in names
                        and token(parameter.get('name'), r'[A-Za-z_][A-Za-z0-9_.-]{0,79}'), 'CONTRACT_PARAMETER_INVALID')
                names[parameter['in']].add(parameter['name'])
            # Body properties and $refs are deliberately not guessed. An adapter must
            # provide an independently reviewed classifier before suppressing them.
            operations[(method.upper(), route)] = names
    require(0 < len(operations) <= 5000, 'CONTRACT_OPERATION_INVALID')
    return operations


def validate_scope(scope: Any, operations: dict) -> None:
    fields(scope, SCOPE_FIELDS, 'SCOPE_INVALID')
    require(scope['role'] in ROLES and scope['method'] in METHODS
            and (scope['method'], scope['routeClass']) in operations, 'SCOPE_INVALID')
    category, name = scope['parameterCategory'], scope['parameterName']
    if category == 'none':
        require(name is None, 'SCOPE_PARAMETER_INVALID')
    else:
        require(category in operations[(scope['method'], scope['routeClass'])]
                and name in operations[(scope['method'], scope['routeClass'])].get(category, set()),
                'SCOPE_PARAMETER_INVALID')


def normalize_document(document: Any, operations: dict) -> dict:
    fields(document, {'schemaVersion', 'scanner', 'sourceSha', 'configSha256', 'execution', 'findings'}, 'REPORT_INVALID')
    require(type(document['schemaVersion']) is int and document['schemaVersion'] == 1, 'REPORT_SCHEMA_INVALID')
    scanner = document['scanner']
    fields(scanner, {'id', 'version'}, 'SCANNER_INVALID')
    require(scanner['id'] in TOOLS and token(scanner['version'], r'\d{1,4}\.\d{1,4}\.\d{1,4}'), 'SCANNER_INVALID')
    require(token(document['sourceSha'], r'[0-9a-f]{40}')
            and token(document['configSha256'], r'[0-9a-f]{64}'), 'IDENTITY_INVALID')
    execution = document['execution']
    fields(execution, {'status', 'exitCode'}, 'EXECUTION_INVALID')
    require(execution['status'] in EXECUTION and type(execution['exitCode']) is int
            and 0 <= execution['exitCode'] <= 255
            and (execution['status'] != 'PASS_EXECUTED' or execution['exitCode'] == 0), 'EXECUTION_INVALID')
    findings = document['findings']
    require(isinstance(findings, list) and len(findings) <= MAX_FINDINGS, 'FINDING_LIMIT_INVALID')
    normalized = {}
    for finding in findings:
        fields(finding, FINDING_FIELDS, 'FINDING_INVALID')
        require(token(finding['ruleId'], r'[A-Za-z0-9][A-Za-z0-9_.-]{0,79}')
                and finding['severity'] in RISKS and finding['confidence'] in CONFIDENCE
                and finding['findingClass'] in CLASSES, 'FINDING_INVALID')
        validate_scope(finding['scope'], operations)
        identity = {'tool': scanner['id'], 'ruleId': finding['ruleId'], 'scope': finding['scope'],
                    'findingClass': finding['findingClass']}
        result = {**finding, 'evidenceHash': digest(identity, 'safe-metadata-evidence-v1'),
                  'fingerprint': digest(identity, 'finding-fingerprint-v1')}
        key = (result['fingerprint'], result['severity'], result['confidence'])
        normalized[key] = result
    return {**document, 'findings': [normalized[key] for key in sorted(normalized)]}


def reference(value: Any) -> bool:
    return token(value, r'https://github\.com/NYGsatoshi/Coglatas/(?:issues|pull)/[1-9][0-9]*(?:#issuecomment-[1-9][0-9]*)?')


def validate_policy(policy: Any, operations: dict, evaluated_at: str) -> list[dict]:
    fields(policy, {'schemaVersion', 'activation', 'initialInventory', 'exceptions'}, 'POLICY_INVALID')
    require(type(policy['schemaVersion']) is int and policy['schemaVersion'] == 1
            and policy['activation'] == 'NOT_ACTIVATED' and policy['initialInventory'] == 'UNREVIEWED', 'POLICY_NOT_PREPARATION')
    now = timestamp(evaluated_at)
    entries = policy['exceptions']
    require(isinstance(entries, list) and len(entries) <= MAX_FINDINGS, 'EXCEPTION_LIMIT_INVALID')
    identities, matches = set(), set()
    for entry in entries:
        fields(entry, EXCEPTION_FIELDS, 'EXCEPTION_INVALID')
        require(token(entry['id'], r'[a-z][a-z0-9-]{0,63}') and entry['id'] not in identities, 'EXCEPTION_ID_INVALID')
        identities.add(entry['id'])
        require(entry['tool'] in TOOLS and token(entry['ruleId'], r'[A-Za-z0-9][A-Za-z0-9_.-]{0,79}')
                and entry['severity'] == 'Medium' and entry['findingClass'] == 'generic', 'EXCEPTION_NON_SUPPRESSIBLE')
        validate_scope(entry['scope'], operations)
        require(token(entry['fingerprint'], r'[0-9a-f]{64}'), 'EXCEPTION_FINGERPRINT_INVALID')
        expected = digest({'tool': entry['tool'], 'ruleId': entry['ruleId'], 'scope': entry['scope'],
                           'findingClass': entry['findingClass']}, 'finding-fingerprint-v1')
        require(entry['fingerprint'] == expected and expected not in matches, 'EXCEPTION_FINGERPRINT_INVALID')
        matches.add(expected)
        require(isinstance(entry['rationale'], str) and 1 <= len(entry['rationale'].strip()) <= 500
                and all(ord(char) >= 32 for char in entry['rationale']), 'EXCEPTION_RATIONALE_INVALID')
        require(token(entry['owner'], r'[A-Za-z0-9][A-Za-z0-9-]{0,38}') and reference(entry['trackingIssue']),
                'EXCEPTION_OWNER_OR_TRACKING_INVALID')
        created, expires = timestamp(entry['createdAtUtc']), timestamp(entry['expiresAtUtc'])
        require(created <= now and created < expires, 'EXCEPTION_DATE_INVALID')
        approval = entry['approval']
        fields(approval, {'approvedBy', 'approvedAtUtc', 'reference'}, 'EXCEPTION_APPROVAL_REQUIRED')
        require(token(approval['approvedBy'], r'[A-Za-z0-9][A-Za-z0-9-]{0,38}') and reference(approval['reference'])
                and created <= timestamp(approval['approvedAtUtc']) <= now
                and timestamp(approval['approvedAtUtc']) < expires, 'EXCEPTION_APPROVAL_INVALID')
    return entries


def evaluate(document: dict, policy: Any, operations: dict, evaluated_at: str) -> dict:
    """Evaluate declared metadata only; this does not authenticate an approval."""
    document = normalize_document(document, operations)
    entries = validate_policy(policy, operations, evaluated_at)
    results, counts = [], Counter()
    for finding in document['findings']:
        matched = next((e for e in entries if e['tool'] == document['scanner']['id']
                        and e['fingerprint'] == finding['fingerprint'] and e['scope'] == finding['scope']
                        and e['ruleId'] == finding['ruleId']), None)
        if finding['severity'] in {'Critical', 'High'} or finding['findingClass'] != 'generic':
            outcome = 'BLOCK_NON_SUPPRESSIBLE'
        elif finding['severity'] == 'Medium':
            outcome = ('BLOCK_UNAPPROVED_MEDIUM' if matched is None else
                       'BLOCK_EXPIRED_EXCEPTION' if timestamp(matched['expiresAtUtc']) <= timestamp(evaluated_at) else
                       'MATCHED_DECLARED_EXCEPTION')
        else:
            outcome = 'REPORT_ONLY'
        counts[outcome] += 1
        results.append({**finding, 'suppressionMatch': outcome,
                        'exceptionId': matched['id'] if matched and outcome == 'MATCHED_DECLARED_EXCEPTION' else None})
    execution = document['execution']
    blocked = execution['status'] != 'PASS_EXECUTED' or execution['exitCode'] != 0 or any(k.startswith('BLOCK_') for k in counts)
    return {'schemaVersion': 1, 'control': 'SEC-08', 'activation': 'NOT_ACTIVATED', 'initialInventory': 'UNREVIEWED',
            'evaluationScope': 'OFFLINE_DECLARED_METADATA_ONLY', 'liveGateQualified': False,
            'approvalVerified': False, 'evaluatedAtUtc': evaluated_at, 'sourceSha': document['sourceSha'],
            'scanner': document['scanner'], 'configSha256': document['configSha256'], 'execution': execution,
            'decision': 'BLOCK' if blocked else 'PASS_OFFLINE_EVALUATION_ONLY',
            'summary': {'findings': len(results), 'bySeverity': dict(sorted(Counter(f['severity'] for f in results).items())),
                        'byOutcome': dict(sorted(counts.items()))}, 'findings': results}


def route_scope(instance: dict, role: str, operations: dict) -> dict:
    allowed = {'method', 'parameter', 'path', 'queryParameterNames'}
    require(isinstance(instance, dict) and {'method', 'parameter', 'path'} <= set(instance) <= allowed, 'ZAP_INSTANCE_INVALID')
    method, path = instance['method'], instance['path']
    require(method in METHODS and isinstance(path, str) and len(path) <= 2048
            and '?' not in path and '#' not in path and '%' not in path, 'ZAP_SCOPE_INVALID')
    candidates = []
    for verb, route in operations:
        expression = '/'.join('[^/]+' if p.startswith('{') else re.escape(p) for p in route.split('/'))
        if verb == method and re.fullmatch(expression, path):
            candidates.append((sum(not p.startswith('{') for p in route.split('/')), route))
    candidates.sort(reverse=True)
    require(bool(candidates) and (len(candidates) == 1 or candidates[0][0] != candidates[1][0]), 'ZAP_ROUTE_UNCLASSIFIED')
    route = candidates[0][1]
    parameter = instance['parameter']
    require(isinstance(parameter, str), 'ZAP_PARAMETER_UNCLASSIFIED')
    categories = [category for category, names in operations[(method, route)].items() if parameter in names]
    require(parameter == '' or len(categories) == 1, 'ZAP_PARAMETER_UNCLASSIFIED')
    return {'role': role, 'method': method, 'routeClass': route,
            'parameterCategory': categories[0] if parameter else 'none', 'parameterName': parameter or None}


def adapt_zap(native: Any, operations: dict, source_sha: str, contract_sha: str) -> dict:
    """Project existing sanitized SEC-06 metadata; do not import bodies or names."""
    fields(native, {'schemaVersion', 'control', 'scanner', 'role', 'target', 'inputs', 'sanitization',
                    'summary', 'alerts', 'attribution'}, 'ZAP_REPORT_INVALID')
    require(type(native['schemaVersion']) is int and native['schemaVersion'] == 1
            and native['control'] == 'SEC-06' and native['role'] in ROLES, 'ZAP_REPORT_INVALID')
    fields(native['scanner'], {'name', 'version', 'image', 'exitCode'}, 'ZAP_SCANNER_INVALID')
    require(native['scanner']['name'] == 'OWASP ZAP' and token(native['scanner']['image'], r'zaproxy/zap-stable:[0-9.]+@sha256:[0-9a-f]{64}'), 'ZAP_SCANNER_INVALID')
    fields(native['target'], {'origin', 'isolated', 'externalNetworkAccess'}, 'ZAP_TARGET_INVALID')
    require(native['target']['origin'] == 'http://app:8080' and native['target']['isolated'] is True
            and native['target']['externalNetworkAccess'] is False, 'ZAP_TARGET_INVALID')
    fields(native['inputs'], {'openApiSha256', 'automationPlanSha256', 'policySha256', 'addonListSha256'}, 'ZAP_INPUTS_INVALID')
    require(all(token(v, r'[0-9a-f]{64}') for v in native['inputs'].values())
            and native['inputs']['openApiSha256'] == contract_sha, 'ZAP_CONTRACT_MISMATCH')
    fields(native['sanitization'], {'forbiddenValueCount', 'unsanitizedAllowed'}, 'ZAP_SANITIZATION_INVALID')
    require(type(native['sanitization']['forbiddenValueCount']) is int and native['sanitization']['forbiddenValueCount'] > 0
            and native['sanitization']['unsanitizedAllowed'] is False, 'ZAP_SANITIZATION_INVALID')
    require(isinstance(native['alerts'], list) and len(native['alerts']) <= MAX_FINDINGS, 'ZAP_ALERT_LIMIT_INVALID')
    findings, risk_counts, rule_counts, instance_counts = [], Counter(), Counter(), Counter()
    confidences = {'': 'unknown', '0': 'unknown', '1': 'low', '2': 'medium', '3': 'high', '4': 'confirmed'}
    for alert in native['alerts']:
        fields(alert, {'ruleId', 'name', 'risk', 'confidence', 'cweId', 'wascId', 'instanceCount', 'instances'}, 'ZAP_ALERT_INVALID')
        require(token(alert['ruleId'], r'[0-9]{1,8}') and alert['risk'] in RISKS
                and alert['confidence'] in confidences and isinstance(alert['instances'], list)
                and type(alert['instanceCount']) is int and 0 < alert['instanceCount'] == len(alert['instances']) <= MAX_FINDINGS,
                'ZAP_ALERT_INVALID')
        risk_counts[alert['risk']] += 1
        rule_counts[alert['ruleId']] += 1
        instance_counts[alert['ruleId']] += len(alert['instances'])
        for instance in alert['instances']:
            findings.append({'ruleId': alert['ruleId'], 'severity': alert['risk'],
                             'confidence': confidences[alert['confidence']], 'findingClass': 'generic',
                             'scope': route_scope(instance, native['role'], operations)})
    expected = {'uniqueAlertsByRisk': {r: risk_counts[r] for r in ('High', 'Medium', 'Low', 'Informational')},
                'uniqueAlertsByRule': dict(sorted(rule_counts.items())), 'instancesByRule': dict(sorted(instance_counts.items())),
                'blockingHighAlerts': risk_counts['High']}
    fields(native['summary'], set(expected), 'ZAP_SUMMARY_INVALID')
    for field in ('uniqueAlertsByRisk', 'uniqueAlertsByRule', 'instancesByRule'):
        require(isinstance(native['summary'][field], dict)
                and all(type(v) is int and v >= 0 for v in native['summary'][field].values()), 'ZAP_SUMMARY_INVALID')
    require(type(native['summary']['blockingHighAlerts']) is int
            and native['summary'] == expected, 'ZAP_SUMMARY_INVALID')
    return {'schemaVersion': 1, 'scanner': {'id': 'zap-api', 'version': native['scanner']['version']},
            'sourceSha': source_sha, 'configSha256': native['inputs']['policySha256'],
            'execution': {'status': 'PASS_EXECUTED' if native['scanner']['exitCode'] == 0 else 'SCANNER_FAILURE',
                          'exitCode': native['scanner']['exitCode']}, 'findings': findings}


def forbidden_values() -> list[str]:
    try:
        values = json.loads(os.environ.get(FORBIDDEN_ENV, ''), object_pairs_hook=unique_object)
        require(isinstance(values, list) and 0 < len(values) <= 1000
                and all(isinstance(v, str) and 0 < len(v) <= 8192 for v in values), 'SANITIZER_VALUES_REQUIRED')
        return values
    except (ValueError, RecursionError):
        raise PolicyError('SANITIZER_VALUES_REQUIRED') from None


def ensure_safe(rendered: str, secrets: list[str]) -> None:
    decoded = unquote(rendered)
    for secret in secrets:
        require(not any(value in rendered or value in decoded for value in
                        (secret, json.dumps(secret)[1:-1], quote(secret, safe=''))), 'SANITIZATION_FAILED')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument('--input', type=Path, help='Closed common report, not raw scanner traffic')
    source.add_argument('--zap-report', type=Path, help='Existing sanitized SEC-06 JSON')
    parser.add_argument('--source-sha', help='Required with --zap-report; declared producer source')
    parser.add_argument('--contract', type=Path, required=True)
    parser.add_argument('--policy', type=Path, required=True)
    parser.add_argument('--evaluated-at', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    try:
        secrets = forbidden_values()
        contract, contract_sha = read_json(args.contract)
        operations = contract_operations(contract)
        policy, policy_sha = read_json(args.policy)
        report, report_sha = read_json(args.zap_report or args.input)
        if args.zap_report:
            report = adapt_zap(report, operations, args.source_sha, contract_sha)
        else:
            require(args.source_sha is None, 'SOURCE_ARGUMENT_INVALID')
        result = evaluate(report, policy, operations, args.evaluated_at)
        result['inputHashes'] = {'reportSha256': report_sha, 'contractSha256': contract_sha, 'policySha256': policy_sha}
        rendered = json.dumps(result, sort_keys=True, indent=2) + '\n'
        require(len(rendered.encode('utf8')) <= LIMIT, 'OUTPUT_LIMIT_INVALID')
        ensure_safe(rendered, secrets)
        # Exclusive creation avoids overwriting any earlier receipt, including failures.
        with args.output.open('x', encoding='utf8', newline='\n') as handle:
            handle.write(rendered)
        print('SEC-08 offline evaluation: ' + result['decision'] + '; NOT_ACTIVATED')
        return 1 if result['decision'] == 'BLOCK' else 0
    except (PolicyError, TypeError, KeyError, OSError, RecursionError):
        # No paths, input values, scanner messages or exception text are printed.
        print('SEC-08 offline evaluation rejected: INVALID_OR_UNSAFE_INPUT; NOT_ACTIVATED', file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
