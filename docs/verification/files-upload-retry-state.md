# Files upload retry state

Related: Student Council Pilot #615. The existing multipart/progress/cancel
adapter is retained; the production Files page still has disabled Upload and no
file input. No acceptance, intake, scanner or privacy policy is activated.

The adapter removed failed uploads from its active-subscription map, while Retry
looked only in that map. Retrying a failed transfer therefore emitted no request.
Keep the selected File separately in browser memory, bound to the original
workspace and page generation, and reuse the same client queue identity for a
manual retry. Active and failed attempts remain separate so refresh reconciliation
cannot mistake a failed transfer for active I/O. Double Retry cannot start a
second request; completion/cancel release the attempt. Closed synchronous HTTP
subscriptions are not registered as active work.

Invalidate retained failed Files with the existing page generation on scope
change, forced reauthorization and protected-state clear. Check the active
workspace again before retry and discard a mismatched attempt. HTTP 401/403
failures retain no retry File. Every new multipart POST still uses current
cookie/CSRF and server-side authorization. No File bytes enter localStorage,
logs, a new public model or a new endpoint. A client queue identity is not a
server idempotency key; commit/response-loss ambiguity is not solved here.

Two positive regressions first failed on unchanged Main: no retry POST, while
31 existing owning controls passed. Eight new cases cover manual retry, original
File/owner/queue identity, double click, progress, cancellation, success/inventory,
workspace switch, forced reauthorization, protected-state clear, active-signal
mismatch and 401/403. Corrected pinned Linux execution: 39 owning tests and all
19 Files suites/193 tests PASS, production build and existing theme sanitizer
exit0. Existing focus/Vite/style-budget warnings remain unchanged. Initial
missing dependency-mount setup failure and premature pre-build sanitizer ENOENT
are retained separately; neither is called a functional test pass. Dependency
volumes were mounted read-only; no dependency or threshold was changed.

These are isolated mocked-HTTP adapter controls and disabled-intake UI unit
controls, not a real UI-upload success or scanner qualification. Fresh exact-head
hosted/native/security/quality/review and protected real-stack disabled-intake
regression remain required before integration. All Owner HOLDs remain.