# Main Security Boolean query contract repair

Prerequisite to #1117–#1122. Audited Main:
`ad0740f60798618a5d08c043ccab7a35b62f6dd0`.

## Failure evidence

[Main CI run 37751230928](https://github.com/NYGsatoshi/Coglatas/actions/runs/37751230928),
job `113226184060`, failed its parallel Security runtime step. The failing
subprocess was **Schemathesis**, beta-owner, seed `544172496`, case `8fOUuC`,
`GET /api/projects`. Core and all three ZAP roles passed. Java Preferences
warnings were not the cause. PostgreSQL migration/startup and evidence
publication completed; the failure was a scanner contract assertion.

The sanitized `main-security-evidence` artifact records the minimized query:

```json
{"":[],"Archived":"false","Page":-100000000,"PageSize":"-0","Search":"\ud83c\uddfa\ud83c\uddf8","WorkspaceId":"3c8c8897-7f8a-4d83-a5eb-73d3eccfb1a6"}
```

The empty additional array disappears during HTTP query serialization. MVC
receives only modeled parameters, accepts the Boolean spelling, and normalizes
pagination to 1/50. The pinned Schemathesis checker compares the serialized
Boolean string to the Boolean-only schema, so its existing additional-property
guard treats an otherwise valid query as invalid.

## Repair and regression evidence

The operation transformer describes Boolean query parameters as Boolean or
string, with a pattern limited to MVC's case-insensitive true/false spellings
and the explicit .NET whitespace/NUL trimming set. ECMAScript `\s` differs
from MVC (`U+0085` is accepted, `U+FEFF` is rejected), so the shorthand pattern
is deliberately avoided. Bodies, headers, ordinary strings,
runtime validation, authorization, query allowlists and pagination are unchanged.

`scripts/security/test-schemathesis-query-wire.py` runs the **existing pinned
negative-data-rejection checker**, first reproducing the failure against the
old schema, then verifying the generated document. Valid wire spellings pass;
invalid strings and numeric values still fail, with and without extra query
metadata. The normal SEC-04 runtime executes this regression before its full
role matrix. No scanner check, role, operation or required check was removed.

Local evidence on the repair candidate:

- Focused OpenAPI, real Kestrel/EF InMemory Boolean query and pagination tests:
  60 passed, 0 failed/skipped, including unmodeled-key and Unicode-padding regressions.
- Actual build-time OpenAPI generation: successful, 0 errors; existing CS9113
  warning for `AuditPackageExportService.clock` remains unrelated.
- Pinned Schemathesis 4.25.2 image
  `sha256:72d6907a936f7b5f08f137c8f84c89eb3ab7834956d9af416e7b6510ebe4e065`:
  original failure reproduced; generated contract replay passed; invalid values
  rejected by the unchanged checker.
- SEC-04 harness contract suite: passed in a disposable Python 3.13 Linux
  container. Windows checkout shell line endings were normalized only in that
  container's temporary copy.
- Architecture suite: 9 passed, 0 failed/skipped.

The first PR head `122531a078706b41ec263d2de91b4b8ba7e9724b` exposed an
existing ReSharper warning in the touched transformer file: conditional access
on the API's non-null `ApplicationServices`. Job `113289118667` in CI run
`37770369945` remains the failure record. The repair removes that redundant
conditional access; it does not relax the changed-file inspection policy.

Extending the pinned replay to MVC's non-ASCII padding reproduced an additional
failure with the initial shorthand pattern. Explicit characters repair that
cross-runtime mismatch; the replay also verifies rejection of BOM/control padding.

The HTTP tests use InMemory and do not establish PostgreSQL behavior. This
repair changes no database model or migration. Hosted exact-head and post-merge
Main runtime evidence must be recorded in the PR before this prerequisite is
considered complete. This document does not claim Security Foundation delivery,
production security, numerical performance assurance or Avalonia implementation.
