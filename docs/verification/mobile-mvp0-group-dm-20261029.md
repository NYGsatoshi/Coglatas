# Independent actual-backend MVP0 Mobile qualification

Tracks #1513. Base Main: `13574a971b10cf10a41716141cfe821fb6f68c04`.

The existing mandatory MVP0 test exercises the DM UI and the real Group creation,
messages, retained history, four synthetic actors, read/unread isolation, removal,
re-entry, first admission, leave and Workspace revocation. Its required Desktop
execution and all existing assertions remain unchanged. It is not sufficient
proof that these flows work at a narrow native Mobile viewport: the existing
Group helper created its other actor contexts with Desktop defaults.

The opt-in Licensed Real Backend Acceptance suite `mobile-mvp0` runs exactly that
one unchanged scenario in a separate Chromium 320x800 touch project, one worker,
retry zero, with the existing 120-second scenario timeout. All four Group actors
use separate authentication contexts with the same device geometry; each reads
and asserts its actual viewport, screen, device scale and touch points. A closed
one-case manifest and JUnit verification reject skipped, failed, missing or extra
cases. The normal Desktop guard, canonical Mobile four-owner/23-step contract,
FCI06 Default C and thirteen configured skips remain intact.

The wrapper requires a clean exact TARGET_SHA, attempt one and a dedicated build.
Only source/tree/run/attempt, image digest, case count and four actor geometry
receipts are uploaded. Trace/video/screenshots and generic failure artifact upload
are disabled only for this new opt-in profile. No protected message body, cookie
or authentication value is included in its published receipts. Post-build image
inspection is not claimed as an independent before/after continuity receipt.

Local Node guards cover valid geometry, missing opt-in, different/canonical
projects, retry, wider/non-touch/nonmobile contexts, and absent/extra/Desktop
actor receipts. Local Docker execution is NOT RUN because the available local
Docker/disk environment is unqualified. Actual hosted first-attempt results,
independent review and Main validation remain pending until recorded in #1513
and #615. No feature, permission, schema, formal security or release contract is
changed. No physical-device/IME or HTTPS/student admission claim is made.

`STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.


## First hosted attempt and navigation adapter

Candidate `bd4d49d3f5568062203464a2ebe469872fd79433` (tree
`20c72c52b2fe9036167300e870494068f8ff115a`) actual run40818408919/1,
job123264866240, FAILED once with retry zero. Login reached the real application,
but the legacy helper asserted `.first()` Desktop `nav-projects` visible at320px;
it is intentionally hidden while the Mobile drawer is collapsed. Source inspection
confirms the existing Mobile toggle, scoped navigation links and Escape/focus
restoration contracts (also independently exercised by existing UI tests).

The opt-in helper now opens that real drawer, checks both original authorized
navigation links, and closes it with Escape plus focus restoration. The Desktop
branch and all login/session assertions remain. No Product defect or historical
reliability root cause is inferred from this adapter mismatch. No timeout/retry/
assertion/force-click relaxation is used. A fresh candidate attempt is required.

First failure artifact12873651567 is481bytes, SHA-256
`31cde764bfc2922ddcc25f94d1cd606cbb7166b9049f650497bf45138133fe9e`,
independently hashed and parsed: source/tree/run/attempt bind the failed candidate,
statusFAIL, retry0, verified actors0, no device profile receipt or qualified image
receipt. The failure did not reach Group execution and is not Mobile acceptance.


The new profile uses file-only JUnit reporting, with no console/list reporter:
failed assertions can contain protected synthetic message values. JUnit stays in
the run workspace for strict result verification and is not uploaded. Failure
correlation publishes only an allowlisted repository filename and bounded source
line, never assertion text or values. A real parser control verifies this boundary
with synthetic private text. Existing required/canonical reporter settings remain.


## Independent review and fresh execution repair

The independent review's P1 navigation finding is valid: the selected scenario's
subsequent feature links also came from the hidden Desktop menu. The explicit
Mobile path now opens and scopes Workspaces, Messages, Announcements, Projects
and Account navigation to the existing drawer and verifies route selection closes
it. The same route, capability and business assertions remain; default Desktop
locator behavior is unchanged.

Its P2 execution/freshness finding is also valid. The wrapper now explicitly calls
and requires a numeric successful result from the real Compose function. Competing
selections, a different source directory and any pre-existing profile/source/
execution/JUnit receipt are rejected before execution. Existing evidence is never
deleted or overwritten, and source/execution writes use exclusive creation. Import
alone or an undefined return cannot establish an executed scenario. The optional
module-import path is separated from the explicit call; existing Desktop and
ReadOnly wrapper entry points retain their behavior.

Eight tests exercise the actual exported wrapper with isolated IO: one positive
execution, conflicting selection plus stale evidence, every stale receipt alone,
source/dirty/directory/reuse rejection, undefined/failed execution, absent newly
generated case/profile, extra cases/Desktop actor, and invalid image rejection.
Together with6 device/privacy controls and22 existing runner regressions, all36
PASS locally. Fake IO is only test coverage; it is not actual backend acceptance.
A fresh hosted exact-HEAD run, review and Main proof remain required. Both original
review threads remain open pending that verification; no valid finding is dismissed.


## First d080 attempt and existing Mobile member cards

HEAD `d08082c4a7cb0906bb03f18aef0170de9ee4c4ca`, tree
`682d6e3252b4edbbc70297415f69e42c00d890bd`, run40820690436/1,
job123272219059:14 guard controls PASS, then actual Group scenario FAIL/retry0 at
`group-dm-real.ts:573`. Unlike bd4, it reached the Group Workspace-revocation
phase. The helper sought a Desktop grid row; the existing Mobile UI renders
`workspace-member-mobile-card` list items and the same `openMemberDetail` action.
The explicit Mobile path now locates the unique named card and clicks that real
action. Desktop grid behavior, uniqueness, fresh detail HTTP200, member identity,
removal confirmation, denial and protected-state clearing assertions remain.
No Product defect, historical root cause or qualification is inferred.

Failure artifact12874237895 (536bytes) SHA-256
`596664b75ccc39a4490325a889f916b764597452e5da7e94575d45a5bc3c2192`
was independently hashed and parsed: exact source/tree/run/attempt, statusFAIL,
retry0, bounded source location, no profile receipt and no qualified image.
No raw assertion/message value was published. A fresh new-source first attempt
is required; this failure is not rerun or promoted to acceptance.


## First064 attempt, partial Group execution and Task cards

Source `064b3b221937deab2ee39e8bbcbe12abb117ad23`, tree
`2719f8f60ecfa2b73b55ca8a01089052fba33d63`, actual40822397195/1,
job123277604414, passed14 guards and finished the Group helper, including its
cleanup, before the enclosing scenario FAILED/retry0 at
`real-backend-smoke.spec.ts:4970`. The Project Task helper sought a Desktop row
and Desktop field IDs on the already-rendered Mobile cards. The explicit profile
now selects the unique named/id-bound existing Mobile Task card and the same
stage/category/blocking/artifact/time/detail controls. Desktop behavior remains.
The selected logout helper also opens the existing narrow utility panel before
the same logout command and unauthorized/session-cookie assertions. This follows
the verified current TopBar toggle contract; no auth command or timeout is changed.

Independently parsed failure artifact12875715905 (827bytes), SHA-256
`5deb8bad7f66378e56dee1d6c412400d73ef9011d12ae6a3a0d7df88639832eb`,
binds source/tree/run/attempt, statusFAIL/retry0. Its profile receipt, written only
after the Group helper returns, contains four actual320x800/screen320x800/scale1/
touch1 actor geometries. This is useful partial execution evidence; the complete
MVP0/image qualification is still absent and is not promoted to acceptance.

Existing14 real-wrapper/device/privacy tests were measured with Node's native
LCOV reporter: profile36/36 lines and29/29 branches; wrapper87/91 lines and33/37
branches. The prior CI collection measured only the functional selector, so it
could not import these new operational subjects. Both existing Mobile test suites
are now added to that same collection, retaining the selector and all analysis
scope/thresholds. No test body is imported to inflate Product coverage. This local
instrumentation does not establish Main New Code80%, ReliabilityA or SecurityA.
New-source hosted first-attempt acceptance, official Gate and review are required.
