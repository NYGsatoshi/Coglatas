# Issue #357 Task execution source-scope decision

Status: Canonical current-release contract complete

Approved foundation: 2026-08-25

Canonical completion: 2026-08-29

Applies to: [Issue #357](https://github.com/NYGsatoshi/Coglatas/issues/357)

Canonical specification baseline:
`AIPsiteNYGspec@9b35b3f4a34d80097f6b266a0c970c5e61982e80`

Canonical source:
`docs/specs/aip-core-v4/01-core/24-task-execution-source-scope-owner-decision-resolution.md`

## Current-release contract

Issue #357 owns the visible and server-authoritative Active Source Scope boundary. The first release has exactly two configurable source kinds:

- Web;
- currently authorized, clean Project files.

A Project owns one default policy. A Task either inherits that policy or owns one complete override. The effective policy contains `webEnabled` and `projectFilesEnabled`; a missing Project policy fails closed as `false` / `false`.

The UI presents enabled as **Allow** and disabled as **Exclude**. It also explains the distinct mental models for **Restrict** and **Prioritize** but does not invent either rule from the two booleans. Specific Sites and Connected Apps are explicitly shown as unavailable under the current contract. Issue #361 remains the separate P1 contract for richer per-source Allow/Prioritize/Exclude authoring and enforcement.

## Provider and execution boundary

The original provider-none foundation is superseded for provider selection by
Issue #461's `FirstPartyProjectFilesRuntimeV1` decision. Issue #357 continues
to own this Active Source Scope, its complete Project/Task override behavior,
and the immutable accepted-policy snapshot.

The V1 runtime still performs no Web search, crawl, fetch, DNS, redirect
handling, socket/network egress, browser-controlled remote-source processing,
provider credential use, or external provider call. A Web-enabled scope is
therefore rejected safely by the server runtime rather than fetched or silently
ignored. The complete provider, lifecycle, result boundary, cancellation, and
retry decision is recorded in
`docs/decisions/issue-461-first-party-project-files-runtime-v1.md`.

Issue #461 changes an accepted run from the historical
`RuntimeUnavailable` terminal record to durable `Accepted` state with an
immutable provider identity and contract version. It deliberately does not add
materialization or output: Issue #462 owns post-commit Project File
materialization, and Issue #463 owns durable Task results.

## Scope-change timing

Each accepted run request atomically captures the current server-built policy snapshot: origin, Project policy version, Task override version when applicable, and the two source flags. Later edits never rewrite the snapshot.

`changesApplyTo` is `nextRun`. Task detail therefore shows the current effective next-run policy separately from the most recent immutable run snapshot. Realtime remains an invalidation hint only; the browser refetches the authoritative HTTP projection after matching Project/Task changes.

## Authorization and redaction

- Reads require current Project/Task visibility.
- Project default changes, Task override changes, clearing an override, and run requests require current Project-management authority.
- Missing, deleted, cross-Tenant, cross-Workspace, and unauthorized resources use the safe indistinguishable not-found boundary.
- Source-scope API, UI, audit, and realtime never disclose unauthorized source/file/site/App names, IDs, counts, integration existence, raw content, credentials, storage paths, provider configuration, prompts, or output.
- Generic capability labels such as Web, Project files, Specific sites, and Connected apps are not inventory disclosures.
- Authorization invalidation clears protected UI state; ordinary Project/Task invalidation triggers an authoritative refetch.

## Completion rule

Issue #357 may close when the maintained Task UI and verification prove:

1. Active effective scope is immediately visible.
2. Project default versus Task override is explicit.
3. Allow/Exclude state and Restrict/Prioritize meanings are visibly distinguished.
4. Specific Sites and Connected Apps truthfully show current-contract unavailability.
5. The summary links to the authorized editor.
6. Next-run policy and immutable run snapshot timing stay distinct after edits.
7. Provider None is stated without claiming Web or file retrieval.
8. Unauthorized source inventory cannot leak.
9. Keyboard and 320px-responsive behavior remain covered.

This completion does not close Issue #361 and does not promote general Task automation/bots or a network-capable research worker.
