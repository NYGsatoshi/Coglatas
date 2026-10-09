# SEC-ARCH registry quality repair

Exact Main `88733966c44ee86eba823ebc9d6508b2f4c3088e` failed the full
Qodana Cloud rule-debt ratchet in
[run 38000589444, attempt 1](https://github.com/NYGsatoshi/Coglatas/actions/runs/38000589444/attempts/1).
The original SARIF artifact `11649768543` remains evidence; its extracted JSON
SHA256 is `9175db506ef56e4faf2c415e87d5be836177421110d87975fdda3572a09e2301`.
The four increased rules were `NotAccessedPositionalProperty.Global` (12 over
budget), `UnusedMember.Global` (5), `MethodSupportsCancellation` (2), and
`UseAwaitUsing` (1). Baselines, inspection configuration and Required Checks are
unchanged.

The repair explicitly projects the existing public registry validation output
and coverage counts instead of reflecting any future private DTO fields.
Candidate/schema/registry identities, family/severity/class counts, lifecycle,
mapping, execution, unresolved and limitation counts retain their existing JSON
names and values. HTTP body writes use the existing timeout token, response and
buffer streams are disposed asynchronously, and killed Git processes are reaped
with an explicitly uncancelled cleanup token.

The existing focused tests now verify every coverage field through actual pinned
Git CLI traceability, all nine closed verifier-class wire names and the optional
contract-artifact scoped review binding. Synthetic authority responses remain
synthetic; these controls do not approve a canonical mapping or establish product
runtime security coverage.

Local development verification on .NET SDK 10.0.401 passed 148 tests, with zero
failures or skips. TRX SHA256:
`acc87c07ee207ab73c734a6c27d99aa4a1dffae2e88665ea0f005eee9b881c87`.
The unrelated existing unread `AuditPackageExportService.clock` compiler warning
remains visible. This local source revision was uncommitted; exact-head hosted
ReSharper/Qodana and subsequent Main qualification are still required. The
original failed Main run is retained. **PRE-AVALONIA SEC-ARCH: BLOCKED.**
