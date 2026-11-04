"""Exact Navigation Product/Test plus pinned Graph compatibility; supplemental only."""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
sys.dont_write_bytecode = True

PRODUCT_BASE = 'a32e0c8daa6429ee54178698153d87110b5c81f7'
TEST_BASE = 'ed2e0b4019cc1e3b8f6cf178238ddc664772ffe2'
GRAPH_PRODUCT = '05c9fa5afc6c57eb81cfa1d321b555bb85ff8130'
GRAPH_TEST = '7241cd0c2f50ccc740b11d0dbbe2a4e5624df4f0'

def allowed(path, product):
    if product:
        return (path == 'frontend/src/app/core/i18n/messages/projectide.messages.ts' or
                path.startswith(('frontend/src/app/features/projectide/', 'tools/ProjectIde.Workbench.Synthetic/', 'docs/verification/'))
                and not path.endswith('.spec.ts'))
    return (path.startswith(('tests/Coglatas.Tests/ProjectIde/', 'tests/ci/')) or
            path == 'tests/ui/projectide-workbench.spec.ts' or
            path == '.github/workflows/projectide-navigation.yml' or
            path.startswith('frontend/src/app/features/projectide/') and path.endswith('.spec.ts'))

def compose(repository, target, receipt, product, test):
    def git(*args):
        return subprocess.check_output(['git', *args], cwd=repository, text=True, encoding='utf-8').strip()
    if target.exists() or receipt.exists(): raise ValueError('Fresh paths required')
    owned = {}
    for base, head, lane in [(PRODUCT_BASE, product, True), (TEST_BASE, test, False)]:
        if len(head) != 40 or any(c not in '0123456789abcdef' for c in head): raise ValueError('Exact SHA required')
        git('merge-base', '--is-ancestor', base, head)
        commits = git('rev-list', '--reverse', '--first-parent', base+'..'+head).splitlines()
        for commit in commits:
            if len(git('show', '-s', '--format=%P', commit).split()) != 1: raise ValueError('Unexpected dependency merge')
            paths = git('diff-tree', '--no-commit-id', '--name-only', '-r', commit).splitlines()
            if not paths or any(not allowed(p, lane) for p in paths): raise ValueError('Navigation ownership violation')
        owned['product' if lane else 'test'] = dict(base=base, head=head, parents=git('show','-s','--format=%P',head),
            tree=git('rev-parse',head+'^{tree}'), commits=commits, paths=git('diff','--name-only',base,head).splitlines())
    helpers = receipt.parent / 'graph-producer'; helpers.mkdir(parents=True)
    hashes = {}
    for path in git('ls-tree','-r','--name-only',GRAPH_TEST,'tests/ci').splitlines():
        name = Path(path).name
        if not name.startswith('projectide') or not name.endswith(('.py','.json')): continue
        content = subprocess.check_output(['git','show',GRAPH_TEST+':'+path],cwd=repository)
        (helpers/name).write_bytes(content); hashes[name]=hashlib.sha256(content).hexdigest()
    sys.path.insert(0,str(helpers))
    spec=importlib.util.spec_from_file_location('navigation_graph_composer',helpers/'projectide_value_consumers.py')
    module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    value=module.compose(repository,target,receipt,GRAPH_PRODUCT,GRAPH_TEST,
        'a84b2914853427f1c8d494e650bd7ce01edc7783','3eee9a05892d987629e410a8c6c4d501f02acb70')
    value['graphCombinedCommit']=value['combinedCommit']; value['graphCombinedTree']=value['combinedTree']
    from projectide_slice2_isolated import run
    for base, head, label in [(PRODUCT_BASE,product,'Product'),(TEST_BASE,test,'Test')]:
        patch=subprocess.check_output(['git','diff','--binary',base,head],cwd=repository)
        if patch:
            subprocess.run(['git','apply','--index','-'],input=patch,cwd=target,check=True)
            run(['git','-c','user.name=Coglatas isolated verification','-c','user.email=isolated-verification@example.invalid',
                 'commit','--date=2026-11-01T00:00:00+00:00','-m','Compose exact Navigation '+label+' delta'],target)
        for path in git('diff','--name-only',base,head).splitlines():
            if subprocess.check_output(['git','diff',head,'HEAD','--',path],cwd=target): raise ValueError('Owned source differs: '+path)
    value.update(productHead=product,testHead=test,productTree=owned['product']['tree'],testTree=owned['test']['tree'],
        navigationOwnership=owned,graphProducerSha256=hashes,
        combinedCommit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=target,text=True).strip(),
        combinedTree=subprocess.check_output(['git','rev-parse','HEAD^{tree}'],cwd=target,text=True).strip(),status='COMPOSED_NOT_VERIFIED')
    if subprocess.check_output(['git','status','--porcelain','--untracked-files=all'],cwd=target): raise ValueError('Dirty candidate')
    receipt.write_text(json.dumps(value,indent=2)+'\n',encoding='utf-8'); return value

if __name__ == '__main__':
    p=argparse.ArgumentParser(); p.add_argument('--product-head',required=True);p.add_argument('--test-head',required=True)
    p.add_argument('--worktree',type=Path,required=True);p.add_argument('--receipt',type=Path,required=True)
    a=p.parse_args(); print(json.dumps(compose(Path.cwd(),a.worktree.resolve(),a.receipt.resolve(),a.product_head,a.test_head),indent=2))
