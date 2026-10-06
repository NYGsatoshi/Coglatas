# Prospective environment-assignment transition decision proposal

**DRAFT_DECISION_PENDING — not active campaign policy.**

Decision owner: repository owner under the current owner-only CODEOWNERS model.
Decision issue: https://github.com/NYGsatoshi/Coglatas/issues/1105.

This proposal authorizes no campaign. The current schema, validators and capture
workflow are unchanged. Owner approval of this precise rule and a reviewed,
qualified enforcement rollout to Main are required before a successor declaration.
A subsequent declaration requires its own new exact-ID/digest premeasurement authorization.

## Authenticated Medium environment mismatch and governance gap

The failed first-attempt Main-push campaign `perf05-medium-20261006-main32-intel8370c-publicdigest`
(run [37457509298/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/37457509298),
manifest `0bc9a6442350949d0c5e4b2c46058f9e178d6de4be6e5d70327e3e7083100bf5`,
source `32a17bde8f7f21ab8670265ae75d21ed081e27b9`) has exhausted all three declared serial groups.
Capture, exact-source build restore and both evidence uploads succeeded. Only selection failed.

Independent actual-ZIP authentication and full immutable-source comparator replay match every retained decision:
**BASELINE_UNAVAILABLE / ENVIRONMENT_MISMATCH**. Each group is complete, structural 28/28 PASS,
all nine streams stable with MAD <= 0.20, but eligible=false with the sole reason `wrong-environment`.
Declared environment `e9c07b1d9fcc3bf7cdc9dd282f44c138d57ce0a0e14ab82db7ca1e5d4ea57443`
(Intel Xeon Platinum 8370C); every actual Medium fingerprint is
`e3fd3025c8dd91dc45f3447350915f978ee1e9e6555264a5456b65b4896a0095`
(AMD EPYC 9V45 96-Core Processor).
Maximum MAD by group is 0.10105736023934894 / 0.14368953443813912 / 0.107396618077116.
All 990 raw sample records are retained byte for byte in the four original archive files under
`performance/baseline-campaigns/evidence/perf05-medium-20261006-main32-intel8370c-publicdigest/`. This is not instability, query/structural regression or measurement failure.

Raw artifact 11411325219, ZIP SHA-256 `7726ef3617a2d1452b977ce95ff51cf3a31ceef60315a97eded8ec7a1efd1bdf`;
metadata artifact 11411415134, ZIP SHA-256 `c6b94255290193cbad401be2f970db4cc396e0829293cfeb346115d063d8b6bf`.
Both actual downloads match fresh GitHub metadata. The failed Intel manifest and all three ineligible groups stay immutable.

## Current policy finding

[BASELINE_CAMPAIGNS.md](https://github.com/NYGsatoshi/Coglatas/blob/7a405c9f3c584ce619710ac585ea364d6b7ef0a5/performance/BASELINE_CAMPAIGNS.md)
lines 65–69 permit declaring known runtime metadata, reject foreign groups, and forbid rerunning after mismatch.
Lines 94–102 forbid avoiding failure by another ID; same-scope successors require a substantive
forward product/measurement correction and different reviewed source.
The #1073 design and #606 history preserve these constraints.
The manifest schema only permits initial-governance-campaign / accepted-product-change / measurement-correction;
registry tests reject exhausted same-scope resets and source-equivalent retries.
The registry's lack of cross-scope predecessor enforcement is not permission to evade the policy.
Earlier #1078/#1101 declarations concerned new scopes before campaign capture; they do not authorize replacement
after an exhausted measured campaign. Current owner decisions approve only the prior exact declarations.
#1103 changes CODEOWNERS, not campaign causes or postfailure authorization.

Therefore the current policy does **not clearly authorize** a successor based on this failed campaign's observed assignment.
No successor is declared or measured.

## Proposed decision: one bounded prospective environment-assignment transition

Require an explicit owner decision on this narrow rule before its use and a separately reviewed enforcement rollout.
The proposed rule permits using only authenticated assignment/fingerprint metadata from one naturally assigned
exhausted campaign to define a prospective scope; it never enrolls its historical measurements.

- Retain/authenticate the failed manifest, declaration, full ordered groups, all samples and result before the decision.
- Identify the predecessor explicitly; require the normal workflow's first Main-push attempt and all predeclared groups.
- Require one identical actual target-profile full environment compatibility digest across all groups, materially different from the declared one.
  Ambiguous assignments require an owner policy decision, not host searching or choosing a subset.
- Select the successor scope from the full metadata identity under the unchanged comparator. Timing values, MAD,
  relative performance and which groups would pass must not determine eligibility for the transition or its target.
  The rule must treat stable and unstable duration observations alike.
- Permit one transition per independently justified engineering epoch. A failed transition cannot chain into another
  environment transition, cycle to a previous scope, add groups or reset the bound. Any later same-scope campaign
  still requires the existing substantive correction/different-source rules.
  An epoch begins at an initial registered declaration or a legitimate substantive product/measurement correction
  under the existing policy. An environment transition is one direct successor within that epoch; a new ID or
  observed CPU change cannot create another epoch. Previously registered scopes cannot be revisited by this rule.
- Preserve the same reviewed measured source, fixture, runtime/tool/comparator/contract identities, scenarios,
  five ordered samples, MAD <= 0.20, maximum three serial groups and earliest eligible stable complete selection.
  Environment metadata does not justify product/methodology changes.
- Add an explicit environment-assignment-transition cause with cross-scope predecessor binding and negative tests;
  do not masquerade as initial-governance-campaign or alter an existing manifest.
- Only after this rule and its enforcement are reviewed and on Main, create a new fixed campaign ID/canonical digest,
  fresh fixed authorization reference and expiry. Obtain exact-ID/digest explicit owner **premeasurement** approval.
  Prior approval does not transfer. Run exactly one normal bounded campaign; all new wrong-environment/unstable/
  partial/failed groups stay retained and ineligible as applicable.
- No favorable-host selection, retries until a CPU appears, ordinary/historical enrollment, result/sample filtering,
  environment-digest rewriting, expired-declaration extension, threshold relaxation or candidate-self baseline.
- Successful fresh capture remains unapproved and needs a separate evidence/baseline PR and actual evidence approval.

## Acceptance

- Owner approves or rejects the precise transition rule; record the real decision, without self-authored authorization.
- Enforce the approved rule prospectively with tests for cross-key reset, mixed assignments, timing-independence,
  historical enrollment, missing/altered predecessor evidence, chained/cyclic transitions, repeated IDs and bound changes.
- Preserve the entire Intel failure permanently; it never becomes a passing campaign.
- A separately authorized new campaign is required before any Medium baseline proposal.

This issue does not approve a successor CPU or campaign. The observed AMD family is evidence, not automatic authorization.
#606/#1056 stay Open, #1046 stays Draft. No Avalonia/ProjectIDE implementation.
