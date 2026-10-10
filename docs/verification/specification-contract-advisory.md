# specification-contract Advisory adapter

Tracks #841 using the #835/#836 registry implementation. The stable
`specification-contract` step and sanitized artifact run inside the existing PR
backend and Main Test jobs. A stable, **non-required** `specification-contract`
job/check consumes those artifacts without running tests again. Required Check/job names, rulesets, security scanners,
architecture-contract authority and all performance thresholds remain unchanged.
No private checkout, new secret, product infrastructure or enforcement promotion
is introduced. **PRE-AVALONIA SEC-ARCH: BLOCKED.**

The adapter reuses existing architecture/backend TRX and SEC-ARCH execution
receipts. It checks exact clean checkout SHA, the existing build stamp when
present, TRX definition/result identities and counters, execution time, raw TRX
digest and matching SEC-ARCH candidate/run/attempt. Representative receipt
metadata is compared with a fresh observation of those same TRX bytes; claimed
case counts or outcomes cannot substitute for actual observation. Existing lanes
are neither rebuilt nor rerun by this summary.

Observed lane PASS remains separate from canonical requirement linkage. The
SEC-ARCH count is explicitly divided into tooling, inventory, representative
runtime and unclassified cases. Synthetic schema and owner-review API fixtures
remain tooling, not authenticated personal approval or product contract coverage.
Unknown, skipped, missing, failed and stale evidence cannot become canonical
PASS. Raw logs, test parameters, private prose, source paths and credentials are
excluded from public artifacts.

## Canonical input and authentication dependency

Public CI has no approved private canonical registry/manifest/source input.
Its report records `canonicalInputs.status=UNAVAILABLE`, an unknown source
revision and unknown declared/qualified requirement and relationship counts.
Missing input is not zero coverage or `NOT_APPLICABLE`. The source/schema/family,
class/status, mapping and limitation counts cannot be manufactured from synthetic
tests. The missing dependency is shown in both JSON and the job summary.

In an authorized private environment, the same adapter can invoke the existing
compiled `traceability-check` CLI with a complete private registry, manifest,
contracts, source checkout, independently supplied source SHA and exact public
candidate. This is a mechanical integration seam; it does not approve the
private candidate packet. The compiled tool and input bytes are digested, its
exit/report must agree, and counts must reconcile. A changed verifier, source,
contract, retired/missing mapping or wrong-candidate execution remains visible
through the existing validator.

An optional `--baseline-registry` invokes `traceability-transition-check` and
records the retained baseline's byte digest alongside the current inputs. It
detects removal of retired identities, rewriting retained versions and registry
rollback through the same validator. Without this independently retrieved
baseline, historical allocation integrity remains unqualified. Supplying a
baseline does not authenticate its governance authority or personal approval.

Every input, including the compiled tool, optional links and retained baseline,
is fingerprinted again after invocation. An observed byte change is an integrity
error and cannot retain the earlier fingerprint as evidence. These observations
are not an atomic filesystem snapshot; they cannot contain an arbitrary process
that replaces and restores files between reads. Source blobs remain pinned to
immutable Git revisions, and independent artifact/producer reconciliation is
still required. JSON inputs reject duplicate keys, non-finite values and nesting
deeper than 128 containers, with bounded diagnostics.

Only counts and opaque hashed diagnostic references are projected publicly.
Declared registry family/severity/class/lifecycle counts are labelled Draft;
known limitations appear as counts plus fixed public blind-spot codes, without
copying private explanations. Unresolved/stale diagnostics remain explicit,
with a bounded list and a truncation flag. Complete detailed findings remain
private. Manual mappings never become automatic PASS.

All reports retain `normativeReady=false`, personal owner approval and trusted
execution attestation `UNVERIFIED`, and qualified normative counts unknown.
Self-reported links/build stamps and a saved approval JSON cannot authenticate
themselves. The live scoped owner-review resolver and independent producer/run/
artifact reconciliation must be completed separately before a consuming gate
can qualify normative relationships. The adapter deliberately rejects supplied
tool metadata that claims unqualified approval, attestation or readiness.

The private specification `spec-traceability.yml` currently validates only
CompetitiveAudit tracking. Its success does not establish this SEC-ARCH registry
or canonical source authority. This adapter complements that workflow without
modifying it, importing private prose or claiming a cross-repository producer
that has not been configured and executed.

## Commands and qualification

```text
python3 -B scripts/ci/specification_contract.py --candidate-sha <exact-SHA> --architecture-trx <existing-architecture.trx> --backend-trx <existing-backend.trx> --sec-arch-execution <existing-execution.json> --output <new-summary.json> --markdown <new-summary.md>
python3 -B scripts/ci/specification_contract.py --candidate-sha <exact-SHA> --registry <private-registry.json> --manifest <private-manifest.json> --contracts <private-contracts.json> --spec-root <private-checkout> --spec-source-sha <independent-source-SHA> --tool-assembly <same-candidate-tool.dll> --execution-links <private-links.json> --output <new-summary.json>
python3 -B scripts/ci/specification_contract.py --candidate-sha <exact-SHA> --registry <private-registry.json> --baseline-registry <independent-retained-baseline.json> --manifest <private-manifest.json> --contracts <private-contracts.json> --spec-root <private-checkout> --spec-source-sha <independent-source-SHA> --tool-assembly <same-candidate-tool.dll> --execution-links <private-links.json> --output <new-transition-summary.json>
python3 -B scripts/ci/test_specification_contract.py
```

Outputs cannot overwrite historical evidence. Input/report reads are bounded;
the tool invocation is shell-free, has a deadline and never echoes stderr.
Metadata integrity errors produce sanitized ERROR receipts and a failing
Advisory step. `continue-on-error` is explicit on that reporting step while the
finding/artifact remains visible. Existing tests and required verifier lanes
keep their existing failure behavior. Deterministic adapter mutation controls
execute in the established CI preflight and Main Test workflow.

The PR consumer depends only on the existing `build-test` producer. The Main
consumer is a top-level job in `main-build-artifacts.yml`, depending on the
existing `main-validation` call. It is deliberately outside that reusable
workflow: an Advisory failure there would otherwise change the result consumed
by Main's existing required aggregators. None of the six required jobs depends
on `specification-contract`. Both consumers use `if: always()` and same-run
artifact downloads. The new non-required check fails visibly for missing,
malformed, wrong-candidate/run/attempt, stale or ERROR receipts. Valid missing
canonical input remains UNKNOWN/UNVERIFIED with no normative coverage credit.
The download step can continue after a missing artifact so the consumer can
publish a sanitized ERROR; the job result itself is not suppressed.

Configured integration is not hosted execution evidence. #841 remains incomplete
until approved canonical inputs, authentic owner scope, mapped existing verifier
identities, trusted exact-candidate evidence and SA-07/#842/#614 qualification are
established. This mechanical slice introduces no issue closure or promotion.
