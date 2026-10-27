# SEC-17 / FCI-06: pre-action denied Thread GET interleaving (diagnostic only)

This is a bounded source-specific regression control, not a change to the canonical
`MSG-THREAD-DENIAL` owner or a release gate exception.

## Original evidence

- Product PR #1468, source `94f0afa7ce9765e2c9f853a2afc92f81d54ae280`,
  real tested merge `c8251bbd2b08ddc086eff07ee888b9f78f555012`,
  [PR CI #40504001921/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40504001921).
- First native Functional collaboration job `122190185886`: the initial authorized
  Thread GET and rendered root **PASS**; following restricted actor test
  `MSG-THREAD-DENIAL` **FAIL** at the outer 120-second Playwright deadline;
  `FUNC-NOTIF-001` independently **PASS**.
- [Closed diagnostic artifact #12734945682](https://github.com/NYGsatoshi/Coglatas/actions/runs/40504001921/artifacts/12734945682),
  ZIP SHA-256 `70724205223f7d9bd5f5656a25b51b70bfaea2bf933c212969a3411f91882e48`,
  independently downloaded and compared to the provider digest.
- Its bounded `threadProbe` event order shows another GET beginning **before**
  the native fresh-observer registration. That same GET returns HTTP **400
  after** the click starts; the original tab action subsequently reports
  the fixed failure token **DETACHED**. No fresh GET is selected, so the
  strict owner remains FAIL.
- A nonblocking pre-action observation contains `errorVisible=true`,
  `rootPresent=false`, and a connected conversation surface. It does not
  establish which internal callback produced the request or whether every
  protected UI element was ultimately cleared.

The earlier mocked Angular investigation already contains
`TD-02 LEGITIMATE_EARLY_REDACTION` and `TD-03 OLD_RESPONSE_EXCLUDED` for
distinct controlled cases. Their mocked evidence is **not** a real HTTP/DB
qualification. Here the additional Node regression exercises the exact
*old-GET-start-before-observer, 400-after-action-start, native-click-detached*
ordering using the existing source-bound strict observation and
`responseToFreshRequest` helper.

## Required negative assertions

- The pre-action 400 is observed once and retains its original Request ordinal.
- The old denied response **cannot** fabricate `FRESH_SELECTED` or resolve
  the required fresh request.
- The native detached click's original error identity is preserved.
- The fixed privacy-safe observation remains `FAILED / INCOMPLETE`, with
  `freshRequest=null`, without raw request URLs, headers or error text.
- No product code, protected-session logic, assertion timeout, retries,
  browser owner, security finding policy or release permission changes.

This test is **unit-level synthetic correlation only**. It proves neither the
initiating cause nor full authorization redaction in the historical native
run. Owner judgment is required before adopting alternative fresh-denial
acceptance criteria. Prior failed attempts, held PRs and student-council
`RELEASE_NO_GO` are preserved.
