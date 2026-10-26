# SEC-17 ReadOnly reconnect and Thread-write controls

This test-only candidate extends current role-change verification for #690.
It is independent of held #1326, #1399 and #1406. Their code and dispositions
remain unchanged. No production authorization, RLS, schema, migration, grant,
workflow, retry, deadline or release policy changes.

## Exact bounded contract

`SecurityArchitectureSignalRReadOnlyReconnectTests.ReadOnlyReconnectRetainsReadsButDeniesThreadWriteUntilCurrentRoleRestoration`
uses the current actual Web entry point, migrated temporary PostgreSQL, real
cookie/CSRF sessions, WebSocket Hub and Outbox dispatcher. It reuses the supported
private ProjectChannel fixture, not a mock or a public target.

The member first posts a successful Thread reply and receives the root, reply
and Thread hint with the owner. Only the synthetic participant's persisted
Role and CanPost fields then change to ReadOnly/false. CanRead and the existing
create-Thread flag remain true. This is a test-owned database arrangement,
not proof of an administration UI or HTTP role-change workflow.

Without another login, the retained session opens a new WebSocket. Both the
old and new sockets must currently subscribe. HTTP timeline and Thread reads
must return the exact original identities. A Thread POST must return the
existing masked HTTP 400 envelope, create no Message/member/Outbox change and
record the existing conversation_post_denied audit. The negative runs twice,
on either side of an actual owner-produced reply whose MessageCreated and
ThreadChanged events must reach all three live sockets.

Restoring only the fixture participant's Member/CanPost state must allow the
same session to post a new reply and all sockets to receive its new events.
Every reply hint is selected from newly produced Outbox identities; an old
hint cannot qualify a later send. Protected snapshots are hashed in memory;
raw message bodies, frames, credentials and routing are never emitted.

## Evidence and limits

Authoring baseline: Product Main
`bccc119eb9f15f490c4f2e739c55eab3bcfeb4b2`.

Local build and PostgreSQL execution are NOT_EXECUTED: the managed execution
environment was starting/offline and rejected shell execution as unavailable.
Hosted exact-head PR CI, named PostgreSQL execution, independent review and
resulting-Main qualification are required separately. A skipped conditional
PostgreSQL fact or an unrelated green job supplies no runtime credit.

This adds a bounded participant-role reconnect control. It does not certify
Workspace role administration, every actor/event/capability combination,
simultaneous revocation races, complete SEC-17, formal SEC-ARCH/RLS mapping,
target HTTPS, recovery, physical IME or pilot acceptance. No Owner HOLD is lifted.
Student Council release remains RELEASE_NO_GO.

## First-attempt hosted execution and collector failure

Source HEAD `e785b14332e72be3e9c0f95caa4d86df7bff1965`, actual tested merge
`2f9acb10d31c268744529665072c6f9675e3c5e3`, [PR CI 40420206426 attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40420206426):
**FAILURE, 15 successful jobs and one failed backend job**. No retry was requested.

[Backend 121913512183](https://github.com/NYGsatoshi/Coglatas/actions/runs/40420206426/job/121913512183)
records PostgreSQL 18.6, the named new fact PASS in 12 seconds, 321/321 selected
backend tests passed and successful TRX verification. Sanitized SEC-ARCH evidence
capture then failed, followed by the missing-receipt upload failure. The named
runtime positive does not qualify the overall PR.

The collector intentionally rejects any observed SecurityArchitecture method
outside its closed reviewed catalogue. This new class was initially omitted.
The follow-up appends exactly this one case (350 to 351 expected cases), retains
every old entry and preserves rejection of unknown methods. Deterministic
controls reproduce the unregistered-case rejection and require this new case's
missing, skipped, failed and aborted results to remain UNVERIFIED, FAIL or ERROR.
No outcome, threshold, binding check or incomplete-coverage qualification is
relaxed. The collector remains REPRESENTATIVE_ADVISORY with approval and trusted
attestation unverified; neither 351 cases nor this registration certifies SEC-17.

The follow-up requires its own exact-head hosted checks and named PostgreSQL
execution. It does not reclassify the first attempt or transfer its result.
