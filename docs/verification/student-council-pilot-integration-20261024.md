# Student Council synthetic integration candidate — 2026-10-24

Related: #611, #1365, #1349 and #1374. This is a **Draft, test-only
integration branch**, not a Main merge or release candidate approval.

## Current isolated synchronization — 2026-10-26 JST

**REVIEW_ONLY / Draft / MUST NOT MERGE TO MAIN / RELEASE_NO_GO.** The ordinary
components and older diagnostic checkpoints below are historical. The previous
protected `suite=all` execution is already complete and is not being repeated
on its old source.

Current observed Product Main is
`81e40dcbf419ba16fb5b03a89869baaf5f5269b0`.
[Main CI 40229408647](https://github.com/NYGsatoshi/Coglatas/actions/runs/40229408647)
attempt 1 has **47/47 successful jobs**, no failed/cancelled/skipped jobs.
That result belongs to Main, not this diagnostic successor or every release
gate. Earlier first-attempt failures remain independent evidence.

The local integration preserves normal ancestry:

1. Existing PR #1378 head `a9fa3d923e8766254e8eb193246aee8dc45d2eb7`
   normally merges Main 81e40 as `bd3d627c364011d782d341d533d5fbff23e64522`.
2. That source normally merges current held #1367 head
   `70b38d1e60e5ce2ee395e9ac7d71f0c81cbbcef5` as executable integration
   `88d6bd7d1b0053c46bdd6f2ad96b9bdca24394fb`.

There is no reset, rebase, force update or Main merge. The first merge resolves
two historical overlaps by preserving the exact current Main files:
`message-thread-context.spec.ts` retains every later Thread control, and
`FileSharingReadOnlyMutationHostedHttpTests.cs` retains the reviewed property
pattern assertions. Current production, other tests, dependency graphs and
workflows are not replaced by older component snapshots.

For the second merge, the twelve current #1367 paths have no later Main overlap
since their common base `4776053c56746a1fe79e0449648ab0d6d153c908`.
All twelve resulting blobs are exactly equal to head 70b38, including current
Composer diagnostic readers and the fresh-request/old-400 distinction. The
Main-to-successor inventory is therefore exactly these diagnostic paths plus
this existing integration document:

| Path | Change from Main |
| --- | --- |
| `.github/workflows/thread-functional-typecheck.yml` | Existing held optional read-only typecheck workflow |
| `docs/verification/functional-ci/authorized-thread-projection-diagnostics.md` | Held bounded chronology contract and limitations |
| `scripts/ci/test-functional-diagnostics.py` | Strict independent evidence controls |
| `scripts/ci/thread_probe_diagnostics.py` | Strict bounded reader |
| `tests/functional/helpers/thread-initial-observation.d.mts` | Diagnostic declaration |
| `tests/functional/helpers/thread-initial-observation.mjs` | Boolean initial/old-denial DOM capture validation |
| `tests/functional/helpers/thread-probe-diagnostics.mjs` | Validated bounded projection |
| `tests/functional/helpers/thread-probe-observation.d.mts` | Diagnostic declaration |
| `tests/functional/helpers/thread-probe-observation.mjs` | Bounded timing/request observation |
| `tests/functional/messaging/collaboration-owner-journey.spec.ts` | Existing owner instrumentation only |
| `tests/functional/thread-initial-observation.node-test.mjs` | Observation and failure-preservation controls |
| `tests/functional/thread-probe-observation.node-test.mjs` | Observation and correlation controls |
| This document | Current source and execution inventory |

`src/`, frontend production, ordinary backend tests, dependency manifests/locks
and the protected licensed workflow match Main exactly. No held #1361 Composer,
#1320 cross-session, #1326 private transport, #1399 security or #1379 recovery
candidate delta is imported. No private specification content, security policy,
RLS, schema, migration, role, deployment or environment contract changes.
The original Thread assertions/native actions, distinct fresh request,
120-second owner deadline, retries, event/ordinal and 4096-byte caps are retained.

## Completed predecessor evidence and successor validation

The OWNER's [exact-head checkpoint](https://github.com/NYGsatoshi/Coglatas/pull/1378#issuecomment-6381070154)
records protected [run 40068617000](https://github.com/NYGsatoshi/Coglatas/actions/runs/40068617000),
attempt 1, job `120797029232`, **SUCCESS** at exact head a9fa. Current workflow
metadata independently confirms that source, attempt, branch and conclusion.
Its P0, authorization, My Tasks and MBJ-01/02/03 steps all completed; the
checkpoint reports required P0 **8/8**, My Tasks **1/1**, completed FCI-04 Full
and eight executed PostgreSQL MBJ follow-up tests. Raw successful JUnit/TRX and
loaded assembly digests were not independently acquired here; do not invent
those deeper checks or repeat the already completed source. Mobile/Issue 683
opt-ins and prebuilt-Main reuse were configured skips, not executed credit.

At local executable merge 88d6bd7d, current strict Messaging TypeScript passes,
both Thread Node files pass **43/43**, and independent Python evidence controls
pass **18/18**, with zero skipped cases/retries. Root and frontend installed
dependency locks match this source. An initial default-sandbox Node subprocess
invocation failed; the unchanged controls passed with the supported execution
grant. It is retained as setup evidence, not a repaired product or hosted pass.

```sh
npm run typecheck:functional:messaging
node --test tests/functional/thread-initial-observation.node-test.mjs tests/functional/thread-probe-observation.node-test.mjs
python3 scripts/ci/test-functional-diagnostics.py -v
git diff --check
```

## One current-source protected execution after publication

The next action is conditional on independent integration review and normal
publication to the existing Draft #1378 branch. Re-read the published immutable
head, freeze that source during execution and dispatch the existing
`licensed-real-backend-acceptance.yml` once with explicit `suite=all`. Use only
the existing protected `syncfusion-licensed-build` environment; its protection
remains applicable. Do not dispatch held #1367 itself, change retry/deadline
policy, substitute Main's producer or introduce a second owner.

Reconcile exact published/checkout/TARGET_SHA, run ID/attempt, actual build/image
producer and the workflow's selected P0, authorization, My Tasks and MBJ owners.
Verify real PostgreSQL and backend TRX/JUnit execution where selected, required
case counts and source/image/assembly evidence actually retained. PR CI's tested
merge SHA is separate from this branch-head protected execution. Configured
mobile/Issue 683 skips are outside `all` and cannot grant device/repeated-race
credit. FCI-04 Full is not complete Fast/Full/Extended messaging qualification.

Every initial failure remains immutable and must be investigated without a
manufactured-green rerun. No successor execution has been requested or credited
by this local preparation. A pass cannot close historical causes, qualify Main,
lift Owner HOLDs or change this PR's explicit Draft/non-Main-merge contract.

## Historical original integration

Original base: Product Main `0c5f9baace186940f948758b1731fd3ec891c2f3`.
The corrected integration normally incorporates Main
`0dd39000b38dd047e0a35513e2b7403d3bc5cd06`, including merged #1373.
Component executions remain bound to their original sources; the combined
candidate needs its own executions.

| Component | Reviewed original source | Integration scope |
| --- | --- | --- |
| #1376 dependency repair | `5bc55dc30c67d476ae0f6cec713786f7c12af90c` | Patched same-major body-parser/undici overrides and lock records |
| #1372 strict owner typecheck | `f750f545f937cc4393f65fadb59092df49d9ac3e` | Declaration filename correction and explicit non-gating local check |
| #1373 retained batch selection | `5d72d34afa6c271c42bd9ef17e8012de09022258` | One current-role File regression |
| #1377 sharing/intake distinction | `b73db04a5b0c8b56f71bd8720182e9573296d5c2` | Exact final test file, including its parent insertion and denial-category correction |
| #1375 newer Thread intent | `2bb0f6bfb63c32712461a842b0a750682cb0170d` | Corrected newer-selection guard, retained unrelated drafts and close tombstones with six component controls |
| #1367 bounded observation | `5a925b2505b5bacfb9239a4c1baa4c30e2d78f44` | Timing/structural diagnostics for a fresh synthetic execution only |

The sharing-file cherry-pick required resolving a missing-parent insertion by
retaining the exact final reviewed file bytes. The diagnostic document combined
cleanly with the typecheck documentation. No runtime conflict was resolved by
changing a product or security contract.

The planned protected `Licensed Real Backend Acceptance` run uses `suite=all`
on this exact branch through the existing `syncfusion-licensed-build`
environment. It creates isolated synthetic fixtures; it does not select an SSH
target, stable public origin or real identity. Any environment protection still
applies. Record the actual checkout, run, attempt, image and required-owner
results separately from PR metadata and Main results.

Original Full failure and all earlier attempts remain retained. A green combined
run cannot establish the cause of #1365/#1360, qualify Main or authorize merging
review-only #1367. No held #1320, #1326 or #1361 code is included. Preserve this
branch as Draft; integrate ordinary components only through their own review
and current required checks. Both Owner HOLDs remain; CODE_READY and
RELEASE_READY are not established and PUBLIC_GO is not authorized.

## Corrected integration qualification

Initial combined HEAD `adc45c4a497cd71671b0efcb48a7bbcab54556a8` is retained
with its protected run `40067491325/1`. It contains superseded Thread behavior:
new selection survived while unrelated captured drafts were discarded. The
corrected component source above preserves those drafts and separately excludes
closed roots. The older run cannot qualify the corrected combined HEAD.

A fresh complete local Angular run and a separate protected `suite=all` run are
required on the corrected source. No prior first-attempt failure or source-bound
pass is rewritten. This Draft remains explicitly non-merge.
