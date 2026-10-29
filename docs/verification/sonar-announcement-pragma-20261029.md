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
