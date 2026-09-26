# Issue #461 First-party Project Files runtime V1 decision

Status: Canonical contest runtime contract

Applies to: [Issue #461](https://github.com/NYGsatoshi/Coglatas/issues/461)

## Decision

The only contest execution provider is `FirstPartyProjectFilesRuntimeV1` at
runtime-contract version `1`. Provider selection is server-owned and is
recorded immutably with every accepted Task execution run. The browser cannot
select a provider, submit provider configuration, or supply execution input.

The V1 lifecycle has exactly one state machine:

`Accepted` -> `Queued` -> `Running` -> (`Succeeded` | `Failed`)

An accepted idempotent request creates one logical run. Replaying its same
`Idempotency-Key` returns that logical run rather than creating another run or
result. A new execution request requires a new key. V1 has no automatic retry
or cancellation contract; a committed run proceeds to a terminal state.

## Network and Web boundary

Web execution is disabled. V1 performs no outbound HTTP, URL fetching, DNS
resolution, browser-controlled remote-source processing, external AI/provider
call, proxy bypass, or use of API keys/provider credentials. The existing Web
source-policy flag remains representable for compatibility with the Active
Source Scope, but a run whose immutable scope requires Web input fails closed
with a safe, generic runtime failure code. It never silently ignores or fetches
that input.

## Authorized input boundary

V1 may consume only Project Files materialized by the server from the durable
run's authoritative Tenant -> Workspace -> Project -> Task ownership chain.
At materialization, the worker must reauthorize the run scope and every
candidate file; require current same-Tenant, Workspace, and Project ownership;
require current authorization; reject deleted, revoked, unsafe, or unclean
files; and retrieve bytes only through the existing server-owned storage/file
services.

Browser file IDs, storage paths, object keys, filenames, source lists, bytes,
URLs, and credentials are never authorization authority and are not part of the
runtime handle. The initial supported input is bounded text extracted by
existing safe application support, such as plain text, Markdown, JSON, CSV, or
another already-supported text-like format. Unsupported binary input must be
safely skipped or fail using the smallest compatible server contract. It must
not disclose inaccessible names, counts, content, paths, or security state.

## Output and durable result boundary

V1's minimum successful output is a deterministic **Project Files Analysis
Report**, not AI-generated research or semantic synthesis. Its bounded,
authorized content can state safe source label/version identity, media type,
approved content hash, bounded byte/line/word statistics, a bounded text
excerpt, execution timestamp, and immutable provenance references. The report
must demonstrate genuine consumption of materialized content.

There is one normal server-owned durable result per successful logical run. It
preserves the Tenant, Workspace, Project, Task, run identity, result schema,
terminal status, safe title, bounded body, timestamps, optional content hash,
and immutable approved provenance. It must not persist credentials, provider
settings, storage paths/keys, browser secrets, raw exceptions/stack traces,
unbounded source bodies, or inaccessible metadata.

Normal Task results are distinct from the ArtifactVersion -> Claim -> Evidence
graph owned by Issue #340. They must not mutate or compete with that audit
model.

## Dispatch and implementation ownership

Acceptance persists an authoritative `Accepted` run before any execution is
eligible for dispatch. The runtime is invoked only by a server-side,
post-commit worker using an opaque run handle. The worker re-reads the durable
run and derives its own authority/input; the database may be the durable
coordination boundary. V1 introduces no Kafka, Confluent, Redis, RabbitMQ,
Hangfire, microservice, new storage backend, or external provider dependency.

Issue #461 formalizes this provider, lifecycle, immutable identity, and
post-commit port only. Issue #462 owns server Project File materialization and
the transitions through `Queued` and `Running`. Issue #463 owns normal durable
result persistence, retrieval, and the terminal result contract. Until those
implementations land, an `Accepted` run is deliberately not synthetic success,
not `RuntimeUnavailable`, and not evidence that file content was consumed.

## Security invariants

- Tenant, Workspace, Project, Task, file eligibility, and current authorization
  are always server-enforced.
- PostgreSQL constrains every persisted run to
  `FirstPartyProjectFilesRuntimeV1` at contract version `1`; raw inserts cannot
  introduce an alternate provider or version, and updates cannot rewrite the
  accepted identity.
- The immutable accepted scope snapshot remains provenance, but later source
  materialization rechecks current authority and file safety.
- Failures use safe public codes/messages and do not reveal inaccessible source
  identity, count, content, paths, credentials, provider diagnostics, or raw
  exception details.
- The browser is a caller and presenter, never the source of execution
  authority or lifecycle completion.
