# SEC-08 finding-policy preparation (#582)

Status: **NOT_ACTIVATED; initial inventory UNREVIEWED; zero real exceptions**.
This is offline policy tooling, not a new required check or a finding disposition.
It does not complete #582, #695, SEC-16/17, SEC-ARCH or release qualification.
Owner Hold Both for detailed security approval and target-environment references
remains unchanged. No protected target or scanner is dispatched by this tooling.

## Authority and unchanged boundaries

[#582](https://github.com/NYGsatoshi/Coglatas/issues/582) requires a common finding
model, bounded owned/reasoned/expiring exceptions, a reviewed Medium inventory,
fail-closed execution states and secret-safe evidence. [#578](https://github.com/NYGsatoshi/Coglatas/issues/578)
retains SEC-06's initial High/scanner-failure blocking and defers the Medium ratchet
to SEC-08. [#695](https://github.com/NYGsatoshi/Coglatas/issues/695) must reuse this
governance when advanced adapters are implemented.

The existing SEC-06 processor/native schema, ZAP policy/plan/four filters,
scanner thresholds, CI routing and SEC-14 consumers are unchanged. The four
existing filters are not imported or declared approved SEC-08 exceptions.
The native processor still reports Medium findings. #1338 separately adds
existing-processor regression controls and traceability; those are not duplicated
or represented as completed Medium governance here.

## Files and data flow

- `scripts/security/finding-policy.json`: preparation-only repository policy;
  the exception list is empty.
- `scripts/security/finding_policy.py`: deterministic common model, policy
  validator/evaluator and optional sanitized SEC-06 adapter.
- `tests/ci/test_security_finding_policy.py`: synthetic fixtures and actual CLI
  positive/negative controls; no fixture accepts a real risk.

The CLI reads bounded regular JSON files, rejects duplicate fields/non-finite
numbers and unknown contract fields, and requires an explicit UTC evaluation
time. Its two input modes are a closed common report or the existing sanitized
SEC-06 report. It writes a separate sidecar using exclusive creation; an earlier
receipt is never overwritten. A missing/malformed/unsafe report produces a
nonzero exit and no new receipt, rather than a zero-finding result.

The common input envelope has `schemaVersion`, `scanner` (`id`, `version`),
`sourceSha`, `configSha256`, `execution` (`status`, `exitCode`) and `findings`.
Each finding has `ruleId`, `severity`, `confidence`, `findingClass` and `scope`.
Scope contains exact role, method, OpenAPI route template, parameter category
and repository-owned parameter name (or null for no parameter). Unknown paths,
ambiguous routes and unclassified parameters are rejected. Referenced/body
parameter classification is deliberately not guessed; those findings need a
separately reviewed adapter before they can obtain exception scope.

Common producer identities include ZAP API/browser, Schemathesis and security
integration suites; this slice implements only the sanitized SEC-06 adapter.
It does not claim live execution/coverage for any of them. Input source/config
identities are declared metadata and file hashes, not independently verified
producer or execution attestations.

The adapter projects rule/risk/confidence and exact classified scope, discarding
scanner labels, concrete resource paths, query values, bodies and attribution
payloads. It checks the supplied OpenAPI digest, closed metadata, isolation
flags, summary counts and complete alert-instance inventory. A truncated native
report fails instead of silently losing finding scope. It does not recover
alerts already filtered inside ZAP or independently attest the native scan.
Native findings are `generic`; a later trusted adapter must supply any
severity-independent control classification. No new rule-to-policy mapping or
false-positive determination is invented here.

## Fingerprints, matching and approval limits

Domain-separated SHA-256 identities are computed from tool, rule, exact scope
and finding class. `evidenceHash` explicitly identifies safe structural metadata,
not raw matched content. It is not a digest of a protected response or secret.
Run timestamps, source/version changes, instance IDs, raw scanner names and
protected bodies do not enter the fingerprint. Distinct parameter/role/route/tool
scopes remain distinct. Repeated identical metadata is deduplicated deterministically;
the result is an identity inventory, not a raw-alert occurrence counter.

Every future declared exception requires an exact fingerprint and scope, tool,
rule, Medium/generic classification, rationale, owner, created/expiry UTC dates,
tracking issue and approval identity/date/reference. Wildcards, unknown scope,
missing fields, duplicate identities, inconsistent dates or fingerprint mismatch
fail validation. Future/expired metadata cannot obtain a current match. Only
Medium/generic findings can match; High/Critical and explicit authorization,
secret-disclosure, injection or security-control-regression classes always block.
Execution/auth/target/coverage failures block even with zero findings.

Approval records are **structurally validated declarations**. No GitHub review,
Security-owner authority or approval authenticity is checked. Output always says
`approvalVerified: false`, `activation: NOT_ACTIVATED`, `initialInventory: UNREVIEWED`.
It also fixes `evaluationScope: OFFLINE_DECLARED_METADATA_ONLY` and
`liveGateQualified: false` independently of the mechanical evaluation outcome.
A valid synthetic exception produces visible `MATCHED_DECLARED_EXCEPTION`, not
an authenticated acceptance. The repository contains no such real entry.
The offline evaluator applies the prospective Medium rule conservatively; it
does not alter the current live report-only behavior.

## Running the bounded CLI

Use a fresh output path and the source-bound OpenAPI artifact from the producer.
Supply a JSON string array of the ephemeral values to exclude through
`COGLATAS_SECURITY_FINDING_FORBIDDEN_VALUES`; do not echo it or commit its values.
This variable is required and has no unsanitized opt-out. Output is checked
before persistence, including JSON-escaped and URL-encoded forms. Only finite
repository-owned diagnostics reach stdout/stderr on failure.

```bash
python3 -B scripts/security/finding_policy.py \
  --zap-report artifacts/security/zap/alpha-owner.json \
  --source-sha "$SOURCE_SHA" \
  --contract artifacts/openapi/coglatas-openapi.json \
  --policy scripts/security/finding-policy.json \
  --evaluated-at 2026-10-23T12:00:00Z \
  --output artifacts/security/sec08-alpha-owner.offline.json
```

Use `--input common-report.json` instead of `--zap-report/--source-sha` for an
already classified common input. Exit 0 means the **offline evaluation only**
passed; 1 means a blocking finding/execution outcome; 2 means invalid/unsafe input
or a failed write. Consumers must inspect both exit and receipt; an existing
output file is not evidence of a new invocation. There is no live integration,
uploader, target selector, network traffic or required-check change in this slice.

## Verification and remaining work

Run focused controls with:

```bash
python3 -B -m unittest discover -s tests/ci -p test_security_finding_policy.py -v
```

Controls cover High/Critical, low-severity control regressions, unapproved
Medium, a visible synthetic exact exception, expiry, scope mismatches, missing
approval/owner/rationale/dates, broad scopes, stable/distinct fingerprints,
execution failures, missing/malformed/oversized input, unsafe output, preserved
old receipts and the real CLI's exit/sidecar behavior.

Before any live activation, review the initial exact-source inventory; resolve
the governance of existing filters; approve actual exception metadata and
classification scope; connect enabled adapters; validate producer/coverage
provenance, retention and consumers; independently review candidate and resulting
Main execution. Do not create approval dates/identities, grow a baseline, waive
High findings or interpret these mechanics tests as the missing inventory review.
Formal mapping/RLS, target staging, complete restore/rollback and public GO stay
on their existing independent tracks.
