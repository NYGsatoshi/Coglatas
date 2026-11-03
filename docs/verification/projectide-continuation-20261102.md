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

## Core value-reference continuation — 2026-11-03

New bounded Product/Test stacks depend respectively on #1628 at
`610f9911f9ce31739188437972f7eeb018f611da` and #1629 at
`9e969183f5b311789b0197d0b6aac12a88206e7c`. This inherited register is
continued without a second management ledger. Exact final commits, trees,
Windows/Ubuntu receipts, inventories and ordinary CI are recorded on the Draft
pair after execution; earlier successful receipts certify only their original pins.
See [scope and contract decisions](projectide-core-value-binding.md).

Phase A refreshed #1653/#1654: heads remain `f9894e51` / `80b71222`, Draft/open;
both lifecycle threads are resolved, full changed-head review and owner acceptance
remain pending. Product ordinary CI 41507043327 was in progress at audit. Test
41507123368 has 184 missing-unmerged-Product compile errors in producer
125505749247 and dependent failures/skips; frontend was still running. These
failures stay visible. No duplicate lifecycle fix, source copying or CI waiver.

No Main/ProjectIDEMain/formal integration/SecurityTestImplimation push or merge,
force push, Ready/Close, auto-merge, production/API/DB/resource acquisition,
SEC-ARCH/RLS/Enforce activation or gate relaxation is authorized or performed.
#1128 stays SUSPENDED / NOT_EVALUATED. Windows remains Pure ViewModel preparation;
actual Avalonia/native keyboard/IME/activation is HOLD under #1144.


## Final Evidence / Resource foundation qualification — 2026-11-03

Separate Drafts: Manifest decision #1666; Product #1667; independent Test #1668.
The task selected Option B architecture. Concrete Manifest/Receipt schemas,
reference-to-entry mapping, canonical domains/migration and authenticated receipt/
eligibility/current-authority semantics remain unapproved. The proposal is
`projectide-manifest-option-b.md`; applicable A-R2 authority remains unchanged.

Implemented: caller-invoked in-memory Requirement evidence consistency and
Source-backed typed Tenant Resource reference boundary validation. Reuses existing
identities, target/input/result/provenance and independent outcome axes. No new wire
schema, actual acquisition, satisfaction, authorization, allocation or reservation.
Invalid/missing/partial/unsupported/ambiguous/budget/cancellation claims remain
explicit. Missing, unsupported and Resource target bodies remain unavailable;
absent entity/relation Source origins are rejected. Existing evidence/Resource/
local self-manifest/external IR incomplete gates are unchanged.

| Candidate | Exact HEAD | Tree | Review base / direct parent |
| --- | --- | --- | --- |
| Product #1667 | `585727a9b480e61d08cbeeb3d996904b5f1f4b5e` | `6e8f2f908cdba5bc25d2d436af6316b75a19e925` | base `48ff196985ddbf90db82386c5413c8f659ea64a7`; parent `d0327c9b0cd1e2ac9d8c703ecf1c2a761ed4f22f` |
| Test #1668 | `6edddea75a8dfd0ecaca8559827ffb92ba06a365` | `93b9ec8aed73e6577dfcfb0be95b644bb2803e4d` | base `8f701e8db3097c74c9a63c30669fbfe36be7bb48`; parent `1b46ba6426e695956ff2406d5b0116a2c7f3cdf7` |
| Core combined | `f038d8216346557b0b9d4d6abbfdce082ddfc333` | `ed9cefbbba01495c31dda44637169e39cfaa2c3f` | canonical isolated composition |
| All-pair consumers | `19b5a8f99ce8c1432389c94af3c29fa428b296da` | `db02e99f6965cacac303f0a5f2449d9f85322a8b` | exact Inspector #1653 `d77b9814f96ba593b98b8842cf1d1355cf310885` / #1654 `930bb8866cf2b8c3c08a414ffc2aa782072ba443` |

Windows local and Ubuntu Core supplemental [41562716515/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/41562716515):
**804 Core + 12 Architecture + 17 composition/content-DAG controls**, zero failures/
skips; Release warnings-as-errors build has zero warnings/errors. All previous
753 Core responsibilities remain; all 51 new evidence/Resource cases execute.
Discovery, TRX definitions/entries/results and complete identities/outcomes agree.

