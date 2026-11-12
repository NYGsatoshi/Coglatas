# Coglatas

> [!IMPORTANT]
> **Public source, not open source.** This repository is visible for transparency,
> technical evaluation, portfolio review, and CI. No permission is granted to
> use, copy, modify, redistribute, sublicense, commercially exploit, or create
> derivative works from repository-owned material except as required by GitHub's
> Terms of Service or applicable law. See [COPYRIGHT.md](COPYRIGHT.md) and
> [CONTRIBUTING.md](CONTRIBUTING.md).

Coglatas is a tenant-aware project collaboration and execution platform that is
evolving toward a **Project IDE**: one typed project model presented through
multiple projections, with reviewable changes, diagnostics, history/diff,
compilation, simulation, and execution-flow inspection.

The current repository contains a substantial .NET backend and a **maintained
Angular Web client**. A separate C# / Avalonia Desktop frontend is planned as an
architectural migration of shared interaction and application contracts, **not**
as a replacement or retirement of the Angular browser application.

> [!WARNING]
> Coglatas is under active development. It is suitable for development,
> architecture review, CI evaluation, and controlled technical testing, but it
> is **not yet a turnkey production deployment**.

## Current direction

The current product and frontend direction was revised in September 2026.

### Project IDE

The approved initial Project IDE scope includes:

- Table, Board, Inspector, Problems, Graph, History/Diff, Dock, and Gantt.
- Typed Project concepts for Task, Milestone, Dependency, Requirement,
  Resource, Constraint, and Workflow.
- A separation between editable Project source, derived IR, presentation state,
  and simulation/runtime state.
- Desktop-local compilation and analysis.
- A staged Project change lifecycle:
  <code>Draft -&gt; Open -&gt; WaitingForReview -&gt; Merge</code>.
- Simulation with breakpoints, inspection, adjustment, recompilation, and
  execution-flow comparison.
- Version-aware History and semantic Diff.

