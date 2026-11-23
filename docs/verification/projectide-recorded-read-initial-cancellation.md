# Initial Workbench read cancellation and newer ownership

Existing work: #1034 / #782 under #888; related original recording and Source
admission contracts. This corrects seven initial read-notification guards:
compiler outputs, diagnostic premises, selected pass boundary, whole pass,
operator recording, predicate premises and Root Source admission. Metadata-only
non-token reads and the already-token-checked pass Source page are unchanged.

## Trigger and behavior

Each read withdraws its previous view and emits PropertyChanged. A synchronous
subscriber can establish a newer successful or refused read and cancel the
outer token. Previously the outer initial generation check returned false
before observing cancellation. The older caller therefore received no
OperationCanceledException even though its token was cancelled.

Each initial supersession guard now checks its own token before returning
false. It does not clear or replace newer state. Ordinary uncancelled
supersession still returns false; argument validation, current-source admission,
refusal boundaries and later read/publication cleanup remain unchanged.

The six recording tests retain actual original result/body data and require
the exact newer admitted view or its actual wrong-result refusal/empty state.
The Root Source tests also retain a newer successful admission and the actual
diagnostic premises read from it, or the newer Unavailable refusal. All these
facts are asserted before the cancellation exception. Owned IDisposable test
subscribers use method-group reads and unsubscribe before disposing tokens.

## Original failure and validation evidence

On dedicated integration f9f4af9c2312f6c6bd02bdb8696436a48451f45e
(tree e67966a8ec5add08bbbe1cfac0480f0f849bb120), all fourteen new cases
failed on both Windows and Linux only at the final expected cancellation
assertion: actual null. Earlier original-data/newer-view and Root Source premise
assertions passed. All fourteen raw GUID/name/outcome identities match across
hosts. SDK10.0.403 fresh nonincremental Release builds had zero warnings/errors;
317 relevant source inputs match before/after. Original failed TRX and source
pins are retained. The final positive run uses byte-identical test sources.

The earlier six-read phase separately passed192 affected,2790 full ProjectIDE
and12 architecture cases per host after its twelve original failures. Prior
180 affected/2778 full/12 architecture identities were retained with exact
cross-host raw parity. Its complete fourteen-file CLI review had zero findings.
These earlier results are retained as a separate source phase, not promoted to
the expanded seven-read candidate. First static analysis reported67 overall /
1 changed RedundantNameQualifier in the new premise fixture. It now uses the
existing imported PropertyChangedEventArgs type; no condition/assertion changed.
The corrected fixture still failed the same twelve original cases before
expanding to the fourteen-case original-runtime negative execution above.

The first fixture attempt used a nonexistent operator snapshot mode and failed
compilation before running cases. It was corrected to existing AvailableOperators.
The first Windows positive harness also refused obsolete source pins before
compilation; its input-file reference was corrected. Both original source/helper
and failed logs remain retained; neither preflight counts as executed acceptance.

Record the final current-source positive inventory, static analysis, full
technical review, hosted CI and original archive/source digests in the PR. New
documentation must not be retrospectively bound to earlier compiled checks.

## Scope and remaining gates

This starts no compiler, simulation, body acquisition, export or new storage
during a read. No Angular/Web API, recording protocol, generated fixture, metric,
current authorization, CI or normative specification change is included.

These Product checks do not establish independent security, actual Avalonia UI,
operational data, Native activation or production readiness. Independent actor/
Tenant/session and privacy/redaction/retention qualification remain OPEN/P0
#1962; shared CI remains P0 #795. Wider parents remain OPEN; #1144 BLOCKED;
#1128 SUSPENDED/NOT_EVALUATED; Registry/Trust/Eligibility/RLS/Native/Production/
Release HOLDs remain unchanged.
