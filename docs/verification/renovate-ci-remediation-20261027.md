# Renovate PR CI triage and coordinated remediation — 2026-10-27

## Scope and evidence

The following diagnoses use the failed GitHub Actions jobs on the current Renovate
candidate heads. This PR does **not** edit or push to any `renovate/*` branch,
approve a newer GitHub Action SHA, turn off npm lifecycle-script enforcement,
or relax a required check.

| Renovate PR | Failing check and observed cause | Disposition |
| --- | --- | --- |
| [#1441](https://github.com/NYGsatoshi/Coglatas/pull/1441) | Node toolchain rejects `npm@11.21.1` vs the independently pinned `11.17.0`; frontend `npm ci` reports missing `qs@6.17.0` and transitive packages from its lockfile. | Exclude standalone npm CLI updates; request a bot-native lockfile regeneration/rebase, then recheck the dependency tree. |
| [#1439](https://github.com/NYGsatoshi/Coglatas/pull/1439) | Removal of Syncfusion base 34.2.8 leaves a previously approved `allowScripts` **true** entry stale. | Hold the Gantt 35 major until an independent coordinated migration removes the obsolete approval and reviews install-script behavior. |
| [#1438](https://github.com/NYGsatoshi/Coglatas/pull/1438) | Manifest-only npm 12 proposal violates the npm 11.17.0 repository toolchain contract (Docker, workflows, CI script). | Disable Renovate proposals **for the npm CLI dependency only**; migrate intentionally with full toolchain tests. |
| [#1437](https://github.com/NYGsatoshi/Coglatas/pull/1437) | Action pin policy rejects new setup-python SHA; actionlint reports the proposed `ubuntu-26.04` label as unknown. | Retain existing gates; review upstream Action/tag, hosted runner availability, and linter support in a separate change. |
| [#1428](https://github.com/NYGsatoshi/Coglatas/pull/1428) | Protected Action pins no longer match `governance/github-actions-allowlist.json`. | Do not automatically approve bot-proposed SHA changes; require separate reviewed allowlist migration. |

## Changes in this Main-targeted repair

1. Exclude the `npm` CLI *dependency* from routine Renovate npm manager proposals.
   npm library updates and npm audit/vulnerability scanning remain enabled.
   `packageManager` is a repository-wide pinned toolchain requiring coordinated
   Docker, runtime and CI updates; independent manifest-only PRs cannot pass.
2. Keep Gantt within major 34 until a manual migration reviews the v35 script
   boundary. Patches within v34 remain discoverable. Other Syncfusion packages
   are **not** globally frozen.
3. In `frontend/package.json`, retain the reviewed **exact-version** true approval
   for `@syncfusion/ej2-angular-base@34.2.8`, but replace the v35.1.41-specific
   `false` entry with an unversioned `@syncfusion/ej2-angular-base: false`.
   The lockfile verifier resolves exact-version decisions first. This means
   future unreviewed Syncfusion versions have install scripts *denied*, not
   silently approved. Updating a dependency to a new base version no longer
   requires a new version-specific denial entry for each patch. Any removed
   positively approved version remains subject to the stale-approval failure.
4. Keep the existing `governance/github-actions-allowlist.json` Action SHAs
   immutable under Renovate. Protected Action upgrades instead require a human
   review that updates the actual workflow uses and the SHA/version allowlist in
   the **same** migration. Unprotected Actions remain eligible for grouped
   dashboard-approved updates. This prevents the repeatedly red #1428/#1437
   protected-Action PRs without bypassing their security check.
5. Hold the Actions-managed `ubuntu` runner on reviewed 24.04 until the hosted
   runner and actionlint versions have both been qualified for 26.04; retain
   `python` setup bootstrap on reviewed 3.12 and `dotnet-sdk` bootstrap on the
   exact `global.json` SDK. These are coordinated toolchain updates, not
   independent Action-manifest changes.
6. Align the Angular 22 compatibility tests with the exact-v34-approval plus
   package-wide-v35-denial rule. Retain version, integrity and install-script
   observations, and explicitly assert no v35 exact approval has appeared.
7. Extend policy mutation tests so protected Action inventory, runner/runtime
   boundaries, npm CLI/Gantt updates, and install-script decisions cannot be
   silently loosened.

## npm lockfile remediation after Renovate regeneration

Renovate's npm manager documents the `npmInstallTwice` post-update option as
a workaround for incomplete npm-generated package-lock trees that fail `npm ci`.
For grouped npm minor/patch updates, run artifact generation twice with
the same exact `npm@11.17.0` as the repository's reviewed toolchain.
This is a lockfile **generation** fix, not permission to weaken `npm ci` or
alter the security audit's approval requirements.

This is specifically motivated by #1441, whose existing lockfile reports
`Missing: qs@6.17.0` and additional missing transitive nodes. Existing bot
commits remain invalid until Renovate regenerates them. Require strict
`npm ci` on the new bot HEAD and review any unexpected lockfile churn.

## Follow-up conditions

- After this repair merges, request Renovate **rebase/retry**, not manual branch
  commits. Check that #1438 and #1439 are removed or withheld by the provider;
  do not assume the bot will immediately close pre-existing PRs.
- For #1441, **do not** bypass `npm ci` or replace `npm ci` with `npm install`
  in CI. Its missing lockfile packages are an additional issue not repaired by
  the unversioned **false** install-script decision. Require a provider-generated
  coherent lockfile and the normal install/tree policy to pass.
- For #1428 and #1437, all allowlisted protected Actions now require a deliberate
  manual SHA+workflow migration; do not auto-trust any bot-pinned SHA. Runner
  majors, Python bootstrap and SDK bootstrap are likewise held to current
  tested versions. GitHub Actions runner labels and the static analyzer must
  both recognize a proposed new runner before migrating. Existing Renovate PR
  heads will not become green merely from these config changes; let Renovate
  regenerate or close superseded candidates on its next run.
- Continue to require exact-head CI, the protected Action guard, npm security
  policy, and publication readiness. None of these PRs receives release
  approval from this change.

## Verification

```sh
ruby scripts/ci/test-dependency-update-policy.rb
node scripts/ci/verify-npm-lockfile.mjs frontend
python3 scripts/ci/test_github_action_pins.py
python3 scripts/ci/github_action_pins.py
```

The above commands are required validation targets, **not** claims of completed
runs. Re-evaluate the five Renovate PRs from their new heads after the next
provider update.
