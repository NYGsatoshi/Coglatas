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
from projectide_slice2_isolated import changes, git, run, sha

CORE_PRODUCT = '610f9911f9ce31739188437972f7eeb018f611da'
CORE_TEST = '9e969183f5b311789b0197d0b6aac12a88206e7c'
INSPECTOR_PRODUCT = '2c1336dd4a45af82d6eb7bfe1a7a0720c580e00d'
INSPECTOR_TEST = '6fdf09436452cd7d91fe3d04df94dcea88406579'


def compose(repository: Path, target: Path, receipt: Path, product: str, test: str) -> dict:
    sha(product); sha(test)
    if target.exists() or receipt.exists():
        raise ValueError('Fresh consumer worktree and receipt paths required')
    product_changes(repository, CORE_PRODUCT, product)
    test_changes = changes(repository, CORE_TEST, test, ('tests/', '.github/workflows/projectide-core-isolated.yml', '.github/workflows/projectide-value-consumers.yml'))
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
                 coreProductTree=git(repository,'rev-parse',product+'^{tree}'), coreTestTree=git(repository,'rev-parse',test+'^{tree}'),
                 pinnedComposerSha256=producer_hashes)
    patch = subprocess.check_output(['git','diff','--binary',CORE_PRODUCT,product,'--','src'],cwd=repository)
    subprocess.run(['git','apply','--index','-'],input=patch,cwd=target,check=True)
    run(['git','-c','user.name=Coglatas isolated verification','-c','user.email=isolated-verification@example.invalid',
         'commit','--date=2026-11-01T00:00:00+00:00','-m','Compose exact Core value-binding runtime delta for Inspector verification'],target)
    for commit in test_changes:
        git(target,'cherry-pick',commit)
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
    args=parser.parse_args()
    print(json.dumps(compose(Path.cwd(),args.worktree.resolve(),args.receipt.resolve(),args.product_head,args.test_head),indent=2))
