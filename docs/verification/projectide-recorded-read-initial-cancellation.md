# Initial recorded read cancellation and newer view ownership

Existing work: #1034 / #782 under #888. This is a Product correction for six
pure Workbench reads of already-owned compiler outputs, diagnostic premises,
selected pass boundaries, whole passes, operators and recorded predicates.

## Trigger and bounded correction

Each read clears its previous view and notifies PropertyChanged before
admitting an original recorded body. A synchronous subscriber can start a
newer admitted or refused read, then cancel the outer token. Previously the
outer generation check returned false without reporting cancellation.

Each of the six initial supersession guards now checks its own token before
returning false. It reports OperationCanceledException while retaining the
exact newer admitted snapshot, unbound refusal or empty view. Uncancelled
supersession still returns false. The later source/generation/publication
checks and owned cancellation cleanup remain unchanged. Metadata-only reads
have no token and are unchanged.

The twelve new cases use actual fixed-source compilation/recording sessions,
explicit original Source admission and selected diagnostics where required.
They retain the original result, result data and previous snapshot data, then
verify the replacement object and its original bodies or actual wrong-result
refusal before asserting the cancellation exception. The test subscriber owns
its token source, uses a method-group read and unsubscribes before disposal.

## Original failure evidence

Based on dedicated integration f9f4af9c2312f6c6bd02bdb8696436a48451f45e
(tree e67966a8ec5add08bbbe1cfac0480f0f849bb120), the original runtime failed
all twelve cases on both Windows and Linux only at the final expected
OperationCanceledException assertion: actual null. All preceding data and
newer admitted/refused view assertions passed. The twelve raw GUID/name/outcome
inventories match across hosts. SDK10.0.403 fresh nonincremental Release builds
had zero warnings/errors, with 317 relevant source inputs exact before/after.

The first fixture attempt failed compilation before running any cases because
the new operator fixture named a nonexistent snapshot mode. The fixture was
corrected to the existing AvailableOperators mode; original source, failed
strict build logs and input pins are retained. The first Windows positive
harness also refused obsolete input pins before compilation; its original
helper/log were retained and the input-file reference corrected. Neither
preflight failure is counted as an executed regression or successful check.

## Validation record

With only the six initial guards corrected, each host passes192 affected,
2790 full ProjectIDE and12 architecture cases with zero skips/failures. All
previous180 affected,2778 full and12 architecture raw GUID/name/outcome rows
are retained, and all three suites match exactly across hosts. The same twelve
original failed IDs now pass with byte-identical test sources. Fresh strict
nonincremental Release builds have zero warnings/errors; all317 relevant inputs
match before/after. Metadata-only non-token methods remain byte-identical.

Record current static analysis, complete technical review and hosted checks
in the PR with exact source/Tree and original archive/source digests. A new
documentation file must not be retrospectively attributed to an earlier
compiled execution.

No Angular/Web API, recording protocol, generated fixture, execution/session,
metric, current authorization, acquisition, storage, export or CI change is
included. These Product checks do not establish independent security, actual
Avalonia UI, operational data, Native activation or production readiness.

Independent actor/Tenant/session and privacy/redaction/retention qualification
remain OPEN/P0 #1962. Shared CI remains P0 #795. Wider parent Issues remain
OPEN; #1144 BLOCKED; #1128 SUSPENDED/NOT_EVALUATED; Registry/Trust/Eligibility/
RLS/Native/Production/Release HOLDs remain unchanged.
