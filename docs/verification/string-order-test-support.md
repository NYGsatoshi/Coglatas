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

No tests, assertions, deadlines, retries, workflows, coverage configuration,
source exclusions, provider dispositions or gate thresholds changed. No new
coverage measurement was performed. The prior 28 program-output goldens were
not rerun; their operational source files and input fixture bytes are unchanged.

Linux rerun was not attempted during the known Docker/host resource problem.
The earlier bf51 Linux cohort is historical evidence; the current Linux/CI
and official Quality Gate remain unverified. The earlier broader Windows
advisor symlink/FIFO limitations remain recorded separately. This local test
refactor does not establish Main acceptance, Pilot completion or Release GO.
