# Private local inputs in Docker build contexts

## Scope and compatibility

The root `.dockerignore` excludes `.tmp/` and `artifacts/`. These directories can
contain local execution evidence and preparation files that the source-image
builds do not require. Existing root exclusions remain unchanged.

The runtime-image producers are an explicit exception: `main-build-artifacts.yml`
and `scripts/ci/package-pr-functional-runtime.sh` prepare the published Web output
and hosted frontend under `artifacts/main-runtime/publish/` and
`artifacts/main-runtime/frontend/`. Those are the two existing `COPY` inputs of
`infra/docker/runtime-prebuilt.Dockerfile`. Its adjacent Dockerfile-specific
ignore file admits only those payload trees and preserves the previous root
filters within them. Docker gives this file precedence over the root ignore file;
see the [Docker build-context documentation](https://docs.docker.com/build/concepts/context/).
Adding only the blanket root exclusion would remove these required inputs.

The distinct `security-runtime-prebuilt.Dockerfile` used by Main Security retains
its existing staged context, including required prebuilt `src/**/bin` and `obj`
outputs. No Dockerfile, producer, image consumer, payload generation, application
contract, or scanner policy changes in this patch.

## Bounded verification

Qualification uses fresh owned directories with fixed nonsecret canary bytes and
the exact Git-sourced ignore files. Actual Docker builds use `FROM scratch`,
`COPY` only, no network, no pull, and no cache reuse. Created containers are
exported for inspection and never started. No repository source, real artifact
payload, credential, or private evidence is copied into these diagnostic images.

The before/after checks require:

- Root private-directory canaries are included before the change and excluded
  afterward; existing exclusions and representative source/configuration inputs
  retain their previous behavior.
- The separately staged Main Security context retains its prebuilt `bin` and
  `obj` canaries.
- Both runtime payloads remain available at the existing `COPY` destinations.
- A separate entire-context export with the specific override excludes unrelated
  root files and artifact siblings, while retaining both required payload trees.
- A blanket-exclusion control without the specific override fails because a
  required runtime payload is missing. This expected failure is a negative
  compatibility control, not a successful application build.
- The before/after payload inclusion matrix is identical, including the prior
  nested `bin`, `obj`, and license-file exclusions.

Execution receipts bind the source commit/tree, exact ignore-file hashes,
diagnostic commands, exported canary manifests, outcomes, and ownership-checked
cleanup. Postcommit receipts are retained separately so updating an execution
record does not change the tested source identity.

## Limits and finding status

This is build-context hygiene for demonstrated unnecessary input inclusion. The
inert checks do not demonstrate an application image build, disclosure of a real
secret, an unauthorized image consumer, or comprehensive secret classification.
In particular, the previous root-only filters do not exclude every nested file
inside a permitted runtime payload; representative nested `.env`, `.tmp`,
`secrets`, log, and NuGet canaries retain their previous inclusion behavior.
Payload producers remain responsible for their intended generated contents.

Sonar Docker alerts #3718 and #3719 remain open; this patch does not dismiss,
waive, or change their severity. Historical scanner attribution and the broader
#582 finding/reporting work remain separate. No formal SEC-ARCH mapping, RLS,
target environment, risk acceptance, or release approval is granted.