Windows local and Ubuntu consumer supplemental [41562716656/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/41562716656):
**842 Core/fixture/Pure ViewModel + 12 Architecture + 278 Vitest + 70 Node + 44
actual desktop/mobile browser cases + 43 controls**, zero failures/skips/pending/
todo. TypeScript, Angular production build, architecture/route controls and all
existing regression responsibilities pass. Each browser producer owns an unused
port/server, binds an explicit static root and verifies the served index hash;
zero retries and forbid-only. No default-port reuse or duplicate browser producer.

Exact fixture bytes reproduce SHA-256
`bc8a3a16917544d144aff0db0dcd91cd8e258d0c4ef427baa2d29c11818c243a` on both hosts and match the pinned Git blob.
All input/base/parent/combined identities, Source ownership and Test-owned equality
are checked; exact candidate checkout is clean before/after, including untracked
files. Test Runtime Source is not copied from Product. The independent lineage
retains the fixed SecurityTestImplimation ancestor. Windows reused dependencies
only after exact lock-byte equality with its successful npm-ci install; local and
hosted dependency provenance remain separate. SDK 10.0.403 on both; Node 24.22.1
Windows / 24.22.2 Ubuntu. Raw verifier/baseline source hashes differ by CRLF/LF;
each matches its host bytes and the identical exact Git blob after newline
normalization. No raw cross-host hash equality is invented.

Downloaded producer archives match GitHub digests; every declared file hash is
independently checked, separately per host:

- Core artifact **13198605632**, ZIP SHA-256
  `0f30e46c9fc90806d9bddda59573a25976fcf8a457fb4984b55f4410250e14ff`.
- Consumer artifact **13198182364**, ZIP SHA-256
  `bbe39140527c3970e3fafffe183019f2e645c9fcae2f19adae5702630b955268`.

Before evidence: initial new-API tests against unchanged `48ff1969` fail at build,
not behavioral execution. First implementation test run retained 41 PASS / 2
fixture-assumption FAIL; unknown receipt fields are correctly rejected by the
existing parser. An intermediate target fixture also attempted invalid non-Project
roots; those failures remain retained and the fixtures now use declarations.
Actual target-body regression: Product `137cd1586df431f849bf772c4a89e2bc5b148545`
with Test `1b46ba64`, combined `45a6e5839d472b15032aab5ef44c66d2217e2af4`,
tree `e346a2ae70d5d726d76cc1963f2b3fe4af0e08f5`: **3 FAIL / 47 PASS / 0 skip**;
TRX SHA-256 `4ce682b5f7d0c591c37aa2961ef05ed4c2075d16df31a3671f438873f7ca6093`.
Relation-origin regression: Product `d0327c9b` with Test `6edddea7`, combined
`9db2ccdee06e7f70271352aed4d4e82fae02eeb2`, tree
`ace05f749dcae09472771e3d85a039bed7f4be0b`: **1 FAIL / 17 PASS / 0 skip**;
TRX SHA-256 `9cef1ab8b4283bc82806b95312707ecd231ccba39612ea53428f2a04bf11a25a`.
Those four behavioral regressions pass in the final full inventory. Earlier
successful/cancelled/failed candidates certify only their own pins.

