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

The original historical three `ICollection.SyncRoot` counterexamples at
the vulnerable Product SHA `2b2c86a447779c2d9d19e4b88f1274a70737d54e`
are recorded in #1938 but are **not yet independently replayed in this PR**.
They must not be marked as executed until separate exact-source negative
controls and original failure evidence are recorded. A successful corrected
Product run alone is not independent human review or all of #1938's exit
criteria. Keep #1938 open until those additional conditions hold.

### Reproduction

On either host, with a committed Security candidate and the immutable Product
commit available locally in the same Git object database:

```text
python -B tests/candidates/projectide-compiler-immutability/compose_and_verify.py --product-head 3613c648f118cafcb1936fdf5a081aa251c6b4bf --worktree <fresh-product-worktree> --evidence <fresh-evidence-directory>
```

Previous old-source CI, Product-authored tests and CodeRabbit comments cannot
replace current-head native execution or source-authorization/governance
clearance. #1144 stays BLOCKED, #1128 SUSPENDED/NOT_EVALUATED, Release NO_GO.
