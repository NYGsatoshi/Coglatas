# U-22 architecture summary

## Submission-path design

Coglatas is a modular monolith. The U-22 path is an Angular browser client
served by an ASP.NET Core host, with application services enforcing the
resource rules and EF Core/Npgsql persisting relational state in PostgreSQL.
It is not a collection of independently deployed task-execution services.

```text
Angular browser
    |
    | cookie authentication and CSRF for unsafe requests
    v
Coglatas.Web
    | tenant resolution, authentication, controllers, middleware
    v
Coglatas.Application
    | use cases, DTOs, resource authorization, idempotency rules
    v
Coglatas.Domain <---- Coglatas.Infrastructure
                       | EF Core, Npgsql/PostgreSQL, audit, Outbox adapters
                       v
                    PostgreSQL
```

The browser is a presentation client. Hiding a button, retaining a local form
value, or remembering a selected Workspace never grants authority. Each
modifying use case checks the current server-side context.

## Scope and authorization boundary

The submission path keeps the following ownership chain explicit:

```text
Tenant
  -> Workspace
       -> Project
            -> Task
                 -> Task Brief, source policy, current phase, Activity read
```

- Tenant filters and tenant-aware save rules protect tenant-owned data.
- Workspace and Project reads and mutations use their existing resource
  authorization boundaries.
- Task create options are server-projected for the current readable Project;
  they are not an authority grant.
- Task creation rechecks authority, Project membership and eligible selected
  resources inside its durable command boundary.
- Task detail and Activity reads resolve the Task through the Project read
  boundary before returning protected metadata.

Cookie-authenticated unsafe requests use the existing CSRF boundary. Canonical
Workspace, Project, and Task create commands use idempotency keys so an
uncertain retry cannot intentionally become a duplicate create.

## U-22 workflow path

1. An authenticated user selects an authorized Workspace or uses the
   server-authorized Workspace create capability.
2. Project create options are read for that Workspace. A Project is created as
   Draft and explicitly activated before the operational Project surface is
   used.
3. The Project's New Task form obtains canonical create options. It owns Task
   metadata, optional structured Brief fields, a Project Milestone, and only
   authorized named Assignee choices.
4. The form shows its effective source policy and an advisory checklist. The
   checklist is local UI guidance; the authoritative create validation,
   authorization, CSRF, and idempotency checks remain server-side.
5. One create transaction stages the Task, initial workflow placement,
   permitted Task scope override, audit data, and required invalidation work.
   It returns an authoritative Task response before the browser opens Task
   detail.
6. Task detail reads the current state and configured Workflow Stage as the
   current phase. Activity is a separate authorized, bounded read and is not
   needed for the phase to remain visible.

## Source-policy boundary

The current source-scope foundation stores only two policy booleans: Web
eligibility and Project-files eligibility. A Project can own the default and,
where currently authorized, a Task can own a complete two-boolean override.
Immutable policy snapshots belong to future run requests; routine Task
creation does not start a run or create an execution snapshot.

The currently registered runtime port is unavailable and performs no I/O. It
does not receive URLs, source identifiers, file bytes, credentials, prompts,
provider settings, or raw content. There is no outbound Web retrieval, file
materialization, provider selection, hosted execution worker, runtime output,
or raw-source persistence in the U-22 scope.

## State, phase, and Activity

The Task state vocabulary remains controlled by the current workflow model.
The displayed current phase is the configured current Workflow Stage; it is
not an inferred completion percentage. Blocked is independent of the phase.
Activity records are shown only when they already exist under the authorized
Task boundary. The UI does not fabricate Failed, Needs input, phase history,
or progress history from Activity volume.

## Operational note

Transactional Outbox and other in-process hosted work in the monolith are not
a Task-execution queue. Realtime messages are treated as invalidation hints;
the browser refreshes authoritative HTTP projections rather than trusting an
event payload as complete Task state.
