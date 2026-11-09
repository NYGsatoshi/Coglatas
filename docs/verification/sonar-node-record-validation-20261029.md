# Native Node coverage record validation refactor

Base: Main0ea944a4602e9484392c8f2592752ffd98f34c5c, the normal merge of PR1518.
Follow-up: Sonar issue AaGGe5XStqzpX64Gak4Q reports cognitive complexity23
against a limit of15 in validateNodeCoverage. It is a Maintainability finding;
this change is not a new authorization or security contract.

Extract the unchanged per-record DA/FN/BRDA syntax, original-line bounds,
branch totals and hit/uncovered counters into a private validateRecordRows
helper. Keep record completeness, unique tracked source inventory, root and
file identity checks in their existing boundaries. Keep the CLI constant,
canonical module entry, fully qualified Git selection, reader, rejection
stages and closed diagnostics unchanged. No LCOV rewriting, filtering,
fabricated hits or source/coverage/scanner policy change.

Local validation: syntax and diff checks pass; all12 existing controls pass
with zero retries/skips. These include real raw-versus-mapped Playwright
coverage, retained source and uncovered branches, malformed records,
source/path/alias/CLI denials and actual Git fixtures. Temporary fixtures run
under the existing managed temporary directory. No new test merely mirrors
the extraction. Fresh hosted99-Node/native/requiredCI/current-head independent
review and official Sonar must verify the refactor and remaining issue count.
The expected complexity improvement is not an observed official result yet.

The earlier source-map and S8707/S4036 evidence remains bound to9f/Main0ea;
no prior result certifies this new source. Formal Owner holds and Main-wide
Sonar54.6%/D/E historical failure remain separate.

STUDENT_COUNCIL_PILOT=RELEASE_NO_GO.
