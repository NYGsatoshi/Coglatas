# Predicate recording input-switch readiness

Existing work: #794 / #782 under #888. This Product test correction makes the
input-switch case await its actual recorded-diagnostic read before navigating
to Problems. Application behavior, fixture bytes and shared CI remain unchanged.

## Original failure and observation

The unchanged full Firefox desktop run at browser Source993ad17dd1ddc89e2bf2c63caba570db8f398797
and retained build3c4d3e433749b6bf1e0b527f08376a4664f0fcd4 executed386 cases:
385passed and the Japanese input-switch case timed out after30seconds. It stopped
waiting for the diagnostic button before the new-input selection or either view
clearing assertion was reached. Original report, trace, video, screenshot and
source/build pre/post pins remain retained.

The trace DOM at16:43:27.031UTC shows Problems Enter while the diagnostic-read
button is disabled and the recording section is absent. At16:43:27.056UTC the
recording section appears, with Source still selected and Problems unselected.
The button helper completing a key press does not await the asynchronous read
or its deferred focus. Focus moving during the key sequence is an inference:
the original trace does not record activeElement or the dispatched key targets.
No failed clearing operation is invented from this earlier timeout.

## Strengthened precondition

The case now requires the recorded-diagnostic panel to receive focus and display
the exact diagnostic run ID from the original Manifest before selecting Problems.
This follows the existing full-recording cases' completion check. It then keeps
the original diagnostic selection, body-count1, new-input selection and both
zero-view assertions unchanged. No arbitrary sleep, forced click, extended
timeout, retry or removed assertion substitutes for the completed read.

The two English/Japanese cases retain their declarations and titles. Later
unmodified declarations in the same file move down three lines; their source
mapping must be recorded explicitly when comparing complete discovery reports.
The application764-input tree and all108 retained generated files remain exact.
This change does not rebuild or restamp the original application artifact.

## Qualification

The changed-case results remain separate from the original failed full run.
The work-only selection passed10/10, with one attempt per case and0retries,
skips or flaky outcomes, on the two established
Chromium projects plus Firefox desktop, WebKit desktop and WebKit mobile. It
retains original30second test/5second expectation deadlines, one worker and
retry0. Actual discovery/results,180source/helper pre/post pins,108build
pre/post pins, runtime versions and all108 HTTP bodies were verified. The
original Firefox failure ID now passes with the unchanged clearing assertions;
raw changed-case report SHA256 is
`dd8ea689feb1953f5eb42f45a7db50465f093605e6efa9a4e6f000817f19b5e1`.
Two private harness preflights stopped before test execution: the global JSON
output path first redirected discovery elsewhere, then a JavaScript replacement
expanded the shell regex anchor into the script. Original logs/statuses remain;
explicit discovery output routing and literal command construction with shell
syntax validation corrected only those helpers. Neither preflight has case credit.
A capability probe is
not Product acceptance, and Linux WebKit emulation is not physical Apple hardware.
The existing shared compatibility selection and workflows are not changed.

The existing canonical Chromium selection can be executed after the normal
repository setup and frontend build:

~~~powershell
node node_modules/@playwright/test/cli.js test tests/ui/projectide-predicate-recordings.spec.ts --grep "clears visible original arrays when selecting a new input: (en|ja)$" --retries=0 --workers=1
~~~

This document's existence does not imply execution. Exact committed source,
counts, failures, artifacts, review and CI outcomes belong in the PR. The
original Firefox385/1 result is not relabeled as a full passing rerun. Actual
Avalonia, server authorization and independent Security/privacy acceptance stay
separate. Existing P0 responsibilities, #1144 BLOCKED, #1128 SUSPENDED/NOT_EVALUATED
and Registry/Trust/Eligibility/RLS/Native/Production/Release holds remain.
