# Documentation integrity qualification

DOC-04 provides a read-only check of committed Markdown references, catalogue
coverage, declared companions, and established document protections. The
[checker](../../scripts/ci/documentation_integrity.py) and its
[independent controls](../../tests/ci/test_documentation_integrity.py) belong to
the `SecurityTestImplimation` workstream. They do not change product behavior
or establish implementation, owner approval, external URL availability, or
release readiness.

## Separate checker and documentation sources

The [qualification workflow](../../.github/workflows/documentation-integrity.yml)
checks out two independent sources:

| Source | Selection | Purpose |
| --- | --- | --- |
| Checker | Exact pull-request head, or the triggering commit on push/manual execution | Execute the checker and its independent controls. |
| Documentation corpus | `f849313aecf480948a6fe9176d73a490f2bec93f` | Qualify against the 475-document DOC-03 integration, including its catalogue. |
| Trusted protection baseline | `f849313aecf480948a6fe9176d73a490f2bec93f` | Establish protection obligations independently of candidate metadata. |

The pinned documentation tree is
`9001024215a6cf2dae75123296e9c020c21b538e`. Its
[maintenance contract](https://github.com/NYGsatoshi/Coglatas/blob/f849313aecf480948a6fe9176d73a490f2bec93f/docs/DOCUMENTATION_MAINTENANCE.md)
and [catalogue](https://github.com/NYGsatoshi/Coglatas/blob/f849313aecf480948a6fe9176d73a490f2bec93f/docs/catalog.yml)
remain on the documentation integration branch. The test branch does not
inherit the whole documentation tree or claim that its own inventory is 475.

Qualification runs for changes to this checker, its requirements, tests,
workflow, or guide in pull requests targeting `SecurityTestImplimation` and
pushes to that branch. Manual dispatch is also available where the workflow
exists. It records checker and corpus commits/trees separately. Its corpus
baseline equals the initial pinned source; deliberate candidate mutations are
exercised in independent temporary Git repositories.

This workflow is a focused qualification lane. It does not install a new Main
required check or automatically check every documentation pull request. Later
promotion must carry only the reviewed checker changes and explicitly bind
the intended candidate and trusted comparison base. Existing required-check
identities, routing, and the manual `documentation_ci.yml` workflow retain
their own roles.

## Run locally

The fixed dependency file targets CPython 3.12 on Linux x86_64 and includes the
parser's transitive dependency. Use the committed wheel hashes:

```sh
python3.12 -m venv /tmp/coglatas-documentation-venv
/tmp/coglatas-documentation-venv/bin/python -m pip install \
  --require-hashes --only-binary=:all: \
  -r scripts/ci/documentation-integrity-requirements.txt
/tmp/coglatas-documentation-venv/bin/python -m unittest discover \
  -s tests/ci -p test_documentation_integrity.py -v
```

For a full check, supply a repository containing the complete Git objects for
both the candidate and trusted baseline. Select the baseline through the
reviewed change context; never copy it from the candidate's editable catalogue:

```sh
/tmp/coglatas-documentation-venv/bin/python \
  scripts/ci/documentation_integrity.py check \
  --repo /path/to/documentation-checkout \
  --ref CANDIDATE_COMMIT \
  --baseline TRUSTED_BASELINE_COMMIT \
  --catalog docs/catalog.yml \
  --json /tmp/coglatas-documentation-integrity.json
```

The baseline must be the candidate or one of its ancestors. Both must contain
the catalogue. The checker reads Git blobs and tree modes, including tracked
dot-directory Markdown files. Staged/unstaged edits and untracked files are
outside this committed-ref mode; commit a candidate before asking it to check
those changes. An untracked file cannot satisfy a missing target. Missing Git
objects, invalid input, or an unreadable baseline fail visibly.
Parsing is bounded to 8 MiB per Markdown blob, 4 MiB per catalogue, and 64
catalogue nesting levels; exceeding a bound is an error, not a skipped success.

The test branch has no DOC-03 catalogue. Its separate navigation-only check
therefore uses the explicitly narrower mode:

```sh
/tmp/coglatas-documentation-venv/bin/python \
  scripts/ci/documentation_integrity.py links \
  --repo . --ref HEAD --json /tmp/coglatas-documentation-links.json
```

`links` does not establish catalogue coverage, companion completeness, or
protected-byte retention. Its report identifies that narrower scope.

## Validation contract

### References and headings

The parser uses CommonMark with tables and strikethrough enabled. All scoped
Markdown sources are parsed, including sources whose own blobs
have not changed. This detects an existing incoming link broken by a target
move or heading rename. Inline, image, full/collapsed/shortcut reference, and
fragment-only links use parser nodes. Fenced and indented code, code spans,
escaped examples, comments, and YAML frontmatter do not invent live links.
Unused reference definitions and unresolved bracket text are not rendered
links and do not receive a separate style-lint rule.

Relative paths resolve against the source directory, and root-relative paths
resolve inside the checked repository. Resolution uses exact Git path case,
decodes percent encoding once, and separates queries from fragments. Tracked
directories can be targets. Invalid encodings, repository escapes, ambiguous
backslashes, and unsupported symlink/gitlink targets fail without following
host filesystem paths.

Markdown fragment checks use parsed heading text, including Setext headings,
inline formatting/code, Unicode, and duplicate-heading suffixes. The character
filter is pinned to the attributed Unicode-13 `github-slugger` source embedded
in the checker; casing follows the reported Python Unicode database. Headings
containing characters unassigned in that runtime fail as unsupported, so a
newer renderer's casing rules cannot silently turn a guessed anchor into a
pass. This bounded implementation does not promise identity with every future
GitHub renderer. Automatic heading collisions are global within each document.
Supported explicit HTML anchors remain separate from automatic numbering.

Actual HTML `a[href]`, `img/audio/video/source[src]`, and `video[poster]`
references are inspected, together with parsed HTML `id` and `a[name]`
anchors. This is not a general HTML/CSS/JavaScript dependency crawler.
Non-Markdown fragments
and GitHub cross-tree relative image addressing are outside the initial local
resolver and produce an explicit unsupported finding instead of a verified
anchor or path.

External destinations in parsed reference nodes are counted separately without
making requests. Bare-URL/email auto-linking is not enabled and is explicitly
reported as outside parsing scope. Private access,
redirects, external fragments, and server availability require their own
verification. Commit-qualified historical URLs keep their original source
identity. The initial checker has no historical-link suppression mechanism:
every local integrity finding fails the checked scope.

### Catalogue and companions

Full mode requires the version-1 catalogue to describe exactly the tracked
`.md` inventory, with one sorted, exact-case record per path. Required fields,
optional field types, enumerations, and unique arrays are validated. Duplicate
YAML keys, aliases, unsupported fields, missing assets, and absent/extra
documents fail. `.mdx` is outside this inventory.

The catalogue's provenance commit/tree/count must match real Git objects.
Version 1 represents the recorded base Markdown set plus declared additions;
it has no removal field. A physical removal therefore requires an explicit,
accurate provenance refresh or a separately reviewed schema extension. Neither
operation changes the externally supplied protection baseline. Every declared
companion is checked independently of parsed navigation links, including
companions shared by more than one document.

### Established protections

The trusted baseline remains authoritative when candidate metadata removes a
record, integrity flag, source binding, or protected companion. Candidate
metadata cannot authorize its own downgrade.

| Protection | Behavior during documentation organization |
| --- | --- |
| `fixed-path-consumer` | Retain the path and established metadata binding. Ordinary prose edits are permitted. |
| `immutable-evidence` | Retain exact Git paths, modes, and bytes for the subject and baseline companions. |
| `generated` | A changed view fails closed with an owning-validation requirement. The generic checker does not execute a candidate-supplied generator or approve a source/view contract change. |

The two retained performance evidence directories contribute 36 complete
files, including companions not selected by a candidate's shortened asset
array. The P4 review subject and its companion manifest are also frozen for
this organization boundary, giving 38 retained files in the pinned corpus.
Freezing the P4 manifest is a scoped organization safeguard; it does not
replace a future authorized review procedure or redefine all manifest fields
as permanently immutable. Coordinated subject/digest rewrites cannot bypass
the trusted original bytes.

Owning governance and performance validators continue to govern substantive
changes. Other retained performance policies outside the catalogue, unknown
consumers, code-formatted path mentions, classification truth, and semantic
approval claims are not exhaustively established by this checker. An absent
integrity flag is not evidence that a document can safely move.

## Evidence and failure handling

The CLI emits JSON with exact source and baseline identities, parser/checker
information, inspected counts, and actionable findings. Exit 0 means the
reported scope passed; exit 1 indicates integrity findings; exit 2 indicates
an input or execution failure. A successful link-only result cannot be read
as a successful full catalogue/protection result.

The workflow retains source identities, control output, full corpus results,
and checker-branch navigation results in one uniquely named seven-day
artifact. A failed or unexecuted step remains a failed or unexecuted step;
artifact presence alone is not qualification. Record the exact checker head,
source pin, run ID, attempt, and result in the implementing PR after execution.
Keep those run observations outside source commits to avoid self-referential
qualification claims.
