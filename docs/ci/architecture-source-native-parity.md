# Architecture source-to-native name-set reconciliation (advisory)

## Scope

This draft adds an exact C# Fact-name to .NET TRX testName set comparison to
the existing non-required Architecture advisory. It reuses both original
Architecture source discovery and native TRX-result validators. No existing
test definition, assertion, test-selection, reporter, runner, workflow
permission, release rule, scanner policy or protected Required Check changes.

The source roster comes from the original two Architecture test sources and
the separate negative-oracle test source. The native .NET TRX must first pass
the original strict thirteen-case verifier, including unique native testId,
Passed outcomes, exact expected names, and consistent zero-skip counters.
Every native testName is then hashed using the source inventory's exact
domain-separated algorithm. The entire sorted hashed set must match the
source-derived set; an equal-count source renaming cannot pass.

The bounded verifier and synthetic controls run in the existing advisory job
after the original native TRX check and before the existing receipt upload.
No extra test runner or artifact owner is introduced.

## Explicit assurance limits

The outcome SOURCE_TRX_NAME_SET_MATCH establishes only local textual name
correlation under the checked-out workflow input. TRX testName does not
independently bind native SDK TestCase IDs to compiled source declarations.
The source collector recognizes a deliberately narrow C# grammar, not Roslyn.
TRX sourceSha, runId and attempt values are caller-supplied until verified by
an independent producer. Hashed names are not signatures.

These output fields stay false, even after a local name-set match:

- sourceShaAuthenticated
- nativeSdkCaseIdsVerified
- artifactProvenanceVerified
- requiredCheckQualified

This result is not D10 GitHub Artifact byte authentication, Source/native
SDK identity acceptance, CodeRabbit independent review, governance approval,
or a seventh protected Required Check. Original skipped, failed, cancelled
or retried attempts stay separately visible.

## Negative controls and usage

The synthetic controls reject missing/extra native results, duplicate native
test IDs, failed/skipped/aborted test outcomes, malformed or DTD-bearing XML,
source-name substitutions preserving a count of thirteen, missing source
files, invalid run identity, oversized TRX files, symlinked TRX leaves, and
same-size path substitutions between file inspection and opening. The parser
reuses the native verifier's same-descriptor bounded TRX input reader; no
second, less strict admission path is maintained. Symlink creation failures
in the focused test are test failures, **never** silent skip/pass credit.
The focused control inventory remains ten authored Python test methods.
A refusal prints only ARCHITECTURE_SOURCE_TRX_PARITY_REFUSED; raw names and
XML contents are not printed.

Run the controls locally:

    python3 -B scripts/ci/test_architecture_source_native_parity.py

The original advisory workflow invokes the verifier after its existing
source and native checks, reusing the same checkout and TRX path:

    python3 -B scripts/ci/architecture_source_native_parity.py \
      --root . \
      --trx "$RUNNER_TEMP/architecture-trx/architecture-tests.trx" \
      --sha "$GITHUB_SHA" --run-id "$GITHUB_RUN_ID" \
      --attempt "$GITHUB_RUN_ATTEMPT"

A future immutable producer/Artifact binding must independently validate the
run and input identities before any broader acceptance. This candidate
requires exact-head native CI and the prescribed CodeRabbit review before
integration is considered. It does not replace either source #1880 or
source-only #1879 and preserves all original source/CI evidence.

Rollback reverts this one additive step/path routing and its three dedicated
files while retaining all historical execution records.

PROGRAM_STATUS=INCOMPLETE
SECURITY_BRANCH_INTEGRATION=HOLD
MAIN_UNCHANGED=true
GOVERNANCE_IDENTITY_DRIFT=OWNER_HOLD
INDEPENDENT_REVIEW_COMPLETE=false
ARTIFACT_PROVENANCE_VERIFIED=false
REQUIRED_GATE_QUALIFIED=false
