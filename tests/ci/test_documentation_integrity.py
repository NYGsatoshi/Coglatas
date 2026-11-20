"""Independent CLI controls over real committed Git trees; no checker imports."""

from __future__ import annotations

import copy
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from urllib.parse import unquote


ROOT = Path(__file__).resolve().parents[2]
CHECKER = ROOT / "scripts/ci/documentation_integrity.py"
CATALOGUE = "docs/catalog.yml"
COUNT_KEYS = {
    "markdown_files", "parsed_links", "external_links", "local_links",
    "validated_fragments", "catalogue_entries", "asset_references",
    "unique_assets", "protected_documents", "immutable_files",
}


class GitFixture:
    """Construct small source histories without using production parsing code."""

    def __init__(self, path, files, metadata=None, catalogue=True):
        self.path = path
        self.path.mkdir()
        self.git("init", "-q")
        self.git("config", "user.name", "Documentation fixture")
        self.git("config", "user.email", "documentation@example.invalid")
        self.git("config", "core.autocrlf", "false")
        self.git("config", "core.filemode", "true")
        self.git("config", "commit.gpgsign", "false")
        for name, content in files.items():
            self.write(name, content)
        self.seed = self.commit("Seed fixture source")
        paths = sorted(name for name in files if name.endswith(".md"))
        self.catalogue = {
            "version": 1,
            "scope": {},
            "documents": [self.document(name, (metadata or {}).get(name)) for name in paths],
        }
        self.set_scope(self.seed, paths)
        if catalogue:
            self.save_catalogue()
            self.baseline = self.commit("Record fixture catalogue")
        else:
            self.baseline = self.seed

    def git(self, *arguments):
        environment = os.environ.copy()
        for key in ("GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE"):
            environment.pop(key, None)
        environment["GIT_CONFIG_NOSYSTEM"] = "1"
        environment["GIT_CONFIG_GLOBAL"] = os.devnull
        result = subprocess.run(
            ["git", "-C", str(self.path), *arguments],
            capture_output=True, text=True, encoding="utf-8", check=True,
            timeout=15, env=environment,
        )
        return result.stdout.strip()

    def write(self, name, content):
        destination = self.path / name
        destination.parent.mkdir(parents=True, exist_ok=True)
        destination.write_bytes(content.encode("utf-8") if isinstance(content, str) else content)

    def commit(self, message="Commit fixture candidate"):
        self.git("add", "--all")
        self.git("commit", "--allow-empty", "-qm", message)
        return self.git("rev-parse", "HEAD")

    @staticmethod
    def document(path, metadata=None):
        record = {
            "path": path,
            "kind": "guide",
            "domain": "documentation-fixture",
            "lifecycle": "active",
            "classification_basis": "Independent committed-tree CLI fixture.",
        }
        record.update(copy.deepcopy(metadata or {}))
        return record

    def record(self, path):
        return next(record for record in self.catalogue["documents"] if record["path"] == path)

    def set_scope(self, base, paths_at_base, additions=()):
        self.catalogue["scope"] = {
            "repository": "NYGsatoshi/Coglatas",
            "base_commit": base,
            "base_tree": self.git("rev-parse", f"{base}^{{tree}}"),
            "file_suffix": ".md",
            "tracked_markdown_at_base": len(paths_at_base),
            "markdown_added_by_this_change": sorted(additions),
            "expected_markdown_count": len(paths_at_base) + len(additions),
            "excluded_formats": [".mdx"],
        }

    def save_catalogue(self):
        # JSON is a YAML subset. Deliberately malformed YAML gets its own control.
        self.write(CATALOGUE, json.dumps(self.catalogue, indent=2, ensure_ascii=False) + "\n")


class DocumentationIntegrityCliTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="documentation-integrity-")
        self.addCleanup(temporary.cleanup)
        self.temporary = Path(temporary.name)
        self.repo_number = 0
        self.report_number = 0

    def fixture(self, files=None, metadata=None, catalogue=True):
        self.repo_number += 1
        return GitFixture(
            self.temporary / f"repo-{self.repo_number}",
            files if files is not None else {
                "README.md": "# Fixture\n\n[Guide](docs/guide.md#section)\n",
                "docs/guide.md": "# Guide\n\n## Section\n",
            },
            metadata, catalogue,
        )

    def run_checker(self, fixture, *, ref=None, baseline=None, mode="check", expected_exit=0):
        self.report_number += 1
        report_path = self.temporary / f"report-{self.report_number}.json"
        source = ref or fixture.git("rev-parse", "HEAD")
        trusted = baseline or fixture.baseline
        arguments = [
            sys.executable, str(CHECKER), mode,
            "--repo", str(fixture.path), "--ref", source,
            "--json", str(report_path),
        ]
        if mode == "check":
            arguments += ["--baseline", trusted, "--catalog", CATALOGUE]
        result = subprocess.run(
            arguments, cwd=ROOT, capture_output=True, text=True,
            encoding="utf-8", timeout=30,
        )
        detail = f"exit={result.returncode}\nstdout={result.stdout}\nstderr={result.stderr}"
        self.assertTrue(report_path.is_file(), detail)
        report = json.loads(report_path.read_text(encoding="utf-8"))
        detail += "\nreport=" + json.dumps(report, ensure_ascii=False)
        self.assertEqual(expected_exit, result.returncode, detail)
        self.assertEqual(1, report["schema_version"], detail)
        self.assertEqual(mode, report["mode"], detail)
        self.assertEqual({0: "pass", 1: "fail", 2: "error"}[expected_exit], report["status"], detail)
        self.assertTrue(report["checker"]["version"], detail)
        self.assertIsInstance(report["checker"]["parser_versions"], dict, detail)
        self.assertTrue(report["checker"]["parser_versions"], detail)
        self.assertTrue(COUNT_KEYS.issubset(report["counts"]), detail)
        for key in COUNT_KEYS:
            self.assertIs(type(report["counts"][key]), int, detail)
            self.assertGreaterEqual(report["counts"][key], 0, detail)
        self.assertIsInstance(report["findings"], list, detail)
        for finding in report["findings"]:
            self.assertTrue({"rule", "path", "line", "target", "message"}.issubset(finding), detail)
            self.assertTrue(finding["rule"], detail)
        if expected_exit != 2:
            self.assertEqual(source, report["source"]["commit"], detail)
            self.assertEqual(fixture.git("rev-parse", f"{source}^{{tree}}"), report["source"]["tree"], detail)
            if mode == "check":
                self.assertEqual(trusted, report["baseline"]["commit"], detail)
                self.assertEqual(fixture.git("rev-parse", f"{trusted}^{{tree}}"), report["baseline"]["tree"], detail)
            else:
                self.assertIsNone(report["baseline"], detail)
        if expected_exit == 0:
            self.assertEqual([], report["findings"], detail)
        return report

    def assert_finding(self, report, rule, path, target=None):
        matches = [
            finding for finding in report["findings"]
            if finding["rule"] == rule and finding["path"] == path
            and (target is None or finding["target"] == target)
        ]
        self.assertTrue(matches, json.dumps(report, indent=2, ensure_ascii=False))
        return matches[0]

    def test_complete_positive_report_counts_real_links_and_shared_assets(self):
        fixture = self.fixture({
            "README.md": (
                "# Entry\n\n"
                "[Inline](docs/guide.md#section)\n"
                "![Image](assets/pixel.png)\n"
                "[Here](#entry)\n"
                "[Full][Target]\n"
                "[Collapsed][]\n"
                "[Shortcut]\n"
                "![Reference image][picture]\n"
                "[Root](/docs/guide.md#section)\n"
                "[Unicode](docs/%E6%97%A5%E6%9C%AC%20guide.md#見出し)\n"
                '[Title](docs/guide.md "Not a filename")\n'
                "[External](https://example.invalid/private)\n"
                "[Email](mailto:documentation@example.invalid)\n\n"
                "[Target]: docs/guide.md#section\n"
                "[Collapsed]: docs/guide.md\n"
                "[Shortcut]: docs/guide.md\n"
                "[picture]: assets/pixel.png\n"
            ),
            "docs/guide.md": "# Guide\n\n## Section\n\n[Parent](../README.md#entry)\n",
            "docs/日本 guide.md": "# 見出し\n",
            "assets/pixel.png": b"\x89PNG\r\n\x1a\nfixture",
            "assets/shared.json": '{"fixture":true}\n',
            "story.mdx": "[Outside the declared format](missing.mdx)\n",
        }, {
            "README.md": {
                "integrity": ["fixed-path-consumer"],
                "related_assets": ["assets/pixel.png", "assets/shared.json"],
            },
            "docs/guide.md": {"related_assets": ["assets/shared.json"]},
        })
        report = self.run_checker(fixture)
        expected = {
            "markdown_files": 3, "parsed_links": 13, "local_links": 11,
            "external_links": 2, "validated_fragments": 6,
            "catalogue_entries": 3, "asset_references": 3, "unique_assets": 2,
            "protected_documents": 1, "immutable_files": 0,
        }
        for key, value in expected.items():
            self.assertEqual(value, report["counts"][key], key)

    def test_each_rendered_link_form_rejects_a_broken_destination(self):
        cases = [
            ("[Inline](docs/guide.md)", "[Inline](docs/missing.md)", "link.missing"),
            ("![Image](assets/pixel.png)", "![Image](assets/missing.png)", "link.missing"),
            ("[Ref][target]\n\n[target]: docs/guide.md", "[Ref][target]\n\n[target]: docs/missing.md", "link.missing"),
            ("[Local](#entry)", "[Local](#missing)", "link.anchor"),
            ("[Remote](docs/guide.md#section)", "[Remote](docs/guide.md#missing)", "link.anchor"),
        ]
        for valid, broken, rule in cases:
            with self.subTest(link=valid):
                fixture = self.fixture({
                    "README.md": f"# Entry\n\n{valid}\n",
                    "docs/guide.md": "# Guide\n\n## Section\n",
                    "assets/pixel.png": b"image fixture",
                })
                positive = self.run_checker(fixture)
                self.assertEqual(1, positive["counts"]["local_links"])
                fixture.write("README.md", f"# Entry\n\n{broken}\n")
                fixture.commit()
                report = self.run_checker(fixture, expected_exit=1)
                finding = self.assert_finding(report, rule, "README.md")
                self.assertIsInstance(finding["line"], int)
                self.assertGreater(finding["line"], 0)

    def test_reference_labels_normalize_and_first_definition_wins(self):
        fixture = self.fixture({
            "README.md": (
                "# Entry\n\n[Full][  Mixed   Label ]\n[Collapsed][]\n[Shortcut]\n\n"
                "[mixed label]: docs/guide.md\n[mixed label]: missing.md\n"
                "[collapsed]: docs/guide.md\n[shortcut]: docs/guide.md\n"
            ),
            "docs/guide.md": "# Guide\n",
        })
        report = self.run_checker(fixture)
        self.assertEqual(3, report["counts"]["local_links"])
        fixture.write("README.md", "# Entry\n\n[Use][target]\n\n[target]: missing.md\n[target]: docs/guide.md\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.missing", "README.md", "missing.md")

    def test_code_comments_frontmatter_and_unused_definitions_are_not_links(self):
        source = (
            '---\ntitle: Frontmatter\nexample: "[Not live](missing-frontmatter.md)"\n---\n\n'
            "# Visible\n\n[Live](#visible)\n\n"
            "`[Inline code](missing-inline.md)`\n\n"
            "\\[Escaped](missing-escaped.md)\n\n"
            '<!-- [Comment](missing-comment.md) <a id="comment-only"></a> -->\n\n'
            "```markdown\n[Fenced](missing-fence.md)\n## Fenced only\n```\n\n"
            "    [Indented](missing-indented.md)\n\n"
            "[Unresolved][no-definition]\n\n[unused]: missing-unused.md\n"
        )
        fixture = self.fixture({"README.md": source})
        report = self.run_checker(fixture)
        self.assertEqual(1, report["counts"]["parsed_links"])
        self.assertEqual(1, report["counts"]["validated_fragments"])
        fixture.write("README.md", source + "\n[Real missing](missing-fence.md)\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.missing", "README.md", "missing-fence.md")

    def test_frontmatter_code_and_comment_anchors_cannot_satisfy_fragments(self):
        fixture = self.fixture({
            "README.md": (
                "---\ntitle: Frontmatter\n---\n\n# Visible\n\n"
                '```markdown\n## Fenced only\n<a id="fenced-anchor"></a>\n```\n\n'
                '`<a name="inline-anchor"></a>`\n\n'
                '<!-- <a name="comment-only"></a> -->\n\n'
                "[YAML](#title-frontmatter)\n[Code](#fenced-only)\n[Comment](#comment-only)\n"
                "[Fenced anchor](#fenced-anchor)\n[Inline anchor](#inline-anchor)\n"
            ),
        })
        report = self.run_checker(fixture, expected_exit=1)
        for target in ("#title-frontmatter", "#fenced-only", "#comment-only", "#fenced-anchor", "#inline-anchor"):
            self.assert_finding(report, "link.anchor", "README.md", target)

    def test_github_duplicate_unicode_and_explicit_anchors(self):
        fixture = self.fixture({
            "README.md": (
                '<a id="foo"></a>\n<a name="foo-1"></a>\n\n'
                "## Foo\n\n## Foo\n\n## Foo-1\n\n## Foo\n\n"
                "## Привет non-latin 你好\n\n"
                "## Install **Client** and `API`\n\n"
                "Setext heading\n--------------\n\n"
                "> ## Quoted heading\n\n- ### Nested heading\n\n"
                "[First](#foo)\n[Second](#foo-1)\n[Collision](#foo-1-1)\n[Third](#foo-2)\n"
                "[Unicode](#привет-non-latin-你好)\n[Formatting](#install-client-and-api)\n"
                "[Setext](#setext-heading)\n[Quote](#quoted-heading)\n[Nested](#nested-heading)\n"
                "[Other document](docs/guide.md#foo)\n"
            ),
            "docs/guide.md": "## Foo\n",
        })
        report = self.run_checker(fixture)
        self.assertEqual(10, report["counts"]["validated_fragments"])
        with (fixture.path / "README.md").open("a", encoding="utf-8") as source:
            source.write("\n[No fourth](#foo-3)\n[No second elsewhere](docs/guide.md#foo-1)\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.anchor", "README.md", "#foo-3")
        self.assert_finding(report, "link.anchor", "README.md", "docs/guide.md#foo-1")

    def test_pinned_unicode_slug_rules_keep_letters_and_connectors_not_numbers_or_emoji(self):
        # Literal oracle values independently executed from Flet/github-slugger
        # commit 3461c4350868329c8530904d170358bca1d31448, not checker helpers.
        source = (
            "## x²\n\n## a‿b\n\n## Ⓐ\n\n## 𐐀\n\n## x😀y\n\n"
            "[Superscript removed](#x)\n[Connector retained](#a‿b)\n"
            "[Circled letter retained](#ⓐ)\n[Deseret lowercase](#𐐨)\n"
            "[Astral emoji removed](#xy)\n"
        )
        fixture = self.fixture({"README.md": source})
        report = self.run_checker(fixture)
        self.assertEqual(5, report["counts"]["validated_fragments"])
        wrong = ("#x²", "#ab", "#a", "#𐐀", "#x😀y")
        fixture.write("README.md", source + "\n" + "\n".join(f"[Invalid]({fragment})" for fragment in wrong) + "\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        actual = {
            unquote(finding["target"]) for finding in report["findings"]
            if finding["rule"] == "link.anchor" and finding["path"] == "README.md"
        }
        self.assertEqual(set(wrong), actual)

    def test_runtime_unassigned_heading_cannot_validate_a_guessed_anchor(self):
        fixture = self.fixture({
            "README.md": "## Prefix \u0264\n\n[Supported letter](#prefix-\u0264)\n",
        })
        positive = self.run_checker(fixture)
        self.assertEqual(1, positive["counts"]["validated_fragments"])
        # U+A7CB is unassigned in the pinned Python 3.12 Unicode database.
        # Dropping it would falsely accept this guessed anchor; newer casing
        # maps it to the supported letter used by the positive control above.
        fixture.write("README.md", "## Prefix \uA7CB\n\n[Old runtime guess](#prefix-)\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.unsupported", "README.md")
        self.assertEqual(0, report["counts"]["validated_fragments"])

    def test_actual_html_links_images_and_anchors_are_inspected(self):
        source = (
            '# Entry\n\n<a href="docs/guide.md#section">Guide</a>\n'
            '<img src="assets/pixel.png" alt="Fixture">\n'
            '<a name="manual"></a>\n<span id="custom-id"></span>\n\n'
            '[Manual](#manual)\n[Id](#custom-id)\n\n'
            '<!-- <a href="missing-comment.md">Not rendered</a> -->\n'
        )
        fixture = self.fixture({
            "README.md": source, "docs/guide.md": "## Section\n",
            "assets/pixel.png": b"image fixture",
        })
        report = self.run_checker(fixture)
        self.assertEqual(4, report["counts"]["local_links"])
        self.assertEqual(3, report["counts"]["validated_fragments"])
        fixture.write("README.md", source.replace("docs/guide.md#section", "missing-guide.md").replace("assets/pixel.png", "missing-image.png"))
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.missing", "README.md", "missing-guide.md")
        self.assert_finding(report, "link.missing", "README.md", "missing-image.png")

    def test_paths_decode_once_and_support_spaces_parentheses_and_fragments(self):
        fixture = self.fixture({
            "README.md": (
                "# Entry\n\n[Space](<docs/a b.md#section>)\n"
                "[Parentheses](docs/a(b).md#section)\n"
                "[Literal percent](docs/a%252Fb.md#section)\n"
                "[Hash in name](docs/hash%23tag.md#section)\n"
                "[Query](docs/a(b).md?raw=true#section)\n"
            ),
            "docs/a b.md": "## Section\n",
            "docs/a(b).md": "## Section\n",
            "docs/a%2Fb.md": "## Section\n",
            "docs/hash#tag.md": "## Section\n",
        })
        report = self.run_checker(fixture)
        self.assertEqual(5, report["counts"]["validated_fragments"])
        fixture.write("README.md", "# Entry\n\n[Not a literal percent](docs/a%2Fb.md)\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.missing", "README.md", "docs/a%2Fb.md")

    def test_wrong_case_fails_against_git_inventory(self):
        fixture = self.fixture()
        fixture.write("README.md", "# Fixture\n\n[Wrong case](docs/Guide.md)\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.missing", "README.md", "docs/Guide.md")

    def test_literal_and_encoded_path_escape_fail(self):
        (self.temporary / "outside.md").write_text("# Outside\n", encoding="utf-8")
        for destination in ("../outside.md", "%2e%2e/outside.md"):
            with self.subTest(destination=destination):
                fixture = self.fixture({"README.md": f"# Entry\n\n[Escape]({destination})\n"})
                report = self.run_checker(fixture, expected_exit=1)
                self.assert_finding(report, "link.path", "README.md")

    def test_invalid_encoded_bytes_and_backslashes_are_not_local_paths(self):
        for destination in ("docs/%FF.md", "docs/guide.md%00", "docs%5Cguide.md"):
            with self.subTest(destination=destination):
                fixture = self.fixture({
                    "README.md": f"# Entry\n\n[Invalid]({destination})\n",
                    "docs/guide.md": "# Guide\n",
                })
                report = self.run_checker(fixture, expected_exit=1)
                self.assert_finding(report, "link.path", "README.md")

    def test_tracked_directories_resolve_and_non_markdown_fragments_are_explicit(self):
        fixture = self.fixture({
            "README.md": "# Entry\n\n[Directory](docs/)\n[Root relative](/docs)\n",
            "docs/member.txt": "Tracked directory member.\n",
        })
        report = self.run_checker(fixture)
        self.assertEqual(2, report["counts"]["local_links"])
        fixture.write("README.md", "# Entry\n\n[Unsupported line anchor](docs/member.txt#L1)\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.unsupported", "README.md", "docs/member.txt#L1")

    def test_symlink_target_is_explicitly_unsupported(self):
        outside = self.temporary / "outside.bin"
        outside.write_bytes(b"outside fixture")
        fixture = self.fixture({"README.md": "# Entry\n"})
        (fixture.path / "linked.bin").symlink_to(outside)
        fixture.write("README.md", "# Entry\n\n[Symlink](linked.bin)\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.unsupported", "README.md", "linked.bin")
        self.assertEqual(b"outside fixture", outside.read_bytes())

    def test_untracked_files_and_directories_cannot_rescue_links(self):
        fixture = self.fixture({"README.md": "# Entry\n\n[File](untracked.md)\n[Directory](untracked-dir/)\n"})
        fixture.write("untracked.md", "# Untracked\n")
        fixture.write("untracked-dir/member.txt", "Untracked\n")
        before = fixture.git("status", "--porcelain=v1", "--untracked-files=all")
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.missing", "README.md", "untracked.md")
        self.assert_finding(report, "link.missing", "README.md", "untracked-dir/")
        self.assertEqual(before, fixture.git("status", "--porcelain=v1", "--untracked-files=all"))

    def test_worktree_edits_do_not_replace_committed_source(self):
        fixture = self.fixture()
        fixture.write("README.md", "# Dirty\n\n[Broken](does-not-exist.md)\n")
        (fixture.path / "docs/guide.md").unlink()
        before = fixture.git("status", "--porcelain=v1")
        readme_before = (fixture.path / "README.md").read_bytes()
        index_before = (fixture.path / ".git/index").read_bytes()
        report = self.run_checker(fixture, ref=fixture.baseline)
        self.assertEqual(2, report["counts"]["markdown_files"])
        self.assertEqual(1, report["counts"]["validated_fragments"])
        self.assertEqual(readme_before, (fixture.path / "README.md").read_bytes())
        self.assertEqual(index_before, (fixture.path / ".git/index").read_bytes())
        self.assertEqual(fixture.baseline, fixture.git("rev-parse", "HEAD"))
        self.assertEqual(before, fixture.git("status", "--porcelain=v1"))
        self.assertFalse((fixture.path / "docs/guide.md").exists())

    def test_newer_commit_cannot_rescue_a_requested_older_tree(self):
        fixture = self.fixture({"README.md": "# Entry\n\n[Missing at baseline](later.txt)\n"})
        fixture.write("later.txt", "Created only in another source commit.\n")
        fixture.commit()
        report = self.run_checker(fixture, ref=fixture.baseline, expected_exit=1)
        self.assert_finding(report, "link.missing", "README.md", "later.txt")

    def test_unchanged_inbound_document_fails_after_target_heading_edit(self):
        fixture = self.fixture()
        self.run_checker(fixture)
        original_owner = fixture.git("rev-parse", f"{fixture.baseline}:README.md")
        fixture.write("docs/guide.md", "# Guide\n\n## Renamed\n")
        candidate = fixture.commit()
        self.assertEqual(original_owner, fixture.git("rev-parse", f"{candidate}:README.md"))
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.anchor", "README.md", "docs/guide.md#section")

    def test_ordinary_move_with_scope_refresh_still_checks_unchanged_inbound_links(self):
        fixture = self.fixture()
        (fixture.path / "docs/guide.md").rename(fixture.path / "docs/moved.md")
        moved = fixture.commit("Move ordinary document")
        fixture.record("docs/guide.md")["path"] = "docs/moved.md"
        fixture.set_scope(moved, ["README.md", "docs/moved.md"])
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.missing", "README.md", "docs/guide.md#section")
        self.assertFalse(any(item["rule"].startswith("catalogue.") for item in report["findings"]))
        fixture.write("README.md", "# Fixture\n\n[Guide](docs/moved.md#section)\n")
        fixture.commit()
        self.run_checker(fixture)

    def test_existing_broken_links_are_not_suppressed_by_the_baseline(self):
        fixture = self.fixture({"README.md": "# Historical\n\n[Retained failure](missing.md)\n"})
        fixture.write("ordinary.txt", "An unrelated candidate addition.\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "link.missing", "README.md", "missing.md")
        self.assertEqual(1, report["counts"]["local_links"])

    def test_links_mode_does_not_claim_catalogue_or_protection_validation(self):
        fixture = self.fixture(catalogue=False)
        report = self.run_checker(fixture, mode="links")
        self.assertEqual(2, report["counts"]["markdown_files"])
        self.assertEqual(1, report["counts"]["validated_fragments"])
        for key in ("catalogue_entries", "asset_references", "protected_documents", "immutable_files"):
            self.assertEqual(0, report["counts"][key], key)

    def test_declared_markdown_addition_has_exact_catalogue_coverage(self):
        fixture = self.fixture()
        fixture.write("docs/new.md", "# New document\n")
        fixture.catalogue["documents"].append(fixture.document("docs/new.md"))
        fixture.set_scope(fixture.seed, ["README.md", "docs/guide.md"], ["docs/new.md"])
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture)
        self.assertEqual(3, report["counts"]["markdown_files"])
        self.assertEqual(3, report["counts"]["catalogue_entries"])

    def test_missing_catalogue_entry_fails_even_when_addition_is_declared(self):
        fixture = self.fixture()
        fixture.write("docs/new.md", "# New document\n")
        fixture.set_scope(fixture.seed, ["README.md", "docs/guide.md"], ["docs/new.md"])
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "catalogue.coverage", CATALOGUE, "docs/new.md")

    def test_extra_or_wrong_case_catalogue_entry_fails(self):
        for path in ("docs/absent.md", "docs/Guide.md"):
            with self.subTest(path=path):
                fixture = self.fixture()
                fixture.catalogue["documents"].append(fixture.document(path))
                fixture.catalogue["documents"].sort(key=lambda record: record["path"])
                fixture.save_catalogue()
                fixture.commit()
                report = self.run_checker(fixture, expected_exit=1)
                self.assert_finding(report, "catalogue.coverage", CATALOGUE, path)

    def test_duplicate_catalogue_entries_cannot_collapse_into_one(self):
        fixture = self.fixture()
        fixture.catalogue["documents"].insert(1, copy.deepcopy(fixture.record("README.md")))
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "catalogue.schema", CATALOGUE)

    def test_catalogue_schema_rejects_invalid_types_values_and_unknown_fields(self):
        cases = [
            ("kind", "unestablished-role"), ("lifecycle", "approved"),
            ("classification_basis", " "), ("domain", False),
            ("integrity", ["movable"]), ("unexpected_field", "unrecognized"),
            ("related_assets", ["asset.json", "asset.json"]),
        ]
        for field, value in cases:
            with self.subTest(field=field):
                fixture = self.fixture()
                fixture.record("README.md")[field] = value
                fixture.save_catalogue()
                fixture.commit()
                report = self.run_checker(fixture, expected_exit=1)
                self.assert_finding(report, "catalogue.schema", CATALOGUE)

    def test_duplicate_yaml_keys_are_rejected_instead_of_last_value_winning(self):
        fixture = self.fixture()
        fixture.write(CATALOGUE, 'version: 2\nversion: 1\nscope: {}\ndocuments: []\n')
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=2)
        self.assert_finding(report, "input.catalogue", CATALOGUE)

    def test_yaml_aliases_are_not_silently_expanded_into_catalogue_records(self):
        fixture = self.fixture()
        fixture.write(CATALOGUE, (
            "version: 1\nscope: " + json.dumps(fixture.catalogue["scope"]) + "\n"
            "documents:\n- &document " + json.dumps(fixture.record("README.md")) + "\n- *document\n"
        ))
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=2)
        self.assert_finding(report, "input.catalogue", CATALOGUE)

    def test_scope_rejects_wrong_tree_and_stale_counts(self):
        for field, value in (("base_tree", "0" * 40), ("expected_markdown_count", 99), ("tracked_markdown_at_base", 99)):
            with self.subTest(field=field):
                fixture = self.fixture()
                fixture.catalogue["scope"][field] = value
                fixture.save_catalogue()
                fixture.commit()
                report = self.run_checker(fixture, expected_exit=1)
                self.assert_finding(report, "catalogue.scope", CATALOGUE)

    def test_v1_scope_cannot_hide_a_removal_by_only_changing_current_entries(self):
        fixture = self.fixture({"README.md": "# Entry\n", "docs/guide.md": "# Guide\n"})
        (fixture.path / "docs/guide.md").unlink()
        fixture.catalogue["documents"] = [fixture.record("README.md")]
        fixture.catalogue["scope"]["expected_markdown_count"] = 1
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "catalogue.scope", CATALOGUE)

    def test_all_declared_companion_assets_are_checked(self):
        assets = ["assets/review.json", "assets/run.log", "assets/picture.png", "assets/archive.zip"]
        fixture = self.fixture({
            "README.md": "# Entry\n",
            **{name: b"independent companion fixture" for name in assets},
        }, {"README.md": {"related_assets": assets}})
        report = self.run_checker(fixture)
        self.assertEqual(4, report["counts"]["asset_references"])
        self.assertEqual(4, report["counts"]["unique_assets"])
        for name in assets:
            (fixture.path / name).unlink()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        for name in assets:
            self.assert_finding(report, "asset.missing", "README.md", name)

    def test_asset_case_and_untracked_disk_copy_cannot_rescue_companions(self):
        fixture = self.fixture({"README.md": "# Entry\n", "assets/Review.json": "{}\n"}, {
            "README.md": {"related_assets": ["assets/review.json"]},
        })
        fixture.write("assets/review.json", "{}\n")
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "asset.missing", "README.md", "assets/review.json")

    def test_fixed_path_prose_edit_is_allowed(self):
        fixture = self.fixture({"README.md": "# Original\n"}, {
            "README.md": {"integrity": ["fixed-path-consumer"]},
        })
        fixture.write("README.md", "# Updated\n\nOrdinary prose at the retained discovery path.\n")
        fixture.commit()
        report = self.run_checker(fixture)
        self.assertEqual(1, report["counts"]["protected_documents"])
        self.assertEqual(0, report["counts"]["immutable_files"])

    def test_fixed_path_removal_survives_candidate_entry_and_scope_refresh(self):
        fixture = self.fixture({"README.md": "# Entry\n", "docs/fixed.md": "# Fixed\n"}, {
            "docs/fixed.md": {"integrity": ["fixed-path-consumer"]},
        })
        (fixture.path / "docs/fixed.md").unlink()
        removed = fixture.commit("Remove fixed source")
        fixture.catalogue["documents"] = [fixture.record("README.md")]
        fixture.set_scope(removed, ["README.md"])
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "protection.fixed_path", "docs/fixed.md", "docs/fixed.md")

    def test_immutable_bytes_and_committed_line_endings_are_preserved(self):
        for mutation in (b"# Evidence\n\nRewritten.\n", b"# Evidence\r\n"):
            with self.subTest(mutation=mutation):
                fixture = self.fixture({"README.md": b"# Evidence\n"}, {
                    "README.md": {"integrity": ["immutable-evidence"]},
                })
                self.run_checker(fixture)
                fixture.write("README.md", mutation)
                fixture.commit()
                report = self.run_checker(fixture, expected_exit=1)
                self.assert_finding(report, "protection.immutable", "README.md", "README.md")

    def test_immutable_git_mode_is_preserved_when_bytes_do_not_change(self):
        fixture = self.fixture({"README.md": "# Evidence\n"}, {
            "README.md": {"integrity": ["immutable-evidence"]},
        })
        (fixture.path / "README.md").chmod(0o755)
        candidate = fixture.commit()
        self.assertTrue(fixture.git("ls-tree", candidate, "README.md").startswith("100755 "))
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "protection.immutable", "README.md", "README.md")

    def test_removing_integrity_flag_does_not_authorize_immutable_rewrite(self):
        fixture = self.fixture({"README.md": "# Evidence\n"}, {
            "README.md": {"integrity": ["fixed-path-consumer", "immutable-evidence"]},
        })
        fixture.record("README.md")["integrity"] = ["fixed-path-consumer"]
        fixture.write("README.md", "# Replacement evidence\n")
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "protection.removed", "README.md")
        self.assert_finding(report, "protection.immutable", "README.md", "README.md")

    def test_removing_protected_document_and_its_catalogue_entry_still_fails(self):
        fixture = self.fixture({"README.md": "# Entry\n", "docs/evidence.md": "# Evidence\n"}, {
            "docs/evidence.md": {"integrity": ["immutable-evidence"]},
        })
        (fixture.path / "docs/evidence.md").unlink()
        removed = fixture.commit("Remove immutable subject")
        fixture.catalogue["documents"] = [fixture.record("README.md")]
        fixture.set_scope(removed, ["README.md"])
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "protection.immutable", "docs/evidence.md", "docs/evidence.md")

    def test_removed_asset_metadata_cannot_hide_any_immutable_companion_change(self):
        assets = ["assets/review.json", "assets/run.log", "assets/picture.png", "assets/archive.zip"]
        fixture = self.fixture({"README.md": "# Evidence\n", **{name: b"retained" for name in assets}}, {
            "README.md": {"integrity": ["immutable-evidence"], "related_assets": assets},
        })
        positive = self.run_checker(fixture)
        self.assertEqual(5, positive["counts"]["immutable_files"])
        fixture.record("README.md").pop("related_assets")
        for name in assets:
            fixture.write(name, b"rewritten")
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assert_finding(report, "protection.binding", "README.md")
        for name in assets:
            self.assert_finding(report, "protection.immutable", name, name)

    def test_campaign_bundle_protects_unlisted_members_and_membership(self):
        directory = "performance/baseline-campaigns/evidence/fixture"
        subject = f"{directory}/README.md"
        removed = f"{directory}/raw.json"
        changed = f"{directory}/screenshots/view.png"
        added = f"{directory}/replacement.log"
        fixture = self.fixture({
            subject: "# Retained campaign\n", removed: b"original JSON",
            changed: b"original image", f"{directory}/archive.zip": b"original archive",
        }, {subject: {"integrity": ["immutable-evidence"]}})
        report = self.run_checker(fixture)
        self.assertEqual(4, report["counts"]["immutable_files"])
        (fixture.path / removed).unlink()
        fixture.write(changed, b"different image")
        fixture.write(added, "Additional evidence membership.\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        for name in (removed, changed, added):
            self.assert_finding(report, "protection.immutable", name, name)

    def test_coordinated_subject_and_manifest_rewrite_cannot_replace_baseline(self):
        subject = "docs/subject.md"
        manifest = "assets/review.json"
        original = b"# Subject\n\nOwner decision remains required.\n"

        def review(content):
            return json.dumps({
                "subject": {"path": subject, "sha256": hashlib.sha256(content).hexdigest()},
                "decision": "OWNER_DECISION_REQUIRED", "approval": None,
            }) + "\n"

        fixture = self.fixture({subject: original, manifest: review(original)}, {
            subject: {"integrity": ["fixed-path-consumer", "immutable-evidence"], "related_assets": [manifest]},
        })
        self.run_checker(fixture)
        replacement = b"# Subject\n\nReplacement review subject.\n"
        fixture.write(subject, replacement)
        fixture.write(manifest, review(replacement))
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        for name in (subject, manifest):
            self.assert_finding(report, "protection.immutable", name, name)

    def test_candidate_scope_and_source_ref_rewrite_do_not_replace_trusted_baseline(self):
        fixture = self.fixture({"README.md": "# Retained evidence\n"}, {
            "README.md": {"integrity": ["immutable-evidence"], "source_ref": "decision: ORIGINAL"},
        })
        fixture.write("README.md", "# Replacement evidence\n")
        rewritten = fixture.commit("Create candidate replacement")
        fixture.set_scope(rewritten, ["README.md"])
        fixture.record("README.md")["source_ref"] = "decision: REPLACEMENT"
        fixture.save_catalogue()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=1)
        self.assertEqual(fixture.baseline, report["baseline"]["commit"])
        self.assert_finding(report, "protection.immutable", "README.md", "README.md")
        self.assert_finding(report, "protection.binding", "README.md")

    def test_generated_view_change_fails_even_after_removing_generated_flag(self):
        for remove_flag in (False, True):
            with self.subTest(remove_flag=remove_flag):
                fixture = self.fixture({"README.md": "# Generated view\n"}, {
                    "README.md": {"kind": "generated-view", "integrity": ["generated"]},
                })
                self.run_checker(fixture)
                fixture.write("README.md", "# Hand-edited view\n")
                if remove_flag:
                    fixture.record("README.md").pop("integrity")
                    fixture.save_catalogue()
                fixture.commit()
                report = self.run_checker(fixture, expected_exit=1)
                self.assert_finding(report, "protection.generated", "README.md")

    def test_check_requires_catalogue_in_candidate_and_trusted_baseline(self):
        fixture = self.fixture()
        report = self.run_checker(fixture, baseline=fixture.seed, expected_exit=2)
        self.assert_finding(report, "input.catalogue", CATALOGUE)
        (fixture.path / CATALOGUE).unlink()
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=2)
        self.assert_finding(report, "input.catalogue", CATALOGUE)

    def test_unknown_source_or_baseline_is_an_input_error(self):
        fixture = self.fixture()
        for arguments in ({"ref": "f" * 40}, {"baseline": "f" * 40}):
            with self.subTest(arguments=arguments):
                report = self.run_checker(fixture, expected_exit=2, **arguments)
                self.assertTrue(any(item["rule"] == "input.git" for item in report["findings"]))

    def test_nonancestor_baseline_is_rejected(self):
        fixture = self.fixture()
        candidate = fixture.baseline
        fixture.git("checkout", "--detach", fixture.seed)
        fixture.save_catalogue()
        fixture.write("sibling.txt", "A sibling history must not become the trusted base.\n")
        sibling = fixture.commit("Create sibling baseline")
        fixture.git("checkout", "--detach", candidate)
        report = self.run_checker(fixture, baseline=sibling, expected_exit=2)
        self.assertTrue(any(item["rule"] == "input.git" for item in report["findings"]))

    def test_invalid_utf8_is_a_visible_input_error(self):
        fixture = self.fixture()
        fixture.write("docs/guide.md", b"# Invalid\n\xff\xfe\n")
        fixture.commit()
        report = self.run_checker(fixture, expected_exit=2)
        self.assert_finding(report, "input.encoding", "docs/guide.md")


if __name__ == "__main__":
    unittest.main()
