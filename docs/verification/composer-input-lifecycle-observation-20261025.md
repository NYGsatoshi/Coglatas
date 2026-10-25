# Composer input chronology: private structural diagnostics — 2026-10-25

**Scope: synthetic test instrumentation only / unmerged / RELEASE NO_GO.**

## Original evidence and open question

[P0 #1360](https://github.com/NYGsatoshi/Coglatas/issues/1360) has multiple
first-attempt real-backend failures where Playwright's second-root `fill`
returns but its unchanged `message-draft.toHaveValue` assertion sees an empty
textarea. Structural evidence has observed composer focus/blur and a
disappearance/reappearance near SignalR reconnection, yet has not established
the triggering native input event or the responsible component transition.
[P0 #1365](https://github.com/NYGsatoshi/Coglatas/issues/1365) is a distinct
authorized Thread-rendering failure; do not conflate the two.

## Bounded observation amendment

The existing *browser test-only* `MessagingObservationV1` envelope has an
exact-key list of safe events. Keep that schema and its **48-event bound**,
frozen-on-failure snapshot, actor privacy scope and 32 KiB attachment budget.
Extend only the fixed `kind` allowlist:

- `beforeinput`: document-level event arrived on a known composer textarea.
- `change`: document-level change event arrived on a known composer textarea.
- `replacement`: the current `message-draft` DOM element is a different
  in-memory object from the previously observed composer, even if the
  presentation booleans and connection class are identical. If a native event
  is the first observer of this replacement, record **both fixed tokens**
  (replacement, then event kind) without changing the privacy schema.

The recorder never touches `event.data`, `event.inputType`, keyboard keys,
DOM values/text, composing string, labels, network frames, credentials,
identifiers or raw page data. It retains no element pointer in the serialized
snapshot: only an in-memory reference for comparison. The exported event keys
are **unchanged**: finite time, fixed `kind`, four booleans, fixed/null
connection token. Do not enable this instrumentation in product code.

JavaScript and Python strict consumers both accept the new fixed tokens and
continue rejecting arbitrary strings, malformed shapes and extra fields.
Node and Python tests assert new token acceptance, rejection of private
payloads, same-structure DOM replacement detection, no duplicate unchanged
structure, bounded evidence, and preservation of failure verdict.

## Evidence and limitations

- [ ] Execute the Node diagnostic suite and Python evidence-validation suite
      at the **exact source SHA**.
- [ ] Complete required PR CI, first-attempt Functional owner controls and
      independent review without modified test deadlines, retries or skips.
- [ ] On a future **genuinely failing** browser run, correlate new tokens with
      composer presence/focus/reconnection and known source without disclosing
      the input value. Event absence is not automatically proof that no native
      editing occurred, because DOM detach can prevent bubbling.
- [ ] Establish a separately reproducible causal defect before promoting any
      candidate to a production Composer fix.

No API, Angular product component, backend, SignalR authorization, DB,
migration, RLS, deploy workflow, required check, quality threshold or
release policy is changed. Held Composer [#1361](https://github.com/NYGsatoshi/Coglatas/pull/1361),
private SEC-ARCH/RLS and approved SSH/HTTPS target, backup/rollback, school
privacy/operator permission and pilot 17-flow acceptance remain independently
blocked. **Both Owner HOLDs / RELEASE NO_GO.**
