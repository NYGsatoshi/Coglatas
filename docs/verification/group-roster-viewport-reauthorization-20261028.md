# Group roster viewport reauthorization candidate

Status: Draft / REVIEW_ONLY. `STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.

## Additional initial-synchronization failure class

The `974ea976` candidate's exact-tree protected source
`7da45ea85b96f1d8576e950c990fd8dc9e4a5449`,
[40705424026/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40705424026),
failed Mobile at MOBILE_SUMMARY_SCROLL before a fresh conversation GET returned.
Original ZIP 12822944320 SHA-256
`7eeae205176fe66143992d734a982a5dae94334ffdc6aa9a31c7b999ec0940f0`
was independently verified. Desktop passed; Mobile mounted +298ms, began a
conversation GET +429ms, removed the panel +432ms, and captured an absent panel.
The native action's raw failure and whether it acquired a handle before removal
are not retained. This is separate from the proven post-scroll remount class.
The earlier `08ff447b` and `19a1b8c7` first-attempt passes do not override it.

The follow-up adds a presentation-only initial synchronization lifecycle flag.
With SignalR enabled, the Group roster first becomes available after the first
transport subscription/catch-up cycle has settled. Its temporary initial HTTP
projection therefore cannot supply an interactive roster node that the first
catch-up immediately removes. This does not postpone or disable catch-up, retain
old nodes, retain an old Group projection across reauthorization, or grant read
authority. The existing current HTTP participation/readBody condition still
controls rendering; catch-up still empties all protected data. HTTP-only mode
and a degraded first connection remain available. Session/Tenant resets and
epoch checks prevent an obsolete attempt from settling a replacement's flag.
Later reconnects leave this first-presentation flag settled, preserving the
existing scoped focus/viewport handoff for a legitimately mounted roster.

Deterministic Chromium controls hold the first subscription and its subsequent
detail read: no roster appears until the current catch-up finishes, then the
original native scroll reaches the fresh panel. Both original-source browser
profiles fail this initial-presentation control. Ten successor controls pass
across Desktop/Mobile, including a separate real browser reconnect after native
scroll completion, outside focus and current HTTP denial. HTTP/SignalR responses
are mocked; exact-source real-backend execution is independently required.
The immutable original real-backend owner is unchanged.

Independent Codex review of `df023ace` identified a terminal-close availability
gap: closing the first transport invalidates its epoch without starting a
replacement, so the old attempt's guarded finally cannot release the initial
presentation flag. Current terminal close and browser-offline paths explicitly
settle presentation for HTTP fallback. Session/Tenant replacement still resets
the flag, and obsolete async completion cannot release a replacement attempt.
The flag never supplies Group authority. The original-source terminal-close
browser controls fail in both profiles; successor controls verify current
authorized HTTP roster availability after terminal close and actual browser
offline events, as well as normal startup and later reconnects.

Two additional deterministic controls exposed an old End boundary surviving
either an intervening wheel event after removal or explicit invalidation before
Angular teardown. Both failed before correction. Focus/viewport cancellation now
clears the existing boundary coordinator too, whose captured epoch prevents late
teardown from recreating a canceled boundary. The same bounded input observer
also covers focus-only intent when the roster was offscreen. No coordinates,
protected nodes, member data or server permissions are retained. Existing direct
Home/End, modifier/IME, dialog, authority, scope and one-time controls remain.

The `df023ace` / exact-tree `09148743` predecessor completed protected
[40711120910/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40711120910)
successfully: original roster ZIP 12827340758 SHA-256
`b555291bfd233e7861a2e63242ddc35c197ef63b970809bb81269e84210aa7a3`
and FCI-04 ZIP 12827710451 SHA-256
`aca058a8221bb3f1c0deea994ab48702bbbc16c604091cab0f22e27dc85b7768`
were independently verified. Both browser profiles completed 43/200/403/400,
End1894/Home0/retry0; engine/served-payload/source binding matched. That pass
does not qualify the later reviewed fallback/boundary correction automatically.

## Source and retained first failure

Inspected actual Main `c72f21048e85d559b100d342364431a17569d3a6`, tree
`1b96161faef9e47e3daae5bcb0db880dcf4efcff`, and existing PR #1476
`b3133e395d5f318378a761f809d9f42756a9ed88` before editing. The clean local
checkout was initially at `19989df9`; a fresh fetch supplied both current PRs
and Main. Spec Main was observed as `6a0491c925b4980ce737dfbdf775816c237e44fe`;
no specification or held contract is changed.

Original [protected run 40690662224/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40690662224)
remains FAIL, source `42222e00c31752cb5fee9af6641a274011d96f13`, tree
`1e9dbb30e138af10c580b69305b6037a2c0d9b6c` (identical to diagnostic #1481).
Original artifact 12818031390 was independently downloaded; ZIP SHA-256:
`027158a448c4c664be2753fe3a30728c94d914f13840dc9d4d9e505a147fe710`.
It binds the served frontend digest and engine image to that source and records
retry zero, Desktop PASS and Mobile NOT_VERIFIED at MOBILE_SUMMARY_VISIBILITY.

Mobile's native summary scroll returned at +394 ms, before panel removal at
+410 ms and fresh remount at +503 ms. The replacement summary was connected
at y680..724 outside the 320x420 viewport; the app-shell main scrollport had
scrollTop 170. Conversation HTTP200 and messages HTTP200 completed. Equal-ms
cross-source events do not establish their internal order; the separated
394/410/503 milestones establish the relevant post-scroll replacement.
The valid 43-member/POST200/403/400 controls do not override that failure.
Other historical missing-panel or Home-focus failures retain their own causes
and evidence; this is not a universal intermittent-reliability claim.

## Mechanism and bounded correction

Initial realtime synchronization can run a feature catch-up after initial
HTTP rendering. `registerConversationRealtime` registers during the initial
detail read; `RealtimeFacade.beginSynchronization` reconciles subscriptions
and pending catch-ups, including on first connection. The messaging catch-up
waits for protected requests, then `reloadConversationForCatchUp` starts a new
request generation and publishes an empty loading projection. The template's
`isGroup && canReadBody` condition consequently destroys the Group panel.
On Mobile, removing the tall panel shrinks the ancestor app-shell scrollport
and clamps its scroll position. The earlier native action targeted a different
DOM node; the fresh authorized remount cannot inherit that viewport position.

Extend the existing PR #1476 focus coordinator, rather than add a parallel
coordinator. A synchronous, non-authorizing facade notification lets the
mounted DM page inspect the current authorized panel before routine clearing.
Capture only an intersecting focused list/Leave target or a fully visible
summary, after checking enclosing clipping scrollports. Retain an enum target,
opaque actor/Tenant/Workspace/Conversation scope and timestamp; no coordinates,
nodes, names, rows, messages or grants survive in the intent.

Group interaction classification is independent of the existing Thread-only
navigation binding, which deliberately excludes Groups. An already authorized
Group must match current actor and scope to be classified as routine. Every
non-routine request-generation boundary clears both existing intents before
old Angular teardown can recreate focus. The existing denial/route epoch,
two-second focus TTL at teardown, native Home/End and modifier/IME guards remain.

After the complete fresh HTTP aggregate authorizes a new panel, consume the
viewport target once within two seconds and scroll the new target with native
instant behavior. Center the short summary to avoid fractional-edge clipping
when an ancestor's scroll offset is rounded; focused lists/Leave use nearest.
It does not focus an unfocused summary. An intervening
focus, pointer, wheel, touch or key action cancels the pending interaction;
expiry and page destruction remove the document listeners. A newly opened
confirmation dialog supersedes the old roster interaction even before its
scheduled focus trap runs. Denial, removed participation, session/identity,
Tenant, Workspace, route and Conversation changes invalidate restoration.
Existing generation checks still reject obsolete HTTP responses.

Protected rows and messages remain cleared throughout reauthorization. No
acceptance action, assertion, retry, timeout, workflow, environment, secret,
permission, database or release gate is changed by this Product candidate.

## Validation and remaining qualification

Local regression coverage uses real Angular lifecycle/facade orchestration
with synthetic HTTP and a separate actual Chromium browser with mocked HTTP
and SignalR protocol responses. It is not PostgreSQL or real-backend evidence.

The browser regression gates initial transport synchronization until one native
summary scroll has returned. It holds the ensuing current detail request,
asserts panel/message removal and the old node's disconnection, and verifies
authorized remount both before and during the unchanged viewport assertion.
Separate controls cover intervening outside focus and current denied HTTP.
There is no rescue scroll, retry or timeout increase. The same two positive
browser cases fail on original #1476 source at viewport ratio zero, and pass
with this correction. The original real-backend acceptance oracle is unchanged.

Executed local results and exact published candidate/run identities are recorded
in the PR checkpoint. A local full Angular run passed 1,908 tests before the
final dialog-specific regression/guard; final focused controls must also pass.
Original local fixture/setup failures remain in the execution logs and earn no
acceptance credit. Production builds retain existing stylesheet budget warnings.

The first broader local browser run on `3c25e1d8` passed 32/34 but failed the two
authorized cases in the Desktop project's 320px viewport with ratio 0.99858.
That original failure remains. The narrow follow-up centers the summary while
preserving the original full-viewport ratio-one assertion; no tolerance is added.

Independent CodeRabbit review at `a037cc4a` found that a deliberate blur to body
can leave a stale keyboard role because focusout has a null relatedTarget. The
pre-reset handoff now checks actual attached focus and clears keyboard intent
when it has left details, while still permitting an unfocused visible summary
to retain only its viewport intent. A facade/conditional-teardown regression
asserts that body focus stays on body after fresh authorization.

Required before merge: independent current-head review, complete required CI,
actual PostgreSQL/ASP.NET Core/Chromium first-attempt 43-member acceptance on
the exact candidate tree, and successful Sonar admission/analysis. A separate
resulting Main needs independent qualification after any normal merge.

## Sonar admission: Owner decision and execution limits

Decision ID: `OWNER-SONAR-PR1476-BRANCH-ADMISSION`.

The original [40688455187/1](https://github.com/NYGsatoshi/Coglatas/actions/runs/40688455187)
at #1476 `b3133e39` resolved the exact trusted target successfully. Job
122816869518 failed with zero steps and no allocated runner name. Its GitHub
check annotation explicitly says the branch
`fix/group-roster-revalidated-focus-20261028` is not allowed to deploy to
`sonarcloud-analysis` due to environment protection rules.

Read-only environment inspection found custom branch restrictions allowing
only `main` and `fix/sonarcloud-ci-based-20261027`. This is an environment
admission rejection, not evidence of a scanner, token or source defect. Main
Sonar run 40687755639 succeeded separately. The effective Main rules API was
read successfully; the classic branch-protection endpoint returned HTTP403.

Options: (A) Owner approves this single exact PR branch after independent
source/workflow review, retaining Main and using required environment reviewers
where code is unreviewed; then run exact-current-head analysis. This is the
recommended narrow route, consistent with the existing static-analysis policy.
(B) Keep admission unchanged and leave PR qualification blocked while Owner
selects another explicitly reviewed token isolation/admission design.
Do not add a broad branch glob or disable the check. No secret value is needed.
This candidate does not change the environment or claim approval.

Subsequent explicit Owner direction supersedes those proposed restrictions:
Sonar is intended to provide early findings without requiring a completely
clean result before ordinary merge consideration; use no environment admission
restrictions and allow administrator bypass. Do not re-request the superseded
exact-branch/reviewer policy. The environment was independently observed with
no protection rules and no deployment-branch policy. The integration's attempted
administration PUT returned HTTP403 and no agent write succeeded; its separate
can_admins_bypass field remained false, but no admission rule is active to bypass.
No token value was accessed. CI-based exact-SHA/same-repository routing,
least-privilege permissions and existing Sonar project/quality thresholds remain.
This specific Sonar policy direction does not waive failed Product acceptance,
required security/Functional checks, unrelated Owner holds or release approval.

After that direction, predecessor `df023ace` Sonar workflow 40710817454/1
completed SUCCESS and published actual analysis. Its separate Cloud Quality Gate
failed 0% new-code coverage against the unchanged 80% threshold. The workflow
currently generates/imports no coverage report; executed unit tests are not
Sonar coverage evidence. Scanner logs also contain a JavaScript analyzer internal
error on unchanged `tests/functional/announcement/announcement-publish-read-journey.spec.ts`
(blob `545e9cb694cf9967e892b3e9d3928640b94bdb7b`, identical to Main), despite
the successful job/report publication. Preserve that partial-analysis limit;
do not call it a Product defect or claim every file was successfully analyzed.
Public Sonar task/issues API reads returned HTTP403; no credential was requested.

The separate `OWNER-FCI06-PREREQUEST-DENIAL` decision on #1365 remains pending
under its existing default C; the canonical Functional owner remains unchanged.
All SEC-ARCH, RLS, recovery, staging, deployment and student-admission holds
remain. Safe Product tests, diagnostics and source review can continue.
