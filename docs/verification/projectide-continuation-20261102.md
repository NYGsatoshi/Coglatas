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
three exact inventory lines are permitted by the composer; independent component/owner
negative controls stay on Test. No Test commit is blindly absorbed. Standalone
Test backend CI still lacks the unmerged Product types; composition does not turn
that failure into PASS.

The Product-only full Node inventory then exposed an existing Windows file-symlink
privilege limitation (67 PASS / 1 FAIL, EPERM). Its existing realpath-denial test
now uses a directory junction/Unix directory symlink with the same lexical and
realpath denial assertions, without elevation or skips. This exact bounded
portability delta is also checked against the pinned baseline; the two independent
ProjectIDE component/owner controls remain Test-only. Both failed initial logs are
retained separately from the new Product-only run.

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

## Continuation checkpoint — 2026-11-03

The inherited register above remains historical. A fresh GitHub REST/GraphQL audit
found the same 21 open ProjectIDE PRs, all Draft and auto-merge unset. Their initial
HEADs match the supplied 2026-11-02 snapshot. Both review-thread pagination and
per-thread comment pagination were exhausted. Only #1628 has new unresolved
inline threads (Cancellation, Expected Kind); its latest CodeRabbit review also
contains the Unknown-basis observation outside the diff. Spec #300 is MERGED
(`16066f182beef0cd7ce57aa23849922f634d82c7`), overriding stale Draft text in Issues.
The current #888/#798/#905/#782/#793-#795/#906/#1144/#1128 bodies and normative
2026-10-31 owner decision were fetched. No earlier Core chain is recreated.

The supplied RiderProjects checkout is absent on this host. Existing clean Product
and independent Test worktrees under `.codex/worktrees` are reused. A historical
Slice 3 reproduction has a pre-existing modified TypedRelationTests file; it is
preserved. Existing Python caches are not source changes. Main and integration
worktrees/refs are not development targets. The current SecurityTestImplimation
remote tip differs from the historical register; the fixed Test ancestor and all
existing review bases remain unchanged.

Core Product `e7a0b7b9da9a9f761b9c018c62b72e0fe7e901a4` and independent Test
`f057f5e86fd28c6ca00875f20f8e30a49fb82f8b` correct all three observations.
Before-fix combined `00a86e1d993d723f42bf4e0ce49d0c50315e324c`, tree
`2329ebb7db13cb62ea05c93583a1d1ff9a80ddd1`: 8 FAIL / 3 PASS / 0 skipped.
After-fix combined `bbcb59b7825722a52b1f250126e3a709af4d55c4`, tree
`a85b3a67eb902ceb269d5a7e1e5a8f3b49718397`: Windows SDK 10.0.403 Release solution
build with warnings as errors, 0 warnings/errors; Core 725/725, Architecture 12/12,
0 failed/skipped. Four isolated composition controls pass. Existing inventories,
late-response cancellation controls and all assertion responsibilities are retained.
Core TRX SHA-256: `458916dc78c1bb39b7746a012856be7a245637b7969743f075156d03479596c1`.
Actual new-head hosted conclusions are recorded separately in PRs; these local
results do not imply hosted, reviewer, security or client acceptance.

Web dependencies advance through automatic merge trees with exact Core parents,
Product-to-Product and Test-to-Test only. The composer explicitly enumerates both
historical and current Core dependency parents and rejects all others, preserving
existing history and fixed review bases. Fixture producer metadata is regenerated
from corrected Core; synthetic cases retain their actual Core-produced values.
All 28 composition/verification negative controls pass. New full Web evidence is
required; the earlier 724/263/70/42 result set is not reused for the changed heads.

Ordinary CI classification at inherited heads:

| PR / run | Producer or consumer | Observed failure classification |
| --- | --- | --- |
| #1629 / 41371615115 | .NET build producer | CS0234/CS0246 for absent unmerged Product Core/types |
| #1629 / same run | build-test, security-scan, Functional, specification-contract | Producer-dependent failed/skipped execution and missing evidence, not PASS |
| #1638 / 41379288123 | .NET build producer | Same independent Test/Product dependency boundary |
| #1638 / same run | frontend-test | 34 browser failures: Preview Product route absent from standalone Test build; retries also fail |
| #1638 / same run | dependent gates | Missing producer/runtime/Advisory evidence; retained as failures |

