# ProjectIDE continuation audit — 2026-11-02

## Authority and retained boundaries

Remote state was fetched before implementation. All 21 open ProjectIDE PRs below
are Draft with auto-merge unset and no unresolved inline review threads. Review
submission/comments, current check rollups, changed files and recent run history
were inspected; resolved threads do not establish new-head approval. No branch
was merged, rebased, reset or force-pushed during the initial audit. Existing working trees were retained.
The original checkout is on an older clean Main and is not the development base.

Observed remote Main: `0c11a1e973740c6829ca263d6fa764d9c1bc62e1`;
integration: `158ab153551184cede99c85ce6476dd6e87b6767`;
SecurityTestImplimation: `847e1c7599ef3d15ee8f5ef2c79a9d7ca8c2e6e4`.
None is incorporated by this continuation. The existing independent Test series
retains its immutable `b18939f9104d52ae33e32a7f9b15a6e1b52892f1` ancestor.

This is an initial audit snapshot, not a claim that subsequent HEADs retain old
verification. Base SHAs below are fetched remote branch tips. GitHub cached
baseRefOid values sometimes describe earlier review snapshots; the audit retained
both values without changing any PR base/head relationship. Frozen candidate bases
are review boundaries, not accepted integration branches.

## Open PR register

| PR | Head branch / SHA | Current base branch / SHA | Scope | Dependency | Implementation | Review | CI | Blocker | Next action |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| [#1603](https://github.com/NYGsatoshi/Coglatas/pull/1603) | `feature/projectide-core-slice2-values-20261031` / `9ad2d5042895bb51605a0cfaff86fbf7d8e1ca49` | `projectide/integration-20261031` / `158ab153551184cede99c85ce6476dd6e87b6767` | add Slice 2a exact values and unit/time contracts (5 files) | projectide/integration-20261031 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1604](https://github.com/NYGsatoshi/Coglatas/pull/1604) | `test/projectide-core-slice2-values-20261031` / `51a057aa7a250953383c99e49dfcd97ab6e01145` | `SecurityTestImplimation` / `847e1c7599ef3d15ee8f5ef2c79a9d7ca8c2e6e4` | add isolated Slice 2 value and declaration fixtures (6 files) | SecurityTestImplimation | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | .NET build producer, build-test, security-scan, specification-contract, functional-fast | Unmerged Product dependency; no check waiver | Preserve independent tests and exact pair evidence |
| [#1605](https://github.com/NYGsatoshi/Coglatas/pull/1605) | `feature/projectide-core-slice2-declarations-20261031` / `90820e10d33ab8630e6ee2a06aab63c94a2671a8` | `feature/projectide-core-slice2-values-20261031` / `9ad2d5042895bb51605a0cfaff86fbf7d8e1ca49` | add Slice 2b Source-backed declarations and bounded predicates (7 files) | 1603 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1606](https://github.com/NYGsatoshi/Coglatas/pull/1606) | `feature/projectide-core-slice2-references-20261101` / `9a1439e6ebe6445a97120df1386acf289f1d9c9e` | `feature/projectide-core-slice2-declarations-20261031` / `90820e10d33ab8630e6ee2a06aab63c94a2671a8` | add Slice 2c pinned references and bounded value shapes (5 files) | 1605 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1607](https://github.com/NYGsatoshi/Coglatas/pull/1607) | `feature/projectide-core-slice2-unit-conversions-20261101` / `f240e34131b006ebd296844af84814c563f40365` | `feature/projectide-core-slice2-declarations-20261031` / `90820e10d33ab8630e6ee2a06aab63c94a2671a8` | add Slice 2d exact pinned unit conversions and arithmetic (4 files) | 1605 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1608](https://github.com/NYGsatoshi/Coglatas/pull/1608) | `feature/projectide-core-slice2-calendar-20261101` / `fd78f3261da6005098bbb47f1de32d26c42babc4` | `feature/projectide-core-slice2-declarations-20261031` / `90820e10d33ab8630e6ee2a06aab63c94a2671a8` | add Slice 2e pinned calendar and timezone evaluation (6 files) | 1605 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1609](https://github.com/NYGsatoshi/Coglatas/pull/1609) | `test/projectide-core-slice2-remainder-20261101` / `15ae613d8db1397eb105a6753798afb948ea7758` | `test/projectide-core-slice2-values-20261031` / `51a057aa7a250953383c99e49dfcd97ab6e01145` | verify Slice 2c 2d 2e contracts and fixed isolated candidates (10 files) | 1604 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | .NET build producer, build-test, security-scan, specification-contract, functional-fast | Unmerged Product dependency; no check waiver | Preserve independent tests and exact pair evidence |
| [#1611](https://github.com/NYGsatoshi/Coglatas/pull/1611) | `feature/projectide-core-slice3-relations-20261101` / `e4026f358f275e1acbfc513f8ac9c62af04b63a9` | `candidate/projectide-slice2-fixed-20261101` / `da112be55ea35794fe4416ba856ba35628a9fe53` | add typed relation descriptors and bounded graph validation (5 files) | candidate/projectide-slice2-fixed-20261101 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1612](https://github.com/NYGsatoshi/Coglatas/pull/1612) | `test/projectide-core-slice3-relations-20261101` / `3632e50742425ddfb07bd04101d431c79fce3b3a` | `test/projectide-core-slice2-remainder-20261101` / `15ae613d8db1397eb105a6753798afb948ea7758` | verify typed relation invariants and exact candidate inventories (6 files) | 1609 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | .NET build producer, build-test, security-scan, specification-contract, functional-fast | Unmerged Product dependency; no check waiver | Preserve independent tests and exact pair evidence |
| [#1614](https://github.com/NYGsatoshi/Coglatas/pull/1614) | `feature/projectide-core-slice4-envelopes-20261101` / `57318802712e4d98ab496c270f9047a7cb28c836` | `feature/projectide-core-slice3-relations-20261101` / `e4026f358f275e1acbfc513f8ac9c62af04b63a9` | bind immutable compiler manifests diagnostics and result axes (4 files) | 1611 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1615](https://github.com/NYGsatoshi/Coglatas/pull/1615) | `test/projectide-core-slice4-envelopes-20261101` / `d5ae33642fb772ce7690c97bfc64ffaa0e87657a` | `test/projectide-core-slice3-relations-20261101` / `3632e50742425ddfb07bd04101d431c79fce3b3a` | verify compiler identities coverage verdicts and freshness (6 files) | 1612 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | .NET build producer, build-test, security-scan, specification-contract, functional-fast | Unmerged Product dependency; no check waiver | Preserve independent tests and exact pair evidence |
| [#1617](https://github.com/NYGsatoshi/Coglatas/pull/1617) | `feature/projectide-core-slice5-binding-20261101` / `3718fe0bf82455945d99a5a4f9d427a790a31b75` | `feature/projectide-core-slice4-envelopes-20261101` / `57318802712e4d98ab496c270f9047a7cb28c836` | parse canonical buffers and bind partial semantic models (3 files) | 1614 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1618](https://github.com/NYGsatoshi/Coglatas/pull/1618) | `test/projectide-core-slice5-binding-20261101` / `839965c0f068ccf1f7e34beab9061fe4a24c931c` | `test/projectide-core-slice4-envelopes-20261101` / `d5ae33642fb772ce7690c97bfc64ffaa0e87657a` | verify partial binding scope snapshots and raw Draft recovery (4 files) | 1615 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | .NET build producer, build-test, security-scan, specification-contract, functional-fast | Unmerged Product dependency; no check waiver | Preserve independent tests and exact pair evidence |
| [#1620](https://github.com/NYGsatoshi/Coglatas/pull/1620) | `feature/projectide-core-slice6-ir-20261101` / `9a06f5be4fbc015a1c74a6cd76be392f0d0f73fe` | `feature/projectide-core-slice5-binding-20261101` / `3718fe0bf82455945d99a5a4f9d427a790a31b75` | validate semantic closure and derive mapped immutable IR (8 files) | 1617 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1621](https://github.com/NYGsatoshi/Coglatas/pull/1621) | `test/projectide-core-slice6-ir-20261101` / `cd1bb602c48c319bbef9aa70941595959a6531f1` | `candidate/projectide-slice5-tests-fixed-20261101` / `c5b6ffc8b92154423c41c28dab66a4bc2c4a50c2` | verify IR maps identities graph closure units and provenance (7 files) | candidate/projectide-slice5-tests-fixed-20261101 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | .NET build producer, build-test, security-scan, specification-contract, functional-fast | Unmerged Product dependency; no check waiver | Preserve independent tests and exact pair evidence |
| [#1624](https://github.com/NYGsatoshi/Coglatas/pull/1624) | `feature/projectide-core-slice7-analysis-20261101` / `5ef8b9caa4cbb0228a48b5c13b4f74bb07837547` | `feature/projectide-core-slice6-ir-20261101` / `9a06f5be4fbc015a1c74a6cd76be392f0d0f73fe` | execute bounded initial analysis with full compile evidence (9 files) | 1620 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1625](https://github.com/NYGsatoshi/Coglatas/pull/1625) | `test/projectide-core-slice7-analysis-20261101` / `24c8386b37267ac730363de1f943c606de0d35fe` | `candidate/projectide-slice6-tests-fixed-20261101` / `939e8d51a4318b8cee79ae7fae533230123d6be8` | verify rule truth evidence NotRun and exhaustion boundaries (7 files) | candidate/projectide-slice6-tests-fixed-20261101 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | .NET build producer, build-test, security-scan, specification-contract, functional-fast | Unmerged Product dependency; no check waiver | Preserve independent tests and exact pair evidence |
| [#1628](https://github.com/NYGsatoshi/Coglatas/pull/1628) | `feature/projectide-core-slice8-hosts-20261101` / `ddb456dbf2eca1b005a8aac5ffb1ca8861b73b5d` | `feature/projectide-core-slice7-analysis-20261101` / `5ef8b9caa4cbb0228a48b5c13b4f74bb07837547` | add Slice 8 compiler host contracts and exact parity binding (6 files) | 1624 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | No failing ordinary checks observed; skips/neutral remain | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1629](https://github.com/NYGsatoshi/Coglatas/pull/1629) | `test/projectide-core-slice8-hosts-20261101` / `5bd53a614b9cfe27de53cfa2b7901a18b95aa36d` | `candidate/projectide-slice7-tests-fixed-20261101` / `92b6b8fde6b872c73adab8861ed109920dd108d4` | verify Slice 8 host parity and immutable manifest binding (8 files) | candidate/projectide-slice7-tests-fixed-20261101 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | .NET build producer, build-test, security-scan, specification-contract, functional-fast | Unmerged Product dependency; no check waiver | Preserve independent tests and exact pair evidence |
| [#1637](https://github.com/NYGsatoshi/Coglatas/pull/1637) | `feature/projectide-web-workbench-20261101` / `9482b474d752d0ee72eafffd2a4deb59fb922432` | `feature/projectide-core-slice8-hosts-20261101` / `ddb456dbf2eca1b005a8aac5ffb1ca8861b73b5d` | add Angular read-only Workbench preview with Problems and source navigation (21 files) | 1628 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | frontend-test | Review/acceptance pending | Validate contracts; retain explicit unsupported scope |
| [#1638](https://github.com/NYGsatoshi/Coglatas/pull/1638) | `test/projectide-web-workbench-20261101` / `cdd0dfa9c21de15edc95f281a85026ea568c97f1` | `test/projectide-core-slice8-hosts-20261101` / `5bd53a614b9cfe27de53cfa2b7901a18b95aa36d` | verify Angular Workbench contracts components and browser journey (12 files) | 1629 | Bounded candidate; full Issue incomplete | No unresolved threads; approval pending | .NET build producer, build-test, security-scan, frontend-test, specification-contract, functional-fast | Unmerged Product dependency; no check waiver | Preserve independent tests and exact pair evidence |

## Dependencies, status and specification alignment

Products progress from #1603/#1605, through the bounded reference/unit/calendar
siblings #1606/#1607/#1608 and fixed Slice 2 candidate, then #1611 → #1614 → #1617
→ #1620 → #1624 → #1628 → #1637. Tests retain their separate chain #1604 → #1609
→ #1612 → #1615 → #1618 → #1621 → #1625 → #1629 → #1638, including existing fixed
candidate review bases. No Test implementation is absorbed into Product.

Merged Core Slice 1 #1063 and Security Foundation #1134/#1137/#1140/#1142/#1153
are historical foundations, not work to recreate. Closed unrelated performance
proposals returned by full-text search are not ProjectIDE implementation slices.
Earlier wording calling specification PR Coglatas-Spec#300 Draft is historical;
its already-merged status is recorded in the Workbench audit. Preserve #888's
IDE-START addendum over older broad implementation holds. Pure Core and maintained
Angular synthetic work may proceed; #1144 native/operational gates and #1128
SUSPENDED / NOT_EVALUATED remain unchanged.

Current Issue bodies and relevant handoffs were obtained for #888/#798/#905,
#782/#793/#794/#795, #766/#814/#1030/#908 and gate trackers. Remaining full Core
semantics include value-reference binding, Requirement evidence and shared Resource
materialization. Keep Unknown, Unsupported, NotRun and exhausted processing explicit.
Runtime API authorization, durable Compiler jobs and native UI acceptance remain
separate unimplemented/gated work; no synthetic fixture supplies that authority.

## Selected work

1. Strengthen the existing Web preview's view-level contract checks, direct
   Source Navigation binding and reload focus lifecycle. Independent Test additions
   reproduce 17 failures against Product `9482b474d752d0ee72eafffd2a4deb59fb922432`
   with Test `f623389bd9aa91933857d6436be34d0194a76e2e`: 90 retained PASS, 17 FAIL,
   zero pending tests. Retain the failing JSON separately from new-head evidence.
2. Continue Core host freshness with an additive exact-request assessment that
   includes the materializations actually executed. The existing manifest-only
   assessment remains explicitly limited to its supplied manifest and generation.
3. Run fresh isolated composition for each pair, with exact SHAs, trees, command
   exit codes, inventories and artifact hashes. Ordinary PR checks remain separate.

The Web fix rejects duplicate tracking/semantic identities, Source-external
locations and structurally impossible Core evidence. Both bundle reads and direct
resolver calls check exact result/input/SourceMap/context binding. Reload clears
panel/filter state and invalidates deferred focus. These are bounded synthetic
view checks, not remote binary attestation or a production authorization adapter.

## Holds and next executable work

The initial audit treated the ordinary Web Product's five-route static assertion
as independent-Test-only maintenance. A fresh Product-only run reproduced its real
failure (11 PASS / 1 FAIL) despite the correct route registration. The continuation
instruction explicitly permits necessary minimal Product internal verification,
so this existing test now retains every assertion with the six-route addendum and
44-route current total. Its fixed 38-route snapshots remain unchanged. Only these
three exact lines are permitted by the composer; independent component/owner
negative controls stay on Test. No Test commit is blindly absorbed. Standalone
Test backend CI still lacks the unmerged Product types; composition does not turn
that failure into PASS.

CodeRabbit CLI 0.9.1 is installed but initially unauthenticated. Browser login was
initiated; a completed independent new-head review is not claimed until an actual
review succeeds. Existing CodeRabbit threads are resolved, and full review/owner
acceptance remains pending. Source changes and tests continue within the owner's
explicit authorization while that separate external review prerequisite is pending.

Owner decisions remain HOLD for native/operational gate activation, an approved
real read-only artifact endpoint/current authority contract, trusted-producer
acceptance, integration and release. No Main/integration merge, Ready/Close,
auto-merge, production action or security/check relaxation is authorized here.
Full shared-client completion is not claimed.

## Exact dependency continuation

Core Product `1218495bdd4e1280c968b0e6d2067c921c64ab31` and independent Core Test
`8348083cc04e9bd85147b32bb1805e94aa14fd3a` add exact-request freshness and seven
independent checks. Their local pair passes 714 Core and 12 architecture tests,
with zero warnings/errors/failed/skipped. These are separate from the old 707-case
receipts. Current local Python is 3.12.14; hosted Python remains pinned at 3.12.12.

Only these pinned Product-to-Product and Test-to-Test dependencies are merged into
the existing Web development heads. No Main/integration/other-purpose branch or
PR is merged. Web fixture producer metadata is regenerated from the new Core pin;
its semantic cases retain their actual Core-produced values.

The Web composer records both dependency merge commits, accepts only their exact
pinned second parent and checks each merge tree against Git's automatic merge.
Additional resolution edits, unknown parents and later ownership violations are
rejected. It composes independent Core Test commits followed by Web Test commits,
then requires exact ProjectIDE-owned Test file equality and unchanged Product
runtime source. Product and Test retain different pre-existing non-ProjectIDE
baselines; those differences are inventoried rather than absorbed into Product.
Shared runner/inventory maintenance uses the validated independent commit patches.
The first dependency composition correctly stopped before verification when an
overbroad whole-tests-directory equality check encountered these inherited
differences. Its failure log is retained. The corrected comparison retains all
ProjectIDE-owned files and adds a negative control for their exact equality;
it neither imports unrelated tests nor removes a required check.
The Web verifier checks both the recorded commit and tree plus a clean tracked
checkout before and after execution. Real temporary-Git controls cover dirty and
staged checkouts, same-tree commit substitution, receipt tampering and dependency
merge substitution. These improve candidate evidence; Test-head-controlled scripts
still do not establish trusted-producer or independent acceptance authority.

The first Web fix pair completed its selected full local inventory before this
dependency/script continuation. Its receipt is retained as historical evidence;
the final updated heads must run fresh verification. Skip/exclusion policies are
unchanged. The original ordinary Product route-assertion failure remains historical
and the corrected Product must pass its own new ordinary CI. Standalone Test
dependency failures remain visible and separate from supplemental evidence.
