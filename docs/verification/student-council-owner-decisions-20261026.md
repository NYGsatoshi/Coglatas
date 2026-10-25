# Student Council Owner decision packet — 2026-10-26 JST

**No approval is recorded by this packet. RELEASE_NO_GO remains.** These are
separate decisions, not a combined request to approve security activation or
school use. The [source-bound matrix](student-council-readiness.json) and
[review supplement](student-council-readiness-20261026.md) contain the current
baseline and evidence. Normal, unheld Files engineering proceeds independently.

## 1. Held Composer candidate

- Decision: retain the explicit hold or authorize integration of the reviewed
  product correction in [#1361](https://github.com/NYGsatoshi/Coglatas/pull/1361),
  head `3de9eb782cdbc1f795f8c10ceebab4cf4b73ab10`, subject to current merge-base,
  exact-head checks and independent resulting Main qualification.
- Current behavior on Main: routine catch-up can destroy the editable Composer
  node; a controlled real-stack comparator reproduced that mechanism. The held
  candidate retains local input only for the same authenticated scope, with
  sending disabled until fresh authority. The identical owner passed on the
  candidate; independent scope/stale-binding/denial review found no blocker.
- Recommendation: review a narrowly worded hold release for this candidate;
  keep historical #1360 open until its causal/current-source exit criteria are
  met. Retaining the hold preserves the existing input-loss risk. Integration
  still requires new Main evidence and does not approve physical IME or release.

## 2. Held Thread diagnostics

- Decision: retain the hold or authorize observation-only integration of
  [#1367](https://github.com/NYGsatoshi/Coglatas/pull/1367) after a current-source
  review and exact-head qualification. The reviewed checkpoint is
  `70b38d1e60e5ce2ee395e9ac7d71f0c81cbbcef5`; refresh before any decision.
- Current evidence: retained Main `f31b15f2...` / run `40193736229`, attempt 1,
  has no fresh request
  and no final DOM/handler-dispatch evidence. Test-only [#1404](https://github.com/NYGsatoshi/Coglatas/pull/1404)
  characterizes possible response/DOM orders without proving the initiating cause.
  The held diagnostic adds bounded, fixed-boolean/provenance observations and
  preserves original errors, requests, deadlines and acceptance assertions.
- Recommendation: prioritize the observation-only decision so a future genuine
  occurrence can provide additional evidence. Retaining the hold leaves the
  historical interleaving unresolved. Neither option justifies a speculative
  production change, an old-response substitute, or a diagnostic rerun waiver.

## 3. Private transport and cross-session candidates

- Decision: keep independent review-only holds, or explicitly approve the scope
  of each test candidate after its own current-source review/CI. Do not infer a
  combined approval from GitHub Draft state or representative success.
- References: [#1326](https://github.com/NYGsatoshi/Coglatas/pull/1326),
  [#1399](https://github.com/NYGsatoshi/Coglatas/pull/1399), new bounded
  [#1406](https://github.com/NYGsatoshi/Coglatas/pull/1406), and cross-session
  [#1320](https://github.com/NYGsatoshi/Coglatas/pull/1320). Each is a separate
  source; later tests do not supersede held parents or qualify the full catalog.
- Recommendation: preserve holds while reviewing the residual matrix. Existing
  real controls already cover representative expiry, role downgrade, Origin,
  Tenant/resource isolation and event families. Complete approved mapping,
  current trusted Main/deep evidence and school-target acceptance remain missing.
  Test integration cannot activate RLS or approve pilot exposure.

## 4. Formal security contracts and target authority

- Current approved behavior: no unapproved formal mapping/RLS/role/policy
  activation, no assumed Staging access, no public endpoint or school-data intake.
- Required decisions: approve exact private mapping and role/grant/policy
  revisions/digests separately from runtime activation; identify the exact
  authorized isolated Staging target, HTTPS boundary and named technical operator.
  Authorized reviewers must inspect private material in Coglatas-Spec. No formal
  approval digest is invented in this public packet.
- Alternatives: keep HOLD BOTH, or approve one precisely identified preparatory
  contract/target boundary while retaining the other hold and separate activation
  gates. Recommendation: approve only source/digest-bound scopes that have been
  reviewed; retain all unqualified activation, deployment and release boundaries.
- Public trackers: [#1144](https://github.com/NYGsatoshi/Coglatas/issues/1144),
  [#1148](https://github.com/NYGsatoshi/Coglatas/issues/1148),
  [#481](https://github.com/NYGsatoshi/Coglatas/issues/481).

## 5. Recovery, physical acceptance and school admission

- Recovery decision: retain the current strategy boundary or formally adopt a
  reviewed physical recovery alternative in held
  [#1379](https://github.com/NYGsatoshi/Coglatas/pull/1379). Its bounded synthetic
  receipt is not current target restore/rollback, off-host retention, full
  Audit/Files recovery or formal adoption. Recommendation: keep the hold until
  those target-specific requirements are verified under
  [#1355](https://github.com/NYGsatoshi/Coglatas/issues/1355).
- Physical acceptance requires an authorized tester/device session for keyboard,
  accessibility and Japanese IME; Chromium emulation and synthetic composition
  cannot approve it. Historical #1342/#1346 remain open.
- School admission requires the actual school privacy/data authority and named
  operator approval. The alternatives are continued synthetic-only verification
  or a separately authorized restricted pilot after every mandatory gate passes.
  Recommendation: continue synthetic-only work; no actual student invitations,
  public endpoint, production security activation or school data is authorized.

Private specification Main reviewed for this work:
`6a0491c925b4980ce737dfbdf775816c237e44fe`. This identifies a source checkpoint,
not approval of Draft mappings or future contract revisions. Every later
decision must identify its actual authority, immutable scope and digest.
