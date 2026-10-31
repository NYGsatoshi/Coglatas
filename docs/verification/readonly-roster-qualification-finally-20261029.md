# ReadOnly roster qualification finalization

## Observed issue and authorized scope

Sonar issue `AaGCW6T8P2m4IR12Ggtw`, `typescript:S1143`, Open / High Reliability, reports the guarded throw in `tests/ui/readonly-roster-real.browser.ts` at baseline line466. Baseline Main `aabae9c7db4ddadda99db42d0cdebe81e5a893a9` already uses a boolean to preserve an existing primary failure; no demonstrated primary-failure override or historical #1346 cause is asserted. This behavior-preserving analysis-clarity refactor does not alter the canonical owner or its acceptance criteria.

## Result

Keep lifecycle freezing and catch/rethrow behavior in the owner. Move owner-context closure, exclusive auth-free receipt writing and attachment into a directly tested finalizer that returns a secondary Error or null. The owner conditionally throws that Error after its finally block; an existing primary throw remains authoritative. Preserve fixed receipt/attachment names, wx creation, mode0644, closure/status fields, fixed annotation and primaryFailure boolean. No new authorization or data projection, no retry/timeout/click/keyboard/member-count/assertion/contract change.

The entire native try/action/assertion/catch segment is byte-identical to Main baseline; UTF-8 SHA-256 `a3c4bd59290514e7c788b3267231847e3a22fa772f2cc5c8d9c06eeea9b67d86`. This includes all actual browser operations and primary diagnostic capture.

## Local verification

- First Node execution **30 PASS**, zero failures/skips:20 existing wrapper controls plus10 finalization controls. New controls use actual temporary receipt files and controlled close/attachment failures: positive closure-before-attachment/source receipt; absent owner; close/attachment/exclusive-write/both failures; primary failure remains authoritative in all four cases; no raw cleanup/attachment detail in public receipt. Existing receipt is never overwritten. No actual Docker or browser owner is claimed by these controls.
- Focused Strict TypeScript for the changed owner PASS with existing lockfile declaration versions/integrities restored into an isolated work directory. Product lockfiles/dependencies and Node24 runtime unchanged.
- Whole repo inspection: **181 baseline errors /181 current errors**, no added or removed diagnostic signatures, read-only CompilerAPI comparison against Mainaaba. This is not a full-project PASS. Initial local commands encountered a missing guessed config, missing local Node declarations and an empty-key lockfile JSON reader error; the reader and exact pinned restore were corrected. Those setup failures remain recorded.

Fresh native/protected CI, current independent review, official Gate/rescan, source-bound actual-backend43-member Desktop/Mobile owner acceptance and resulting-Main qualification remain required. Never infer a fix to the historical roster failure from this refactor or current success. No finding dismissal, suppression, rule/source/threshold change; FCI06 Default C, all Owner holds and historical failures remain.

`STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.

The81da officialGate123676286634 passed92.5%NewCodeCoverage but reported two Low javascript:S6551 findings (AaGJGMXs8dYv669dApMS/ApMT, runner unitL592/L611) because heterogeneous tuple inference may treat the interpolated name as an object. Replace the same eight finite parameter tuples with named records; labels, fault values, expected calls, all test bodies and production helper remain unchanged. No actual runtime stringification defect is claimed. Current Main9677 normally incorporated; new30controls/focusedStrictTS/native/Gate/review/real43DesktopMobile evidence required. Provider issues remain unchanged until rescan.
