"""Verify new Core with exact existing Inspector Product/Test; supplemental only."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode = True
from projectide_core_isolated import product_changes
from projectide_slice2_isolated import git, run, sha
from projectide_dependency_continuations import PRODUCT_MERGES, apply_test_changes, product_merge, test_changes as continuation_test_changes

CORE_PRODUCT = '610f9911f9ce31739188437972f7eeb018f611da'
CORE_TEST = '9e969183f5b311789b0197d0b6aac12a88206e7c'
INSPECTOR_PRODUCT = 'a32e0c8daa6429ee54178698153d87110b5c81f7'
INSPECTOR_TEST = 'ed2e0b4019cc1e3b8f6cf178238ddc664772ffe2'


def compose(repository: Path, target: Path, receipt: Path, product: str, test: str,
            review_product_base: str = '585727a9b480e61d08cbeeb3d996904b5f1f4b5e',
            review_test_base: str = '6edddea75a8dfd0ecaca8559827ffb92ba06a365') -> dict:
    sha(product); sha(test)
    sha(review_product_base); sha(review_test_base)
    git(repository, 'merge-base', '--is-ancestor', review_product_base, product)
    git(repository, 'merge-base', '--is-ancestor', review_test_base, test)
    if target.exists() or receipt.exists():
        raise ValueError('Fresh consumer worktree and receipt paths required')
    product_commits = product_changes(repository, CORE_PRODUCT, product)
    test_changes, test_merges = continuation_test_changes(repository, CORE_TEST, test, ('tests/', '.github/workflows/projectide-core-isolated.yml', '.github/workflows/projectide-value-consumers.yml'))
    helpers = receipt.parent / 'composer-source'
    helpers.mkdir(parents=True)
    producer_hashes = {}
    for name in ('projectide_web_isolated.py', 'projectide_web_verify.py', 'projectide_core_isolated.py', 'projectide_slice2_isolated.py'):
        data = subprocess.check_output(['git', 'show', INSPECTOR_TEST + ':tests/ci/' + name], cwd=repository)
        (helpers / name).write_bytes(data)
        producer_hashes[name] = hashlib.sha256(data).hexdigest()
    sys.path.insert(0, str(helpers))
    spec = importlib.util.spec_from_file_location('pinned_inspector_composer', helpers / 'projectide_web_isolated.py')
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    value = module.compose(repository, target, receipt, INSPECTOR_PRODUCT, INSPECTOR_TEST)
    value.update(inspectorProduct=INSPECTOR_PRODUCT, inspectorTest=INSPECTOR_TEST,
                 inspectorCombinedCommit=value['combinedCommit'], inspectorCombinedTree=value['combinedTree'],
                 coreProduct=product, coreProductBase=CORE_PRODUCT, coreTest=test, coreTestBase=CORE_TEST,
                 coreContinuationTestChanges=test_changes, coreTestDependencyMerges=test_merges,
                 coreProductContinuationMerges=[product_merge(repository, commit, ('src/Coglatas.Domain/ProjectIde/', 'src/Coglatas.Application/ProjectIde/', 'docs/verification/'))
                                                for commit in product_commits if commit in PRODUCT_MERGES],
                 coreReviewProductBase=review_product_base, coreReviewTestBase=review_test_base,
                 coreProductTree=git(repository,'rev-parse',product+'^{tree}'), coreTestTree=git(repository,'rev-parse',test+'^{tree}'),
                 pinnedComposerSha256=producer_hashes)
    patch = subprocess.check_output(['git','diff','--binary',CORE_PRODUCT,product,'--','src'],cwd=repository)
    subprocess.run(['git','apply','--index','-'],input=patch,cwd=target,check=True)
    run(['git','-c','user.name=Coglatas isolated verification','-c','user.email=isolated-verification@example.invalid',
         'commit','--date=2026-11-01T00:00:00+00:00','-m','Compose exact Core value-binding runtime delta for Inspector verification'],target)
    apply_test_changes(target, test_changes, test_merges)
    delta = git(target,'diff','--name-only',product,'HEAD','--','src').splitlines()
    if delta != ['src/Coglatas.Domain/ProjectIde/CompilerSemanticInspection.cs']:
        raise ValueError('Consumer Core runtime differs from exact Product plus pinned inspection projection')
    if git(target,'diff',INSPECTOR_PRODUCT,'HEAD','--','frontend','tools','Coglatas.slnx'):
        # Existing independent frontend specs/internal maintenance are already on
        # the exact Inspector composition. Compare to that exact combined tree.
        if git(target,'diff',value['inspectorCombinedCommit'],'HEAD','--','frontend','tools','Coglatas.slnx'):
            raise ValueError('Core composition changed consumer runtime or independent consumer tests')
    for path in git(repository,'diff','--name-only',CORE_TEST,test).splitlines():
        if git(target,'diff',test,'HEAD','--',path):
            raise ValueError('Consumer composition differs from exact new independent Test file: '+path)
    value.update(combinedCommit=git(target,'rev-parse','HEAD'), combinedTree=git(target,'rev-parse','HEAD^{tree}'),
                 status='COMPOSED_NOT_VERIFIED')
    if git(target,'status','--porcelain','--untracked-files=all'):
        raise ValueError('Consumer candidate is not clean')
    receipt.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8')
    return value


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--product-head',required=True); parser.add_argument('--test-head',required=True)
    parser.add_argument('--worktree',type=Path,required=True); parser.add_argument('--receipt',type=Path,required=True)
    parser.add_argument('--review-product-base',default='585727a9b480e61d08cbeeb3d996904b5f1f4b5e')
    parser.add_argument('--review-test-base',default='6edddea75a8dfd0ecaca8559827ffb92ba06a365')
    args=parser.parse_args()
    print(json.dumps(compose(Path.cwd(),args.worktree.resolve(),args.receipt.resolve(),args.product_head,args.test_head,
                             args.review_product_base,args.review_test_base),indent=2))
