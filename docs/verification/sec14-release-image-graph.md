# SEC-14 release image graph consistency

The version-1 Advisory implementation at
`17dbd0bc5b8f1212495eaa8e55ee3add36e2e542` treated Docker `.Id` as the image
configuration digest. A test-owned image on the current Docker/containerd
store disproved that inference. Native Docker-source Trivy 0.65.0 reported
the OCI index ID; archive-source Trivy reported the actual configuration ID
from the same saved image. The original observation, native reports and
failed configuration-equality assertion remain in private development
evidence. No historical receipt or failed run was rewritten.

## Current byte relationships

The non-required `sec14-release-assurance-advisory` job reads the already
published immutable subject. It retrieves original registry JSON with
`docker buildx imagetools inspect --raw`, checks its exact SHA-256 against the
independently supplied subject and selects one `linux/amd64` runtime manifest.
Its descriptor digest and size bind the separately retrieved platform
manifest. A direct single-platform manifest is also supported. Unknown
platforms, duplicate runtime matches, nested indexes, artifact manifests and
unsupported media types fail visibly; none become `NOT_APPLICABLE`.

[OCI indexes](https://github.com/opencontainers/image-spec/blob/v1.1.1/image-index.md)
and [image manifests](https://github.com/opencontainers/image-spec/blob/v1.1.1/manifest.md)
have separate descriptor relationships. The reader accepts only digest-bound
native JSON bytes. One terminal transport LF may be removed only when that
exact payload matches the independently supplied digest; arbitrary whitespace
or JSON reserialization cannot satisfy the binding.

`release_image_graph.py` snapshots the native `docker image save` file with a
4 GiB stream bound, then parses that same private snapshot without extracting
paths or following links. Reads use chunks up to 1 MiB. There are at most 8,192
members, 512 runtime layers, 2 GiB per member and 8 GiB expanded layer bytes.
Retained JSON is limited to the bounded manifest/config objects, each at most
32 MiB with depth 128. PAX headers are limited to 64 KiB and GNU long-name
headers to 4 KiB before parser allocation; sparse archives are unsupported.
These are input resource limits, with no change to existing performance
thresholds or scanner baselines. Duplicate paths, traversal, special members,
truncation, malformed JSON, missing blobs and decompression overflow fail.

For current native Docker OCI exports, the archive wrapper must reference the
expected registry subject. The actual top and selected platform blobs must
equal the digest-verified registry bytes. The selected config descriptor must
match actual config bytes and platform. Every runtime layer must match the
registry blob digest and size, and its uncompressed SHA-256 must equal the
ordered configuration `rootfs.diff_ids`.

Legacy Docker saves retain the native `<config-sha>.json` and `*/layer.tar`
convention. Their actual config digest and all uncompressed layer diff IDs
are verified against the same registry graph. They report
`registryCompressedLayerBytesQualification: UNVERIFIED`: an uncompressed
legacy export does not retain original compressed registry blobs. Gzip and
plain layers are supported; zstd, foreign/remote layers and embedded
descriptors are currently unsupported and fail explicitly.

Docker `.Id` is only `observedDockerId`. It may match the independently
resolved subject, selected manifest or config; it cannot choose a config.
Docker's observed platform and RepoDigests must remain consistent with the
expected subject and resolved graph. Trivy 0.65.0 uses its supported
[`--input` Docker archive reader](https://github.com/aquasecurity/trivy/blob/v0.65.0/pkg/fanal/image/docker.go),
whose [image ID implementation](https://github.com/aquasecurity/trivy/blob/v0.65.0/pkg/fanal/image/image.go)
derives the configuration digest. Its native `Metadata.ImageID` and complete
`DiffIDs` must equal actual resolved bytes. The original SEC-10 CLI and
vulnerability policy behavior are unchanged; the Advisory caller supplies
this resolved config identity to the shared native evaluator.

## Receipt compatibility and retention

New receipts use `coglatas-release-assurance-advisory-v2` and verifier version
`2`, retaining the subject, selected platform manifest, actual config, observed
Docker ID, archive hash and ordered diff IDs. The retained graph is recomputed
from actual inputs before comparing it. A self-declared graph, substituted
config, scanner layer scope shrinkage or modified result cannot qualify.
Verifier source fingerprints now include the graph reader.

All version-1 receipts remain scoped historical observations with
**UNVERIFIED configuration-graph qualification**. The new consumer reports
`HISTORICAL_V1_CONFIGURATION_GRAPH_UNVERIFIED` without modifying their bytes.
It does not infer the new relationship from old JSON fields.

The full licensed archive remains on the runner and is excluded from the
uploaded artifact. The job's producer and read-only consumer both recompute
the graph while those original bytes exist. Uploaded sanitized graph hashes
and native reports are insufficient for later complete archive re-verification.
That requires the original archive or a separately identified new capture;
neither a new capture nor retained JSON authenticates prior execution.

GitHub run, registry, scanner execution, source-to-candidate ancestry, Cosign,
exception approval and personal owner approval remain `UNVERIFIED`. This
repair does not run a release or change promotion prerequisites, Required
Checks, exceptions, schedules, baselines, thresholds or product activation.
The four advanced adapters and 14 complete Tier B acceptance conditions remain
outstanding. PRE-AVALONIA SEC-ARCH remains BLOCKED.

## Local execution scope

Focused mechanical controls exercise OCI index/direct-manifest exports,
legacy diff-ID exports, gzip/plain layers, exact CLI operation, exclusive
outputs, tampered/missing manifests/config/layers, wrong platform, ambiguous
scope, forged graph, wrong native config/diff IDs, bounded archive/header/JSON
inputs and historical compatibility. Existing release, SBOM/source/policy
and Required Check topology controls are also rerun unchanged.
The final local suite passed **230 tests**: **39 new graph controls**, **73
Advisory controls** (including ten additional version-2 controls), **34 existing
release controls**, **39 existing native SBOM/source/policy controls** and **45
unchanged Required Check topology controls**, with no failures or skips.
The five relevant Python helpers compiled successfully. All six workflow
`run` steps in the Advisory job passed Bash syntax validation.

A real isolated scratch image was built and saved by the current Docker
daemon. The actual archive graph resolved successfully; real immutable
local-subject and archive-mode Trivy secret-only scans used
`aquasec/trivy@sha256:a22415a38938a56c379387a8163fcb0ce38b10ace73e593475d3658d578b2436`
with networking disabled. Archive mode reported the resolved configuration
and exact diff IDs. The fixture's GHCR label in the graph resolver is synthetic;
no GHCR retrieval, vulnerability DB scan, product runtime, operational release
or cryptographic verification is claimed by that observation. Exact-head
hosted release qualification remains unexecuted.
