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

The advisory runs dotnet restore/test on a pinned Ubuntu 24.04 GitHub-hosted job.
The composed trial also runs eight standalone Architecture source-roster Python
controls and the fail-closed `WITH_NEGATIVE_ORACLE` source inventory **before**
native compilation. Missing or extra C# source files, unsupported literal Fact
syntax, duplicate source case names, symlinked source leaves, and file-identity
swaps reject the advisory job. An environment without symlink capability fails
that guard test rather than silently crediting a skipped result. These checks
produce bounded, hashed *source-text* claims only. They do not compile C#,
confirm native SDK TestCase IDs, bind source to independently authenticated
producer artifacts, or prove equality between source and native case identities.
A successful source roster does **not** substitute for the unchanged 13-case
native dotnet test and exact TRX verifier; both must succeed separately.

The bounded TRX parser accepts UTF-8 with or without exactly one UTF-8 BOM,
rejects UTF-16 and embedded NULs, and explicitly rejects DTD declarations.
scripts/ci/architecture_advisory.py requires an exact thirteen-case native TRX
inventory, each Passed on its single result, consistent non-skipped counters,
source/run/attempt metadata and fixed nonsecret receipt fields. Unexpected or
duplicate cases fail pending a reviewed inventory update. Nine focused Python
methods test successful interpretation, malformed/invalid inputs, missing,
duplicate, unexpected, failed, DTD, scope and counter cases, plus bounded
regular-file reads, symlink rejection, and file replacement between check/open.

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