The repository uses rulesets rather than legacy branch protection (that endpoint
returns 404). Active owner-only Main ruleset 24643016 requires build-test,
frontend-test, security-scan, publication-readiness, functional-fast and
performance-fast, strict status checking, thread resolution, no deletion and no
non-fast-forward update. No ruleset, workflow gate, exclusion or threshold is
relaxed. Supplemental evidence is a separate candidate evaluation axis.

Next bounded implementation candidate: Source-backed semantic inspection, with
immutable input/Source identity, deterministic symbols/types/reference status,
Angular Inspector consumption and a pure .NET synthetic Workbench adapter for the
Windows lane. Actual Avalonia startup, native E2E and real API/authorization remain
HOLD under #1144. This is independent of complete Requirement evidence/resource
materialization, which remain unimplemented. Shared-client completion is not claimed.
#1128 remains SUSPENDED / NOT_EVALUATED.

### Exact parent reconciliation and new verification

GitHub marked #1628 CONFLICTING after the first correction; ordinary pull_request
CI did not start. Its existing Slice 7 branch has additional approved Product
ancestors, rather than the earlier single-parent review snapshot. Exact parent
`5ef8b9caa4cbb0228a48b5c13b4f74bb07837547` is now incorporated in Core merge
`610f9911f9ce31739188437972f7eeb018f611da`, whose first parent is
`e7a0b7b9da9a9f761b9c018c62b72e0fe7e901a4`. The only conflicting file was
CompilerSemanticValidation.cs; its verified correction was retained verbatim.
The merge tree equals its first-parent tree. This reconciles real dependency
ancestry; no earlier same-tree receipt is substituted for the new Commit.

Independent Test `9e969183f5b311789b0197d0b6aac12a88206e7c` pins that exact merge
Commit, both parents and its unchanged resolution tree; unknown merges, parents
and resolution edits are rejected. Five composition controls pass. A new exact
Windows combined candidate was built and tested: Release warnings-as-errors,
Core 725/725, Architecture 12/12, zero warnings/errors/failures/skips.

The preceding Web candidate Product `1b0d6e4d8db36d7b5a8196515aa72e94bec5b829`,
Test `7efcc42117ca99110ba28aa0b06848af4c060841`, combined
`65a9dc38d2892cb880b2527a7a18026b9f34e556`, tree
`9754333d95323725ab33f68e9eb08aef7c074798`, passed Windows full verification:
Core plus fixture 735/735, Architecture 12/12, Vitest 263/263, Node 70/70,
Playwright Desktop/Mobile 42/42, controls 28/28, no failures/skips. Local Node is
24.22.1 (hosted remains 24.22.2), Python 3.12.14 (hosted 3.12.12), SDK 10.0.403.
Installed dependency directories were reused only after both exact lockfile hashes
matched; no lockfile/dependency changes were made. This evidence is retained for
those exact pins; subsequent metadata/ancestry updates require their own receipt.

## Source-backed inspection continuation — 2026-11-03

