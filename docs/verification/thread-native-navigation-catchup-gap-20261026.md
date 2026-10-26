# Thread native navigation during routine catch-up

Status: test-only controlled reproduction; production correction is not included.
The Student Council release remains **NO_GO**. Existing Owner holds remain in
force. This record establishes a reachable local mechanism, not the initiating
cause of the hosted failure or every historical Thread failure.

## Immutable source and hosted trigger

The production baseline is Main
`4c0d468582456a2f444eb82a02d72db3687c30f8`. Its initial Main workflow
`40252112986/1`, Extended collaboration job `121350152575`, failed
`FUNC-MSG-001 / MSG-THREAD-DENIAL` at the authorized root assertion in
`collaboration-owner-journey.spec.ts:401`, after a selected HTTP 200. The
restricted-session transition and fresh denial request had not been reached.
This is distinct from the historical 120-second fresh-request failure.

The bounded failure artifact `12621028773` has ZIP SHA-256
`1995347aa1387ed21651d69e011c85a88180512304e63ad5e2c61bf29e25890d`.
Independent artifact analysis observed click completion before two authorized
Thread responses, followed by a missing Thread workspace/root. It does not
identify the originating caller, native handler dispatch, or component request
generation. The same Main Fast owner succeeded despite two authorized responses
while its root assertion was pending. Extra GETs alone therefore do not explain
the Extended failure.

## Controlled mechanism

`MessagingFacade.openThread()` searches the current page for an authorized root
before recording navigation intent or issuing its GET. The registered routine
catch-up callback synchronously clears the page before Angular renders the new
loading projection. A previously authorized native button can remain connected
in that interval. Its native `.click()` reaches the actual MessageItem -> Timeline
output chain and `openThread()`, but the empty projection causes an early return.
The later fresh aggregate cannot restore an intent that was never created.

A separate baseline characterization also dispatched two actual
`Messaging.ThreadChanged.v1` notifications after that fresh aggregate. Each
issued a Thread GET and consumed a synthetic HTTP 200 through the real facade.
Neither created a tab or opened the workspace. Background refresh can thus
produce authorized responses without accepting a preceding click. This
characterization is retained as an isolated diagnostic patch and log outside the
repository test suite; the known failure is not asserted as desired behavior.

## Permanent desired-behavior controls

Code-bearing commit: `854876b9d894aea855ac11e2d462bb6ed8ad4bae`.
Test blob: `48872eceaa13d0adf434e9618c60742c0aac9109`.
Only `message-thread-context.spec.ts` changes executable source. Its original
99-test contract block is byte-identical to Main (SHA-256
`f24f0b2029f2b5a78daaa0aec932905e59e29ee85fe5bc9eeda92d1cefc0d063`).

The new live-binding Angular host uses the production MessageTimeline,
MessageItem native click handler and ThreadWorkspace. The facade spy calls
through and does not replace behavior. No timer, second-click recovery,
authorization bypass, assertion reduction, deadline increase or retry is added.

The new 18 controls require or establish:

- A single connected, previously authorized click during routine clearing must
  eventually issue a fresh authorized activation GET and mount the actual root.
  No old body may substitute for this response.
- Native clicks before the gap and after the fresh render work through the same
  component chain. Background events without any explicit click remain closed.
- Workspace, authorization, session, Tenant and actual current-user/current-Tenant
  refresh boundaries reject old-button navigation while authority is absent.
  Conversation, Workspace and route transitions also reject it before new
  authority arrives.
- Fresh read denial, removal, missing root and actual HTTP 403 aggregate denial
  do not restore navigation. A freshly authorized ReadOnly participant can read
  the Thread while its reply editor remains disabled and dispatch emits no POST.

Command:

```bash
npm --prefix frontend test -- --include=src/app/features/messaging/message-thread-context.spec.ts
```

The final permanent suite produced **116 PASS / 1 FAIL, 117 total**, with no
skips or retries. The new controls produced **17 PASS / 1 desired-behavior FAIL**.
The failure observed `{ freshAuthorizedReads: 0, roots: [], body: null }` instead
of one fresh read, `root-a`, and the visible synthetic root body. The failure is
intentional regression evidence; this branch is not a green release candidate.

Earlier evidence is preserved separately: one initial TypeScript overload
compilation error; the baseline characterization run (116 PASS / 1 desired FAIL);
and a first HTTP-denial fixture error that incorrectly expected a messages GET
before successful detail authority (116 PASS / 2 FAIL). The fixture was corrected
to require no downstream messages GET after detail HTTP 403. None is reclassified
as a successful first attempt.

## Environment and limits

Local execution used Node `24.19.0`, npm `11.9.0`, Angular core `22.2.3`, Angular
build/CLI `22.2.4`, Vitest `5.0.5`, TypeScript `6.0.3` and jsdom `30.1.3` from an
existing dependency installation. That installation has `fast-uri 3.1.8`; the
current lock requires `3.1.9`. The two lock package-record sets differ only at
`node_modules/fast-uri`. This is supporting local mechanism evidence, not
current-lock qualification. A reviewed candidate still needs its own supported
locked installation and exact-source protected real-backend owners.

The native mounted DOM dispatch is Angular/HTTP-testing execution, not trusted
Playwright input or real ASP.NET Core, PostgreSQL, Outbox or SignalR transport.
It establishes neither physical device nor Japanese IME acceptance. It does not
prove the hosted click entered this interval. No production code, Composer draft
strategy, retained-tab selection behavior, security contract, workflow, owner
assertion or fresh-request requirement changes here.
