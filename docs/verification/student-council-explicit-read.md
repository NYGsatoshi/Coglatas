# Student Council explicit read action

The current DM, Group DM and Channel headers provide an explicit `Mark as read`
command through the existing participant-private read endpoint. It marks only
the newest confirmed, non-deleted main-timeline Message already loaded in the
currently authorized conversation. Empty, malformed, pending and thread-reply
rows never supply a cursor. Fractional server timestamp ticks are retained;
equal timestamps do not require a UUID tie-break because the existing server
read sequence uses `CreatedAt.UtcTicks`.

The frontend sends no automatic read command based on visibility or scrolling.
It blocks duplicate pending commands, cancels on existing scope/authorization
boundaries, clears protected state for masked or explicit access denial, and
shows a safe unconfirmed failure for unknown outcomes. It never claims unread
zero from a successful POST. The participant's inbox rows and counts are
refetched from the server; newer or unloaded messages may remain unread.
Late older list responses cannot replace that refresh. The active timeline,
thread and draft are preserved on ordinary successful read marking.

Focused unit and mocked browser cases cover cursor precision, current scope,
denial, failure, duplicate prevention, authoritative refresh, keyboard use and
responsive controls. Mock responses do not establish backend compatibility,
participant privacy, actual PostgreSQL persistence, or public pilot acceptance.
Those require separate current-source backend and deployment evidence. No API,
database, role, authorization meaning or Group thread activation is introduced.
