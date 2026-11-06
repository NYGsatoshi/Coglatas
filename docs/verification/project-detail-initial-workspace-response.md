# Project detail initial response Workspace binding

The required browser regression exposed a missing navigation recovery after an
initial Board/Gantt denial. Protected data is still cleared immediately, but a
new parent request and fresh Task/workload/member requests can restore authorized
Project navigation. Both operational projections stay denied and empty; neither
is automatically retried. Failed parent revalidation retains denial. Captured
Project/load/authorization/Workspace ownership is checked before follow-up
dispatch and application, so an old response cannot revive a replaced scope.
Ten additional controls first failed on the previous implementation; the original
browser assertions and original facade regression suite remain unchanged.

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
