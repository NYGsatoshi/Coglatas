# Main Qodana MergeIntoPattern regression — 2026-10-24

Status: **NARROW SOURCE REPAIR / CURRENT PR CI NOT YET QUALIFIED / RELEASE NO_GO**.

## Exact failing source

Product Main `48cf0b397122fb3cadcf18926a1891e7bbf80a5d`,
[Main CI run 40073017091](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091),
attempt 1, [Qodana Cloud job 120811125250](https://github.com/NYGsatoshi/Coglatas/actions/runs/40073017091/job/120811125250)
failed **Guard Qodana findings and project model**. The stored original
`qodana-cloud-full-inventory` artifact ID `12552367796` reports
`MergeIntoPattern` current **135** against unchanged baseline **132**
(delta +3). This is a quality ratchet failure, not evidence of a product
authorization bypass. The original Main run and failure must remain
unmodified and independently terminal-qualified.

Three retained source-bound raw SARIF inventories, compared by
`(path, startLine, ruleId, equalIndicator/v1)` **with duplicate multiplicity
preserved**, show:

| Full Qodana Cloud source | Count | Difference |
| --- | ---: | --- |
| Earlier Main `0c5f9baace186940f948758b1731fd3ec891c2f3`, run `40056196940` | 132 | Baseline |
| Main `0dd39000b38dd047e0a35513e2b7403d3bc5cd06`, run `40068087572` | 133 | +1 at `FileSelectionRetainedCredentialHostedHttpTests.cs:69` |
| Main `48cf0b397122fb3cadcf18926a1891e7bbf80a5d`, run `40073017091` | 135 | +2 at `FileSharingReadOnlyMutationHostedHttpTests.cs:67` (same source site counted twice in the full inventory) |

Other `MergeIntoPattern` source/location records in the compared inventories
are unchanged. This finding count is from Qodana Cloud, **not** a claim
that Community or all separate inspections succeeded. The full original
artifacts are not copied into Product source or this public packet.

## Minimal proposed change

1. For retained selection, use the equivalent C# property pattern
   `snapshot is { ConsumedAt: not null, ConsumptionVersion: 1 }`, keeping
   the same consumed-at/nonzero-version requirement and the subsequent
   PostgreSQL audit query entirely unchanged.
2. For the ReadOnly sharing projection, require the same five authorization
   redaction invariants with one recursive property pattern and keep the
   **exact** previous sharing-version equality as a separate xUnit assertion.

The intent is exactly the original fail-closed acceptance oracle; there
are **no removals of assertions, test cases, expected HTTP statuses,
negative controls, backend code or quality budgets**. No API, tenant scope,
RLS, grant, FileSharing upload policy, schema, migration, CI workflow or
timeout/retry/skip contract is changed.

## Qualification gates

- [ ] Exact combined-head PR CI and independent source/diagnostic review.
- [ ] Verify real PostgreSQL/HTTP selection and sharing facts execute and
      pass (no skipped, zero-duration or mock-only credit).
- [ ] Re-run both full Qodana consumers on an authorized resulting Main,
      show that the `MergeIntoPattern` count is **at most 132** with the
      unchanged budget. Passing a PR fast-only inspection does not qualify
      the full Main Cloud/Community ratchet.
- [ ] Check current-Main Functional Full/Fast and licensed P0 results
      separately; retain any initial failure, retry and exposed release hold.

The separate pilot SEC-16/17 [Draft #1381](https://github.com/NYGsatoshi/Coglatas/pull/1381)
uses test-only candidate logic, not this quality fix. Both approved
environment and detailed SEC-ARCH/RLS **Owner HOLDs** remain.
No production target access, merge authorization for held PRs,
student admission or `RELEASE_READY` is implied.
