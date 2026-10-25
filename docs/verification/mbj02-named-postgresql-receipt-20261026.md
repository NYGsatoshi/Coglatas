# MBJ-02 fixed PostgreSQL result receipt

## Problem and boundary

At Main `0eb156a773f8763b4fe7e1c14acead1fd4806560`, the MBJ-02 runner executes `Scope=Issue527` but does not invoke the required-name TRX verifier. The licensed workflow retains `test-results/` only on failure. Successful logs therefore do not provide an independently retained, strictly reconciled eight-fact result.

The migration service already bind-mounts the checkout at `/src`. `--rm` removes its container, **not** the host-bound TRX. This change adds no mount or extraction copy. The existing PostgreSQL facts already require a real connection and fail in CI when it is absent; they do not return an unexecuted success.

## Change

- Keep the exact `Scope=Issue527` selection, test project, Release configuration, logger, existing deadlines, and assertion/fixture/authorization behavior.
- Write raw TRX and SDK output to an exclusive per-run/attempt private directory under `artifacts/mbj02-issue527-private/`, outside the existing failure artifact globs. Do not publish those files.
- Require eight distinct exact fully qualified names, matching result/definition/entry identities, expected assembly paths, individual outcomes, and consistent counters. Reuse `verify-trx-results.sh` with minimum eight and the fixed manifest as an additional gate.
- Project only a bounded 16 KiB closed JSON receipt: current clean checkout SHA, run/attempt, fixed selection, eight outcomes, primary acceptance/test exit codes, TRX and post-test assembly SHA-256, and matching live PostgreSQL version queries before and after the selected tests. No result output, exception, environment, connection string, path, user data, or machine identifier is projected.
- Recompute the projection immediately before publication. Refuse stale source/run metadata, changed raw TRX/DLL, links, malformed/oversized XML, XML entities, duplicate JSON keys, unknown receipt fields, and incorrect field types. Publish only the validated fixed JSON path, on success or failure; a valid failure receipt still fails the gate.
- Preserve the primary test/runner exit code if evidence capture also fails. Missing/invalid producer evidence fails publication; early setup failure or cancellation does not acquire test credit or a fabricated receipt.

`verification=VERIFIED` describes the selected eight facts only. `status=PASS` additionally requires the entire MBJ-02 wrapper to exit successfully. A later browser or persistence-probe failure retains the eight outcomes while the receipt remains `FAIL`. The producer's `acceptanceExitCode` is the primary wrapper exit code; an evidence-only gate failure may make the workflow fail despite that code being zero.

## Evidence limits

The receipt binds current source, real test selection, guarded PostgreSQL execution, and the digest of the TRX-referenced assembly **on disk after the run**. It is not an attestation of the assembly actually loaded by VSTest, compiler reproducibility, or the application image. The version probes observe the configured database service; they are not independent transport capture of every test connection. Source inspection of the unchanged PostgreSQL facts and their connection guard remains part of assessment. A successful receipt is not complete invitation, SEC-17, security architecture, staging, backup, or pilot release acceptance.

## Initial local controls

On the unchanged Main source `0eb156a773f8763b4fe7e1c14acead1fd4806560`, the native pinned SDK 10.0.402 built the test assembly and ran the exact eight selected facts against an exclusively owned PostgreSQL 18.6 container: **8 passed, 0 failed, 0 skipped**, no retry. The real 13,084-byte TRX confirmed the producer's namespace, eight exact FQNs, result/definition/entry linkage, lowercase `storage`, and case-preserving `codeBase`. Raw output remains local and private. This result establishes the fixture format and does not qualify the later tooling commit or the full licensed application/browser lane.

The candidate's focused Python controls cover positive projection/publication; missing, extra, renamed, duplicate, failed, skipped, inconsistent and malformed results; wrong IDs, paths and namespace; DTD/entities/size limits; missing, stale and linked files; source/run/type mismatch; changed DLL/TRX; unknown fields and duplicate JSON keys; primary failure preservation; and a synthetic secret canary confined to private output. Source-bound candidate execution results belong in the PR evidence packet after its final source SHA is fixed.

No production, test fixture, dependency, migration, security policy, release gate, Owner hold, or merge permission changes. Release remains `RELEASE_NO_GO`.
