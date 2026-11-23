# ProjectIDE Core Slice 2a: exact values and time

Status: implemented pure contracts; review and hosted acceptance pending. This is the first small part of #905 Slice 2, not completion of Slice 2 or either client.

## Authority and isolation

Follow #888/#798's 2026-10-31 owner receipt, #905 Core contract v1 and A-R2. Specification PR NYGsatoshi/Coglatas-Spec#300 is Draft/unmerged; the recorded directions are approved separately. Retain merged Slice 1/#1063 and Security Foundation/#1122.

Remote audit on 2026-10-31 found `main`, `projectide/integration-20261031` and the unused `feature/projectide-core-slice2-values-20261031` at `158ab153551184cede99c85ce6476dd6e87b6767`. Main/integration diff was empty. `SecurityTestImplimation` was `b18939f9104d52ae33e32a7f9b15a6e1b52892f1`. Existing open PRs and worktrees were preserved. The supplied RiderProjects path was absent on this host; the actual existing repository is under `source/repos/NYGsatoshi/Coglatas`.

Reuse the existing feature branch, targeting only the temporary integration branch. Tests are on `test/projectide-core-slice2-values-20261031`, based on SecurityTestImplimation. Validation applies only that branch's new test commits to a detached product checkout; it never merges the security/test integration branch into product. Publish exact final product/test/tested-tree identities in the PR evidence, outside a self-referential source commit.

The current active Main ruleset is 24643016, with six required contexts, resolved review threads, no bypass, no force push/deletion and zero required approving reviews. The historical strict external-review ruleset is disabled. This observation grants no merge permission. The ordinary branch-protection endpoint was unavailable to the connector (403). PR CI is unfiltered by target branch; other workflows have their own filters and must be inspected individually.

## Implemented

- `ExactNumbers.cs`: arbitrary precision finite decimal input, canonical decimal strings, reduced rational arithmetic and exact comparisons. Explicit nearest/ties-to-even quantization retains the original rational, target scale and rounding mode. Division by zero and bounds are errors. Defaults of 4,096 digits/scale are provisional processing bounds, not capacity/support guarantees or #1128 assurance.
- `TypedProjectValues.cs`: immutable Source-backed type/value decoding; Known, Null and Unknown remain separate from missing fields. Nullability and Unknown acceptance are explicit flags. Unsupported kinds, versions, enum tokens and unknown members are retained without defaulting or executing them.
- `ProjectUnits.cs`: exact decimal quantity, declared unit identity/dimension and checked dimension vectors. Dimension mismatch is invalid; same-dimension unit conversion is Unsupported without an explicit versioned conversion contract. Registry binding is not claimed.
- `ProjectTimeValues.cs`: checked signed 100 ns elapsed ticks, distinct working duration with pinned calendar reference, civil calendar periods, Gregorian dates without midnight conversion, exact seven-digit UTC instants and half-open instant intervals. No OS timezone/calendar lookup or clock access.

The existing Source codec remains unchanged. A structurally decoded Source or value is not Compiler success, executable IR, reference resolution, authorization or Merge evidence. Value readers retain their original canonical SourceJson at every boundary. Unknown JSON values remain lossless through the existing exact codec. Malformed raw JSON/Draft recovery remains owned by the existing Source codec.

## Verification

At the initial code candidate, SDK 10.0.403 Release Domain build passed with zero warnings/errors. Detached product plus test commit `a35e8170` ran all ProjectIde tests: 362 passed, zero failures/skips. This includes 82 new cases (33 exact-number, 21 time, 28 typed-value) and existing Source/security regressions. Architecture and final exact-tree evidence are recorded in the PR after execution.

CodeRabbit CLI installation was attempted through the documented installer on this Windows host. It failed with `Unsupported operating system: mingw64_nt-10.0-26200`; no review ran. A supported Linux/WSL/macOS CLI environment or the repository's PR review service is needed. No manual review is represented as CodeRabbit evidence.

No API, EF/RLS/migration, UI, dependency/lockfile, CI or protection configuration changes. Real PostgreSQL, browser/native UI, IME/focus and accessibility acceptance are not established by these pure tests. Hosted workflows must be assessed on their own exact candidate; routed-out/skipped work is not runtime PASS. Local evidence is separate from required hosted acceptance.

## Remaining work and paired clients

Next Slice 2 work: typed declarative views for all seven entity kinds, ports, reference/collection types, bounded expressions, resource semantics, and Source integration fixtures. Later slices own relation binding, immutable Compiler manifests, pipeline, IR and actual analysis. Zoned civil-time resolution, calendar execution and versioned unit conversion are explicitly unsupported here; no fallback conversion is supplied.

Angular Web remains the active independent renderer (Angular 22.2.x, existing Vitest/Playwright). This PR adds no Web acceptance. Windows/Avalonia uses the same contracts but has no new implementation or native-UI acceptance. PRE-AVALONIA SEC-ARCH remains BLOCKED under #1144 until its existing exact mapping, all-adapter/RLS evidence and separate concrete approvals qualify. Independent pure Core/Web contracts can continue. No common feature is DONE while a required client is blocked.

Main and Student Council product behavior are unchanged by this unmerged candidate. Security Evaluation remains Disabled/Shadow; #1128 remains SUSPENDED/NOT_EVALUATED. No production activation, operational data, external side effects or new security approval is involved.