Ordinary PR CI remains independent: Product [41562708845](https://github.com/NYGsatoshi/Coglatas/actions/runs/41562708845)
was **IN_PROGRESS / PENDING** when this checkpoint was written;
Test [41562716565](https://github.com/NYGsatoshi/Coglatas/actions/runs/41562716565)
is **COMPLETED / FAILURE**. Test producer logs report
absent unmerged Product namespaces/types with dependent failures/skips; they are
not waived or replaced by supplemental success. Current exact-HEAD normal CI must
be reread for later review/acceptance. No Required Check changes.

Phase A inherited heads/bases remain the task pins. #1657/#1653 ordinary Product
CI succeeded; #1658/#1654 ordinary Test CI failed on absent Product dependencies,
including Inspector frontend TS2307 modules. Reacquired inherited ZIP digests match
their historical receipts. No unresolved inline thread or APPROVED review was
returned. Exact Product re-review retries returned CodeRabbit **rate limited /
Action not completed**, not full review or approval. CLI browser login was stopped
without authentication; no billing/limit change. New-head source/CODEOWNER/Owner
acceptance remains pending. Observed inherited Inspector Test bytecode caches are
retained; acceptance uses separate verified clean candidate worktrees.

Remaining: concrete Manifest/Receipt schema and migration decision; local
self-manifest reference binding; authenticated actual evidence/Resource/external
snapshot materialization, target-specific eligibility/current authorization,
coverage/freshness and offline bundle mapping; per-dependency input/manifest and
supported transitive IR lowering. Content-DAG prototypes are not a semantic closure
implementation. `external-closure-not-lowered` remains; no incomplete executable
IR or Complete Pass. Consumer results are bounded synthetic regression parity,
not new external navigation or actual Avalonia/native UI/IME/a11y/activation
acceptance. #1144 HOLD; #1128 SUSPENDED / NOT_EVALUATED.

Only this task's feature/test/docs refs are pushed. No Main/ProjectIDEMain/formal
ProjectIDE integration/SecurityTestImplimation push/merge, force push, Ready/Close,
auto-merge, production/API/DB/Job/Resource action, RLS/SEC-ARCH activation, release
approval or gate relaxation. External Main advances are recorded as external,
never attributed to these changes. Final ref/PR audit is linked from the PR receipts.


Final protected-ref audit at 2026-11-03T02:15:30.494006+00:00: Main advanced externally from
1542923a203191dbe7f37032ef655728ead54533 to
aac603be6d5389f8b605d22f7b6c7d5442a47c97. Formal ProjectIDE integration remains
158ab153551184cede99c85ce6476dd6e87b6767;
SecurityTestImplimation remains a4bb658903a7b11d564006949fc58b42b94da575.
No exact ProjectIDEMain remote ref was returned. All seven inspected PRs remain
Draft/open with auto-merge unset and zero APPROVED reviews. All five task source/
verification worktrees are clean. Only the named feature/test/docs refs were
pushed by this task; no protected ref was changed by this task.


Ordinary CI update at 2026-11-03T02:32:39.694390+00:00: Product
#1667 run 41562708845 completed **SUCCESS** on exact `585727a9`. Standalone Test
#1668 run 41562716565 remains **FAILURE** on absent unmerged Product dependencies;
supplemental success does not waive it. After the included-capacity reset,
CodeRabbit reported **Full review triggered** for #1657 at 02:24 UTC; no completed
current-head review or approval is inferred while publication is pending. #1653
and #1667 re-review requests remained rate-limited. No billing/limit change.


#1657 full review subsequently published at exact `48ff1969` (review 5746497216,
COMMENTED, 2026-11-03 02:30 UTC) with one Major predicate-reference finding. The
reported bypass is not reproducible: the existing Boolean-only predicate parser
rejects decoded typed references before Requirement construction/binding. Seven
independent read-only Windows probes pass against unchanged Product, including
all four reference kinds, a short-circuited operand, a spoofed Boolean and opaque
Unknown provenance. Result SHA-256
`51959c7351750eacd0f38a8352f5b6952b3ed0108f540f2499f9380f5d590732`.
Reproduction/source and precise limitations: `projectide-core-predicate-review.md`.
These probes do not change the qualified Product/Test heads or maintained unit
inventories. Reviewer reconsideration/resolution is pending on the original
thread; no runtime patch or approval is inferred.


#1657 review validation outcome: CodeRabbit inspected the exact `48ff1969` source,
withdrew its sole predicate-reference finding and resolved thread
`PRRT_kwDOSy0QwM6y9NKW` (reply 4450375540). The provider acknowledged the existing
Boolean-only guard and eager operand validation, confirmed no demonstrated binding
bypass and required no runtime/raw-scan patch. The reviewer did not rerun the seven
probes; those are separately retained Windows evidence. Full review 5746497216 is
now a completed current-head COMMENTED review with no unresolved finding, not
Owner/CODEOWNER approval. #1653/#1667 full changed-head review remains rate-limited.
Ordinary current Product CI is SUCCESS; ordinary standalone Test CI is FAILURE;
both exact supplemental runs and Windows parity remain verified without runtime
head changes.

## Immutable admission / receipt / closure continuation — 2026-11-03

### Reacquired authority and Phase A audit

All seven PR heads/bases/trees, ordinary checks, review requests/submissions,
review threads and supplemental run states were reacquired. No accepted concrete
Manifest/Receipt schema, authenticated producer, authorization-freshness interface
or Evidence Eligibility decision was found in #1666/#905/#798. Formal specification
cross-check uses Coglatas-Spec `850a5d9376db11e75235cf55e03ebed4de9c0510`:
effective-contracts blob `1f333c99d12fe84384fd350514ec9399b0c80673`, controlling
A-R2 amendment blob `ab45d7cf031656b4dfc2344b478f4f1bf1fe231f`, and the
2026-10-31 parallel-client decision. Private specification text is not republished.

| PR | Head / tree | Review base | Ordinary PR CI at initial audit |
| --- | --- | --- | --- |
| #1657 | `48ff196985ddbf90db82386c5413c8f659ea64a7` / `714124fb87a791ffedd3bdf26fb919093a68eb97` | `610f9911f9ce31739188437972f7eeb018f611da` | SUCCESS 41514097329 |
| #1658 | `8f701e8db3097c74c9a63c30669fbfe36be7bb48` / `d342e8379d98a7a0b6253b1f726f52521f3951e8` | `9e969183f5b311789b0197d0b6aac12a88206e7c` | FAILURE 41517973360; 5 failed / 4 skipped PR-CI jobs |
| #1666 | `d138d53690c95c3832b3a40b948a0a8e9fcfd492` / `17ca07fd1634350b03cf80c3ea13dc3ee1836cfa` | `48ff196985ddbf90db82386c5413c8f659ea64a7` | Documentation routing, 4 skipped PR-CI jobs; not a runtime qualification |
| #1667 | `585727a9b480e61d08cbeeb3d996904b5f1f4b5e` / `6e8f2f908cdba5bc25d2d436af6316b75a19e925` | `48ff196985ddbf90db82386c5413c8f659ea64a7` | SUCCESS 41562708845 |
| #1668 | `6edddea75a8dfd0ecaca8559827ffb92ba06a365` / `93b9ec8aed73e6577dfcfb0be95b644bb2803e4d` | `8f701e8db3097c74c9a63c30669fbfe36be7bb48` | FAILURE 41562716565; 5 failed / 4 skipped PR-CI jobs |
| #1653 | `d77b9814f96ba593b98b8842cf1d1355cf310885` / `5d2308c95843feb2c34e413169c8b45782403285` | `0a79e05a6533a7e4468bb7b74f47aeceb04ae119` | SUCCESS 41517683660 |
| #1654 | `930bb8866cf2b8c3c08a414ffc2aa782072ba443` / `18aeb0072029e73f2fbb0e79454cd83f54dc9a7c` | `5f2825e1d88f307efddc5edac16579ec220cbd4b` | FAILURE 41517821832; 6 failed / 4 skipped PR-CI jobs |

Supplemental 41562716515 / 41562716656 remain SUCCESS on exact Test `6edddea7`;
Inspector 41517821241 remains SUCCESS on `930bb886`; previous Core and consumer
41517973310 / 41517973455 remain SUCCESS on `8f701e8d`. These are historical
comparison receipts, not automatic acceptance of the new candidate.

All inspected PRs remained Draft/open with auto-merge unset. No APPROVED review
or unresolved thread was returned. #1653 has three resolved findings. #1657's
completed exact-head COMMENTED review and withdrawn predicate finding remain
unchanged; no duplicate runtime fix was applied. #1666/#1667/#1668/#1654 had no
submitted reviews. CODEOWNERS names `@NYGsatoshi`; no Owner/schema acceptance is
inferred. Normal included-capacity full-review requests on #1681/#1682/#1666/
#1667/#1653 returned **rate limited / Action not completed** at 13:11 UTC (first
response 13:11:34), with 46 minutes until the reported next included capacity. No CLI/alternate-identity,
billing, configuration or limit workaround was attempted.

