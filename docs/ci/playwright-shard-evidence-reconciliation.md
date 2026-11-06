# Playwright two-shard evidence reconciliation — static trial

This experiment does not start a shard or replace protected `frontend-test`. 
The immutable Angular build producer and the existing full test/check execution
remain unchanged. It tests a future shard receipt/manifest data contract.

An independent source-and-producer-bound roster supplies the exact set of
privacy-safe SHA256 test identities and expected PASS/SKIP statuses. Both
shard receipts must have the same exact tested merged SHA, run ID, attempt,
immutable Angular artifact ID and roster digest. Shards 1/2 and 2/2 must
contain nonempty strictly ordered IDs with disjoint complete union. Unknown,
repeated, missing, skipped-drift, failed, interrupted or retried cases
are rejected, not counted as a green required gate. Output contains only
counts, hashes and identity metadata, never paths, names, HTTP data or secrets.

The manifest verifier intentionally reports `EXTERNAL_VERIFICATION_REQUIRED`
because JSON metadata does not prove that a GitHub artifact ID exists or belongs
to a successful same-run producer. A future CI gate must independently query
the GitHub artifact/run/job APIs and verify uploaded bytes before publishing a
protected `frontend-test` PASS. The complete original keyboard/search evidence
retention and all required checks remain unchanged.

No speedup or sharding activation is claimed. Only `SecurityTestImplimation`
may be modified, never Main.

The fixed aggregate output is also opened relative to no-follow directory file
descriptors for both `artifacts/` and `artifacts/ci/`, refusing parent
symlinks and an existing output file. This is Linux-only preparatory CI
behavior; no shard execution or publication is activated.

Input roster/shard receipt files now use no-follow directory descriptors for
**every path component**, not only the final filename. They reject
symbolic-link ancestors, hardlinked evidence, `..` traversal, non-finite JSON,
and same-file size/mtime/ctime drift while reading. These are fail-closed
Linux CI controls. They still **do not** independently prove which GitHub
workflow/job uploaded the bytes: `requiredCheckQualified=false` and
`EXTERNAL_VERIFICATION_REQUIRED` remain mandatory.
# CLI privacy qualification after current Security reconciliation

The existing PR #1680 was reconciled locally with Security head
`3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`; no Main changes were imported.
Its original twelve Linux controls passed, but a new actual-CLI regression
exposed a separate argument-parser boundary: default argparse could echo a
rejected value and mixed `--help`/invalid arguments could exit successfully
before validation. The initial focused regression failed seven assertions;
that result remains a failure of the old parser, not shard execution evidence.

The parser now emits only `INVALID_ARGUMENTS` for argument rejection, accepts
static help only by itself, fixes the help program name and rejects abbreviated
options. Parsing occurs inside the existing finite error handler. Thirteen
Linux tests pass, including actual subprocess checks for private argument
canaries, malformed integers, missing values, unknown/abbreviated options and
mixed help, with no receipt creation. All existing union/schema/file/privacy
controls remain. No browser/shard launcher, artifact upload, trusted-provenance
attestation or protected-check replacement is activated.

Fresh exact-head native CI and independent review remain required. The original
full frontend run stays authoritative; PR #1717 source-only discovery remains
a separate dependency. Rollback through a Security-targeted PR by reverting
this parser/test/documentation repair; retain its original failed regression.

