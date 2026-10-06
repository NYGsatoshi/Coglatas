# Retained rejected Intel Medium campaign

Classification: **ENVIRONMENT_MISMATCH**. The retained campaign decision is
`BASELINE_UNAVAILABLE`; all three groups stay rejected/ineligible.

The four original evidence files are copied byte for byte from artifact
`11411325219`, ZIP SHA-256
`7726ef3617a2d1452b977ce95ff51cf3a31ceef60315a97eded8ec7a1efd1bdf`.
Archive metadata is independently authenticated from artifact `11411415134`,
ZIP SHA-256 `c6b94255290193cbad401be2f970db4cc396e0829293cfeb346115d063d8b6bf`.

Original campaign: `perf05-medium-20261006-main32-intel8370c-publicdigest`;
manifest digest: `0bc9a6442350949d0c5e4b2c46058f9e178d6de4be6e5d70327e3e7083100bf5`;
source: `32a17bde8f7f21ab8670265ae75d21ed081e27b9`;
declaration: `b03b0be86d086ecefad38760479037f11d5de079`;
first-attempt Main-push run: https://github.com/NYGsatoshi/Coglatas/actions/runs/37457509298.
The fixed owner premeasurement authorization was last edited at `2026-10-06T11:06:46Z`,
before run creation at `2026-10-06T11:36:18Z`:
https://github.com/NYGsatoshi/Coglatas/issues/606#issuecomment-6010339834.

All capture/restore/archive steps succeeded. Only the selection step failed.
Each group is complete, structural 28/28 PASS and all nine canonical duration
streams stable, but `eligible=false` with sole reason `wrong-environment`.
Declared target: Intel Xeon Platinum 8370C, full environment digest
`e9c07b1d9fcc3bf7cdc9dd282f44c138d57ce0a0e14ab82db7ca1e5d4ea57443`.
Actual target-profile fingerprint in all three naturally assigned groups:
AMD EPYC 9V45 96-Core Processor, full digest
`e3fd3025c8dd91dc45f3447350915f978ee1e9e6555264a5456b65b4896a0095`.
The independent replay report retains every structural result, MAD,
fingerprint/fixture/tool identity and canonical sample array.
All 990 raw sample records across both captured profiles, page sizes and pages
are preserved in `raw-groups.json`.

The actual ZIP digests match fresh GitHub metadata. Full immutable-source
comparator replay equals the original aggregate and each group decision.
Separate median/MAD calculations match all streams and earliest selection is
correctly absent. These observations do not approve an AMD scope or authorize
another measurement. No Medium baseline document or approval is generated.

This failed campaign consumed its declared maximum of three groups. No retry,
expiry change, target rewrite, historical enrollment, replacement group or rescue
is permitted. The prospective policy gap and proposed owner decision are tracked
in https://github.com/NYGsatoshi/Coglatas/issues/1105 and
`performance/ENVIRONMENT_ASSIGNMENT_TRANSITION_PROPOSAL.md`.
Current policy remains unchanged until a legitimate approved rollout.
