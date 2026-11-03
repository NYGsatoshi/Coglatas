"""Real temporary Git histories cannot turn arbitrary merges into Test ownership."""
import copy
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch

import projectide_dependency_continuations as continuation


def git(repository, *arguments):
    result = subprocess.run(['git', *arguments], cwd=repository, check=True,
                            text=True, encoding='utf-8', stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    return result.stdout.strip()


class ExactDependencyContinuationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='projectide-continuation-controls-')
        self.root = Path(self.temporary.name).resolve()
        self.assertEqual(Path(tempfile.gettempdir()).resolve(), self.root.parent)
        self.addCleanup(self.temporary.cleanup)
        self.repo = self.root / 'repository'
        self.repo.mkdir()
        git(self.repo, 'init', '--initial-branch=graph')
        git(self.repo, 'config', 'user.name', 'Independent continuation fixture')
        git(self.repo, 'config', 'user.email', 'continuation@example.invalid')
        self.base = self.commit('tests/base.txt', 'base')
        self.first = self.commit('tests/graph.txt', 'graph')
        git(self.repo, 'checkout', '-b', 'diagnostic', self.base)
        self.side = self.commit('tests/fix.txt', 'fix')
        git(self.repo, 'checkout', 'graph')
        git(self.repo, 'merge', '--no-ff', '--no-edit', 'diagnostic')
        self.merge = git(self.repo, 'rev-parse', 'HEAD')
        self.expected = dict(parents=[self.first, self.side], tree=git(self.repo, 'rev-parse', 'HEAD^{tree}'),
                             paths=['tests/fix.txt'], sideCommits=[self.side])

    def commit(self, path, text):
        target = self.repo / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text)
        git(self.repo, 'add', '--all')
        git(self.repo, 'commit', '-m', text)
        return git(self.repo, 'rev-parse', 'HEAD')

    def merge_object(self, tree, parents, message):
        arguments = ['commit-tree', tree]
        for parent in parents:
            arguments += ['-p', parent]
        return git(self.repo, *arguments, '-m', message)

    def plan(self, head=None):
        return continuation.test_changes(self.repo, self.base, head or self.merge, ('tests/',))

    def test_exact_first_parent_plan_replays_the_inspected_merge_once_with_equal_bytes(self):
        with patch.dict(continuation.TEST_MERGES, {self.merge: self.expected}, clear=True):
            commits, merges = self.plan()
            self.assertEqual([self.first, self.merge], commits)
            self.assertEqual([self.side], merges[0]['sideCommits'])
            target = self.root / 'replay'
            git(self.repo, 'worktree', 'add', '--detach', str(target), self.base)
            continuation.apply_test_changes(target, commits, merges)
            self.assertEqual(self.expected['tree'], git(target, 'rev-parse', 'HEAD^{tree}'))
            self.assertEqual('2', git(target, 'rev-list', '--count', self.base + '..HEAD'))
            self.assertEqual('', git(target, 'diff', self.merge, 'HEAD', '--', 'tests'))

    def test_unlisted_sha_is_rejected_even_with_the_same_parents_tree_and_paths(self):
        alternate = self.merge_object(self.expected['tree'], self.expected['parents'], 'uninspected merge identity')
        self.assertNotEqual(self.merge, alternate)
        with patch.dict(continuation.TEST_MERGES, {self.merge: self.expected}, clear=True):
            with self.assertRaisesRegex(ValueError, 'Unapproved dependency merge identity'):
                self.plan(alternate)

    def test_ordered_two_parent_identity_cannot_be_swapped_or_extended(self):
        swapped = dict(self.expected, parents=list(reversed(self.expected['parents'])))
        with patch.dict(continuation.TEST_MERGES, {self.merge: swapped}, clear=True):
            with self.assertRaisesRegex(ValueError, 'parents or tree'):
                self.plan()
        parents = self.expected['parents'] + [self.base]
        alternate = self.merge_object(self.expected['tree'], parents, 'third parent')
        with patch.dict(continuation.TEST_MERGES, {alternate: dict(self.expected, parents=parents)}, clear=True):
            with self.assertRaisesRegex(ValueError, 'parents or tree'):
                self.plan(alternate)

    def test_a_substituted_resolution_tree_cannot_use_an_inspected_merge_identity(self):
        expected = dict(self.expected, tree=git(self.repo, 'rev-parse', self.base + '^{tree}'))
        with patch.dict(continuation.TEST_MERGES, {self.merge: expected}, clear=True):
            with self.assertRaisesRegex(ValueError, 'parents or tree'):
                self.plan()

    def test_uninspected_resolution_paths_are_rejected(self):
        expected = dict(self.expected, paths=['tests/uninspected.txt'])
        with patch.dict(continuation.TEST_MERGES, {self.merge: expected}, clear=True):
            with self.assertRaisesRegex(ValueError, 'ownership boundary'):
                self.plan()

    def test_runtime_side_commits_are_rejected_even_when_the_resolution_omits_runtime(self):
        git(self.repo, 'checkout', 'diagnostic')
        runtime = self.commit('src/runtime.cs', 'synthetic Product ownership sentinel')
        parents = [self.first, runtime]
        alternate = self.merge_object(self.expected['tree'], parents, 'concealed side runtime')
        expected = dict(self.expected, parents=parents, sideCommits=[self.side, runtime])
        with patch.dict(continuation.TEST_MERGES, {alternate: expected}, clear=True):
            with self.assertRaisesRegex(ValueError, 'single-parent ownership boundary'):
                self.plan(alternate)

    def test_duplicate_and_unplanned_merge_replays_are_rejected_before_mutation(self):
        with patch.dict(continuation.TEST_MERGES, {self.merge: self.expected}, clear=True):
            commits, merges = self.plan()
            with self.assertRaisesRegex(ValueError, 'Duplicate or unbound'):
                continuation.apply_test_changes(self.repo, commits + [self.merge], merges)
            with self.assertRaisesRegex(ValueError, 'unplanned merge'):
                continuation.apply_test_changes(self.repo, [self.merge], [])
            self.assertEqual(self.merge, git(self.repo, 'rev-parse', 'HEAD'))

    def test_product_merge_requires_the_exact_clean_resolution_tree(self):
        expected = copy.deepcopy(self.expected)
        expected.pop('sideCommits')
        with patch.dict(continuation.PRODUCT_MERGES, {self.merge: expected}, clear=True):
            self.assertEqual(self.expected['tree'], continuation.product_merge(self.repo, self.merge, ('tests/',))['tree'])
        tree = git(self.repo, 'rev-parse', self.base + '^{tree}')
        alternate = self.merge_object(tree, self.expected['parents'], 'uninspected Product resolution')
        paths = git(self.repo, 'diff', '--name-only', self.first, alternate).splitlines()
        with patch.dict(continuation.PRODUCT_MERGES, {alternate: dict(expected, tree=tree, paths=paths)}, clear=True):
            with self.assertRaisesRegex(ValueError, 'clean merge tree'):
                continuation.product_merge(self.repo, alternate, ('tests/',))


if __name__ == '__main__':
    unittest.main()
