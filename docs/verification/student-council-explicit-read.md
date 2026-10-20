# Student Council explicit read action

The current DM, Group DM and Channel headers provide an explicit `Mark as read`
command through the existing participant-private read endpoint. The frontend
supplies the newest confirmed, non-deleted main-timeline Message already loaded
in the currently authorized conversation. Empty, malformed, pending and
thread-reply rows never supply a cursor. Fractional server timestamp ticks are
retained; equal timestamps do not require a UUID tie-break because the existing
server read sequence uses `CreatedAt.UtcTicks`.

The existing backend stores the cursor ID and its `CreatedAt.UtcTicks` as the
read sequence when advancing, and also sets `LastReadAt` to `clock.UtcNow`.
Current unread counts and inbox filters compare that `LastReadAt` value.
Consequently, messages not loaded in the browser but created before the command
may also be counted as read. This action introduces no backend semantic change
and promises neither a single-message effect nor a strict cursor-only prefix.

The frontend sends no automatic read command based on visibility or scrolling.
It blocks duplicate pending commands, cancels on existing scope/authorization
boundaries, clears protected state for masked or explicit access denial, and
shows a safe unconfirmed failure for unknown outcomes. It never claims unread
zero from a successful POST. The participant's inbox rows and counts are
refetched from the server. The UI retains the exact confirmation caveat:
`Loaded messages marked as read. Refresh the list if its count is unavailable.`
Late older list responses cannot replace that refresh. The active timeline,
thread and draft are preserved on ordinary successful read marking.

Focused unit and mocked browser cases cover cursor precision, current scope,
denial, failure, duplicate prevention, authoritative refresh, keyboard use and
responsive controls. Mock responses do not establish backend compatibility,
participant privacy, actual PostgreSQL persistence, or public pilot acceptance.
Those require separate current-source backend and deployment evidence. No API,
database, role, authorization meaning or Group thread activation is introduced.

Frozen frontend `5933f8f9` has a fresh production build, eighteen mocked browser
passes with zero skips/retries and ten source-bound five-width originals.
Its earlier identical facade/class at `6978f5bb` passed 328 owning/adjacent
units, 59 architecture checks and four license-guard contracts; those checks
were not rerun at 5933. The subsequent frontend production change is only
intrinsic button alignment. Separate helper `276e8194` prepares a real third
cookie user unread `0 → 2 → UI command → 0` journey, persisted cursor, directory
reload and unchanged other-user counts/cursors. Licensed f4e run `39366803248`
failed at first opposite Group body rendering after selected wire receipt;
the third-user read/isolation stage was **NOT_REACHED**. The later repair
integration runtime is **NOT_EXECUTED**; no actual read/privacy success is
inferred from this failed run or from the mocked frontend cases.
