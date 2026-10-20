# Main381 Messaging diagnostics

The fresh Main `3816908c6a1bae3cb79128af9f9dd3c6633cf50f` run
`39377066283/1` failed its Fast collaboration owner `FUNC-MSG-001` in job
`118478691254`. The `MSG-THREAD` step filled the second-root draft, then observed
an empty composer at the unchanged 15-second value assertion. The next send
and POST were not reached. Earlier MSG-01 through MSG-04 passed. Full and
licensed successes are separate observations and do not waive this failure.

The retained capture contains status-only network rows, a generated diagnostic
panel and no native trace, authenticated product screenshot, composer history
or timestamped authorization control events. Its earlier 401 rows cannot be
correlated to the fill after intentional logout/login. The hosted cause is
**unproven**. No timeout, retry, acceptance oracle or product authorization is
changed by this diagnostic extension.

## Bounded observation

New browser observations use nested schema version 2. The independent JavaScript
and Python validators still accept historical version 1 with its exact original
fields; they do not invent new evidence for old observations. Both versions keep
the existing Functional gate outcomes and attachment/diagnostic size limits.

The observer records up to 48 structural events from the current document:
initial state, composer mount/removal, input occurrence, focus and blur. Each row
contains a wall-clock millisecond observation and fixed booleans for composer
presence, enabled draft/send controls and draft focus. It never reads draft
values, input event data, protected DOM text, headings or product pixels.
Connected/Reconnecting are inferred only from the existing presentation classes;
other or absent indicator states are unobserved, not guessed.

A separate 48-entry buffer retains only the fixed SignalR invocation tokens
`AuthorizationInvalidated` and `AuthorizationStateChanged`, plus observation and
socket-open times. It accepts only same-origin `/hubs/app` WebSockets, drops
binary/oversized/malformed/non-control data and never persists URLs, headers,
identities, raw frames or event payloads. Frames from sockets opened before the
current document are excluded, including late arrivals. Same-document Angular
navigation does not discard the socket's observations. `webSocketObserved=false`
is distinct from an observed socket with no retained control tokens.

The document also records whether optional session storage permits access to
its length and a temporary, uniquely named synthetic write probe. The probe
contains only `1` and is removed on a best-effort basis. These booleans are not
proof that every real draft read/write succeeded. No existing storage value or
key is read or enumerated. Probe failure cannot fail or change product commands.

The first failed-step snapshot freezes before the owner's cleanup; unavailable
capture is explicitly null. Success observations are after cleanup and are not
presented as failed-step state. The existing generated panel remains a structural
diagnostic rather than an authenticated product screenshot. Observation does
not determine or promote Functional acceptance.

## Interpretation limits

Wall clocks and asynchronous observer delivery do not establish atomic ordering
between the browser, Node and server. A received control token does not prove
that the application applied it or why the server emitted it. No SSE/long-polling
control frames, server authorization generations or precise request-to-input
correlation are captured. Empty/truncated/unavailable buffers cannot prove the
absence of authorization changes. These diagnostics support investigation and
must not be used to retain protected state across a boundary.

A separately reproduced optional-storage draft-recovery gap is a different
source-backed defect. It must not be assigned as the hosted cause without
corresponding execution evidence. The pilot remains **NO_GO**; this document
neither qualifies resulting Main nor authorizes target deployment or school use.
