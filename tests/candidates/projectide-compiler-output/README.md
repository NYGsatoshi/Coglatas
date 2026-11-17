# P0 #1966 — independent Compiler output snapshot lifecycle candidate

This Security-owned candidate independently tests the **already integrated**
local `ProjectCompilerOutputSnapshot` and pure `SyntheticWorkbenchViewModel`
against an immutable ProjectIDE Product. The new lifecycle fixtures compile
their own synthetic input, accept the genuine host response and exercise
snapshot queries on that accepted compilation through public APIs.

## Exact Product/Test composition

- Pinned Product commit `d570a6727b5e5af5c8902c1bf2d0575c66c29143`,
  tree `c00aac4581a7aecfe1e13b5fba89275ff6373938`, from ProjectIDE
  integration. Qualification applies to this exact source, without adopting
  later regional host changes.
- Product output wrapper blob `af304c2dec8694bd68a388ded3381c740516acec`
  and ViewModel blob `6ab45725d5694aa9f36849f7e87a0e57e069534f`.
- Independent Test C# blob `b13bc41f398ca0877d1d9505a2899fd7d835b657`,
  **35 native xUnit facts: 11 original Core facts plus 24 new lifecycle
  facts**. The original eleven method identities, bodies and helpers remain
  byte-for-byte intact from Test blob
  `a7832c98fcded823532b974eee11b74168ddfbf2`.
- Independent composition runner `1bf29bec7e2094b033b5d1057d5d9de77f9a8bc6`
  (current Security Test-owner runner model). The runner stages **only** the
  original exact Test blob into a detached, clean Product worktree, verifies
  the Git index identity, actual source bytes before and after execution and
  denies unrelated tracked Product file changes. Product and Test source
  remain separate.
- Product `global.json` pins .NET 10.0.403 and roll-forward disabled;
  the nonrequired Windows-latest and Ubuntu-24.04 advisory checks SDK/source
  pinning and requires **35/35 actual compiled xUnit PASS**, 0 failed,
  skipped or not executed on each host.
- The runner freezes the exact fully qualified native roster. Original TRX
  result names must equal that roster; result, definition and entry IDs must
  form a unique bijection with matching raw names. Compiled discovery must
  contain the same 35 distinct identities. Count-only, duplicated, substituted,
  missing or non-Passed cases cannot qualify.
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

## Twenty-four real ViewModel lifecycle controls

The new fixtures independently construct a committed one-task Source and
matching compiler profile. Each requires a real completed
`LocalNetCompilerHost` response, the accepted original Result, its declared
publication and actual execution plan. Positive body queries require the
`CapturedAvailableOutputs` state and all three real output roles.

| Control group | Facts | Required observation |
| --- | ---: | --- |
| Explicit admission and rejected/invalid queries | 2 | Admission is required; a new query withdraws the previous view while retaining the real compiler Result and plan. |
| Reset, disposal, newer request and scoped readmission | 4 | Reset requires a newer generation and readmission; disposal rejects later queries; two genuine same-content responses retain distinct generation ownership; scoped readmission clears the current view and requires another query. |
| Lifecycle reentry at both notifications | 8 | Reset, disposal, genuinely changed input and scoped readmission at the initial and published notifications prevent the interrupted query from republishing old bodies. |
| Cancellation with a newer admitted or refused query | 4 | Cancellation at each notification throws while leaving the exact newer captured or `RefusedWrongResult` snapshot visible. |
| Cancellation without a replacement | 1 | Both notification boundaries withdraw the interrupted snapshot without erasing the caller-held prior bytes or changing the original Result and plan. |
| Newer query ownership without cancellation | 4 | A same-context admitted or refused query at each notification keeps its exact object; the interrupted outer query returns false. |
| Pre-cancelled query | 1 | An already-cancelled ViewModel query withdraws the prior visible snapshot before throwing, while returned bytes, Result and plan remain intact. |

Every reentry hook must fire and is armed once before its nested action.
Refusal controls require the actual non-null refused snapshot with no bodies,
input/result/context binding or serialized source/result details. Retained
caller-held snapshots are compared by their original digest and exact
canonical data/body bytes across lifecycle changes.

This is a **bounded local Core and pure ViewModel** qualification, not an admission test of
actual Tenant/actor/session permissions, persistent body storage, cross-project
snapshots, private SourceMap redaction, current Query/Open/export authority,
durable TTL/Pin, Native/Avalonia UI, or Release. A caller-held SourceJson
object may remain valid after a ViewModel clears its own retained reference;
view withdrawal is not physical erasure. Running these pure .NET controls on
Windows does not qualify an actual Windows/Avalonia UI or native runtime.
Regional collector/host coverage remains outside this lifecycle slice.

## Admission and open work

Every receipt must set `requiredCheckQualified`,
`independentReviewComplete`, `nativeUiQualified`,
`productionQualified`, `trustQualified` and `releaseQualified`
to `false` regardless of actual xUnit success.

Do **not** mark this candidate qualified before native GitHub CI succeeds on
its exact HEAD, each original artifact/file hash is verified and the selected
raw native test identities agree across hosts. The source blobs and composed
tree must match across hosts; per-host ZIP/TRX hashes and native execution IDs
may differ. Local source and parser checks do not establish a C# build
or native execution. A nonrequired source advisory cannot replace the six
original required app15368 checks, formal independent current-head review or
Security Owner adoption. The remaining #1966 authorization, redaction,
persistence, hold and runtime-specific acceptance checks remain OPEN.
#1144 is BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, Release NO_GO.

Repeat on a fresh checkout with the immutable Product commit available:

```text
python -B tests/candidates/projectide-compiler-output/compose_and_verify.py --product-head d570a6727b5e5af5c8902c1bf2d0575c66c29143 --worktree <fresh-product-path> --evidence <fresh-evidence-path>
```
