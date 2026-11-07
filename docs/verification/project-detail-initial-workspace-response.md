# Project detail initial response Workspace binding

The required browser regression exposed a missing navigation recovery after an
initial Board/Gantt denial. Protected data is still cleared immediately, but a
new parent request and fresh Task/workload/member requests can restore authorized
Project navigation. Both operational projections stay denied and empty; neither
is automatically retried. Failed parent revalidation retains denial. Captured
Project/load/authorization/Workspace ownership is checked before follow-up
dispatch and application, so an old response cannot revive a replaced scope.
Ten additional controls first failed on the previous implementation; the original
browser assertions and original facade regression test bodies remain unchanged.
The original facade fixture now explicitly selects an authorized Workspace;
its assertions are preserved. A cold Active response must not dispatch any
subresource before the first Workspace selection. The actual mobile browser
trace exposed two Board GETs when that response beat hydration. A deterministic
HTTP control reproduces all five premature subresource reads. Requiring the
captured non-null Workspace suppresses them, then hydration starts the fresh
authorized load. No pre-hydration response is adopted or retroactively rebound.
Independent review additionally identified ordinary Realtime events restarting
denied projections between fresh parent and collection responses. Eight controls
reproduce that ordering for Board/Gantt, 401/403 and Task/Project events. Ordinary
events retain denial both during and after navigation recovery; explicit
authorization invalidation still clears and revalidates operational projections.
An ordinary Gantt follow-up queued before a 401/403 is also discarded. Queued
continuations recheck current scope and denial before dispatch; explicit
authorization revalidation still recovers. Separate 401/403 controls first
reproduce the forbidden extra GET while the other 136 controls remain passing.
A current Gantt 401/403 also takes precedence over an active editor or command,
matching the Board's existing definitive-authority rule. It clears protected
generations and pending interaction/queue ownership immediately. Closing the
invalidated editor cannot retry it; explicit authority revalidation still recovers.
The response checks captured Project/load/authorization/live Workspace before
either denial or successful projection application.

The initial detail load captures the selected Workspace identity with its
existing Project/load/authorization generations. It rechecks that identity
before dispatching operational reads, before mapping their joined result, and
before applying a view or recording Continue Working history. Clearing or
replacing the Workspace discards the old response before deferred Realtime
teardown. A label-only update for the same Workspace preserves the load.

The first isolated controls produced four failures and one legitimate-control
pass on unchanged Main implementation: stale Active responses dispatched old
subresource reads, and stale Draft responses populated the old detail/history.
Those controls and assertions remain. Additional controls cover Workspace
changes during operational reads and an unchanged-scope Active result.

A direct route can start while initial Workspace selection is still absent.
Once the first selection is hydrated, a scoped Angular effect starts a fresh
server-authorized load for that route intent. The pre-hydration response remains
discarded. The effect reads Workspace selection only; load side effects run
untracked. Existing route release prevents revival after teardown. Cold Active,
Draft and denial controls qualify fresh requests instead of accepting old data.

The real HTTP-only Workspace/Realtime integration controls also reproduced a
fresh fallback denial overwritten by a delayed initial success. A definitive
401/403 from either the initial load or fallback now uses the existing
authorization-generation and protected projection clearer before presenting
the original denial. Earlier pending loads or catch-ups cannot restore the old
projection. Both arrival orders and fresh authorized recovery are covered.
Repeated definitive denials also invalidate earlier overlapping recoveries when
the current view is already denied. Non-definitive failures preserve the
existing denial presentation. An older Kanban move response cannot release the
in-flight flag or trigger reconciliation owned by a new load/authorization
generation; current move completion retains its existing behavior.
An overlapping schedule denial or durable authorization invalidation releases
the invalidated move's lock without giving its old callback ownership of a new
command. A late successful old move is never applied to a remounted view. Its
committed effect is reconciled through fresh, current-authority board and Task
reads; an in-flight initial reload is restarted, and a newer move keeps the
board refresh queued until that command completes. Route/Workspace replacement
and denial prevent those stale completions from reviving the old view.
Starting a move invalidates earlier successful board refresh responses and
retains a queued read when one was in flight. Such a response cannot clear the
current move's busy state or replace its successful board. A definitive board
401/403 still clears current protected generations before the refresh-order
check, provided Project/load/authorization/Workspace ownership remains current.
Already dispatched reads are retained but cannot restore state after denial.
Explicit Refresh and presentation changes also queue while a move is pending.
The resulting current-authority read preserves the latest selected presentation
and runs after the move succeeds or fails, never across its busy interval.
A moved Task returning 404 also drains an already queued Board read. A still
available Board can recover independently; a Board 404 retains not-found
presentation and its current 401/403 still clears protected state.
Initial Board and Gantt readers apply a current 401/403 immediately, before
waiting for the other joined projections. Their shared guard covers initial
load and activation reconciliation, including the captured live Workspace.
Successful initial Board mapping has no presentation side effects: only the
request that still owns Board state may apply its selected filters. A late old
move queues reconciliation behind a newer fallback move even while the initial
join remains pending; it cannot restart that load or release the newer lock.
A non-definitive initial join failure also retains a later ready fallback view,
including its current move lock and queued reconciliation. This covers either
Task or workload failure followed by current move success or failure. Without
a later authorized ready view, initial failure presentation remains unchanged;
definitive current 401/403 continues to clear protected generations.
Transient failures retain their original
behavior and can be recovered by an authorized initial result. This preserves
the original denial mapping instead of inventing a new 400/404 policy.

