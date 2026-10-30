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
The final Windows Node 24.22.1 cohort passed 652 tests across 37 spec files,
with zero failed or pending tests: core auth, Tenant, Workspace and Realtime,
plus all messaging specs. All five actor controls passed. No fixture repair,
assertion relaxation, retry or deadline change was needed.

Runtime/test candidate commit:
`52ca824c1107abb4d8d6eac50435da1da5dfaa1a`.

| Local evidence | SHA-256 |
| --- | --- |
| Main runtime, final 61-test RED report | `bed89a59c43e79782c45b9f405b5b70022e7a214e8fbaae21a682712e6252a22` |
| Corrected 652-test cohort report | `c5dbd1dcf98d0ac21e42f2c4e744e79fbdf9752620aa60c72f475b27f5989897` |

The optional Linux production build remains pending while host memory is
constrained by concurrent acceptance work. Protected checks, independent
review, official Sonar Quality Gate, Main acceptance and normal merge
qualification remain separate. These local results do not grant merge or
release admission.

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
