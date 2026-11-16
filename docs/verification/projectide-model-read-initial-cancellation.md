# Initial model read cancellation and newer query ownership

Existing work: #1033 / #1032 / #782 under #888. This corrects the initial
notification boundary of the two existing pure Workbench profile/comparison
reads. It starts no simulation, acquires no new recording and changes no
metric, recording, result kind, protocol, fixture or current access grant.

## Trigger and behavior

Each read first withdraws its earlier view and emits PropertyChanged. A
synchronous subscriber can start a newer admitted or refused read and cancel
the outer token. The outer generation is then superseded. Previously its
early false return suppressed OperationCanceledException even though the
token was cancelled and the newer view was already current.

Each initial supersession guard now checks its own token before returning
false. It reports cancellation without clearing or replacing the newer view.
Ordinary uncancelled supersession still returns false. The existing later
read/publication ownership checks, argument validation, refusal data boundaries
and post-publication cancellation cleanup remain unchanged.

The profile test retains the actual original Compiler result and run, their
data and the preceding summary data. The comparison test additionally retains
the original before recording. Both exercise a genuine newer successful read
and a genuine newer refusal with no recording basis. Earlier facts, current
newer object identity and refusal state are asserted before the exception
assertion; no fake metric or mock recording is substituted.

## Executed Product checks

Before the runtime correction, the four new cases failed on both Windows and
Linux only at their final expected OperationCanceledException assertion
(actual null). Their original run/result/data and newer admitted/refused view
assertions passed. The four raw GUID/name/outcome inventories match across
hosts; the original failed TRX, source and strict build logs are retained.

With only the two initial runtime guards corrected, the exact same four test
sources and identities pass. Each host passes70 focused, 2778 full ProjectIDE
and 12 architecture cases with zero skips/failures. All previous 66 focused,
2774 full and 12 architecture identities are retained; all three raw suites
match across hosts. SDK10.0.403 fresh nonincremental Release builds have zero
warnings/errors, and 316 relevant inputs match before/after execution. These
are Product developer checks, not actual Avalonia UI or independent acceptance.

The first positive native execution used anonymous callbacks that captured
disposable outer variables. InspectCode reported 78 overall / 12 changed
AccessToDisposedClosure warnings; its original source, SARIF and successful
TRX remain retained. The four new cases now use an owned IDisposable subscriber
that unsubscribes before disposing its token source, and a method-group read.
This fixture lifetime correction preserves every condition and assertion.
The original runtime still fails the same four raw identities at the same
final cancellation assertion after this fixture correction.

Record exact source/Tree, original and corrected artifacts, current static
analysis, complete technical review and hosted CI in the PR. New documentation
must not be retrospectively attributed to an earlier compiled execution.
The Angular application and original profile-recording browser runtime are
unchanged; the native token exception contract is not a new Web API.

Independent actor/Tenant/session, privacy/redaction/retention, operational data
and actual Native qualification remain OPEN/P0 #1962. Product tests and the
Owner human-review waiver do not qualify those controls. Wider parent Issues
remain OPEN; #1144 BLOCKED; #1128 SUSPENDED/NOT_EVALUATED; Registry/Trust/
Eligibility/RLS/Native/Production/Release HOLDs remain unchanged. No Security
branch, shared CI, activation or normative specification change is included.
