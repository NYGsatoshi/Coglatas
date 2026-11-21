# ProjectIDE Core Slice 2b: typed declarations and bounded predicates

Status: pure contract implementation, incomplete Slice 2; paired-client acceptance and review are outstanding. Stacked on the value contracts in PR #1603; final target remains `projectide/integration-20261031`, never Main. No dependency PR is merged automatically.

## Source ownership and behavior

`PlanDeclarationReader` projects the existing immutable Source AST into seven typed views: Project, Task, Milestone, Requirement, ResourceBinding, Constraint and Workflow. Each view retains the exact SourceJson instance, EntityId and owning DocumentId. There is no independently writable Task list, dependency list, Milestone membership, operational progress or shared resource stock. Unknown kind/version/member/enum/operator data remains opaque Unsupported; invalid data is retained in its result. Reading a declaration is not a Compiler result or execution permission.

Task work requires nonnegative elapsed/working ticks or explicit Unknown. Milestones cannot carry work duration. Requirements carry pinned target context, condition and evidence-kind contracts; evaluating a condition cannot establish eligible evidence/satisfaction. Resource bindings carry Tenant-owned ResourceId plus exact snapshot version/digest, with reusable Human/Equipment/Facility distinct from consumable Material. Both declaration entries and document roots reject Resource Tenant mismatch. Shared resource loading, stock/capacity trajectories and authorization remain later work.

Ports have distinct typed identity, direction, explicit connection cardinality, type/nullable/Unknown rules and a same-type input default. Scalar input supports at most one connection; collection/merge semantics remain Unsupported. Output defaults, contradictory cardinalities, mismatched defaults and duplicate IDs are rejected. Slice 3 still owns actual relation/port binding and graph cardinality/cycle checks.

Pure predicates implement Known/Unknown Boolean literals, NOT, bounded AND/OR and deterministic three-valued short circuit evaluation. Returned premises identify only actually evaluated literals, not full Compiler proof/coverage. Unknown is never zero/false/Null. Initial limits (1,024 nodes, depth 32, operator fanout 32) are provisional local processing bounds. Unsupported operators cannot read clocks, files, network, secrets or execute scripts. Workflow states/transitions/guards require valid typed IDs/endpoints and a positive transition bound; loops are not misclassified as dependency cycles. No Workflow runtime or external actions are implemented.

## Verification and separation

Tests remain on the short branch based on SecurityTestImplimation, in PR #1604. Only its new test commits are cherry-picked into a detached product checkout. The complete security integration branch is not merged into product; no tests are added to either product PR.

Initial combined tests found four incorrect reflection-exception expectations. The test now checks TargetInvocationException and its ArgumentException cause. Corrected ProjectIde verification passed **406/406**, zero skips (126 new cases across both sub-slices). Additional cases cover all seven concepts, missing fields, Unknown work, unsupported nested data, Resource Tenant scope, reference contexts, port defaults/cardinality, bounded Workflow loops and all Boolean truth-table combinations. Exact final product/test/tree identities, architecture results and hosted check URLs are published in PR evidence after final execution.

PR #1603's existing hosted CI runs against the integration target. PR #1604's standalone CI correctly fails compilation because the separate product contracts are absent from its base. This is a dependency blocker, not waived acceptance; do not suppress the tests or import product changes to manufacture green. The detached combined result is a different, explicitly identified tree. CLI CodeRabbit is unavailable on native Windows; repository PR review was requested. The test PR's service currently reports its free OSS review limit reached. No paid capacity or protection change is authorized.

## Remaining scope and gates

Slice 2 is partial: complete approved reference/artifact/approval/collection/structural types, versioned unit conversion, pinned civil-time/calendar resolution and richer bounded typed expression/resource contracts before declaring full Slice 2 acceptance. Unsupported capabilities stay explicit. Slice 3/CompilerInput/pipeline/IR/analysis/host parity remain unimplemented.

Angular Web: independent renderer retained, no new UI implementation/real-browser acceptance in this Core PR. Windows/Avalonia: same contracts, implementation/native UI remains BLOCKED by #1144's pre-Avalonia SEC-ARCH/RLS prerequisites. No common feature is DONE. Existing API/persistence/authorization/Realtime/Files behavior, packages, workflows and protections are unchanged. Main/Student Council release authority is untouched; Security Evaluation stays Disabled/Shadow and #1128 remains SUSPENDED/NOT_EVALUATED. No security mapping, RLS SQL/GRANT/activation or production approval is inferred.
