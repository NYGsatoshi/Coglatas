"""Supplemental exact Product/Test composition. Never replaces ordinary PR checks."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
from projectide_slice2_isolated import changes as independent_changes, git, sha

CORE_PRODUCT = "1218495bdd4e1280c968b0e6d2067c921c64ab31"
CORE_TEST = "8348083cc04e9bd85147b32bb1805e94aa14fd3a"
TEST_BASE = "b18939f9104d52ae33e32a7f9b15a6e1b52892f1"
INVENTORY_TESTS = (
    "frontend/scripts/av-mig-inventory.node-test.mjs",
    "frontend/scripts/av-mig-source.node-test.mjs",
)
INTERNAL_INVENTORY = INVENTORY_TESTS[0]

def verify_internal_inventory(repository: Path, product: str, path: str = INTERNAL_INVENTORY) -> None:
    """Permit only existing internal assertions following the newly registered route."""
    baseline = git(repository, "show", CORE_PRODUCT + ":" + path)
    expected = baseline
    inventory_changes = (
        ("explicit five-route addendum", "explicit six-route addendum"),
        ("'/home', '/teams'", "'/home', '/projectide/preview', '/teams'"),
        ("SourceInventory.verify([inventory, legacyFreeze, targetMap, currentWeb]), 43)",
         "SourceInventory.verify([inventory, legacyFreeze, targetMap, currentWeb]), 44)"),
    )
    source_link_changes = (
        ("alias = resolve(root, 'alias.ts')", "alias = resolve(root, 'alias')"),
        ("symlinkSync(outsideFile, alias);",
         "// A directory junction exercises realpath containment on Windows without\n"
         "    // requiring elevation; Node creates a directory symlink on Unix.\n"
         "    symlinkSync(outside, alias, 'junction');"),
        ("for (const ownerPath of ['../other/external.ts', 'alias.ts'])",
         "for (const ownerPath of ['../other/external.ts', 'alias/external.ts'])"),
        ("for (const routeDefinition of ['../other/external.ts', 'alias.ts'])",
         "for (const routeDefinition of ['../other/external.ts', 'alias/external.ts'])"),
    )
    if path not in INVENTORY_TESTS:
        raise ValueError("Unknown internal maintenance path")
    for before, after in (inventory_changes if path == INTERNAL_INVENTORY else source_link_changes):
        if expected.count(before) != 1:
            raise ValueError("Pinned internal inventory baseline differs from the approved maintenance profile")
        expected = expected.replace(before, after)
    if git(repository, "show", product + ":" + path) != expected:
        raise ValueError("Only exact internal inventory maintenance is permitted on Product")

def frontend_test(path: str) -> bool:
    return path.endswith(".spec.ts") or path in INVENTORY_TESTS

def changes(repository: Path, base: str, head: str, allowed: tuple[str, ...],
            dependency: str | None = None, merges: list[str] | None = None) -> list[str]:
    if dependency is None:
        return independent_changes(repository, base, head, allowed)
    git(repository, "merge-base", "--is-ancestor", base, head)
    commits = git(repository, "rev-list", "--first-parent", "--reverse", f"{base}..{head}").splitlines()
    result = []
    for commit in commits:
        parents = git(repository, "rev-list", "--parents", "-n", "1", commit).split()[1:]
        if len(parents) == 2:
            if parents[1] != dependency:
                raise ValueError("Only the exact pinned Core dependency may be merged")
            expected = git(repository, "merge-tree", "--write-tree", *parents).splitlines()[0]
            if git(repository, "rev-parse", commit + "^{tree}") != expected:
                raise ValueError("Dependency merge contains additional resolution changes")
            if merges is not None:
                merges.append(commit)
            continue
        if len(parents) != 1:
            raise ValueError("Only independent single-parent changes or the pinned dependency merge are allowed")
        paths = git(repository, "diff-tree", "--no-commit-id", "--name-only", "-r", commit).splitlines()
        if not paths or any(not path.startswith(allowed) for path in paths):
            raise ValueError("A change commit crosses its Product/Test ownership boundary")
        result.append(commit)
    return result

def compose(repository: Path, target: Path, receipt: Path, product: str, test: str) -> dict:
    sha(product); sha(test)
    if target.exists() or receipt.exists():
        raise ValueError("Fresh worktree and receipt paths required")
    git(repository, "merge-base", "--is-ancestor", CORE_PRODUCT, product)
    git(repository, "merge-base", "--is-ancestor", CORE_TEST, test)
    product_merges: list[str] = []
    test_merges: list[str] = []
    internal_maintenance: set[str] = set()
    product_changes = changes(repository, CORE_PRODUCT, product, (
        "frontend/src/", "frontend/public/assets/projectide/", "frontend/scripts/check-av-mig-source.mjs", *INVENTORY_TESTS,
        "tools/ProjectIde.Preview/", "docs/verification/", "Coglatas.slnx"), CORE_PRODUCT, product_merges)
    for commit in product_changes:
        for path in git(repository,"diff-tree","--no-commit-id","--name-only","-r",commit).splitlines():
            if path in INVENTORY_TESTS:
                verify_internal_inventory(repository, product, path)
                internal_maintenance.add(path)
            elif frontend_test(path):
                raise ValueError("Product candidate contains independent Test source")
    web_tests = changes(repository, CORE_TEST, test, (
        "frontend/src/app/features/projectide/", *INVENTORY_TESTS, "tests/", ".github/workflows/projectide-web-isolated.yml"), CORE_TEST, test_merges)
    core_tests = changes(repository, TEST_BASE, CORE_TEST, (
        "tests/", ".github/workflows/projectide-slice2-isolated.yml", ".github/workflows/projectide-core-isolated.yml"))
    test_changes = core_tests + web_tests
    for commit in web_tests:
        paths = git(repository, "diff-tree", "--no-commit-id", "--name-only", "-r", commit).splitlines()
        if any(path.startswith("frontend/") and not frontend_test(path) for path in paths):
            raise ValueError("Test candidate changed frontend Product source")
    git(repository, "worktree", "add", "--detach", str(target), product)
    for commit in test_changes:
        git(target, "cherry-pick", commit)
    # The full source delta may contain only independent Web tests.
    paths = git(target, "diff", "--name-only", product, "HEAD", "--", "frontend/src", "frontend/scripts", "src", "tools").splitlines()
    if any(path not in INVENTORY_TESTS and (not path.startswith("frontend/src/app/features/projectide/") or not path.endswith(".spec.ts")) for path in paths):
        raise ValueError("Composition changed Product runtime source")
    if git(target, "diff", CORE_PRODUCT, product, "--", "src"):
        raise ValueError("Web Product differs from the exact pinned Core runtime source")
    # Independent Test and Product have different pre-existing non-ProjectIDE
    # baselines. Compare exact owned files, rather than absorbing unrelated tests.
    if git(target, "diff", test, "HEAD", "--", "tests/Coglatas.Tests/ProjectIde",
           "tests/ci/projectide*", "tests/ci/test_projectide*", "tests/ui/projectide-workbench.spec.ts",
           ".github/workflows/projectide-*.yml", *INVENTORY_TESTS,
           "frontend/src/app/features/projectide/*.spec.ts"):
        raise ValueError("Composition differs from the exact Test candidate source")
    value = dict(schemaVersion=1, coreProduct=CORE_PRODUCT, coreTest=CORE_TEST,
        productHead=product, testHead=test, productTree=git(repository,"rev-parse",product+"^{tree}"),
        testTree=git(repository,"rev-parse",test+"^{tree}"), productChanges=product_changes, testChanges=test_changes,
        productDependencyMerges=product_merges, testDependencyMerges=test_merges,
        coreTestChanges=core_tests, webTestChanges=web_tests,
        internalMaintenancePaths=sorted(internal_maintenance),
        inheritedTestBaselineDifferences=git(repository, "diff", "--name-only", TEST_BASE, CORE_PRODUCT,
                                             "--", "tests").splitlines(),
        combinedCommit=git(target,"rev-parse","HEAD"), combinedTree=git(target,"rev-parse","HEAD^{tree}"),
        status="COMPOSED_NOT_VERIFIED")
    receipt.parent.mkdir(parents=True, exist_ok=True)
    receipt.write_text(json.dumps(value, indent=2)+"\n",encoding="utf-8")
    return value

if __name__ == "__main__":
    parser=argparse.ArgumentParser()
    parser.add_argument("--product-head",required=True); parser.add_argument("--test-head",required=True)
    parser.add_argument("--worktree",type=Path,required=True);parser.add_argument("--receipt",type=Path,required=True)
    args=parser.parse_args()
    print(json.dumps(compose(Path.cwd(),args.worktree.resolve(),args.receipt.resolve(),args.product_head,args.test_head),indent=2))
