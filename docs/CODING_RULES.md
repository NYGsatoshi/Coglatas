# Coding Rules

## General

- Keep the app a modular monolith.
- Prefer simple, explicit code over broad abstractions.
- Keep modules separated by namespace and folder.
- Do not add production features outside the current implementation phase.
- Keep public APIs REST-first.
- Use async I/O for database and file operations.

## Project Language

- English is the canonical engineering/project language.
- Use English for source identifiers, code comments, docstrings, test names and descriptions, repository documentation, configuration documentation, commit messages, pull requests, and issues.
- Do not introduce new non-English engineering prose when adding or materially editing repository artifacts.
- Intentional product localization/i18n resources are exempt and should use the language required by the product.
- Do not rewrite localized end-user content solely to satisfy the engineering-language policy.

## Project References

Allowed direction:

- `Coglatas.Web` references `Coglatas.Application` and `Coglatas.Infrastructure`.
- `Coglatas.Application` references `Coglatas.Domain`.
- `Coglatas.Infrastructure` references `Coglatas.Application` and `Coglatas.Domain`.
- `Coglatas.Domain` references no application project.

Composition root lives in `Coglatas.Web`.

## Controllers And APIs

- Controllers or endpoint handlers must stay thin.
- Do not put business logic directly in controllers.
- Do not return EF entities from APIs.
- Use request and response DTOs.
- Use pagination for list endpoints.
- Validate input before calling use cases.
- Return consistent error shapes.
- Avoid leaking whether cross-scope records exist; return `404` or `403` according to the authorization policy.

## Application Layer

- Put use cases in the Application layer.
- Enforce server-side authorization in every modifying use case.
- Use transactions for multi-record changes.
- Create audit logs for important operations.
- Trigger notifications from use cases, not controllers.
- Project query results to DTOs.
- Avoid N+1 queries with projections, includes, or explicit joins.

## Domain Layer

- Keep entities focused on state and local invariants.
- Use enums for controlled status values.
- Use value objects when they protect a real invariant.
- Do not reference ASP.NET Core, EF Core, or infrastructure concerns.
- Use domain methods when direct property mutation would bypass important rules.

## Infrastructure Layer

- Configure EF Core with Fluent API.
- Keep migrations reviewable.
- Configure PostgreSQL indexes for foreign keys and common filters.
- Keep file storage behind an interface.
- Keep search behind an interface.
- Read storage paths, limits, and connection strings from configuration.
- Store timestamps in UTC.

## Security

- Hash passwords with a proven password hasher.
- Never log passwords, tokens, file contents, or secrets.
- Store session tokens and invite tokens as hashes.
- Use secure cookies if cookie auth is chosen.
- Require resource authorization for workspace, group, channel, conversation, project, task, and file access.
- Validate file size, extension, and MIME type.
- Use soft delete where auditability matters.
- Keep audit logs append-only from normal application code.

## Data Access

- Prefer DTO projections for read APIs.
- Use `AsNoTracking` for read-only queries.
- Add pagination to every potentially large list.
- Index foreign keys.
- Index common filters such as workspace, group, channel, project, status, created date, due date, and read state.
- Avoid loading full object graphs unless needed.
- Keep the first Gantt query compact and read-only.

## Naming

- Use `ProjectTask` as the C# entity name for project tasks.
- Map `ProjectTask` to a database table named `Tasks` if desired.
- Use `Utc` suffix for UTC timestamps.
- Use clear DTO suffixes such as `CreateProjectRequest`, `ProjectSummaryResponse`, and `PagedResponse<T>`.
- Use module-specific namespaces.

## Testing

- Add unit tests for domain rules and authorization-sensitive use cases.
- Add integration tests for API authorization boundaries.
- Add tests for cross-workspace and cross-group access denial.
- Add tests for file validation.
- Add tests for Gantt API output shape once implemented.

## UI Shell

- Keep docking and radial menu support data-driven.
- Do not build a fully free-form docking marketplace in the first version.
- Persist `FeatureModule`, `PanelDefinition`, `UserLayout`, `CommandDefinition`, `RadialMenuProfile`, and `RadialMenuItem`.
- Keep command keys stable.
- Treat persisted layout JSON as versioned data.

## Documentation

- Update docs when architecture, modules, entity ownership, or security rules change.
- Keep docs concise and implementation-ready.
- Prefer concrete names and constraints over vague intentions.
