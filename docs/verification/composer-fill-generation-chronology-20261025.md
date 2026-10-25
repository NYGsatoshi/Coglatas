# Composer native fill and DOM generation chronology — 2026-10-25

**Disposition: TEST-ONLY / DRAFT REVIEW / NO RELEASE GO.** Related: [Composer P0 #1360](https://github.com/NYGsatoshi/Coglatas/issues/1360), held [product candidate #1361](https://github.com/NYGsatoshi/Coglatas/pull/1361), held [Thread diagnostic #1367](https://github.com/NYGsatoshi/Coglatas/pull/1367), and isolated [SEC-17 test #1399](https://github.com/NYGsatoshi/Coglatas/pull/1399).

## Problem and exact source of a new observation

First-attempt Fast Collaboration on PR #1399's test merge `aeac130b5a7a0b395f5ca2b45fe0ad3e65c622c7` failed at the canonical `MSG-THREAD` second-root `message-draft.toHaveValue`: `fill()` returned, but the assertion saw an empty textarea for the original 15-second window. That failure remains immutable in [run #40163887187](https://github.com/NYGsatoshi/Coglatas/actions/runs/40163887187), [job #121081658004](https://github.com/NYGsatoshi/Coglatas/actions/runs/40163887187/job/121081658004). The validated original browser observation contains focus, blur, disappearance, replacement, and no observed input events. It does **not** contain the Playwright `fill()` call's timing or a persistent, public-safe DOM node identity. Recreating the cause from this older source would be speculation.

## New bounded observer (no product change)

The current test-only change observes the existing native `draft.fill(secondRootBody)` promise with two **host wall-clock** samples taken synchronously before invocation and immediately after settlement. The wrapper adds **no browser evaluation, retry, wait, page mutation, assertion change, text read or timeout**. A single per-Page record uses the exact three keys `startedAtMs`, `finishedAtMs`, and `outcome` (one of `resolved` or `rejected`). It is suppressed if it predates the active browser document. All other form contents and arbitrary thrown error text remain in memory only.

The existing browser-only probe separately increments a small integer `composerGeneration` whenever it observes a new `message-draft` DOM node; each fixed kind/boolean structural event records the active generation. Generation 0 denotes no observed node, 1 the first observed node, and 2+ later replacements. This **does not expose** the underlying node, DOM HTML, form value, user, Workspace, Conversation or network contents. The existing 48-event/48-control bounded buffers, explicit truncation counts, network limit of 80 and 32,768-byte attachment cap remain unchanged.

The generated browser diagnostic envelope is **schema v3**. The independent Node and Python validators reject unknown keys, forbidden values, malformed generation, reversed/invalid fill intervals, and unbounded rows. Historical v1 (no Messaging) and v2 (old Messaging events, no generation/interval) remain accepted with their original strict shapes. No old failure is rewritten as a new v3 record.

Browser events and test-worker fill samples use epoch milliseconds. When the successful canonical journey later navigates to a new document, the earlier fill interval is deliberately null in the final after-cleanup observation: it cannot be joined to a different document's DOM generations. A failed-step checkpoint on the same document is required for direct correlation. They are useful to classify relative scheduling on a shared runner; a source-bound trace must still account for timer resolution, cross-process clock skew and sampling delay before claiming a precise happens-before or causal relation. A truncated buffer, a missing interval, a document mismatch, an unobserved native input event, or a green run alone cannot establish the historical cause.

## Qualification checklist

- [ ] Exact-head independent Node producer/consumer and Python validation tests PASS with privacy-mutation negatives; verify unchanged historical v1/v2 acceptance.
- [ ] Strict no-emit Functional Messaging TypeScript check and actual hosted PR CI PASS on the exact source; report every first-attempt failure and retry separately.
- [ ] Real canonical `FUNC-MSG-001` first-attempt evidence retains `MSG-THREAD` and `MSG-THREAD-DENIAL` assertions and original 15-second deadline. Reconcile `fill` start/end with new `composerGeneration` and `replacement` events only if an actual matching failure is observed.
- [ ] Independent review verifies the wrapper records no protected text, browser pixels, credential, raw URLs, actor/resource identifiers or unbounded arbitrary timestamps.
- [ ] A supported product mechanism must be reproduced and a separate product repair independently qualified before closing #1360 or integrating held #1361.

No change to SEC-ARCH/RLS, Hub, authorization roles, real user admission, live Staging/HTTPS, backup/rollback, CI thresholds, or release policy. **Owner HOLD BOTH and Student Council RELEASE NO_GO remain.**
