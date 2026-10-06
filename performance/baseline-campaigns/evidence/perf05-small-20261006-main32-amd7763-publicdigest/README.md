# Small Main32 baseline evidence review

Status: unapproved baseline candidate; separate human evidence approval is pending.

The original Small campaign is `perf05-small-20261006-main32-amd7763-publicdigest`.
The canonical manifest digest is `713fe0b1b4ac34f1069c66b6355f6384d31ab0bd3230bef1d50224bd6726761e`.
The fixed premeasurement owner authorization was last edited at
`2026-10-06T11:06:32Z`, before capture run creation at `2026-10-06T11:32:17Z`:
https://github.com/NYGsatoshi/Coglatas/issues/606#issuecomment-6009942401.
This authorization covers measurement, not baseline promotion.

## Authenticated retained evidence

The first-attempt Main push run https://github.com/NYGsatoshi/Coglatas/actions/runs/37457063454
completed successfully at declaration `a1e2b57a9eb04c30749556ddb61ac89406239895`,
measuring earlier Main source `32a17bde8f7f21ab8670265ae75d21ed081e27b9`.
The four original campaign JSON files are copied byte for byte from the original ZIP.

- Raw artifact: `11410011669`; ZIP SHA-256 `f1cfc663ab45f46ea9220a55d2619ff7cebfb5409c0feb70464475855478b25e`.
- Archive metadata artifact: `11410106603`; ZIP SHA-256 `879f125f7312b914fb716662982668f0f12a909b4689c43dc1de42d062e4b062`.
- Exact target and observed environment digest: `6073dae0f06d232beca0b634d4bffedca4dcb1897634d246e19c26c3a779e8ce`.
- Observed CPU in every group: AMD EPYC 7763 64-Core Processor.
- Fixed fixture identity, runtime versions, comparator and instrument digests are
  preserved in the original manifest, full fingerprints and independent replay report.

The local independent verification authenticated both actual ZIP digests against
fresh GitHub metadata, bound authorization chronology, source and declaration
to Main history, verified every individual group against the complete aggregate,
and replayed all groups with the immutable source contracts and comparator.
A separate median/MAD calculation exactly matches every canonical stream.
No benchmark measurement was executed by this review.

All three serial groups are complete and eligible. Each has 28/28 structural
PASS, all nine canonical streams stable with five original ordered samples each,
and 165 raw sample records for each profile, including page-10 and second-page
records. All 990 raw sample records are preserved. Maximum relative MAD by group:
`0.15910136301690778`, `0.12188548764364884`, `0.10626430004699872`.
Earliest-eligible-stable-complete selection is group 1, exactly matching the
retained `BASELINE_CANDIDATE` result. Groups 2 and 3 remain retained and are
never substituted for group 1.

## Proposed promotion and honest approval boundary

The nine documents in `proposed-baselines/` are the unchanged canonical
`baseline_documents(..., approved=False)` output. They are outside the active
baseline catalog and do not grant duration acceptance. The approval ledger remains
unchanged: no human approval or approval timestamp is invented.

After actual separate evidence approval under the current owner-only CODEOWNERS
model, record its real timestamp in the policy approval ledger and move the same
nine documents to
`performance/baselines/db/small/6073dae0f06d232beca0b634d4bffedca4dcb1897634d246e19c26c3a779e8ce/`,
using the reviewed `approved=True` promotion. Reauthenticate the original live
artifact and run the approval validator before normal merge. Its fixed-reference
field remains governed by the current policy; the premeasurement comment alone
must not be treated as separate postcapture baseline acceptance.

Repository ownership is the sole owner recorded in current CODEOWNERS. Evidence
independence here means reproducible artifact/provenance/comparator verification.
No separate human review, external CODEOWNER approval or baseline acceptance is
claimed. Policy wording requiring an independently reviewed promotion remains
unchanged and must be satisfied or explicitly superseded by an owner decision.

This PR contains only the Small campaign's evidence and proposed baseline
documents. The other profile captured in the same Small campaign is retained
only because the campaign contract requires full structural evidence; it is not
a Medium baseline. No Medium failed-campaign archive is included.
