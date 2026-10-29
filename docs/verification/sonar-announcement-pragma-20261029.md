# Announcement Sonar pragma compatibility

This is a bounded analyzer compatibility workaround for #1496. It changes the
order of two existing lint aliases; all executable bytes, the three disabled
rule IDs, their justification and every Functional acceptance condition stay
unchanged. It grants no FCI06 interpretation, Security approval or release GO.

## Observed failure

Sonar run40786464815/1, job123160934292, uses JavaScript analyzer14.3.0.46350 and
reports `Cannot read properties of null (reading '0')` on
`tests/functional/announcement/announcement-publish-read-journey.spec.ts`.
Scanner execution succeeding does not prove that this file was fully analyzed.
Main99c1615c5b9768df684738833267ebc11db1b67d retains the same original pragma.
Its fresh run40790457828/1, job123174139308, independently reports the same
Announcement TypeError and a separate invalid-UTF-8 protobuf error. Neither
scanner SUCCESS nor the pragma workaround establishes resolution of that
separate protobuf error.

## Source-bound isolated reproduction

Official SonarJS commit822582ba4369505b8701de3c4a0029e7f769e93a,
`packages/analysis/src/jsts/linter/pragmas.ts`, Git blob
334edf3c322844bf35798e177534df7d6d2b1c5e, SHA-256
be62afc2ec4d12bc863bea71c893a9efe2eaa38161cdd25a9c36f35d76f345bc,
remaps aliases by successive whole-string replacements. Official S104 and S138
metadata map `max-lines` and `max-lines-per-function`, respectively.

The exact remapping hook and ESLint9.39.5's actual
`createIndividualDirectivesRemoval` function were executed in an isolated Node
VM with synthetic directives and no product/server activity. The npm package's
SHA-512 integrity was checked before extracting that function; its source file
SHA-256 is2653bbe7f446f4a9bf34c7e6c43459f43954de89e037a990ad18c3d35ef48f55.

With the shorter alias first, the parent text becomes
`sonarjs/S104, sonarjs/S104-per-function`, but the second rule ID is
`sonarjs/S138`. ESLint's required whole-token match returns null and its removal
function throws the observed TypeError. With the longer alias first, both
mapped tokens match and the function completes. Canonical-ID and unrelated-name
controls also complete. This proves the isolated upstream alias collision;
it is not a complete Sonar scan or actual-backend acceptance.

## Qualification requirement

A fresh official analysis must establish that the actual Announcement file no
longer reports this bridge failure. Record the exact PR/Main SHA and run in
#1496/#615. Do not infer resolution of other invalid-UTF-8 protobuf, shell-parser,
coverage, Reliability or Security findings from this change. No rule, source
scope, assertion, deadline, retry, canonical owner or quality threshold changes.

Primary source: [pinned SonarJS pragma hook](https://github.com/SonarSource/SonarJS/blob/822582ba4369505b8701de3c4a0029e7f769e93a/packages/analysis/src/jsts/linter/pragmas.ts)
and [official ESLint9.39.5 package](https://www.npmjs.com/package/eslint/v/9.39.5).

## Actual candidate analysis and Artifact source visibility

At HEAD53f4f6beb37c6aeb1dc7235009085f8dc0d83c01, actual
run40792687751/1 job123181543157 completed SUCCESS without the Announcement
TypeError. This is a fresh real scan, unlike initial routing-only40792034546/1,
whose scanner job was SKIPPED because the PR did not exist yet.

That actual scan and e0f scan40790623813/1 still report four unresolved LCOV
paths, first the active Artifact detail-page component. Issue #1501 records two
proven broad filters: the Sonar **/artifacts/** analysis/duplication patterns,
and Git's unanchored artifacts/ ignore. Three production TS files plus existing
unit tests/templates/styles match them; Windows case folding additionally
hides tracked C# Artifacts source/test folders.

Limit Sonar output patterns to repository-root artifacts/**. Keep Git's broad
root/nested audit-output protection, adding only the three existing tracked
source directory exceptions. Credential/private-key rules remain effective
inside those source directories. No global SCM exclusion disable is added.
Three new controls exercise actual NUL-delimited Git ignore behavior in both
case modes, protected generated/sensitive paths, and the Sonar scope policy.
Before repair, source visibility and Sonar scope fail while privacy controls
pass; after repair all three pass. The original15 Linux routing controls remain
unchanged; their complete hosted execution is still required. A Windows text
stdin CRLF issue in the initial new control was corrected to Git's native NUL
protocol before evaluating the negative/positive source controls.

A new real scan must prove restored source indexing/LCOV mapping. The remaining
fourth unmatched path has not yet been identified; do not claim every mapping
or analysis-completeness issue resolved. See the official
[Sonar path matching rules](https://docs.sonarsource.com/sonarqube-cloud/appendices/defining-matching-patterns).
