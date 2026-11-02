# Source-backed semantic inspection and synthetic Workbench adapters

Bounded continuation of #905/#1030 and paired #782/#793 work, based on the existing
Web Product candidate and independent Web Test chain. Authority: #888 IDE-START-01
through -03, MODEL-10/17, COMP-04/07, INDEX-07 and PLS-15. This is inspection of the
existing full-reference binding model, not a new compiler, execution plan or API.

`CompilerSemanticInspection` publishes immutable local declaration and port symbols,
actual declared types, bound-reference states and exact Source locations from one
PartialSemanticModel. Input/Source digests and committed/candidate/Scenario context
participate in its deterministic envelope/digest. Partial and unsupported symbols
remain inspectable without IR; Unknown bases never become known values. Inspection
requires one exact symbol location; changed revision, non-symbol paths and ambiguous
locations return unavailable. Incoming references use resolved symbol identity;
missing/coverage-unknown/ambiguous/unsupported/out-of-scope references remain distinct.
Only the local Source's symbols are projected; this adds no external-data loader or
current authorization. Existing compiler reference/evidence limitations remain.

The Angular synthetic exporter adds optional `coglatas.semantic-inspection/1` data
for completed fixtures. Older fixtures remain readable without fabricated symbols.
The adapter validates exact input/Source/context, unique symbol identity, actual
Source declaration/port types and local reference origins. Inspector shows selected
symbols/types and incoming resolved references in English/Japanese. No browser
semantic evaluation, compile request, editing or API operation is introduced.

`tools/ProjectIde.Workbench.Synthetic` is a separate pure .NET adapter project for
Windows preparation, outside the Compiler and generic UI.Core. Its ViewModel exposes
Source, Problems, Inspector and Source navigation with property notifications.
A lifecycle requires a strictly newer desired generation; reset/disposal clears
results/selection/navigation, stale responses cannot replace a current result, and
actual materialization receipts must match. It reads synthetic in-memory compiler
objects only; it registers no application service, window, network or real-data path.
Callers own threading; this prototype does not promise an Avalonia dispatcher.

Independent tests cover deterministic/partial/type/reference inspection, exact
revision/location rejection, cancellation, ViewModel lifecycle/disposal and late
manifest/generation/materialization/status responses. Both consumers use the actual
Core-exported fixture. Browser regression, desktop/mobile a11y and Windows-hosted
pure ViewModel tests are recorded separately from native UI acceptance.

Windows/Avalonia Views, keyboard/IME, accessibility tree, native UI automation and
product activation remain BLOCKED/NOT_RUN under #1144. The pure ViewModel is neither
a running Avalonia shell nor Windows E2E evidence. Real API authentication/current
authority, Requirement evidence materialization, full typed value-reference closure,
Resource evaluation and shared-client acceptance remain incomplete. #1128 stays
SUSPENDED/NOT_EVALUATED. No integration, release or operational GO is claimed.

## Synchronous notification lifecycle correction

The 2026-11-03 CodeRabbit review identified reentrant Begin and Dispose paths in
the pure synthetic ViewModel. A PropertyChanged subscriber may legitimately begin
a newer context, reset or dispose during the outer Begin's clearing notification.
Begin now reserves its monotonic generation/lifecycle before notifying, then
rechecks that lifecycle before publishing Source or desired request. Reset
invalidates pending publication. Dispose marks the instance disposed before
notifying and is idempotent, preventing recursive disposal or a nested restart.
Thread dispatch remains the caller's responsibility; no cross-thread guarantee or
Avalonia activation is added. Independent bounded regressions cover newer Begin,
Reset/Dispose during Begin, reentrant restart and repeated disposal without crashing
the test host. Exact before/after receipts are recorded on #1653/#1654; the earlier
758-case candidate remains historical and does not qualify this correction.
