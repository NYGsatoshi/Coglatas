# Core Slice 8: host contracts and parity

This bounded Slice 8 adds pure Application-layer adapters over the same immutable
CompilerInputManifest and full-reference PureCoreCompiler used by Slice 7. It does
not integrate a UI, HTTP endpoint, operational job, database or reservation service.
The Product branch depends on Slice 7; independent Tests remain in the existing
SecurityTestImplimation-derived chain.

CompilerInputTransfer clones canonical Source and manifest buffers, caps their
initial byte profile, and reconstructs and verifies the exact input digest and
identity. CompilerHostRequest freezes materialized snapshot/unit/calendar lists.
A host-owned CompilerHostProfile checks exact compiler/schema/ruleset/semantics/IR/
evaluator identities and required capabilities before compilation or transport.
The profile is a trusted composition seam, not runtime binary attestation or an
access grant. Production authentication and approved engine distribution remain
separate gates. Protocol version 1 is the only supported initial contract.

LocalNetCompilerHost schedules headless .NET compilation away from the caller UI
context. ServerCompilerHost uses an injected IServerCompilerTransport; tests use a
Fake backed by the same Core. It rejects substituted input manifests, digests,
generations, result IDs/digests, status and produced artifact bindings. Invocation
attempt IDs remain outside deterministic semantic identities. Error details are
normalized to safe reason codes. Host completion can carry Partial, Unknown, Fail,
Unsupported or ResourceExhausted compiler results and never grants Product merge.

Both adapters propagate cancellation. Completed remote responses arriving after
cancellation are discarded. Freshness assessment is an immutable comparison with
an explicitly supplied desired manifest and client generation; unavailable desired
context is Undetermined. It neither reads ambient branch heads nor proves current
authorization. No production server/client acceptance is inferred from Fake parity.

Host-independent JSON fixtures cover Pass, known Fail, Unknown, Partial/Fail,
budget exhaustion and opaque Source. Golden input/result/IR/source-map digests and
v8 IDs were independently calculated with Python SHA-256 over the emitted canonical
payloads and formal derivation tuples. Repeated Local and Fake Server results must
match the same golden values and diagnostic identities. Tests also cover transfer
mutation, identity substitution, version/capability mismatch, cancellation and safe
failure for unsupported schemas and transport exceptions. Slice 5/6 regression
fixtures retain explicit Unsupported for unbound typed value references and unknown
nominal reference types, and require units for nested quantity types even when no
values are present. They cannot become complete host results by matching shape.

Fixed Product/Test/combined commits and trees, exact discovery/TRX inventories,
Release build, Windows/Ubuntu parity and GitHub run URLs are recorded on paired
Draft PRs and #905/#798. Supplemental Test-head-controlled CI remains reviewable
candidate evidence, not an independently trusted Required Check replacement.
Normal independent Test PR CI dependency failures and all review/approval gaps
remain visible. No old receipt proves a changed Product or Test HEAD.

Limitations inherited from earlier slices remain explicit: no complete value-
reference binding, arbitrary DSL/package/extension execution, executable composite
lowering, external closure, zoned-time execution, actual Requirement evidence or
resource capacity materialization, Simulation, incremental reuse, or Review/Merge
policy. Unsupported and incomplete contracts cannot produce Complete Pass.

No protected branch push/merge, Required Check relaxation, new dependency,
production/RLS/SEC-ARCH/Shadow/Enforce or Student Council release decision change.
Angular retained; Web UI NOT_STARTED and Windows/Avalonia BLOCKED by #1144. Headless
Windows .NET tests are not native UI acceptance. #1128 remains SUSPENDED /
NOT_EVALUATED. CODEOWNER/review/integration/release acceptance remains pending.
