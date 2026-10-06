#!/usr/bin/env python3
"""Regression tests using ordinary temporary checkout fixtures."""
from __future__ import annotations

import importlib.util
import io
import itertools
import json
import re
import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from unittest.mock import patch

sys.dont_write_bytecode = True
SCRIPT = Path(__file__).with_name("check-coglatas-rename.py")
SPEC = importlib.util.spec_from_file_location("coglatas_naming_guard", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
GUARD = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = GUARD
SPEC.loader.exec_module(GUARD)


def examples(pattern: str) -> list[str]:
    """Expand visible case classes into example text, solely for test fixtures."""
    classes = re.findall(r"\[([A-Za-z])([A-Za-z])\]", pattern)
    return ["".join(characters) for characters in itertools.product(*classes)]


INITIALS = [examples(branch)[0] for branch in GUARD.RETIRED_NAMES.pattern.split("|")]
OWNER = examples(GUARD.EXTERNAL_OWNER.pattern)[0]
PACKAGE = examples(GUARD.EXTERNAL_PACKAGE.pattern)[0].lower()


class LegacyNameGuardTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory(prefix="coglatas-naming-")
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.git("init", "--quiet")

    def git(self, *arguments):
        return subprocess.run(
            ["git", "-c", f"safe.directory={self.root.as_posix()}", *arguments],
            cwd=self.root, check=True, capture_output=True, text=True,
        )

    def write(self, relative, text="Coglatas", encoding="utf-8"):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding=encoding)
        return path

    def scan(self):
        return GUARD.audit(self.root)

    def run_cli(self):
        return subprocess.run(
            [sys.executable, str(SCRIPT), "--root", str(self.root)],
            capture_output=True, text=True,
        )

    def lockfile(self, **fields):
        fields = {"resolved": "https://registry.npmjs.org/example/-/example-1.0.0.tgz", **fields}
        data = {"lockfileVersion": 3, "packages": {"node_modules/example": fields}}
        self.write("package-lock.json", json.dumps(data))

    def digest(self):
        # An ordinary valid 64-byte SRI digest with an incidental policy match.
        return f"sha512-{INITIALS[0]}{'Z' * 83}=="

    def test_clean_checkout_and_cli_pass(self):
        self.write("README.md")
        self.assertEqual(self.scan().findings, [])
        self.assertEqual(self.run_cli().returncode, 0)

    def test_every_case_variant_fails(self):
        for branch in GUARD.RETIRED_NAMES.pattern.split("|"):
            for name in examples(branch):
                with self.subTest(name=name):
                    self.write("fixture.md", name)
                    findings = self.scan().findings
                    self.assertEqual(len(findings), 1)
                    self.assertEqual(findings[0].kind, "content")

    def test_product_derivatives_prefixes_and_suffixes_fail(self):
        names = [(f"{INITIALS[0]}site", 1), (f"{INITIALS[0]}site{INITIALS[1]}", 2),
                 (f"{INITIALS[0]}ex", 1), (f"prefix{INITIALS[0]}Suffix", 1),
                 (f"{INITIALS[1]}Server", 1), (f"__{INITIALS[0]}_FEATURE_FLAGS__", 1)]
        for name, expected in names:
            with self.subTest(name=name):
                self.write("fixture.txt", name.swapcase())
                self.assertEqual(len(self.scan().findings), expected)

    def unicode_variants(self, name):
        yield "".join(chr(ord(letter) + 0xFEE0) for letter in name)
        yield "".join(chr(0x1D400 + ord(letter) - ord("A")) for letter in name)
        yield "".join(chr(0x24B6 + ord(letter) - ord("A")) for letter in name)
        yield "\u200b".join(name)
        yield "\u0301".join(name)
        yield name.replace("A", "\u0391").replace("I", "\u0406").replace("P", "\u0420")
        yield name.replace("N", "\u039d").replace("Y", "\u03a5").replace("G", "\u050c")

    def test_unicode_content_variants_fail_with_original_locations(self):
        for name in INITIALS:
            for variant in self.unicode_variants(name):
                with self.subTest(variant=variant):
                    self.write("fixture.txt", f"safe\n  {variant}")
                    finding, = self.scan().findings
                    self.assertEqual((finding.kind, finding.line, finding.column), ("content", 2, 3))
                    self.assertEqual(self.run_cli().returncode, 1)

    def test_unicode_checkout_paths_fail(self):
        for name in INITIALS:
            for variant in self.unicode_variants(name):
                with self.subTest(variant=variant):
                    path = self.write(f".hidden/{variant}/safe.txt")
                    self.assertEqual(self.scan().findings[0].kind, "path")
                    path.unlink()

    def test_unicode_archive_paths_and_content_fail(self):
        for name in INITIALS:
            for variant in self.unicode_variants(name):
                with self.subTest(variant=variant):
                    self.archive({f"{variant}/record.md": variant})
                    self.assertEqual({finding.kind for finding in self.scan().findings}, {"path", "content"})
                    self.assertEqual(self.run_cli().returncode, 1)

    def test_unicode_external_owner_is_not_an_exact_identity(self):
        for variant in self.unicode_variants(OWNER):
            if variant == OWNER:
                continue
            with self.subTest(variant=variant):
                self.write("owner.md", f"@{variant}")
                self.assertTrue(self.scan().findings)

    def test_normalization_does_not_broaden_identity_or_digest_exceptions(self):
        variant = next(self.unicode_variants(INITIALS[0]))
        self.write("owner.md", f"@{OWNER} {variant}")
        result = self.scan()
        self.assertEqual(result.external_matches, 1)
        self.assertEqual(len(result.findings), 1)
        (self.root / "owner.md").unlink()
        self.lockfile(integrity=self.digest(), description=variant)
        self.assertEqual(len(self.scan().findings), 1)

    def test_json_escaped_ascii_and_unicode_names_fail(self):
        for name in INITIALS:
            variants = [name, *self.unicode_variants(name)]
            for variant in variants:
                with self.subTest(variant=variant):
                    escaped = "".join(f"\\u{ord(letter):04x}" for letter in name)
                    value = '{"name":"' + escaped + '"}' if variant == name else json.dumps({"name": variant})
                    self.write("fixture.json", value)
                    finding, = self.scan().findings
                    self.assertEqual(finding.kind, "content")
                    self.assertEqual(self.run_cli().returncode, 1)

    def test_json_escaped_identity_remains_unapproved(self):
        escaped = "".join(f"\\u{ord(letter):04x}" for letter in OWNER)
        self.write("owner.json", '{"owner":"' + escaped + '"}')
        self.assertTrue(self.scan().findings)

    def test_json_escape_does_not_duplicate_raw_findings(self):
        self.write("fixture.json", json.dumps({"name": INITIALS[0] + "\n"}))
        self.assertEqual(len(self.scan().findings), 1)

    def test_valid_json_with_non_string_values_passes(self):
        self.write("fixture.json", json.dumps({"value": [None, 12, True, {"name": "Coglatas"}]}))
        self.assertEqual(self.scan().findings, [])

    def test_hidden_untracked_content_fails(self):
        self.write(".hidden/config.json", INITIALS[0])
        self.assertEqual(self.scan().findings[0].path, ".hidden/config.json")

    def test_filename_and_directory_fail(self):
        for relative in (f"{INITIALS[0]}.cs", f"src/{INITIALS[1]}/safe.cs"):
            with self.subTest(relative=relative):
                path = self.write(relative)
                self.assertEqual(self.scan().findings[0].kind, "path")
                path.unlink()

    def test_cli_intentional_content_and_path_injections_fail(self):
        path = self.write("fixture.txt", INITIALS[0])
        result = self.run_cli()
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("FAIL content", result.stdout)
        path.unlink()
        self.write(f"{INITIALS[1]}.txt")
        result = self.run_cli()
        self.assertEqual(result.returncode, 1, result.stdout + result.stderr)
        self.assertIn("FAIL path", result.stdout)

    def test_tracked_ignored_content_remains_in_scope(self):
        self.write(".gitignore", "tracked.log\n")
        self.write("tracked.log", INITIALS[0])
        self.git("add", "--force", "--", "tracked.log")
        self.assertEqual(self.scan().findings[0].path, "tracked.log")

    def test_deleted_tracked_path_is_not_current_checkout(self):
        path = self.write(f"{INITIALS[0]}.md", INITIALS[0])
        self.git("add", "--", path.name)
        path.unlink()
        self.assertEqual(self.scan().findings, [])

    def test_utf16_and_utf32_content_fail(self):
        for encoding in ("utf-16", "utf-32"):
            with self.subTest(encoding=encoding):
                self.write("fixture.txt", INITIALS[0], encoding)
                self.assertEqual(self.scan().findings[0].kind, "content")

    def test_binary_asset_path_still_fails(self):
        path = self.root / f"{INITIALS[0]}.png"
        path.write_bytes(b"\x89PNG\r\n\x1a\n\0\xff")
        result = self.scan()
        self.assertEqual(result.binary_files, 1)
        self.assertEqual(result.findings[0].kind, "path")

    def test_only_specification_has_exact_patent_boundary(self):
        self.write(f"docs/legal/patents/{INITIALS[0]}-IP-001.md", INITIALS[0])
        result = self.scan()
        if GUARD.PATENT_DIRECTORY is None:
            self.assertEqual(result.patent_files, 0)
            self.assertEqual({finding.kind for finding in result.findings}, {"path", "content"})
        else:
            self.assertEqual(GUARD.PATENT_DIRECTORY, "docs/legal/patents")
            self.assertEqual(result.patent_files, 1)
            self.assertEqual(result.findings, [])

    def test_similar_legal_directories_are_not_excluded(self):
        for directory in ("docs/legal/patents-old", "docs/legal/patent",
                          "docs/legal/Patents", "other/docs/legal/patents"):
            with self.subTest(directory=directory):
                path = self.write(f"{directory}/record.md", INITIALS[0])
                self.assertEqual(len(self.scan().findings), 1)
                path.unlink()
        self.write("docs/legal/patents.md", INITIALS[0])
        self.assertEqual(len(self.scan().findings), 1)

    def test_archive_evidence_logs_and_migration_are_not_excluded(self):
        for relative in ("docs/archive/record.md", "docs/evidence/record.log",
                         "docs/verification/record.md", "docs/migration/record.json"):
            with self.subTest(relative=relative):
                path = self.write(relative, INITIALS[0])
                self.assertEqual(len(self.scan().findings), 1)
                path.unlink()

    def test_exact_external_owner_is_classified_but_internal_extension_fails(self):
        self.write("owner.md", f"@{OWNER} https://github.com/{OWNER}/Coglatas")
        result = self.scan()
        self.assertEqual(result.findings, [])
        self.assertEqual(result.external_matches, 2)
        self.write("owner.md", f"{OWNER}Service")
        self.assertEqual(len(self.scan().findings), 1)

    def test_external_owner_does_not_exempt_same_line_or_local_path(self):
        self.write("owner.md", f"@{OWNER} {INITIALS[0]}")
        self.assertEqual(len(self.scan().findings), 1)
        self.write("owner.md")
        self.write(f"{OWNER}/record.md")
        self.assertEqual(self.scan().findings[0].kind, "path")

    def test_escaped_tsv_repository_identity_is_classified_narrowly(self):
        self.write(
            "workflow.yml",
            f"expected='1046\\topen\\t{OWNER}/Coglatas\\t{OWNER}/Coglatas\\tfalse'",
        )
        result = self.scan()
        self.assertEqual(result.findings, [])
        self.assertEqual(result.external_matches, 2)

        self.write("workflow.yml", f"expected='1046\\topen\\t{OWNER}\\tfalse'")
        result = self.scan()
        self.assertEqual(len(result.findings), 1)
        self.assertEqual(result.external_matches, 0)

    def test_valid_lockfile_digest_and_exact_dependency_are_classified(self):
        self.assertTrue(GUARD.valid_integrity(self.digest()))
        self.lockfile(integrity=self.digest(), dependencies={PACKAGE: "0.2.17"})
        result = self.scan()
        self.assertEqual(result.findings, [])
        self.assertEqual(result.external_matches, 2)

    def test_lockfile_integrity_does_not_exempt_same_line_metadata(self):
        self.lockfile(integrity=self.digest(), description=INITIALS[0])
        result = self.scan()
        self.assertEqual(result.external_matches, 1)
        self.assertEqual(len(result.findings), 1)

    def test_invalid_digest_and_nonlockfile_digest_fail(self):
        self.lockfile(integrity=f"sha512-{INITIALS[0]}")
        self.assertEqual(len(self.scan().findings), 1)
        (self.root / "package-lock.json").unlink()
        self.write("fixture.json", json.dumps({"integrity": self.digest()}))
        self.assertEqual(len(self.scan().findings), 1)

    def test_unrelated_dependency_requires_valid_lockfile_context(self):
        self.write("fixture.txt", PACKAGE)
        self.assertEqual(len(self.scan().findings), 1)
        (self.root / "fixture.txt").unlink()
        self.write("package-lock.json", json.dumps({"dependency": PACKAGE}))
        self.assertEqual(len(self.scan().findings), 1)

    def test_bare_account_and_service_suffix_are_not_identity_references(self):
        for value in (OWNER, f"{OWNER}-server", f"{OWNER}_runtime"):
            with self.subTest(value=value):
                self.write("config.json", json.dumps({"service.name": value}))
                self.assertEqual(len(self.scan().findings), 1)

    def test_standalone_external_identity_contexts_are_classified(self):
        examples = (f"Copyright (c) 2026 {OWNER}. All rights reserved.",
                    f"account `{OWNER}`", f'{{"user":{{"login":"{OWNER}"}}}}',
                    f'self.review_state(author="{OWNER}", reviews=[])',
                    f"# GitHub repository transfer: Coglatas organization to {OWNER}",
                    f"`{OWNER}` (GitHub user id `285141121`)")
        for text in examples:
            with self.subTest(text=text):
                self.write("identity.md", text)
                result = self.scan()
                self.assertEqual(result.findings, [])
                self.assertEqual(result.external_matches, 1)

    def test_dependency_description_and_unrelated_integrity_property_fail(self):
        self.lockfile(description=PACKAGE)
        self.assertEqual(len(self.scan().findings), 1)
        self.write("package-lock.json", json.dumps(
            {"lockfileVersion": 3, "description": {"integrity": self.digest()}, "packages": {}}
        ))
        self.assertEqual(len(self.scan().findings), 1)

    def test_campaign_approver_is_external_identity_only(self):
        self.write('campaign.json', json.dumps({'authorization': {'approver': OWNER}}))
        self.assertEqual(self.scan().findings, [])
        self.assertEqual(self.scan().external_matches, 1)
        self.write('campaign.json', json.dumps({'authorization': {'approver': OWNER + 'Service'}}))
        self.assertEqual(len(self.scan().findings), 1)
        self.write('campaign.json', json.dumps({'authorization': {'approver': OWNER, 'reason': INITIALS[0]}}))
        self.assertEqual(len(self.scan().findings), 1)

    def test_digest_requires_external_dependency_record(self):
        self.write("package-lock.json", json.dumps(
            {"lockfileVersion": 3, "packages": {"node_modules/example": {"integrity": self.digest()}}}
        ))
        self.assertEqual(len(self.scan().findings), 1)

    def test_unsupported_text_encoding_fails_closed(self):
        path = self.root / "fixture.txt"
        for encoding in ("utf-16-le", "latin-1"):
            with self.subTest(encoding=encoding):
                path.write_bytes(f"{INITIALS[0]} café".encode(encoding))
                result = self.scan()
                self.assertEqual(result.binary_files, 0)
                self.assertEqual(len(result.findings), 1)
                self.assertEqual(result.findings[0].kind, "unreadable")


    def archive(self, entries, relative="source.zip"):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        with zipfile.ZipFile(path, "w") as archive:
            for name, content in entries.items():
                archive.writestr(name, content)
        return path

    def test_compliant_source_archive_passes(self):
        self.archive({"docs/README.md": "Coglatas", "tables/data.csv": "name,value\nCoglatas,1\n"})
        result = self.scan()
        self.assertEqual(result.findings, [])
        self.assertEqual(result.archive_members, 2)
        self.assertEqual(result.binary_files, 0)

    def test_archive_content_and_member_path_fail(self):
        for name, content, kind in (("README.md", INITIALS[0], "content"),
                                    (f"src/{INITIALS[1]}/safe.md", "Coglatas", "path")):
            with self.subTest(kind=kind):
                self.archive({name: content})
                result = self.scan()
                self.assertEqual(len(result.findings), 1)
                self.assertEqual(result.findings[0].kind, kind)
                self.assertEqual(result.findings[0].path, f"source.zip!/{name}")
                self.assertEqual(self.run_cli().returncode, 1)

    def test_archive_fake_patent_directory_is_not_excluded(self):
        self.archive({f"docs/legal/patents/{INITIALS[0]}-IP-001.md": INITIALS[0]})
        self.assertEqual({finding.kind for finding in self.scan().findings}, {"content", "path"})

    def test_nested_archive_content_is_checked(self):
        inner = io.BytesIO()
        with zipfile.ZipFile(inner, "w") as archive:
            archive.writestr("README.md", INITIALS[0])
        self.archive({"nested.zip": inner.getvalue()})
        result = self.scan()
        self.assertEqual(len(result.findings), 1)
        self.assertEqual(result.findings[0].path, "source.zip!/nested.zip!/README.md")

    def test_archive_unsafe_paths_are_rejected_without_extraction(self):
        self.archive({"../outside.md": "Coglatas"})
        self.assertEqual(self.scan().findings[0].kind, "unsafe-archive-path")

    def test_only_outer_specification_patent_path_is_excluded(self):
        self.archive({"README.md": INITIALS[0]}, "docs/legal/patents/source.zip")
        result = self.scan()
        if GUARD.PATENT_DIRECTORY is None:
            self.assertEqual(len(result.findings), 1)
            self.assertEqual(result.archive_members, 1)
        else:
            self.assertEqual(result.findings, [])
            self.assertEqual(result.patent_files, 1)
            self.assertEqual(result.archive_members, 0)

    def test_invalid_archive_fails_closed(self):
        (self.root / "source.zip").write_bytes(b"PK\x03\x04broken")
        result = self.scan()
        self.assertEqual(len(result.findings), 1)
        self.assertEqual(result.findings[0].kind, "unreadable-archive")

    def test_archive_bom_text_content_is_checked(self):
        for encoding in ("utf-16", "utf-32"):
            with self.subTest(encoding=encoding):
                self.archive({"README.md": INITIALS[0].encode(encoding)})
                result = self.scan()
                self.assertEqual(len(result.findings), 1)
                self.assertEqual(result.findings[0].kind, "content")
    def test_guard_and_tests_have_no_special_self_exclusion(self):
        for source in (SCRIPT, Path(__file__)):
            relative = f"scripts/ci/{source.name}"
            self.write(relative, source.read_text(encoding="utf-8"))
        self.assertEqual(self.scan().findings, [])
        self.write("scripts/ci/check-coglatas-rename.py", INITIALS[0])
        self.assertEqual(len(self.scan().findings), 1)

    def test_missing_git_inventory_is_an_error(self):
        with patch.object(GUARD, "checkout_files", side_effect=subprocess.CalledProcessError(128, "git")):
            with self.assertRaises(subprocess.CalledProcessError):
                self.scan()


if __name__ == "__main__":
    unittest.main()
