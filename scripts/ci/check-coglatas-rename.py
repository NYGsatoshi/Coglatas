#!/usr/bin/env python3
"""Audit current Git checkout paths and text for retired Coglatas names."""
from __future__ import annotations

import argparse
import base64
import binascii
import io
import json
import os
import re
import subprocess
import sys
import unicodedata
import zipfile
from pathlib import Path, PurePosixPath
from typing import NamedTuple

ROOT = Path(__file__).resolve().parents[2]
PATENT_DIRECTORY: str | None = None

# Explicit ASCII case classes implement case-insensitive policy without storing
# retired names as product prose. These are ordinary, reviewable regex patterns;
# no encoding, string assembly, or exception for the guard itself is involved.
RETIRED_NAMES = re.compile(r"[Aa][Ii][Pp]|[Nn][Yy][Gg]")

# This exact external account is a real GitHub owner, not a product identifier.
# Delimiters prevent its prefix from exempting newly introduced internal names.
EXTERNAL_OWNER = re.compile(
    r"(?<![A-Za-z0-9_-])[Nn][Yy][Gg][Ss][Aa][Tt][Oo][Ss][Hh][Ii](?![A-Za-z0-9_-])"
)
# A transitive npm dependency has an incidental substring match. Only its exact
# token in a valid npm lockfile is classified as a third-party dependency.
EXTERNAL_PACKAGE = re.compile(
    r"(?<![A-Za-z0-9_])[Tt][Ii][Nn][Yy][Gg][Ll][Oo][Bb][Bb][Yy](?![A-Za-z0-9_])"
)
INTEGRITY_TOKEN = re.compile(
    r"(?P<algorithm>sha(?:1|256|384|512))-(?P<digest>[A-Za-z0-9+/]+={0,2})"
)
DIGEST_LENGTHS = {"sha1": 20, "sha256": 32, "sha384": 48, "sha512": 64}

# Common Greek/Cyrillic lookalikes for the policy's Latin letters. Compatibility
# alphabets (fullwidth, mathematical, circled) are handled by decomposition.
LOOKALIKES = str.maketrans({
    "\u0391": "a", "\u03b1": "a", "\u0410": "a", "\u0430": "a",
    "\u0399": "i", "\u03b9": "i", "\u0406": "i", "\u0456": "i",
    "\u03a1": "p", "\u03c1": "p", "\u0420": "p", "\u0440": "p",
    "\u039d": "n", "\u03a5": "y", "\u0423": "y", "\u0443": "y",
    "\u050c": "g", "\u050d": "g",
})


def policy_text(text: str) -> tuple[str, list[int]]:
    """Normalize scan text while retaining original offsets for diagnostics."""
    if text.isascii():
        return text, list(range(len(text)))
    characters: list[str] = []
    origins: list[int] = []
    for offset, character in enumerate(text):
        for value in unicodedata.normalize("NFKD", character).translate(LOOKALIKES).casefold():
            if unicodedata.category(value) in {"Mn", "Mc", "Me", "Cf"}:
                continue
            characters.append(value)
            origins.append(offset)
    return "".join(characters), origins


class Finding(NamedTuple):
    path: str
    kind: str
    line: int = 0
    column: int = 0


class Audit(NamedTuple):
    findings: list[Finding]
    files: int
    binary_files: int
    patent_files: int
    external_matches: int
    archive_members: int


