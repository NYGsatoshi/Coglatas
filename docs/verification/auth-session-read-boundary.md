# Session hydration response boundaries

An auth read admitted before a terminal/local session clear could finish later
and restore the old user, capabilities or tenant projection. An old 401 probe
could also clear a newer login through the real auth interceptor's downstream
snapshot decision. Existing server authorization is not bypassed by this bug.

AuthSessionFacade captures an in-memory boundary at subscription time for
bootstrap, current-user, status and tenant reads. Explicit session clear, login
start and successful login establishment advance that boundary. The successful
login retains its new token for its own subsequent tenant hydration; reads begun
during the pending login retain their prior token. Obsolete success/error
responses cannot patch or clear the current projection or dispatch subsequent
hydration. An obsolete pre-login CSRF continuation cannot dispatch a login after
a later clear. Already dispatched login responses also respect a later boundary;
this does not undo a server write.

Obsolete session reads emit the current snapshot and obsolete tenant reads emit
null. They do not complete empty, so firstValueFrom and the global 401 interceptor
retain normal settlement. A current terminal 401 or anonymous status still clears
state. A separate bootstrap loading owner prevents old finalize callbacks from
resetting a newer bootstrap. Parallel same-session status and tenant reads remain
valid; no global latest-read-wins ordering or authorization policy is introduced.

Twenty-four new HttpTestingController cases use the registered real auth
interceptor and concrete facade. The nineteen-case comparison with unchanged
Main88f8648 produced 17 failures and two valid positive controls. Independent
review found an additional login-establishment gap in initial candidatee6540a4:
reads begun during its pending login could still replace the newly established
session. The full twenty-four-case comparison there produced 19 passes and five
failures; those results remain retained. The corrected d9572fcc runtime passes
136 auth/tenant/Workspace/realtime cases across eleven spec files, zero pending,
on both Windows Node24.22.1 and pinned Linux Node24.22.2. Controls cover late200,
401/403, anonymous status, stale500, loading ownership, no obsolete follow-up/POST,
fresh login survival and later legitimate terminal denial. A preliminary CLI
multi-include invocation was rejected before test execution; the explicit brace
glob selected the intended eleven files without changing assertions or timeouts.

Runtime d9572fccb9d7ec4de692f372913efa88f303695e passed the ordinary production
build and theme sanitizer in repository-pinned Linux Node image
e7add2a490bb17d97b485b8641bd88455fff7839ade0f5eba493c3508124d413,
Node24.22.2/npm11.17.0. Existing bundle/style budget warnings remain. Final
published-head CI, official Sonar gate, independent review and resulting Main
qualification are separate requirements.

No threshold, assertion, timeout, retry, server endpoint, cookie, CSRF policy,
permission or enforcement authority changes are made. This repair does not
establish arbitrary same-session read ordering, cross-tab cookie atomicity,
server rollback, transport-principal replacement, formal Pilot acceptance or
release permission. All Owner HOLDs remain in force.
STUDENT_COUNCIL_PILOT=RELEASE_NO_GO.