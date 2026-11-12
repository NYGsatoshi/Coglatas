# Group roster keyboard lifecycle observation, version 2

Status: **REVIEW_ONLY** diagnostic candidate for
[Issue #1346](https://github.com/NYGsatoshi/Coglatas/issues/1346).
This adds evidence, not a scrolling correction or a causal declaration.
Student Council remains **RELEASE_NO_GO**; all existing Owner holds remain.

## Why extend the finite recorder

Main `d78f3722f04c3573c3f0890f9ca950d24090144e`, CI `40219519075/1`,
frontend job `121248997035`, retained a mobile ReadOnly-list first failure:
native Home left `scrollTop` at 1893 through the unchanged five-second poll.
Retry 1 passed. The workflow SUCCESS does not reclassify that initial failure.

Artifact `12608704892`, SHA-256
`f57189c88d84b9b57caf8af033914085b7904062154346b9ad4df705cf823154`,
contains the original version-1 records. Its LIST target/focus categories do not
establish that the current list, dispatched target and active element were the
same connected node. It also lacks `scrollend` observation. Native-scroll
experiments are separate evidence and do not retrospectively establish the
hosted failure's initiating cause.

## Closed versioned contract

Version 1 keeps its exact schema, enums, 128-event cap and **32 KiB** serialized
and raw-input byte bounds. The original three d78 observations still validate
without conversion: Desktop attempt 0 has 51 events; mobile attempts 0 and 1
have 36 and 50 events respectively. Their outcomes and UNKNOWN classifications
are preserved. Historical bytes are never rewritten.

New captures emit version 2, with the existing scope/profile/attempt and
structural scroll metadata plus:

- Ephemeral positive node integers for the current UL, previously tracked UL,
  event target and active element, and each node's connectivity. No application
  identifier or DOM `id` value is read. Node numbers are capped at 4096.
- `SCROLL_END` events for the observed list/aside, with target identity and
  `isTrusted`. A synthetic event is distinguishable; a native event is still
  not an attribution to a particular command or compositor operation.
- `LIFECYCLE` snapshots when the observed UL identity/connectivity changes.
- `CAPTURE_MICROTASK`, replacing the misleading new-use `POST_KEYDOWN` phase
  name. Version 1 retains its historical phase name for strict compatibility.

Version 2 retains **128 events**, with an explicit **64 KiB** bound. A maximum
closed-field fixture is 53,974 bytes, so it cannot fit the old byte ceiling.
Both versions reject extra/raw fields, invalid enums, oversized records and
inconsistent absent-node/connected flags. Truncated, empty, unavailable or
version-2 captures that never observed a list are INCOMPLETE. Other captures
remain UNKNOWN; there is no causal PASS classification.

The collector reads at most the new finite maximum, then additionally rejects
version-1 raw input over 32 KiB, including padding. It retains the existing
clean tracked source, exact expected Main/merge SHA, trusted frontend producer
SHA, file-race guards and validated-only publication. A mixed-version synthetic
control preserves each inner version and binds both source/producer identities.
No workflow, upload path, retention, configured retry or general trace policy
changes are required.

## Evidence limits

The document listener is in the capture phase. Its queued CAPTURE_MICROTASK
can run before Angular bubbling handlers. Recorded focus/default-prevented
values are snapshots at that phase, not proof that Angular handled the event
or that dispatch/default action finished. The version-1 POST_KEYDOWN label
must be interpreted with the same limitation.

DOM mutation delivery is batched. If replacement occurs within one batch,
the current/tracked reference can already point to the new connected UL;
an intermediate disconnected snapshot is not guaranteed. A changed node
number proves an observed DOM replacement, not Angular binding validity,
the initiating callback, visibility, authorization or component state.

Native SCROLL_END records only the browser's event on the observed target.
Absence can reflect missing delivery, a different node, capture truncation or
the operation not having settled. The recorder exposes no compositor animation
state and must not infer active/finished animation from `scrollTop` alone.
Ordering Home before an observed scrollend is timing evidence, not proof that
an earlier End caused a later failure.

The recorder reads no text, names, row/application IDs, CSS/style content,
HTML, URLs, queries, bodies, cookies or authentication data. It does not alter
focus, styles, scroll position, event defaults, product state or native actions.
Finalization disconnects mutation observation and aborts event listeners.
The existing primary-error identity and separate immutable attempt files remain.

## Validation

- **13/13** owning helper controls and **29/29** selected helper controls pass,
  including legacy/new schema positives and negatives, missing-list/truncation,
  worst-case bounds, raw-byte padding rejection, mixed source-bound versions,
  primary-error identity, mechanical retry retention and file-acquisition races.
- **4/4** portable native recorder controls pass with pinned Playwright 1.64.1 /
  Chromium 156: connected identical native key target/active/current UL and
  native scrollend; synthetic scrollend marked untrusted; replacement of UL;
  a 128-event truncated INCOMPLETE case; and a missing-list INCOMPLETE case.
  Private-text canaries never appear in serialized output.
- The three immutable hosted version-1 receipts independently pass the extended
  strict validator. This does not waive the failed mobile first attempt.

```sh
node --test tests/ui/group-keyboard-observation.node-test.mjs tests/ui/student-council-pilot-helpers.node-test.mjs tests/ui/public-https-golden-path-helpers.node-test.mjs
node tests/ui/group-keyboard-observation.browser-control.mjs
git diff --check
```

The canonical owner test file, its assertions and five-second poll are
byte-identical to Main d78. There is no product, MessagingFacade, security,
permission, backend or held-candidate change. These controls do not establish
real-backend, physical-device, complete keyboard/accessibility, hosted
exact-candidate or resulting-Main qualification. Independent review and current
CI remain necessary for any integration decision.