def checkout_files(root: Path) -> list[str]:
    """Include hidden, tracked and nonignored untracked files; never Git history."""
    result = subprocess.run(
        ["git", "-c", f"safe.directory={root.as_posix()}", "ls-files", "-z",
         "--cached", "--others", "--exclude-standard"],
        cwd=root, check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    return sorted({item.decode("utf-8", errors="surrogateescape")
                   for item in result.stdout.split(b"\0") if item})


def is_patent_path(path: str) -> bool:
    """The implementation has no exclusion; the specification has one prefix."""
    return PATENT_DIRECTORY is not None and path.startswith(f"{PATENT_DIRECTORY}/")


def read_text(path: Path) -> str | None:
    # Git stores symlinks as the link target, not the target file's contents.
    if path.is_symlink():
        return os.readlink(path)
    data = path.read_bytes()
    return decode_text(data, path.suffix.lower())


def decode_text(data: bytes, suffix: str) -> str | None:
    if data.startswith((b"\xff\xfe\0\0", b"\0\0\xfe\xff")):
        return data.decode("utf-32")
    if data.startswith((b"\xff\xfe", b"\xfe\xff")):
        return data.decode("utf-16")
    # Only recognized binary formats receive path-only checks. Undecodable text
    # must fail closed rather than silently becoming an unchecked binary file.
    binary = ((suffix == ".png" and data.startswith(b"\x89PNG\r\n\x1a\n"))
              or (suffix == ".ico" and data.startswith(b"\0\0\x01\0"))
              or (suffix == ".avif" and data[4:12] == b"ftypavif")
              or (suffix == ".zip" and data.startswith((b"PK\x03\x04", b"PK\x05\x06")))
              or (suffix == ".pyc" and data[2:4] == b"\r\n"))
    if binary:
        return None
    if b"\0" in data:
        raise UnicodeError("Unrecognized text encoding or binary format")
    return data.decode("utf-8-sig")


def valid_integrity(value: str) -> bool:
    tokens = value.split()
    if not tokens:
        return False
    for token in tokens:
        match = INTEGRITY_TOKEN.fullmatch(token)
        if match is None:
            return False
        try:
            digest = base64.b64decode(match["digest"], validate=True)
        except (ValueError, binascii.Error):
            return False
        if len(digest) != DIGEST_LENGTHS[match["algorithm"]]:
            return False
    return True


class JsonText(NamedTuple):
    path: tuple[str | int, ...]
    value: str
    is_key: bool
    start: int
    end: int


def json_text_atoms(text: str) -> list[JsonText]:
    """Locate JSON string keys and values with their structural context."""
    tokens = list(re.finditer(
        r'"(?:[^"\\]|\\.)*"|[{}\[\]:,]|true|false|null|-?\d+(?:\.\d+)?(?:[Ee][+-]?\d+)?',
        text,
    ))
    atoms: list[JsonText] = []
    cursor = 0

    def visit(path: tuple[str | int, ...]) -> None:
        nonlocal cursor
        token = tokens[cursor]
        cursor += 1
        if token.group() == "{":
            while tokens[cursor].group() != "}":
                key_token = tokens[cursor]
                key = json.loads(key_token.group())
                atoms.append(JsonText((*path, key), key, True, *key_token.span()))
                cursor += 2  # key and colon
                visit((*path, key))
                if tokens[cursor].group() == ",":
                    cursor += 1
            cursor += 1
        elif token.group() == "[":
            index = 0
            while tokens[cursor].group() != "]":
                visit((*path, index))
                index += 1
                if tokens[cursor].group() == ",":
                    cursor += 1
            cursor += 1
        elif token.group().startswith('"'):
            atoms.append(JsonText(path, json.loads(token.group()), False, *token.span()))

    visit(())
    return atoms


def is_dependency_record(path: tuple[str | int, ...]) -> bool:
    if len(path) == 2 and path[0] == "packages":
        return isinstance(path[1], str) and path[1].startswith("node_modules/")
    return (len(path) >= 2 and len(path) % 2 == 0
            and all(path[index] == "dependencies" for index in range(0, len(path), 2))
            and all(isinstance(path[index], str) and path[index] for index in range(1, len(path), 2)))


def external_dependency_record(lockfile: dict, path: tuple[str | int, ...]) -> bool:
    if not is_dependency_record(path):
        return False
    record = lockfile
    for part in path:
        if not isinstance(record, dict) or part not in record:
            return False
        record = record[part]
    return (isinstance(record, dict) and isinstance(record.get("resolved"), str)
            and record["resolved"].startswith("https://registry.npmjs.org/"))


def owner_spans(text: str) -> list[tuple[int, int]]:
    spans = []
    for match in EXTERNAL_OWNER.finditer(text):
        before = text[text.rfind("\n", 0, match.start()) + 1:match.start()]
        newline = text.find("\n", match.end())
        after = text[match.end():newline if newline >= 0 else len(text)]
        identity_field = re.search(
            r"\b(?:account|login|author|owner|reviewer)\b[\s\x60\"':=]*$", before, re.IGNORECASE
        )
        copyright_holder = re.search(r"\bcopyright\b", before, re.IGNORECASE)
        transfer_account = re.search(r"\bGitHub repository transfer\b", before, re.IGNORECASE)
        github_user = re.match(r"[\s\x60\"'()]*GitHub user id\b", after, re.IGNORECASE)
        if (before.endswith("@") or after.startswith("/")
                or identity_field or copyright_holder or transfer_account or github_user):
            spans.append(match.span())
    return spans


def external_spans(path: str, text: str) -> list[tuple[int, int]]:
    spans = owner_spans(text)
    if Path(path).name not in {"package-lock.json", "npm-shrinkwrap.json"}:
        return spans
    try:
        lockfile = json.loads(text)
    except ValueError:
        return spans
    if (not isinstance(lockfile, dict) or type(lockfile.get("lockfileVersion")) is not int
            or lockfile["lockfileVersion"] not in {1, 2, 3}):
        return spans
    dependency_maps = {"dependencies", "devDependencies", "optionalDependencies", "peerDependencies"}
    for atom in json_text_atoms(text):
        package_key = (atom.is_key and len(atom.path) >= 2
                       and atom.path[-2] in dependency_maps
                       and EXTERNAL_PACKAGE.fullmatch(atom.value))
        package_path = (atom.is_key and atom.path[:-1] == ("packages",)
                        and atom.value.startswith("node_modules/")
                        and EXTERNAL_PACKAGE.fullmatch(atom.value.rsplit("/", 1)[-1]))
        registry_url = (not atom.is_key and atom.path[-1] == "resolved"
                        and is_dependency_record(atom.path[:-1])
                        and atom.value.startswith("https://registry.npmjs.org/")
                        and EXTERNAL_PACKAGE.fullmatch(atom.value.split("/")[3]))
        if package_key or package_path or registry_url:
            spans.extend(match.span() for match in EXTERNAL_PACKAGE.finditer(text, atom.start, atom.end))
        if (not atom.is_key and atom.path[-1] == "integrity"
                and external_dependency_record(lockfile, atom.path[:-1]) and valid_integrity(atom.value)):
            spans.append((atom.start, atom.end))
    return spans


def inspect_text(relative: str, text: str) -> tuple[list[Finding], int]:
    findings = []
    external_matches = 0
    approved = external_spans(relative, text)
    normalized, origins = policy_text(text)
    for match in RETIRED_NAMES.finditer(normalized):
        original_start = origins[match.start()]
        original_end = origins[match.end() - 1] + 1
        if any(start <= original_start and original_end <= end for start, end in approved):
            external_matches += 1
            continue
        line = text.count("\n", 0, original_start) + 1
        column = original_start - text.rfind("\n", 0, original_start)
        findings.append(Finding(relative, "content", line, column))
    # Valid JSON can spell letters with escapes. Inspect decoded string atoms
    # too, but report only matches crossing an escape to avoid duplicate raw
    # findings. Exception spans and diagnostics remain anchored to raw text.
    try:
        json.loads(text)
    except ValueError:
        return findings, external_matches
    for atom in json_text_atoms(text):
        raw = text[atom.start + 1:atom.end - 1]
        if "\\" not in raw:
            continue
        decoded_origins: list[int] = []
        decoded_ends: list[int] = []
        for token in re.finditer(r'\\u[dD][89aAbB][0-9a-fA-F]{2}\\u[dD][c-fC-F][0-9a-fA-F]{2}|\\u[0-9a-fA-F]{4}|\\.|[^\\]', raw):
            value = json.loads('"' + token.group() + '"')
            decoded_origins.extend([atom.start + 1 + token.start()] * len(value))
            decoded_ends.extend([atom.start + 1 + token.end()] * len(value))
        normalized, origins = policy_text(atom.value)
        for match in RETIRED_NAMES.finditer(normalized):
            start = decoded_origins[origins[match.start()]]
            end = decoded_ends[origins[match.end() - 1]]
            if "\\" not in text[start:end]:
                continue
            if any(left <= start and end <= right for left, right in approved):
                external_matches += 1
                continue
            line = text.count("\n", 0, start) + 1
            column = start - text.rfind("\n", 0, start)
            findings.append(Finding(relative, "content", line, column))
    return findings, external_matches


def inspect_archive(data: bytes, relative: str, depth: int = 0) -> tuple[list[Finding], int, int, int]:
    """Inspect archive members without extraction or member-directory exclusions."""
    findings: list[Finding] = []
    members = binary_files = external_matches = 0
    if depth >= 4:
        return [Finding(relative, "archive-depth")], 0, 0, 0
    try:
        with zipfile.ZipFile(io.BytesIO(data)) as archive:
            entries = archive.infolist()
            if sum(entry.file_size for entry in entries) > 128 * 1024 * 1024:
                return [Finding(relative, "archive-size")], 0, 0, 0
            for entry in entries:
                member = entry.filename
                member_path = PurePosixPath(member)
                display = f"{relative}!/{member}"
                if (member_path.is_absolute() or ".." in member_path.parts or "\\" in member
                        or re.match(r"^[A-Za-z]:", member)):
                    findings.append(Finding(display, "unsafe-archive-path"))
                    continue
                if RETIRED_NAMES.search(policy_text(member)[0]):
                    findings.append(Finding(display, "path"))
                if entry.is_dir():
                    continue
                members += 1
                if entry.file_size > 16 * 1024 * 1024:
                    findings.append(Finding(display, "archive-member-size"))
                    continue
                content = archive.read(entry)
                if member_path.suffix.lower() == ".zip":
                    nested, count, binaries, external = inspect_archive(content, display, depth + 1)
                    findings.extend(nested)
                    members += count
                    binary_files += binaries
                    external_matches += external
                    continue
                try:
                    text = decode_text(content, member_path.suffix.lower())
                except UnicodeError:
                    findings.append(Finding(display, "unreadable"))
                    continue
                if text is None:
                    binary_files += 1
                    continue
                matches, external = inspect_text(display, text)
                findings.extend(matches)
                external_matches += external
    except (OSError, RuntimeError, ValueError, zipfile.BadZipFile):
        findings.append(Finding(relative, "unreadable-archive"))
    return findings, members, binary_files, external_matches


def audit(root: Path) -> Audit:
    findings: list[Finding] = []
    files = binary_files = patent_files = external_matches = archive_members = 0
    for relative in checkout_files(root):
        path = root / relative
        # Deleted tracked files are absent from the current checkout.
        if not path.exists() and not path.is_symlink():
            continue
        if is_patent_path(relative):
            patent_files += 1
            continue
        files += 1
        # External owner/content classification never exempts a local path.
        if RETIRED_NAMES.search(policy_text(relative)[0]):
            findings.append(Finding(relative, "path"))
        if path.suffix.lower() == ".zip" and not path.is_symlink():
            try:
                matches, count, binaries, external = inspect_archive(path.read_bytes(), relative)
            except OSError:
                findings.append(Finding(relative, "unreadable-archive"))
                continue
            findings.extend(matches)
            archive_members += count
            binary_files += binaries
            external_matches += external
            continue
        try:
            text = read_text(path)
        except (OSError, UnicodeError):
            findings.append(Finding(relative, "unreadable"))
            continue
        if text is None:
            binary_files += 1
            continue
        matches, external = inspect_text(relative, text)
        findings.extend(matches)
        external_matches += external
    return Audit(findings, files, binary_files, patent_files, external_matches, archive_members)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", type=Path, default=ROOT, help="Git checkout root to audit")
    args = parser.parse_args()
    try:
        result = audit(args.root.resolve())
    except (OSError, subprocess.CalledProcessError) as error:
        print(f"LegacyNameGuard could not inventory the checkout: {error}", file=sys.stderr)
        return 2
    print(f"LegacyNameGuard: files={result.files}, binary-path-only={result.binary_files}, "
          f"patent-files={result.patent_files}, classified-external-matches={result.external_matches}, "
          f"archive-members={result.archive_members}")
    if result.findings:
        for finding in result.findings:
            location = f"{finding.path}:{finding.line}:{finding.column}" if finding.line else finding.path
            print(f"FAIL {finding.kind}: {location}")
        print(f"LegacyNameGuard FAIL: {len(result.findings)} finding(s)")
        return 1
    print("LegacyNameGuard PASS: no retired product names in checkout paths or text")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
