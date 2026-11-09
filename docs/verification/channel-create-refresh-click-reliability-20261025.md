# Workspace Channel create-click reliability during current-scope refresh — 2026-10-25

**Source scope:** Product Main `7fa8f7217bc71251e90842df6d1035f49d2bd9b2` + this isolated candidate. **Pilot RELEASE NO_GO / SEC-ARCH/RLS and staging Owner HOLD BOTH.**

## Observed first-attempt failure and narrower product defect

[P0 #1391](https://github.com/NYGsatoshi/Coglatas/issues/1391) retains the immutable first-attempt licensed real-backend browser failure: the browser observed a create button enabled, completed its native click, and the unchanged 15-second `channel-create-dialog` assertion failed before POST. Bounded protected Playwright timing from Main `2e260516…` shows a second current-Workspace Channel directory HTTP 200 near the end of the click. Its causal relationship to the missing dialog, and whether a genuine protected-state invalidation also ran, remain UNKNOWN. Green runs on subsequent distinct Main SHAs do not retroactively pass the failed attempts.

A separate **deterministic client-state defect** exists in the previous `WorkspaceChannelsFacade` implementation: a previously granted create button can be shown as enabled, but `refresh()` synchronously changes the server capability to temporarily unknown (`status=loading`, `directory=null`); `openCreate()` checked the now-false `canCreate()` and silently discarded the user's actual native click. This was reproducible by invoking the registered same-scope catch-up between the previous granted UI render and the click. This mechanism is plausible for the recorded HTTP/click overlap, **not verified as the hosted failure's initiating cause**.

## Bounded implementation

- The existing `canCreate` and `canSubmitCreate` remain **unchanged**: POST still requires fresh current-Tenant/actor/Workspace directory permission, ready state, valid name/visibility and no pending command.
- New `canOpenCreate` allows **only the local empty dialog** during an in-flight routine refresh when the *immediately preceding* same-scope server response had `canCreate=true`. Starting a first load, an initially denied directory, an already revoked/currently invalid scope, a pending mutation, logout or Workspace/Tenant/actor replacement provides no opening authority.
- Concurrent same-scope refreshes preserve this narrow local opening affordance, but **neither the dialog open nor server response submits a command**. All actual creation remains an explicit user submit after a successful new permitted GET.
- The prior local-opening affordance is cleared on validation error, HTTP denial/error, a fresh `canCreate=false` result, and synchronously on every protected-state reset. Any protected authorization loss continues to cancel current requests and purge dialog/name/identity; later successful reauthorization cannot restore a previously cancelled user intent.
- The template now binds the create button's disabled state to `canOpenCreate`, instead of incorrectly disabling a displayed local-only affordance at the instant a routine refresh starts. No authorization, SignalR group, backend/API/DB/RLS/security boundary is bypassed.

## First-attempt acceptance

Four deterministic Angular `HttpTestingController` cases on the existing facade verify the native-click interleaving, initial denied scope, fresh server refusal + protected-state purge, and overlapping-refresh/Workspace replacement. Two existing real Angular/Playwright browser-profile cases hold the actual mocked HTTP GET during a **native** click; both require an opened local dialog and **zero** POST while permission is unknown. A permitted fresh result enables manual submit; a denied result immediately closes the dialog and clears the local action. Prior positive and fail-closed tests remain unmodified.

- [ ] Exact-head Angular unit tests and native Chromium desktop/mobile interleavings succeed on first attempt with zero retry.
- [ ] Exact-head mandatory PR CI, ReSharper/CodeQL/publication/source checks and independent privacy/authz audit pass.
- [ ] Resulting Main's **actual protected ASP.NET Core/PostgreSQL first-attempt** Channel P0 (8 cases) and Fast/Full/Extended Thread owners qualify independently, with failures and skips retained.
- [ ] Open [#1391](https://github.com/NYGsatoshi/Coglatas/issues/1391) until the source-bound actual first-attempt defect has a demonstrated causal closure. Do not equate a passing rerun or this mocked-browser regression with a proof of hosted cause.

No retry, timeout, original mandatory E2E assertion, CI required check, scanner threshold, release gate, school data, live staging or secret changes. Held #1320/#1326/#1361/#1367 and existing SEC-ARCH/RLS, approved HTTPS origin, strict backup/recovery, school privacy, IME and external 17-flow pilot remain separate **NO_GO** blockers.
