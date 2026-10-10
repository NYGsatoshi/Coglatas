# SEC-14 release evidence consistency and dependency feasibility

This mechanical continuation strengthens the existing release-evidence helper.
It does not qualify #614 or claim a release, signature, attestation or personal
owner approval. The inspected integration base is
`0038afa15802ce6edb7ad094d3bb2c396c82e654`; the separately fetched public Main
was `2f7e6da4d319c4c9b8ebfd8fba766d295d16fee0`.

## Exact candidate and source controls

The unchanged helper accepted internally consistent evidence with a missing
Git source dependency, a dependency from another repository or commit, and
evidence differing from independently supplied candidate, subject or run
expectations. The preserved baseline ran 20 tests: 14 passed and six deliberate
invalid controls failed their rejection assertions.

`verify-evidence` now requires the expected repository SHA, immutable GHCR
subject and exact GitHub run/attempt URL. The existing verification job supplies
these from the publish job outputs and current trusted workflow context.
The helper also validates the existing SLSA builder's exact Git dependency URI
and commit digest. Existing signing inputs, keyless identity restrictions,
Cosign operations, original publish-byte comparison and promotion dependencies
are preserved.

JSON inputs use a bounded stream read of at most 32 MiB plus one overflow byte,
with a maximum structural nesting depth of 128. Duplicate fields, non-finite
numbers, invalid UTF-8, missing/empty files and non-object roots fail explicitly.
Expected identities have closed syntax and finite length. String contents and
escape sequences do not count as structural nesting.

The new exclusive `coglatas-release-consistency-advisory-v1` receipt records
matched supplied expectations and mechanical consistency. It always reports
GitHub run authentication, cryptographic verification and personal approval as
`UNVERIFIED`, including when an input JSON claims `verified` results. Existing
receipts cannot be overwritten. Only the existing later actual Cosign commands
can provide signature and attestation verification evidence; the Advisory
receipt does not replace them or authenticate its own supplied expectations.

Example invocation:

```bash
python3 scripts/ci/release_supply_chain.py verify-evidence \
  --evidence release-signing-evidence.json \
  --expected-repository-sha "$RELEASE_SHA" \
  --expected-subject "$IMMUTABLE_SUBJECT" \
  --expected-run-identity "$EXACT_RUN_ATTEMPT_URL" \
  --advisory-output new-release-consistency-advisory.json
```

## Three unresolved dependency alerts

The independently authenticated live Dependabot observation on 2026-10-10
retains alerts **121**, **122** and **123**, all `OPEN` / `HIGH`, package
`braces`, advisory `GHSA-vfj7-8cjw-p6xm`, with `first_patched_version: null`.
Its original snapshot SHA-256 is
`fb002414d2580069f71e3c3d1761739a5c73109a8196ba9a72faa766e15dbd2a`.
That immutable original is retained separately; no alert was dismissed and no
exception was introduced.

| Alert | Lockfile | Exact installed chains to `braces@3.0.3` |
| --- | --- | --- |
| 121 | `coglatas-frontend/package-lock.json` | `@angular-devkit/build-angular@22.2.1` → `http-proxy-middleware@3.0.7` → `micromatch@4.0.8` → `braces@3.0.3`; also build-angular → `webpack-dev-server@5.2.6` → nested `chokidar@3.6.0` → `braces@3.0.3` |
| 122 | `frontend/package-lock.json` | Same two installed chains; `@storybook/angular@10.6.1` additionally consumes the build-angular peer |
| 123 | `tools/frontend-inspections/package-lock.json` | `stylelint@17.16.0` → `micromatch@4.0.8` → `braces@3.0.3`; Stylelint → `fast-glob@3.3.3` → Micromatch → Braces; Stylelint → `globby@16.2.4` → Micromatch → Braces; Stylelint → Globby → Fast-glob → Micromatch → Braces |

Each affected lock contains one development-dependency `braces` instance.
The root `package-lock.json` contains none. These source/build dependency
observations do not establish runtime-image reachability or a not-applicable
disposition. The inactive UI remains untouched.

