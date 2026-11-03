"""Negative controls for retaining exact consumer test responsibilities."""
import unittest
from projectide_evidence_consumer_verify import normalized, require_inventory


class EvidenceConsumerInventoryTests(unittest.TestCase):
    def test_host_paths_do_not_change_vitest_identity(self):
        windows = [dict(file='C:\\candidate\\frontend\\src\\a.spec.ts', name='case', status='passed')]
        ubuntu = [dict(file='/runner/frontend/src/a.spec.ts', name='case', status='passed')]
        self.assertEqual(normalized('vitestTests', windows), normalized('vitestTests', ubuntu))
        require_inventory('vitestTests', windows, ubuntu)

    def test_removed_duplicated_empty_or_changed_outcomes_cannot_pass(self):
        for actual in [[], ['a', 'a'], ['b']]:
            with self.subTest(actual=actual), self.assertRaises(ValueError):
                require_inventory('projectideTests', actual, ['a'])
        with self.assertRaises(ValueError):
            require_inventory('vitestTests', [dict(file='src/a', name='case', status='failed')],
                              [dict(file='src/a', name='case', status='passed')])

    def test_added_cases_are_allowed_without_requiring_old_counts(self):
        require_inventory('projectideTests', ['a', 'b'], ['a'])


if __name__ == '__main__':
    unittest.main()
