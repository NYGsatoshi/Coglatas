# P0 #1966 — independent Compiler output snapshot Core candidate

This Security-owned candidate independently tests the **already integrated**
local `ProjectCompilerOutputSnapshot` against an immutable ProjectIDE Product.
It is not the Product's original test source, a compiler rerun, or an API.

## Exact Product/Test composition

- Pinned Product commit `510986d31a9bb111eee02779b6a8efb88b03c257`,
  ProjectIDE integration, with the existing Compiler snapshot implementation.
- Independent Test C# blob `a7832c98fcded823532b974eee11b74168ddfbf2`,
  eleven separately authored native xUnit facts.
- Independent composition runner `6462aee5866215997585e7259aacd6e7541b400f`
  (current Security Test-owner runner model). The runner stages **only** the
  original exact Test blob into a detached, clean Product worktree, verifies
  the Git index identity, actual source bytes before and after execution and
  denies unrelated tracked Product file changes. Product and Test source
  remain separate.
- Product `global.json` pins .NET 10.0.403 and roll-forward disabled;
  the nonrequired Windows-latest and Ubuntu-24.04 advisory checks SDK/source
  pinning and requires **11/11 actual compiled xUnit PASS**, 0 failed,
  skipped or not executed on each host.
- Original TRX, compiled discovery, source blobs, Product and composed tree,
  logs and SHA256 receipts are retained as separate hosted artifacts.

## Eleven real Core controls

1. Default Manifest produces **no original body** while retaining the exact
   original input/Source/context, result/execution/verdict axes, output
   references and canonical digest.
2. AvailableOutputs captures only the three actually published semantic model,
   IR and SourceMap objects with same result/source binding and exact byte sum.
3. A mismatched expected Result digest refuses the request without exposing
   Source identity, input/context, output count or result summary.
4. A body budget equal to the real UTF-8 byte count admits the three bodies;
   one byte less discards **all** captured bodies atomically, preserving
   original Result metadata and NotCaptured flags.
5. Public output dictionary, Keys, Values, generic/legacy interfaces, copied
   entries, and nonpersistent mutations must not expose owned mutable state.
6. Nested SourceJson reparse and copied canonical byte edits cannot change
   retained originals, data digest or body identity.
7. Current vs stale desired-input freshness must be separately reported, with
   the actual source/result axes and compiler result unchanged.
8. Legacy artifacts without a retained producer must not fabricate bodies.
9. Cancelled read queries must not publish or mutate the original compilation.
10. Cancelled compiler execution must not fabricate unrecorded intermediate
    states, complete verdicts or output bodies.
11. Unsupported mode/out-of-bound budgets and absent run identity or
    unadopted Trust/Eligibility/retention/authority flags never qualify.

This is a **bounded local Core** qualification, not an admission test of
actual Tenant/actor/session permissions, persistent body storage, cross-project
snapshots, private SourceMap redaction, current Query/Open/export authority,
durable TTL/Pin, Native/Avalonia UI, or Release. A caller-held SourceJson
object may remain valid after a ViewModel clears its own retained reference;
UI withdrawal is not physical erasure.

## Admission and open work

Every receipt must set `requiredCheckQualified`,
`independentReviewComplete`, `nativeUiQualified`,
`productionQualified`, `trustQualified` and `releaseQualified`
to `false` regardless of actual xUnit success.

Do **not** mark this candidate qualified before native GitHub CI succeeds on
its exact HEAD and the original ZIP/TRX/discovery/receipt case IDs and hashes
match across hosts. A nonrequired source advisory cannot replace the six
original required app15368 checks, formal independent current-head review or
Security Owner adoption. The remaining #1966 authorization, redaction,
persistence, hold and runtime-specific acceptance checks remain OPEN.
#1144 is BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, Release NO_GO.

Repeat on a fresh checkout with the immutable Product commit available:

```text
python -B tests/candidates/projectide-compiler-output/compose_and_verify.py --product-head 510986d31a9bb111eee02779b6a8efb88b03c257 --worktree <fresh-product-path> --evidence <fresh-evidence-path>
```
