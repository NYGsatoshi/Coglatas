#!/usr/bin/env python3
"""SEC-11 release signing evidence and policy helpers.

Cryptographic operations stay in Cosign. This module validates the digest-bound
artifacts crossing job boundaries, constructs a minimal SLSA v1 provenance
predicate, emits exact-predicate Rego policies for Cosign verification, and only
finalizes retained evidence when explicit verification markers exist.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path
from typing import Any, NoReturn

EVIDENCE_SCHEMA = "coglatas-release-signing-evidence-v1"
SBOM_EVIDENCE_SCHEMA = "coglatas-sbom-evidence-v1"
VERIFICATION_SCHEMA = "coglatas-release-signing-verification-v1"
OIDC_ISSUER = "https://token.actions.githubusercontent.com"
GIT_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
SHA256_RE = re.compile(r"^[0-9a-f]{64}$")
SUBJECT_RE = re.compile(
    r"^(?P<repository>ghcr\.io/[a-z0-9._/-]+)@sha256:(?P<digest>[0-9a-f]{64})$"
)
PREDICATE_TYPES = {
    "cyclonedx": "https://cyclonedx.org/bom",
    "spdxjson": "https://spdx.dev/Document",
    "slsaprovenance1": "https://slsa.dev/provenance/v1",
}
FORBIDDEN_KEY_RE = re.compile(r"(?:token|secret|password|private.?key)", re.IGNORECASE)
FORBIDDEN_VALUE_RE = re.compile(
    r"(?:github_pat_|gh[oprsu]_[A-Za-z0-9_]{20,}|-----BEGIN [A-Z ]*PRIVATE KEY-----)"
)


class ReleaseEvidenceError(ValueError):
    """Raised when release supply-chain evidence is inconsistent."""


def fail(message: str) -> NoReturn:
    raise ReleaseEvidenceError(message)


def read_json(path: Path) -> dict[str, Any]:
    if not path.is_file() or path.stat().st_size == 0:
        fail(f"JSON artifact is missing or empty: {path}")
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        fail(f"JSON artifact is not valid UTF-8 JSON: {path}: {exc}")
    if not isinstance(value, dict):
        fail(f"JSON artifact root must be an object: {path}")
    return value


def write_json(path: Path, value: dict[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        json.dumps(value, indent=2, sort_keys=True, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


def sha256_file(path: Path) -> str:
    if not path.is_file() or path.stat().st_size == 0:
        fail(f"artifact is missing or empty: {path}")
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_subject(subject: str) -> tuple[str, str]:
    match = SUBJECT_RE.fullmatch(subject)
    if match is None:
        fail(
            "release subject must be a lowercase GHCR repository pinned as "
            "ghcr.io/<owner>/<image>@sha256:<64 lowercase hex>"
        )
    return match.group("repository"), f"sha256:{match.group('digest')}"


def require_git_sha(value: str) -> None:
    if not GIT_SHA_RE.fullmatch(value):
        fail("repository SHA must be a lowercase 40-character Git SHA")


def _required_string(mapping: dict[str, Any], key: str, label: str) -> str:
    value = mapping.get(key)
    if not isinstance(value, str) or not value:
        fail(f"{label} is missing required string field {key}")
    return value


def _validate_sbom_format(
    metadata: dict[str, Any],
    format_name: str,
    artifact: Path,
) -> str:
    formats = metadata.get("formats")
    if not isinstance(formats, dict):
        fail("SBOM metadata has no formats map")
    entry = formats.get(format_name)
    if not isinstance(entry, dict):
        fail(f"SBOM metadata is missing {format_name}")
    expected_file = _required_string(entry, "file", f"SBOM {format_name} metadata")
    expected_hash = _required_string(entry, "sha256", f"SBOM {format_name} metadata")
    if Path(expected_file).name != expected_file:
        fail(f"SBOM metadata contains an unsafe file name for {format_name}")
    if expected_file != artifact.name:
        fail(
            f"SBOM metadata file mismatch for {format_name}: "
            f"expected {expected_file}, got {artifact.name}"
        )
    if not SHA256_RE.fullmatch(expected_hash):
        fail(f"SBOM metadata contains an invalid SHA-256 for {format_name}")
    actual_hash = sha256_file(artifact)
    if actual_hash != expected_hash:
        fail(
            f"SBOM payload hash mismatch for {format_name}: "
            f"expected {expected_hash}, got {actual_hash}"
        )
    return actual_hash


def validate_sbom_binding(
    *,
    subject: str,
    repository_sha: str,
    metadata_path: Path,
    cyclonedx_path: Path,
    spdx_path: Path,
) -> tuple[str, str, str]:
    _, subject_digest = parse_subject(subject)
    require_git_sha(repository_sha)
    metadata = read_json(metadata_path)
    if metadata.get("schema") != SBOM_EVIDENCE_SCHEMA:
        fail("SBOM metadata has an unexpected evidence schema")
    if metadata.get("sourceKind") != "image":
        fail("release signing requires an image SBOM")
    if metadata.get("repositoryCommit") != repository_sha:
        fail("SBOM repository commit does not match the release commit")
    if metadata.get("imageOrReleaseDigest") != subject_digest:
        fail("SBOM image digest does not match the final release subject digest")
    cyclonedx_hash = _validate_sbom_format(metadata, "cyclonedx-json", cyclonedx_path)
    spdx_hash = _validate_sbom_format(metadata, "spdx-json", spdx_path)
    return subject_digest, cyclonedx_hash, spdx_hash


def expected_workflow_identity(repository: str, workflow_ref: str) -> str:
    expected_prefix = f"{repository}/.github/workflows/release-supply-chain.yml@"
    if not workflow_ref.startswith(expected_prefix):
        fail(
            "workflow identity must point at "
            f"{repository}/.github/workflows/release-supply-chain.yml"
        )
    return f"https://github.com/{workflow_ref}"


def scan_forbidden_evidence(value: Any, path: str = "$") -> None:
    if isinstance(value, dict):
        for key, child in value.items():
            if FORBIDDEN_KEY_RE.search(str(key)):
                fail(f"evidence contains forbidden secret-like key at {path}.{key}")
            scan_forbidden_evidence(child, f"{path}.{key}")
    elif isinstance(value, list):
        for index, child in enumerate(value):
            scan_forbidden_evidence(child, f"{path}[{index}]")
    elif isinstance(value, str) and FORBIDDEN_VALUE_RE.search(value):
        fail(f"evidence contains forbidden credential material at {path}")


def build_provenance(
    *,
    repository: str,
    repository_sha: str,
    workflow_identity: str,
    workflow_ref: str,
    run_identity: str,
    release_tag: str,
    subject_digest: str,
) -> dict[str, Any]:
    source_uri = f"git+https://github.com/{repository}@{repository_sha}"
    return {
        "buildDefinition": {
            "buildType": workflow_identity,
            "externalParameters": {
                "repository": repository,
                "commit": repository_sha,
                "ref": workflow_ref.rsplit("@", 1)[-1],
                "releaseTag": release_tag,
                "subjectDigest": subject_digest,
            },
            "resolvedDependencies": [
                {
                    "uri": source_uri,
                    "digest": {"gitCommit": repository_sha},
                }
            ],
        },
        "runDetails": {
            "builder": {"id": workflow_identity},
            "metadata": {"invocationId": run_identity},
        },
    }


def build_evidence_command(args: argparse.Namespace) -> None:
    subject_path = Path(args.subject_file).resolve()
    if not subject_path.is_file():
        fail(f"subject file is missing: {subject_path}")
    subject = subject_path.read_text(encoding="utf-8").strip()
    repository_name, subject_digest = parse_subject(subject)
    if repository_name != f"ghcr.io/{args.repository.lower()}":
        fail("release subject repository does not match the GitHub repository after lower-casing")
    require_git_sha(args.repository_sha)
    expected_ref = f"refs/tags/{args.release_tag}"
    if args.github_ref != expected_ref:
        fail(f"release workflow ref mismatch: expected {expected_ref}, got {args.github_ref}")

    metadata_path = Path(args.sbom_metadata).resolve()
    cyclonedx_path = Path(args.cyclonedx).resolve()
    spdx_path = Path(args.spdx).resolve()
    metadata_digest, cyclonedx_hash, spdx_hash = validate_sbom_binding(
        subject=subject,
        repository_sha=args.repository_sha,
        metadata_path=metadata_path,
        cyclonedx_path=cyclonedx_path,
        spdx_path=spdx_path,
    )
    if metadata_digest != subject_digest:
        fail("internal digest consistency check failed")

    workflow_identity = expected_workflow_identity(args.repository, args.workflow_ref)
    if not args.workflow_ref.endswith(f"@{args.github_ref}"):
        fail("workflow identity is not bound to the release tag ref")
    run_identity = (
        f"https://github.com/{args.repository}/actions/runs/"
        f"{args.run_id}/attempts/{args.run_attempt}"
    )

    provenance = build_provenance(
        repository=args.repository,
        repository_sha=args.repository_sha,
        workflow_identity=workflow_identity,
        workflow_ref=args.workflow_ref,
        run_identity=run_identity,
        release_tag=args.release_tag,
        subject_digest=subject_digest,
    )

    out_dir = Path(args.out_dir).resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    provenance_path = out_dir / "provenance.json"
    write_json(provenance_path, provenance)
    provenance_hash = sha256_file(provenance_path)

    evidence = {
        "schema": EVIDENCE_SCHEMA,
        "repository": args.repository,
        "repositoryCommit": args.repository_sha,
        "releaseTag": args.release_tag,
        "subject": subject,
        "subjectDigest": subject_digest,
        "cosignVersion": args.cosign_version,
        "oidcIssuer": OIDC_ISSUER,
        "certificateIdentity": workflow_identity,
        "workflowRef": args.github_ref,
        "workflowIdentity": args.workflow_ref,
        "runIdentity": run_identity,
        "sbom": {
            "cyclonedx": {"file": cyclonedx_path.name, "sha256": cyclonedx_hash},
            "spdx": {"file": spdx_path.name, "sha256": spdx_hash},
        },
        "provenance": {"file": provenance_path.name, "sha256": provenance_hash},
    }
    scan_forbidden_evidence(evidence)
    write_json(out_dir / "release-signing-evidence.json", evidence)


def verify_evidence_values(*, evidence: dict[str, Any], evidence_dir: Path) -> None:
    if evidence.get("schema") != EVIDENCE_SCHEMA:
        fail("release signing evidence has an unexpected schema")
    repository = _required_string(evidence, "repository", "release signing evidence")
    release_tag = _required_string(evidence, "releaseTag", "release signing evidence")
    subject = _required_string(evidence, "subject", "release signing evidence")
    subject_repository, subject_digest = parse_subject(subject)
    if subject_repository != f"ghcr.io/{repository.lower()}":
        fail("release evidence subject repository does not match repository")
    if evidence.get("subjectDigest") != subject_digest:
        fail("release evidence subjectDigest does not match the subject")
    repository_sha = _required_string(evidence, "repositoryCommit", "release signing evidence")
    require_git_sha(repository_sha)
    if evidence.get("oidcIssuer") != OIDC_ISSUER:
        fail("release signing evidence has an unexpected OIDC issuer")

    workflow_ref = _required_string(evidence, "workflowRef", "release signing evidence")
    if workflow_ref != f"refs/tags/{release_tag}":
        fail("release signing evidence workflowRef is not the release tag ref")
    workflow_identity_ref = _required_string(
        evidence, "workflowIdentity", "release signing evidence"
    )
    certificate_identity = _required_string(
        evidence, "certificateIdentity", "release signing evidence"
    )
    if expected_workflow_identity(repository, workflow_identity_ref) != certificate_identity:
        fail("certificate identity does not match the release workflow identity")
    if not workflow_identity_ref.endswith(f"@{workflow_ref}"):
        fail("workflow identity is not bound to workflowRef")
    run_identity = _required_string(evidence, "runIdentity", "release signing evidence")

    sbom = evidence.get("sbom")
    if not isinstance(sbom, dict):
        fail("release signing evidence has no SBOM map")
    for label in ("cyclonedx", "spdx"):
        entry = sbom.get(label)
        if not isinstance(entry, dict):
            fail(f"release signing evidence is missing {label} SBOM")
        file_name = _required_string(entry, "file", f"{label} SBOM evidence")
        expected_hash = _required_string(entry, "sha256", f"{label} SBOM evidence")
        if Path(file_name).name != file_name or not SHA256_RE.fullmatch(expected_hash):
            fail(f"{label} SBOM evidence contains an unsafe file name or hash")
        path = evidence_dir / file_name
        if sha256_file(path) != expected_hash:
            fail(f"{label} SBOM evidence payload hash does not match")

    provenance = evidence.get("provenance")
    if not isinstance(provenance, dict):
        fail("release signing evidence has no provenance entry")
    provenance_name = _required_string(provenance, "file", "release provenance evidence")
    provenance_hash = _required_string(provenance, "sha256", "release provenance evidence")
    if Path(provenance_name).name != provenance_name:
        fail("provenance evidence contains an unsafe file name")
    if not SHA256_RE.fullmatch(provenance_hash):
        fail("provenance evidence contains an invalid SHA-256")
    provenance_path = evidence_dir / provenance_name
    if sha256_file(provenance_path) != provenance_hash:
        fail("provenance payload hash does not match release evidence")

    provenance_payload = read_json(provenance_path)
    build_definition = provenance_payload.get("buildDefinition")
    run_details = provenance_payload.get("runDetails")
    if not isinstance(build_definition, dict) or not isinstance(run_details, dict):
        fail("provenance payload is missing required SLSA v1 sections")
    if build_definition.get("buildType") != certificate_identity:
        fail("provenance buildType does not match release workflow identity")
    external_parameters = build_definition.get("externalParameters")
    if not isinstance(external_parameters, dict):
        fail("provenance externalParameters are missing")
    expected_external = {
        "repository": repository,
        "commit": repository_sha,
        "ref": workflow_ref,
        "releaseTag": release_tag,
        "subjectDigest": subject_digest,
    }
    for key, expected in expected_external.items():
        if external_parameters.get(key) != expected:
            fail(f"provenance {key} does not match release evidence")

    builder = run_details.get("builder")
    metadata = run_details.get("metadata")
    if not isinstance(builder, dict) or builder.get("id") != certificate_identity:
        fail("provenance builder identity does not match release evidence")
    if not isinstance(metadata, dict) or metadata.get("invocationId") != run_identity:
        fail("provenance run identity does not match release evidence")
    scan_forbidden_evidence(evidence)
    scan_forbidden_evidence(provenance_payload)


def verify_evidence_command(args: argparse.Namespace) -> None:
    evidence_path = Path(args.evidence).resolve()
    verify_evidence_values(evidence=read_json(evidence_path), evidence_dir=evidence_path.parent)


def build_rego_policy(
    *, subject_digest: str, predicate_type: str, predicate: dict[str, Any]
) -> str:
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", subject_digest):
        fail("policy subject digest must be an immutable sha256 digest")
    predicate_uri = PREDICATE_TYPES.get(predicate_type)
    if predicate_uri is None:
        fail(f"unsupported predicate type for release policy: {predicate_type}")
    expected_digest = subject_digest.split(":", 1)[1]
    expected_predicate = json.dumps(
        predicate, sort_keys=True, separators=(",", ":"), ensure_ascii=False
    )
    return (
        "package signature\n\n"
        f'expected_digest := "{expected_digest}"\n'
        f'expected_predicate_type := "{predicate_uri}"\n'
        f"expected_predicate := {expected_predicate}\n\n"
        "allow {\n"
        "  input.predicateType == expected_predicate_type\n"
        "  some i\n"
        "  input.subject[i].digest.sha256 == expected_digest\n"
        "  input.predicate == expected_predicate\n"
        "}\n"
    )


def write_policy_command(args: argparse.Namespace) -> None:
    evidence_path = Path(args.evidence).resolve()
    evidence = read_json(evidence_path)
    verify_evidence_values(evidence=evidence, evidence_dir=evidence_path.parent)
    predicate = read_json(Path(args.predicate).resolve())
    policy = build_rego_policy(
        subject_digest=_required_string(evidence, "subjectDigest", "release signing evidence"),
        predicate_type=args.predicate_type,
        predicate=predicate,
    )
    output = Path(args.output).resolve()
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(policy, encoding="utf-8")


def verify_statement_values(
    *,
    statement: dict[str, Any],
    subject_digest: str,
    predicate_type: str,
    expected_predicate: dict[str, Any],
) -> None:
    if not re.fullmatch(r"sha256:[0-9a-f]{64}", subject_digest):
        fail("expected subject digest is invalid")
    expected_uri = PREDICATE_TYPES.get(predicate_type)
    if expected_uri is None:
        fail(f"unsupported predicate type: {predicate_type}")
    if statement.get("predicateType") != expected_uri:
        fail("attestation predicate type does not match")
    subjects = statement.get("subject")
    if not isinstance(subjects, list) or not subjects:
        fail("attestation has no subject")
    expected_hex = subject_digest.split(":", 1)[1]
    if not any(
        isinstance(subject, dict)
        and isinstance(subject.get("digest"), dict)
        and subject["digest"].get("sha256") == expected_hex
        for subject in subjects
    ):
        fail("attestation subject digest does not match")
    if statement.get("predicate") != expected_predicate:
        fail("attestation predicate content does not match the expected artifact")


def verify_statement_command(args: argparse.Namespace) -> None:
    verify_statement_values(
        statement=read_json(Path(args.statement).resolve()),
        subject_digest=args.subject_digest,
        predicate_type=args.predicate_type,
        expected_predicate=read_json(Path(args.predicate).resolve()),
    )


def _verified_marker(path_value: str, label: str) -> str:
    path = Path(path_value).resolve()
    if not path.is_file() or path.stat().st_size == 0:
        fail(f"{label} verification marker is missing or empty: {path}")
    if path.read_text(encoding="utf-8").strip() != "verified":
        fail(f"{label} verification marker is not verified")
    return "verified"


def finalize_command(args: argparse.Namespace) -> None:
    evidence_path = Path(args.evidence).resolve()
    evidence = read_json(evidence_path)
    verify_evidence_values(evidence=evidence, evidence_dir=evidence_path.parent)
    results = {
        "signature": _verified_marker(args.signature_result, "signature"),
        "cyclonedxAttestation": _verified_marker(
            args.cyclonedx_result, "CycloneDX attestation"
        ),
        "spdxAttestation": _verified_marker(args.spdx_result, "SPDX attestation"),
        "provenanceAttestation": _verified_marker(
            args.provenance_result, "provenance attestation"
        ),
        "mutableTagDigestRecheck": _verified_marker(
            args.mutable_tag_result, "mutable tag digest recheck"
        ),
    }
    verification = {
        "schema": VERIFICATION_SCHEMA,
        "subject": evidence["subject"],
        "subjectDigest": evidence["subjectDigest"],
        "cosignVersion": evidence["cosignVersion"],
        "oidcIssuer": evidence["oidcIssuer"],
        "certificateIdentity": evidence["certificateIdentity"],
        "workflowRef": evidence["workflowRef"],
        "workflowIdentity": evidence["workflowIdentity"],
        "runIdentity": evidence["runIdentity"],
        "releaseTag": evidence["releaseTag"],
        "sbom": evidence["sbom"],
        "provenance": evidence["provenance"],
        "results": results,
    }
    scan_forbidden_evidence(verification)
    write_json(Path(args.output).resolve(), verification)


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser()
    subparsers = parser.add_subparsers(dest="command", required=True)

    build = subparsers.add_parser("build-evidence")
    build.add_argument("--subject-file", required=True)
    build.add_argument("--sbom-metadata", required=True)
    build.add_argument("--cyclonedx", required=True)
    build.add_argument("--spdx", required=True)
    build.add_argument("--repository", required=True)
    build.add_argument("--repository-sha", required=True)
    build.add_argument("--release-tag", required=True)
    build.add_argument("--github-ref", required=True)
    build.add_argument("--workflow-ref", required=True)
    build.add_argument("--run-id", required=True)
    build.add_argument("--run-attempt", required=True)
    build.add_argument("--cosign-version", required=True)
    build.add_argument("--out-dir", required=True)
    build.set_defaults(func=build_evidence_command)

    verify = subparsers.add_parser("verify-evidence")
    verify.add_argument("--evidence", required=True)
    verify.set_defaults(func=verify_evidence_command)

    policy = subparsers.add_parser("write-policy")
    policy.add_argument("--evidence", required=True)
    policy.add_argument("--predicate", required=True)
    policy.add_argument("--predicate-type", required=True, choices=tuple(PREDICATE_TYPES))
    policy.add_argument("--output", required=True)
    policy.set_defaults(func=write_policy_command)

    statement = subparsers.add_parser("verify-statement")
    statement.add_argument("--statement", required=True)
    statement.add_argument("--predicate", required=True)
    statement.add_argument("--subject-digest", required=True)
    statement.add_argument("--predicate-type", required=True, choices=tuple(PREDICATE_TYPES))
    statement.set_defaults(func=verify_statement_command)

    finalize = subparsers.add_parser("finalize")
    finalize.add_argument("--evidence", required=True)
    finalize.add_argument("--signature-result", required=True)
    finalize.add_argument("--cyclonedx-result", required=True)
    finalize.add_argument("--spdx-result", required=True)
    finalize.add_argument("--provenance-result", required=True)
    finalize.add_argument("--mutable-tag-result", required=True)
    finalize.add_argument("--output", required=True)
    finalize.set_defaults(func=finalize_command)

    return parser


def main() -> int:
    try:
        args = build_parser().parse_args()
        args.func(args)
    except ReleaseEvidenceError as exc:
        print(f"SEC-11 release supply-chain validation failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
