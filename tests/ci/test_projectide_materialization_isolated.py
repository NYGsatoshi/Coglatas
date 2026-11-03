"""Independent negative cases for a test-only proposal, not approval of a schema."""
from dataclasses import replace
import unittest
from projectide_materialization_prototype import Input, ReceiptClaim, Node, Edge, pin, assess, graph


class MaterializationProposalTests(unittest.TestCase):
    def setUp(self):
        self.source = pin('source', b's')
        self.manifest = pin('manifest', b'm')
        self.dependency = pin('source', b'd', revision='dep-r1')
        self.input = Input(self.source, self.manifest, (self.dependency,))
        self.receipt = ReceiptClaim(self.input, ((self.dependency, b'd'),))

    def test_integrity_never_authenticates_or_qualifies_evidence(self):
        value = assess(self.input, self.receipt)
        self.assertEqual((), value['findings'])
        self.assertEqual('UNVERIFIED', value['producer'])
        self.assertEqual('UNVERIFIED', value['authorization'])
        self.assertEqual('INELIGIBLE', value['evidence'])
        self.assertEqual('NOT_EVALUATED', value['satisfaction'])
        self.assertEqual('NOT_LOWERED', value['externalIR'])

    def test_source_and_manifest_substitution_or_old_receipt_are_stale(self):
        for field in ['source', 'manifest']:
            with self.subTest(field=field):
                desired = replace(self.input, **{field: pin(field, b'substituted')})
                self.assertIn('input-mismatch/stale', assess(desired, self.receipt)['findings'])

    def test_same_bytes_different_revision_candidate_scenario_or_scope_cannot_replay(self):
        for field in ['revision', 'candidate', 'scenario', 'scope']:
            with self.subTest(field=field):
                desired = replace(self.input, source=replace(self.source, **{field: 'new'}))
                self.assertIn('input-mismatch/stale', assess(desired, self.receipt)['findings'])

    def test_tampered_body_fails_integrity(self):
        receipt = replace(self.receipt, actual=((self.dependency, b'tampered'),))
        self.assertIn('integrity-mismatch', assess(self.input, receipt)['findings'])

    def test_unknown_schema_or_canonical_version_is_not_interpreted(self):
        for field in ['version', 'canonical']:
            changed = replace(self.dependency, **{field: 99})
            desired = replace(self.input, dependencies=(changed,))
            result = assess(desired, ReceiptClaim(desired, ((changed, b'd'),)))
            self.assertIn('unsupported-version', result['findings'])

    def test_duplicate_and_conflicting_declared_pins_reject(self):
        for second in [self.dependency, replace(self.dependency, digest='f' * 64)]:
            desired = replace(self.input, dependencies=(self.dependency, second))
            self.assertIn('duplicate-or-conflicting-pin', assess(desired, self.receipt)['findings'])

    def test_missing_and_undeclared_bodies_stay_distinct(self):
        self.assertIn('missing-materialization', assess(self.input, replace(self.receipt, actual=()))['findings'])
        unknown = pin('source', b'other', revision='other')
        self.assertIn('undeclared-body', assess(self.input, replace(self.receipt, actual=((unknown, b'other'),)))['findings'])

    def test_duplicate_body_cannot_be_counted_twice(self):
        self.assertIn('duplicate-body', assess(self.input, replace(self.receipt, actual=self.receipt.actual * 2))['findings'])

    def test_cancellation_and_exhaustion_retain_all_unverified_stages(self):
        for options, code in [(dict(cancelled=lambda: True), 'cancelled'), (dict(budget=1), 'exhausted')]:
            value = assess(self.input, self.receipt, **options)
            self.assertIn(code, value['findings']); self.assertEqual('INELIGIBLE', value['evidence'])

    def test_denied_bodies_have_no_identity_in_diagnostics(self):
        results = [assess(self.input, replace(self.receipt, actual=((replace(self.dependency, scope=scope), b'secret'),)))
                   for scope in ['hidden-a', 'hidden-b']]
        self.assertEqual(*results)
        self.assertNotIn('hidden', str(results))


class ClosureProposalTests(unittest.TestCase):
    def setUp(self):
        self.a = pin('source', b'a', revision='a'); self.b = pin('source', b'b', revision='b')
        self.nodes = (Node(self.a), Node(self.b)); self.edge = Edge('edge-1', 'dependency', self.a, self.b)

    def test_deterministic_complete_example_is_not_production_closure(self):
        self.assertEqual((), graph(self.nodes, (self.edge,)))
        self.assertEqual(graph(self.nodes, (self.edge,)), graph(reversed(self.nodes), (self.edge,)))

    def test_partial_and_missing_frontier_are_not_empty_adjacency(self):
        self.assertIn('partial-frontier', graph((Node(self.a, False),), ()))
        self.assertIn('missing-frontier', graph((Node(self.a),), (self.edge,)))

    def test_duplicate_nodes_and_conflicting_bodies_reject(self):
        self.assertIn('duplicate-node', graph(self.nodes + (Node(self.a),), ()))
        self.assertIn('conflicting-node', graph(self.nodes + (Node(replace(self.a, digest='f' * 64)),), ()))

    def test_edge_identity_is_stable_and_duplicates_or_conflicts_reject(self):
        for edge in [self.edge, replace(self.edge, kind='related')]:
            self.assertIn('duplicate-or-conflicting-edge', graph(self.nodes, (self.edge, edge)))

    def test_exact_version_and_digest_are_required_at_edge_targets(self):
        for field, value in [('version', 2), ('digest', 'f' * 64), ('revision', 'another')]:
            self.assertIn('missing-frontier', graph(self.nodes, (replace(self.edge, target=replace(self.b, **{field: value})),)))

    def test_cycles_use_provisional_kind_specific_rules(self):
        edges = (self.edge, Edge('edge-2', 'dependency', self.b, self.a))
        self.assertIn('dependency-cycle', graph(self.nodes, edges))
        self.assertEqual((), graph(self.nodes, tuple(replace(edge, kind='related') for edge in edges)))
        self.assertIn('unsupported-kind', graph(self.nodes, (replace(self.edge, kind='unknown'),)))

    def test_cancellation_and_shared_budget_bound_traversal(self):
        self.assertIn('cancelled', graph(self.nodes, (self.edge,), cancelled=lambda: True))
        self.assertIn('exhausted', graph(self.nodes, (self.edge,), budget=3))

    def test_unknown_canonical_versions_stay_unsupported(self):
        self.assertIn('unsupported-version', graph((Node(replace(self.a, canonical=2)),), ()))


if __name__ == '__main__':
    unittest.main()
