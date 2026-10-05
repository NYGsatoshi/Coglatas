# Project IDE Core — #905 Slice 1

Authority: #905 Core contract v1 (`CORE_CONTRACT_READY`, 2026-10-04), #888
A-R2, merged Coglatas-Spec PR #83 at `0a5f234e21dbcec795b57b744e35aa923b10d440`.
Related boundaries: #906 proposals, #908 history, #766 transport, #769 architecture,
#804 dependencies. This implements Slice 1 only; #905 remains open.

## Placement and immutable contracts

`Coglatas.Domain.ProjectIde` reuses the existing dependency-free .NET 10 Domain
project. No project reference, NuGet dependency, API DTO, EF entity, migration,
database, filesystem/network service, UI.Core type or renderer is introduced.
Tests use the existing backend and architecture projects and normal CI routes.

Domain identities are distinct readonly UUID-v4 wrappers: TenantId, ProjectId,
EntityId, RelationId, BranchId, RevisionId, ProposalId, CandidateRevisionId,
ScenarioId, ScenarioRevisionId and DocumentId. Construction validates UUID
version/variant. Serialization uses lowercase hyphenated UUIDs. Parsing accepts
hyphenated UUID case variants for explicit identity import; Source/context/location
wire readers require canonical lowercase spelling. No implicit conversions exist.
Default struct values cannot enter a valid Source/context or be serialized.
Display names and content digests remain separate from identity.

BranchRef and RevisionRef are scoped immutable references. ProposalContext binds
an exact base, frozen candidate and captured target head; ScenarioRef binds an exact
committed/candidate base and overlay digest. SourceRevisionContext discriminates
these identities and rejects conflicting fields and Tenant/Project mismatches.
An ancestor may have a different Branch within the same Tenant/Project. These
contracts implement no Branch storage, review, Merge, current authorization or
history behavior. Context is an envelope binding, never a business AST property.

ProjectLocation uses `coglatas.project-location/1`, exact revision context,
DocumentId, optional EntityId/RelationId, an RFC 6901 property pointer and optional
UTF-16 text span. A span is supplementary buffer navigation, never row-number
authority. Unknown context/location members and omitted versus explicit null
remain distinct in canonical equality and serialization.

## Source schema and codec

`coglatas.project-source/1` has independent schemaId `coglatas.project-source`,
schemaVersion 1 and canonicalEncodingVersion 1. None is an assembly/product
version. It requires tenantId, projectId, context, documents, manifest,
declarations, relations, extensionDeclarations, packageLock and sourceReferences.
Empty arrays are explicit. A document set must contain a `coglatas.project` root.

SourceDocument has stable DocumentId, logicalName, document schema identity,
typed SourceRoot common header (EntityId, namespaced kind, kind schema version,
immutable opaque payload) and verified digest. Document schemaId is
`coglatas.source-document`, version 1. Logical names are not storage paths or
reference identity. The document digest includes all document members, including
unknown members, except its own digest object. SourceManifest pins every document's
ID/schema/digest and must agree exactly with the document set. Content-reference
materialization is deferred; Slice 1 accepts inline immutable roots only.

Declaration/relation arrays retain typed common IDs, kind/version, owning DocumentId
and opaque payload. Relations retain endpoint objects without implementing
descriptor/endpoint semantic resolution. A Source reference carries a DocumentId
plus typed EntityId/RelationId for known reference kinds. External references carry
an explicit scoped context and digest; missing pins fail structural validation.
Reference resolution, visibility and evidence coverage belong to later slices/hosts.
Descriptor and package sets use stable namespace/artifactId keys; their semantic
support, locked artifact verification and executable loading are not implemented.

SourceJson owns a frozen JSON value and canonical representation. It preserves
unknown names, values and their original object boundaries, nested payloads,
unknown enum tokens, null presence and ordered arrays. There is no default-enum
mapping or discard-only deserialization. No plugin code is loaded or fetched.

