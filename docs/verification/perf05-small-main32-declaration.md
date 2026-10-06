# Proposed small Main32 public-digest declaration

Do not merge into the schema topic branch. First independently review and merge
#1099 to Main, then retarget this Draft declaration to Main. Registration and
capture also require the fixed independent human authorization below.

- Campaign: `perf05-small-20261006-main32-amd7763-publicdigest`.
- Canonical manifest SHA-256:
  `713fe0b1b4ac34f1069c66b6355f6384d31ab0bd3230bef1d50224bd6726761e`.
- Earlier approved source: `32a17bde8f7f21ab8670265ae75d21ed081e27b9`.
- Created: `2026-10-06T05:24:52Z`; expires: `2026-10-08T05:24:52Z`.
- Fixed authorization allocation:
  https://github.com/NYGsatoshi/Coglatas/issues/606#issuecomment-6009942401
  is NOT approval. The executor has not posted approval.

## Premeasurement metadata provenance

Use the ordinarily assigned Main PERF-02 small job, not a search for hardware:
Main `37414550908/1`, artifact `11390213450`, archive SHA-256
`9611c2f068f8cef7521e0c8b0f905e02c3bb7e26aa61c7b84a0e20e762fdb833`.
It contains production runtime/environment, fixture, preflight and warmup JSON;
there is no benchmark result or ordered DB measurement group in that archive.
Its actual CPU is AMD EPYC 7763. No capture outcome selected this host.

That general environment smoke uses fixture version 1. Its observed fingerprint
is retained unchanged. This declaration prescribes DB fixture version 2 from
the source-pinned DB contract; the deterministic planned hash is computed from
that source's datasets.json. Applying only the prescribed fixture identity to
the observed production runtime payload produces expected compatibility digest
`6073dae0f06d232beca0b634d4bffedca4dcb1897634d246e19c26c3a779e8ce`.
This is a prospective compatibility target, not fabricated observed evidence.
Actual campaign groups must independently match this exact environment and
fixture after collection; incompatible or partial groups are retained and
ineligible. The source-pinned comparator/contract/tool hashes remain exact Git
bytes, rather than workstation line-ending representations.

## Supersession and unchanged bounds

This proposed declaration supersedes unmerged #1095's publication-invalid
small declaration for the old AMD9V74 environment. Preserve that original
branch history, ID, digest, expiry and failure. No capture existed for it, so
there is no failed, partial or rejected measurement group to rescue. It was
never registered on Main. The new profile/environment scope is initially
declared; this does not reset an exhausted registered scope.

Keep nine ordered scenarios, five ordered samples, MAD <= 0.20, maximum three
serial groups, earlyStopPolicy never and earliest eligible stable complete
selection. No additional capture, favorable-host selection, baseline promotion,
historical enrollment, independent approval or product implementation occurs in
this PR. Capture remains disabled until source policy rollout, valid publication
and security, fixed human authorization and unexpired Main declaration all hold.
