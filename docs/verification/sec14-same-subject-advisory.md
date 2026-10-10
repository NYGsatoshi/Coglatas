# SEC-14 same-subject Advisory evidence

This additive continuation starts from mechanical release-consistency commit
`1294426bf3d442bd099fb574ab09e2d970cc1ab8`. It records a reviewable subset of
Issue #614; it cannot qualify Tier B, release acceptance or PRE-AVALONIA SEC-ARCH.
Historical native and release receipts remain unchanged.

## Existing release flow and the new observation

The existing `publish-release-image` job builds and publishes the licensed
candidate, resolves an immutable GHCR subject and produces its original image
SBOM. Signing and verification consume those exact original bytes. The
separate SBOM image workflow may build another image from the same source;
source equality alone does not establish image equality.

The new non-required `sec14-release-assurance-advisory` job depends only on
`publish-release-image`. It checks out that exact candidate, downloads the
original `sec11-release-inputs-<run>-<attempt>` artifact, pulls the already
published subject and uses the existing Grype 0.118.0 and Trivy 0.65.0 lanes.
Grype reads the original CycloneDX SBOM. Trivy scans the archive whose actual
config and layer bytes are reconciled with the exact immutable published
subject. The job needs only `contents: read` and `packages: read`;
it has no signing authority, protected build environment or new secret.

The existing promotion prerequisites remain `publish-release-image` and
`verify-release-subject`. This observation does not promote enforcement or
replace the existing actual Cosign verification. The six Required Checks,
scanner baselines, exceptions, Medium inventory policy, numeric thresholds,
DB-duration suspension and commented deep schedule are unchanged.

## Manifest and configuration are distinct identities

`subjectDigest` identifies the immutable registry manifest or index used for
publishing, signing and the original SBOM. `platformManifestDigest` identifies
the selected runtime platform manifest. `imageConfigurationDigest` identifies
the digest-verified config bytes in the native Docker archive. Docker `.Id`
is retained only as `observedDockerId`: native Docker/containerd may expose an
index ID there. [The version-2 graph repair](sec14-release-image-graph.md)
records the concrete local failure of the previous inference and the current
raw-manifest/config/layer checks. Trivy archive mode must report the resolved
configuration and complete diff-ID list, not the observed Docker ID.

The pre-existing Docker-image SEC-10 CLI keeps its existing configuration
digest behavior. A shared pure evaluator applies exactly the same scanner,
freshness, reconciliation and vulnerability policy rules to captured
documents. Only the new registry caller supplies the separately reconciled
Trivy configuration identity while keeping the manifest as its SBOM and
policy subject. Original version-1 records are not retroactively upgraded.

