# Acceptance image receipt retention and completeness

Date: 2026-10-19

Base: `780f8c94e29c1568328a661621afb8279e17714b`

## Retained failed audit

Main [run 39150862060](https://github.com/NYGsatoshi/Coglatas/actions/runs/39150862060),
attempt 1, [job 117745080559](https://github.com/NYGsatoshi/Coglatas/actions/runs/39150862060/job/117745080559)
concluded SUCCESS. Independent inspection of the continuity artifact
`12145282131` found only the MBJ-01/02/03 receipts. The MVP-A AuthZ receipt was
missing, so the required four-receipt completeness audit **FAILED**. Job success
does not override that result.

The downloaded ZIP is 913 bytes, SHA-256
`a49153fd4a74af8cebb8d02b79fd94a69a0a451a48f6e56378c91e7db4dd8131`.
The [independent audit](https://github.com/NYGsatoshi/Coglatas/pull/1257#pullrequestreview-5564189925)
is recorded on PR #1257.
The artifact and detailed execution records remain outside Product Git.

The AuthZ wrapper wrote its receipt into `test-results`. The subsequent My Tasks
runner invokes Playwright, whose default output-directory cleanup removes
`test-results` before testing. Later MBJ receipts were written after that cleanup
and survived. The previous isolated per-wrapper fixtures used separate temporary
workspaces, so they did not exercise this sequence. The prior
[implementation record](acceptance-prebuilt-image-reuse-20261019.md) remains a
historical record and does not establish complete hosted receipt retention.

## Repair scope

Only the four sanitized continuity receipts move to
`artifacts/acceptance-image-reuse/`. Their filenames and four-field JSON contract
remain unchanged. Generic Playwright output and cleanup behavior remain intact.

The licensed workflow now performs these reuse-only operations:

1. Reset the four prior receipt files before the acceptance lanes. The fixed
   directory must resolve to its exact intended location inside the current
   workspace; redirected parent directories are rejected. Reset unlinks only
   the four canonical filenames and does not recursively remove a directory.
2. Run a shared-workspace fixture across actual AuthZ/MBJ shell wrappers and
   the installed Playwright CLI's default cleanup. All fixture files and fake
   Docker state live in their own temporary workspace, separate from real job
   receipts. The trivial no-browser test exists only to trigger actual output
   cleanup, not to qualify a browser or backend journey.
3. After successful full acceptance, require all four current producer-bound
   receipts before job success. Missing files, invalid UTF-8/JSON, duplicate or
   extra fields, wrong schema/types, wrong producer image ID, a false match flag
   or wrong scope fail the job. Extra files/directories and non-regular receipt
   entries also fail; unknown files are not deleted or given qualification credit.
   The expected image ID comes from the existing
   validated producer restore; a tag is not recaptured as a new baseline.
4. Always upload the four exact artifact paths in reuse mode, retaining partial
   receipts after genuine failures. Existing failure diagnostics remain.

The validator reads bounded regular receipt files, rejects redirected parents
and emits only a fixed diagnostic and canonical filename on invalid content.
It does not dump receipt payloads, Compose configuration or environments.
Docker image IDs are public content digests, not credentials.

No product source, Compose definition, dependency, lock, license handling,
custody policy, database contract, threshold or approval policy changes.
Ordinary local build/acceptance behavior remains unchanged. SEC-ARCH, Mapping,
RLS, exposure/deployment and Avalonia HOLDs remain unchanged.

## Verification and qualification limits

The existing `performance-contract.yml` suite includes the new validator path
in its exact PR triggers. It runs all harness tests without installed browser
dependencies, modeling the normal `test-results` removal. The licensed workflow
uses its already installed Playwright dependency for the actual CLI variant;
no new dependency or install step is introduced.

Local commands use Git Bash and the existing bundled Python:

```powershell
$env:PATH='C:/Program Files/Git/bin;C:/Program Files/Git/usr/bin;'+$env:PATH
$env:COGLATAS_RECEIPT_TEST_PLAYWRIGHT_CLI='C:/Users/Hirota Kaname/RiderProjects/Coglatas-webwave-coordinator-20261019/node_modules/@playwright/test/cli.js'
& 'C:/Users/Hirota Kaname/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe' scripts/ci/test-main-runtime-artifact.py -v
```

The actual CLI is Playwright 1.64.0. Omitting the explicit CLI environment value
runs the dependency-free modeled cleanup variant. Both variants execute the
real four shell wrappers against synthetic fake-Docker responses in one shared
temporary workspace, prove the old `test-results` sentinel disappears, reject
the incomplete set after AuthZ, and validate all four stable receipts after MBJ.

The focused validator controls remove each required receipt in turn, reject
malformed/mismatched content, verify exact reset scope, and check redirected
parents with actual Windows junctions or Linux symlinks. They retain the original
producer-binding and no-build controls.

Local results:

| Check | Result |
| --- | --- |
| Full suite with actual Playwright CLI cleanup | 20 methods PASS / 0 FAIL / 0 SKIP, 91.399 seconds |
| Dependency-free modeled cleanup variant | 1 method PASS / 0 FAIL / 0 SKIP, 15.984 seconds |
| Changed shell syntax | 5 PASS |
| Existing `js-yaml` workflow parsing | 2 PASS; parsing does not establish Actions execution |
| Staged diff whitespace check, including new files | PASS |
| Gitleaks v8.24.3, ten changed files | PASS, no leaks, no new ignores |
| Linux FIFO and required-file symlink branches | NOT_EXECUTED on Windows; hosted Linux execution required |

The Windows tests execute non-regular directory rejection and both redirected
parent junction controls. A successful method count does not claim that its
Linux-only branches ran here. The immutable head is recorded in the PR receipt.
Machine-local logs remain outside Product Git as `receipt-retention-final.log`
and `receipt-retention-modeled-cleanup.log`.

These are harness and cleanup checks, not real HTTP, PostgreSQL, browser-feature
or complete runtime qualification. Actual Docker startup is **NOT_EXECUTED**
locally because the Linux engine is unavailable. Exact-head CI and a fresh Main
run with independent inspection of all four current receipts remain required
before claiming hosted four-receipt continuity. The failed Main audit above
remains failed regardless of later results.
