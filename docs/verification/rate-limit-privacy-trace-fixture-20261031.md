# Stable rate-limit response privacy qualification

## Retained Main failure

Main `0f389fa58db815baa92c6e9889036ce4b26655df` ran CI
`41136029384/1`. Backend job `124340290565` reported **2,638 passed,
1 failed, 2,639 total**; architecture reported 12 passed.
`LoginPolicyRejectsEleventhAnonymousRequestWithRetryAfter` failed at its
case-insensitive body-wide `ip:` exclusion because the generated Kestrel
request identifier happened to contain that suffix. The retained diagnostic
locates the match inside `requestId`; it does not establish exposure of a
rate-limit partition identity. The original failed attempt remains a failure.
Raw response and trace-identifier text are not reproduced here.

## Test-only repair

The existing owned Kestrel test host assigns a fixed neutral trace identifier
before rate limiting. All eight original assertion lines remain byte-identical:
ten allowed requests, the eleventh denied with HTTP 429, a `Retry-After` value
between 1 and 60, `RateLimitExceeded`, and both body-wide `ip:` and `user:`
exclusions. An additional assertion checks the exact serialized fixture trace.
No assertion, timeout, retry, rate-limit policy or protected check is relaxed.

A separate two-case callback control obtains a real denied
`FixedWindowRateLimiter` lease and invokes the production `OnRejected` delegate
with anonymous and authenticated synthetic contexts. Its opaque synthetic trace
intentionally ends in `IP:`. It requires exact trace retention, exactly the
`requestId`/`error` root fields, exactly the canonical `code`/`message` error
fields, bounded retry metadata, and absence of the actual synthetic IP address,
user ID and full rate-limit partition key. The callback control does not start
a second server or claim middleware, PostgreSQL, public HTTPS or Pilot acceptance.
The existing Kestrel test retains the actual middleware/429 qualification.

No product source, response contract, authentication authority, feature flag,
configuration, database or deployment asset changes.

## Source verification and pending execution

The source guard reverses only the additive imports, fixture middleware,
exact-trace assertion and new callback control to recover the original tracked
file byte-for-byte. The original hosted assertion-line sequence has SHA-256
`0bdf620e28aaf55294d20296f8e963199804efa226abc812332eb7df095a4904`.
The recovered original file has SHA-256
`920f070f262c24e1a989da23427d49bce1952ccb77e251e5f36c0476ec26f88c`.

Local source/API review used the installed .NET 10.0.14 reference XML. No local
compilation or .NET/Docker test execution was attempted: the earlier global
Windows SDK workload-installer history and unavailable Docker engine are retained
limitations. Source inspection is not a compile or test PASS. Exact-HEAD hosted
unfiltered Native backend qualification, required checks, independent review,
ordinary merge and resulting-Main qualification remain required.

This repair grants no release, intake/scanner, RLS, recovery or target-environment
approval. `RELEASE_NO_GO` remains in effect.

## 2026-10-31 Main Qodana collection-expression follow-up

Main `5106ee4705e0c6a2b1d93d10d3af914e531df343` Cloud job
`124365835314` failed the unchanged `UseCollectionExpression` ratchet:
2,280 total findings, current 39 versus budget 37. Original artifact
`13021216254` attributes the two new findings to the expected-field arrays
in this test at lines 200 and 205. The original failure remains retained.

The follow-up changes only those two expected sequences to collection
expressions in `Assert.Equal`, with `string` inferred from the ordered actual
sequence for the existing `IEnumerable<string>` overload. Element order,
actual-field ordinal ordering, all response assertions and the complete hosted
Kestrel test remain unchanged. No production source, inspection, budget, timeout
or retry changes. Review removed unnecessary explicit type arguments to avoid
the existing zero-budget `RedundantTypeArgumentsOfMethod` rule; no executed
finding or test result is claimed for that precaution.

The earlier 2,641-test Native PASS belongs to Main 5106. This follow-up has
source/reverse review only; no local compilation or test execution and no
new PASS claim. Exact-HEAD hosted qualification and independent review are
required before ordinary merge and fresh Main qualification.
