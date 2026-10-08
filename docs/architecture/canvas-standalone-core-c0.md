# Coglatas Canvas: standalone core C0 candidate

Issue: [#1125](https://github.com/NYGsatoshi/Coglatas/issues/1125)

## Scope and status

This document describes an **unmerged, implementation-candidate core slice**. It
does not constitute a working Canvas UI, a new product acceptance gate, or an
expansion of frozen MVP-A/MVP-B requirements. No ProjectIDE integration is part
of C0.

The source under `src/Coglatas.Domain/Canvas/` is independent of ASP.NET Core,
EF Core, Avalonia, SignalR, `Coglatas.Domain.ProjectIde` and any ProjectIDE
domain type. It has no routes, database schema, registration, deployment hook or
other runtime activation. Its tests live under `tests/Coglatas.Tests/Canvas/`.

## Document and identity contracts

- `CanvasDocument`: `Id`, `TenantId`, `WorkspaceId`, `SchemaVersion`,
  `Revision`, pages.
- `CanvasPage`: stable ID, name, regions.
- `CanvasRegion`: stable ID, Freeform/MindMap/Diagram kind, layout policy,
  nodes and connections.
- `CanvasNode`: stable ID, visual kind, text, position, size, optional
  MindMap-only parent and pinned state.
- `CanvasConnection`: stable ID, endpoints within one region, CrossLink or
  Connector, optional symbolic port names.

All IDs are UUIDs and are unique within one Canvas document (except separately
scoped Tenant/Workspace identities). Numeric geometry must be finite and node
sizes strictly positive. The model is intentionally visual: tree parentage and
graph connectors do not imply executable dependencies or workflow semantics.

A MindMap region may be empty. A nonempty one has exactly one root; all other
nodes have an in-region parent, and cycles are prohibited. Cross-links are
separate from parentage. A Diagram region permits directed cycles.

The snapshot and editor use immutable arrays. `CanvasDocumentEditor.Apply`
takes a typed command with an expected revision and produces a new snapshot
only after full validation. A stale expected revision raises a conflict.
A failed operation never mutates the caller's snapshot. Deleting a node with
MindMap children is rejected; deleting a leaf or Diagram node drops its incident
connections.

## Deliberately deferred

- Actual Canvas view, hit-testing, zoom/pan, layout algorithms, connector
  routing, stylus ink, undo/redo UX and shape/stencil library.
- Persistence, schema migration, audit, server-side authorization,
  idempotency storage, quotas, realtime ordering, snapshot retention and
  immutable-operation-log durability.
- Import/export and external sharing.
- Education/Enterprise role-specific workflows.
- **ProjectIDE integration, references, adapters, compiler, IR and semantic
  graph synchronization.**

The presence of Tenant/Workspace IDs in the document is *not* proof of isolation
or access control. A future service must authorize each read/write against
current scope and capabilities and commit the expected revision atomically with
the mutation. External sharing must default to denied. Personal/school data
must not be piloted until the applicable operations and security gates pass.

## Future integration boundary, not an implementation requirement

Consumers should address Canvas documents, pages, regions and nodes by stable
IDs and schema/revision. No ProjectIDE entity identity or typed source model
is embedded in a node. If integration is later authorized, a separate adapter
can translate selected Canvas content into independently reviewed ProjectIDE
proposals. Freeform hierarchy and connector edges must not silently become
Task dependencies, Requirements or executable actions.

## Validation

The accompanying unit tests target tree constraints, cross-links, cyclic
diagram connections, stale revisions, atomic failure, invalid geometry,
duplicate IDs and foreign endpoints.

No build/test or CI success is claimed by this document. This slice requires
normal protected CI and owner review before merge. Future slices require their
own separately scoped implementation and security verification.
