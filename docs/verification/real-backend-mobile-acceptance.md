# Opt-in real-backend mobile acceptance

Status: implementation candidate based on Main
`4ae574ab567f003b91ce2e467f0fd3681880b918`. Exact-candidate protected runtime
qualification is required; discovery and mocked compatibility runs earn no
real-backend credit. This does not lift release, security architecture or
target deployment holds.

The existing **Licensed Real Backend Acceptance** workflow accepts the explicit
`mobile` suite in its existing protected `syncfusion-licensed-build` environment.
No additional credential scope, permission or environment is introduced. It runs
`node scripts/ci/run-real-backend-mobile.mjs` from an exact clean checkout with
`TARGET_SHA` matching Git HEAD. The canonical Compose stack starts PostgreSQL,
applies migrations, and runs real Kestrel, Angular, SignalR and Playwright.
The mobile overlay adds only a build revision label and the isolated test command.

The honestly named `real-backend-mobile-320` profile fixes Chromium, a 320x800
CSS-pixel viewport and screen, touch/mobile emulation, scale 1, one worker and
zero retries. Four owners must execute once with no skips or missing steps:

| Owner | Required behavior |
| --- | --- |
| FUNC-MSG-001 | Reuses FCI-06 Direct DM persistence, independent read state, canonical Thread drafts/replies/reload, freshly restricted denial and protected-state clear, offline HTTP catch-up and recipient rendering |
| FUNC-NOTIF-001 | Reuses FCI-06 generic recipient notification, durable read/open state and unauthorized actor denial |
| FUNC-FILE-002 | Reuses FCI-05 invalid-upload denial, API-prepared synthetic FileObject, authoritative list/detail identity, mobile card/Preview/Details, grant-backed exact bytes, reload, run-owned cleanup and denied future reads/grants |
| FUNC-MOBILE-001 | Derives Workspace creation, named private channel, Message/root/reply persistence, reload and archived Workspace removal from the existing Main smoke; imports no held Pilot-01 helper |

Each independent recipient context explicitly inherits only viewport, screen,
touch/mobile and scale options. Cookies, headers, storage state and authentication
are not copied from another actor. Existing independent synthetic login remains.
The Files mobile card receives a nonvisual opaque row identity hook; the helper
uses the actual mobile Preview and selection controls, preserving canonical
identity, byte, cleanup and denial assertions. **Files upload is API fixture
preparation, not mobile UI upload or scanner qualification.** The unavailable
pilot upload control and server policy remain unchanged.

Canonical Fast six-owner and Full seven-owner inventories, default Desktop
project, required checks and evidence manifests remain unchanged. The new channel
file is `.browser.ts`, outside canonical `*.spec.ts` discovery. Mobile receipts
have their own namespace and cannot substitute for canonical domain manifests.
Discovery prints `NOT_EXECUTED` and issues no passing owner receipt.

## Source and runtime evidence

The host checks a clean exact checkout before running. Fresh isolated builds must
carry that revision label; a before/after verifier binds the configured producer
image ID to the actual running container and rechecks source identity. Restored
Main image mode retains the existing artifact/dotnet stamp and image-continuity
guard, and additionally verifies its producer source and image IDs. Binding
failure fails the suite without relaxing any browser assertion or retry setting.

Only `artifacts/mobile/owners.json`, `diagnostics.json`, `image-continuity.json` and `source-sha` are
published. The reporter rejects missing/duplicate/wrong-profile/mock/failed/
skipped/retried/incomplete owners and emits finite IDs, step tokens and outcomes.
It ignores assertion bodies, attachments and protected text. Authenticated pixels,
trace, video, JUnit and generic failure bundles are not uploaded for this suite.
Require both a successful exact-candidate job and matching passing owner/image
receipts; a partial owner receipt alone is insufficient.

The finite diagnostic projection records each allowlisted owner status, retry,
completed/failed step tokens, fixed failure kind, and allowlisted source module
and line number. It never forwards arbitrary test titles, error messages, URLs,
attachments or resource identities. A failed owner produces diagnostics but no
PASS receipt. Browser output stays in the disposable container's `/tmp` directory,
separate from host harness diagnostics, without changing file permissions.

The first exact candidate `b1a477afdffc301899de1b933d8b51efb423ad08` failed
protected run `39951320069`, job `120406213232`. Its artifact `12500398221`
(ZIP SHA-256 `2a783f0dbc7ba8ee43d576b43f458289c800d85984b546b0fc6e0133ec80bbf0`)
contains matching source and fresh-image continuity receipts, but no owner PASS.
The initial reporter retained no browser failure detail, so its underlying
browser cause remains unknown. Later host diagnostic PermissionErrors occurred
after the suite exited unsuccessfully and do not explain or clear that failure.
The subsequent diagnostic changes require their own exact-source runtime run.

The reviewed diagnostic replay at `2c44deedacf45a8f0760be1c719049ab3bbeb6d5`
also failed (run `39953523941`, job `120413598604`). Artifact `12500424875`
(ZIP SHA-256 `14bde7b04bc43cdcba197fdf30f4a5821c37dc3123938c605a3e8ef33dc03853`)
records three complete owners at retry zero: Files, Messaging and Notifications.
The channel owner completed MOB-01 through MOB-03; a later cleanup timeout hid
the earlier failing reload step in the first diagnostic projection. These
partial results provide no suite PASS. Source/image continuity matched, and the
secondary host output PermissionErrors did not recur.

