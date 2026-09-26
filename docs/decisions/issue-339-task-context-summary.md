# Issue #339 Task context summary decision

Status: implementation contract

Applies to: [Issue #339](https://github.com/NYGsatoshi/Coglatas/issues/339)

Upstream contract: Issue #357 at
`AIPsiteNYGspec@9b35b3f4a34d80097f6b266a0c970c5e61982e80`,
`docs/specs/aip-core-v4/01-core/24-task-execution-source-scope-owner-decision-resolution.md`

## Derived current-release contract

The repository has one authoritative Task context projection suitable for this
Issue: Issue #357's effective source policy. It contains exactly two generic
source kinds, `Web` and `Project files`, plus a server-computed
`ProjectDefault` or `TaskOverride` origin. It deliberately contains no source
inventory.

The compact count is therefore the number of effective policy flags set to
`true`, from zero through two. It is labelled **source kinds allowed**. It is
not a count of files, sites, Apps, integrations, records, or resources that a
runtime can retrieve. The summary repeats each generic kind's `Allow` or
`Exclude` state and identifies the effective origin.

This is the smallest contract that satisfies Issue #339 without violating the
canonical privacy boundary. Project-file names or counts, site hosts, App
names or counts, integration existence, and raw source content remain absent.
Specific Sites and Connected Apps remain unavailable under the current
source-scope contract.

## Interaction and refresh

- The compact summary is visible only after the authorized Task source-scope
  read succeeds.
- Activating it moves keyboard focus to the existing detailed context region.
  The existing capability-gated `Change source settings` link remains the
  direct editor path; every save is independently reauthorized by the server.
- Project/Task invalidations remain hints only. They trigger the existing
  authoritative HTTP refetch, which updates the compact summary and detail
  together.
- Authorization invalidation or a denied authoritative refresh synchronously
  removes the summary and all protected source-scope state. Late responses are
  already rejected by the existing generation boundary.
- The feature adds no API, persistence, inventory query, execution provider,
  or runtime behavior.

## Acceptance interpretation

| Issue #339 criterion | Current-release interpretation |
| --- | --- |
| Major context types and counts are immediately understandable | Show `0–2 of 2 source kinds allowed`, Web state, Project-files state, and effective origin. |
| Summary leads to detail/settings | Keyboard and pointer activation focus the detailed context; authorized managers retain the direct settings link. |
| Project inheritance and Task override differ | Render the server-owned `Project default` or `Task override` origin in the compact summary and detail. |
| Permission changes update the display | Reuse authoritative Project/Task refetch and protected-state clearing. |
| Names/counts do not leak unauthorized information | Count only the two generic policy kinds and explicitly state that it is not an inventory count. |
