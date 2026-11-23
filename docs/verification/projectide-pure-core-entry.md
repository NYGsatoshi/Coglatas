# Pure Core entry candidate

Authority: #888 IDE-START-01, #905, #793, #1034 and #798. This candidate adds
only pure, in-memory checks to existing provisional contracts. It is not formal
Reader adoption, a storage migration, a new artifact allocation, trust, eligibility
or operational integration. Product base: `3aebedb3d975b04070262751d4dc0a187e0df89b`.

## New guarantees

- Prototype decoding can compare an independently supplied full typed pin and
  reject mismatched content without publishing an artifact. Pre-cancelled and
  cancelled-after-parse Source/artifact reads expose no decoded object. Original
  bytes remain recoverable. Existing v1 domains, unknown preservation and hash
  vectors are unchanged.
- Import now requires the existing receipt completeness check to succeed before
  publishing. In particular, an empty interrupted enumeration is not a complete
  receipt. A new experimental `CGPC0001` frame reader accepts only existing
  supported artifact and Source bodies. It rejects truncation, trailing data,
  duplicate/conflicting entries and unsafe lengths before publication. Unknown
  framing/body semantics, missing closure, cancellation and processing exhaustion
  retain distinct outcomes. Invalid structure throws `FormatException` as the
  existing store does. Count/byte bounds are per-operation processing limits,
  not product capacity promises.
- Container origin is a bounded opaque caller label. Successful imports retain
  its exact original bytes, including noncanonical legacy Source formatting;
  reusing an origin for different bytes or a different root fails. Content and
  origin publication share the store lock. No file, path extraction, network,
  external body resolver or durable persistence is introduced. The framing is an
  experimental local candidate, not a formal interchange or artifact schema.
- Receipt assessment exposes execution and coverage claims, producer claim,
  provenance, run identity, structural completeness and desired-context freshness
  separately. Its evidence kind remains Missing and verdict Inconclusive, even
  for a complete receipt bearing a candidate trusted-producer label. Requirement
  assessment optionally compares an independently supplied desired run; absent
  and mismatched run identities remain unavailable and stale respectively.
- Optional source-entry checks bind an existing Requirement/mapping/verifier to
  exact candidate, Git blob identity, SHA-256, declaration and reviewed assertion
  byte range. Missing, skipped, stale, declaration-only and invalid states remain
  distinct. A located assertion reference is not semantic assertion verification
  or compiled discovery. Execution stays NOT_EXECUTED and approval UNVERIFIED.
  Existing SEC-ARCH validators, schema versions and pass criteria are unchanged.

## Verification ownership

Independent tests are prepared on a separate branch from
`SecurityTestImplimation` at `3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`.
The additional project is intentionally excluded from the ordinary solution:
it compiles against the exact isolated product candidate, not the older test
baseline. Existing integration tests remain intact. Draft #1750 at
`0d4d4bea6cd8bebed9165e43148c8eec98953fe1` supplies the existing Unicode/array
and opaque-future-type tests; those tests are reused, not rewritten here.
Record exact product/test/reused-test SHAs, composition commit/tree, discovery,
TRX and retained identities for every actual verification. PRs retain measured
results and review observations; configured or queued CI is not PASS.

## Remaining boundaries

Unsupported external dependency bodies remain Incomplete. There is no new
Requirement evidence eligibility policy, trusted producer enrollment, signature
verification, current authorization lookup, reconstruction of absent originals,
formal migration or executable external IR. Cancellation is cooperative at
bounded parse/import boundaries, not interruption inside a synchronous JSON parse.
Source assertion ranges remain reviewer-supplied references, not a new analyzer.

NOW 15 remains DEFERRED; adoption-ready 0/99; AP-01 71/76; individual Relation
approvals 7; Mapping Groups 0/41. Owner blockers remain open. #1144 BLOCKED;
#1128 SUSPENDED / NOT_EVALUATED; Product RLS, Native and Production HOLD;
Trust Root and Eligibility UNAPPROVED; Release NO_GO. Pure Core results cannot
discharge any of these gates. No Main or SecurityTestImplimation ref is modified.
