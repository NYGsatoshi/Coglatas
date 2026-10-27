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
4. Extend Renovate policy mutation tests so npm CLI auto-updates, Gantt major
   auto-updates and an unreviewed Syncfusion install-script approval cannot be
   silently reintroduced.

## Follow-up conditions

- After this repair merges, request Renovate **rebase/retry**, not manual branch
  commits. Check that #1438 and #1439 are removed or withheld by the provider;
  do not assume the bot will immediately close pre-existing PRs.
- For #1441, **do not** bypass `npm ci` or replace `npm ci` with `npm install`
  in CI. Its missing lockfile packages are an additional issue not repaired by
  the unversioned **false** install-script decision. Require a provider-generated
  coherent lockfile and the normal install/tree policy to pass.
- For #1428 and #1437, update reviewed Action SHA/version metadata only after
  independent upstream verification. GitHub Actions runner labels must actually
  be supported before selecting a new hosted runner release.
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
