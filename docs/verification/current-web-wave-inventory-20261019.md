# Current Web inventory addendum and architecture guard

Date: 2026-10-19 JST. Implementation base:
`a5aef3f088e77f9d6e57affa7f854d81facef2fc`. This is observational maintenance
for the current Angular Web wave, with no Avalonia eligibility, PNL, target-owner,
SEC-ARCH Mapping, platform, cutover or backend-contract approval.

## Historical authority remains unchanged

The September source pin `4e6a10903a5a472ca89f833aba993a29e1b2cf73`, its 38
routes and its statement that NgRx dependencies had no production imports remain
true historical facts. The v5.8.1 target decisions and execution owners remain
under the authorities documented in
`docs/migration/avalonia/angular-frontend-inventory.md`. No historical record was
edited or reinterpreted as approval for current Web additions.

| Preserved historical JSON | Unchanged Git blob |
| --- | --- |
| `angular-frontend-inventory.json` | `844159c28c89a7becf60dae11f89e1e1822f98e7` |
| `angular-feature-freeze-matrix.json` | `2f1e2f3b1e2081ff619fac13e97d375b9fac616e` |
| `angular-target-surface-map-v5.8.1.json` | `a159e6f05bef11c53b7ca6e5c5f0676be179721b` |
| `angular-persisted-state-inventory.json` | `aa087d5a7637346397b6c98027f3d9097b6ec285` |

The separate [current Web manifest](current-web-wave-inventory-20261019.json)
records `/dm`, `/teams` and `/drafts`, their current component/source bindings,
registered direct state owners, and the two Files-page bindings to
`LocalFileStarsService`. It adds no target PNL or Avalonia execution owner.

Current `LocalDraftStore` uses the installed NgRx `ComponentStore`; its editor
and synthetic Links checkpoint is memory only and has no server synchronization.
Starred persists only versioned opaque File IDs in a Tenant/user/Workspace-scoped
local preference; restored IDs require authoritative File reads. Both families
remain `unmapped-web-only`, without authorization authority or Desktop cutover
disposition. This supplements the historical persisted-state inventory rather
than changing its eleven historical families.

## Guard and negative controls

The source guard still checks exact agreement among all three historical
38-route records. Current production routes must equal that historical set plus
exactly the three explicit Web additions. The guard binds current routes to
their component symbol, lazy import module, source file, directory discriminator
and registered direct state owners. Files owner extensions, adapter source
bindings, local-state classification and the current production NgRx import are
checked separately. Production imports are inspected across all non-test source
files, including modules without class declarations.

Unknown, missing, duplicate or mutually deleted route/addendum entries fail.
Controls also reject a missing addendum, synchronized route-owner/registry
removal, missing Files extensions, wrong component/module/directory or owner
imports, adapter-source drift, local-storage/payload/authority promotion, target
owner injection and a missing NgRx fact. All existing negative controls,
including lexical/symlink containment, remain in place. No test was skipped and
no assertion, inspection threshold, timeout or workflow was relaxed.

## Retained execution evidence

- Product PR #1252 head `e71650c6b8a225e6aaeb828dba1c10495f53d3ad`,
  [run 39143086624, frontend job 117722081022](https://github.com/NYGsatoshi/Coglatas/actions/runs/39143086624/job/117722081022):
  browser **191 PASS, 0 FAIL, 13 existing SKIP**, 132 Angular test files passed,
  architecture **34 PASS, 2 FAIL** because current `/dm` and `/teams` were
  incorrectly compared directly with the historical 38-route set.
- Product PR #1251 [run 39142527733](https://github.com/NYGsatoshi/Coglatas/actions/runs/39142527733)
  independently retained the same strict inventory mismatch for `/drafts`.
  Its separate old mobile PNG failure is not repaired by this inventory change.
- Initial focused local inventory suite: **45 PASS, 1 FAIL, 0 SKIP** of 46.
  The sole failure was Windows `EPERM` while creating the existing symlink
  fixture; it does not count as a passed containment assertion.
- Final full architecture selection: **58 PASS, 1 FAIL, 0 SKIP** of 59. All
  added controls and historical checks passed; the same unchanged symlink fixture
  failed with Windows `EPERM`. Overall command result remains **FAIL /
  BLOCKED_BY_ENVIRONMENT**, requiring hosted Linux execution for that control.
- Source guard: **PASS**, `AV-MIG source inventory verified: 41 routes`.
  Existing source architecture check and `git diff --check`: **PASS**.
- Pinned Gitleaks 8.24.3 pre-commit scan of all five changed files: **PASS**,
  no leaks found. No scanner exception was added.
- Full Angular/browser suites were **NOT_EXECUTED** for this guard-only patch;
  exact-head hosted required CI remains necessary before a merge decision.

Commands from `frontend/`:

```powershell
node scripts/check-av-mig-source.mjs
node scripts/check-architecture.mjs
node --test scripts/av-mig-inventory.node-test.mjs scripts/av-mig-source.node-test.mjs
node --test scripts/check-architecture.node-test.mjs scripts/sanitize-syncfusion-theme-css.node-test.mjs scripts/angular22-third-party-compatibility.node-test.mjs scripts/av-mig-inventory.node-test.mjs scripts/av-mig-source.node-test.mjs
```

The full local architecture output is retained outside Git at
`%TEMP%/coglatas-webwave-20261019/current-web-inventory-architecture.log`.

## Unchanged compiled product boundary

The `frontend/src` tree remains exactly
`e6c28e8a705a08482f7d2155ee8066df2471c391`. Build configuration, package
manifest, lockfile and UI screenshots are unchanged from the implementation
base. The whole `frontend` tree changes from
`12bc68fbce2707c654faa20a7fb09781a355594e` to
`f1dd6bbc090f4f5ea34e3dc1c1f56ce95205bdb8` because its guard scripts and
their tests changed; it must not be described as an unchanged whole frontend
tree. Existing compiled-source/screenshot evidence remains bounded to the same
product source, while hosted CI must verify the new guard bytes independently.
