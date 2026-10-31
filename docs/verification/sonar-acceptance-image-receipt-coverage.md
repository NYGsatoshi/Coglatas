# Source-bound Python coverage for acceptance image receipts

This change adds only the exact tracked operational CLI
`scripts/ci/verify-acceptance-image-reuse.py` to the existing Python measurement
include list. Sonar source/rule scope, coverage exclusions, thresholds, workflow
credentials, permissions and bootstrap behavior are unchanged. No operational
receipt validator or existing acceptance assertion is changed.

The existing `test-main-runtime-artifact.py` suite already executes that CLI
using `sys.executable` from separate temporary workspaces. Its synthetic runtime
environment preserves inherited coverage/bootstrap variables while clearing
runtime image identity. Existing controls check all four receipts, each missing
receipt, reset isolation, wrong producer/schema/types, invalid/duplicate/oversize
documents, extra entries and non-regular receipts without echoing content.

The process-coverage regression reuses three of those existing pure CLI controls
without running their Bash/Docker harness siblings. With the already-pinned
hosted coverage package it requires separate child shards, branch data for the
exact tracked CLI, an actually executed receipt-verification line, an untouched
zero distinct-engine binding line, and the same identity/positive/zero evidence
in standard unmodified Cobertura XML. Existing bootstrap and same-suffix
outside-source rejection controls remain intact. No hit or report rewriting is
introduced. This proves measurement behavior, not real runtime-image admission.

Local Windows Python3.12.14 passed the three existing pure CLI controls. The
coverage package is absent locally, so the new measured regression and original
XML qualification are **NOT_RUN** there; no installation was attempted. Hosted
execution, original report/zero audit, independent review and exact resulting
Main qualification remain required. Historical Main Eba Gate65.6% / ReliabilityD
/ SecurityE is retained. Its 74 missed new lines in this file are an inventory,
not newly covered points or a prediction of Main80%.

Files intake/scanner, RLS, Recovery, target environment and Owner admission holds
remain unchanged. `STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.
