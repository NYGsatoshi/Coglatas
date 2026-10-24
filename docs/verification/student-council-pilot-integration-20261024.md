# Student Council synthetic integration candidate — 2026-10-24

Related: #611, #1365, #1349 and #1374. This is a **Draft, test-only
integration branch**, not a Main merge or release candidate approval.

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
