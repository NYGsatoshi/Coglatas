# Non-enabled Files upload presentation preparation

The Files page reuses the existing shared CoglatasFileUploader selection, drop, single-file queue and Cancel/Retry presentation. It does not introduce another uploader or mount the older independent UploadDropZone file input. The page binds the existing facade upload state and handlers. Known totals render native determinate progress; unknown totals omit the progress value and retain an accessible label.

## Admission boundary

FilesPageComponent.uploadsAvailable remains the literal false. The default page keeps its disabled Upload button and pilot intake explanation, renders no file input, and rejects direct acceptUpload/retryUpload calls. No admission flag, API, scanner, quota, backend, RLS or deployment configuration changes. Server authorization remains authoritative. This preparation does not establish Pilot B implementation readiness or authorize either pilot release.

An approved future activation requires DEC-FILES-INTAKE decisions covering scanner operations, privacy, limits, quarantine, tenant policy and upload admission. A separately reviewed activation change would need to replace the FilesPageComponent.uploadsAvailable restriction, reconcile its existing tenant FileSharing admission boundary, and qualify the actual browser-to-API/PostgreSQL/storage/scanner journey. That change is absent here. Do not infer activation authority from the presence of an unreachable template branch or these tests.

## Reused lifecycle and retry eligibility

The existing facade owns HTTP upload, retained transient failures, explicit retry, cancellation, generation/workspace/session isolation and success inventory refresh. Single-file selection remains explicit: this does not claim batch queue execution. The shared adapter's optional canUpload presentation input blocks selection/drop and Retry while preserving an in-flight Cancel; its existing global disabled input still blocks every control. Optional uploading/progressPercent inputs describe the current facade operation without guessing a queue identity from filenames.

A read-only canRetryUpload projection checks the existing retained-failure map and current Workspace/generation. The page supplies per-item eligibility to the adapter: cancelled files and non-retained 401/403 failures do not advertise a dead Retry. Existing adapter consumers that omit this optional override retain their prior behavior. No file is newly retained, no failure policy changes, and no authorization or mutation path changes.

## Source and local verification

The local branch starts at Main 88f8648cbcc9286407ea17e75cb2b9b7ed6ff599, normally merges the reviewed Files cancellation/artifact-compensation candidate 6d9edece60a5e95b74a39d686cf9f1ea7a9dcb85, then normally merges Main 99f257b5a2b0859581386cefe9b07abf2cb931c0. The approved dependency histories remain intact. This document records local results, not hosted checks, independent approval, Main acceptance or a merge decision.

Pinned Linux Node 24.22.2 and npm 11.17.0, existing dependency volumes read-only, network disabled:

- Complete Files cohort plus shared uploader: 216 PASS across 21 spec files, no failed/skipped tests. This includes the unchanged production-default no-input/no-POST/direct-handler guard, 48 existing facade regressions, eight adapter cases and six new intercepted-HTTP presentation cases.
- Production build and theme sanitizer: PASS (59.260-second build; sanitizer scanned six stylesheets and changed one).
- Frontend architecture check: PASS.

The six future-presentation cases use an explicit test-only property override and HttpTestingController; every HTTP request is intercepted. They exercise single-file forwarding, unknown/known progress, Cancel during a busy operation, transient Retry without duplicate POST, success inventory reconciliation, non-retained denial, busy older-retry gating and session cleanup. They are not real browser intake, PostgreSQL persistence, storage byte-integrity, scanner, revocation or production acceptance evidence. ScanStatusSkipped remains skipped; no clean-scan assertion is introduced.

An earlier ten-test diagnostic preceded the eligibility projection and used the image-bundled npm 11.19.1. Its log is retained separately and gives no final-source qualification credit. Existing Vitest configuration and CDK focus warnings remain visible in the final cohort log; no assertion, timeout, retry or required-check rule was relaxed.

The production build retains configured initial-bundle and component-style budget warnings; no budget threshold was changed. Runtime source was unchanged between the final 216-test cohort and production build. Local artifact hashes and exact final source identity are retained in the qualification packet; hosted CI, official Sonar gate and independent current-head review remain pending.
