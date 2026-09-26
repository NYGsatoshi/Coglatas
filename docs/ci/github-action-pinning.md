# GitHub Action immutable pin policy

Issue: #627

## Scope

Coglatas treats an Actions workflow as **protected** when at least one of these conditions is true:

- the workflow is listed as a repository-required workflow in `governance/github-actions-allowlist.json`;
- it references a GitHub Actions secret or inherits secrets;
- it binds a GitHub Environment;
- it requests a GitHub token write permission.

Protected workflows execute external Actions or external reusable workflows only by a reviewed, full 40-character commit SHA.

The post-#657 repository model does not use `ci/governance`. Enforcement is part of the existing required `publication-readiness` check and is independent of the retired Governance aggregate.

## Repository allowlist

`governance/github-actions-allowlist.json` records, for each approved external repository:

- canonical `owner/repository` identity;
- reviewed commit SHA;
- human-readable upstream version/tag;
- purpose;
- whether use from a protected workflow is allowed.

Unknown external Action repositories fail validation even in an unprivileged helper workflow. This prevents an unreviewed third-party Action from appearing silently while allowing non-protected legacy workflows to migrate to immutable refs without forcing unrelated workflow rewrites into #627.

## Workflow syntax

Protected external use:

```yaml
uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7
```

The following are rejected in protected workflows:

```yaml
uses: actions/checkout@v7
uses: actions/checkout@main
uses: actions/checkout@123abc
```

A full SHA that is not the SHA currently approved in the allowlist is also rejected. The version comment must agree with the allowlist entry.

Repository-local Actions and reusable workflows (`./...`) are bound by the repository commit and do not require an external allowlist entry.

## Enforcement

`scripts/ci/github_action_pins.py` inventories every `uses:` reference under `.github/workflows/` and fails closed on:

- malformed external `uses:` syntax;
- unknown Action repositories;
- a repository not approved for protected use;
- mutable refs in a protected workflow;
- short SHAs;
- a full SHA that differs from the reviewed allowlist SHA;
- missing/mismatched human-readable version comments;
- missing required workflow paths.

The validator emits a machine-readable inventory when `--inventory-out` is supplied. `publication-readiness` runs both the validator and its negative self-tests.

## Dependency update flow

`.github/dependabot.yml` already enables the `github-actions` ecosystem on a weekly schedule. Action updates therefore remain normal reviewable pull requests.

When Dependabot proposes a new Action revision:

1. Inspect the upstream repository/release and confirm the proposed commit belongs to the intended release/tag.
2. Confirm the Action repository remains appropriate for its recorded purpose and protected-use classification.
3. Update the workflow `uses:` SHA and its human-readable version comment.
4. Update the matching `sha` and `version` fields in `governance/github-actions-allowlist.json` in the same reviewed change.
5. Run `python3 scripts/ci/test_github_action_pins.py` and `python3 scripts/ci/github_action_pins.py`.
6. Require the normal repository checks, including `publication-readiness`, before merge.

The allowlist is deliberately not auto-ratcheted to a new SHA. A dependency bot can propose an update, but cannot make the new external code trusted merely by changing a workflow reference.

## Adding a new external Action

A new external Action requires an explicit allowlist entry with purpose and protected-use decision. For protected use, record a reviewed full SHA before adding the workflow reference. If the repository is not allowlisted, CI fails.

Prefer existing approved Actions, repository-local scripts, or repository-local composite Actions when they satisfy the same requirement. Do not add a third-party Action solely to avoid a few lines of repository-owned scripting.

## Migration boundary

#627 establishes immutable refs for required and privileged workflows first. Unprivileged helper workflows are still inventoried and may only use known allowlisted repositories; their mutable refs can be converted in coherent follow-up slices without weakening the protected boundary.

Any workflow that later gains a secret, protected environment, or write permission is automatically promoted into protected scope by the validator and must already be fully pinned before `publication-readiness` can pass.
