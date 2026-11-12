# Thread denials before action observation

Main `89dcc361bbeca1287a6f1938dc5586503f13fb89`, Main CI
`42554889577/2`, Functional collaboration job `128960959444` failed
`MSG-THREAD-DENIAL` at the unchanged 120-second owner deadline. The
original log SHA-256 is
`0593b8a0c6ba5b315cdf7ca253bf0709bf4aaa0e7cb564f0f6ce092790c61714`.
The original provider diagnostic artifact `13646919828` has ZIP SHA-256
`887c3e43b94b9c41e5449b4e1b309b3a86d14522ea5573f31e738a64a81f2436`.

The retained bounded observation shows the authorized GET returning 200
and the root assertion passing. After session replacement, two other
matching GETs return 400 before `OBSERVER_REGISTERED`. No fresh request is
selected, both request and action fail, and the failure DOM/target samples
are unavailable. The original observation remains `FAILED / INCOMPLETE`.
This ordering does not identify the internal request initiator or prove
the application's final redaction. The underlying cause remains UNKNOWN.

The additional Node test exercises this specific ordering using the
existing correlation and fresh-request helpers. It requires both earlier
400 responses to retain their separate ordinals without becoming a fresh
denial or qualifying the post-action DOM hook. It preserves the original
error identity, unavailable target, incomplete outcome, finite privacy
projection and frozen observation after later cleanup activity.

This is synthetic correlation regression evidence only. It does not fix
or qualify the real Functional failure, substitute an earlier denial for
the mandatory fresh GET, or change an action, timeout, retry, observer,
helper, schema, product authorization, coverage scope or quality budget.
The canonical owner and all its assertions remain unchanged. Alternative
acceptance criteria still require Owner judgment. Historical MSG-01,
Thread and HTTP500 causes, held promotions, formal finding decisions,
actual-environment acceptance and Student Council RELEASE_NO_GO remain.
