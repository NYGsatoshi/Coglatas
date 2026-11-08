# PR-K: independent architecture contract advisory (not a Required Check)

Issue #828 requests independent Architecture CI. The existing build-test normally
runs Coglatas.Architecture.Tests during full backend scope, but the scoped backend
path can omit the Architecture project. This candidate adds a separate, read-only,
GitHub-hosted and *non-required* PR workflow to the SecurityTestImplimation base
only. It does not alter the six registered required checks, their routing, the
existing build-test, the product, secrets, or Main. It introduces an additional
advisory runner only when relevant source, architecture tests, or this workflow
change. Runner time and cost remain unmeasured; no speedup is claimed.

The original LayerDependencyTests (six layer directions) and the six existing
ProjectIdeBoundaryTests remain unchanged. One additional Fact uses ArchUnitNET
against test-only synthetic references to prove all six forbidden dependency
directions are rejected by the dependency oracle. It does not change production
architecture or prove all future product modules have appropriate constraints.

The advisory runs dotnet restore/test on a fresh GitHub-hosted Ubuntu job.
scripts/ci/architecture_advisory.py requires an exact thirteen-case native TRX
inventory, each Passed on its single result, consistent non-skipped counters,
source/run/attempt metadata and fixed nonsecret receipt fields. Unexpected or
duplicate cases fail pending a reviewed inventory update. Five focused Python
methods test successful interpretation and negative malformed, missing,
duplicate, unexpected, failed, DTD, scope and counter cases.

The resulting sanitized receipt is uploaded from the runner's temporary
directory under a unique run/attempt artifact name. It records hashed
case identities without raw test titles. It is explicitly
EXTERNAL_VERIFICATION_REQUIRED, not authenticated D10 producer evidence,
not a release/PR-N final attestation and not a seventh Required Check.
The workflow is independent from Qodana, CodeQL, Playwright and PostgreSQL.

Existing Required Check failures remain separate. If the advisory cannot run or
fails, report it as non-required failure, not as a green architecture qualification.
Do not use its metadata to waive original Test or release obligations.

Before any future promotion, require stable exact-current-head native runs,
source-defined test coverage, independently verified artifact provenance,
independent review, and explicit Owner approval for changes to required-check
topology. Governance identity drift (registered ruleset 22302146 disabled and
an alternative active ruleset) remains OWNER_HOLD. Integration into
SecurityTestImplimation itself remains HOLD until applicable governance and
review conditions are satisfied. Main must not be changed.

Rollback: revert this advisory workflow, its negative fixture, verifier/tests,
and documentation in a Security-targeted PR; preserve prior failures, run IDs
and any produced artifacts. No live governance settings are modified.