Existing generation, denial, server authorization, operational/Draft distinction,
feature flags and Realtime clear/reload contracts remain. This does not establish
atomic authorization, cancel already dispatched HTTP, implement guards for every
detail command/refresh, or prove the cause of historical MSG/Thread/P0 failures.
Independent exact-head native, protected browser, required-check, original Gate
and resulting-Main qualification remain separate requirements.

Further independent review identified the same missing live Workspace binding
in HTTP catch-up and Project-event parent refreshes. These paths now require a
captured non-null Workspace before dispatch and at every response/application
stage; an optional caller guard supplements that scope check instead of replacing
it. Both operational readers also refuse dispatch without a live selection.
Real HTTP-only controls with multiple Workspaces and no preference preserve
selectionRequired, discard unscoped parent results, and wait for explicit
selection before reading fresh projections. No API row-zero adoption is added.

An initial route whose Workspace discovery settles empty or unavailable now
presents the existing localized Workspace state instead of an indefinite Project
spinner. The mounted route intent remains available for later selection and a
fresh scoped request. Discovery retry and the initial scoped load may overlap;
their normal authoritative reads remain allowed. An attempted broad initial-load
coalescing guard broke 15 existing recovery controls and was removed. The
current implementation preserves those original assertions and denial behavior.

A current refreshed Gantt 401/403, including while an editor is active, now uses
the shared immediate protected-state invalidation and fresh parent/collection
navigation recovery already required by the initial readers. Both operational
snapshots remain denied and null until an explicit user retry or explicit
authorization revalidation. Ordinary events, editor close and queued refreshes
do not retry them. Fresh parent 403/503 retains denial. New native controls cover
both Gantt denial statuses, explicit retry, failed parent recovery and the
Workspace states above. They do not prove live transport or historical causes.

The same navigation-only recovery now applies to refreshed Board 401/403.
Additional controls cover both denial statuses, fresh parent success/403/503,
explicit Board retry, and a previously dispatched Move succeeding before or
after the parent recovery. A stale committed Move cannot restart a currently
denied Board even after navigation is authorized again. Existing overlapping
move/refresh assertions remain, with additional fresh-parent denial checks.
Real HTTP-only controls also hold parent, Board and Gantt reads across an
explicit Workspace replacement: already dispatched responses complete without
restoring the old Project or starting old-scope child reads.

The denied Board presentation now exposes the existing explicit Retry board
action after navigation is reauthorized. Real Angular page/HTTP controls for
401 and 403 first retain the missing-button failure, then verify no automatic
operational read, an enabled user action, and a credentialed fresh Board GET
only after that action. Original page and browser tests remain unchanged.

Normal reconnect reauthorizes the server subscription before invoking Project
catch-up and accepting fresh HTTP projections. This existing path is explicit
authority revalidation, distinct from ordinary Task/Project data events. Two
controls run the actual Realtime coordinator with synthetic transport responses:
after Board 401/403 and navigation-only recovery, both operational snapshots
remain denied; reconnect must issue a fresh Project subscription and accept new
parent/Board/Gantt responses before restoring them. An unchanged Workspace
directory need not be reloaded. Early fixture versions assumed that reload or
inspected HTTP before subscription reconciliation completed; those failures are
retained separately. The bounded coordinator wait changes no original deadline.
This verifies client coordination with doubles, not live server/transport or a
new authorization policy.

