# Workspace search: preserve results during IME Escape

Tracking: #615. Original Main baseline: `bd1c0f531aa0465aa20b8ac454a6ce55a8e21bd7`.

## Observed defect and repair

The document Ctrl+K handler already ignored active composition and legacy keyCode229. The search form's Escape binding called unconditional result dismissal without receiving the keyboard event. A composing Escape therefore cleared a populated result list and cancelled its presentation even while the input method was handling Escape.

Route the form-scoped Escape action through the existing document keyboard handler, sharing its existing composition/keyCode229 compatibility guard with Ctrl/Meta+K rather than adding a second legacy API read. Containment is checked against this component's form. Preserve the original unmodified Escape/Esc matching; modified Escape and events outside this form leave populated results unchanged. Normal in-form Escape calls the unchanged dismissal method without emitting shortcut-focus. Unconditional dismissal, request generation/cancellation, Workspace changes, result navigation, HTTP contracts and authorization filtering are unchanged.

## Regression evidence

With production code unchanged and only the two regression cases added, the first targeted Angular/Vitest run built successfully and reported **2 FAIL /7 PASS**. Both failures passed the non-empty authorized-scope result preconditions, then observed status `idle` instead of `ready` after composing Escape.

After the minimal handler repair the same nine cases passed. Add a separate non-vacuous boundary control: begin with visible populated results, preserve them through composing Escape, then change Workspace and require the results and rendered list to clear. Final targeted suite: **10/10 PASS**, zero skip. Normal Escape dismissal remains a positive control, and no additional search request is permitted.

The real frontend dependencies were installed from the baseline lockfile using pinned npm11.17.0 with install scripts disabled. No fake vendor CSS, license input, timeout, retry, assertion weakening or test deletion was used.

These are Angular TestBed / mocked HTTP / synthetic KeyboardEvent controls. They do not establish physical IME, actual-backend search or historical issue1413 failure causality. The original historical failure remains open and unexplained. Held candidate paths were checked; no held implementation was copied or bypassed.

## Hosted finding and candidate reconciliation

HEAD `eed7591cc04ab957950c9fef9f55a7b191bc1c1f`, based on Main7ee07ac0, passed PR CI40900091219/1 and native40900079730/1 but official PR Gate123558238566 **FAILED** for Maintainability B. Preserve that first result. Provider issues were AaGIWTXT8kQG5Vn_4lco (prefer the dedicated array-length matcher in the new populated fixture) and AaGIWTgy8kQG5Vn_4lcp (typescript:S1874, the duplicated keyCode read). Use the dedicated matcher and consolidate keyboard actions through the existing compatibility guard; no reflection, property-type shadow, NOSONAR, rule switch or provider dismissal is used.

Add scope-parity controls for outside-form Escape, result-button Escape/Esc, and each Alt/Shift/Ctrl/Meta Escape modifier. Before applying shared routing, run all17 cases against the actual eed component with the extended controls:17/17 PASS. The wrapper then failed printing a checkmark to cp932; the original UTF-8 log is retained, shows17 PASS, and candidate restoration was independently checked. This was an output-formatting failure after test execution, not a Product retry.

The shared-handler candidate passes the same17/17 cases on the first run with genuine Main1.63 lockfile dependencies, pinned npm11.17.0 and install scripts disabled. The existing Vite native-config warning is retained. These remain synthetic keyboard/mocked HTTP controls; no physical IME/actual-backend search or historical1413 root-cause claim is added. The initial two Product regression failures and eed Gate failure remain recorded.

## Scope and remaining qualification

No visual design, localization string, role/capability model, server policy, schema, FCI06 contract, release gate or Owner HOLD changes. Hosted full tests, strict checks, independent current-head review, normal merge and fresh resulting Main qualification are required. `STUDENT_COUNCIL_PILOT = RELEASE_NO_GO` remains.
