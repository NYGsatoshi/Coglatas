# Renovate dependency artifact integrity — 2026-11-16

## Objective and scope

Prevent known incompatible dependency candidates from being regenerated, make
provider-side artifact generation failures visible, and preserve strict CI checks.
This change targets `main` from a dedicated non-Renovate branch. It does not
write to any `renovate/*` branch, disable locked restores, approve a dependency
major migration, or relax dependency/security gates.

## Reproduced failures (original provider heads)

| Candidate | First causal failure | Evidence |
| --- | --- | --- |
| [#2000](https://github.com/NYGsatoshi/Coglatas/pull/2000) | `@angular/build@22.2.7` installs `@emnapi/core` and `@emnapi/runtime@2.0.0-alpha.6`, but `@napi-rs/wasm-runtime@1.2.7` requires a compatible 1.x or 2.0.0 beta peer. `npm ls` reports `ELSPROBLEMS` even after a second clean install. | [npm producer](https://github.com/NYGsatoshi/Coglatas/actions/runs/43857626199), [Angular acceptance](https://github.com/NYGsatoshi/Coglatas/actions/runs/43857626055) |
| [#1916](https://github.com/NYGsatoshi/Coglatas/pull/1916) | `stylelint-config-standard-scss@18.0.0` requires `stylelint@^18` but the workspace stays on 17.16.0; `npm ci` fails with `ERESOLVE`. | [npm audit/install](https://github.com/NYGsatoshi/Coglatas/actions/runs/43857677454) |
| [#2003](https://github.com/NYGsatoshi/Coglatas/pull/2003) | EF Core project references change 10.0.15 to 11.0.0 without updating `packages.lock.json`. CI reports five direct-package requested-range mismatches. Npgsql's approved EF Core provider remains 10.0.3, requiring separate major compatibility work. | [CI routing self-test](https://github.com/NYGsatoshi/Coglatas/actions/runs/43857782402), [PR CI](https://github.com/NYGsatoshi/Coglatas/actions/runs/43857782505) |
| [#2001](https://github.com/NYGsatoshi/Coglatas/pull/2001) | PostgreSQL 18.7 runs, but the FileIntake HTTP integration test expects the patch string to start with `18.6`; 1 of 4,523 backend tests fails. | [PR CI](https://github.com/NYGsatoshi/Coglatas/actions/runs/43857654928) |

## Prevention in this change

1. Temporarily exclude only Angular build tooling 22.2.5, 22.2.6 and 22.2.7.
   Do not disable later fixes or unrelated Angular runtime packages.
2. Keep both `stylelint` and `stylelint-config-standard-scss` on the
   compatible 17.x line until an independently tested joint migration.
3. Keep EF Core packages, the `dotnet-ef` tool, and
   `Npgsql.EntityFrameworkCore.PostgreSQL` on their reviewed 10.x major.
   This is a temporary compatibility hold: patch updates within 10.x remain
   discoverable. A future EF Core/Npgsql 11 migration must update all affected
   project manifests, NuGet locks and tool manifests together after verifying
   provider availability and compatibility.
4. Pin Renovate's `constraints.dotnet` to the exact SDK in `global.json`.
   `statusCheckWhen.artifactError=always` requests a green/red
   `renovate/artifacts` status instead of only reporting failures.
5. Ensure *both* jobs in npm Security Audit install the reviewed npm CLI rather
   than letting the second job use the npm bundled with setup-node.
6. Check the supported PostgreSQL **major** (18), not its patch number, in
   FileIntake HTTP acceptance. Continue logging the observed full version,
   and retain the separately reviewed service image digest policy.

The config regression script rejects deletion or relaxation of the guards,
including the SDK/artifact-status contract, and requires the same reviewed npm
CLI in both audit jobs.

## Provider artifact failure is not the same as a CI failure

A generated PR with a broken lockfile does **not** prove that the Mend-hosted
Renovate job itself crashed. The GitHub Actions failures above are confirmed;
the Mend job's artifact-generation log has not been retrieved here. This change
improves the constraints and failure reporting, but does not claim a provider-side
lockfile fix without observing a fresh bot-generated candidate.

To diagnose an actual artifact-generation failure:

1. Open [Mend Renovate job logs](https://developer.mend.io/github/NYGsatoshi/Coglatas).
   For the failing update branch search for `updateArtifacts`, `dotnet`,
   `packages.lock.json`, `allowedCommands`, `ERESOLVE`, `NU1004` and
   `artifactError`. Record the version of Renovate, exact tool/SDK, exit status,
   and branch HEAD. Preserve the raw failure evidence; do not infer success from
   a subsequent unrelated CI green.
2. Inspect `renovate/artifacts` on the bot-generated head. This check is emitted
   by Renovate; do **not** make it globally required for all PR authors, because
   non-Renovate PRs may not receive the check.
3. If NuGet files are changed, the same PR must contain all affected
   `packages.lock.json` files. Failure to update them is a **failed candidate**,
   not a reason to weaken `--locked-mode`.
4. Reproduce in an isolated writable checkout using the pinned SDK:
   `CI=false dotnet restore Coglatas.slnx --force-evaluate`.
   Inspect the generated lockfile graph/hashes, then require
   `CI=true dotnet restore Coglatas.slnx --locked-mode` and normal checks.
   Use a reviewed separate PR if bot-side artifact generation cannot produce a
   coherent candidate; do not silently commit to bot-owned branches.
5. For npm update candidates require `npm ci`, `npm ls`, and the existing
   lifecycle-script/lockfile policies. Repeating a broken peer tree or passing
   `--legacy-peer-deps` is not a repair.
6. After the main-targeted repair is merged, request bot-native rebase/retry
   from the Dependency Dashboard and inspect the **new** HEAD/CI results.
   Previously generated failed heads are not qualified by configuration changes.

Mend-hosted Renovate does not guarantee arbitrary `postUpgradeTasks`: the
host-side `allowedCommands` list is controlled separately. Do not add an
untested shell post-upgrade command or switch to self-hosted Renovate as an
implicit part of this PR. If a fresh 10.x NuGet update still omits its locks,
obtain the corresponding Mend log and resolve the provider's artifact generation
mechanism separately.

## Verification and merge criteria

- `ruby scripts/ci/test-dependency-update-policy.rb`, including its negative
  mutations, must pass on the current branch and hosted CI.
- `python3 -B scripts/ci/check-nuget-lock-policy.py` and
  `python3 -B scripts/ci/test-nuget-lock-policy.py` must pass.
- `npm` audit/install policy and the actual PostgreSQL 18.7 FileIntake test
  must pass if the service image is changed in a separate, reviewed update.
- Normal exact-HEAD required checks, security scanning and independent review
  remain mandatory. Do not claim that an old failing Renovate PR has passed
  merely because this separate repair PR is green.

Relevant upstream references:
- https://docs.renovatebot.com/configuration-options/#statuscheckwhen
- https://docs.renovatebot.com/configuration-options/#constraints
- https://docs.renovatebot.com/modules/manager/nuget/
- https://docs.renovatebot.com/usage/mend-hosted/hosted-apps-config/
