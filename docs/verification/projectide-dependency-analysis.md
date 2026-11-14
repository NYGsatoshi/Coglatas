# Root-bound dependency semantic and rule analysis

Existing Product ownership: #905 / #888 / IDE-START-01, with #1032 and both shared Compiler consumers. This connects the transitive binding regions to actual regional semantic validation, derived inspection IR and the existing locked rule families. It does not complete global cross-region graph validation or publish a complete external Compiler IR/PASS.

## Actual compilation

PureCoreCompiler.Compile first performs the existing local compilation. When it retains a resolved foreign reference, complete root binding and successfully read bounded unit/calendar supplies, it builds the reachable binding closure and runs the same semantic checks and rule implementation over every complete region, including the root. Caller-owned supplies are enumerated only once; immutable admitted copies are reused. Missing/denied/partial supplies do not acquire proof by reusing another region.

Known invalid dependency declarations and false attached required predicates affect the actual root rule outcomes and can make the root Compiler FAIL despite partial coverage. Requirement conditions are evaluated; missing eligible evidence and Resource stock/capacity remain explicitly unavailable. Unsupported or omitted root rule families are not implicitly enabled. Advisory failures remain warnings. A Constrains relation whose source belongs to another exact context cannot attach a local predicate merely because the EntityId matches. Cross-region attachment completeness still needs the global graph stage.

CoreCompilation.Dependencies retains a root-bound derived receipt and exact regional diagnostics/rules. Foreign primary locations stay in their own regional receipts. The normal root Result contains explicitly coded dependency-finding summaries at a root document location, with a discriminator linking the regional digest and original diagnostic ID. Known failures carry root/region proof digests; missing proof remains Missing. A summary is not a foreign Source navigation target or current read authorization. The original dependency compile manifest/Scenario overlay intent is not reconstructed from its materialized Source.

## Scoped derived IR

Regional IR uses the development format coglatas.project-ir-region/1 and source maps coglatas.semantic-region-source-map/1. These are derived regional inspection receipts, not formal Artifact Registry adoption or ordinary project-ir/1 publication. Normal external-root artifacts remain absent and the external-closure-not-lowered guard remains.

Endpoint resolution uses a context/entity index. Stable node identity still derives Tenant/Project/Entity and the existing role, while each endpoint explicitly retains its exact context. Two versions of one Project therefore retain stable node IDs in separate regions/maps rather than being flattened or confused. Local node origins retain the actual regional Tenant/Project/document/context. Existing purely local IR bytes and identity golden controls stay unchanged.

Regional IR means that region's supported structural checks completed; it does not prove graph-kind invariants across the entire closure. A reference cycle is not proof that dependency/dataflow/containment cycles are legal. Source-map lookup, cross-region graph proofs, global attachments, consolidated publication and both clients' foreign navigation admission remain required follow-up work.

## Bounds and lifecycle

One root logical budget spans the original compile, closure binding, subsequent regional validation, IR/source-map traversal and rule evaluation. Each region receives only the remaining budget; its already-charged binding cost is not counted twice. Result counters expose the total consumed bound. Source byte limits also constrain regional/aggregate publication. Exhaustion or publication overflow cannot yield an Evaluated complete prefix; cancellation drops the whole new compilation/receipt. Returned collections own their storage. All receipts bind the whole root input, including changes limited to the root Source.

## Product verification

CompilerDependencyAnalysisTests cover actual required false/true/advisory/Unknown predicates; missing attachment/family; false Requirement with missing evidence and Resource supply; unavailable typed units; invalid dependency declarations; exactly scoped IR edges and multiple versions; missing/denied/partial input; shared budgets/publication bounds; changed-root invalidation; single caller enumeration; cached/permuted equality; actual LocalNetCompilerHost and cancellation.

The Preview exporter adds --dependency-analysis, a separate three-case actual Core fixture. Baseline, applied Scenario and converted Draft each have Compiler FAIL from the external required false predicate; the distinct local Tests are FAIL/PASS/PASS. C# replays every original fixture through Core and the Windows pure ViewModel, including Problem selection and clearing. The Angular store and Playwright desktop/mobile en/ja controls use these actual Core bytes through the existing Scenario reader. No JavaScript predicate evaluator or fabricated Compiler result is introduced. Browser route substitution selects this static Core-generated fixture; it proves local consumer behavior, not a backend/runtime API.

The existing 31-case main and two-case Test-edit exports remain byte-identical. The 15-case Scenario export is regenerated because actual Compiler dependency stages/evidence changed; local Test meanings and original input/Source/overlay bytes remain fixed. The new three-case fixture is separate, so the existing example selector and 15-case receipt are preserved.

Typical isolated commands:

- dotnet test /work/harness/DependencyAnalysisPairedProduct.csproj --no-restore (developer composition, not independent/platform acceptance).
- dotnet run --project tools/ProjectIde.Preview -- output.json --dependency-analysis.
- npm test -- --include 'src/app/features/projectide/**/*.spec.ts' with pinned Node/npm.
- Playwright projectide-workbench, local-tests, test-definitions, scenarios and dependency-analysis specs, zero retries, pinned Linux desktop/mobile.

Retain original failed verification records. Fixture authoring errors, stale generated fixture comparisons and an incorrectly nested isolated frontend build are not positive qualification. Product tests do not replace independent Security work in existing P0 #1951, native Windows/Avalonia or hosted PR-required checks.

Broader #905/#1032, actual Avalonia, current authority, durable jobs, Simulation/quantitative Impact and shared Resource adapters remain open. Parent PR #1946 remains dependent on P0 #1949's cancelled ReSharper scope verification. No Security branch/global workflow/protected branch/Main edit or merge is performed; all RLS/Trust/Eligibility/Native/Production/Release HOLDs remain.
