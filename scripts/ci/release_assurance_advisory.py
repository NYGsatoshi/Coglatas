#!/usr/bin/env python3
"""Collect bounded, exact-subject SEC-14 consistency observations in Advisory mode.

Supplied expectations and JSON receipts do not authenticate GitHub, a scanner,
Cosign, owner review or execution. Missing advanced adapters remain visible.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any

import functional_evidence
import release_supply_chain as release
import verify_grype_sbom_source as grype_source
import verify_sbom_vulnerability_gate as gate

SCHEMA = 'coglatas-release-assurance-advisory-v1'
GRYPE_VERSION = '0.118.0'
TRIVY_VERSION = '0.65.0'
SYFT_VERSION = '1.51.0'
RUN_RE = re.compile(
    r'https://github\.com/(?P<repository>[A-Za-z0-9_.-]{1,100}/[A-Za-z0-9_.-]{1,100})'
    r'/actions/runs/(?P<run>[1-9][0-9]{0,19})/attempts/(?P<attempt>[1-9][0-9]{0,19})'
)
# Existing #614 categories are an evidence inventory, not newly allocated SPECs.
TIER_B = ('SEC-04', 'SEC-05', 'SEC-06', 'SEC-07', 'SEC-08', 'SEC-09',
          'SEC-10', 'SEC-12', 'SEC-13', 'SEC-15', 'SEC-16', 'SEC-17',
          'SEC-18', 'SEC-20')
SCHEMATHESIS_ROLES = ('anonymous', 'alpha-restricted', 'alpha-member',
                     'alpha-owner', 'beta-owner')
ZAP_ROLES = ('alpha-owner', 'alpha-restricted', 'beta-owner')
ZAP_IMAGE = ('zaproxy/zap-stable:2.17.0@sha256:'
             '781a2bdaea47324e7bab583e2263f21d257b0aee61ed51521a5be45f5f5081ef')
SCHEMATHESIS_FIELDS = {'contract_sha256', 'lane', 'network_errors',
                      'operation_count', 'operations', 'request_count', 'role',
                      'scanner_exit', 'schemathesis_version', 'seed'}
ZAP_FIELDS = {'control', 'role', 'status', 'scannerVersion', 'scannerImage',
              'openApiSha256', 'automationPlanSha256', 'policySha256',
              'addonListSha256', 'forbiddenValueCount', 'unsanitizedAllowed',
              'highAlerts', 'mediumAlerts', 'lowAlerts', 'informationalAlerts'}


class AdvisoryError(ValueError):
    def __init__(self, code: str):
        super().__init__(code)
        self.code = code


def require(condition: bool, code: str) -> None:
    if not condition:
        raise AdvisoryError(code)


def read_artifact(path: Path, label: str) -> tuple[dict[str, Any], str]:
    # Hash and parse the same bounded snapshot, including during file replacement.
    try:
        with path.open('rb') as handle:
            raw = handle.read(release.MAX_JSON_BYTES + 1)
        document = release.parse_json_bytes(raw)
    except (OSError, release.ReleaseEvidenceError):
        raise AdvisoryError(f'{label}_INPUT_INVALID') from None
    return document, hashlib.sha256(raw).hexdigest()


def verifier_source_hashes() -> dict[str, str]:
    hashes = {}
    for module in (sys.modules[__name__], release, grype_source, gate, functional_evidence):
        path = Path(module.__file__)
        try:
            with path.open('rb') as handle:
                raw = handle.read(release.MAX_JSON_BYTES + 1)
            require(0 < len(raw) <= release.MAX_JSON_BYTES, 'VERIFIER_SOURCE_SIZE_INVALID')
        except OSError:
            raise AdvisoryError('VERIFIER_SOURCE_UNAVAILABLE') from None
        hashes[path.name] = hashlib.sha256(raw).hexdigest()
    return hashes


def parse_run(value: str) -> re.Match[str]:
    require(isinstance(value, str) and len(value) <= 320, 'RUN_IDENTITY_INVALID')
    match = RUN_RE.fullmatch(value)
    require(match is not None, 'RUN_IDENTITY_INVALID')
    assert match is not None
    return match


def validate_context(args: argparse.Namespace) -> tuple[str, re.Match[str]]:
    try:
        release.require_git_sha(args.expected_repository_sha)
        repository, digest = release.parse_subject(args.expected_subject)
    except release.ReleaseEvidenceError:
        raise AdvisoryError('EXPECTED_IDENTITY_INVALID') from None
    run = parse_run(args.expected_run_identity)
    require(repository == f"ghcr.io/{run['repository'].lower()}", 'REPOSITORY_MISMATCH')
    if args.tier_b_run_identity:
        require(parse_run(args.tier_b_run_identity)['repository'] == run['repository'],
                'NATIVE_REPOSITORY_MISMATCH')
    return digest, run


def validate_image_observation(observation: dict[str, Any], subject: str,
                               trivy: dict[str, Any]) -> str:
    require(set(observation) == {'imageId', 'repoDigests'}, 'IMAGE_OBSERVATION_SCHEMA')
    image_id = observation['imageId']
    digests = observation['repoDigests']
    require(isinstance(image_id, str) and gate.IMAGE_DIGEST_RE.fullmatch(image_id) is not None,
            'IMAGE_CONFIGURATION_INVALID')
    require(isinstance(digests, list) and 0 < len(digests) <= 32
            and all(isinstance(item, str) and len(item) <= 512 for item in digests)
            and subject in digests, 'DOCKER_SUBJECT_MISMATCH')
    metadata = trivy.get('Metadata')
    require(isinstance(metadata, dict), 'TRIVY_IMAGE_METADATA_MISSING')
    require(trivy.get('ArtifactName') == subject
            and isinstance(metadata.get('RepoDigests'), list)
            and subject in metadata['RepoDigests'], 'TRIVY_SUBJECT_MISMATCH')
    require(metadata.get('ImageID') == image_id, 'TRIVY_CONFIGURATION_MISMATCH')
    return image_id


def validate_sbom(metadata: dict[str, Any], sbom: dict[str, Any], spdx: dict[str, Any],
                  hashes: dict[str, str], args: argparse.Namespace, digest: str,
                  run: re.Match[str], now: Any) -> dict[str, Any]:
    require(metadata.get('schema') == release.SBOM_EVIDENCE_SCHEMA
            and metadata.get('sourceKind') == 'image', 'SBOM_SCHEMA_INVALID')
    require(metadata.get('syftVersion') == SYFT_VERSION, 'SBOM_PRODUCER_VERSION_MISMATCH')
    require(metadata.get('repositoryCommit') == args.expected_repository_sha,
            'SBOM_CANDIDATE_MISMATCH')
    require(metadata.get('imageOrReleaseDigest') == digest, 'SBOM_SUBJECT_MISMATCH')
    require(metadata.get('runIdentity') == f"{run['run']}/{run['attempt']}/publish-release-image",
            'SBOM_PRODUCER_RUN_MISMATCH')
    formats = metadata.get('formats')
    require(isinstance(formats, dict), 'SBOM_FORMATS_MISSING')
    for format_name, file_name, label in (
        ('cyclonedx-json', 'sbom.cyclonedx.json', 'cyclonedx'),
        ('spdx-json', 'sbom.spdx.json', 'spdx'),
    ):
        entry = formats.get(format_name)
        require(isinstance(entry, dict) and entry.get('file') == file_name
                and entry.get('sha256') == hashes[label], 'SBOM_BYTES_MISMATCH')
    require(sbom.get('bomFormat') == 'CycloneDX'
            and isinstance(sbom.get('components'), list) and len(sbom['components']) > 0,
            'SBOM_COMPONENTS_MISSING')
    try:
        root_name, root_version, _ = grype_source.canonical_image_identity(sbom)
    except grype_source.SourceBindingError:
        raise AdvisoryError('SBOM_CONTAINER_ROOT_MISSING') from None
    # Pinned Syft's registry formatter uses UserInput and ManifestDigest here.
    # Its bom-ref is an opaque artifact reference, not the Docker config ID.
    require(root_name == args.expected_subject and root_version == digest,
            'SBOM_CONTAINER_SUBJECT_MISMATCH')
    require(isinstance(spdx.get('spdxVersion'), str)
            and spdx['spdxVersion'].startswith('SPDX-')
            and isinstance(spdx.get('packages'), list) and len(spdx['packages']) > 0,
            'SPDX_PACKAGES_MISSING')
    generated = gate._parse_timestamp(metadata.get('generationTimestampUtc'), 'SBOM generation')
    gate._require_age_within(generated, now, 2.0, 'SBOM evidence')
    return {'sha256': hashes['cyclonedx'],
            'generatedAtUtc': generated.isoformat().replace('+00:00', 'Z'),
            'componentCount': len(sbom['components'])}


def native_observations(args: argparse.Namespace, run: re.Match[str],
                        hashes: dict[str, str]) -> dict[str, Any]:
    require(len(args.schemathesis_metadata) <= len(SCHEMATHESIS_ROLES)
            and len(args.zap_metadata) <= len(ZAP_ROLES), 'NATIVE_SCOPE_OVERFLOW')
    contract_hash = None
    if args.schemathesis_metadata or args.zap_metadata:
        require(args.open_api is not None, 'NATIVE_CONTRACT_MISSING')
        _, contract_hash = read_artifact(Path(args.open_api), 'OPEN_API')
        hashes['nativeOpenApi'] = contract_hash
    result: dict[str, Any] = {'functionalFull': None, 'schemathesisDeep': [], 'zapApi': []}
    if args.functional_manifest:
        document, digest = read_artifact(Path(args.functional_manifest), 'FUNCTIONAL')
        native_run = parse_run(args.tier_b_run_identity or args.expected_run_identity)
        require(native_run['repository'] == run['repository'], 'NATIVE_REPOSITORY_MISMATCH')
        try:
            functional_evidence.validate_manifest(document, args.expected_repository_sha,
                                                  'functional-full', native_run['run'],
                                                  native_run['attempt'])
        except (ValueError, TypeError, KeyError):
            raise AdvisoryError('FUNCTIONAL_SCOPE_OR_IDENTITY_INVALID') from None
        hashes['functionalFull'] = digest
        result['functionalFull'] = {
            'nativeOutcome': 'PASS', 'candidateAndRunConsistency': 'MATCHED',
            'runIdentity': native_run.group(0), 'domainCount': len(document['lanes']),
            'journeyCount': sum(len(lane['journeys']) for lane in document['lanes']),
            'imageSubjectBinding': 'UNVERIFIED', 'executionAuthentication': 'UNVERIFIED',
            'securityContractCompleteness': 'UNVERIFIED',
        }
    for path in args.schemathesis_metadata:
        document, digest = read_artifact(Path(path), 'SCHEMATHESIS')
        require(set(document) == SCHEMATHESIS_FIELDS, 'SCHEMATHESIS_SCHEMA_INVALID')
        role = document['role']
        require(role in SCHEMATHESIS_ROLES
                and role not in [item['role'] for item in result['schemathesisDeep']],
                'SCHEMATHESIS_ROLE_DUPLICATE_OR_INVALID')
        require(document['contract_sha256'] == contract_hash
                and document['lane'] == 'deep'
                and document['schemathesis_version'] == '4.25.2', 'SCHEMATHESIS_INPUT_MISMATCH')
        for field in ('request_count', 'operation_count', 'seed'):
            require(type(document[field]) is int and 0 < document[field] < 2**31,
                    'SCHEMATHESIS_EXECUTION_MISSING')
        for field in ('scanner_exit', 'network_errors'):
            require(type(document[field]) is int and document[field] == 0,
                    'SCHEMATHESIS_EXECUTION_ERROR')
        operations = document['operations']
        require(isinstance(operations, list) and len(operations) == document['operation_count']
                and all(isinstance(item, str) and 0 < len(item) <= 512 for item in operations)
                and len(set(operations)) == len(operations), 'SCHEMATHESIS_SCOPE_INVALID')
        hashes[f'schemathesis:{role}'] = digest
        result['schemathesisDeep'].append({
            'role': role, 'nativeOutcome': 'PASS', 'requestCount': document['request_count'],
            'operationCount': document['operation_count'], 'candidateAndRunConsistency': 'UNVERIFIED',
            'imageSubjectBinding': 'UNVERIFIED', 'executionAuthentication': 'UNVERIFIED',
            'securityContractCompleteness': 'UNVERIFIED',
        })
    for path in args.zap_metadata:
        document, digest = read_artifact(Path(path), 'ZAP')
        require(set(document) == ZAP_FIELDS, 'ZAP_SCHEMA_INVALID')
        role = document['role']
        require(role in ZAP_ROLES and role not in [item['role'] for item in result['zapApi']],
                'ZAP_ROLE_DUPLICATE_OR_INVALID')
        require(document['control'] == 'SEC-06' and document['openApiSha256'] == contract_hash
                and document['scannerVersion'] == '2.17.0' and document['scannerImage'] == ZAP_IMAGE,
                'ZAP_INPUT_MISMATCH')
        for field in ('automationPlanSha256', 'policySha256', 'addonListSha256'):
            require(isinstance(document[field], str)
                    and release.SHA256_RE.fullmatch(document[field]) is not None,
                    'ZAP_INPUT_HASH_INVALID')
        require(document['status'] in ('passed', 'blocked-high', 'scanner-failed'), 'ZAP_OUTCOME_INVALID')
        require(document['unsanitizedAllowed'] is False, 'ZAP_UNSANITIZED_INPUT')
        for field in ('forbiddenValueCount', 'highAlerts', 'mediumAlerts', 'lowAlerts', 'informationalAlerts'):
            require(type(document[field]) is int and 0 <= document[field] < 2**31, 'ZAP_COUNT_INVALID')
        require((document['highAlerts'] == 0) == (document['status'] != 'blocked-high'),
                'ZAP_OUTCOME_INCONSISTENT')
        hashes[f'zap:{role}'] = digest
        result['zapApi'].append({
            'role': role, 'nativeOutcome': document['status'], 'highAlerts': document['highAlerts'],
            'candidateAndRunConsistency': 'UNVERIFIED', 'imageSubjectBinding': 'UNVERIFIED',
            'executionAuthentication': 'UNVERIFIED', 'requestCoverage': 'UNVERIFIED',
            'planAndPolicyBytesBinding': 'UNVERIFIED', 'securityContractCompleteness': 'UNVERIFIED',
        })
    result['schemathesisDeep'].sort(key=lambda item: item['role'])
    result['zapApi'].sort(key=lambda item: item['role'])
    result['outstandingSchemathesisRoleCount'] = len(SCHEMATHESIS_ROLES) - len(result['schemathesisDeep'])
    result['outstandingZapRoleCount'] = len(ZAP_ROLES) - len(result['zapApi'])
    return result


def collect(args: argparse.Namespace) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    digest, run = validate_context(args)
    now = gate._utc_now(args.now)
    hashes: dict[str, str] = {}
    documents: dict[str, dict[str, Any]] = {}
    for label, path in (
        ('cyclonedx', Path(args.sbom_directory) / 'sbom.cyclonedx.json'),
        ('spdx', Path(args.sbom_directory) / 'sbom.spdx.json'),
        ('sbomMetadata', Path(args.sbom_directory) / 'metadata.json'),
        ('dockerObservation', Path(args.scan_directory) / 'docker-image-observation.json'),
        ('grypeReport', Path(args.scan_directory) / 'grype-vulnerabilities.json'),
        ('grypeVersion', Path(args.scan_directory) / 'grype-version.json'),
        ('grypeDb', Path(args.scan_directory) / 'grype-db-status.json'),
        ('trivyReport', Path(args.scan_directory) / 'trivy-vulnerabilities.json'),
        ('trivyVersion', Path(args.scan_directory) / 'trivy-version.json'),
        ('vulnerabilityPolicy', Path(args.policy)),
    ):
        documents[label], hashes[label] = read_artifact(path, label.upper())
    sbom_evidence = validate_sbom(documents['sbomMetadata'], documents['cyclonedx'],
                                  documents['spdx'], hashes, args, digest, run, now)
    image_id = validate_image_observation(documents['dockerObservation'], args.expected_subject,
                                          documents['trivyReport'])
    try:
        grype_source.validate_source(documents['grypeReport'], documents['cyclonedx'],
                                     (Path(args.sbom_directory) / 'sbom.cyclonedx.json').resolve())
    except grype_source.SourceBindingError:
        raise AdvisoryError('GRYPE_SBOM_SOURCE_MISMATCH') from None
    native_args = argparse.Namespace(grype_version=GRYPE_VERSION, trivy_version=TRIVY_VERSION,
                                     max_grype_db_age_hours=120.0, max_trivy_db_age_hours=48.0)
    normalized, summary = gate.evaluate_documents(
        native_args, digest, now, sbom_evidence, documents['grypeReport'],
        documents['grypeVersion'], documents['grypeDb'], documents['trivyReport'],
        documents['trivyVersion'], documents['vulnerabilityPolicy'], trivy_digest=image_id,
    )
    observations = native_observations(args, run, hashes)
    controls = [{
        'control': control,
        'mechanicalObservation': 'MATCHED' if control in ('SEC-09', 'SEC-10') else 'UNVERIFIED',
        'acceptanceQualification': 'UNVERIFIED',
        'applicability': 'UNDECIDED' if control == 'SEC-18' else 'REQUIRED',
    } for control in TIER_B]
    report = {
        'schema': SCHEMA, 'verifierId': 'SEC-14-RELEASE-CONSISTENCY', 'verifierVersion': '1',
        'mode': 'ADVISORY', 'integrityStatus': 'CONSISTENT',
        'verifierSourceBytesSha256': verifier_source_hashes(),
        'verifierSourceToCandidateAuthentication': 'UNVERIFIED',
        'repositoryCommit': args.expected_repository_sha, 'subject': args.expected_subject,
        'subjectDigest': digest, 'imageConfigurationDigest': image_id,
        'runIdentity': args.expected_run_identity,
        'sbomProducerRunIdentity': documents['sbomMetadata']['runIdentity'],
        'buildIdentityKind': 'OCI_MANIFEST_AND_CONFIGURATION',
        'buildAndCandidateConsistency': 'MATCHED', 'inputSha256': hashes,
        'nativeVulnerabilityDecision': summary['decision'],
        'normalizedFindingCounts': summary['normalizedFindingCounts'],
        'blockedFindingCount': len(summary['blockedFindings']),
        'waivedFindingCount': len(summary['waivedFindings']),
        'exceptionApprovalAuthentication': 'UNVERIFIED',
        'mediumRatchetMode': summary['mediumRatchet']['mode'],
        'githubRunAuthentication': 'UNVERIFIED', 'scannerExecutionAuthentication': 'UNVERIFIED',
        'cryptographicVerification': 'UNVERIFIED', 'personalOwnerApproval': 'UNVERIFIED',
        'nativeTierBObservations': observations, 'tierBControls': controls,
        'tierBControlCount': len(TIER_B), 'mechanicallyBoundControlCount': 2,
        'acceptanceQualifiedControlCount': 0, 'outstandingAcceptanceControlCount': len(TIER_B),
        'advancedAdapters': {'RESTler': 'MISSING', 'browserAjaxZap': 'MISSING',
                             'imageArtifactSecrets': 'MISSING', 'approvedOast': 'OWNER_DISPOSITION_PENDING',
                             'advancedAcceptanceNormalization': 'MISSING'},
        'missingAdvancedAdapterCount': 4, 'optionalBurp': 'NOT_SUPPLIED_OPTIONAL',
        'releaseAcceptance': 'BLOCKED', 'preAvaloniaVerdict': 'BLOCKED',
        'blindSpots': [
            'Inputs and supplied expectations are unauthenticated local consistency observations.',
            'On-disk verifier source hashes do not authenticate loaded code or source-to-candidate ancestry.',
            'Legacy Tier B metadata has no immutable release-image provenance.',
            'Functional journeys do not establish exhaustive security-contract coverage.',
            'Missing advanced adapters and undecided OAST applicability cannot be NOT_APPLICABLE.',
            'No Cosign result or personal owner approval is authenticated by this receipt.',
            'Open source-dependency alerts remain separate from shipped-image observations.',
            'The Advisory job is not a release promotion prerequisite or enforcement promotion.',
        ],
    }
    return report, normalized, summary


def rendered_bytes(value: dict[str, Any]) -> bytes:
    raw = (json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + '\n').encode('utf-8')
    require(len(raw) <= release.MAX_JSON_BYTES, 'OUTPUT_SIZE_LIMIT')
    return raw


def write_exclusive(path: Path, value: dict[str, Any]) -> str:
    raw = rendered_bytes(value)
    with path.open('xb') as handle:
        handle.write(raw)
    return hashlib.sha256(raw).hexdigest()


def policy_exit(report: dict[str, Any]) -> int:
    native_failure = any(item['nativeOutcome'] != 'passed'
                         for item in report['nativeTierBObservations']['zapApi'])
    return 1 if report['nativeVulnerabilityDecision'] == 'block' or native_failure else 0


def verify_record(args: argparse.Namespace) -> int:
    """Recompute retained outputs; never modify producer or historical bytes."""
    report, normalized, summary = collect(args)
    directory = Path(args.verify_directory)
    recorded, _ = read_artifact(directory / 'release-assurance-advisory.json', 'RECORDED_ADVISORY')
    normalized_record, normalized_hash = read_artifact(directory / 'vulnerabilities.normalized.json',
                                                       'RECORDED_NORMALIZED')
    summary_record, summary_hash = read_artifact(directory / 'vulnerability-gate-summary.json',
                                                 'RECORDED_SUMMARY')
    require(normalized_record == normalized and summary_record == summary,
            'RECORDED_NATIVE_RESULT_MISMATCH')
    report['outputSha256'] = {
        'normalized': hashlib.sha256(rendered_bytes(normalized)).hexdigest(),
        'nativeSummary': hashlib.sha256(rendered_bytes(summary)).hexdigest(),
    }
    require(report['outputSha256'] == {'normalized': normalized_hash, 'nativeSummary': summary_hash},
            'RECORDED_NATIVE_BYTES_MISMATCH')
    require(recorded == report, 'RECORDED_ADVISORY_MISMATCH')
    print('SEC-14 Advisory retained consistency=MATCHED release=BLOCKED authentication=UNVERIFIED')
    return policy_exit(report)


def command(args: argparse.Namespace) -> int:
    output = Path(args.out_directory)
    try:
        output.mkdir(parents=True, exist_ok=False)
    except OSError:
        raise AdvisoryError('OUTPUT_ALREADY_EXISTS_OR_UNAVAILABLE') from None
    try:
        report, normalized, summary = collect(args)
        report['outputSha256'] = {
            'normalized': write_exclusive(output / 'vulnerabilities.normalized.json', normalized),
            'nativeSummary': write_exclusive(output / 'vulnerability-gate-summary.json', summary),
        }
        write_exclusive(output / 'release-assurance-advisory.json', report)
        print(f"SEC-14 Advisory consistency={report['integrityStatus']} "
              f"vulnerability-policy={report['nativeVulnerabilityDecision']} release=BLOCKED "
              f"outstanding-controls={report['outstandingAcceptanceControlCount']}")
        return policy_exit(report)
    except (AdvisoryError, gate.GateError, release.ReleaseEvidenceError) as error:
        # Do not echo attacker-provided JSON values, filenames or diagnostic bodies.
        code = error.code if isinstance(error, AdvisoryError) else 'NATIVE_VULNERABILITY_INPUT_INVALID'
        write_exclusive(output / 'release-assurance-advisory.json', {
            'schema': SCHEMA, 'mode': 'ADVISORY', 'integrityStatus': 'ERROR', 'errorCode': code,
            'releaseAcceptance': 'BLOCKED', 'preAvaloniaVerdict': 'BLOCKED',
            'githubRunAuthentication': 'UNVERIFIED', 'cryptographicVerification': 'UNVERIFIED',
            'personalOwnerApproval': 'UNVERIFIED', 'acceptanceQualifiedControlCount': 0,
            'outstandingAcceptanceControlCount': len(TIER_B),
        })
        print(f'SEC-14 Advisory evidence error: {code}', file=sys.stderr)
        return 1


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--expected-repository-sha', required=True)
    parser.add_argument('--expected-subject', required=True)
    parser.add_argument('--expected-run-identity', required=True)
    parser.add_argument('--sbom-directory', required=True)
    parser.add_argument('--scan-directory', required=True)
    parser.add_argument('--policy', required=True)
    output = parser.add_mutually_exclusive_group(required=True)
    output.add_argument('--out-directory')
    output.add_argument('--verify-directory')
    parser.add_argument('--functional-manifest')
    parser.add_argument('--tier-b-run-identity')
    parser.add_argument('--schemathesis-metadata', action='append', default=[])
    parser.add_argument('--zap-metadata', action='append', default=[])
    parser.add_argument('--open-api')
    parser.add_argument('--now', help=argparse.SUPPRESS)
    return parser


def main() -> int:
    try:
        args = build_parser().parse_args()
        return verify_record(args) if args.verify_directory else command(args)
    except (AdvisoryError, gate.GateError, release.ReleaseEvidenceError, OSError) as error:
        code = error.code if isinstance(error, AdvisoryError) else 'ADVISORY_INPUT_OR_OUTPUT_INVALID'
        print(f'SEC-14 Advisory evidence error: {code}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
