# Authorized Thread projection failure evidence

Related: #1353. Historical #1342 has not been causally linked.

Main `1301d8128bc5725cbef5fca26c00401031bd2f86`, run `40017100188`,
Fast collaboration job `120634069021`, failed before restricted session
replacement: the initial Thread read returned 200, then the existing 15-second
root-body assertion found no root element. Full and protected mobile outcomes
are separate observations and do not waive this failure. Its cause is UNKNOWN.
No product correction is claimed by this evidence-only change.

## Retained evidence contract

The canonical Functional reporter now carries the existing validated
`restricted-thread-probe-v1` attachment for `FUNC-MSG-001`, including its finite
secondary-cleanup classification. Other owners retain their existing schema.
Old diagnostic artifacts remain readable. New Thread data is validated again by
the Python artifact consumer; malformed, unknown-field, duplicate or oversized
attachments project INVALID with no attached data. The probe stays bounded to
4096 UTF-8 bytes and its observation to 24 events with explicit truncation.
AVAILABLE means schema-valid, not complete, successful, or causally conclusive.
Missing, orphaned or truncated observations cannot establish a cause or isolation.
The underlying owner status, stable mandatory steps, retry count and gate
acceptance continue to come from actual execution.

The initial native root-open action keeps both the existing 20-second HTTP
response wait and the mandatory 200/root-body assertions. A newly emitted
matching Playwright Request is selected after observer registration; its exact
object identifies the awaited Response. Only the bounded request ordinal and
fixed stages are exported. This excludes a response for a request already in
flight before registration. It does not prove that every matching background
request was caused by the click, or that every successful response was applied
by Angular.

An optional initial projection records six boolean DTO comparisons in memory:
object shape, expected root, expected Conversation, summary root, reply
identities and bounded reply counts. These are limited comparisons, not complete
DTO validation or product acceptance. No body, identifier, schema value, URL,
header, cookie, token or native error string is exported. An unavailable JSON
comparison stays null and does not replace the mandatory UI assertion.
The root matcher starts immediately after AUTHORIZED_READ (with a synchronous diagnostic start marker). Body comparison is
launched afterward without joining the functional promise chain: a body that
never resolves cannot postpone matcher startup, failure propagation or later
functional actions. A rejected/unavailable body stays null (unknown). The
observation lease is sealed before failure inspection or publication, so late
completion cannot mutate a frozen receipt. Commit `576e8b6a` awaited diagnostic
body parsing before the matcher and was rejected for changing that timing; its
controls qualify retention only and remain historical evidence.

On a caught failure, one browser evaluation records presence/visibility booleans
for the Thread workspace, loading indicator, error indicator and root. Existing
target flags describe the retained tab. These inspections occur before failure
capture, publication and actor restoration, but after the assertion failed;
they are not atomic snapshots of the failure instant. Network events can still
arrive during inspection. A failed inspection stays null. The first mandatory
error object is preserved even if diagnostics, attachment or restoration fail;
secondary failures suppress unsupported primary-kind attribution.

The structural PNG remains a generated visualization of sanitized metadata,
never authenticated product pixels. Source, image and run bindings remain
independently required. The original failure's missing Thread chronology cannot
be reconstructed retroactively.

## Verification limits

The collaboration owner and its transitive TypeScript/declaration imports can
be checked separately from Playwright's transpilation and test discovery:

```sh
npm run typecheck:functional:messaging
```

This uses the repository's frontend TypeScript dependency and requires both
root and frontend dependencies to be installed. The strict, no-emit check uses
Bundler resolution to match Playwright's extensionless TypeScript imports.
Declaration companions for `.mjs` helpers use `.d.mts`; `.d.ts` companions do
not provide those helpers' types under this resolution. This command is an
explicit local check, with no Required Check or workflow change. It does not
execute JavaScript helper implementations or establish browser, backend,
authorization or first-attempt reliability acceptance.

Node controls replay the exact current owner callback with synthetic DOM and
transport, including an old authorized response preceding the selected new
request, unavailable comparison/capture, and failing restoration. They exercise
retention ordering and primary-error preservation only. They do not qualify a
real cookie, backend, authorization result, browser render or historical cause.
The real canonical reporter and independent Python consumer are exercised with
strict privacy, shape, ordinal-order, size and truncation negative controls.
The existing architecture preflight routes both control suites; no workflow,
product, policy, retry, timeout or mandatory assertion is weakened.

An independently reviewed, exact-source real-stack execution is still required
to observe the missing boundary. A later passing run alone will not establish
or repair the original cause, close #1353, or waive #1342/#1346 and Owner HOLDs.


## Optional relative timing and page boundary flags

