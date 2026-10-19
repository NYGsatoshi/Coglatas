# ANG22-06 — reviewed third-party pin refresh (PR #1259)

## Exact dependency updates

The Dependabot grouped update moves six dependencies covered by the existing ANG22-06
regression contract. Previous immutable expected versions caused `frontend-test`
to reject the update, even though compilation and end-to-end UI regression checks
for the candidate had passed. This change revises **exact expected versions**;
it does not turn off or relax the guard.

| Package | Previous | Candidate |
| --- | --- | --- |
| `@lucide/angular` | `1.53.0` | `1.57.0` |
| `@syncfusion/ej2-angular-grids` | `35.1.39` | `35.1.40` |
| `@storybook/angular` | `10.6.1` | `10.6.2` |
| `storybook` | `10.6.1` | `10.6.2` |
| `jsdom` | `30.1.2` | `30.1.3` |
| `vitest` | `5.0.3` | `5.0.5` |

The ANG22-06 test now verifies all guarded production/development dependencies
against three independent declarations: `frontend/package.json`, lockfile root
dependencies, and the installed-package lockfile record, requiring SHA-512 integrity
metadata. The unchanged ANG22-06 checks continue to require Compodoc 2.x, the
approved Storybook builder, the Syncfusion license/theme sanitation path and the
Angular 22 Vitest runner configuration.

## Candidate evidence and boundary

At candidate `c4fdc0d53be05698ad5ff36ab3767b8a5883f578`, PR CI
[39206393213](https://github.com/NYGsatoshi/Coglatas/actions/runs/39206393213)
observed Angular Vitest **1,388 passed**, Playwright **207 passed** with
13 conditional skips, Functional Fast PASS, production frontend build PASS and
security-scan PASS. Its remaining frontend failure was the stale ANG22-06
expected `@lucide/angular` 1.53.0 vs the candidate's reviewed 1.57.0, with
the other five stale expected versions identified by independent manifest
and lockfile inspection. This report is candidate-specific, not evidence
of full interoperability for every unexecuted feature.

All dependency updates still require success of fresh CI on the exact new HEAD.
Do not claim verification from earlier CI, bypass test failures or merge the
PR automatically.