The original CycloneDX container root must also name that immutable subject
and carry its manifest as `version`, matching the
[pinned Syft 1.51.0 registry formatter](https://github.com/anchore/syft/blob/v1.51.0/syft/format/common/cyclonedxhelpers/to_format_model.go).
The root's BOM reference is opaque; it is not compared to Docker's configuration
digest. Missing roots, another image's rehashed root and a wrong recorded Syft
producer version are rejected.

## Producer and consumer controls

`scripts/ci/release_assurance_advisory.py` requires independent expected
candidate SHA, immutable GHCR subject, runtime platform and exact GitHub
run/attempt URL.
The current workflow supplies these from the existing publish outputs and
current run context. The original SBOM metadata must reference that candidate,
manifest and exact `run/attempt/publish-release-image` producer identity.
Both CycloneDX and SPDX bytes must match their original metadata hashes.

Every JSON input is parsed and hashed from the same stream snapshot bounded
to 32 MiB plus one overflow byte. Structural nesting is bounded to 128;
duplicate fields, non-finite numbers, invalid UTF-8, empty inputs and
non-object roots are rejected. Outputs are bounded and written exclusively
into a new directory. No prior receipt can be overwritten.

The `coglatas-release-assurance-advisory-v2` receipt retains the original
input hashes, both image identities, candidate, run, native findings and
policy decision, and exact normalized-output and summary hashes. On-disk
verifier source hashes support later change detection; they do not
authenticate loaded code or source-to-candidate ancestry. Historical version-1
receipts retain their original bytes and have **UNVERIFIED configuration-graph
qualification**. The current consumer refuses to upgrade them implicitly.

The read-only `--verify-directory` consumer re-derives native decisions from
the retained inputs and compares the complete receipt and exact native result
bytes. Changed input bytes, changed verifier source fingerprints, an unknown
verifier version, missing results, removed findings, scope shrinkage and forged
approval markers fail visibly. Policy blockers retain a nonzero result after
the receipt is written. Scanner errors still fail the job; an `always()`
collector and upload preserve an explicit error receipt when inputs are missing.

Both producer and consumer always report GitHub run authentication, scanner
execution authentication, cryptographic verification, exception approval
authentication and personal owner approval as `UNVERIFIED`. Supplied JSON,
including a native policy's reviewer/owner field or a `verified` marker, does
not authenticate itself. No actual release, registry write or Cosign operation
was performed for the local controls.

Example production and read-only reconciliation:

```bash
python3 scripts/ci/release_assurance_advisory.py \
  --expected-repository-sha "$RELEASE_SHA" \
  --expected-subject "$SUBJECT" \
  --expected-platform linux/amd64 \
  --expected-run-identity "$EXACT_RUN_ATTEMPT_URL" \
  --sbom-directory original-release-inputs \
  --scan-directory exact-subject-scan \
  --policy security/sbom-vulnerability-policy.json \
  --out-directory new-advisory-receipt

# Retain the same inputs and replace only the exclusive output argument:
python3 scripts/ci/release_assurance_advisory.py \
  --expected-repository-sha "$RELEASE_SHA" \
  --expected-subject "$SUBJECT" \
  --expected-platform linux/amd64 \
  --expected-run-identity "$EXACT_RUN_ATTEMPT_URL" \
  --sbom-directory original-release-inputs \
  --scan-directory exact-subject-scan \
  --policy security/sbom-vulnerability-policy.json \
  --verify-directory new-advisory-receipt
```

## Tier B and advanced evidence limits

The receipt retains all 14 existing #614 Tier B categories: SEC-04, SEC-05,
SEC-06, SEC-07, SEC-08, SEC-09, SEC-10, SEC-12, SEC-13, SEC-15, SEC-16,
SEC-17, SEC-18 and SEC-20. Two categories have a local mechanical SBOM/scanner
binding. **Zero categories receive complete acceptance qualification; all 14
remain outstanding.** Input fields cannot shrink this inventory or substitute
`NOT_APPLICABLE` for missing evidence. These labels do not allocate new SPECs
or resolve pending canonical mappings.

Three optional native adapters summarize existing interfaces:

| Input | Mechanical validation | Remaining limitation |
| --- | --- | --- |
| `--functional-manifest` | Existing full-gate schema; all four domains and seven required journeys; exact supplied candidate/run/attempt; first-attempt PASS required | No immutable release-image provenance; functional journeys are not exhaustive security-contract coverage |
| Repeated `--schemathesis-metadata` plus `--open-api` | Existing deep metadata, exact contract hash, closed five-role set, nonzero request and operation counts, scanner/network error rejection | Metadata contains no candidate, workflow or image provenance; missing roles remain counted |
| Repeated `--zap-metadata` plus `--open-api` | Existing SEC-06 schema, exact contract hash, pinned tool/image identity, closed three-role set, classified outcome and sanitized-mode requirement | Metadata contains no candidate/run/image provenance or request coverage; plan/policy bytes are not supplied by that native interface |

An additive [original Main artifact consumer](sec14-native-main-observation.md)
can independently resolve the same candidate's native scanner artifact using
live run/job/upload metadata and original digest-verified ZIP bytes. Its three
optional inputs are supplied together; partial, wrong-candidate or forged
receipt inputs fail. This supplies a scoped Main provenance observation without
upgrading legacy metadata, immutable release-image binding, scanner execution
attestation or any of the fourteen outstanding acceptance categories.

`--tier-b-run-identity` can independently supply the existing functional
producer's Main run/attempt URL. It is a consistency expectation, not a live
GitHub authentication record. Without it, the functional adapter requires the
current expected release run. Neither legacy scanner metadata nor a wrapper's
self-declared candidate field can upgrade provenance. Unknown schema fields,
duplicate roles, skipped journeys and zero execution are rejected.

RESTler, browser/AJAX ZAP, image/artifact secret acceptance and advanced
acceptance normalization have no implemented adapter in this inspected slice:
**four missing advanced adapters**. Approved OAST applicability/collector
authority remains separately pending. Optional Burp is not supplied and cannot
replace ZAP. Weekly deep scheduling remains inactive. The receipt always
reports `releaseAcceptance: BLOCKED` and `preAvaloniaVerdict: BLOCKED`.

The actual Dependabot snapshot retains alerts 121, 122 and 123 as OPEN/HIGH,
`braces`, `GHSA-vfj7-8cjw-p6xm`, no patched version, SHA-256
`fb002414d2580069f71e3c3d1761739a5c73109a8196ba9a72faa766e15dbd2a`.
Those source/build findings are separate from shipped-image observations.
No dismissal, exception, suppression or dependency replacement was introduced.
The [preceding dependency feasibility report](sec14-release-evidence-consistency.md)
retains exact lockfile chains and historical failures.

## Focused local verification

The original version-1 continuation passed **63 producer/consumer and workflow controls**,
**34 existing release controls**, **39 existing SBOM/source/policy/inventory
controls**, and **45 unchanged Required Check topology controls**: **181 tests**,
zero failures or skips. Tests used Python 3.13.12 / Ruby 3.4.9 in a local test
image, with networking disabled and the checkout mounted read-only. All
positive vulnerability and native-lane inputs were deliberate fixtures;
they are tooling controls, not actual release execution or normative coverage.
Exact-head hosted qualification and a genuine release-subject scan remain
unexecuted. Earlier helper and environment failure artifacts remain unchanged.
These historical controls did not establish the actual configuration graph.
The version-2 correction and native execution scope are recorded separately in
[the graph repair report](sec14-release-image-graph.md).
