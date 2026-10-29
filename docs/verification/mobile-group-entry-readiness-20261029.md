# Group fixture Workspace restoration readiness

## First-attempt evidence and limits

Mainfac295cc867e6bc796971e2fba55745d08aa2962 native Mobile MVP0 run40861079628/1,
job123404176758, fails at the immediate Workspace switcher GUID assertion in
`tests/ui/group-dm-real.ts:26`, before the three additional actor contexts exist.
Retry0, one required case, zero verified profiles. The closed reporter records
failed, not timedOut. Artifact12891662849,533bytes, ZIP SHA256
`600e351a437008221f532014bd2b0c1d7b4f677c67a1d056e88220bbdb21df95`.
No raw selected value or response body is retained. The original failure remains;
older Main55a/PR Mobile passes do not qualify this Main. #1513 stays open.

The preceding DM helper reloads and checks DM/API readiness. Separately, the
approved Workspace selection lifecycle clears the protected header projection
while authorization is pending; top-bar renders a disabled empty selection.
An immediate inputValue read is not a Workspace readiness assertion. The exact
state/response chronology of the original failed run is missing, so its initiating
cause is not established by this separate regression.

## Independent fixture repair

Extract the existing GUID precondition and require the select to be enabled and
hold the same canonical GUID before capture. Keep the final original GUID
assertion after reading, so invalidation between readiness and capture still
fails. Never select a fallback Workspace, preserve stale protected UI, synthesize
an ID, ignore denial, or issue a new command from this guard.

Default existing Playwright assertion deadlines and the original120s owner budget
remain; no retry, forced click, timeout setting or assertion is removed/relaxed.
The original Group/DM commands, membership/isolation/revocation/recovery oracles,
Desktop case and native Mobile case/title/profile are unchanged. FCI06 DefaultC,
Product authorization/model/schema and all Owner HOLDs are unchanged.

## Controlled browser regression

Three tests execute the real guard against a controlled DOM, with retry0:
allowed loading-to-selected completion, permanently disabled stale GUID, and
completed empty selection-required state. They run in both static Chromium
profiles and do not require or claim a real backend.

Local Windows headless Edge155.0.4283.45, Playwright1.64.1, Node24.22.1:
Desktop and320x800 touch: old immediate guard4FAIL/2PASS; repaired guard6PASS,
0skip/retry,23.8s. No sleep, custom assertion timeout, trace, video or screenshot
was used. The negative controls use the existing assertion deadline and remain
blocking. This does not prove that the historical failure had the same chronology
or replace native licensed actual-backend acceptance.

Fresh exact-revision CI/strict typing/independent review/native Sonar/actual Mobile
MVP0/source-tree-image proof and resulting Main verification remain required.
Keep the first Mainfac failure and every earlier failure visible. RELEASE_NO_GO.


## First official coverage failure and measured existing controls

On f719, official check123423436465 fails0.0% NewCodeCoverage. Preserve that
first result. Native Mobile MVP0 run40865211011/attempt1 separately passes the
original case with4actual320x800touch actors/retry0; artifact12894121084 ZIP
SHA2562c85b7a79839b8774b79ef58028d6ac4f20cf83570ee154f59f2ad72989bae3b
and imagefcd0b7093e877b0a8ba93c3d3526bf4f55b44b0cff9d61cdfd2e928613db933e
were independently checked against actual build/source/post-inspection metadata.
That old HEAD does not qualify subsequent source changes or close historical cause.

Run the existing three DOM controls in both desktop and320touch projects through
a native Node coverage wrapper. The same six browser cases execute, without a
Product server, frontend/license substitute, test removal or extra retry. The
focused configuration preserves the established static30second case timeout and
5second assertion timeout; actual-backend owner120seconds is unchanged. Linux
uses the lockfile-pinned Playwright headless Chromium; local Windows uses the
installed Edge in an isolated headless test context. No user browser profile.

The pre-credential Sonar sequence installs that pinned browser and adds one Node
wrapper to the existing120controls. Native Node source maps produce original
TypeScript LCOV for the real helper and browser test body; do not rewrite hits or
exclude either source file. Existing tracked-file/identity/line-bound validation
still applies. A local six-case experiment passes and validates3sources/11branches
with no invalid original-source lines. Most pre-existing Group fixture functions
are not exercised by these isolated controls; their whole-module coverage remains
low. Only the official current-source Gate can establish NewCodeCoverage.

The full local measured Node/browser command exits0; unchanged strict validation
accepts24tracked sources/1255branches/116uncovered branches without altering
the generated LCOV. This is not an official Sonar Gate or real-backend run.


## Independent review: regular static selection

Review4411713252 is valid for the original f719 static runner and remains
partially valid on f04: its explicit staticSuiteFiles list omitted this new spec.
The dedicated native Node invocation on315/f04 does execute the six DOM cases,
but that does not establish their selection in the normal Angular browser suite.
Correct the earlier regular-suite assumption and add the exact spec to the
existing explicit list. Retain every existing selected file, configured profile
skip, assertion, case timeout and retry setting. Verify actual Playwright listing
on the regular configuration, then require the new HEAD's full hosted static
execution. Do not count the old437regular passes as including these six cases.
