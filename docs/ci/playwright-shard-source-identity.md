# Source-bound Playwright test identity receipts — offline preparation

This is a **nonpublishing, pure Node contract helper** for the later Playwright
shard evidence program. It does not execute tests, start additional runners,
write files, collect secrets, replace the protected `frontend-test`, or change
the six Required Checks. The existing full Vitest and Playwright suites run
unchanged in their original CI job.

`tests/ui/playwright-shard-evidence.mjs` accepts abstract full-test discovery,
shard-test selection, explicit per-test execution results, and proposed
source/run/attempt/frontend Artifact ID metadata. It hashes Playwright
`TestCase.id` values with a domain-separated SHA-256 input, retains
only lower-case hexadecimal IDs, pass/skip status and zero retry counters,
and produces objects shaped for Draft PR #1680's offline Python reconciler.
Test paths, titles, locations, test error messages, HTTP content and any
raw identifiers are **not present in its output**.

The helper rejects malformed/duplicate/foreign test IDs, incomplete
shard results, cross-roster status changes, unauthorized skips,
failed/timed-out/interrupted/flaky or retried outcomes, and untrusted
non-integer source/run/artifact metadata. The full roster is sorted and
hashed using the same ASCII canonical JSON field order as the Python
roster verifier.

**Explicit trust boundary:** Callers must still demonstrate the full
roster was **independently discovered from the exact versioned source**,
both shard receipts came from actual Playwright reporter events, and the
artifact ID belongs to the successful same-run Angular producer by
an independent GitHub API verification. The helper cannot prove these
facts; therefore its output always carries
`artifactTrust=EXTERNAL_VERIFICATION_REQUIRED` and
`requiredCheckQualified=false`.

A real Playwright reporter must also fail the surrounding required gate
on any reporter/write error: Playwright can swallow exceptions thrown
inside reporter hooks. No reporter is wired or used by CI in this PR.
Do not fabricate missing TestCase events or count-only manifests.

The nine Node positive/negative test classes run in the original PR
Preflight only. Nothing here is a speedup claim. The earlier additive
two-shard shadow trial #1715 was closed unmerged after +766 runner-seconds
without an end-to-end improvement; the required full suite remains.

Only merge validated changes into `SecurityTestImplimation`; never Main.

The independent Python interoperability test also invokes the Node helper
through a bounded subprocess using four synthetic private-label inputs. It
recomputes domain-separated test IDs and the canonical JSON SHA-256 digest
with Python's hashlib/json, checks exact candidate roster/shard field sets,
zero retries, synthetic union, and trust flags. It never reads actual
Playwright reporter events or grants a Required Check PASS.

## Pure, nonpublishing Reporter-hook capture adapter

The additional `playwright-shard-capture.mjs` is a **disconnected**,
reporter-shaped in-memory adapter. It consumes Playwright's documented
`onBegin(config, suite)`, `suite.allTests()`, `onTestEnd(test, result)`,
`test.outcome()`, `result.retry`, `onError` and `onEnd` APIs. It
accepts only complete selected-test event inventories and zero-retry
expected PASS/SKIP outcomes. On a missing, duplicated, foreign,
interrupted, unexpectedly skipped, flaky or failed event, its
`onEnd` returns `{ status: 'failed' }` rather than silently trusting
reporter callback exceptions (which Playwright can swallow). The
`snapshot` method still requires a separately supplied full-test
discovery inventory and returns **unverified** evidence only.

Eight new pure Node lifecycle tests validate these negative cases,
privacy and that the real Playwright runner/Compose/workflow do not
import or enable this adapter. No uploads, extra runners, reporter
configuration or protected Required Check changes are introduced.
