# Shared ordering support for observation tests

Base: bf51ae8dcce727310461a7f4c9b6c80a8e7ce267.

The Group keyboard and Search drawer Node tests now import one comparison
function from tests/ui/test-support/code-unit-order.mjs. Its body is copied
exactly from their previously qualified private functions: coerce the left
operand and then the right with template interpolation, return zero for equal
strings, otherwise compare UTF-16 code units. The two call sites are unchanged.
Standalone operational collectors and the other eleven qualified files retain
their original implementations and Git blobs.

Verification: existing affected tests passed 23/23 before and after the move
with portable Windows Node 24.22.2, with no failures, cancellations or skips.
All temporary home/cache/output directories were redirected into owned work
fixtures. Reverse-edit guards restore both frozen test files byte-for-byte;
the existing observation fixture bytes are identical.

That deduplication changed no tests, assertions, deadlines, retries, workflows, coverage configuration,
source exclusions, provider dispositions or gate thresholds changed. No new
coverage measurement was performed. The prior 28 program-output goldens were
not rerun; their operational source files and input fixture bytes are unchanged.

Linux rerun was not attempted during the known Docker/host resource problem.
The earlier bf51 Linux cohort is historical evidence; the current Linux/CI
and official Quality Gate remain unverified. The earlier broader Windows
advisor symlink/FIFO limitations remain recorded separately. This local test
refactor does not establish Main acceptance, Pilot completion or Release GO.

## Existing regression measurement wiring

A later change adds the existing restore-advisor, Thread matrix, Group keyboard
and Search drawer Node controls to the pre-credential Native cohort. The advisor
invocation, test-body V8 filter and three guard assertions match the reviewed
measurement component of #1576 at 2e84ce0de822a617fc194f8e19898ea5e6322cbf;
no other branch or roster-finalization implementation is merged.

A narrow adapter invokes the unchanged channel-dialog serializer test in
student-council-workspace-channels.spec.ts. Its config imports the original
Playwright config and retains the original Desktop/Mobile project objects,
assertions, timeouts, expectations and retries. Only file/title selection,
no-server execution, list reporting and owned artifact output are specialized.
The case requests no page/browser fixture; this is not Mobile or Pilot credit.

Local portable Windows Node 24.22.2: the four existing controls report 49 PASS
and the same 2 advisor platform failures (file-symlink EPERM and POSIX FIFO),
with no skips. The adapter passes and verifies both original serializer cases.
Existing bundled Python 3.12.14 passes all 12 selected pure routing/source
controls. Home/temp/cache writes use owned fixtures; no packages or SDKs were
installed. Full shell/JQ routing and Linux Native execution remain unrun.

No local instrumented LCOV was collected. The known Native line/function
mapping inconsistency remains unresolved; this wiring neither certifies
historical mapping nor predicts 80 percent coverage. No LCOV rewrite/filter,
raw V8 upload, Sonar scope/rule/exclusion/threshold, or permission change is
included. Exact-head hosted checks, independent review and official Gate
remain pending. RELEASE_NO_GO is unchanged.
