# Same-run native actor measurement candidate

This route produces `SONAR_ACTOR_MEASUREMENT_ONLY` evidence. It cannot qualify a
canonical Main/PR runtime, deployment, Pilot admission or Release GO. The actual
event remains a push. Sonar PR routing metadata never becomes a fictitious PR
merge, reviewed-head stamp or Main build identity.

## Closed admission and trust delta

The unchanged read-only Sonar target router must first admit the owner-selected
push. Measurement additionally requires the original and triggering actors to be
the repository owner, and exactly Main or `ci/sonar-actor-measurement`. The latter
must have the router's exact open same-repository Main-target PR association.
There is no workflow dispatch, arbitrary ref/run/URL/command input, added token
scope, secret or environment. Other Sonar analysis targets retain their existing
behavior and receive no actor report.

The new image tag is exclusively `coglatas-sonar-actor-measurement:<actual SHA>`.
Canonical `archive_binding`, canonical restore and canonical actor admission
continue to reject it. The shared acceptance helper rejects it by default; the
explicit `sonar-actor-v1` profile calls a separate verifier. That verifier reuses
the strict uncompressed OCI/archive graph implementation and proves either the
archive config ID or manifest ID against loaded image metadata, tag and DiffIDs.
It records the representation, never relabeling it as a canonical artifact.

The new producer builds unlicensed Angular and Release .NET from the clean exact
push source in the same job before scanner credentials. It publishes no runtime
image or binary artifact. Its only public outputs are an unchanged validated
one-source native LCOV and a closed finite receipt. Private control, build,
harness, native spec and application diagnostics remain in runner-temp. The
runtime does not contain licensed Main producer output or scanner configuration.

## Actual execution boundary

The frozen actor SHA-256 remains
`b8eaf7577da1503db15ac85f57331090b40c981d932727f648dd2216907ba942`.
Only its standalone native ESM execution inside the existing owned Playwright
container is wrapped as one implicit Node file test. The host stays direct Node;
the existing Bash harness owns Compose setup, signals, image checks and volume
cleanup. The actor's 17 criteria, same-document history, 15-second negative
observation and 240-second watchdog are unchanged. FileSharing must still be
persisted false by the frozen actor. This route grants no intake/scanner/RLS or
authorization-contract change.

The additional overlay pins PostgreSQL 18.6 to the existing Sonar CI digest and
inherits the existing pinned SDK and Playwright image. It has no published ports.
`NODE_V8_COVERAGE`, source-map flags, TypeScript transforms, SDK CLI hooks and host
test-worker execution are absent. Original zero line/function/branch rows are
retained. Strict validation rejects missing or duplicated records, dropped line
rows, invalid totals, source drift, incomplete implicit tests, nonzero/signal
exit, image drift or failed harness cleanup. A passing receipt includes actual
source/tree, run/attempt, image/archive/tool hashes and runtime versions.

After successful runtime cleanup the pre-analysis Release outputs are cleaned,
so the existing scanner begin/build/end interval still compiles the solution.
The existing 75-minute job limit, report scopes, coverage exclusions, Quality
Gate thresholds and analysis credential boundaries are unchanged.

## Qualification limits

This is a local preparation candidate. Portable admission, archive, LCOV and
command/scope controls are not an actual browser journey or official coverage
measurement. Linux Node/container reporter behavior, migration inheritance,
pre-analysis build/clean interaction, actual signal cleanup, runtime duration
and the official Sonar import and Quality Gate still require independent review
and an authorized real run. Missing or incompatible evidence fails closed.
The new direct-host orchestration body is not measured by the container's
actor-only report; its effect on the New Code denominator is unqualified. The
portable parser fixture is never imported as coverage, and no 80% claim is made.

The earlier mapped-TypeScript native coverage counterexamples remain unresolved;
this unmapped native ESM route does not convert or repair historical raw V8 data.
The standard Istanbul VM proof is a separate execution family. The blocked c8
installation was not retried or replaced by an equivalent fetch/execute path.
No Pilot or Release authority follows from this candidate or technical success.
