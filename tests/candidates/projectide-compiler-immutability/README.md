# Independent Compiler immutability qualification (P0 #1938)

This SecurityTestImplimation-owned candidate independently probes the public
collection surfaces of the **already integrated** ProjectIDE Compiler. It does
not rewrite Product implementation or import the Product's own immutability
tests as an independent reviewer. The nine xUnit facts are separately authored.

## Fixed Product and source boundaries

- Exact Product commit: `3613c648f118cafcb1936fdf5a081aa251c6b4bf`
  (`projectide/integration-20261031`, after Product fixes #1939-#1942 and
  local Test CLI #1946).
- Independent Test source: `SecurityCompilerImmutableBoundaryCandidateTests.cs`
  in this Security PR. The workflow binds the exact source Git blob
  `067043361a2e7a83613886234e1afc0b499d70e9`.
- Composition runner: `compose_and_verify.py`, bound by its exact Git blob
  `5d476f82d0fda88bfa60d0af45f640715b0cacd4`.
- Committed admission regression controls: `test_compose_and_verify.py`,
  Git blob `d52e587c524c9d2d37eba988508417424c79f6ca`, verified and executed on both hosts before the native suite.
- Only the original Test candidate is copied into a clean, detached Product
  worktree. Its actual compiler-input bytes, staged blob, Product HEAD and
  unrelated tracked Product changes are checked initially, again after restore
  before compilation, after native execution and after discovery. Neither repository's
  branch ref is rewritten by the runner. Fail-closed identity checks distinguish
  Product, independent Test, and composed source tree.
- The Product SDK is pinned by its own `global.json` to .NET 10.0.403 with
  roll-forward disabled. Two separate read-only GitHub-hosted jobs use literal
  `ubuntu-24.04` and `windows-latest` runner labels.

## Nine actual compiled xUnit controls

1. Locked manifest Rules and same-input real compilation survive caller-owned
   rule JSON changes and synchronization-root access.
2. Evidence premise digests cannot be rewritten by a caller array or legacy
   synchronization-storage handle.
3. RuleOutcome replacement cannot turn retained PASS into a reconstructed FAIL
   without changing its immutable evidence/result digest.
4. Actual nonempty diagnostics and artifact views do not expose mutable backing.
5. Real Core compilation protects stage lists and derived IR nodes.
6. Current/stale/undetermined freshness explanation lists, including empty views,
   preserve their values and state.
7. Real fixed host profile contracts/capabilities retain compatibility.
8. Windows pure ViewModel accepts a real compiled result and preserves it after
   collection probes (also run headlessly on Ubuntu for parity).
9. Empty NotRun and Missing-evidence collections retain immutable semantics.

The explicit runner requires the **nine frozen fully qualified fact identities**
in native results and compiled discovery. Each result binds to a unique native
test/execution pair, matching definition, exact class and bare method, and entry.
Duplicate summaries, counter sets or inventory containers, unsupported result
records, non-Passed outcomes, nonzero failure counters and foreign discovery
entries fail closed. Discovery removes only the four-space CLI presentation
prefix; case, namespace and method whitespace are preserved. Product and
combined tree SHAs and original retained-file hashes remain recorded. Ordinary
Security required CI does **not** discover these candidate tests automatically.

### Admission regressions and source verification

The earlier runner accepted substituted nine-method inventories, extra foreign
discovery entries, missing method metadata and a second failed summary. It also
checked only the staged source blob after execution, allowing changed working
compiler-input bytes to retain an earlier source hash. Controlled original-runner
probes reproduced these acceptance defects; they do not invalidate previously
independently audited native artifacts.

The committed Python controls retain a valid original-roster positive and reject
missing, duplicate, substituted, inconsistent or failed native inventories,
unsupported XML children, foreign discovery and raw-name whitespace. Actual
`main()` controls use temporary real Git repositories, indexes and worktrees,
with only the dotnet process controlled. They verify failure receipts for bad
inventories and actual source, index, HEAD and tracked Product drift, including
refusal after restore before compilation. The controls are not native C# results.
All original nine C# fact bodies and helpers, the Product pin and the separate
historical RED source/runner/workflow remain unchanged.

Artifacts retain source identities/hashes and pre/post verification receipts;
the raw C# file is obtained independently from its pinned Git blob. Each host's
ZIP and retained-file digests must be verified separately. Compare unchanged raw
`(testId, testName)` sets and source/composed identities across hosts; per-run
execution IDs and whole artifact bytes need not be equal. These checks detect
observed source drift and do not claim continuous execution attestation.

### Qualification boundaries

Passing this advisory is **synthetic headless Core and pure ViewModel
qualification only**. It is not actual Avalonia/Native UI, current authority
or Tenant authorization, remote Compiler, production API, Trust/Eligibility,
Artifact provenance, runtime activation, or Release. No CI required context,
protected ruleset, permissions, Product source, or Main
branch is altered. The workflow and source/compiled-results receipts explicitly
set all relevant admission flags to `false` independent of the test outcome.

The three original `ICollection.SyncRoot` counterexamples were **independently
replayed** at the exact vulnerable Product commit
`2b2c86a447779c2d9d19e4b88f1274a70737d54e`, using the unchanged
candidate Test blob. The separate fixed-SHA historical workflow
`.github/workflows/projectide-compiler-immutability-historical-red.yml`
uses `replay_vulnerable.py` (Git blob
`acd00c742086151dfcaf54bc25765cc9cfb25aa2`) to require precisely three
compiled TRX failures, zero PASS/skip, and the *specific* original legacy
synchronization-root invariant. Successful compilation without those exact
failures, missing TRX, unrelated exceptions, or source drift fail closed.
The first source-admission attempt failed because this archived commit was
unadvertised; the corrected workflow fetches only its immutable SHA.

Native [historical RED run 43600806819](https://github.com/NYGsatoshi/Coglatas/actions/runs/43600806819)
qualified all three expected failing facts on Ubuntu, with source/receipt
ZIP artifact `14115629643` (SHA-256
`e59f61b5874b8520b3ea28e65f2819e94219c2a8ea43f2150be371287b59593b`),
and original failed-test TRX identities. Separately, the corrected Product
qualified all **nine passing facts on Windows and Ubuntu** in
[run 43600806818](https://github.com/NYGsatoshi/Coglatas/actions/runs/43600806818).
Those native results belong to the exact referenced heads and source Git
blobs; any new PR HEAD must itself pass fresh required checks and native
advisories. Read-only source/result tests do not amount to independent human
review or all of #1938's runtime/authorization/Native acceptance criteria.
Keep #1938 open until every remaining governance exit genuinely holds.

### Reproduction

On either host, with a committed Security candidate and the immutable Product
commit available locally in the same Git object database:

```text
python -B tests/candidates/projectide-compiler-immutability/compose_and_verify.py --product-head 3613c648f118cafcb1936fdf5a081aa251c6b4bf --worktree <fresh-product-worktree> --evidence <fresh-evidence-directory>
```

Previous old-source CI, Product-authored tests and CodeRabbit comments cannot
replace current-head native execution or source-authorization/governance
clearance. #1144 stays BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, Release NO_GO.
