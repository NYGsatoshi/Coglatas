# SEC-04 integration input attribution repair

This is an independent Main qualification repair before Security Foundation
#1117-#1122. No foundation implementation or acceptance item is completed by it.

## Failure evidence

Exact Main `6843daf7165f0ce281fbe0300dc132bfbe3586be`,
[Main CI 37775793206](https://github.com/NYGsatoshi/Coglatas/actions/runs/37775793206),
[Security job 113307650855](https://github.com/NYGsatoshi/Coglatas/actions/runs/37775793206/job/113307650855),
failed the Schemathesis deep lane in alpha-owner, seed `520194126`.
Case `1WzbXc` successfully created an integration with scanner-supplied
`displayName: sqlstate`; case `X9hacU` subsequently read that stored name.
The disclosure check classified that same user input as a SQLSTATE diagnostic.
Only finite categories and case provenance are recorded here; full response
payloads, credentials and private artifact contents are not republished.

Anonymous and alpha-restricted completed successfully before that failure.
All three ZAP roles completed with zero High/Medium/Low alerts. Main Backend
and Frontend passed. The separately failed licensed Messaging journey is
addressed by normally merged #1130 at
`64c070da52928d8066e0236e058195c4d434cbdc`; it does not repair this scanner defect.

## Attribution boundary

The hook retains its original diagnostic patterns and auth-material checks.
Only an HTTP 200 POST to `/api/tenant/integrations` can establish a name proof:
the returned canonical nonempty UUID and exact `displayName` must match the
scanner's string input. A later collection GET can attribute only that same
ID/name pair in the same scanner role and tenant. Other routes, methods,
statuses, fields, IDs, names and scopes have no attribution exception.

Auth material is checked before attribution, including JSON-escaped material.
Errors, stacks and diagnostics in other fields remain blocking. Failed checks
cannot establish proof. Duplicate JSON fields cannot establish provenance.
Decoded scalar checks prevent JSON escaping from hiding multiline diagnostics.
The hook does not change request or response data. Its bounded process-local
registry retains IDs and name hashes; it is not published as an artifact or
used to establish application authorization.

## Local evidence

- Pinned Schemathesis `4.25.2` hook replay before repair: 20 tests executed,
  11 failures (including the recorded POST/GET false positives).
- After repair: 27 tests pass, including generated-contract Case metadata,
  exact stored input, unproven/foreign
  reads, different ID/name/role/tenant, error/stack fields, auth material,
  failed creation, non-200 statuses, malformed/duplicate JSON, cache capacity,
  escaped diagnostics and response/request immutability.
- Existing SEC-04 shell contract/evidence sanitizer harness passes in Linux.
  A temporary LF copy is used because this checkout uses Windows line endings.
- `git diff --check` passes.

The replay runs the actual custom hook in the existing digest-pinned scanner
image. It does not constitute a full live API scan. The runner executes it
before the unchanged role scans. Scanner selection, seeds, example budgets,
checks, statuses, thresholds, DAST roles, retries and protected checks remain.
No application API, authorization, dependency or deployment change is made.

## Hosted qualification

Candidate PR checks and post-merge exact-Main deep Security, licensed browser
and Functional qualification remain required. Record immutable SHA/run evidence
in the PR before claiming Main is healthy. Failed Main remains failed evidence.
Numerical performance remains `SUSPENDED / NOT_EVALUATED`; #1128 and #1046 are
separate workstreams. No new Avalonia/ProjectIDE implementation is introduced.
