"""Supplemental exact Product/Test composition. Never replaces ordinary PR checks."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
from projectide_slice2_isolated import changes, git, sha

CORE_PRODUCT = "ddb456dbf2eca1b005a8aac5ffb1ca8861b73b5d"
CORE_TEST = "5bd53a614b9cfe27de53cfa2b7901a18b95aa36d"
TEST_BASE = "b18939f9104d52ae33e32a7f9b15a6e1b52892f1"

def compose(repository: Path, target: Path, receipt: Path, product: str, test: str) -> dict:
    sha(product); sha(test)
    if target.exists() or receipt.exists():
        raise ValueError("Fresh worktree and receipt paths required")
    git(repository, "merge-base", "--is-ancestor", CORE_PRODUCT, product)
    git(repository, "merge-base", "--is-ancestor", CORE_TEST, test)
    product_changes = changes(repository, CORE_PRODUCT, product, (
        "frontend/src/", "frontend/public/assets/projectide/", "tools/ProjectIde.Preview/", "docs/verification/"))
    web_tests = changes(repository, CORE_TEST, test, (
        "frontend/src/app/features/projectide/", "tests/", ".github/workflows/projectide-web-isolated.yml"))
    test_changes = changes(repository, TEST_BASE, test, (
        "frontend/src/app/features/projectide/", "tests/", ".github/workflows/projectide-slice2-isolated.yml",
        ".github/workflows/projectide-core-isolated.yml", ".github/workflows/projectide-web-isolated.yml"))
    for commit in web_tests:
        paths = git(repository, "diff-tree", "--no-commit-id", "--name-only", "-r", commit).splitlines()
        if any(path.startswith("frontend/") and not path.endswith(".spec.ts") for path in paths):
            raise ValueError("Test candidate changed frontend Product source")
    git(repository, "worktree", "add", "--detach", str(target), product)
    for commit in test_changes:
        git(target, "cherry-pick", commit)
    # The full source delta may contain only independent Web tests.
    paths = git(target, "diff", "--name-only", product, "HEAD", "--", "frontend/src", "src", "tools").splitlines()
    if any(not path.startswith("frontend/src/app/features/projectide/") or not path.endswith(".spec.ts") for path in paths):
        raise ValueError("Composition changed Product runtime source")
    value = dict(schemaVersion=1, coreProduct=CORE_PRODUCT, coreTest=CORE_TEST,
        productHead=product, testHead=test, productTree=git(repository,"rev-parse",product+"^{tree}"),
        testTree=git(repository,"rev-parse",test+"^{tree}"), productChanges=product_changes, testChanges=test_changes,
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