The channel reload URL omitted the reply `focusMessageId` required alongside
`threadRootMessageId` by the existing channel component and Main smoke. The
follow-up supplies the validated returned reply ID and also requires that same
reply identity in the persisted projection. Diagnostics preserve the first
failed mandatory branch when later cleanup also fails. No product contract,
assertion, owner inventory, timeout or retry setting is relaxed. The follow-up
still requires independent review and its own exact-source runtime execution.

The source follow-up `689d0a29244425e370e4a328e79fa57db98d1542` passed protected
run `39955029289`, job `120418631629`: four owners, 23 mandatory steps, one attempt
each, no failed/skipped/retried owners, matching checkout/source/image continuity.
Artifact `12502596216` ZIP SHA-256 is
`1d6ac1e2c70d9137060ae898f2546b69551317f005fd171b85de2ffedc4356d8`.
That source result does not qualify a combined integration source.

Combined source `bed6e2f7382f5ee10189c04064e50eb10b2622e0` subsequently failed
run `39955831750`, job `120421315334`. Messaging timed out during the retained
restricted Thread activation; recovery did not execute. Files, Notifications
and channel owners passed, but no owner PASS receipt was issued for the suite.
Artifact ZIP SHA-256 is
`82f5f48faa5d3fe40648d634798dd5bb845e7eb187a35c32e9e850565d52ccac`.
The timeout location alone cannot distinguish an unactionable click, absence of
a new request, or an unfinished response; it proves no authorization outcome.

A diagnostic-only follow-up retains the existing request/action concurrency and
adds fixed phase tokens for observer, action, request and response completion or
failure. One nonmutating pre-action snapshot contains only tab existence,
visibility/enabled/selected state, viewport/pointer-hit and expanded booleans.
The reporter accepts only the bounded validated attachment; it retains no raw
locator, text, attribute value, resource ID, URL or error. All current-session,
fresh-request, exact 400/envelope/redaction, UI-clear and recovery requirements
remain unchanged. No product root cause or runtime fix is asserted without a
new independently reviewed exact-source execution.

## Future retained-Thread observations (#1342)

The opt-in owner can additionally observe only the target Thread GET from the
denial step's entry, before its authorized positive read and cookie replacement.
Ordered events contain a fixed kind, a transient request ordinal and an HTTP
status where applicable. The selected fresh request is bound to an observed
request after the verified restricted session and registered request observer.
Fixed markers bound the start and completion of the login helper, allowing
requests emitted within that interval to be distinguished from earlier requests.
They do not establish the exact cookie-set instant or actor identity of those requests.
This records observation order, not server-side actor identity for every request.
Requests already in flight before observation may be absent; unmatched terminal
events explicitly mark incomplete coverage. `orphanResponse` covers any such
response, failed request or finished request.

The attachment remains limited to 4096 bytes and 24 ordered events, with an
explicit truncation flag. Target booleans are inspected after the caught failure
and frozen before actor restoration. This is not an atomic failure-instant
snapshot: network events may arrive while that inspection awaits the browser.
An unavailable failure snapshot is null, never an
invented absent target. Native action errors are projected in memory to fixed
tokens only; a token describes a call-log fragment, not a verified product cause.
No raw errors, URLs, headers, resource identities, text, bodies, screenshots or
native traces enter this attachment. Existing phase-only receipts remain readable.

Every complete record's causal classification is `UNKNOWN`, including a successful
run. Missing selected-request binding, an unavailable failure snapshot, orphan
terminal events or truncation yield `INCOMPLETE`. Neither classification resolves
the historical `bed6` failure or supplies missing retrospective evidence.
The strict fresh 400, exact hidden-error envelope, body/draft/cache clearing,
positive controls and recovery remain mandatory. Product behavior, canonical owner
inventories, gate, workflow, timeouts and retries are unchanged. Focused synthetic
observer/reporter controls qualify future capture mechanics only; protected
exact-source execution and independent interpretation remain required.

## Qualification limits

- Synthetic local HTTP is not HTTPS staging, Secure-cookie or target qualification.
- Touch emulation is not a physical device, software keyboard or physical Japanese IME test.
- Existing design direction is preserved; no latest Penpot approval or visual parity is asserted.
- This does not qualify Group Chat, the held Pilot-01 cross-session private-channel lifecycle, complete RLS/SEC-ARCH, or all File grant lifecycle boundaries.
- No real accounts, invitations, school data, deployment or release GO is authorized.

Local candidate verification and hosted exact-SHA outcomes must retain their own
source identities, original failures and environment limitations. The existing
five-profile mocked compatibility evidence remains separate.

## Local candidate checks

The working candidate passed all 58 Functional Node contract tests in the pinned
Linux Playwright image, including five new mobile mutation tests, with zero
failures or skips. The Linux harness self-test, strict TypeScript Bundler check,
four-owner discovery, publication-readiness and required-check topology checks
also passed. An earlier Windows attempt retained one shell invocation failure
from WindowsApps `bash.exe`; it is not counted as a passing run. The local
unlicensed Angular production build passed with existing style-budget warnings
and supplies diagnostic compilation evidence only. These checks do not provide
licensed producer or real-backend mobile qualification. The exact committed
candidate still requires independent review and protected runtime execution.

The diagnostic follow-up passed seven focused mobile Node controls on both
Windows and the pinned Linux runner, including a real controlled Playwright
failure that publishes bounded diagnostics, preserves a host output sentinel,
and never issues owner PASS or exposes its private assertion canary. This is
reporter/output-boundary validation, not successful backend acceptance.
