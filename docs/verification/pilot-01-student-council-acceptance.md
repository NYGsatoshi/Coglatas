# Pilot-01: Student Council cross-session acceptance (implementation candidate)

Status: **Draft PR, no merge or deployment authorization**. This candidate adds
synthetic, isolated, real-browser acceptance verification to the existing
licensed ASP.NET Core + PostgreSQL P0 runner. It does **not** imply readiness
for school-user or public access.

## Executable scope

The required real-backend smoke test
`creates and activates a Workspace through the delegated real-backend capability`
creates a run-owned Workspace, then a named **private** Workspace channel.
It uses real Angular UI to persist an original Message and canonical Thread
reply before a second browser session authenticates as another existing
synthetic actor.

Pilot-01 extends the existing Workspace member lifecycle with five verified
checkpoints on the **same Workspace and Conversation IDs**:

1. Before Workspace admission, independent actor's scoped channel directory,
   private history, Message Thread and Files inventory are denied.
2. After Workspace admission, Files list is permitted but the uninvited
   private channel remains absent from metadata and its history and Thread stay denied.
   Owner uses the **real Angular channel member picker**, which issues the
   CSRF-protected scoped admission API. Only after server confirmation do
   canonical message and thread reads succeed.
3. After the Workspace role is downgraded to ReadOnly, previous history and
   File listing remain readable while a fresh CSRF-authenticated Message POST
   is refused, even if the stored Conversation participant flags are stale.
4. After the owner restores the current Workspace role, a new POST succeeds.
5. After Workspace membership is removed, scoped channel metadata, history,
   Thread and Files listing become unavailable again; authenticated Message
   POST is also denied despite previously valid channel membership.

All actors are synthetic `@example.test` seeded accounts. The negative HTTP
calls use Playwright's isolated browser cookie context; they do not intercept
or stub the backend. No broad permission grants, RLS/migration changes or new
product API are required. The existing seeded workspace and its new channel
are cleaned up through the already-owned archived Workspace acceptance flow.

## Protected real-backend P0 retry — run 39784739935

The protected acceptance execution on candidate
`b75e8b17b8da774bb0c10484f511ee68c0db3988` returned five passing and
three failing Playwright smoke tests. Trace evidence shows that the manager
admitted the recipient into the Workspace before a realtime refresh removed
the channel invitation candidate from the DOM. The 120-second test timeout
then prevented timely Workspace cleanup.

The next candidate waits for the real authorization outbox to settle and
for the authenticated Angular UI to reconnect before opening the invitation.
The candidate click is bounded to 15 seconds to allow normal cleanup if the
dialog becomes invalid again. This is not an unauthorized retry, a mocked
event, a changed permission grant, or an increase in overall test timeout.

The Hub-degraded smoke now explicitly selects the authorized all-Workspaces
scope before waiting for My Tasks HTTP reads; a fresh session without a
selected Workspace deliberately does not issue a current-Workspace query.
PR03C independently verifies that the revoked Workspace is absent from the
authoritative directory and the available count matches *other* current
authorized Workspaces. The prior global zero-count assertion was affected by
the Workspace left behind when the Pilot-01 test timed out.

These modifications are executable test corrections only. All protected
P0 stages remain **NOT QUALIFIED** until another exact-head live backend run
passes with no skipped Pilot-01 checkpoints.

## Existing tests, no duplicate implementation

The same required P0 manifest continues to run its existing smoke for actual
Direct Message creation and persisted reload, real Group Chat member
remove/re-entry and WS role revocation, and File grant reauthorization. The
Pilot-01 extension ties the named private channel to a **second session and
Workspace member lifecycle**. It does not assert that one user performed
unrelated DM and File upload actions in the same transaction.

## Verification gates

- [ ] Exact PR CI passes (Angular, .NET, PostgreSQL, ReSharper, security).
- [ ] Licensed real-backend P0 acceptance runs this modified smoke on the
      **exact candidate SHA**, with Playwright's non-static backend guard,
      no skipped steps and no intercepted API responses.
- [ ] Cross-tenant and SignalR transport/fan-out adversarial proof for
      SEC-17 / issue #690 is separately qualified. HTTP denial alone is
      **not** real-transport non-delivery evidence.
- [ ] Files signed URL/download grant expiry and post-revocation proof for
      SEC-16 / issue #689 is separately qualified. This test covers scoped
      Files **listing**, not every content/download path.
- [ ] Physical IME/focus, HTTPS staging access isolation, rollback/backups,
      tenant onboarding and authorized owner approval are completed.
- [ ] SEC-ARCH and product RLS / data model activation holds are resolved
      only by explicit Owner/spec approval, not by this PR.

## Protected P0 UI-synchronization regression

The first protected `suite=p0` execution ([run #39780390259](https://github.com/NYGsatoshi/Coglatas/actions/runs/39780390259))
at commit `54fc249a92cb3806f65ffa40cdf9f79e14620f89` reported
7 Playwright smoke tests passing and one failing before any Pilot-01 private
channel admission checks. The real Angular channel creation button was clicked
while its SignalR status was `Reconnecting realtime updates.`; the directory
then refreshed and the new-channel dialog was no longer present.

The acceptance helper now requires the existing UI's
`Realtime updates connected.` state (bounded to 30 seconds) after Workspace
creation and before the channel-create action. The channel must still be
listed with current authority and the button must be enabled. This does not
force dialog state, bypass authentication, retry a write, or turn a denied
response into success. **A new protected real-backend P0 run against the exact
post-fix commit is still required; source-level Preflight cannot prove it.**

Helper-only changes to any Pilot-01 TypeScript module must trigger the P0
preflight on pull requests and Main. Preflight also asserts both new negative
access boundaries are still present in the real-backend acceptance journey.

No CI threshold, required-test inventory, package version or deployment setting
is changed. The existing trusted P0 runner continues to use the protected
Syncfusion license environment, and the added evidence contains only sanitized
boolean/status summaries without account tokens, file contents or session IDs.
