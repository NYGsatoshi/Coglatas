# Thread native navigation during routine catch-up

Status: **IMPLEMENTED_NOT_VERIFIED** production candidate, with the immutable
baseline reproduction retained below. Hosted qualification is still required.
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

## Baseline permanent desired-behavior controls

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
prove the hosted click entered this interval. The baseline comparator changes no production code, Composer draft strategy,
security contract, workflow, owner assertion or fresh-request requirement. The
subsequent candidate below changes only local Thread navigation.


## Scoped production candidate

A native message-button click can retain only a previously readable root ID,
trigger ID and optional anchor within the exact routine catch-up generation,
actor, Tenant, selected Workspace, Conversation and route. No body, Composer
draft, send command or server grant is retained. After the fresh conversation
aggregate, normal `openThread` re-finds the eligible root and obtains its body
through a distinct authorized HTTP request. Background refresh alone still
cannot open a tab. Close, newer navigation, denial and hard scope changes veto
an older queued intent.

Both messaging page templates and the expanded Thread workspace capture the
last-rendered navigation binding. The binding is a client identity check, not
server authority. It includes a boundary revision; only a routine refresh that
starts from an already readable projection preserves it. Recovery from denial
or other unreadable state advances that revision. Native root and retry buttons
pass the captured binding. An unbound programmatic call may still read a fresh
current root through the existing API, but cannot queue during the empty gap.

Independent review found two defects in the first local candidate: an unbound
expanded-workspace root button could retarget old actor input into a newer
actor's catch-up, and detail HTTP 403 followed by routine recovery could reuse
an old binding. Both produced a deterministic unexpected GET (one versus zero).
A separate unbound-command control also failed. These were client navigation
boundary defects; no unauthorized body or server authorization bypass was
established. The candidate now binds the expanded root/retry paths, rejects
unbound deferral and advances the binding boundary after unreadable admission.

The original 99 contract tests remain byte-identical. The original native host
now also captures the actual render binding; one exact-call argument assertion
was strengthened to include that binding without removing a behavior assertion.
The original baseline RED commit and dependency limitations remain immutable;
the final host uses a new API and is not claimed to compile on unchanged Main.
Additional controls cover readable projections after actor/scope changes with
reused root IDs, denial/regrant, close/tab veto, newer roots, overlapping catch-up,
pending no-send, fresh/background HTTP 403, and accepted overlap with two HTTP
200 responses. A queued read that becomes ReadOnly must show only a fresh
readable root while the editor/send controls stay disabled and no POST occurs.

The corrected candidate passed 137 owning controls using the reused dependency
installation, before the last ReadOnly control. A fresh installation using the
pinned npm 11.17.0 and exact lockfile completed successfully, including fast-uri
3.1.9. At code-bearing `73351e63ee947aa7a0d7dffc638de4e19783ae4c`, the fresh locked
installation passed all 154 frontend files / 1,848 tests, including the 138
owning controls, and the production build. Architecture 63 and license 4
controls passed using the supported execution permission after the diagnostic
comparison below. Independent exact-head hosted CI is still required. Earlier fixture failures remain
recorded separately: incorrect closed/permissionDenied expectations, null versus
undefined empty-state assertions, and a Workspace fixture missing its actual
boundary clear. Correcting those fixtures did not change product authority or
canonical owner assertions.

No server authorization, migration, RLS, workflow, deadline, owner selection or
retry policy changes. Composer input-preservation #1361 remains held and is not
integrated. The hosted initiating cause remains UNKNOWN: the controlled gap is
reachable and consistent with the failure, but actual hosted handler dispatch
and generation were not captured. The original 120-second missing-fresh-request
failure remains separate. Existing unmodified real-backend canonical owners,
independent exact-head CI and resulting-Main qualification remain required.


## Local execution permissions and Main synchronization

The first local architecture invocation reported four file processes passed and
one failed; the first license invocation also failed. Direct unchanged tests
exposed missing child stderr despite the expected failure exit. A fixed public
14-case child-process matrix recorded EPERM in all restricted executions. The
same Node, source and inputs with the supported execution permission produced
zero EPERM errors and all expected stderr, including the actual missing-addendum
CLI message. No external network operation, test retry, deadline or repository
source change was used in that permission comparison. The original failures
remain failures. With that execution configuration, the unchanged 63 architecture
and four license controls passed. The precise kernel/syscall cause is unknown;
only the controlled permission difference is established.

External Main advanced through dependency PRs1420/1409/1410 and Docker frontend
PR1422 while this correction was being prepared. Normal merge commits preserve
those changes without resetting either history. The complete frontend tree is
byte-identical to the locally validated733 source; current exact-head CI must
still validate the integrated .NET SDK10.0.403 / dependency / Docker inputs.
No successful parent or PR result substitutes for resulting Main qualification.
