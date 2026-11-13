# Coordinated Storybook Renovate updates

## Motivation

Renovate's generic minor/patch npm group previously proposed Storybook addons
without updating the Storybook CLI and Angular integration. Existing package
manifests require the same Storybook release for their peer dependencies.
Partially updated addons produced npm ERESOLVE and failed the strict clean
install. Historical examples: PRs #1907 and #1908.

## Update policy

The final matching npm minor/patch package rule in \`.github/renovate.json\`
groups \`storybook\` and every \`@storybook/*\` direct dependency under
\`Storybook coordinated updates\` with a stable branch slug. The rule is after
the generic npm group and ANG22 review group so they cannot split updates.

The group requires explicit Dependency Dashboard approval **before** Renovate
creates a proposal. It does not enable automerge. Existing major-update approval
and version restrictions remain unchanged. Both the active \`frontend/\` and
legacy documentation \`coglatas-frontend/\` workspaces are in scope.

Renovate must perform its full npm install and second lockfile generation pass.
The existing required CI then runs ordinary npm clean-install, build, static,
security, and publication gates. No \`--force\`, \`--legacy-peer-deps\`,
lockfile omission, disabled lifecycle-script review, or CI exemption is permitted.

## Peer and lockfile validation

Grouping by itself does **not** guarantee that upstream publishes every
component at the same version. The required publication-readiness policy check
(\`ruby scripts/ci/test-dependency-update-policy.rb\`) therefore rejects a
partial direct Storybook update before merge:

- CLI, Angular integration and direct addons must share one exact version
  across both workspaces;
- each direct dependency must agree with the lockfile root and resolved
  \`node_modules\` entry, with SHA-512 integrity metadata present;
- newly introduced direct \`@storybook/*\` dependencies inherit the same rule.

The policy test includes negative mutations of approval, grouping precedence,
workspace scope, package matching, npm lockfile regeneration, manifest versions
and lockfile roots. A coherent candidate can be approved and tested, but no
production or release qualification follows merely from a green config check.

Already-open Renovate branches are not rewritten or auto-approved by this
configuration PR. Request a fresh provider rebase/retry or close a superseded
candidate after the policy is merged; check each resulting lockfile and CI run
on its actual HEAD.

