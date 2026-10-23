# Group roster keyboard evidence retention

## Demonstrated gap and retained failure

PR #1345 candidate `76c1ea83edd217ae96d28945048eaf1fcbd1ff61`, tested merge
`a5d0c24beb5f4c82028d798068dcd43713d804be` (identical tree), retained a first
Chromium-mobile failure in job `120494503547` of run `39976811744`.
The ordinary ReadOnly long-roster keyboard case expected list scrollTop 0 after
Home; the final logged sample was 1893 after the existing five-second poll.
The retry passed. The original result remains **FLAKY**, not a clean first pass.

The general browser-artifact upload runs only on `failure()`. Because the job
ultimately succeeded after retry, its first-failure trace, screenshot and video
were never published. Their names in a log are not retained artifact bytes.
The log cannot establish key receipt, persistent focus, scroll chronology or a
causal product defect. Historical evidence is not reconstructable from this patch.

Four bounded local diagnostic executions reused the actual producer artifact
after source-tree equality and 86 output-file hashes were checked. Original
mobile, observed mobile and observed desktop each passed once at one worker,
zero retries, original 30-second test/five-second expectation limits. A fourth
attempt opening a blank second page before Home also passed, but did not induce
document focus loss; it is **CONDITION_NOT_INDUCED**, not a successful negative
control. The subset does not reproduce all preceding full-suite/browser history
or parallel frontend workload. No product keyboard fix is demonstrated.

## Narrow future capture

Only the existing keyboard case wraps its unchanged body in a test helper. The
axe counterpart and all assertions, action order, native keyboard input, profile,
timeouts and retries remain unchanged. No Home/End handler, foreground-forcing
workaround, product, API, permission, design or security-model change is made.

The helper observes fixed key/focus/scroll event kinds for the existing nested
roster and aside. It retains only fixed key/target/focus categories, booleans,
bounded numeric scroll positions and relative time. It reads no DOM text, HTML,
URL, headers, cookies, credentials, license content or arbitrary event data.
The buffer is capped at 128 events and the closed JSON at 32 KiB. Truncation,
empty buffers or unavailable capture are **INCOMPLETE**. All other causal
classifications remain **UNKNOWN**, including successful body completion.

Final inspection occurs after the body returns or throws, before test teardown;
it is not an atomic failure-instant snapshot. Observation can perturb scheduling.
`bodyOutcome` identifies completion/failure of that wrapped body, not a replacement
for Playwright's eventual test, teardown or retry result. Capture/publication
exceptions cannot replace the identical mandatory primary failure. Without a
primary, unavailable or failed diagnostic publication still fails the case.

Profile and attempt are closed identities. Exclusive per-attempt writes preserve
attempt 0 when a later retry succeeds. No attempt is relabeled as another source
or result. The normal configured retry count is not changed or accepted as clean
release qualification by this retention repair.

## Publication boundary

An always-run collector checks actual host checkout HEAD against `GITHUB_SHA`,
requires tracked source to remain clean, and compares the restored frontend
producer stamp to that same HEAD. This reuses the existing build/source contract;
it is not independent execution, backend or producer attestation.

Before writing anything, it validates every bounded regular input, exact filename,
closed schema and attempt identity. It refuses malformed/oversized input, unknown
fields/files, symlinks, source/producer mismatch or an existing validated output.
Expected artifact parent directories must also be regular directories rather than
links. Descriptor acquisition is nonblocking, and post-open type/inode checks
reject a swapped special file. Reads allocate at most the applicable limit plus
one byte, so same-inode growth cannot trigger an unbounded read. These bounded-I/O
controls concern collection robustness; they do not diagnose the keyboard failure.
It writes a fresh serialized projection into a separate fixed directory, never a
copy of raw input. Missing observations are zero captures, not successful test or
causal evidence. A partial/failed collection is not uploaded.

The narrow always-run uploader executes only after successful collection and
publishes only `artifacts/group-keyboard-observation-validated/*.json`, with the
existing seven-day retention. General Playwright trace/HTML/video publication
keeps its original failure-only condition. No protected environment, credential,
required check, branch protection or other artifact path is expanded.

## Verification and unresolved gates

Focused controls cover closed-field privacy and byte/event caps, incomplete and
unknown semantics, identical-primary preservation, explicit unavailable capture,
an actual controlled Playwright failure followed by retry success with distinct
files, source/producer/dirty-state rejection, and the exact CI conditions/paths.
The existing Angular Playwright wrapper's mandatory Node preflight explicitly
selects `tests/ui/group-keyboard-observation.node-test.mjs` before the UI suite;
the controls are not left outside the CI selection.
That controlled retry exercises retention mechanics only; it is not a passing
product journey or permission to retry a release gate until green.

Exact committed-source focused checks, independent review, current-head CI and
applicable runtime execution remain separately required. This patch repairs the
demonstrated evidence-publication gap only. The historical keyboard failure and
P0 #1342 remain unresolved; Owner Hold Both and public **NO_GO** remain unchanged.
