# Compatible npm dependency qualification — 2026-10-29

This continues existing non-held PR #1441 and the ANG22-06 exact-version
compatibility contract. Provider-generated manifests/lockfiles are unchanged by
the maintainer repair. No lifecycle-script approval, security override, assertion,
retry, timeout or required check is relaxed.

At provider HEAD8fdd9ea49d704a277ec7199a4435d3e861c42230 (parent Mainada89790),
the selected existing version guards fail exactly on Lucide1.63.0 versus1.61.0
and CDK22.2.5 versus22.2.4. Manifest/lock inspection identifies the remaining
stale constants behind those first assertions. The original3ea22b79 peer-tree
failure and earlier review remain historical; later source is not credited with
those earlier qualifications.

## Candidate exact cohort

| Dependency | Previous guard | Candidate |
| --- | --- | --- |
| Lucide Angular | 1.61.0 | 1.63.0 |
| Syncfusion Grids / Inputs | 35.1.41 | 35.1.42 |
| Syncfusion Popups | 35.1.39 | 35.1.42 |
| Angular Storybook / Storybook | 10.6.3 | 10.6.5 |
| Angular CDK / CLI / sibling DevKit Core | 22.2.4 | 22.2.5 |
| Sibling DevKit Architect | 0.2202.4 | 0.2202.5 |
| Angular build / DevKit build-angular | 22.2.4 | 22.2.4 |

The known-invalid22.2.5 build patch stays excluded by #1490. Core/compiler22.2.3,
Gantt34.2.8, jsdom30.1.3 and Vitest5.0.5 are unchanged. Exact manifest, lock-root,
installed-node and SHA-512-integrity assertions remain. Nested Syncfusion base
35.1.41 installers remain observable and denied by the package-wide false
fallback; only already-approved34.2.8 has its existing version-specific approval.
Compodoc2.x, Storybook builder/zone.js, license/theme sanitation and Angular's
Vitest contract remain mandatory.

## Qualification boundary

After the constant repair, a local run observed seven guards PASS and the
installed-asset guard FAIL because vendor node_modules assets are not installed
in this disk-constrained checkout. That environment failure is retained; it is
not a full local PASS or evidence of a vendor defect. The installed asset,
license and theme gates must pass with actual dependencies in hosted CI; local
checks are not full architecture or runtime acceptance. Record exact
final PR HEAD, tested merge/tree, full architecture/Angular/browser/Functional
results and independent review before normal merge, then qualify resulting Main.
Do not use old source or a retry as first-attempt success. #615 remains the
non-authorizing release checkpoint; STUDENT_COUNCIL_PILOT = RELEASE_NO_GO.