Review subsequently identified an ordering gap: a successful Board/Gantt read
could restore a denied snapshot while the reconnect parent was still pending.
Eight clean controls failed before the repair. Navigation-only recovery now
joins the current parent and all three collections before starting operational
reads. The established parallel HTTP contract for subscription denial and
explicit authorization invalidation remains, but successful child responses
wait for that same completed parent authorization decision. Parent 403/503
never releases those buffered responses. Four positive controls additionally
require parent 200 and the final collection before either snapshot is restored.
The original 56 facade callback bodies, 61 expanded assertions, 23 page controls,
and original browser assertions remain unchanged. Earlier serial-only and
replayed-false implementation failures are retained; they are not passing runs.

An additional review found that a discarded successful Schedule response could
complete without releasing its in-flight owner after parent 503. Four clean
controls failed with the other 210 passing. Generation-owned finalization now
releases the completed refresh without retrying denied state. Both response
orders must recover on later authorized reconnect and permit explicit Schedule
retry. A separate control holds a newer refresh while an older response completes:
old cleanup must not release that newer owner or bypass its queued refresh.

Further clean controls retained three failures with 215 controls passing:
ordinary Task events could read operational projections after the parent but
before its collections completed, and activation reconciliation could advance
the response generation while leaving an old Schedule refresh owner stranded.
Revalidation admits ordinary operational events only after the full authorized
collection join. Project events reenter Draft recovery only for a ready Draft.
Schedule cleanup tracks its actual refresh owner separately from response
application generations. Both collection-failure response orders and the held
Schedule/activation sequence must pass; older cleanup still cannot release a
newer refresh. No original test body or authority policy is relaxed.

Initial loading preserves the original concurrent HTTP refresh contract.
Successful Board, Gantt and Task responses wait for the initial authorized join
or a completed explicit current catch-up. An initial failure discards buffered
responses; an older non-authority failure cannot revoke an already recovered
view's owner. Four additional clean controls reproduced Project-event and
initial-join bypasses with the preceding 218 controls passing. Serializing all
initial dispatch broke 12 controls; buffering without preserving recovered
ownership broke four. Those failed implementations remain retained. The repair
preserves every original assertion and records current-scope HTTP completion,
not live transport or a change in server authorization.

Ordinary Task refreshes also retain their live Workspace identity while queued
and in flight. Four clean controls failed when that Workspace was cleared or
replaced, with the other 230 passing. The queued callback now checks its load,
authorization and Workspace before dispatch; the held response checks that
same Workspace before applying data or scheduling follow-up work. A normal
same-Workspace label refresh still proceeds. Additional controls hold nonempty
Task responses across initial collection failure and inspect state before
recovery. The resulting four-file cohort has 234 passing controls. This is
client lifecycle evidence with HTTP doubles, not server isolation acceptance.

A later independent review identified Task refresh ownership left behind by a
Schedule command denial. Four added controls reproduced both queued and held
Task refresh failures on 401 and 403, while the preceding 234 controls passed.
The Schedule denial now resets Task refresh ownership when it advances the
authorization generation, using the same reset as the other authority changes.
Stale callbacks still return before dispatch and cannot release a newer flight.
The controls require a fresh Task retry and its coalesced follow-up to succeed
while the denied Schedule remains empty. The initial test-authoring compile
failure and the four original runtime failures remain retained separately.

Overlapping catch-ups require independent ownership for failed parent gates and
successful current recovery. Four additional controls reproduced a missing
Schedule follow-up with either parent order and either child-response order;
the preceding 238 controls passed. A discarded Schedule response now releases
only its own flight and resumes queued work after current ready authority.
Successful parent recovery also resumes a queued follow-up whose previous
flight already completed. Older transient parent failures cannot replace a
newer applied parent projection. Current 401/403 responses still clear protected
state, including an older request completing after a newer success. The first
new fixture's Board query mismatch is retained as an authoring failure, separate
from the four subsequent runtime failures. Original test bodies remain intact.
