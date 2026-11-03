"""Exact reviewed Core dependency continuations; never a generic merge waiver."""
from pathlib import Path

from projectide_slice2_isolated import git


PRODUCT_MERGES = {
    '05c9fa5afc6c57eb81cfa1d321b555bb85ff8130': {
        'parents': ['5f6475a0dbfb27470ab74454229cdeb0975d4f3d', 'a84b2914853427f1c8d494e650bd7ce01edc7783'],
        'tree': '02414b04e35e594213efe61a686119c48432a709',
        'paths': ['docs/verification/projectide-compiler-diagnostic-integration.md',
                  'src/Coglatas.Domain/ProjectIde/CompilerSemanticValidation.cs'],
    },
}
TEST_MERGES = {
    'c7f2732451b5dcd43e28ac530736894a38fc95af': {
        'parents': ['07bf73a7c152e1fed69f1b3b258b342e9ce8cdf4', '3eee9a05892d987629e410a8c6c4d501f02acb70'],
        'tree': 'f4655815e949201d4b6147333e1253aed0a4b88f',
        'paths': ['.github/workflows/projectide-core-isolated.yml', '.github/workflows/projectide-value-consumers.yml',
                  'tests/Coglatas.Tests/ProjectIde/CompilerDiagnosticIntegrationTests.cs',
                  'tests/ci/projectide-evidence-consumer-baseline.json'],
        'sideCommits': ['65dda7651b24cdaa8491e427d5a3f6c66698cc58', '3eee9a05892d987629e410a8c6c4d501f02acb70'],
    },
}


def inspect_merge(repository: Path, commit: str, expected: dict | None, allowed: tuple[str, ...], *, clean: bool = False) -> dict:
    if expected is None:
        raise ValueError('Unapproved dependency merge identity')
    parents = git(repository, 'rev-list', '--parents', '-n', '1', commit).split()[1:]
    tree = git(repository, 'rev-parse', commit + '^{tree}')
    if parents != expected['parents'] or len(parents) != 2 or tree != expected['tree']:
        raise ValueError('Dependency merge parents or tree differ from the inspected candidate')
    paths = git(repository, 'diff', '--name-only', parents[0], commit).splitlines()
    if paths != expected['paths'] or not paths or any(not path.startswith(allowed) for path in paths):
        raise ValueError('Dependency merge resolution crosses its inspected ownership boundary')
    if clean and git(repository, 'merge-tree', '--write-tree', *parents) != tree:
        raise ValueError('Product dependency merge is not the inspected clean merge tree')
    return dict(sha=commit, parents=parents, tree=tree, paths=paths)


def product_merge(repository: Path, commit: str, allowed: tuple[str, ...]) -> dict:
    return inspect_merge(repository, commit, PRODUCT_MERGES.get(commit), allowed, clean=True)


def _single_parent_change(repository: Path, commit: str, allowed: tuple[str, ...]) -> None:
    parents = git(repository, 'rev-list', '--parents', '-n', '1', commit).split()[1:]
    paths = git(repository, 'diff-tree', '--no-commit-id', '--name-only', '-r', commit).splitlines()
    if len(parents) != 1 or not paths or any(not path.startswith(allowed) for path in paths):
        raise ValueError('Independent Test commit crosses its single-parent ownership boundary')


def test_changes(repository: Path, base: str, head: str, allowed: tuple[str, ...]) -> tuple[list[str], list[dict]]:
    git(repository, 'merge-base', '--is-ancestor', base, head)
    if base not in git(repository, 'rev-list', '--first-parent', head).splitlines():
        raise ValueError('Independent Test baseline is outside the first-parent lineage')
    commits = git(repository, 'rev-list', '--reverse', '--first-parent', f'{base}..{head}').splitlines()
    merges = []
    for commit in commits:
        parents = git(repository, 'rev-list', '--parents', '-n', '1', commit).split()[1:]
        if len(parents) == 1:
            _single_parent_change(repository, commit, allowed)
            continue
        expected = TEST_MERGES.get(commit)
        record = inspect_merge(repository, commit, expected, allowed)
        side = git(repository, 'rev-list', '--reverse', '--topo-order', f'{parents[0]}..{parents[1]}').splitlines()
        if side != expected['sideCommits'] or any(item in commits for item in side):
            raise ValueError('Dependency side commits differ or would be replayed twice')
        for item in side:
            _single_parent_change(repository, item, allowed)
        merges.append(dict(record, mainline=1, sideCommits=side))
    if len(set(commits)) != len(commits):
        raise ValueError('Duplicate independent Test replay commit')
    return commits, merges


def apply_test_changes(target: Path, commits: list[str], merges: list[dict]) -> None:
    selected = {record['sha']: record for record in merges}
    if len(selected) != len(merges) or len(set(commits)) != len(commits) or not set(selected).issubset(commits):
        raise ValueError('Duplicate or unbound dependency replay record')
    for commit in commits:
        if commit in selected:
            record = selected[commit]
            expected = TEST_MERGES.get(commit)
            if expected is None or record != dict(sha=commit, **expected, mainline=1):
                raise ValueError('Dependency replay is not the exact inspected mainline record')
            git(target, 'cherry-pick', '-m', '1', commit)
        else:
            if len(git(target, 'rev-list', '--parents', '-n', '1', commit).split()) != 2:
                raise ValueError('An unplanned merge cannot be replayed as a single-parent Test commit')
            git(target, 'cherry-pick', commit)
