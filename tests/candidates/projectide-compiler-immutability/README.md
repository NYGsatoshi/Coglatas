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
  `355323433b114fee2efe7b0c0c5a586bfea83b22`.
- Only the original Test candidate is copied into a clean, detached Product
  worktree, after byte-level Git index/blob verification. Neither repository's
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

The explicit runner verifies precisely **9 passing distinct TRX identities**,
actual compiled discovery, source identity before and after execution, Product
and combined tree SHAs, and hashes of original retained logs/TRX. Ordinary
Security required CI does **not** discover these candidate tests automatically.

### Qualification boundaries

Passing this advisory is **synthetic headless Core and pure ViewModel
qualification only**. It is not actual Avalonia/Native UI, current authority
or Tenant authorization, remote Compiler, production API, Trust/Eligibility,
Artifact provenance, runtime activation, or Release. No CI required context,
protected ruleset, permissions, existing workflow, Product source, or Main
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
