# SEC-04 Workspace icon input attribution candidate

## Original first-attempt failure

Main `55a17da2b4ebdbe6f5df84909fa0d2f78ef7ff3c`, CI `40852143497/1`,
Security job `123375998593`: security-core and ZAP exit0, Schemathesis exit1.
Deep alpha-owner seed695621768 reports two `sqlstate` disclosure-policy failures
on Workspace POST201 and collection GET200. Three earlier roles pass; beta-owner
is not reached. CI finishes43success/4failure, including three dependent gates.
The original failure is preserved, not rerun or waived.

Exact evidence was inspected in memory: the marker occurs only in `data.icon`
and one collection row's `icon`. The scanner's request icon exactly equals the
returned icon without normalization. POST Location identifies that same row,
and its recorded POST201 precedes the failed GET200. No raw generated values,
IDs, protected content or auth material are reproduced. Artifact12889836716,
14034897bytes, ZIP SHA256
`73c90719540e94f9229f7e8344a6b71f671b2b4d847a8b68ee9fe6993becb5de`.

Product legitimately validates, trims, persists and echoes icon text. This
specific provenance does not establish a Product internal-error disclosure.
Independent source investigation confirms the existing checker attributes only
integration displayName, so the Workspace input remains outside its proof scope.

## Narrow scanner boundary

Preserve the integration POST200/collectionGET200 contract. Add only canonical
`/api/workspaces` POST201 envelope.data.icon: a nonempty canonical UUID and exact
scanner string input must match. Only the same role, tenant, resource ID, field
and value can then be attributed in collection GET200. Resource path and field
are part of the hashed proof key, so equal integration and Workspace IDs/values
cannot contaminate each other's proof. The existing4096-entry capacity is shared.

No Python strip approximation, field normalization, broad name/description
exemption, whole-row suppression or global diagnostic-keyword exception is added.
Authentication material remains blocking before attribution, including escaped
material. Every other field, nested memberPreview, error, stack, unknown ID/value,
route, method, status and scope stays inspected. Duplicate JSON cannot establish
proof; failed checks cannot seed reads. Parsed copies are altered only for
inspection; original request/response payloads remain immutable. No Product API,
authorization, identity, DB/RLS, Recovery or FCI06 DefaultC change is made.

## Regression and measurement

- Real shared-policy replay reproduces original POST/GET false positives with an
  ordinary-input positive control. New17tests initially fail15assertions against
  the old policy, then all17pass after the narrow repair.
- Existing fixed-version Schemathesis4.25.2 hook tests retain all27controls and add
  one Workspace generated-Case bridge:28pass. This local package replay uses the
  exact published Main55a OpenAPI3.1.1/298paths/916939bytes, SHA256
  `dc8c9b8a7606251b5c93a6150e912aca08d3731817d671760ebbbec4008c5860`.
  It is not the digest-pinned scanner image or a live server acceptance run.
- Seventeen policy controls plus28actual-hook controls measure104statements and
  56branches at100% in coverage7.17.0. This local module result is not Sonar NewCode
  coverage or an official Gate. Add the operational policy to the existing narrow
  Python measurement and the17controls to its existing pre-credential sequence;
  retain every existing control and threshold. Update the exact subject guard.
- A complete Windows curated-sequence attempt is not PASS: the pre-existing
  POSIX-only MBJ02 reader lacks O_NOFOLLOW here (24errors), and7Linux controls skip.
  No portability/symlink protection is weakened. Full Linux hosted execution,
  official Gate and actual five-role deep scan remain required.

Fresh source/merge/tree reconciliation, independent candidate review, required
checks, digest-pinned actual-backend deep qualification, ordinary merge and
resulting Main qualification remain pending. Keep all budgets, checks, seeds,
roles, deadlines and retries. No SEC-08 exception, formal Security decision,
Owner HOLD release or pilot admission is conferred. RELEASE_NO_GO.

## First hosted secret-scan follow-up

Initial ba7f74f6 publication job123407808916 and PR security123409139647 fail
Gitleaks8.24.3 generic-api-key at the new unit fixture line90. The value is a
noncredential local test canary passed only to the policy forbidden-values
argument; it is not a service credential and grants no access. Preserve both
first failures. Generate the canary at runtime and add only the original immutable
commit/path/rule/line fingerprint under the repository's existing historical
false-positive policy. No path/commit-wide ignore, rule change, history rewrite,
real exception or formal Security approval is introduced. Pinned scanner replay
of the original range and a different-commit same-path/line control are required.


Pinned Gitleaks8.24.3 (published archive SHA256
`3f1a35578631dbfe633cc5b49e6c906e55ff14a4bfd7336a10fb27fe33b6dcd2`)
replays the immutable fac..ba7 range:1noncredential fixture finding before,0
after the exact fingerprint. A separate disposable commit with the same fixture
path/rule/line is still detected:1finding/exit1. No broader exception is created.

## First official Coverage result and test classification

Sonar check123418472540 on ba7 attempt2 is FAIL at79.6%, not PASS. Provider
file-level measures identify all11uncovered new lines in
`scripts/security/test-schemathesis-input-attribution.py`; the operational
`schemathesis_policy.py` is100% on new code, including all18conditions.
The hook control file contains only unittest.TestCase classes and helpers, and
the existing scanner runner executes its28tests. Classify that exact test-body
file consistently with the existing CI/Node test-body coverage exclusions. Keep
it in source issue/security/duplication analysis and retain all28live controls.
Add an AST/runner guard proving the exact excluded file remains a unit-test body.
No production code is removed from coverage; no threshold, NewCode definition,
security/reliability rule or test execution changes. A fresh official Gate on
the resulting source remains required.
