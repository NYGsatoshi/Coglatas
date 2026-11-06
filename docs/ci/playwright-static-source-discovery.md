# Playwright full-source test discovery — bounded read-only trial

## Scope

This is a **dependent Draft** on #1716. The base for integration remains
`SecurityTestImplimation`, never Main. The protected `frontend-test` continues
to execute all original Playwright/Vitest checks, including keyboard/Search
observations and their canonical uploads. All six required contexts remain
unchanged. No second runner or Artifact upload is added.

## Exact-source discovery

Playwright **v1.64.0** supports `test --list --reporter=<module>`; list mode
loads and reports the test suite through Reporter `onBegin` without launching
the tests. The original 24 static spec filenames are **read directly from**
`tests/ui/run-angular-playwright.mjs` by a strictly bounded parser, not
copied into another source-of-truth list. Nonliteral/duplicate/extra spec
selection fails closed.

During this **one PR head branch only**, the existing `frontend-test`
runner executes an additional read-only CLI step after its normal Node/Angular
installation and before the **unchanged** original full-suite launcher.
The exact accepted trial branch is
`ci-opt/playwright-full-source-list-20261105`, base
`SecurityTestImplimation`. The trial validates `git rev-parse HEAD`
against `GITHUB_SHA`.

The custom list-only reporter gathers `suite.allTests()` and validates
each `TestCase.id` and expected PASS/SKIP state with the pure
`constructSourceOnlyDiscovery` helper. No raw test title, file name, network
content or test error is emitted. The Reporter flags any real test execution,
global error, duplicated lifecycle or malformed discovery as failed, while
suppressing Playwright's regular title-printing list reporter.

The trusted wrapper captures the Reporter stdout, requires **exactly one
JSON object** with the accepted schema, original **456 test cases**
(strictly source-discovered expected statuses; the **13 actual runtime
skips are not necessarily declared in `--list`**), canonical
sorted SHA-256 identities, and an independently recomputed SHA-256 roster
digest. Invalid stdout, unexpected counts, SHA drift or digest mismatch
fails without disclosing the raw stdout/stderr. It writes **one local,
exclusive, owner-only file** in `artifacts/ci/`, whose name is fixed.
No file is uploaded; a later independent attestation process must
consume it through an explicitly reviewed producer.

## Remaining trust gap

`coglatas.playwright-source-discovery.v1` labels its evidence
`SOURCE_LIST_ONLY_NOT_EXECUTION_PROOF`, with
`requiredCheckQualified=false` and
`artifactTrust=EXTERNAL_VERIFICATION_REQUIRED`. Source list discovery does
**not** attest that a test ran, that a Shard result belongs to the same
immutable artifact, that there were no retries, or that a GitHub Actions
artifact came from the expected producer job. It cannot replace the current
full `frontend-test` Required Check.

This trial also consumes some extra CPU in the **existing** frontend runner
and should be measured. Do not claim runtime savings from roster collection.
The separate additive two-runner trial #1715 was closed without merge after
+20.1% Runner consumption; this is not a repeat of that experiment.

## Validation

- Native Node negative tests validate strict static-suite extraction,
  privacy-safe reporter stdout, exact-456 count refusal, source SHA,
  canonical digest, exclusive local output and symlink-denied parents.
- The guarded native PR CI step proves the original v1.64 CLI actually
  enumerates source tests (unlike synthetic mock-only tests).
- The existing original frontend-test, 6 required statuses, SEC-04/06
  roles and permissions stay intact.

No integration into Main. Use only `SecurityTestImplimation` once full
exact-head CI and independent reviews are satisfied.

The source-only verifier requires exactly 456 test IDs and permits at most
13 **declaration-time** skipped expectations. Playwright can call `test.skip`
during execution, so a declaration-time count of 0 (or another number no
larger than 13) is **not** evidence that the original 13 runtime skips were
executed or observed. It emits `runtimeSkipAttestation=NOT_PERFORMED`.
The original, independently executed full Playwright suite must still
pass 443 and skip 13; the aggregate/shard receipts must eventually
attest precise individual IDs and skip justifications, never rely on
the declaration-only count as a shortcut.

## Race-safe local output handling (Linux CI only)

The local roster writer opens the checkout root and each ancestor directory
through stable directory file descriptors using `O_DIRECTORY|O_NOFOLLOW`
and Linux `/proc/self/fd/<fd>` relative paths. The `artifacts/` and
`artifacts/ci/` parent handles remain open while the fixed JSON leaf is
created with `O_EXCL|O_NOFOLLOW` and permissions `0600`. This avoids
a pathname-only `lstat` to `open` race involving symlink substitutions.
Failures never echo the raw JSON/test names, and the local roster is not
uploaded. This is not an independent GitHub producer or retry attestation.
