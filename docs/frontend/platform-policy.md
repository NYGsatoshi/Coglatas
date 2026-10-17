# Frontend platform policy — Angular Web / Avalonia Desktop

**Policy clarified:** 2026-10-18 (JST) from the owner's October 2026
Web = Angular / Desktop = Avalonia direction, reaffirmed in the
[Penpot-based Web implementation PR #1234](https://github.com/NYGsatoshi/Coglatas/pull/1234).
This record clarifies platform ownership; it is **not** a new security or
product-feature approval.

## Maintained platforms

| Target | Stack | Current disposition |
| --- | --- | --- |
| Web/browser | Angular 22, TypeScript, HTML, SCSS in `frontend/` | **Maintained active product frontend.** New authorized Web presentation/functionality, maintenance, i18n, and accessibility changes may proceed against existing approved server contracts. |
| Desktop | .NET 10 / Avalonia 12.1.x | **Separate planned frontend.** Security/platform/CI qualifications and release evidence remain required. |
| Mobile / other host | To be qualified independently | No unmeasured release or full-feature equivalence is claimed. |

The Angular browser app is **not deprecated** and is **not scheduled for
automatic deletion** after an Avalonia cutover. Keep its source, runtime,
toolchain, licenses, tests, hosting, and CI as necessary for an active Web product.

## Historical roadmap reconciliation

The original September 2026 all-Avalonia path in
[#764](https://github.com/NYGsatoshi/Coglatas/issues/764) and
[#798](https://github.com/NYGsatoshi/Coglatas/issues/798) and the
proposed Angular retirement in [#803](https://github.com/NYGsatoshi/Coglatas/issues/803)
do **not** authorize deprecation/removal of the maintained Web application.
The associated source inventories, historical issue text, test evidence and
approved Desktop-oriented architectural requirements are retained for
traceability, not reinterpreted as Web retirement obligations. An Avalonia
Browser/WASM exploration under [#799](https://github.com/NYGsatoshi/Coglatas/issues/799)
is not a prerequisite for supported Angular Web delivery and cannot silently
replace it. A genuinely new Web removal/platform change requires a fresh
explicit owner decision.

The November presentation's Angular-primary evidence rules and any
Desktop technical-preview/release gates remain separate: passing a Web
CI or demo gate does not qualify Avalonia Desktop.

## Shared contracts and guardrails

- Existing ASP.NET Core Application/API and authorization remain authoritative
  for Tenant, Workspace, Project, Task, Files, Session and Realtime behavior.
- Angular presentation state lives in Angular Signals/RxJS facades and cannot
  directly reference the C# `Coglatas.UI.Core` assembly. Share semantics
  through reviewed API/interaction contracts, not vendor-specific UI types.
- Generic C# UI contracts used by Desktop remain renderer/vendor-independent.
- The [SEC-ARCH pre-Avalonia gate](../verification/security-architecture-preflight.md)
  remains **BLOCKED**. This record grants no RLS, PostgreSQL migration,
  production activation, SPEC mapping, permission, or Required CI exception.
- Distinguish `implemented`, `tested`, `approved`, and `released`.
  Missing advanced ProjectIDE/File/Thread APIs remain deferred rather than
  fabricated as successful browser workflows.

## Quality and active evidence

The [Web PR #1234](https://github.com/NYGsatoshi/Coglatas/pull/1234)
provides bounded Penpot revision-339 Web and locale evidence; it is not a
certification of every design board or unsupported workflow. The
[PR #1234 post-merge Main CI](https://github.com/NYGsatoshi/Coglatas/actions/runs/39034748592)
passed on exact merge SHA
`2718fb59952341a6a6f2ccfb2670a1ce9933ca62`.
Subsequent main commits require their own source-bound CI evidence.

The existing frontend lint/Stylelint enforcement suspension remains in
effect **as quality debt**, not a signal that Angular is retired. A successful
SKIPPED marker is not an analyzer PASS. See
[static analysis strategy](../quality/static-analysis-strategy.md).
Restoring enforcement requires a separate reviewed change; this policy does
not weaken existing checks or edit workflow configuration.
