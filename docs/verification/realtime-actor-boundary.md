# Realtime authenticated-actor lifecycle boundary

## Problem and bounded change

The one browser-owned Realtime transport previously followed authentication and
Tenant identity but did not observe the authenticated user identity. Replacing
an authenticated user with another user in the same Tenant could leave the old
transport, resource intent and protected client projection intact.

The root lifecycle now observes `currentUser.userId`. A replacement actor uses
the existing session-boundary clear, synchronization epoch invalidation and
transport stop before the replacement synchronization. Connection continuations
also require the current actor to match the tracked actor. An incoming frame is
discarded when the actor signal has changed before Angular runs that lifecycle
effect. A same-actor display-name refresh preserves the current connection.

There are no API, Hub subscription, server authorization, role, grant, RLS,
feature-activation or enforcement-authority changes. The existing disabled
Realtime rollout and same-actor Tenant-hydration behavior remain independent of
an actor replacement.

## Source and regression evidence

### Same-actor HTTP fallback refresh regression

Main `eba360a8d4682a8a609c31428fdf61c167834740` exposed an unintended lifecycle
dependency on the complete current-user object. A failed Hub start probes
`/api/auth/status`; the authoritative mapper creates a fresh user object even
when the actor ID is unchanged. That update could start another connection
attempt and return the indicator to Reconnecting instead of stable HTTP fallback.

Realtime now observes one shared computed actor ID in the lifecycle effect,
connection admission and early-frame guard. AuthSession continues publishing
the complete fresh profile/capability snapshot. Actor changes still invalidate
the old epoch, stop the transport, clear protected scope and reject old frames.

Test-only commit `5c15392cf4c7f26ad7593f4670fc357b7a9dc3cc` reproduced the defect
with the real AuthSession status mapper, HttpTestingController and a rejecting
transport that emits connecting: one test passed and one failed. The same-actor
case observed two starts, Reconnecting and an unexpected second status probe.
The changed-actor restart/old-frame control already passed.

The minimal runtime correction passed both unchanged new controls, then all 87
focused tests: 61 existing Realtime, 24 Auth read-boundary and two status-refresh
controls, with zero failed or pending tests. These are local Windows Node
24.22.2 controls using existing installed dependencies; source lockfiles match
after LF normalization. They are not actual browser/backend or current Main CI
qualification. The existing 15-second delayed-state browser assertions, transport
retry policy, session/authorization contracts and release boundaries are unchanged.

Independent review before merge found that the first selector normalized an
absent user ID from undefined to null. During terminal clearing, a registered
clearer can synchronously reenter frame delivery after the owned actor becomes
null and before the old Tenant/Connected state is reset. That normalization
changed the original early guard comparison. Test-only successor
`a3863c75ff59c501f6b71ebf2725d1a5ae444fd5` retained the first runtime and observed
one published reentrant frame: two status controls passed and the new one failed.
The selector now preserves the original optional-read undefined value. All 88
focused controls passed, including the unchanged two status-refresh controls and
the added authoritative terminal-status reentrancy control. This is a bounded
local regression, not evidence of an operational network exploit. The original
87-test result and source-bound CI run remain historical evidence.

The starting source is Main
`88f8648cbcc9286407ea17e75cb2b9b7ed6ff599`, tree
`24866051f0154b21cd337ed23ea53a54546d1a37`.

The initial test-only reproduction is commit
`7dd47c9838d738c6a769c9b507c27ff33a1bd9c0`: 58 tests, 57 passed,
one failed, zero pending. The actor-replacement negative retained the old
connection and both protected contexts; the same-actor positive passed.

The final five actor controls, together with the 56 existing tests, were run
against unchanged Main runtime source: 61 tests, 57 passed, four failed,
zero pending. The stop-drain control reached the existing 5-second test deadline
because the old runtime never attempted to stop; no deadline or retry changed.
The early-frame control observed publication before the lifecycle effect, and
the disabled-rollout/hydration control retained the previous actor projection.

An initial incorrect local file path prevented a patch from being applied; an
accidental unchanged-source repeat is retained separately. It is not green
candidate evidence. The original baseline and its failures remain retained.

The first corrected runtime run passed all 58 then-current focused tests.
The final Windows Node 24.22.1 cohorts passed 920 tests across 53 distinct spec
files, with zero failed or pending tests: core auth, Tenant, Workspace and
Realtime, all messaging specs, feature Workspace/member management and right-panel
notifications. All five actor controls passed. No fixture repair, assertion
relaxation, retry or deadline change was needed.

The same 920 tests passed under repository-pinned Linux Node 24.22.2/npm 11.17.0,
with zero failed or pending tests, followed by the ordinary production build
and theme sanitizer. Existing bundle/style warnings remained unchanged.
Host memory pressure delayed this lane until capacity became available; no
acceptance threshold changed.

The runtime/test candidate is
`52ca824c1107abb4d8d6eac50435da1da5dfaa1a`. Independent parent source review
found no merge-blocking issue in this bounded actor lifecycle scope.
Documentation-only component head
`39f1ebdcaa1c3368770c105758b8a63cdcf2111e` was normally merged into reviewed
AuthSession component `5665f35fabb468b855276bfe3f81137bdbd3c6cc` as
`364c8d2c5a62d447a952a2baae4f1f48c9d400af`. Both component runtime and test
Git blobs remain identical; no cherry-pick or history rewrite was used.

The combined source passed 944 tests across 54 spec files on Windows,
with zero failed or pending tests, and a fresh pinned Linux ordinary production
build plus theme sanitizer. The component Linux 920-test result is distinct
from this combined Windows 944-test result.

| Local evidence | SHA-256 |
| --- | --- |
| Main runtime, final 61-test RED report | `bed89a59c43e79782c45b9f405b5b70022e7a214e8fbaae21a682712e6252a22` |
| Corrected core/messaging 652-test Windows report | `c5dbd1dcf98d0ac21e42f2c4e744e79fbdf9752620aa60c72f475b27f5989897` |
| Corrected Workspace/right-panel 268-test Windows report | `036081ef589ef0aedb2d250cd3e08db3efe10e59e3dc03ad118aac039a136653` |
| Realtime-component Linux 920-test report | `f40ddfb45fbd297b795b23f600b6ca220395fb849ea9845dfb5be6909d4facac` |
| Combined Windows 944-test report | `3b849cc313a82f4e081a9bf70e8039930416719285b52e7c299fd08ba8ec79c8` |
| Combined Linux generated index | `ecd79de3e74cf944aa69679e54c80163a7c9845805587a3cb311649ec5370c7e` |
| Combined Linux generated app marker | `47ce7e2784175b501d25daa94078760f673f6fc447c87789f32986a8dcd54012` |

Published-head protected checks, final independent integration review, official
Sonar Quality Gate, resulting Main acceptance and normal merge qualification
remain separate. Local results do not grant merge or release admission.
## Limits and retained holds

These controls use the real client facade with synthetic AuthSession snapshots
and a fake Realtime transport. They prove local lifecycle, projection clearing,
ordering and stop/start behavior. They do not establish actual cookie/session
identity replacement, SignalR principal replacement or wire-level disclosure,
server revocation, browser rendering, mobile acceptance or staging HTTPS.
Same-user cookie-session replacement and multi-tab session behavior are outside
this bounded actor-identity fix.

The held security and pilot contracts remain unchanged. No held PR is imported,
no upload intake is activated and no formal SEC-ARCH approval or release
admission is inferred. The pilot release remains `RELEASE_NO_GO`.
