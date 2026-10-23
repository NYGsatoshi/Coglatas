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

Only `artifacts/mobile/owners.json`, `image-continuity.json` and `source-sha` are
published. The reporter rejects missing/duplicate/wrong-profile/mock/failed/
skipped/retried/incomplete owners and emits finite IDs, step tokens and outcomes.
It ignores assertion bodies, attachments and protected text. Authenticated pixels,
trace, video, JUnit and generic failure bundles are not uploaded for this suite.
Require both a successful exact-candidate job and matching passing owner/image
receipts; a partial owner receipt alone is insufficient.

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
