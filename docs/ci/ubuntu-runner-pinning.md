# Explicit Ubuntu runner pins for Main CI

## Reason and scope

The GitHub-hosted Ubuntu latest-image transition can change the operating
system under existing Main and pull-request checks without a reviewed repository
change. This Main-targeted corrective PR pins Linux job selectors in 67 existing
workflow files to ubuntu-24.04, including Main build, PR CI, functional,
security, supply-chain, quality, performance and release-evidence owners.
The CodeQL Swift job stays on macos-latest; its Linux fallback is pinned.

The COMPAT-02 OS Portability Matrix remains unchanged: its three exact labels
are ubuntu-latest, windows-latest and macos-latest. Its original contract
continues to validate the matrix. Draft PR #1560 remains the separate,
unmerged Ubuntu 24.04/26.04 qualification experiment. Explicitly selected
ubuntu-26.04 comparison jobs are not prohibited.

## Static enforcement

The Publication Readiness check now tests and enforces
scripts/ci/check-ubuntu-hosted-runner-pins.py. It rejects implicit
ubuntu-latest in any workflow outside os-portability.yml, including a
matrix entry or dynamic expression. Tests cover direct, matrix, portable,
pinned, and explicitly qualified runner selections. The workflow trust
registry recognizes ubuntu-24.04 and the bounded CodeQL fallback while
retaining the portability expression and hosted labels.

No workflow trigger, permissions, required check, timeout, skip, retry,
Action pin, security threshold, release gate, production base image or
dependency version is intentionally changed.

## Qualification boundary

This is a runner-selection stabilization candidate, not an Ubuntu 26.04
migration or compatibility approval. Protected PR checks, independent
review, and exact-source resulting-Main verification remain necessary
before any normal merge. The PR does not authorize Main pushes, releases,
deployment, waiver, or student-council admission.

The relevant static commands in a complete checkout are:

    python3 scripts/ci/test-ubuntu-hosted-runner-pins.py
    python3 scripts/ci/check-ubuntu-hosted-runner-pins.py
    ruby scripts/ci/test-workflow-trust-boundaries-upper-bound.rb
    node --test tests/ci/os-portability-contract.node-test.mjs
