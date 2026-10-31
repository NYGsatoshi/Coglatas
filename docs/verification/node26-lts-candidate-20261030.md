# Node.js 26 LTS migration candidate (2026-10-30)

## Isolation and provenance

This is a **Draft, test-only migration candidate** on `test/node26-lts-20261030`, targeting `main` without permission to merge. It starts at Renovate's existing Node 26 workflow-only commit `c4eb4085e2a2633a87f0505afa265fd1887c9a91` (PR #1437), whose parent is Main `36471e4d9f16b8b98b9731e850f1d76b41c98b4e`. Neither the Renovate branch nor Main is modified by this candidate.

Node.js 26 entered LTS on 2026-10-28. Angular 22's published Node range includes `^26.0.0`. This establishes vendor-declared compatibility, **not** Coglatas runtime acceptance.

## Candidate changes

- Move the 24 workflow `actions/setup-node` version references from Node 24 to Node 26, retaining existing action SHA pins, workflow permissions, runners, conditions, and test scopes.
- Pin the production frontend dependency stage to official `node:26@sha256:6d2c7d5d074bf72f81ddc252b4fbb5d625bc028eeda49cb4b8c2ff8eda1ee617` (multi-platform index) and the development stage to `node:26-alpine@sha256:c6ad9e9c1d80f6eb607c26b4b0a132ff7a2344fad1c78ea3baa2a7d4c8de6d94` (multi-platform index), both identified with Node 26.13.0 at preparation time.
- Align the Node toolchain guard, its preflight report, and Renovate's Docker major-version hold. Keep the pre-existing negative-control tests: a future unsupported major and a missing digest must still fail.
- Retain npm `11.17.0`, existing npm lockfiles, the Angular/Playwright dependencies, backend SDK/runtime, licenses, authorization, and required CI checks.
- Update only active development/testing instructions. Do not rewrite historical evidence for Node 24.

## Deliberately retained independent runtime

The pinned Playwright WebKit image currently runs Node `24.21.0` / npm `11.19.0`, with a separate exact-runtime guard. Do not relabel its historical/isolated WebKit evidence as Node 26 execution, and do not modify its browser image/engine or weaken that guard in this PR. Other browser/functional runs must be interpreted according to their actual execution environments.

## Acceptance and evidence status

All items below start **not yet verified**; configuration and a Draft PR alone are not execution evidence.

- [ ] Run `node scripts/ci/verify-node-toolchain.mjs` on the candidate.
- [ ] Run `ruby scripts/ci/test-dependency-update-policy.rb` including existing negative controls.
- [ ] Verify Node toolchain preflight and the script-free production Docker dependency smoke actually pass using the candidate image digest.
- [ ] Verify clean root/frontend npm installs, Angular production build, Vitest tests, architecture checks, and applicable Playwright checks under Node 26.
- [ ] Verify the applicable CI checks, OS-portability matrix, static/security checks, and any licensing-specific lanes in their authorized environment; retain initial failures and skipped paths.
- [ ] Independently review the effective PR diff, Node 26 image provenance, and regressions before any release decision.

**Gate: REVIEW_ONLY / DRAFT / DO_NOT_MERGE.** Preserve existing Student Council pilot release holds and security approvals. Do not merge to Main, trigger production deployment, alter protected check policy, or claim Node 26 acceptance until exact-candidate evidence exists.

## Reference documentation

- Node.js release status: https://nodejs.org/en/about/previous-releases
- Angular compatibility: https://angular.dev/reference/versions
- Candidate Docker image digests: https://hub.docker.com/layers/library/node/latest/images/sha256-a9faa17e89c1bbe1a93009afed1b72afe549a5985bad5bc92bc9566f95a6ab4a and https://hub.docker.com/layers/library/node/26-alpine/images/sha256-d431b1d60cd8de74dc66d271c236f66227998c308e011e60faa9c60dc4d37382