The prompt's RiderProjects checkout was absent. Existing intact repository
`C:/Users/Hirota Kaname/source/repos/NYGsatoshi/Coglatas` and its registered
continuation worktrees were found and used; no deleted environment was recreated.
Initial protected remote refs: `main` `f44faf582031812f84e07e8e291ec04725d52131`,
formal integration `158ab153551184cede99c85ce6476dd6e87b6767`, and
SecurityTestImplimation `a4bb658903a7b11d564006949fc58b42b94da575`.
No exact Main/ProjectIDEMain ref was returned. External ref movement is separate
from this task's explicitly named feature/test/docs pushes.

### Implemented bounded slice and concrete decision packet

Draft Product [#1681](https://github.com/NYGsatoshi/Coglatas/pull/1681) uses exact
#1667 as its base; independent Draft Test
[#1682](https://github.com/NYGsatoshi/Coglatas/pull/1682) uses exact #1668.
Product Runtime Source remains owned by Product; Test contains no copied runtime.
The new behavior is existing-type immutable materialization admission, duplicate/
conflict/opaque detection, shared budgets and cooperative cancellation/disposal,
safe denied-body projection and deterministic exhausted-prefix findings.
Detailed implementation boundaries are on the Product branch in
`projectide-materialization-binding.md`.

The 22-row concrete packet in `projectide-manifest-option-b.md` records normative
source IDs, proposed types/encoding/domain/identity/reference mapping/input
association/actual sets/receipt responsibilities, unresolved choices, compatibility
impacts and independent verification obligations. Packet commit `5142115c99d8f922d2439cd54f1e178178c0e427`.
Option B architecture remains SELECTED; every concrete schema/trust/authority/
offline/migration/eligibility row remains **PROPOSED / OWNER DECISION REQUIRED**.
No formal Wire V1, trust root, production default or existing-v1 reinterpretation
is adopted. Fifteen independent .NET cases and eighteen additional test-only
Receipt/Graph proposal controls exercise the new bounded slice and future
decision cases. These models are non-normative; their tuple shapes and ASCII
hash examples never reach runtime or certify transitive closure.

### Retained before-fail evidence

Corrected original behavioral proof: unchanged Product `585727a9`, exact Test
`cb3012a26574bcdd9bcc7a8fd07e3ccdc4b31639`, combined
`7d9c573eb1c204d988879e6b680239a6c11efaf7`, tree
`3f9ecafeac75bb3ff128a0fc25dc0f73b4f6a36a`: **7 FAIL / 6 PASS / 0 SKIP**.
TRX SHA-256 `33aa58cde641bdd8299b6e0a77258947a7f96b491a091b6ffe9d5d4187bec3ad`.
Clean checkout, Product source equality and relevant exact-Test equality hold.
Initial raw 8 FAIL / 5 PASS with Test `8404e250` is separate: one unknown-pin
fixture was already rejected by the input constructor and the fixed budget
could exhaust local Source before reaching the supply. The first corrected-head
full run's 815 PASS / 2 FAIL is retained, not relabelled as successful behavior.

An additional self-audit caught supply-order-dependent exhaustion findings in
intermediate Product `38fba7d6`. Test `5ea8f4ce58b434433afc3ac702d8780dc04e89ac`,
combined `4c78ad59b9544dd431c3b80f6321893b4bb3dba0`, tree
`0d00afdae97b2947162868bcc033640d702c774d`: **1 FAIL / 14 PASS / 0 SKIP**;
TRX SHA-256 `188b1bed8d67d9707cf8da1baca7dc9d1602a6090108087fdb5700bbcbd1068c`.
Product `7f5bb1d1` moves denial finding publication after successful bounded
enumeration; exhausted prefixes retain no partial symbols/bodies. The repeated
denied-context entry-cap control remains included.

The intermediate `38fba7d6` / `cb3012a2` Core candidate passed Windows/Ubuntu
817 Core + 12 Architecture + 35 controls in 41641823007. ZIP artifact 13232503128,
SHA-256 `c729516972952120fab4dcbf04b813a171b64b5995cce9d86250abb86440a4d3`,
matches GitHub; all eight producer-file hashes, 54 runtime Git-blob/host-byte
identities and complete inventories were independently compared. Its consumer
41641822930 also completed SUCCESS. Both remain superseded comparison evidence;
the final two added controls and new Product head require their own receipts.

### Final exact Product/Test and both-host qualification

The consumer comparison baseline's duplicate old combined-key spellings were
corrected in Test `4eb77ef3`: the baseline now names one exact earlier verified
pair and combined commit/tree. Actual candidate pins/inventories had matched;
the correction removes ambiguous provenance rather than changing an oracle.
Intermediate `7474642b` receipts remain separate, not final-head evidence.

| Role | Review base / base tree | Final HEAD / tree |
| --- | --- | --- |
| Product #1681 | `585727a9b480e61d08cbeeb3d996904b5f1f4b5e` / `6e8f2f908cdba5bc25d2d436af6316b75a19e925` | `7f5bb1d1f4902bf3e5505276031056ec3f7276e9` / `9b6b40398ec2ddabefd1d88fccf794edf6cd0492` |
| Independent Test #1682 | `6edddea75a8dfd0ecaca8559827ffb92ba06a365` / `93b9ec8aed73e6577dfcfb0be95b644bb2803e4d` | `4eb77ef367510730ca1fa357a01700ad740fc980` / `b36592b014277a4886d4237e0a565f5fb3296399` |
| Exact Core combined | Exact Product + Test ownership-checked composition | `66b2c9ecbebe13b4741d6a8159d8bd0b469be728` / `dc796f351e3cf6cd72386e86e0541829f242b4a4` |
| Exact consumer combined | Same Core pair plus Inspector `d77b9814` / Test `930bb886` | `f35408e77a87667b450b9e775e68156449df3d78` / `291752d4cc3acb6f60c3aa4d84a86ee579c542e9` |

Windows and fresh Ubuntu [Core 41645131682/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/41645131682)
pass **819 Core Unit/Contract + 12 Architecture + 35 controls**. Windows and
fresh Ubuntu [consumer 41645131500/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/41645131500)
pass **857 Core/fixture/Pure ViewModel + 12 Architecture + 278 Vitest + 70 Node +
44 Desktop/Mobile browser + 61 controls**. Release warnings-as-errors builds
pass with zero warnings/errors. Zero final selected failures/skips/pending/todo;
all fifteen new .NET cases execute. Complete inventories retain all original
804/842 responsibilities, the intermediate thirteen admission cases, and both
additional controls. No case was deleted or skipped to obtain success.

Downloaded Core artifact **13234992054**, ZIP SHA-256
`b57a11f8c9db1f429deed3a0483eacb9619ffb56baa5f597e080700ad8d4f915`, equals GitHub.
All eight producer-file hashes, 54 Runtime Git-blob identities/host bytes,
exact pins/trees/combined commit and complete discovery/execution/control
inventories are independently verified. Consumer artifact **13234377664**, ZIP
SHA-256 `69a11d80c3936bd451dd17607386eb0c297db52856e700cc7d484c9e9a126696`,
also equals GitHub. Every declared file hash is checked: 32 hosted files and 33
Windows files, the extra Windows file documenting dependency reuse. Parsed TRX,
Vitest, Node, browser and all 61 control identities/outcomes match across hosts.
Vitest absolute host prefixes are normalized only after binding each to its
actual checkout; repository-relative test file/name/status remain exact.

Fixture reproduction is byte-exact against its pinned Git blob on both hosts:
SHA-256 `bc8a3a16917544d144aff0db0dcd91cd8e258d0c4ef427baa2d29c11818c243a`.
Existing Source/input/result golden tests remain in the executed inventory.
Windows browser port **58128** and Ubuntu port **43347** use owned servers,
exact static roots, matching per-host served/expected index SHA-256, zero retries
and forbid-only. Different host build hashes are not falsely described as equal.

Toolchains: Windows 11 `10.0.26200`, Python **3.12.14**, Node **24.22.1**;
Ubuntu/Linux `7.0.0-1017-azure` / glibc 2.39, Python **3.12.12**, Node **24.22.2**;
both use exact repository SDK **10.0.403** and pinned npm **11.17.0**.
Windows initially lacked an npm command; the repository-pinned npm package was
downloaded and its registry SHA-512 integrity verified, without modifying a
toolchain pin or user/global configuration. Package ZIP SHA-256
`b290bbb35b9e72c3ef84edbe041f28c4479c4d9ee79f555817b8caafe7ce4bba`.

Windows baseline/verifier raw hashes use CRLF; Ubuntu uses LF. Both are checked
against the same exact Test Git blobs and their actual host representation,
not assumed to have equal raw SHA-256. All semantic baseline pins and complete
inventories match. The final metadata-only Test correction reused local build
intermediates only after exact unchanged C#/project/frontend source checks;
all build/discovery/controls/tests/fixture/browser commands reran at the new
clean commit/tree. Ubuntu independently rebuilt fresh. Locked npm dependencies
were installed once and reused sequentially through task-owned junctions after
all four package/lock byte hashes matched. No Product Runtime Source was copied
to Test. Producer/consumer ownership and reuse conditions are in the receipts.

Exact candidate checkouts are clean before and after verification, including
untracked files. Runtime equality and every Test-owned delta are checked. The
consumer retains the three Inspector-owned test files rather than mistaking
their presence for Core Test ownership. An initial over-broad local orchestration
preflight rejected those expected extras before execution; its failure log is
retained separately. No product assertion or quality gate was relaxed.

Ordinary Product [PR CI 41643472906](https://github.com/NYGsatoshi/Coglatas/actions/runs/41643472906)
completed **SUCCESS** at exact Product `7f5bb1d1`; Security, Functional, build,
unit, frontend, ReSharper and Specification lanes succeeded. Ordinary standalone Test
[41645154387](https://github.com/NYGsatoshi/Coglatas/actions/runs/41645154387)
is **FAILURE**, with 5 failed / 4 skipped PR-CI jobs. The actual final Test
producer **125948715934** reports CS0234/CS0246 missing unmerged Core namespaces/
types; supplemental success does not waive that failure or copy Product Source
to repair it. The superseded Product run 41641818467 had a functional aggregate
failure on `RUNTIME_RESULT=cancelled` / `FUNCTIONAL_RESULT=skipped`; no runtime
behavior defect is inferred. After normal obsolete-head cancellation did not
finish, only that exact superseded `38fba7d6` run was force-cancelled through the
Actions API at 13:34 UTC, releasing latest-head CI under the existing
`cancel-in-progress: true` policy. This is not a Git force push, gate waiver or
cancellation of current-head qualification. All old failure/cancellation records
remain; current-head Product CI and Owner/review acceptance are separate gates.

Remaining boundary: approved local reference-to-Manifest mapping and migration;
actual authenticated receipt/materialization and current authority; Resource/
external Snapshot acquisition; eligible target-specific Requirement Evidence;
per-dependency complete graph and supported transitive External IR lowering;
actual native UI/IME/accessibility/activation. The seven evidence stages remain
separate. The current caller authorization boolean is a host claim, not verified
producer trust or current authority. `requirement-evidence-unavailable`,
`resource-snapshot-unavailable`, local `value-reference-unavailable` and
`external-closure-not-lowered` remain. No incomplete IR becomes executable.
#1144 remains HOLD; #1128 remains SUSPENDED / NOT_EVALUATED.

Local raw artifacts, complete inventories, both before-fail receipts, orchestration
failures, ZIP downloads, independent comparison outputs and npm provenance are
retained under
`C:/Users/Hirota Kaname/.codex/tmp/projectide-materialization-20261103/`.
Final Windows receipts are `core-final/receipt.json` and
`consumers-final/receipt.json`; independent comparisons are
`hosted/core-final-comparison.json` and `hosted/consumer-final-comparison.json`.
These are verification artifacts, not another management ledger.

Protected-ref audit observed at 2026-11-03 13:58 UTC:

| Remote ref | Start | End observed | Task attribution |
| --- | --- | --- | --- |
| `main` | `f44faf582031812f84e07e8e291ec04725d52131` | `8e51d84189a123d3b0d2617a2fd5671f736af9c7` | External advance; this task did not push/merge it |
| `projectide/integration-20261031` | `158ab153551184cede99c85ce6476dd6e87b6767` | `158ab153551184cede99c85ce6476dd6e87b6767` | Unchanged |
| `SecurityTestImplimation` | `a4bb658903a7b11d564006949fc58b42b94da575` | `a4bb658903a7b11d564006949fc58b42b94da575` | Unchanged |
| Exact `ProjectIDEMain` / `Main` | Absent | Absent | No ref created |

Only `feature/projectide-materialization-binding-20261103`,
`test/projectide-evidence-materialization-20261103` and the existing
`docs/projectide-manifest-option-b-20261103` are pushed by this task. No protected
Git ref, Draft lifecycle, auto-merge, required check/threshold, production system,
SEC-ARCH/RLS/Enforce or #1128 policy was changed. Full current-head CodeRabbit,
Owner/CODEOWNER and all concrete schema/trust/eligibility acceptance remain
separate/pending; rate-limit responses are not approval.
