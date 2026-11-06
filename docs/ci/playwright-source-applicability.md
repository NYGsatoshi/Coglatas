# Bounded conditional source applicability — PR-J follow-up

This extends #1717's source discovery with a separately versioned, source-only
applicability sidecar. It preserves the original v1 roster and existing reporters.
#1755 remains an independent unmerged candidate; its original REJECTED result,
native attempts and two declaration-time skips must never be rewritten.
D01–D15 and the complete [#1747 contract](https://github.com/NYGsatoshi/Coglatas/pull/1747)
remain binding. The PR-N final verifier remains unimplemented.

## Source and identity proof

The existing locked frontend TypeScript **6.0.3** compiler API parses the original
24 selected specs. No dependency is added. The AST analyzer never executes,
transpiles or passes predicate text to an evaluator. Playwright's existing trusted
`--list` discovery transforms and imports the specs and can execute module-level
code; it supplies actual `TestCase.id`, location, describe ancestry, project and
declaration status. The AST independently verifies the declaration and hook edges.
The same integrated `coglatas.playwright-test-id.v1\0` hash is reused.

Supported predicates are only leading `test.skip(info.project.name !==
'chromium-desktop' | 'chromium-mobile', <literal reason>)`, or leading calls to a
local, single-statement helper with that exact predicate grammar. Parameter
binding, the original helper call, declaration and enclosing `beforeEach` scope
are verified. Multiple applicable conditions, ambiguous hooks, unconsumed skip
calls, unknown projects and unsupported dynamic expressions reject.

SDK locations must resolve to exactly one AST test-callee range. Conditional
cases require a literal title and matching native describe ancestry; titles and
paths remain private. Existing generated unconditional titles are supplied by the
SDK and never evaluated here. Those declarations cannot authorize conditional skips.
Declaration skips from `test.skip` or `test.describe.skip` remain a separate class.

This deliberately narrow candidate also requires complete canonical source-byte
equality with the independently read original Security Git blobs at
`3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`: all selected specs, the original runner
and Playwright configuration. It reads each candidate Git blob at the caller's
exact checkout SHA and compares it to the actual checkout. Thus moving helpers,
changing scopes or otherwise changing an approved predicate cannot create new
authority merely by retaining an aggregate skip count. Future source changes need
a separately reviewed source-anchor update; this is not a general skip evaluator.

The two original Chromium projects must retain their configured browser and mobile
applicability. Unknown or additional projects reject. No list of eleven IDs is
stored. Classifications and counts are derived from actual SDK cases and the AST.

## Nonpublishing sidecar

`playwright-source-applicability-reporter.mjs` is opt-in and unregistered. Invoke it
only with the original full selection in `--list` mode and independently supplied
`COGLATAS_SOURCE_DISCOVERY_SHA`, with `COGLATAS_SOURCE_APPLICABILITY_LIST=1`.
It emits a bounded source-only envelope with:

- The unchanged `coglatas.playwright-source-discovery.v1` roster.
- `coglatas.playwright-source-applicability.v1`, bound to that roster, source SHA,
  source-anchor manifest and a canonical applicability digest.
- Per-case hashed ID, fixed Chromium project classification, `RUN`,
  `DECLARATION_SKIP` or `CONDITIONAL_SKIP`, and a source-binding digest.

Raw titles, reasons, paths, helper names, bodies and diagnostics are not emitted.
The reporter is not installed into current Playwright configurations or workflows.
All configured selection, assertions, worker/retry budgets, timeouts, reporters,
artifact ownership, permissions and scanners remain unchanged.

## Composed-source reconciliation boundary

`playwright_applicability_reconcile.py` requires the independently pinned #1755
adapter. It fails closed when that dependency is absent from #1717 alone. It first
invokes the unchanged #1755 reconciler, retaining its entire original result and
native attempts. It then checks each native row against independently generated
source applicability, preserving declaration status and runtime status separately.
Only a complete matching conditional case can account for its specific runtime
skip. RUN cases skipped on their intended execution project reject. A missing,
foreign, duplicated, failed, retried or interrupted case still rejects.

The caller must supply the independently regenerated applicability digest; it
must not trust a digest merely because the sidecar asserts it. The stdin envelope
has `source`, `native`, `applicability` and `expected` objects. Expected metadata is
the original #1755 source/run/attempt contract plus `applicability_sha256`.
The original bounded JSON reader, schema validation and sanitized refusal behavior
are reused. The output is a separately versioned composed-trial record; it never
relabels #1755's earlier REJECTED result.

A qualified trial needs an explicit combined Git tree containing both candidates,
their exact parent/source identities, clean selected sources and independently
generated SDK/AST evidence at that same combined SHA. Native evidence from another
SHA must not simply be relabeled. Preserve any producer merge versus reviewed head
distinction and input artifact identities. A newly executed local composed trial
does not establish GitHub Artifact authenticity.

Even CONTENT_MATCH retains `COVERAGE_UNVERIFIED` for final assurance. D10 artifact
provenance and protected Required Gate qualification remain false. Local producer
identity, native case-content reconciliation, mandatory independent review,
governance, Nightly and release acceptance remain separate.

## Controls, retention and rollback

Run `node --test tests/ui/playwright-source-applicability.node-test.mjs` with the
existing frontend TypeScript and root SDK dependencies available. Run
`python3 -B scripts/ci/test-playwright-applicability-reconcile.py` only with the
pinned #1755 adapter and its companion `test-playwright-case-reconcile.py` fixture
explicitly available together in the composed checkout or an isolated qualification
import path. Pin both files to #1755's `4ca1c2834e17f9da623ac0520291fdf1b97383b5`.
That import path is not evidence of integration.
Synthetic controls never grant product/browser acceptance.

Controls cover predicate reversal, renamed projects, removed/moved helpers and
conditions, changed hook scopes, extra or unsupported skips, wrong native IDs,
source/run/attempt mismatches, wrong execution applicability, missing/duplicate/
malformed/incomplete evidence, retries and interruptions. Retain original failed
controls, native attempts and unsuccessful reviews alongside correction evidence.
Rollback reverts these additive modules and documentation while preserving evidence.

```text
PROGRAM_STATUS=INCOMPLETE
SECURITY_BRANCH_INTEGRATION=HOLD
MAIN_UNCHANGED=true
GOVERNANCE_IDENTITY_DRIFT=OWNER_HOLD
TRUSTED_SONAR_IMPORT=NOT_VERIFIED
PR_J_TO_N_ACCEPTANCE=OPEN
```
