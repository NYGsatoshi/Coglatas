# P0 Task execution Active Source Scope completion

Issue: #357

Canonical specification: `Coglatas-Spec@9b35b3f4a34d80097f6b266a0c970c5e61982e80`, `docs/specs/coglatas-core-v4/01-core/24-task-execution-source-scope-owner-decision-resolution.md`

This record completes the visible Active Source Scope contract on top of the policy/snapshot foundation merged in PR #426 and the Task-create integration merged in PR #427.

## Contract under test

The maintained Task detail source-scope panel consumes only the server-authoritative Project and Task scope routes. It does not derive source state from client roles, Project metadata, file lists, integration state, or realtime payloads.

The current release exposes two configurable source kinds: Web and authorized clean Project files. The compatibility state remains `Enabled` / `Disabled`, while the explicit source-policy semantics are displayed alongside it as `Allow` / `Exclude`. The panel also explains the distinct meanings of `Restrict` and `Prioritize` without inventing either richer rule. Specific Sites and Connected Apps are presented generically as unavailable under the current contract.

Issue #461 supersedes the original provider-none completion record with the
server-owned `FirstPartyProjectFilesRuntimeV1` contract. A run request persists
its immutable source-policy snapshot plus provider/version in durable
`Accepted` state. Web execution remains disabled and fails closed; the V1
runtime performs no outbound Web I/O. This #357 record does not claim Project
File materialization or a result: those post-commit server behaviors are owned
by #462 and #463. Scope changes remain next-run changes only.

## Issue #357 acceptance mapping

| Acceptance criterion | Evidence |
| --- | --- |
| Current active source scope is immediately visible from Task detail | `task-execution-scope.component.html` renders the authoritative effective policy and origin in the always-visible panel after the protected read succeeds. |
| Restrict / prioritize meanings are distinguishable | The `Scope terms` section renders visible, non-tooltip definitions for Allow, Restrict, and Prioritize and states that Restrict/Prioritize are not configured by the current contract. |
| Summary can move directly to editing | The server-authorized manager projection renders `Change source settings` linking to `#task-execution-scope-editor`. Mutation authorization is still rechecked server-side. |
| Display remains aligned after a scope change | Existing component behavior refetches Project and Task authoritative HTTP projections after successful saves and matching realtime invalidations. The current panel is explicitly the next-run policy; a previous run snapshot remains immutable and separate. |
| Unauthorized source names/counts are not leaked | API remains boolean/version/origin-only. UI uses only generic capability labels for Sites/Apps and never renders source inventory. Existing denied-refresh and redacted-error tests clear or suppress protected data. |

## Additional canonical checks

- `ProjectDefault` and `TaskOverride` remain visibly distinct.
- Web and Project-file rows preserve `Enabled` / `Disabled` compatibility text and separately expose explicit `Allow` / `Exclude` policy semantics.
- Specific Sites and Connected Apps are not silently omitted and are not inferred from unrelated integrations.
- `Execution provider: First-party Project Files V1` is visible; the UI
  explains that Web is disabled, browser state is not execution authority, and
  no materialization/result is claimed before the follow-on workstreams.
- The most recent run snapshot displays source state at request time independently from the current next-run policy.
- Existing responsive CSS collapses the summary and snapshot grids to one column at `max-width: 720px`; the PR #426 static 320px Task-detail smoke remains the regression surface.
- Existing focus-visible rules cover editor links, buttons, checkboxes/radios, and status feedback.
- Issue #361 remains open for richer source-specific Allow/Prioritize/Exclude authoring and runtime enforcement.

## Automated coverage

Focused Angular coverage:

```text
npm --prefix frontend test -- --include 'src/app/features/projects/task-execution-scope/*.spec.ts'
```

Full frontend regression:

```text
npm --prefix frontend test
npm --prefix frontend run build
```

The Angular smoke regression asserts both compatibility state (`Enabled` / `Disabled`) and explicit policy semantics (`Allow` / `Exclude`) so the maintained Real Backend surface and the richer Issue #357 presentation contract stay aligned.

Repository CI remains authoritative for backend, architecture, security, Playwright, PostgreSQL-qualified, and cross-surface regression gates. The completion PR must not merge until all required CI checks on its head commit are green.
