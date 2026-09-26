# SEC-09 Syft SBOM supply-chain evidence

SEC-09 deliberately uses two workflows so pull-request code never shares a workflow definition with protected build secrets.

- `.github/workflows/sbom-security.yml` is the PR-safe source lane. It runs on pull requests, `main`, and manual dispatch and produces the repository/source dependency view.
- `.github/workflows/sbom-image-security.yml` is the trusted image lane. It has no pull-request trigger; it runs on `main`, published releases, or manual dispatch inside the protected `syncfusion-licensed-build` environment and scans the final licensed production image.

A source dependency list is never treated as a substitute for the final runtime image scan.

## Pinned toolchain

Syft is pinned to `1.51.0`. `scripts/ci/install-syft.sh` downloads the Linux amd64 release archive and rejects it unless its SHA-256 is exactly `2a2e837a2c8d59ec9af5472ee22d3b04ee463c4e44476ecf993fd1e5ab6ebc7f`. Neither workflow uses a moving `latest` tag.

## Evidence set

Each view contains:

- `sbom.cyclonedx.json`;
- `sbom.spdx.json`;
- `components.normalized.json`;
- `metadata.json`.

The image view additionally contains `image.digest`, the Docker content-addressed image ID (`sha256:...`) bound to the image evidence.

`metadata.json` records the repository commit, GitHub Actions run identity, Syft version, CycloneDX/SPDX schema versions, generation time, component counts, output hashes, and the image digest where applicable. `verify_sbom.py verify-hashes` recomputes the recorded SHA-256 values before artifact upload.

## Source dependency materialization

The source lane includes npm development dependencies so the root test/tooling graph is represented. Syft's .NET source cataloger consumes `packages.lock.json`, so CI runs `dotnet restore --use-lock-file --force-evaluate` and materializes resolved NuGet lock graphs in the ephemeral Actions workspace before the Syft directory scan. These generated lock files are evidence inputs only; they are not committed by the workflow.

The source lane then requires packages from all material dependency families:

- root npm: `@playwright/test`;
- frontend npm: `@angular/core`;
- NuGet: `Microsoft.EntityFrameworkCore`.

## Runtime completeness and leak checks

The trusted image lane builds the production Dockerfile with the Syncfusion license supplied only through a BuildKit secret mount. It records the resulting Docker content-addressed image ID, requires the Debian runtime package `curl` in both SBOM formats, and independently checks that `/app/Coglatas.Web.dll` and `/app/Coglatas.Web.deps.json` exist in the final image.

A dynamically generated fake secret marker must not appear in either SBOM. The trusted image lane also rejects the protected Syncfusion license value if it appears in either generated JSON document. Build secrets are never passed as Docker build arguments.

## Reproducibility policy

Raw SBOM metadata can contain timestamps, document namespaces, UUIDs, and ordering that are not stable across runs. The machine reproducibility projection therefore retains only component identity fields (`type`, `name`, `version`, package URL) plus duplicate occurrence counts, sorted deterministically.

The trusted image lane scans the same per-run production image twice and fails if the normalized projections differ. The image digest is recorded alongside the first evidence set so a later consumer can prove which immutable image instance was scanned.

## Trusted final-candidate gate

`scripts/ci/verify-mvp-a-final-checks.py`, used by the main-only `MVP-A Final Gate`, requires successful GitHub Actions checks named `sbom-source` and `sbom-image-trusted` on the exact candidate SHA. A missing, pending, failed, or untrusted SBOM check therefore makes the final-candidate gate fail. This connects SEC-09 evidence to the trusted release-candidate path rather than leaving the image workflow as advisory evidence.

SBOM generation, JSON validation, structural schema validation, required-package checks, leak checks, hash verification, image reproducibility checks, and the final-candidate SBOM requirements are blocking. Signing and attestations are deliberately outside SEC-09 and belong to SEC-11.
