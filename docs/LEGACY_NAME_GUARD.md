# Coglatas naming policy enforcement

`LegacyNameGuard` runs on every pull request, push and merge queue event. Run it
locally with `python scripts/ci/check-coglatas-rename.py`; run the regression suite
with `python scripts/ci/test-coglatas-rename.py`.

The policy expression `[Aa][Ii][Pp]|[Nn][Yy][Gg]` uses explicit ASCII case
classes to detect retired product initials and every longer identifier containing
them. Each character class implements case matching directly. The expression is
readable policy syntax, with no encoding or assembled product names. The guard,
its tests and this document are scanned by the same policy as other files.

The inventory includes all tracked and nonignored untracked checkout files,
including hidden files. Deleted files and Git history are outside the current
checkout. Ignored dependency installations and build output are outside the Git
source inventory. Text is checked as UTF-8 or BOM-marked UTF-16/UTF-32. Recognized
binary assets receive path checks and are counted in the summary;
their visible pixels require a separate visual audit. Unsupported text encodings
or unknown binary formats fail closed rather than bypassing content checks.

ZIP source archives receive member-path and text-content checks without extracting
files. Nested ZIPs are checked too. A patent-like member directory inside an
ordinary archive has no exemption; only the outer checkout path can qualify for
the specification's exact legal exclusion. Unsafe member paths, unreadable
archives and unsupported text encodings fail. Archives exceeding four levels,
128 MiB total uncompressed size per level or 16 MiB per member also fail closed.

This implementation repository has **no patent-directory exclusion**. The
specification repository's policy excludes only `docs/legal/patents/` and its
descendants. Archive, evidence, migration, log and ordinary specification files
have no exemptions.

Content matches are classified only for the exact real external GitHub owner
account in explicit identity contexts and, within a valid npm lockfile, one exact
unrelated transitive dependency in dependency keys, package paths and registry
URLs, plus valid Subresource Integrity fields on dependency records resolved from
the npm registry. Arbitrary digest-shaped metadata is not classified. Digest
algorithm, Base64 syntax and decoded byte length must all be valid. These
classifications do not exempt files or lines: another retired name on the same
line still fails. Local paths always
receive the full check. External matches are counted separately because a raw
substring search also finds these unrelated values; no dependency integrity or
ownership value is rewritten to reduce that search count.

Regression fixtures are derived from the case classes as ordinary example text
and written into temporary Git repositories outside this checkout. Tests verify
failure exit codes, case variants, longer identifiers, hidden and untracked files,
path and directory names, deletion handling, exact patent-directory boundaries,
the absence of an implementation patent exception, digest classification and the
guard's own source and archived source. There is no broad allowlist or
guard-file exclusion.