New observations align an optional elapsedMs array with the retained events.
Samples use one monotonic test-process clock, integer milliseconds from observer
creation, bounded to 0..3600000. Unavailable, invalid, backward or out-of-range
samples are null and force INCOMPLETE; historical records without timing remain
untimed. These are observer-handler times, not application handler admission or
the matcher engine's internal rejection instant. Existing limits remain 24 events,
24 local request ordinals and 4096 UTF-8 bytes; truncation never proves a cause.

AUTHORIZED_ACTION_STARTED/COMPLETED delimit the original native activation action.
ROOT_ASSERTION_STARTED/PASSED/FAILED mark the original root matcher; its failure
marker precedes asynchronous precleanup inspection and rethrows the identical
primary error. No new awaited diagnostic work, deadline, retry, request, body read
or acceptance assertion is introduced. Response ordering can distinguish a
response observed after rejection from one observed before it; it does not prove
which application call initiated that request or whether Angular applied it.

The one precleanup DOM snapshot adds four fixed booleans: dm-page presence,
conversation-surface presence and the existing thread-open/group workspace CSS
classes. No DOM text, identities, arbitrary attributes, URLs or product globals
are exported. The original eight presence/visibility flags remain. Historical
eight-flag records remain readable; new twelve-flag records are exact shapes.
Neither snapshot establishes continuous absence across the 15-second matcher.

There is no production facade diagnostic hook in this change. Activation
admission, request initiator, generation/reset reason and response-drop branch
remain UNKNOWN. This is future diagnostic capability, not a product correction,
a causal resolution of any historical failure, or release qualification.


## An old denied request may finish during a required new activation (2026-10-25)

Exact docs-only Main `f31b15f2b42d3b83b5c1ffae5ea304922e10cc16`
[Main CI attempt 1 #40193736229](https://github.com/NYGsatoshi/Coglatas/actions/runs/40193736229)
failed the real **Extended Collaboration** `FUNC-MSG-001 / MSG-THREAD-DENIAL`:
[Job #121172675286](https://github.com/NYGsatoshi/Coglatas/actions/runs/40193736229/job/121172675286)
ran out the unchanged 120-second owner deadline waiting for a **fresh**
post-activation Thread GET. Other Fast/Full owners succeeded on the same
source; they cannot waive Extended. The authenticated positive Thread GET
returned 200 with all six shape checks true. The actual downloaded and
SHA-256-verified retained browser diagnostic
[artifact #12599865031](https://github.com/NYGsatoshi/Coglatas/actions/runs/40193736229/artifacts/12599865031)
(`c4e2f7447ba8326cda9d0338d057806d9a955ebf58141747e4f58735681ef2a0`)
contains one **old Thread GET with HTTP 400** whose request started before
`OBSERVER_REGISTERED` and whose response arrived after
`ACTION_STARTED`. The original tab was visible/selected/pointer-targetable
at the pre-action sample, but no newly started Request was observed after
activation. The failed owner never asserted the current restricted HTTP 400,
denial envelope or protected UI clearing. After the global deadline,
`failureDom` and `failureTarget` were unavailable and RESTORE failed.
The older GET's 400 must **never** substitute for the mandatory fresh
post-action GET. The cause of missed activation remains UNKNOWN.

The optional **pre-action-denied DOM snapshot** is a narrow diagnostic
response to that specific gap. During the existing native action, if an HTTP
400 arrives for a matching Thread GET which **started before** the new
`OBSERVER_REGISTERED`, the bounded Node observation invokes at most **one**
read-only browser DOM sampler. The 12 boolean fields use exactly the already
approved page, conversation, workspace, loading, error and root flags; the
callback does not await response bodies, add requests, change the click,
supply a denied response, retry, wait or change any acceptance deadline.
The result appears only as optional `initial.preActionDeniedDom` and stays
`null` if the event/capture did not complete. Neither a missing sample nor
a captured sample establishes an application's internal authorization or
render ordering; the browser evaluation occurs **after** the observed 400,
not at an atomic handler boundary.

Node and independent Python validators reject unexpected/private fields,
nonbooleans, shape collisions, impossible visibility, mismatched schema,
and a captured sample without an actual ordinal-bound older GET whose HTTP
400 arrived **after** the new action marker. Historical receipts lacking
this field remain valid without gaining evidentiary credit. The original
24-event/24-request, 4096-byte and elapsed-time limits remain unchanged;
the existing summary/required Functional owner still fails closed on
timeouts, missing results, stale artifacts and retries. This candidate
does not add product telemetry, a hidden diagnostic endpoint or any
security/role/tenant policy change. The new sample may help distinguish
a racing **old authorization denial** from the original fresh-click
request absence on a future genuine failure; it is **not a root-cause
finding**, a product fix or a reason to close #1365/#1342.

**Draft / no Main merge**, SEC-ARCH/RLS and authorized staging Owner HOLD
BOTH, strict rollback/restore, school and device approvals and Student
Council RELEASE NO_GO remain.