The Project IDE decision register is
[#888](https://github.com/NYGsatoshi/Coglatas/issues/888). The first approved
implementation slices are tracked by
[#905](https://github.com/NYGsatoshi/Coglatas/issues/905),
[#906](https://github.com/NYGsatoshi/Coglatas/issues/906),
[#907](https://github.com/NYGsatoshi/Coglatas/issues/907), and
[#908](https://github.com/NYGsatoshi/Coglatas/issues/908).

Several details are deliberately still undecided, including measured
Web/Mobile capability limits and release tiers, arbitrary branch semantics, long-term
history retention, production external side effects, and the detailed analysis
rule / Quick Fix policy. Do not treat proposals for those areas as implemented
or approved behavior.

### Frontend platforms: Angular Web and Avalonia Desktop

- **Web:** Angular is a maintained product client under `frontend/`.
  Approved features, fixes, localization, and accessibility work continue; Web
  delivery is not a temporary fallback awaiting an Avalonia-only cutover.
- **Desktop:** C# / Avalonia is a separate planned frontend. It must pass the
  applicable security, platform, and release acceptance gates before activation.
- The Desktop Project IDE effort follows **Project IDE Foundation -> Projection
  Migration**, not independent screen ports. Shared interaction, selection,
  version, diagnostics, and projection contracts precede renderer adapters.
- Both clients consume the existing **server-authoritative** API and business
  rules. Angular does not directly consume C# UI.Core; reusable .NET layers
  remain independent of Avalonia, Dock.Avalonia, MSAGL, and other vendor types.
- Historical all-Avalonia and Angular-retirement language in
  [#764](https://github.com/NYGsatoshi/Coglatas/issues/764),
  [#798](https://github.com/NYGsatoshi/Coglatas/issues/798), and
  [#803](https://github.com/NYGsatoshi/Coglatas/issues/803) is superseded
  **only for Web/Desktop platform ownership and the proposed Web deletion**.
  Existing security, API, functional, and Desktop acceptance gates remain.

See the [frontend platform policy](docs/frontend/platform-policy.md) for the
decision boundary. The Desktop baseline remains **.NET 10 + Avalonia 12.1.x**.
The existing UI-independent <code>src/Coglatas.UI.Core</code> project remains
renderer-neutral. Desktop admission remains subject to
[SEC-ARCH preflight](docs/verification/security-architecture-preflight.md).

## What exists today

The implementation is broader than the original school-portal prototype, but
the backend remains the most mature part of the repository.

| Area | Current state |
| --- | --- |
| ASP.NET Core modular monolith | Implemented |
| .NET target | .NET 10 |
| PostgreSQL + EF Core migrations | Implemented |
| Tenant-aware persistence and authorization boundaries | Implemented |
| Authentication, session, CSRF, lockout, password flows | Implemented |
| Workspace / Group / Project / Task backend | Broad implementation |
| Messaging / Announcements / Notifications | Broad backend implementation |
| Files / artifacts | Local storage implemented; object storage remains future work |
| Audit / search / admin surfaces | Broad backend implementation |
| Angular frontend under <code>frontend/</code> | Active current browser frontend |
| <code>Coglatas.UI.Core</code> | Present as a UI-independent C# foundation |
| Avalonia Desktop frontend | Planned / qualification-gated; not production-qualified |
| Project IDE Compiler / IR / proposal / simulation / history subsystems | Approved scope; implementation work remains |
| External production execution provider | Not implemented |
| Outbound webhook dispatcher / API-token auth / external SSO | Not complete |

For detailed backend evidence and known limitations, see
[docs/AI_CONTEXT.md](docs/AI_CONTEXT.md),
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), and
[docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md).

## Architectural principles

### Server authority stays authoritative

Tenant, Workspace, Project, Task, permission, revocation, and concurrency rules
remain server-authoritative. Client-side ActionGate, diagnostics, compilation,
or simulation results do not grant permission to mutate shared state.

### Project source is not renderer state

Coglatas distinguishes:

~~~text
authoritative Project source / proposal
            |
            v
     typed application boundary
            |
            v
      compiler / derived IR
            |
            +----> diagnostics / source map
            |
            +----> simulator / run trace
            |
            +----> Table / Board / Graph / Gantt / Inspector projections
~~~

A renderer, graph-layout engine, or Dock layout must never become the
authoritative Project model.

### Proposed changes are separate from live state

The approved Project change flow is:

~~~text
live/base revision
 -> Draft
 -> Open
 -> WaitingForReview
 -> Merge
 -> updated live Project
~~~

Saving or compiling a proposal must not silently update the live Project.
Revision identity, current authorization, concurrency, and adopted checks must
be revalidated when changes are applied.

### Framework and vendor isolation

UI-independent code must not depend on Avalonia controls or vendor runtime
types. Specialist libraries are isolated behind owned adapters so that
renderers and hosting frameworks can be replaced without rewriting product
semantics.

## Repository layout

~~~text
src/
  Coglatas.Domain/          Domain entities, enums, and common domain types
  Coglatas.Application/     Use cases, DTOs, authorization, service contracts
  Coglatas.Infrastructure/  EF Core, PostgreSQL, repositories, storage, audit
  Coglatas.Web/             ASP.NET Core host, middleware, controllers
  Coglatas.UI.Core/         UI-independent interaction / projection foundation

frontend/                    Maintained Angular Web client
coglatas-frontend/           Inactive legacy Angular scaffold
tests/
  Coglatas.Tests/            Backend unit/service/HTTP/integration tests
  Coglatas.Architecture.Tests/
  ui/                        Browser / Playwright / real-backend test tooling

docs/
  migration/avalonia/        Avalonia migration evidence and contracts
  quality/                   Quality and static-analysis documentation
  archive/                   Historical plans and status snapshots

scripts/                      CI, validation, and repository automation
tools/                        Analysis and migration-support tooling
~~~

## Development environment

### Requirements

- .NET SDK **10.0.403** as pinned by <code>global.json</code>
- Node.js 26 LTS
- npm 11.x
- Docker + Docker Compose
- PostgreSQL 18 through the recommended local Compose profile

### Recommended local setup

Use Docker for PostgreSQL only and run the backend/frontend on the host.

~~~bash
dotnet restore Coglatas.slnx
dotnet tool restore

docker compose -f infra/compose/dev/db.yml up -d

dotnet ef database update \
  --project src/Coglatas.Infrastructure \
  --startup-project src/Coglatas.Web

npm --prefix frontend ci
~~~

Start the backend:

~~~bash
dotnet run --project src/Coglatas.Web
~~~

Start the current Angular frontend in another terminal:

~~~bash
npm --prefix frontend run start
~~~

The default development PostgreSQL profile is exposed on
<code>localhost:5433</code>. Override
<code>ConnectionStrings__DefaultConnection</code> if a different PostgreSQL
instance is required.

See [README.dev-env.md](README.dev-env.md) for the supported development modes.

## Verification

Common local verification commands include:

~~~bash
dotnet test Coglatas.slnx --configuration Release
npm --prefix frontend test
npm --prefix frontend run test:architecture
npm test
~~~

The repository also contains functional real-backend tests, Playwright flows,
security checks, dependency checks, architecture tests, and static-analysis
gates. CI behavior varies by change scope; a green lightweight documentation
route must not be interpreted as exhaustive product validation.

### Qodana / inspection-debt work

Static-analysis debt is being reduced under the Qodana program while preserving
runtime behavior, external contracts, test strength, and security properties.
The parent program is
[#862](https://github.com/NYGsatoshi/Coglatas/issues/862), with bounded
implementation lanes including
[#894](https://github.com/NYGsatoshi/Coglatas/issues/894) and
[#895](https://github.com/NYGsatoshi/Coglatas/issues/895).

## Deployment

The repository includes Docker Compose and development deployment tooling.
These paths are intended for controlled development/test environments.

For deployment details, see:

- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md)
- [docs/OPERATIONS.md](docs/OPERATIONS.md)
- [deploy/gcp/README.md](deploy/gcp/README.md)

Production deployment requires an explicit HTTPS/reverse-proxy topology,
backup/recovery design, monitoring, hardened secret management, and completion
of the relevant release/security gates.

## Documentation

Start with:

- [AI context](docs/AI_CONTEXT.md)
- [Architecture](docs/ARCHITECTURE.md)
- [Development](docs/DEVELOPMENT.md)
- [Deployment](docs/DEPLOYMENT.md)
- [Security model](docs/SECURITY_MODEL.md)
- [Database](docs/DATABASE.md)
- [Testing](docs/TESTING.md)
- [Known issues](docs/KNOWN_ISSUES.md)
- [Backend logic audit](docs/BACKEND_LOGIC_AUDIT.md)
- [Coding rules](docs/CODING_RULES.md)
- [API conventions](docs/API_CONTRACTS.md)
- [Operations](docs/OPERATIONS.md)
- [Roadmap](docs/ROADMAP.md)
- [Archive index](docs/archive/README.md)

Archived documents are historical evidence, not current project truth.

## Status language

Project documentation uses these terms consistently:

- **Implemented** — wired into the running system with direct code evidence.
- **Partially implemented** — meaningful implementation exists, but an
  end-to-end path or enforcement layer is incomplete.
- **Approved scope** — explicitly authorized for design/implementation, but not
  necessarily implemented.
- **Planned** — future work without implementation evidence.
- **Deprecated** — retained only for compatibility or historical reference.
- **Needs verification** — source or runtime evidence is insufficient.
