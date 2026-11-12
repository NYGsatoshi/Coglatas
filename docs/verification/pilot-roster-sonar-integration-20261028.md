# Reviewed roster diagnostics and Sonar coverage integration

Status: Draft / REVIEW_ONLY. `STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.

## Exact source and scope

Start from freshly fetched Main
`fe21adb7ccbe972322c3b22d36e4d37b163a3601`. Normal merges combine:

- #1483 `8bc4b438911a6a13665475d7450baabedfe3c2f9`: supported pinned
  Istanbul Angular coverage, repository-rooted LCOV, trusted scanner import and
  the recorded Owner Sonar policy. Five Main-relative files.
- #1481 `cc598157de7e50ea34e8b5b68a7bdaa3749dfda2`: finite native Mobile
  action markers, strict legacy/current receipt reading and explicit
  INDETERMINATE equal-millisecond ordering. Three Main-relative files.

The initial combined source before this note is
`633c51768da7ad3d46fc9cd65cae62f1e8502cd3`, full tree
`fec0d39d01957141334740bb60c9a258e21192ad`. Both merges were clean; no
new Product implementation, held candidate, acceptance contract, workflow
permission, secret, environment setting or Quality Gate threshold is added.
This integration note is the only additional file beyond those reviewed diffs.
Final published HEAD/tree and current executions belong in the PR checkpoint;
the note does not claim its own future commit identity or pending results.

## Separate evidence, not combined execution credit

Main50a7d417 after Product #1476 separately completed Main CI40723277353/1:
45 successful jobs/one configured skip, actual native43-member Desktop/Mobile
and eight required P0 JUnit cases PASS, plus actual Full Functional. Original
Mainc72's failure remains FAIL. Resulting Mainfe21 after the separately reviewed
finite Thread diagnostic #1484 has its own Main CI40730961301/1 qualification.

At coverage HEAD8bc4b438, Sonar40731140295/1 actually passed1,919 instrumented
tests, imported LCOV and uploaded the report. Official Cloud Quality Gate PASS
displays0% New Code Coverage on that configuration-only PR; it does not establish
80% application-new-code coverage. Local report lines20,924/24,603 (85.04%)
have300 repository-rooted source paths. Existing `**/artifacts/**` exclusions
match four unresolved first-party Artifacts report entries; an unchanged
announcement-test analyzer exception also remains. No scope/threshold was
weakened to hide these limits. Exact-tree native40731161843/1 separately passed
both profiles43/200/403/400/End1894/Home0/retry0 and FCI-04 fast/full.

At diagnostic HEADcc598157, identical-tree sourceb1c3ffb3 in40732809442/1
passed the same native controls. Original ZIP12836386682 SHA-256
`73ccb1088bf09b69419f08482d7e19782d93aba11303e752444ca60ad6b36d81`
was independently verified. The Mobile V2 receipt retained all eight native
action markers, with explicit tie semantics and no truncation. Original failed
40690662224/1 and ZIP12818031390 SHA-256
`027158a448c4c664be2753fe3a30728c94d914f13840dc9d4d9e505a147fe710`
remain immutable FAIL; a different source's pass does not rewrite them.

The V8 coverage generation40724553982/1 and capped diagnostic publication
40724559009/1 first failures also remain FAIL. Supported instrumentation and
the independently merged append-result guard are distinct subsequent fixes.
No timeout, retry, native action or mandatory assertion was relaxed.

## Required combined qualification and remaining holds

Current combined-source required CI, independent review and actual protected
ASP.NET Core/PostgreSQL/Chromium first-attempt execution are required before
ordinary protected integration. Separate candidate passes are not substitutes.
The final Main then requires its own source/build/image/served-payload and
first-attempt Functional/native evidence. No pending, skipped or canceled job
is a PASS; original failures and their artifacts retain their identities.

`OWNER-FCI06-PREREQUEST-DENIAL` remains unapproved/default C with the canonical
fresh-request owner unchanged. Historical #1342/#1365/#1413 causes are not
resolved by this integration. Formal SEC-ARCH/TMT/SPEC, RLS, Enforce, recovery,
public staging/HTTPS and real-student admission holds remain. No release,
deployment, confidential specification or held-service activation is authorized.
