# Selected original pass boundaries

This bounded #1034 / #782 Product slice adds explicit selection of one retained
initial-root boundary to the existing owned pass-recording reads. It changes no
Compiler execution, observation metadata bytes, original fixture or capture policy.
Whole-root views and diagnostic-specific operator/predicate views remain separate.

## Original boundary and body ownership

The Domain observation retains a read-only list of the actual immutable boundary
metadata objects already collected before its original record is constructed.
The new selected snapshot requires the same owned compilation/observation pair
and exact expected input/result as a whole-root read. An equal-content older
compilation or record does not satisfy that reference ownership. Refusals expose
no previous boundary, original record digest, capture state or input/result basis.

Selection uses the original zero-based ordinal. An ordinal beyond retained events
returns BoundaryNotRecorded, with no invented boundary or bodies; the owned
record's actual event/capture counters remain explicit. A pre-cancelled Core
compile and an event-limit gap do not gain an event. A cancelled host attempt
still has no accepted Compiler execution to read through the Workbench.

Manifest is the default and has no bodies. AvailableOriginalJson selects only
the distinct domain/digest keys actually referenced by that original boundary
and retained by its original capture. Body objects and their original text are
reused. An empty canonical-body boundary keeps its actual gap. Missing capture
bodies stay missing regardless of a later larger viewing allowance.

The selected viewer accepts 1 byte through 32 MiB, independently of the whole-root viewer.
Its atomic budget counts only the selected distinct bodies' actual UTF-8 bytes.
An exact-fit Source boundary can be read even when all recorded root bodies
would exceed that same viewing allowance. A failed selected budget returns no
partial body map and changes neither original capture nor later valid reads.
This is a local view policy, not a retention or access policy.

## Current Workbench and Web ownership

The Windows pure ViewModel requires the exact already admitted run and root
Source/result. Root Source, result/context changes, metadata refresh, whole-root
reads, reset and disposal clear selected details. Each new selected read has its
own generation. Notifications before and after publication cannot revive an
older selected read, including reentrant selection of a newer boundary.
Selecting a diagnostic alone does not change this whole-root evidence context.
Cancellation and invalid read parameters withdraw the current selected display;
previous caller-owned immutable text is not physically erased.

Angular derives a selected view from already verified original observation and
snapshot frames. It does not create a native producer frame, native snapshot
digest or new Compiler run. Private binding checks retain exact case/root/run,
original member fingerprint and original snapshot ownership before/after the
asynchronous boundary. Cloned admissions, foreign root admissions, replaced
members and reused global fixture identities cannot keep a current selected view.

The store clears pending/visible details on Source/run/input/result changes and
protected-state clearing, and the latest overlapping selection wins. Permission
visibility and current snapshot identity are checked again before focus.
These frontend checks are presentation boundaries, not server authorization.

Each actual retained boundary offers an explicit metadata selection. Its gap,
body references, current original run, ordinal and viewer budget remain visible.
Original JSON requires a separate action; a one-byte view is another explicit
choice. English/Japanese labels, keyboard focus and narrow wrapping follow the
existing offline Workbench. No selected diagnostic is required.

## Qualification and limits

Domain and pure ViewModel tests cover all eleven original inputs, all actual
boundary metadata/body objects, same-hash foreign pairs, exact-fit/insufficient
selected budgets, missing captures, no-event cancellation, invalid selectors,
Source lifecycle and reentrant publication. Angular reader/store/component tests
cover all original roles and boundaries, original body identity, private binding,
real overlapping selections, current Source, capability visibility, protected
clearing and both locales. Actual Linux browser checks exercise keyboard reads
of Source/input/IR/publication and explicit missing-body boundaries, original
text/domain hashes, capture/viewer gaps, cancellation/retry, accessibility,
narrow width and absence of mutation requests. Current source pins, raw retained
test inventories, actual results and reviews belong on the PR before integration.

This remains local initial-root JSON observation, not FullDump/FullTrace,
optimization/plan/regional completeness, causal explanation or formal Registry
schema adoption. It provides no current actor/Tenant read/export, trusted producer,
evidence eligibility, redaction/revocation, durable retention/Pin, backend transport
or actual Avalonia acceptance. The original Compiler result, logical operations
and Requirement evidenceComplete=false remain unchanged.

Owner scope remains ProjectIDE only. Independent selected-boundary/pass lifecycle
qualification and operational controls remain existing OPEN/P0 #1966 and related
#1932/#1959/#1974/#1980 handoffs; no Security/shared-CI source or merge is included.
Full #905/#907/#782/#1032/#1033/#1034 stay OPEN, #1144 BLOCKED,
#1128 SUSPENDED/NOT_EVALUATED and all Registry/Trust/Eligibility/RLS/Native/
Production/Release holds remain.