One bounded Draft pair follows the existing Web lanes; the inherited register is
not replaced. [Product #1653](https://github.com/NYGsatoshi/Coglatas/pull/1653),
`feature/projectide-workbench-inspection-20261103`, bases on #1637 at
`0a79e05a6533a7e4468bb7b74f47aeceb04ae119`. [Independent Test #1654](https://github.com/NYGsatoshi/Coglatas/pull/1654),
`test/projectide-web-windows-inspection-20261103`, bases on #1638 at
`5f2825e1d88f307efddc5edac16579ec220cbd4b`. Both remain Draft/open with no
integration authority. Exact new-head receipts and check state are appended to PRs.

The new pure Core projection exposes input-bound local symbols, actual port types
and distinct reference resolution states, including partial models before IR.
Angular Inspector consumes the optional exported projection and validates exact
Source/type/identity binding. A separate pure .NET synthetic Windows ViewModel
prepares Source/Problems/Inspector/navigation with monotonic generations, exact
materializations and boundary cleanup. It contains no Avalonia package/window or
real API/data loader. See [implementation/limits](projectide-source-inspection.md).
Windows Native UI, keyboard/IME and actual Avalonia acceptance remain NOT_RUN/HOLD;
Angular success is not Windows E2E. Complete value-reference/evidence materialization
and production API/current authority remain incomplete.

The additional composer permits only the exact existing Web Product/Test parents
and the one new Core inspection file. An initial composition failed because a
stacked Test parent had advanced on its own line. The correction validates and
replays its missing independent deltas exactly once, with an ownership-negative
control; no duplicate patch or Product source substitution is permitted. Test-owned
project-reference files are also compared exactly. The failed candidate is retained.
The initial full build then failed on two xUnit2031 checks; the next run passed all
754 .NET ProjectIDE/fixture/ViewModel tests but exposed two strongly typed Entity vs
Relation ID errors in the new UI Test. Both Test issues are fixed without rule,
assertion or type relaxation; failed receipts remain historical.

Reconciled Core Product `610f9911` / Test `9e969183` now qualifies on Windows and
Ubuntu at 725 Core + 12 Architecture cases; all input/combined pins, trees, inventory
names/outcomes and producer hashes match. Hosted run **41499617923**, attempt 1,
artifact **13167922631**, ZIP SHA-256
`be661c775e038b3e6bc7b9e14a552ce1fdf3c2ffbabbe30ba060f3659621e7e9`.
Both 2026-11-02 inline review threads have reproduction/code/evidence replies and
are resolved. The outside-diff Unknown-basis observation is recorded separately;
manual new-head review was requested and remains independent of approval.

Reconciled Web Product `0a79e05a` / Test `5f2825e1`, combined
`7c396789e9cf37bfed8cfbaa339025b3d7126e36`, tree
`e49ae5a2d83a7d20e78873a8ee774d4e2ac4cdf4`, passed full local verification:
735 Core/fixture, 12 Architecture, 263 Vitest, 70 Node, 42 Desktop/Mobile browser,
29 composition controls, zero failures/skips. Hosted run **41500323945**, attempt 1,
artifact **13168084539**, ZIP SHA-256
`c9b2dc93c16ed35addb6b98b8a9e7aebef153321dd10aa67b2a91ce1dad38cf2`.
Downloaded pins/trees, all producer hashes and parsed complete inventories match.
Its job queued 2 seconds (created 18:24:49Z, started 18:24:51Z), then executed
456 seconds (completed 18:32:27Z). These are workflow timings, not #1128 assurance.
Ordinary Product PR CI is still separately running/queued; standalone Test CI
remains failed on unmerged Product dependencies. No required check is waived.

No Main/ProjectIDEMain/integration/SecurityTestImplimation push/merge, force push,
Ready/Close, auto-merge, production/DB/RLS/Enforce activation or gate relaxation.
#1128 stays SUSPENDED / NOT_EVALUATED. Remaining work is recorded in the focused
feature document and the live Draft PRs; no shared-client or release GO is claimed.

### New-slice review continuation

CodeRabbit reviewed Product `ba054507` and reported two synchronous ViewModel
reentrancy defects. The independent Test chain adds five bounded reproductions;
the corrected publication/disposal logic is documented in
[the feature record](projectide-source-inspection.md). The earlier 758/274/70/44
Windows/Ubuntu candidate and artifact 13170308202 are historical exact-pin evidence,
not a PASS for the subsequent review correction. New-head receipts, CI and thread
responses remain on the same Draft #1653/#1654 pair. No new feature or gate change
is mixed into these corrections.

### Diagnostic navigation continuation

The 34 inherited PRs were audited at their live Draft heads. The Product and
Test dependency chains stay separate. The inherited ordinary Product checks
succeeded and the 15 standalone Independent Test checks failed with unmerged
Product dependencies. No current unresolved inline thread or approval was
present in the audit. Full review and Owner/CODEOWNER acceptance are pending.

Navigation builds on #1653 and #1654. A current Diagnostic selection is distinct
from a Source or Symbol selection. The Angular Inspector resolves its Diagnostic
record through the same locator as Source navigation. The Windows ViewModel
keeps the selected Core Diagnostic and its primary location separate from the
selected symbol. Both reject ambiguous records and SourceMap entries and clear
the previous target before an attempt. Location checks cover the accepted input,
scope/revision, Document and record membership, property paths and UTF-16
boundaries. A partial result may omit a SourceMap. Its canonical record can still
support a semantic location, while a document-only location is unsupported by
the record navigators. Neither client invents a span.

Angular guards deferred DOM focus across reload, cancellation and context
changes. The pure ViewModel guards success reporting against synchronous
notifications that reset, dispose or replace the selection. Native Avalonia
remains on HOLD under #1144. The existing Option B, Receipt, Evidence, external
closure, Production and #1128 decisions still apply.

The first independent Angular before-run against the inherited Inspector with
Graph composition yielded 7 passed and 5 failed. Exact follow-up pins, inventories
and ordinary/supplemental results are tracked on the stacked Navigation Drafts.

#### Reviewed Navigation heads and next Language Service slice

The live continuation retained these 34 inherited Drafts. Ordinary Product and
documentation CI succeeded on #1603, #1605–#1608, #1611, #1614, #1617, #1620,
#1624, #1628, #1637, #1653, #1657, #1666, #1667, #1681, #1686 and #1688.
Ordinary Independent Test CI failed on #1604, #1609, #1612, #1615, #1618,
#1621, #1625, #1629, #1638, #1654, #1658, #1668, #1682, #1687 and #1689.
Their supplemental receipts remain separate and scoped to their original
heads. Current parent relationships and owner acceptance are tracked on the
existing PRs. No change to their Draft states or branch dependencies was needed.

Navigation Product #1697 and Independent Test #1698 add the paired consumer
candidate. The first full Windows run recorded 293 Vitest passes and three
fixture consistency failures. The Test follow-up removes the stale map artifact
from the intentionally mapless load controls, preserves their assertions and
adds a negative control for the stripped map. Independent focused verification
on the corrected candidate covers 33 Vitest and four desktop/mobile cases.
See [Navigation Product](https://github.com/NYGsatoshi/Coglatas/pull/1697) and
[Navigation Test](https://github.com/NYGsatoshi/Coglatas/pull/1698) for current
pins and the complete cross-host evidence.

The next Product/Test pair adds a bounded Diagnostic-to-Symbol action. It
requires one Core-emitted local symbol at the exact Diagnostic location and a
matching produced SourceMap. The Workbench uses the established source resolver
and the platform's existing navigation state. Mapless partial diagnostics remain
inspectable through the preceding slice. No reference lowering, external
snapshot acquisition or new public contract is introduced.

Navigation qualified on Windows and [Ubuntu run 41748117076](https://github.com/NYGsatoshi/Coglatas/actions/runs/41748117076)
at combined 6c3ac678891492a988bdad69b098bc1788de822f, tree
158bad296ae7c138ea91b5c32f69baae71bb75cb. The exact candidate passed 907
ProjectIDE .NET, 12 Architecture, 297 Vitest, 70 Node, 48 browser and 71
composition controls. Hosted artifact 13284368200 has ZIP SHA-256
52f6a2657d0c0280ff2316bd938fc2011c54552de9dc10e607e2e403a653d51d.
The cross-host comparison verified the full inventories and producer hashes.

The Navigation review identified record-wide symbols appearing in the Diagnostic
Inspector. The stacked Symbol slice displays the unique exact-location symbol
and its references for a Diagnostic selection. Source-record inspection still
shows the record's symbols. The independent control checks a Diagnostic with a
valid property location and no exact symbol at that location.

## Local reference navigation scope

The next independent Product/Test pair extends #1699 and #1700. The
synthetic Web preview and Windows pure ViewModel resolve a current
Core-emitted reference to the unique decoded symbol with the exact
ordinal SymbolKey. The shared Source resolver checks both the origin
and the definition against the current input, revision, source records
and bound SourceMap. Origin and definition are separate read-only
actions. A result with partial coverage may support those actions when
both positions are backed by its map.

Supported locations are mapped declaration and relation records and
their exact properties, including task-port symbol locations when
present. References that are missing, unknown, ambiguous, unsupported,
out of scope, external, mapless, or bound to a different result cannot
produce a navigation target. Document-level source-reference envelopes
remain outside the existing record navigation contract.

#1697 retains its review boundary. Its open finding applies to that
HEAD. The exact Diagnostic-to-Symbol fix and independent regressions
are in #1699/#1700. Find References and its coverage/budget contract
are tracked as a separate slice under #1030. The prior HOLD decisions
remain in force, including #1144 and #1128.

## Bounded local Find References scope

The read-only preview query returns the current inspection's resolved
origins for one exact SymbolKey in Core order. It checks each returned
origin with the same SourceMap requirement used for navigation, with
a ceiling of 4,096 examined references, 64 origin checks and 32 results
per query. It
reports incomplete coverage for truncated searches and when an exact
matching origin is outside the supported Source navigation contract.
The Web query yields during scanning and discards cancelled results.
The pure ViewModel observes cancellation and returns an immutable
query result whose entries must still pass current selection checks
before navigation. No protocol or persistent index is introduced.

## Local language service exact cross-host evidence

The starting audit covered 38 open ProjectIDE PRs. All were Draft, with 21
ordinary CI successes and 17 Independent Test failures. CodeRabbit showed two
completed reviews, 13 rate-limited reviews and 23 skipped reviews. Owner/CODEOWNER
approval was absent. The original 38 PRs kept their exact bases, heads and Draft
state during the continuation.

The single unresolved inline finding was on #1697. The stacked fix in
#1699/#1700 passed a fresh 18-test focused Vitest run on combined commit
360c381de3727a081977516f6374ee90f915237a, tree
2ecfbf2d9711f1b82684b06a315fc7e700cb33ac. The report SHA-256 is
646e4e16fcb9fcc46b0ca4a405a1aa51d253e86fce04234f5355e7fd5ed8b110.
The [parent thread](https://github.com/NYGsatoshi/Coglatas/pull/1697#discussion_r4468135066)
remains open at its original review boundary.

| Input | Commit | Tree |
| --- | --- | --- |
| Go Product base #1699 | 19a3ad044d078b256121f88d109abf77743b7d86 | f4b9f13f4c5fd7d401d7ca0800d0261647ab6c85 |
| Go Product #1702 and Find Product base | 73528ac668067707392521099301e57b5c6d3c1d | d66e9510e496d2cdf4a4c3642edb1de42ee96745 |
| Go Test base #1700 | 42bdd8777ffce045e076886f2f741bd995e8de93 | e2db613f380c2320e90c86a896b6390eeb0ff768 |
| Go Test #1701 and Find Test base | 617381fb651054a86128820d9cee46c04609de71 | 9add919abe52494a124791c9365fb22561f3eee2 |
| Go combined | 4ff98d0881d74fe5e8d24436d484c2a9ca8dcd7f | 1c5585ef293bd319b0160883a36bd67fca2aa5fa |
| Find Product #1704 | 8fa6d1827a19175b75a8446d55b3a18b086a9618 | 2c2d1fe671313c54d3a9ee7cae7271dc37a40aa4 |
| Find Test #1703 | 1f4078b3b6625ec1396e41879d90c21481a03e0d | b33bf36b5862c6690e2b4ff490e60ac293596b00 |
| Find combined | eaad2f8cb0827aecec7f3ab83c9e55b0088b2b8f | 53422355cfc302c3ceb38d98518e0d3365e13f8b |

Both slices used Graph Product #1688 at 05c9fa5afc6c57eb81cfa1d321b555bb85ff8130
and Graph Test #1689 at 7241cd0c2f50ccc740b11d0dbbe2a4e5624df4f0. The
independent ownership checks, exact parent ancestry and branch merge trees
matched the pins.

| Candidate | ProjectIDE .NET and pure ViewModel | Navigation ViewModel class subset | Architecture | Vitest | Node | Playwright | Controls | FAIL | SKIP | NOT_RUN |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Go to Definition | 925 | 38 | 12 | 324 | 70 | 54 | 72 | 0 | 0 | 0 |
| Find References | 927 | 40 | 12 | 332 | 70 | 58 | 72 | 0 | 0 | 0 |

Both hosts passed Release build with warnings as errors, TypeScript, frontend
architecture, route inventory and Angular production build. The existing
stylesheet-budget warnings remain in the logs. Windows used .NET 10.0.403,
Node 24.22.1 and Python 3.12.14. Ubuntu used .NET 10.0.403, Node 24.22.2 and
Python 3.12.12. The first Windows Go build exhausted disk space and passed on
retry at the exact same candidate.

| Candidate | Ubuntu artifact | Ubuntu ZIP SHA-256 | Windows ZIP SHA-256 |
| --- | --- | --- | --- |
| Go to Definition | [41838544006](https://github.com/NYGsatoshi/Coglatas/actions/runs/41838544006/artifacts/13326718884) | d11519c51c681b301b5003c16fbd13843a40a5652004cd69d483932f221b9a98 | edcec025657a20539d7dd0f2d8aba778d5e8eddd4685a52fc2da224ad5ec882a |
| Find References | [41841167565](https://github.com/NYGsatoshi/Coglatas/actions/runs/41841167565/artifacts/13329582116) | 1b699402c8c8ab01bcb1c7f9ae529f7d069c39a56fade3756855eb2b804fb9fa | 33d4b587ac7f56a406fa6ec8eef97b9156f8faa2bb04e2e4d9f0c244337f0a21 |

Both golden exports matched SHA-256
bc8a3a16917544d144aff0db0dcd91cd8e258d0c4ef427baa2d29c11818c243a.
The verifier SHA-256 is
65acbc622c56592877ad10c87f3c298491152fc21f62d9c12b31426c17f267b8.
The ZIPs retain the producer hashes, every recorded artifact-file digest,
full test inventories and the exact checkout checks before and after verification.

The prior Go browser candidate lacked the reference-origin action. Its failed
JUnit report has SHA-256 e07d6ec642c361dac074cb5c81a1ae4ad8205e23040ea8045ab480ed893b7a5f.
The prior Find candidate lacked the query action. Its failed JUnit report has
SHA-256 9e897b33a97ef2f4326b80bfe941d42a31a3279827da810ce5524b833d8eec50.
The [first Find References supplemental run](https://github.com/NYGsatoshi/Coglatas/actions/runs/41839719829)
retains two browser failures for a literal count placeholder. The repaired
locale messages use the existing interpolation format and the independent
browser suite checks both languages.

| Slice | Ordinary Product CI | Ordinary Independent Test CI |
| --- | --- | --- |
| Go to Definition | [41838495161](https://github.com/NYGsatoshi/Coglatas/actions/runs/41838495161) build-test success, workflow in progress at capture | [41838544187](https://github.com/NYGsatoshi/Coglatas/actions/runs/41838544187) build-test failure, workflow in progress at capture |
| Find References | [41841161162](https://github.com/NYGsatoshi/Coglatas/actions/runs/41841161162) queued at capture | [41841167800](https://github.com/NYGsatoshi/Coglatas/actions/runs/41841167800) queued at capture |

The Go Test producer and the first Find Test producer both failed on the unintegrated Core types. The
supplemental evidence has a separate role. All four feature and test PRs
remain Draft, with Owner/CODEOWNER approval pending. CodeRabbit reported
zero actionable issues and a docstring advisory on #1702. The other full
reviews remain pending following rate limits.

The protected refs audit matched main at fe44c73a161b663b4f8fffe491a7fdae0be034e3,
the formal integration ref projectide/integration-20261031 at
158ab153551184cede99c85ce6476dd6e87b6767 and SecurityTestImplimation at
ce57148d8fc233e4134d720d9804e212f73c66bb. ProjectIDEMain was absent at both checks.

Typed context-aware completion is the next bounded read-only candidate under
#1030. Rename Preview, incremental invalidation and production authorization
integration remain open. The existing Manifest, Receipt, Evidence, external
acquisition, executable IR, production enforcement and Native Avalonia holds
retain their recorded status. #1144 remains on HOLD and #1128 is
SUSPENDED/NOT_EVALUATED.
