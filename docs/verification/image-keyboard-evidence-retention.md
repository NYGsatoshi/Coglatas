# Files image keyboard evidence retention

## Retained P0 failure

Issue [#1980](https://github.com/NYGsatoshi/Coglatas/issues/1980) records the
first-attempt failure of the 1536 CSS-pixel Files image case in PR CI
`43692134220`, frontend job `132745921641`, on source
`7aefe873bee993159e3981e04049a1af57760a90`. After native ArrowDown, the original
five-second assertion still observed `scrollTop == 0`. The retry succeeded.
The original result remains **FLAKY**, and its cause remains **UNKNOWN**.
The original log's trace paths do not prove those bytes were uploaded.

The already integrated diagnostic patch retains geometry snapshots in the
Playwright attachment and log. General browser artifacts are deliberately
failure-only: changing them to success-time publication would broaden the raw
browser-data boundary. This repair preserves that policy and retains a separate
bounded numeric record after every observed image scroll sequence, including
attempt zero when a later retry succeeds.

## Observation and publication contract

Only the existing native keyboard scroll sequence is wrapped. Its focus calls,
ArrowRight/ArrowDown order, original assertions, timeouts and retry policy are
unchanged. The existing failure snapshots and original rethrow remain intact.
The helper records initial/final geometry, fixed key/focus/scroll event kinds,
trusted/default-prevented flags at the capturing listener, document and element
focus, node continuity, decoded image size, viewport dimensions and scroll
positions. A capture-time flag does not establish what a later event handler
does. The observer never prevents input, focuses a node or changes scrolling.

It reads no URL, DOM text, HTML, cookie, header, credential, user filename or
license. Every field has a closed schema. Per-record limits are 64 events and
64 KiB; the profile, five widths, two synthetic image kinds and attempts 0–2
form at most 180 distinct filenames. Exclusive writes cannot overwrite the
failed first attempt. A later local rerun must use a fresh output directory.

`bodyOutcome` describes only the wrapped scroll sequence, not later test steps,
teardown or the eventual retry result. Missing/unavailable/truncated capture is
**INCOMPLETE**; every other causal classification remains **UNKNOWN**. Observation
can affect timing and does not itself prove that the historical cause recurred.
Capture/publication failure preserves the identical original assertion error.
If the original sequence succeeded, unavailable capture is a visible failure.

The PR collector checks clean tracked source and actual checkout HEAD against
`GITHUB_SHA`, and checks the restored frontend producer SHA against that same
source. It binds the validated projection to the workflow run ID, run attempt
and `frontend-test` job, separately from each Playwright retry index. These
checks reuse the existing producer/source contract and are not an independent
execution attestation. Missing observations are zero captures, not a pass.

All inputs are validated before writing the output. Unknown fields/files,
oversized input, identity mismatch, source changes, stale output, symlinks and
special files are rejected. Descriptor reads are bounded and nonblocking. Only
freshly serialized JSON from the separate validated directory is uploaded,
after successful collection, with seven-day retention. Both steps have a
two-minute bound. Their success condition includes retry-success; general
Playwright report/trace/video publication remains failure-only.

## Qualification and remaining work

The mandatory Angular Playwright runner preflight selects the Node controls for
schema privacy, bounded I/O, immutable retry files, source/producer/run binding,
unavailable capture and original-failure preservation. A controlled Playwright
worker failure followed by retry success exercises record retention mechanics
without a browser; it is not a product journey or reproduction of #1980.

Local production Angular compilation succeeds. A native Chromium launch on the
local managed executor failed before any page/test because browser singleton
socket creation was denied. No local native browser pass or causal repair is
claimed. Exact-source hosted browser results and uploaded JSON must be inspected
before qualification. Main publication is outside this PR's Security-branch
scope. The historical first-attempt cause still requires native reproduction,
an owned repair and the applicable browser matrix. **#1980 remains open.**
No Files product change, Required Check change or release approval is made;
#1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED and Release NO_GO remain.
