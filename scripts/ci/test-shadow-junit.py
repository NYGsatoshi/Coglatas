#!/usr/bin/env python3
"""Reject incomplete, failed, and untrusted local shadow JUnit evidence."""
from __future__ import annotations
import importlib.util
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "scripts/ci/check-shadow-junit.py"
spec = importlib.util.spec_from_file_location("shadow_junit", SCRIPT)
assert spec is not None and spec.loader is not None
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
XML = "<testsuites><testsuite><testcase name='private case'/><testcase name='skip'><skipped/></testcase></testsuite></testsuites>"


class ShadowJunitTests(unittest.TestCase):
    def test_success_counts_only_without_publishing_names(self):
        with tempfile.TemporaryDirectory() as root:
            f = Path(root) / "junit.xml"
            f.write_text(XML, encoding="utf-8")
            result = module.summarize(f, "1/2")
            self.assertEqual({"cases": 2, "passed": 1, "skipped": 1},
                             {k: result[k] for k in ("cases", "passed", "skipped")})
            self.assertNotIn("private case", str(result))
            self.assertEqual("LOCAL_SHARD_ONLY_NOT_FULL_ROSTER", result["qualification"])

    def test_incomplete_or_failed_or_invalid_shard_denied(self):
        with tempfile.TemporaryDirectory() as root:
            f = Path(root) / "junit.xml"
            for shard in ("", "0/2", "1/1", "2/3", "1/02"):
                with self.subTest(shard=shard):
                    f.write_text(XML, encoding="utf-8")
                    with self.assertRaises(ValueError):
                        module.summarize(f, shard)
            for content in (
                "<testsuites/>",
                "<testsuites><testsuite><testcase><failure/></testcase></testsuite></testsuites>",
                "<testsuites><testsuite><testcase><error/></testcase></testsuite></testsuites>",
                "<testsuites><testsuite><testcase><skipped/></testcase></testsuite></testsuites>",
                "<testsuites",
            ):
                with self.subTest(content=content):
                    f.write_text(content, encoding="utf-8")
                    with self.assertRaises((ValueError, module.ET.ParseError)):
                        module.summarize(f, "2/2")

    def test_symlink_and_oversized_input_denied(self):
        with tempfile.TemporaryDirectory() as root:
            original = Path(root) / "junit.xml"
            original.write_text(XML, encoding="utf-8")
            link = Path(root) / "alias.xml"
            link.symlink_to(original)
            with self.assertRaises(ValueError):
                module.summarize(link, "1/2")
            large = Path(root) / "oversize.xml"
            with large.open("wb") as out:
                out.truncate(16 * 1024 * 1024 + 1)
            with self.assertRaises(ValueError):
                module.summarize(large, "1/2")


if __name__ == "__main__":
    unittest.main()
