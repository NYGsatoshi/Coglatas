# Files row input-lifecycle regression controls

## Authorized scope

Baseline Main `38b598f89a4bb705396df2e7bc269d547e4aacd4`. This adds focused Angular component/template regression tests for existing server-projected download states. Product implementation, API, authorization models, policies and database remain unchanged. Tracking: #615. Existing Files page grant-denial, scan-state and inert-override coverage remains in place.

## Controls

- Six non-vacuous input transitions: pending/unavailable/blocked scan, denied policy, removed download capability and pending authorization. Each first completes an enabled synthetic canonical-ID action, then verifies both disabled native-button behavior and the component guard, then replaces the input and requires an explicit new action using the new canonical ID.
- Failed authorization permits a manual retry without automatic dispatch or substitution of the presentation row ID.
- Both English-to-Japanese and Japanese-to-English changes update the denied action and reason while retaining denial and emitting no download.
- Replacing an enabled row with an admin-override-required row retains the existing inert override path with and without its presentation capability, including input of a reason. No override action is enabled or wired.

## Local verification

`node frontend/node_modules/@angular/cli/bin/ng.js test --watch=false --include=**/file-row.component.spec.ts`: first execution **11 PASS**, zero failures/skips, on the Main 1.63 dependency installation. The existing Vite native-config warning remains visible. No timeout, retry, assertion, production source or test-scope relaxation.

These are JSDOM component/template controls using synthetic input projections and output observations. They establish neither actual HTTP authorization nor actual UI upload/download acceptance, physical-device behavior, server-driven revocation or the historical cause/disposition of a pilot failure. No physical/browser acceptance is inferred. Fresh PR CI/native analysis, official Quality Gate, independent review and resulting-Main qualification remain required.

`STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`. All formal Owner holds remain.
