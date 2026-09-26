#!/usr/bin/env python3
from __future__ import annotations

import hashlib
import importlib.util
import json
import subprocess
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