The [official GitHub advisory](https://github.com/advisories/GHSA-vfj7-8cjw-p6xm)
still lists affected versions through 3.0.3 and no patched version. Published
npm metadata still identifies 3.0.3 as latest. The pending
[advisory correction PR #10132](https://github.com/github/advisory-database/pull/10132)
is open; its dispute is not a withdrawal or remediation.

A supported `webpack-dev-server@6.0.0` / modern Chokidar branch can remove the
old Chokidar edge, but
[http-proxy-middleware@4.2.0](https://github.com/chimurai/http-proxy-middleware/blob/v4.2.0/package.json)
still requires `micromatch@4.0.8`, which
[requires `braces`](https://github.com/micromatch/micromatch/blob/4.0.8/package.json).
The current
[Stylelint 17.16.0 package](https://github.com/stylelint/stylelint/blob/17.16.0/package.json)
also still requires Micromatch and the Fast-glob/Globby paths. Updating these
supported dependencies therefore cannot remove all three findings.

Removing the active build-angular dependency would also remove the configured
Storybook browser builder and its required framework peer. Replacing Micromatch
with a differently shaped API, installing an unofficial fork or replacing the
SCSS inspection engine has no established compatibility proof. No such alias,
fork, deletion or scanner suppression is proposed as a safe remediation.

## Remaining #614 acceptance gaps

Issue #614 remains open. Its configured individual lanes and passing mechanical
controls are not a final integrated acceptance record. Source inspection found:

- PR security and Required Check topology controls are present. Existing
  dependency baseline acceptance and Trivy `--ignore-unfixed` cannot establish
  the separate release condition of zero unresolved Critical/High findings.
- Main has isolated Core, Schemathesis and ZAP paths plus independent functional,
  SBOM and SEC-ARCH lanes. No complete Tier-B and required advanced penetration
  acceptance aggregation was demonstrated for one exact release candidate.
- No RESTler, OAST collector or SEC-20 advanced acceptance implementation was
  found under the inspected `scripts/security`, `scripts/ci` and workflow paths.
  Applicability and approved callback authority remain separate dispositions.
  Optional licensed Burp cannot substitute for required ZAP evidence.
- The SEC-04 weekly cron is commented out with the existing CI-consumption
  explanation. It remains unchanged; a configured manual lane is not an active
  weekly deep scan.
- Release signing verifies an exact immutable subject and exact SBOM/provenance
  predicates. The release workflow does not itself bind the existing vulnerability
  gate and complete Tier-B acceptance to that same subject. A separately rebuilt
  image from the same source SHA does not prove equal image digests.
- Current Medium policy uses inventory mode; it is not an enforced reviewed
  Medium ratchet. New exceptions or policy promotion require their existing
  owner/governance review.

The remaining integrated gate, same-subject vulnerability evidence, advanced
coverage and schedule qualification are real engineering work. Passing this
helper's controls does not resolve them, authorize infrastructure activation,
or qualify PRE-AVALONIA SEC-ARCH as PASS.

## Local verification

The final patch passed **34 release controls** (14 existing and 20 new), all
**45 unchanged Required Check topology controls**, and **39 existing SBOM,
Grype-source, vulnerability-policy and component-inventory controls**: **118
tests**, zero failures or skips. Tests ran with networking disabled against a
read-only checkout in Python 3.13.12 / Ruby 3.4.9. No release, signing or registry
write was performed. Exact-head hosted qualification remains pending.

The development receipt preserves the initial Ruby-unavailable environment
failures, six unchanged-helper invalid failures, and an intermediate nesting
control failure before the explicit depth bound. Its SHA-256 is
`04be39aff16eb2c9ef2c832ee1d64c4219b307bf772acec6f677076a4ca8b9ae`.
Existing evidence bytes, six Required Checks, workflow identities, scanner
baselines, schedules and owner boundaries are unchanged.
