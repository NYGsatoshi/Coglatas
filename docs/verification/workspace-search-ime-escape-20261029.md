# Workspace search: preserve results during IME Escape

Tracking: #615. Approved Main baseline: `bd1c0f531aa0465aa20b8ac454a6ce55a8e21bd7`.

## Observed defect and repair

The document Ctrl+K handler already ignored active composition and legacy keyCode229. The search form's Escape binding called unconditional result dismissal without receiving the keyboard event. A composing Escape therefore cleared a populated result list and cancelled its presentation even while the input method was handling Escape.

Route only the form's Escape event through an IME-aware entry point. Active composition or keyCode229 returns without consuming the event. Normal Escape calls the existing dismissal method. Unconditional dismissal, request generation/cancellation, Workspace changes, result navigation, HTTP contracts and authorization filtering are unchanged.

## Regression evidence

With production code unchanged and only the two regression cases added, the first targeted Angular/Vitest run built successfully and reported **2 FAIL /7 PASS**. Both failures passed the non-empty authorized-scope result preconditions, then observed status `idle` instead of `ready` after composing Escape.

After the minimal handler repair the same nine cases passed. Add a separate non-vacuous boundary control: begin with visible populated results, preserve them through composing Escape, then change Workspace and require the results and rendered list to clear. Final targeted suite: **10/10 PASS**, zero skip. Normal Escape dismissal remains a positive control, and no additional search request is permitted.

The real frontend dependencies were installed from the baseline lockfile using pinned npm11.17.0 with install scripts disabled. No fake vendor CSS, license input, timeout, retry, assertion weakening or test deletion was used.

These are Angular TestBed / mocked HTTP / synthetic KeyboardEvent controls. They do not establish physical IME, actual-backend search or historical issue1413 failure causality. The original historical failure remains open and unexplained. Held candidate paths were checked; no held implementation was copied or bypassed.

## Scope and remaining qualification

No visual design, localization string, role/capability model, server policy, schema, FCI06 contract, release gate or Owner HOLD changes. Hosted full tests, strict checks, independent current-head review, normal merge and fresh resulting Main qualification are required. `STUDENT_COUNCIL_PILOT = RELEASE_NO_GO` remains.
