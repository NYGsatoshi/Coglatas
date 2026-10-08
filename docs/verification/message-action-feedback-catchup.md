# Completed Message action feedback during catch-up

Prerequisite Main qualification repair for the pre-Avalonia Security Foundation.
Audited Main: `6843daf7165f0ce281fbe0300dc132bfbe3586be`.

## Failure and reproduction

[Main CI run 37775793206](https://github.com/NYGsatoshi/Coglatas/actions/runs/37775793206),
[licensed real-backend job 113307651473](https://github.com/NYGsatoshi/Coglatas/actions/runs/37775793206/job/113307651473),
failed the mandatory MVP0 journey at `real-backend-smoke.spec.ts:4564`.
The real Report POST returned HTTP success with `status=OK`, while the browser
could not find `message-action-status` containing `Report request recorded.`
Seven other required P0 cases passed; the failed journey receives no credit.

The existing deterministic settlement regression now also checks feedback after
the authoritative reload. Against the unchanged facade it reports 44 passed and
one failed: expected the Report acknowledgment, received `undefined`.
This isolates a product lifecycle defect without changing browser waits,
timeouts, retries, API assertions, fixtures or required tests.

## Repair boundary

Routine catch-up already waits for protected commands to settle, then starts a
new generation and clears local action state. Its restoration path previously
handled only unsubmitted delete/report confirmation dialogs. A completed Report
produces idle action state with safe feedback, so its acknowledgment was lost.

The same restoration path now retains that feedback only after a fresh readable
conversation in the same generation. It does not overwrite a newer action,
pending request or feedback. Existing confirmation target revalidation remains.
Session, Tenant, authorization and Workspace invalidation still cancel requests
and clear state immediately. A denied authoritative reload cannot restore the
acknowledgment. Restoration sends no mutation and performs no retry.

No request/response DTO, authorization policy, endpoint, package, lockfile,
migration, screenshot baseline or hosted generated artifact is changed.

## Verification

- Red: focused Messaging suite, 44 passed / 1 failed on the unchanged facade.
- Green: focused Messaging suite, 50 passed / 0 failed/skipped, including four
  completed-feedback security-boundary cases and denied-reload coverage.
- Full Angular suite: 1,154 passed across 122 files, 0 failed/skipped.
- Production Angular build and frontend architecture check: passed. Existing
  stylesheet budget and tooling warnings remain unrelated to this facade change.
- Hosted current-head required checks and post-merge exact-Main licensed P0,
  Backend, Security and Functional verification: pending candidate execution.

Local command: `npm --prefix frontend test -- --include=src/app/features/messaging/messaging-ui.spec.ts`.
Mocked Angular tests establish the lifecycle contract, not backend integration.
The unchanged real-backend journey remains the hosted acceptance check.
This record does not claim Security Foundation delivery or production readiness.
