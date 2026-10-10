#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

ROOT = Path(__file__).resolve().parents[2] if "__file__" in globals() else Path.cwd()
SCRIPT = ROOT / "scripts" / "ci" / "release_supply_chain.py"
SPEC = importlib.util.spec_from_file_location("release_supply_chain", SCRIPT)
assert SPEC and SPEC.loader
release = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(release)


def digest_file(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


class ReleaseSupplyChainTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.digest = "sha256:" + ("a" * 64)
        self.subject = f"ghcr.io/nygsatoshi/coglatas@{self.digest}"
        self.sha = "b" * 40
        self.repository = "NYGsatoshi/Coglatas"
        self.release_tag = "v1.2.3"
        self.github_ref = f"refs/tags/{self.release_tag}"
        self.workflow_ref = (
            f"{self.repository}/.github/workflows/release-supply-chain.yml@"
            f"{self.github_ref}"
        )
        self.certificate_identity = release.expected_workflow_identity(
            self.repository, self.workflow_ref
        )
        self.run_identity = (
            f"https://github.com/{self.repository}/actions/runs/123/attempts/1"
        )
        self.cdx = self.root / "sbom.cyclonedx.json"
        self.spdx = self.root / "sbom.spdx.json"
        self.provenance = self.root / "provenance.json"
        self.metadata = self.root / "metadata.json"
        self.cdx.write_text(
            json.dumps(
                {
                    "bomFormat": "CycloneDX",
                    "specVersion": "1.6",
                    "version": 1,
                    "components": [{"type": "library", "name": "curl"}],
                }
            ),
            encoding="utf-8",
        )
        self.spdx.write_text(
            json.dumps(
                {
                    "spdxVersion": "SPDX-2.3",
                    "SPDXID": "SPDXRef-DOCUMENT",
                    "name": "test",
                    "packages": [{"name": "curl", "SPDXID": "SPDXRef-Package-curl"}],
                }
            ),
            encoding="utf-8",
        )
        self.metadata.write_text(
            json.dumps(
                {
                    "schema": release.SBOM_EVIDENCE_SCHEMA,
                    "sourceKind": "image",
                    "repositoryCommit": self.sha,
                    "imageOrReleaseDigest": self.digest,
                    "formats": {
                        "cyclonedx-json": {
                            "file": self.cdx.name,
                            "sha256": digest_file(self.cdx),
                        },
                        "spdx-json": {
                            "file": self.spdx.name,
                            "sha256": digest_file(self.spdx),
                        },
                    },
                }
            ),
            encoding="utf-8",
        )

    def write_complete_evidence(self) -> Path:
        provenance = release.build_provenance(
            repository=self.repository,
            repository_sha=self.sha,
            workflow_identity=self.certificate_identity,
            workflow_ref=self.workflow_ref,
            run_identity=self.run_identity,
            release_tag=self.release_tag,
            subject_digest=self.digest,
        )
        release.write_json(self.provenance, provenance)
        evidence = {
            "schema": release.EVIDENCE_SCHEMA,
            "repository": self.repository,
            "repositoryCommit": self.sha,
            "releaseTag": self.release_tag,
            "subject": self.subject,
            "subjectDigest": self.digest,
            "cosignVersion": "3.1.3",
            "oidcIssuer": release.OIDC_ISSUER,
            "certificateIdentity": self.certificate_identity,
            "workflowRef": self.github_ref,
            "workflowIdentity": self.workflow_ref,
            "runIdentity": self.run_identity,
            "sbom": {
                "cyclonedx": {
                    "file": self.cdx.name,
                    "sha256": digest_file(self.cdx),
                },
                "spdx": {
                    "file": self.spdx.name,
                    "sha256": digest_file(self.spdx),
                },
            },
            "provenance": {
                "file": self.provenance.name,
                "sha256": digest_file(self.provenance),
            },
        }
        evidence_path = self.root / "release-signing-evidence.json"
        release.write_json(evidence_path, evidence)
        return evidence_path

    def test_good_digest_and_sbom_binding_succeeds(self) -> None:
        digest, cdx_hash, spdx_hash = release.validate_sbom_binding(
            subject=self.subject,
            repository_sha=self.sha,
            metadata_path=self.metadata,
            cyclonedx_path=self.cdx,
            spdx_path=self.spdx,
        )
        self.assertEqual(self.digest, digest)
        self.assertEqual(digest_file(self.cdx), cdx_hash)
        self.assertEqual(digest_file(self.spdx), spdx_hash)

    def test_different_release_digest_fails(self) -> None:
        other_subject = "ghcr.io/nygsatoshi/coglatas@sha256:" + ("c" * 64)
        with self.assertRaises(release.ReleaseEvidenceError):
            release.validate_sbom_binding(
                subject=other_subject,
                repository_sha=self.sha,
                metadata_path=self.metadata,
                cyclonedx_path=self.cdx,
                spdx_path=self.spdx,
            )

    def test_sbom_payload_from_other_artifact_fails(self) -> None:
        self.cdx.write_text('{"bomFormat":"CycloneDX","components":[]}', encoding="utf-8")
        with self.assertRaises(release.ReleaseEvidenceError):
            release.validate_sbom_binding(
                subject=self.subject,
                repository_sha=self.sha,
                metadata_path=self.metadata,
                cyclonedx_path=self.cdx,
                spdx_path=self.spdx,
            )

    def test_attestation_subject_mismatch_fails(self) -> None:
        statement = {
            "_type": "https://in-toto.io/Statement/v0.1",
            "predicateType": release.PREDICATE_TYPES["cyclonedx"],
            "subject": [{"name": "image", "digest": {"sha256": "c" * 64}}],
            "predicate": json.loads(self.cdx.read_text(encoding="utf-8")),
        }
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_statement_values(
                statement=statement,
                subject_digest=self.digest,
                predicate_type="cyclonedx",
                expected_predicate=json.loads(self.cdx.read_text(encoding="utf-8")),
            )

    def test_provenance_subject_mismatch_fails(self) -> None:
        provenance = release.build_provenance(
            repository=self.repository,
            repository_sha=self.sha,
            workflow_identity=self.certificate_identity,
            workflow_ref=self.workflow_ref,
            run_identity=self.run_identity,
            release_tag=self.release_tag,
            subject_digest=self.digest,
        )
        statement = {
            "_type": "https://in-toto.io/Statement/v0.1",
            "predicateType": release.PREDICATE_TYPES["slsaprovenance1"],
            "subject": [{"name": "image", "digest": {"sha256": "c" * 64}}],
            "predicate": provenance,
        }
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_statement_values(
                statement=statement,
                subject_digest=self.digest,
                predicate_type="slsaprovenance1",
                expected_predicate=provenance,
            )

    def test_attestation_predicate_mismatch_fails(self) -> None:
        statement = {
            "_type": "https://in-toto.io/Statement/v0.1",
            "predicateType": release.PREDICATE_TYPES["cyclonedx"],
            "subject": [{"name": "image", "digest": {"sha256": "a" * 64}}],
            "predicate": {"bomFormat": "CycloneDX", "components": []},
        }
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_statement_values(
                statement=statement,
                subject_digest=self.digest,
                predicate_type="cyclonedx",
                expected_predicate=json.loads(self.cdx.read_text(encoding="utf-8")),
            )

    def test_unsigned_or_missing_statement_fails(self) -> None:
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_statement_values(
                statement={},
                subject_digest=self.digest,
                predicate_type="cyclonedx",
                expected_predicate=json.loads(self.cdx.read_text(encoding="utf-8")),
            )

    def test_rego_policy_pins_subject_and_exact_predicate(self) -> None:
        predicate = json.loads(self.cdx.read_text(encoding="utf-8"))
        policy = release.build_rego_policy(
            subject_digest=self.digest,
            predicate_type="cyclonedx",
            predicate=predicate,
        )
        self.assertIn("a" * 64, policy)
        self.assertIn(release.PREDICATE_TYPES["cyclonedx"], policy)
        self.assertIn("input.predicate == expected_predicate", policy)

    def test_verify_evidence_values_accepts_complete_bound_evidence(self) -> None:
        evidence_path = self.write_complete_evidence()
        release.verify_evidence_values(
            evidence=release.read_json(evidence_path), evidence_dir=self.root
        )

    def test_verify_evidence_rejects_tampered_provenance_hash(self) -> None:
        evidence_path = self.write_complete_evidence()
        self.provenance.write_text(
            self.provenance.read_text(encoding="utf-8") + "\n", encoding="utf-8"
        )
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_values(
                evidence=release.read_json(evidence_path), evidence_dir=self.root
            )

    def test_verify_evidence_rejects_mismatched_builder_identity(self) -> None:
        evidence_path = self.write_complete_evidence()
        provenance = release.read_json(self.provenance)
        provenance["runDetails"]["builder"]["id"] = "https://example.invalid/builder"
        release.write_json(self.provenance, provenance)
        evidence = release.read_json(evidence_path)
        evidence["provenance"]["sha256"] = digest_file(self.provenance)
        release.write_json(evidence_path, evidence)
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_values(evidence=evidence, evidence_dir=self.root)

    def rewrite_source_dependencies(self, dependencies: object) -> dict:
        evidence_path = self.write_complete_evidence()
        provenance = release.read_json(self.provenance)
        provenance['buildDefinition']['resolvedDependencies'] = dependencies
        release.write_json(self.provenance, provenance)
        evidence = release.read_json(evidence_path)
        evidence['provenance']['sha256'] = digest_file(self.provenance)
        release.write_json(evidence_path, evidence)
        return evidence

    def test_missing_git_source_dependency_fails(self) -> None:
        evidence = self.rewrite_source_dependencies([])
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_values(evidence=evidence, evidence_dir=self.root)

    def verification_args(self, **overrides: str) -> SimpleNamespace:
        values = {
            'evidence': str(self.write_complete_evidence()),
            'expected_repository_sha': self.sha,
            'expected_subject': self.subject,
            'expected_run_identity': self.run_identity,
            'advisory_output': None,
        }
        values.update(overrides)
        return SimpleNamespace(**values)

    def test_consistent_evidence_from_another_expected_candidate_fails(self) -> None:
        args = self.verification_args(expected_repository_sha='c' * 40)
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_command(args)

    def test_consistent_evidence_from_another_expected_subject_fails(self) -> None:
        args = self.verification_args(expected_subject='ghcr.io/nygsatoshi/coglatas@sha256:' + 'c' * 64)
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_command(args)

    def test_consistent_evidence_from_another_expected_run_fails(self) -> None:
        args = self.verification_args(expected_run_identity=self.run_identity.replace('/123/', '/124/'))
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_command(args)

    def test_consistent_evidence_from_another_expected_attempt_fails(self) -> None:
        args = self.verification_args(expected_run_identity=self.run_identity.replace('/attempts/1', '/attempts/2'))
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_command(args)

    def test_expected_binding_emits_consistency_without_authentication_credit(self) -> None:
        output = self.root / 'consistency-advisory.json'
        args = self.verification_args(advisory_output=str(output))
        release.verify_evidence_command(args)
        report = release.read_json(output)
        self.assertEqual(release.CONSISTENCY_SCHEMA, report['schema'])
        self.assertEqual('CONSISTENT', report['consistencyStatus'])
        self.assertEqual('MATCHED', report['candidateExpectationBinding'])
        self.assertEqual(self.sha, report['repositoryCommit'])
        self.assertEqual(self.subject, report['subject'])
        self.assertEqual(self.run_identity, report['runIdentity'])
        self.assertEqual(digest_file(Path(args.evidence)), report['evidenceSha256'])
        for field in ('githubRunAuthentication', 'cryptographicVerification', 'personalOwnerApproval'):
            self.assertEqual('UNVERIFIED', report[field])

    def test_advisory_cannot_overwrite_existing_evidence(self) -> None:
        output = self.root / 'original-receipt.json'
        original = b'{"historic":true}\n'
        output.write_bytes(original)
        args = self.verification_args(advisory_output=str(output))
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_command(args)
        self.assertEqual(original, output.read_bytes())

    def test_self_declared_verified_fields_do_not_authenticate_advisory(self) -> None:
        output = self.root / 'consistency-advisory.json'
        args = self.verification_args(advisory_output=str(output))
        evidence = release.read_json(Path(args.evidence))
        evidence['cryptographicVerification'] = 'VERIFIED'
        evidence['results'] = {'signature': 'verified', 'provenanceAttestation': 'verified'}
        release.write_json(Path(args.evidence), evidence)
        release.verify_evidence_command(args)
        report = release.read_json(output)
        self.assertEqual('UNVERIFIED', report['cryptographicVerification'])
        self.assertEqual('UNVERIFIED', report['githubRunAuthentication'])
        self.assertEqual('UNVERIFIED', report['personalOwnerApproval'])

    def test_cli_requires_independent_binding_inputs(self) -> None:
        result = subprocess.run([sys.executable, str(SCRIPT), 'verify-evidence',
                                 '--evidence', str(self.write_complete_evidence())],
                                capture_output=True, text=True, check=False)
        self.assertEqual(2, result.returncode)
        self.assertIn('--expected-repository-sha', result.stderr)
        self.assertIn('--expected-subject', result.stderr)
        self.assertIn('--expected-run-identity', result.stderr)

    def test_actual_cli_accepts_exact_expectations_and_emits_advisory(self) -> None:
        evidence = self.write_complete_evidence()
        output = self.root / 'cli-advisory.json'
        result = subprocess.run([
            sys.executable, str(SCRIPT), 'verify-evidence', '--evidence', str(evidence),
            '--expected-repository-sha', self.sha, '--expected-subject', self.subject,
            '--expected-run-identity', self.run_identity, '--advisory-output', str(output),
        ], capture_output=True, text=True, check=False)
        self.assertEqual(0, result.returncode, result.stderr)
        report = release.read_json(output)
        self.assertEqual('MATCHED', report['candidateExpectationBinding'])
        self.assertEqual('UNVERIFIED', report['cryptographicVerification'])

    def test_invalid_or_unbounded_cli_expectations_fail_before_reading_evidence(self) -> None:
        for field, values in {
            'expected_repository_sha': ['B' * 40, 'b' * 41, True],
            'expected_subject': [self.subject.replace('@sha256:', ':latest'), 'a' * 513, True],
            'expected_run_identity': [self.run_identity.replace('https:', 'http:'),
                                      self.run_identity.replace('/123/', '/0123/'),
                                      self.run_identity.replace('/attempts/1', '/attempts/0'),
                                      'a' * 513, True],
        }.items():
            for value in values:
                with self.subTest(field=field, value=str(value)[:32]):
                    args = self.verification_args(**{field: value})
                    args.evidence = str(self.root / 'not-read.json')
                    with self.assertRaises(release.ReleaseEvidenceError) as caught:
                        release.verify_evidence_command(args)
                    self.assertNotIn('cannot be read', str(caught.exception))

    def test_duplicate_json_fields_fail(self) -> None:
        path = self.root / 'duplicate.json'
        path.write_text('{"nested":{"commit":"old","commit":"new"}}', encoding='utf-8')
        with self.assertRaisesRegex(release.ReleaseEvidenceError, 'duplicate'):
            release.read_json(path)

    def test_missing_empty_invalid_utf8_and_non_object_json_fail(self) -> None:
        path = self.root / 'invalid.json'
        with self.assertRaises(release.ReleaseEvidenceError):
            release.read_json(path)
        for value in (b'', b'\xff', b'[]'):
            with self.subTest(value=value):
                path.write_bytes(value)
                with self.assertRaises(release.ReleaseEvidenceError):
                    release.read_json(path)

    def test_non_finite_json_values_fail(self) -> None:
        path = self.root / 'non-finite.json'
        for value in ('NaN', 'Infinity', '-Infinity'):
            with self.subTest(value=value):
                path.write_text('{"count":' + value + '}', encoding='utf-8')
                with self.assertRaisesRegex(release.ReleaseEvidenceError, 'non-finite'):
                    release.read_json(path)

    def test_json_depth_bound_preserves_escaped_string_contents(self) -> None:
        path = self.root / 'quoted-braces.json'
        value = {'quoted': '"\\' + '{[' * (release.MAX_JSON_DEPTH + 1)}
        release.write_json(path, value)
        self.assertEqual(value, release.read_json(path))

    def test_excessive_json_nesting_has_bounded_diagnostic(self) -> None:
        path = self.root / 'nested.json'
        path.write_text('{"nested":' + '[' * 4000 + '0' + ']' * 4000 + '}', encoding='utf-8')
        with self.assertRaises(release.ReleaseEvidenceError) as caught:
            release.read_json(path)
        self.assertLess(len(str(caught.exception)), 100)

    def test_actual_oversize_json_stream_and_hash_fail(self) -> None:
        path = self.root / 'oversize.json'
        with path.open('wb') as handle:
            handle.seek(release.MAX_JSON_BYTES)
            handle.write(b'1')
        for reader in (release.read_json, release.sha256_file):
            with self.subTest(reader=reader.__name__):
                with self.assertRaisesRegex(release.ReleaseEvidenceError, 'size limit'):
                    reader(path)

    def test_release_job_supplies_independent_candidate_run_and_subject(self) -> None:
        workflow = (ROOT / '.github/workflows/release-supply-chain.yml').read_text(encoding='utf-8')
        for argument in ('--expected-repository-sha "$RELEASE_SHA"',
                         '--expected-subject "$SUBJECT"',
                         '--expected-run-identity "https://github.com/${GITHUB_REPOSITORY}/actions/runs/${GITHUB_RUN_ID}/attempts/${GITHUB_RUN_ATTEMPT}"'):
            self.assertIn(argument, workflow)
        self.assertIn('release-consistency-advisory.json', workflow)

    def test_git_source_dependency_from_another_commit_fails(self) -> None:
        evidence = self.rewrite_source_dependencies([{
            'uri': f'git+https://github.com/{self.repository}@{self.sha}',
            'digest': {'gitCommit': 'c' * 40},
        }])
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_values(evidence=evidence, evidence_dir=self.root)

    def test_git_source_dependency_from_another_repository_fails(self) -> None:
        evidence = self.rewrite_source_dependencies([{
            'uri': f'git+https://github.com/example/other@{self.sha}',
            'digest': {'gitCommit': self.sha},
        }])
        with self.assertRaises(release.ReleaseEvidenceError):
            release.verify_evidence_values(evidence=evidence, evidence_dir=self.root)

    def test_finalize_requires_explicit_verified_markers(self) -> None:
        evidence_path = self.write_complete_evidence()
        result_dir = self.root / "results"
        result_dir.mkdir()
        markers = {}
        for name in ("signature", "cyclonedx", "spdx", "provenance", "mutable-tag"):
            path = result_dir / f"{name}.txt"
            path.write_text("verified\n", encoding="utf-8")
            markers[name] = path
        output = self.root / "release-verification.json"
        args = SimpleNamespace(
            evidence=str(evidence_path),
            signature_result=str(markers["signature"]),
            cyclonedx_result=str(markers["cyclonedx"]),
            spdx_result=str(markers["spdx"]),
            provenance_result=str(markers["provenance"]),
            mutable_tag_result=str(markers["mutable-tag"]),
            output=str(output),
        )
        release.finalize_command(args)
        finalized = release.read_json(output)
        self.assertEqual(release.VERIFICATION_SCHEMA, finalized["schema"])
        self.assertTrue(all(value == "verified" for value in finalized["results"].values()))

        markers["signature"].unlink()
        with self.assertRaises(release.ReleaseEvidenceError):
            release.finalize_command(args)

    def test_evidence_rejects_token_or_private_key_material(self) -> None:
        with self.assertRaises(release.ReleaseEvidenceError):
            release.scan_forbidden_evidence({"githubToken": "not-even-a-real-token"})
        with self.assertRaises(release.ReleaseEvidenceError):
            release.scan_forbidden_evidence(
                {"material": "-----BEGIN PRIVATE KEY-----\nredacted"}
            )

    def test_release_workflow_only_signing_job_has_oidc_write(self) -> None:
        workflow_path = ROOT / ".github" / "workflows" / "release-supply-chain.yml"
        workflow_text = workflow_path.read_text(encoding="utf-8")
        self.assertNotIn("pull_request_target:", workflow_text)
        self.assertNotIn("pull_request:", workflow_text)

        ruby = subprocess.run(
            [
                "ruby",
                "-rpsych",
                "-rjson",
                "-e",
                (
                    "doc=Psych.safe_load_file(ARGV[0], aliases: false); "
                    "puts JSON.generate(doc)"
                ),
                str(workflow_path),
            ],
            text=True,
            capture_output=True,
            check=False,
        )
        self.assertEqual(ruby.returncode, 0, ruby.stderr)
        workflow = json.loads(ruby.stdout)
        workflow_permissions = workflow.get("permissions") or {}
        self.assertNotEqual(
            "write",
            workflow_permissions.get("id-token"),
            "unexpected OIDC write authority at workflow scope",
        )

        jobs = workflow["jobs"]
        self.assertIn("sign-release-subject", jobs)
        for job_name, job in jobs.items():
            permissions = job.get("permissions") or {}
            if job_name == "sign-release-subject":
                self.assertEqual("write", permissions.get("id-token"))
            else:
                self.assertNotEqual(
                    "write",
                    permissions.get("id-token"),
                    f"unexpected OIDC write authority on job {job_name}",
                )


if __name__ == "__main__":
    unittest.main()
