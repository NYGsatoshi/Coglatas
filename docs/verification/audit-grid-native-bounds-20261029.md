# Audit grid native bounds regression

Main f33a6ee5bc5b977b228b7fc84b37a89c343547df uses Playwright boundingBox() top/bottom properties that do not exist. Both nullish fallbacks make the non-overlap assertion0>=0 even when the opener overlaps the sticky header. Two existing TS2339 diagnostics identify these accesses.

A first native headless Chromium DOM control verifies actual x/y/width/height boxes: a header at y100/height40, a clear button at y150, and the same button moved to y120. The original expression passes both; y>=header.y+header.height accepts the clear control and rejects the actual overlap. No Product or historical incident root cause is inferred.

Use the actual y/height coordinates in the existing bounded-grid owner and fail closed if either native rectangle is missing. Preserve24px dimensions, existing positive visible-row selection, native click, scroll/focus/drawer lifecycle assertions, timeouts, retries and profiles. No new formal acceptance condition or Product behavior is introduced.

Local controlled native Chromium geometry is not the complete Angular app owner. Full current-source frontend/browser CI and native analysis/officialGate/independent review remain required. Whole-repository strict baseline is not claimed green; compare the unchanged remaining diagnostics separately. No module/configuration change or exclusion is made.

No private Spec text, held/shadow code, model, authorization, seed, migration, release gate or approval change. STUDENT_COUNCIL_PILOT=RELEASE_NO_GO.

Maintainer attestation: this reviewed two-file scope is authorized for the existing owner-push analysis route. The first implementation commit is32435b74ebdf5e5e151ff44b22e8fa255e4dacb0. No resolver/token/actor/scanner policy is changed. Current-source native and officialGate evidence must be acquired independently. Strict diagnostic multiset181 to179 removes only the two invalid native rectangle property accesses, with zero additions.
