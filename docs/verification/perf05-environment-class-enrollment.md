# PERF-05 preserved evidence enrollment

Policy repair #1115 reached Main `746286446c978bdcbd220393d70aff6839dd1b3d`.
This enrollment uses existing first-attempt GitHub archives, not a new capture.
The measured source is `32a17bde8f7f21ab8670265ae75d21ed081e27b9`.
The active hard class is GitHub-hosted standard / Ubuntu 24.04 / x86_64 /
four vCPUs / 16 GiB, including the pinned runtime, compiler, workload,
configuration, implementation and schema attributes in each document.

| Profile | Original run / attempt | Artifact | ZIP SHA-256 | Class digest |
| --- | --- | --- | --- | --- |
| Small | 37457063454/1 | 11410011669 | f1cfc663ab45f46ea9220a55d2619ff7cebfb5409c0feb70464475855478b25e | df0e3ea41a8b8506f330524c3a92b1c3abde3cc82457996111ff5d39e0368295 |
| Medium | 37626135166/1 | 11483899746 | aab12efb2a88adc48cfde4a759e4cd339f8d382a101b9233a49a4985da7bd7f6 | 418ceffb78e2573202017319008fc493571cc1c3158bd28662b3937178dcb45c |

Both profiles retain all three serial groups, structural 28/28, nine ordered
streams and five ordered samples per stream. Every stream meets relative MAD
<= 0.20. Group 1 is the earliest eligible stable complete group; there are no
prior rejected groups under the new policy. Groups 2 and 3 remain immutable
evidence and are not pooled with group 1. The Medium archive retains all 990
raw captures across its original Small and Medium profiles.

Small preserves the original owner approval at `2026-10-06T12:55:30Z`, selected
group, samples, source, artifact and historical approved catalog. Its hardware
fingerprint remains AMD EPYC 7763. Medium preserves AMD EPYC 9V74, exact observed
memory/kernel/image and the complete original fingerprints. Missing historical
microcode or host observations remain null. The historical 9V45 target is retained.

Medium's original workflow failure and `BASELINE_UNAVAILABLE` result are unchanged.
The separate class qualification is `BASELINE_CANDIDATE`, with digest
`1ef32cae83aa170ecc570792713f792a6f1fe58fe3ee66f2a6a1214808f177b2`.
The owner explicitly authorized conditional promotion in the current Codex
session when class, structural, stream, MAD, integrity, schema and sample/group
criteria pass. The evidence-bound executor record is
https://github.com/NYGsatoshi/Coglatas/pull/1116#issuecomment-6040649801.
It is not a native approving review or an independent human review.

Proposal commit `428db62c92246227c3d181e9bd7ccb4269244ea7` retained eighteen
`approved:false` documents outside the active catalog. Promotion adds exactly
nine `approved:true` documents per profile and immutable records in
`performance/environment-class-baselines.json`; samples and group selection
are identical to the proposals. The live validator authenticates original
archives and run attempts, replays the pinned comparator, verifies all digests,
regenerates canonical documents and checks authorization provenance.

Enrollment approval alone does not complete #606 or #1056. #1046 integration
and fresh exact-current-Main API, structural, duration and protected acceptance
remain required. The old #1046 77/78 plus one UNSTABLE cohort remains rejected.