SourceDecodeResult explicitly distinguishes StructurallyDecoded, Unsupported and
Invalid. **StructurallyDecoded is only envelope/header/integrity validation, never
Compiler PASS, executable-plan availability or authorization.** Slice 1 treats
entity/relation/extension semantics, nonempty root payloads, references and unknown
meaning conservatively as Unsupported while retaining the parsed Source. Future
whole schema/context/document/digest representations remain unsupported raw bytes,
without being interpreted as version 1. Invalid/oversized buffers retain exact
original bytes for the owning incomplete-Draft workflow. No buffer is repaired,
truncated or promoted to an AST. Raw recovery is immutable via returned copies;
authorized bounded storage/loading is a host responsibility.

## Canonical encoding version 1

- UTF-8 without BOM, whitespace or trailing newline. Object members use
  case-sensitive Unicode scalar order, including unknown members. Duplicate keys
  and invalid Unicode/UTF-8 are rejected. Names and strings retain exact scalar
  content; no Unicode normalization, case folding or locale conversion occurs.
- Strings escape only quotes, backslashes and controls; controls use lowercase
  `\u00xx`. Non-ASCII scalars and `/` remain unescaped.
- Unknown JSON numbers use exact digit manipulation, without binary floating point
  or decimal rounding. Remove redundant zeros/exponents; negative zero becomes
  zero. Checked expansion/size/depth limits reject processing, preserving raw input.
  Built-in tagged quantity/rational/time semantic types remain Slice 2 work.
- Documents and manifest documents sort by DocumentId; declarations by EntityId;
  relations by RelationId; descriptors by namespace; package locks by artifactId.
  Source references are an unordered set keyed by their full canonical reference.
  Duplicate keys/IDs are errors. Every other array, including unknown nested arrays,
  remains ordered. Property order does not affect identity.
- UUIDs have canonical spelling; kind/enum tokens stay stable strings. Source
  serialization generates no timestamp. Unknown timestamp strings remain exact;
  typed Instant/civil-date/calendar validation and canonical UTC encoding belong
  to Slice 2. Missing optional values and explicit null are not conflated.
- SHA-256 hashes `UTF8(domainTag) || 0x00 || canonicalPayload`. The Source tag is
  `coglatas.project-source/1`; document tag is `coglatas.source-document/1`.
  Digest objects contain algorithm `sha-256` and 64 lowercase hexadecimal digits.
  Algorithm, representation version and UUID identity are independent. Digest
  equality never proves scope access. Content-derived Compiler/IR/diagnostic UUID-v8
  derivations are later-slice work, not random Source identity regeneration.

Default processing bounds are 4 MiB input/output, 64 nesting levels and 100,000
characters per expanded number. These are defensive codec bounds, configurable
through SourceCodecLimits, not measured deployment/device/storage acceptance or a
promise to store arbitrarily oversized Drafts. Hosts authorize and bound acquisition
before calling the codec. No rejected payload content is included in error reasons.

## Verification and remaining scope

The independently authored golden fixtures fix canonical bytes and Source/document
digests, supplementary-plane property ordering, decomposed/composed Unicode,
exact large decimals, exponents, controls and negative zero. Tests cover all typed
IDs, culture independence (en-US/fr-FR/tr-TR/ar-SA/ja-JP), unknown round-trip,
document reorder, rename/move/reference stability, duplicate IDs/keys, malformed
UTF-8/surrogates, unsupported versions, missing required fields, digest tampering,
scope/context/location separation and bounds. The product boundary is checked
without a UI thread, database or network. Unicode fixture text is intentional
test data under the project language policy.

Review regressions cover exact numeric processing bounds and padded positive,
negative and zero exponents without changing finite values. Boundary negative
fixtures prove both filesystem and exact `System.Environment` dependencies are
detected. Immutable Source digests are computed once without changing canonical
bytes or digest vectors.

Focused invocation:

```sh
dotnet test tests/Coglatas.Tests/Coglatas.Tests.csproj -c Release \
  --filter FullyQualifiedName~Coglatas.Tests.ProjectIde
dotnet test tests/Coglatas.Architecture.Tests/Coglatas.Architecture.Tests.csproj -c Release
```

No seven-concept semantic implementation, typed relation descriptors, Compiler,
IR, diagnostics, analysis, simulation, incremental compilation, Merge/history,
backend adapter or Avalonia/UI integration is included. Source opaque preservation
does not relax strict API mutation DTOs. No persistence/API compatibility boundary
changes and no data reset occur. Next: **#905 Slice 2**.
