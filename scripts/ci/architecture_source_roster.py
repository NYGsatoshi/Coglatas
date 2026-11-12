#!/usr/bin/env python3
"""Bounded architecture source-case inventory, not native execution evidence."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import sys

NAMESPACE = 'Coglatas.Architecture.Tests'
PROJECT = Path('tests/Coglatas.Architecture.Tests')
SOURCES = ('LayerDependencyTests.cs', 'ProjectIdeBoundaryTests.cs')
OPTIONAL_NEGATIVE = 'LayerDependencyNegativeControls.cs'
PROFILES = {'CORE': (SOURCES, 12), 'WITH_NEGATIVE_ORACLE': (SOURCES + (OPTIONAL_NEGATIVE,), 13)}
MAX_BYTES = 128 * 1024
MAX_CASES = 64
CLASS = re.compile(r'^public sealed class ([A-Za-z_][A-Za-z0-9_]*)\s*$', re.M)
FACT = re.compile(r'^    \[Fact\]\s*$', re.M)
METHOD = re.compile(r'^    public void ([A-Za-z_][A-Za-z0-9_]*)\(\)\s*$')
CASE_HASH_DOMAIN = b'coglatas.architecture-source-case.v1\x00'


class SourceRosterError(ValueError):
    """A fixed refusal code, never untrusted source data."""


def require(ok: bool, code: str) -> None:
    """Reject unsupported or incomplete bounded source evidence."""
    if not ok:
        raise SourceRosterError(code)


def directory(path: Path) -> None:
    """Require an actual directory rather than a symbolic-link endpoint."""
    try:
        mode = path.lstat().st_mode
    except OSError as exc:
        raise SourceRosterError('SOURCE_DIRECTORY') from exc
    require(stat.S_ISDIR(mode) and not path.is_symlink(), 'SOURCE_DIRECTORY')


def regular_bytes(path: Path) -> bytes:
    """Validate and read a bounded regular source through one stable descriptor."""
    try:
        leaf = path.lstat()
        require(stat.S_ISREG(leaf.st_mode), 'SOURCE_FILE')
        flags = os.O_RDONLY | getattr(os, 'O_NOFOLLOW', 0) | getattr(os, 'O_BINARY', 0)
        with os.fdopen(os.open(path, flags), 'rb') as source:
            info = os.fstat(source.fileno())
            require(stat.S_ISREG(info.st_mode) and os.path.samestat(leaf, info)
                    and 0 < info.st_size <= MAX_BYTES, 'SOURCE_FILE')
            raw = source.read(MAX_BYTES + 1)
            after = os.fstat(source.fileno())
            require(os.path.samestat(info, after) and info.st_size == after.st_size
                    and info.st_mtime_ns == after.st_mtime_ns
                    and info.st_ctime_ns == after.st_ctime_ns, 'SOURCE_FILE')
    except OSError as exc:
        raise SourceRosterError('SOURCE_FILE') from exc
    require(len(raw) == info.st_size and b'\x00' not in raw, 'SOURCE_FILE')
    return raw


def parse_source(raw: bytes, expected_class: str) -> list[str]:
    """Recognize the exact supported C# Fact declaration form; not full Roslyn."""
    try:
        source = raw.decode('utf-8')
    except UnicodeError as exc:
        raise SourceRosterError('SOURCE_ENCODING') from exc
    require('/*' not in source and '*/' not in source and '#if' not in source,
            'UNSUPPORTED_SYNTAX')
    # Match declarations as whole supported C# lines, never as substrings
    # inside comments or identifiers. Reject foreign/duplicate namespaces.
    namespaces = re.findall(r'(?m)^namespace[ \t]+([A-Za-z_][A-Za-z0-9_.]*)[ \t]*;[ \t]*
    classes = CLASS.findall(source)
    require(classes == [expected_class], 'SOURCE_CLASS')
    require('[Theory' not in source and '[InlineData' not in source and '[Fact(' not in source,
            'UNSUPPORTED_SYNTAX')
    facts = list(FACT.finditer(source))
    require(0 < len(facts) <= MAX_CASES and source.count('[Fact]') == len(facts),
            'FACT_DECLARATION')
    lines = source.splitlines()
    names: list[str] = []
    for match in facts:
        # Restrict to one annotated, immediately following zero-argument
        # public void method. Unknown attributes or generated cases fail.
        line_index = source.count('\n', 0, match.start())
        require(line_index + 1 < len(lines), 'FACT_DECLARATION')
        signature = METHOD.fullmatch(lines[line_index + 1])
        require(signature is not None, 'FACT_DECLARATION')
        names.append(NAMESPACE + '.' + expected_class + '.' + signature.group(1))
    require(len(names) == len(set(names)), 'DUPLICATE_CASE')
    return names


def discover(root: Path, profile: str) -> dict:
    """Inventory fixed project files; results never authenticate a Git source SHA."""
    require(type(profile) is str and profile in PROFILES, 'PROFILE')
    directory(root)
    directory(root / 'tests')
    project = root / PROJECT
    directory(project)
    filenames, count = PROFILES[profile]
    seen = sorted(x.name for x in project.glob('*.cs'))
    require(seen == sorted(filenames), 'SOURCE_FILE_SET')
    cases: list[str] = []
    blobs: dict[str, str] = {}
    for filename in filenames:
        raw = regular_bytes(project / filename)
        blobs[filename] = hashlib.sha256(raw).hexdigest()
        cases.extend(parse_source(raw, filename.removesuffix('.cs')))
    require(len(cases) == count and len(set(cases)) == count, 'CASE_COUNT')
    hashes = sorted(hashlib.sha256(CASE_HASH_DOMAIN + case.encode('utf-8')).hexdigest()
                    for case in cases)
    require(len(set(hashes)) == len(hashes), 'CASE_HASH_COLLISION')
    digest = hashlib.sha256(('coglatas.architecture-source-roster.v1\n' +
                             '\n'.join(hashes)).encode('ascii')).hexdigest()
    return {'schema': 'coglatas.architecture-source-roster.v1',
            'profile': profile, 'caseCount': count, 'caseIdHashes': hashes,
            'rosterSha256': digest, 'sourceFileSha256': blobs,
            'sourceEvidence': 'BOUNDED_CSHARP_TEXT_ONLY_NOT_COMPILED_DISCOVERY',
            'nativeCaseIdsVerified': False, 'artifactProvenanceVerified': False,
            'requiredCheckQualified': False}


def main() -> int:
    """Print sanitized source-only JSON or a fixed refusal indicator."""
    p = argparse.ArgumentParser()
    p.add_argument('--root', type=Path, required=True)
    p.add_argument('--profile', choices=tuple(PROFILES), required=True)
    args = p.parse_args()
    try:
        print(json.dumps(discover(args.root, args.profile), sort_keys=True,
                         ensure_ascii=True, separators=(',', ':')))
        return 0
    except (SourceRosterError, OSError, ValueError, RecursionError):
        print('ARCHITECTURE_SOURCE_ROSTER_REFUSED', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
,
                            source)
    require(namespaces == [NAMESPACE] and
            re.search(r'(?m)^using Xunit;[ \t]*
    classes = CLASS.findall(source)
    require(classes == [expected_class], 'SOURCE_CLASS')
    require('[Theory' not in source and '[InlineData' not in source and '[Fact(' not in source,
            'UNSUPPORTED_SYNTAX')
    facts = list(FACT.finditer(source))
    require(0 < len(facts) <= MAX_CASES and source.count('[Fact]') == len(facts),
            'FACT_DECLARATION')
    lines = source.splitlines()
    names: list[str] = []
    for match in facts:
        # Restrict to one annotated, immediately following zero-argument
        # public void method. Unknown attributes or generated cases fail.
        line_index = source.count('\n', 0, match.start())
        require(line_index + 1 < len(lines), 'FACT_DECLARATION')
        signature = METHOD.fullmatch(lines[line_index + 1])
        require(signature is not None, 'FACT_DECLARATION')
        names.append(NAMESPACE + '.' + expected_class + '.' + signature.group(1))
    require(len(names) == len(set(names)), 'DUPLICATE_CASE')
    return names


def discover(root: Path, profile: str) -> dict:
    """Inventory fixed project files; results never authenticate a Git source SHA."""
    require(type(profile) is str and profile in PROFILES, 'PROFILE')
    directory(root)
    directory(root / 'tests')
    project = root / PROJECT
    directory(project)
    filenames, count = PROFILES[profile]
    seen = sorted(x.name for x in project.glob('*.cs'))
    require(seen == sorted(filenames), 'SOURCE_FILE_SET')
    cases: list[str] = []
    blobs: dict[str, str] = {}
    for filename in filenames:
        raw = regular_bytes(project / filename)
        blobs[filename] = hashlib.sha256(raw).hexdigest()
        cases.extend(parse_source(raw, filename.removesuffix('.cs')))
    require(len(cases) == count and len(set(cases)) == count, 'CASE_COUNT')
    hashes = sorted(hashlib.sha256(CASE_HASH_DOMAIN + case.encode('utf-8')).hexdigest()
                    for case in cases)
    require(len(set(hashes)) == len(hashes), 'CASE_HASH_COLLISION')
    digest = hashlib.sha256(('coglatas.architecture-source-roster.v1\n' +
                             '\n'.join(hashes)).encode('ascii')).hexdigest()
    return {'schema': 'coglatas.architecture-source-roster.v1',
            'profile': profile, 'caseCount': count, 'caseIdHashes': hashes,
            'rosterSha256': digest, 'sourceFileSha256': blobs,
            'sourceEvidence': 'BOUNDED_CSHARP_TEXT_ONLY_NOT_COMPILED_DISCOVERY',
            'nativeCaseIdsVerified': False, 'artifactProvenanceVerified': False,
            'requiredCheckQualified': False}


def main() -> int:
    """Print sanitized source-only JSON or a fixed refusal indicator."""
    p = argparse.ArgumentParser()
    p.add_argument('--root', type=Path, required=True)
    p.add_argument('--profile', choices=tuple(PROFILES), required=True)
    args = p.parse_args()
    try:
        print(json.dumps(discover(args.root, args.profile), sort_keys=True,
                         ensure_ascii=True, separators=(',', ':')))
        return 0
    except (SourceRosterError, OSError, ValueError, RecursionError):
        print('ARCHITECTURE_SOURCE_ROSTER_REFUSED', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
, source) is not None,
            'SOURCE_NAMESPACE')
    classes = CLASS.findall(source)
    require(classes == [expected_class], 'SOURCE_CLASS')
    require('[Theory' not in source and '[InlineData' not in source and '[Fact(' not in source,
            'UNSUPPORTED_SYNTAX')
    facts = list(FACT.finditer(source))
    require(0 < len(facts) <= MAX_CASES and source.count('[Fact]') == len(facts),
            'FACT_DECLARATION')
    lines = source.splitlines()
    names: list[str] = []
    for match in facts:
        # Restrict to one annotated, immediately following zero-argument
        # public void method. Unknown attributes or generated cases fail.
        line_index = source.count('\n', 0, match.start())
        require(line_index + 1 < len(lines), 'FACT_DECLARATION')
        signature = METHOD.fullmatch(lines[line_index + 1])
        require(signature is not None, 'FACT_DECLARATION')
        names.append(NAMESPACE + '.' + expected_class + '.' + signature.group(1))
    require(len(names) == len(set(names)), 'DUPLICATE_CASE')
    return names


def discover(root: Path, profile: str) -> dict:
    """Inventory fixed project files; results never authenticate a Git source SHA."""
    require(type(profile) is str and profile in PROFILES, 'PROFILE')
    directory(root)
    directory(root / 'tests')
    project = root / PROJECT
    directory(project)
    filenames, count = PROFILES[profile]
    seen = sorted(x.name for x in project.glob('*.cs'))
    require(seen == sorted(filenames), 'SOURCE_FILE_SET')
    cases: list[str] = []
    blobs: dict[str, str] = {}
    for filename in filenames:
        raw = regular_bytes(project / filename)
        blobs[filename] = hashlib.sha256(raw).hexdigest()
        cases.extend(parse_source(raw, filename.removesuffix('.cs')))
    require(len(cases) == count and len(set(cases)) == count, 'CASE_COUNT')
    hashes = sorted(hashlib.sha256(CASE_HASH_DOMAIN + case.encode('utf-8')).hexdigest()
                    for case in cases)
    require(len(set(hashes)) == len(hashes), 'CASE_HASH_COLLISION')
    digest = hashlib.sha256(('coglatas.architecture-source-roster.v1\n' +
                             '\n'.join(hashes)).encode('ascii')).hexdigest()
    return {'schema': 'coglatas.architecture-source-roster.v1',
            'profile': profile, 'caseCount': count, 'caseIdHashes': hashes,
            'rosterSha256': digest, 'sourceFileSha256': blobs,
            'sourceEvidence': 'BOUNDED_CSHARP_TEXT_ONLY_NOT_COMPILED_DISCOVERY',
            'nativeCaseIdsVerified': False, 'artifactProvenanceVerified': False,
            'requiredCheckQualified': False}


def main() -> int:
    """Print sanitized source-only JSON or a fixed refusal indicator."""
    p = argparse.ArgumentParser()
    p.add_argument('--root', type=Path, required=True)
    p.add_argument('--profile', choices=tuple(PROFILES), required=True)
    args = p.parse_args()
    try:
        print(json.dumps(discover(args.root, args.profile), sort_keys=True,
                         ensure_ascii=True, separators=(',', ':')))
        return 0
    except (SourceRosterError, OSError, ValueError, RecursionError):
        print('ARCHITECTURE_SOURCE_ROSTER_REFUSED', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
