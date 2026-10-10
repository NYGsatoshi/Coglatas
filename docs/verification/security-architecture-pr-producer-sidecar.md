# SEC-ARCH PR producer source binding

The existing `.NET build producer` packages reusable Release outputs and their
internal `artifacts/ci/dotnet-build-sha` stamp. Its original PR artifact upload
published only `dotnet-release-build.tar`. The independent SEC-ARCH ZIP consumer
also requires a bounded top-level `source-sha`; an actual downloaded PR artifact
without that sidecar remains an `ERROR`, even when its observed tests passed.

The packaging step now captures the actual checked-out Git HEAD once, validates
its exact SHA form, and writes those same bytes to the internal build stamp and
top-level source sidecar. The original `dotnet-release-build` artifact publishes
both files from the same runner directory. Job name, artifact identity, build
scope, upload condition, retention, compression and every required CI context
are preserved. The Main producer already publishes its original source sidecar
and is unchanged.

The consumer continues to compare independently supplied candidate, run,
attempt and downloaded ZIP digests, both source stamps, six canonical assemblies
and the actual Test-bin copies. The producer change grants no approval or
attestation authority. Its JSON reports continue to retain trusted attestation
`UNVERIFIED`, owner approval unset and PRE-AVALONIA `BLOCKED`.

Focused controls execute the actual YAML packaging step with real local Git,
Bash and tar against synthetic build bytes, then construct exactly the members
declared by that upload and pass them to the unchanged byte consumer. They prove
a positive six-assembly result before mutating source or run/attempt identities.
Missing, empty, wrong or concatenated sidecars remain rejected. Original input
archive bytes are compared after rejection to detect accidental rewriting.

The before-fix run retained one topology failure and two missing-sidecar errors;
the repaired run passes all 25 producer/consumer controls. The local fixture
does not perform a real application build or authenticate a GitHub run. Actual
same-run hosted producer and execution artifacts still require independent
download and reconciliation after this revision reaches its own PR workflow.
Historical artifacts and failures remain scoped to their original candidates;
adding a sidecar to a later producer cannot upgrade old missing-sidecar ZIPs.

The broader 275-control SEC-ARCH, Required Check topology and runtime-restore
suite passes against an immutable prospective Git tree exported with
`core.autocrlf=false`, with the shell bytes independently checked for CR.
Two earlier diagnostic runs retain existing shell-launch failures caused by
the Windows checkout/archive conversion. Those failures are preserved; no
existing shell source, baseline or test assertion was changed to hide them.
