# Task parent Project request ownership

The Task aggregate loader captures its authorization generation, mounted Task
generation and selected Workspace before dispatch. It rechecks all three before
using the Task result to dispatch an uncached parent Project read and before
applying the combined result or its failure. A Workspace replacement or clear
can precede deferred effects and Realtime teardown; that interval must not allow
the older request to dispatch another read, populate history or change current
loading/error/denial presentation.

The helper retains its original Task/Project identity and captured Workspace
checks, cached-parent shortcut, parent response validation and current 401/403/
404 mapping. A same-Workspace label refresh retains normal requests and history.
Cold null-to-selected Workspace hydration still requires fresh authorization;
the older aggregate is not disclosed. This is a client request/apply guard;
server authority and current resource scope remain mandatory.

New focused controls reproduce stale uncached dispatch and already-dispatched
parent 200/401/403/503 application for Workspace clear and replacement before
effects run, with an ordinary same-Workspace label control. Actual Angular
services and HttpTestingController are used, with Realtime/history doubles.
Existing Projects/My Tasks/hydration tests and assertions remain unchanged.
This does not establish actual backend/browser/Realtime teardown, every detail
command guard, atomic authorization or a historical MSG/Thread/P0 root cause.

Exact-head execution, protected P0, independent review, all required checks,
original official PR Gate and current/resulting Main qualification remain
separate conditions before normal integration. No Owner hold or formal finding
disposition is changed.
