# Runtime image identity across Docker image stores

The producer's `runtime-image-id` is retained without rewriting. The classic
restore path still requires the loaded tag's image ID to equal that value.
An unequal ID is accepted only by the bounded verification described here;
an arbitrary second ID or an operator assertion of equivalence is insufficient.

## Supported content binding

`scripts/ci/runtime_image_identity.py` supports one Docker-save OCI layout with
exactly one Linux/amd64 OCI manifest. The source-derived Main or PR tag, source
stamp and restored .NET stamp must match the caller's exact source revision.
The manifest's config digest must equal the unchanged producer image ID.

The verifier streams the compressed archive without extracting members. It
checks every included blob's SHA256, descriptor size and media type, rejects
duplicate JSON fields and archive members, and requires the Docker-save legacy
manifest to select the same config, tag and ordered layers. Every layer must
use the observed uncompressed OCI tar representation and equal the corresponding
config RootFS DiffID. Unauthenticated graphs/blobs, multiple manifests, redirected members,
compressed layers and other platforms are unsupported and fail closed. Member,
byte, layer and retained-metadata bounds also apply.

Only regular-file and empty-directory raw tar headers are supported. PAX/GNU
extensions, sparse files and special entries are rejected before tarfile can
read their metadata payloads or hide them behind a returned ordinary member.
The tar end marker and remaining zero padding are checked, and the entire gzip
stream, including concatenated members, CRC and trailer, must validate.

The observed Docker-save legacy bundle is also supported, with exactly one v1
auxiliary per ordered layer, one repository mapping and matching `LayerSources`.
Auxiliaries are reconstructed byte-for-byte from the authenticated config,
ordered ChainIDs and fixed Docker zero defaults; their exact hashes, sizes and
bytes must match. The repository must select the exact tag and last DiffID.
Auxiliary JSON supplies no independent runtime authority. Unknown metadata,
missing/extra nodes, another serializer schema or a changed config/parent/ID
fails closed. The supported config projection is the seventeen fixed Docker
fields plus `ExposedPorts`; other optional fields remain unsupported.

The reconstruction follows immutable Moby commit
`89c5e8fd66634b6128fc4c0e6f1236e2540e46e0` (v28.5.2):
[v1 ID construction](https://github.com/moby/moby/blob/89c5e8fd66634b6128fc4c0e6f1236e2540e46e0/image/v1/imagev1.go#L44-L68),
[export projection](https://github.com/moby/moby/blob/89c5e8fd66634b6128fc4c0e6f1236e2540e46e0/image/tarexport/save.go#L438-L472)
and [Docker config field order/defaults](https://github.com/moby/moby/blob/89c5e8fd66634b6128fc4c0e6f1236e2540e46e0/api/types/container/config.go#L44-L73).
Their raw-file SHA256 values are respectively
`73dc429bb823e30d14ea25e2e9c852aaa7786f0db8b2f5c905c98f4fb9dc62b3`,
`61af163ccab42982e47b34606de4a79e299b061414e91d03ba27f88b1e05eea1`
and `08df9dce6147eb2a4edb9859d81fedbdb13f2af2e249db38bf79361f67db4bcf`.
These are algorithm references, not a claim about the producer engine version.

The loaded tag must resolve to the authenticated OCI manifest digest, with the
same descriptor size/media type, Linux/amd64 platform and ordered RootFS DiffIDs.
The resulting proof retains the original producer config ID and adds a separate
engine image ID. It includes archive and verifier hashes and the source/.NET
stamp hashes. It does not retain config environment, command or label contents.
Errors are finite messages rather than raw archive or Docker metadata.

Each subsequent use recomputes the archive graph, rechecks both stamps and
loaded metadata, and compares the proof exactly. A proof file alone is not
an attestation or an alternative source of producer identity.

## Consumer continuity

The canonical restore helper exports `COGLATAS_REUSE_PREBUILT_APP_IMAGE_ID` as
the unchanged producer ID. The distinct engine ID, proof path, archive path,
source SHA and .NET stamp path are separate reuse inputs. On the classic path
the engine ID equals the producer ID and no proof is needed.

The legacy acceptance helper revalidates the binding before and after its
existing boundaries. The selected app container's `.Image` must equal the
verified engine ID exactly. Build/pull prohibitions are unchanged. Classic
receipts retain schema1. The bounded alternate path uses schema2, preserving
the producer ID and recording the engine ID and proof SHA256. The final verifier
requires all four canonical receipts and recomputes the proof before accepting
them. Missing, redirected or altered proof/receipts fail closed.

## Separate tooling and runtime provenance

A separately reviewed tool revision may inspect an unchanged runtime artifact
without being copied into that runtime's source checkout. Invoke the helper
from its own immutable checkout, passing the artifact archive, producer source,
exact tag/producer/engine IDs, restored .NET stamp and a separate proof path.
Record the tool commit/tree and file hash separately from the producer and
server/test commits. `restore` creates the proof; `verify` rechecks it. This
standalone interface does not start an app, restore the .NET archive or alter
any source stamp. Existing consumer scripts need the corresponding reviewed
integration before using the additional proof inputs.

The Functional harness's reuse branch verifies the current clean tracked Git
source and canonical restored .NET stamp before starting services. For a
separate tool bundle, set `COGLATAS_FUNCTIONAL_TOOL_SHA` explicitly. All three
executed tool files (harness, reuse helper and identity verifier) must match
that commit's raw Git blobs, available locally; no implicit fetch or metadata
rewrite occurs. An absolute tool-harness invocation uses Compose and test
paths from its current application checkout. The tool bundle may therefore
remain separate from that unchanged checkout.

Functional app startup uses `--no-build --pull never`; the probe uses
`--no-deps`. Tag/container checks run before the probe and again after it,
including on probe failure, before the existing cleanup trap removes the
container. Nonreuse commands, migration SDK/restored-build handling, readiness
limits, test arguments and cleanup remain unchanged. A finite stdout
`FUNCTIONAL_IMAGE_REUSE` JSON record includes actual tool-file hashes, separate
tool/runtime revisions, identities and the probe exit code. It is a completion
record for that invocation, not a fifth legacy receipt. A nonzero probe remains
failed even when continuity verifies; failed continuity emits no completion
record. Retain and independently bind that output to the actual local run.

Synthetic tests use Docker command stubs to exercise classic/alternate paths,
graph mutations, tag/container continuity and receipt rejection. They do not
establish actual containerd app-container `.Image` behavior, real collector
execution, successful application startup or hosted/target acceptance. If an
engine represents container identity differently, the exact check fails; it
must not be replaced with an unverified producer-or-engine fallback.

This tool change supplies no application authorization, security-mapping,
distribution, performance, recovery or release approval. Existing required
checks, independent quality gates and Owner/Spec holds remain applicable.
