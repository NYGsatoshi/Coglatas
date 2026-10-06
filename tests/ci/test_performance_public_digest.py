import copy
import sys
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts/performance'))
from compare import ComparatorError, baseline_environment_digest


class PublicDigestReaderTests(unittest.TestCase):
    def test_legacy_and_public_name_read_exactly_the_same_digest(self):
        value = 'a' * 64
        self.assertEqual(value, baseline_environment_digest({'environmentCompatibilityKey': value}))
        self.assertEqual(value, baseline_environment_digest({'environmentCompatibilityDigest': value}))

    def test_dual_identity_is_rejected_even_when_values_agree(self):
        for second in ('a', 'b'):
            with self.assertRaises(ComparatorError):
                baseline_environment_digest({'environmentCompatibilityKey': 'a' * 64, 'environmentCompatibilityDigest': second * 64})

    def test_digest_cannot_be_replaced_with_a_credential_or_missing_identity(self):
        for value in ({}, {'environmentCompatibilityDigest': 'credential-shaped-text'}, {'environmentCompatibilityDigest': True}):
            with self.assertRaises(ComparatorError):
                baseline_environment_digest(value)
