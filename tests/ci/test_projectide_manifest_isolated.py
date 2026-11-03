"""Independent Option B content-DAG prototype; no proposed wire encoding is normative."""
import hashlib
import json
import unittest


def digest(role, payload):
    # Test-only ASCII examples. Production canonicalization remains owned by Core.
    return hashlib.sha256(('example.proposal/' + role + '/1\0').encode() +
                          json.dumps(payload, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def ordered(graph):
    pending = {node: set(dependencies) for node, dependencies in graph.items()}
    if any(dependency not in graph for dependencies in pending.values() for dependency in dependencies):
        raise ValueError('Partial content graph')
    result = []
    while pending:
        ready = sorted(node for node, dependencies in pending.items() if not dependencies)
        if not ready:
            raise ValueError('Circular content graph')
        result.extend(ready)
        pending = {node: dependencies.difference(ready) for node, dependencies in pending.items() if node not in ready}
    return result


class ManifestProposalGraphTests(unittest.TestCase):
    graph = dict(source=(), dependency=(), manifest=('source', 'dependency'),
                 compilerInput=('source', 'manifest'), actualSet=('source', 'dependency'),
                 receipt=('compilerInput', 'actualSet'))

    def test_selected_relationship_has_a_deterministic_construction_order(self):
        actual = ordered(self.graph)
        self.assertEqual(actual, ordered(dict(reversed(list(self.graph.items())))))
        self.assertLess(actual.index('source'), actual.index('manifest'))
        self.assertLess(actual.index('manifest'), actual.index('compilerInput'))
        self.assertLess(actual.index('compilerInput'), actual.index('receipt'))

    def test_embedding_manifest_in_source_reintroduces_the_cycle(self):
        with self.assertRaisesRegex(ValueError, 'Circular'):
            ordered(dict(self.graph, source=('manifest',)))

    def test_embedding_receipt_in_input_reintroduces_the_cycle(self):
        with self.assertRaisesRegex(ValueError, 'Circular'):
            ordered(dict(self.graph, compilerInput=('source', 'manifest', 'receipt')))

    def test_missing_dependency_is_not_an_empty_complete_graph(self):
        graph = dict(self.graph)
        del graph['dependency']
        with self.assertRaisesRegex(ValueError, 'Partial'):
            ordered(graph)

    def test_receipt_updates_never_change_source_manifest_or_input_identity(self):
        source = digest('source', {'content': 'unchanged'})
        manifest = digest('manifest', {'source': source, 'dependency': 'a'})
        compiler_input = digest('input', {'source': source, 'manifest': manifest})
        receipt = digest('receipt', {'input': compiler_input, 'actual': ['a']})
        changed_receipt = digest('receipt', {'input': compiler_input, 'actual': []})
        self.assertNotEqual(receipt, changed_receipt)
        self.assertEqual(compiler_input, digest('input', {'manifest': manifest, 'source': source}))
        self.assertEqual(source, digest('source', {'content': 'unchanged'}))

    def test_substituted_dependency_changes_manifest_input_and_receipt(self):
        manifests = [digest('manifest', {'source': 's', 'dependency': dependency}) for dependency in ['a', 'b']]
        inputs = [digest('input', {'source': 's', 'manifest': manifest}) for manifest in manifests]
        receipts = [digest('receipt', {'input': value, 'actual': ['a']}) for value in inputs]
        for values in [manifests, inputs, receipts]:
            self.assertNotEqual(*values)

    def test_same_content_at_different_revision_is_a_different_pin(self):
        self.assertNotEqual(digest('manifest', {'source': 's', 'revision': 'commit-a'}),
                            digest('manifest', {'source': 's', 'revision': 'commit-b'}))

    def test_hash_domains_and_schema_versions_are_not_interchangeable(self):
        self.assertNotEqual(digest('source', {}), digest('manifest', {}))
        self.assertNotEqual(digest('manifest', {}), digest('receipt', {}))
        self.assertNotEqual(digest('manifest', {'schemaVersion': 1}), digest('manifest', {'schemaVersion': 2}))


if __name__ == '__main__':
    unittest.main()
