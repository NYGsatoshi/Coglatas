# Project detail initial response Workspace binding

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

Existing generation, denial, server authorization, operational/Draft distinction,
feature flags and Realtime clear/reload contracts remain. This does not establish
atomic authorization, cancel already dispatched HTTP, implement guards for every
detail command/refresh, or prove the cause of historical MSG/Thread/P0 failures.
Independent exact-head native, protected browser, required-check, original Gate
and resulting-Main qualification remain separate requirements.
