# Real Thread authorized-root/catch-up ordering — isolated candidate

Status: **TEST-ONLY / DRAFT / UNMERGED / NOT_YET_HOSTED_VERIFIED**.

Owner: [P0 #1365](https://github.com/NYGsatoshi/Coglatas/issues/1365).
Current Main `0c5f9baace186940f948758b1731fd3ec891c2f3`.

## Actual first-attempt failure (preserved)

[Main CI #40056196940](https://github.com/NYGsatoshi/Coglatas/actions/runs/40056196940),
Full collaboration first job #120760727090 FAILED at `MSG-THREAD-DENIAL`
**before** restricted actor replacement: the root-text matcher failed at its
unchanged 15-second deadline. The selected matching Thread request completed
HTTP 200, with all six retained DTO shape/identity checks true. A second
matching request also completed HTTP 200 before observation sealing, but its
completion relative to matcher rejection is unknown. One post-rejection,
precleanup DOM snapshot found no Thread workspace/loading/error/root. This
does not establish continuous absence for 15 seconds or identify response
admission, request initiators or the cause. Sanitized diagnostic artifact
12545438209 captures the bounded state; no raw session/content evidence is
incorporated.

A same-SHA single-job attempt-2 #120773901268 subsequently passed all three
Full collaboration owners. The overall attempt remained FAILED because the
Full aggregate lacked the other current-attempt domain lanes. The lane PASS
does **not** waive the original failure,
identify a shared cause with historical #1353/#1342 or qualify Main for release.

## Executable bounded control

The new Angular `message-thread-context.spec.ts` control interleaves a
pending authorized Thread GET with the existing registered routine realtime
catch-up callback. It asserts the original GET remains pending until settled,
that protected UI clears before reauthorization, the original HTTP 200 does
not alone establish a rendered root, the conversation and messages are
freshly reloaded, a distinct current-generation Thread GET is required, and
the fully authorized response **actually renders** `thread-root-message`.
No POST, actor switch, credentials, protected body or artificial redirect is
introduced.

This controlled Angular/HttpTestingController test establishes **one expected
race contract**, not the historical production root cause, a real ASP.NET Core
transport PASS or a workaround for missing UI. If the current implementation
fails, preserve the failure and investigate the product transition rather
than modifying assertions, increasing timeouts or introducing retries.

## Remaining release holds

- Independently review and host-run the new test on exact commit SHA.
- Obtain same-SHA real ASP.NET Core/PostgreSQL/SignalR interleaving evidence
  sufficient to determine why the full owner sometimes loses the Thread panel.
- Fix any demonstrated flaw with a first-attempt blocking regression and
  verify current Main Full owner, then separately staging/HTTPS, recovery,
  SEC-ARCH/RLS and actual Owner GO.
- Keep #1365, #1342, #1360 and prior history open or unchanged until justified.

**RELEASE NO_GO**. No product authorization, API, policy, schema, CI gate,
password, environment, cleanup or deployment changes in this PR.
