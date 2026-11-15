# Independent fixed Model trace profile qualification — P0 #1962

This Security-owned candidate independently verifies the **local** fixed
simulation recording and trace profiler Core that ProjectIDE Product PR #1963
integrated. It does not add any new execution, observe live operations, assess
people or Resources, authorize access, or certify Native/Avalonia UI.

## Immutable source and test boundaries

- Actual immutable Product HEAD:
  `edebdc42b88bf7cd6d441bc9128cc12e0ed5234f`,
  containing `ProjectSimulationTraceProfile.cs` Git blob
  `754a509692cbdd4e3024233b82f4baf156797cff`
  and the already merged profiler implementation.
- Exactly **twelve** independently authored C# xUnit `[Fact]` cases from
  `SecuritySimulationTraceProfileCandidateTests.cs` Git blob
  `680b7da997b4d9abee16536b7915df749d293972`.
- Composition script `compose_and_verify.py` Git blob
  `a9a6620369ed781e7b49af6b64a30125fbe762aa`.
  It checks the exact original Test Git blob/index and actual working-tree
  bytes both before/after compilation, rejects unrelated Product tracked
  changes, uses a detached immutable Product worktree and retains the source,
  Product/composed tree, SDK and actual compiled TRX/discovery evidence.
- Product `global.json` pins SDK `10.0.403` without roll-forward.
  The read-only nonrequired advisory uses pinned GitHub Actions, the literal
  `ubuntu-24.04` and `windows-latest` runners, and no persisted checkout
  credentials. Both hosts must prove exactly 12 unique native compiled
  passing TRX results, 0 fail/skip/not-executed, and exact discovery parity
  before a PASS may be claimed.

## Independent coverage

1. Ordinary fixed simulated recording separates union busy ticks from
   per-Task integrals and unattributed idle time.
2. Explicit overlapping Task intervals count once for busy union and once
   per Task for the independent integral.
3. Changing a virtual start offset does not create extra utilization or
   misleading idle attribution.
4. Zero-duration start/end transitions maintain the engine's event ordering
   and zero duration without losing peak concurrency.
5. Exact `BigInteger` Task integral exceeds Int64 without rounding, while
   the union clock retains Int64 virtual tick bounds.
6. A bounded prefix keeps open Tasks open, retains its last committed clock,
   and cannot masquerade as a completed run.
7. A smaller query budget refuses the whole metric summary instead of
   truncating an existing complete recording.
8. Wrong recording digest or unsupported engine refuses every basis and
   metric field, not merely the final UI verdict.
9. Stale input and cancelled queries do not disclose retained metrics or
   rewrite the fixed recording's canonical input bytes.
10. The profile keeps `SimulatedModel`, `NotRun`, `NotEvaluated`,
    `Unattributed` and `FixedRecording` metadata, never operational or
    permission-bearing semantics.
11. Returned canonical bytes are independent and culture-invariant.
12. Windows pure ViewModel withdrawal/re-admission cannot reuse a previous
    successful profile when its expected recording is wrong or the run is no
    longer currently owned.

## Actual authority and trust boundaries

This is only **synthetic local Core and Windows pure ViewModel** qualification.
The underlying model trace is not measured elapsed wall time, operational
observations, causal waiting attribution, Resource demand, staff performance,
predictive accuracy, a trusted evidence producer, an executed requirement
suite, an access grant, or eligibility for Release.

A real server-side profile query would require *current* actor/session,
Tenant/Project/Branch scope, capability checks, revocation, private or small
group contribution suppression, retention/redaction, bounded export and
audit. None are provided or approved by these tests. No new persistency,
actual Angular/Avalonia UI, remote trace reader, observed-data adapter,
Trust/Eligibility, Native/Production/Release activation or policy changes
are requested. The underlying issue #1962 remains OPEN for those separate
qualifications. Parent #1033 remains OPEN; #1144 BLOCKED, #1128
SUSPENDED/NOT_EVALUATED and Release NO_GO.

All original receipts explicitly have
`requiredCheckQualified=false`, `independentReviewComplete=false`,
`nativeUiQualified=false`, `productionQualified=false`,
`trustQualified=false`, `releaseQualified=false`, even after a local
candidate PASS. Supplemental advisory PASS cannot replace the six original
app15368 Required Check contexts, independent formal source review, or
Security Owner governance. Historical Product-authored tests are not
independent Security approval.

### Local reproduction from an immutable Security Test HEAD

```text
python -B tests/candidates/projectide-trace-profile/compose_and_verify.py --product-head edebdc42b88bf7cd6d441bc9128cc12e0ed5234f --worktree <fresh-product-worktree> --evidence <fresh-evidence-directory>
```
