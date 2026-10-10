# SEC-14 original Main API scanner observation

This additive Advisory consumer addresses a concrete provenance gap in existing
SEC-04 Schemathesis and SEC-06 ZAP metadata. Those native formats contain tool,
role and OpenAPI identities but no candidate or workflow identity. The consumer
resolves the original GitHub artifact and immutable source independently rather
than adding a self-declared candidate field to historical metadata. It grants
no category acceptance, security promotion, exception or owner approval.

## Independent authority and original bytes

`scripts/ci/sec14_main_security_evidence.py` accepts an independently expected
candidate, Main run, attempt and artifact ID plus the original downloaded ZIP.
It queries the fixed repository's live GitHub API using the existing read-only
token interface. It requires a successful, completed Main push run from the
repository's Main workflow and origin; checks the original build, backend and
security jobs; and selects the successful security artifact upload step.
Artifact origin, digest, length, expiry and creation time must match the same
observed upload window. The API does not expose an upload-attempt identity:
`UPLOAD_STEP_TIME_WINDOW_OBSERVED` is a consistency control with that explicit
limit, not an invented immutable attempt assertion.

The consumer fetches the Main caller, Main validation workflow, ZAP plan and
ZAP policy at the exact immutable candidate. Base64 content length and Git blob
address are checked; SHA-256 records the actual bytes. Original native metadata
and reports are parsed from one digest-verified ZIP snapshot. Live run, job and
artifact observations are fetched again after byte validation. These HTTPS
observations are non-atomic and are not signed scanner or loaded-application
attestations. A dismissed, expired, replaced, incomplete or wrong-candidate
artifact cannot be qualified by a saved JSON receipt.

Inputs are bounded: 512 MiB compressed ZIP, 256 members, 1 GiB total expanded
bytes, 32 MiB JSON, 128 MiB per Schemathesis stream, 8 MiB per NDJSON line and
100,000 events per role. Archive paths, duplicate members, links and encrypted
entries are rejected; nothing is extracted. JSON rejects duplicate fields,
non-finite numbers and excessive nesting. Exclusive output preserves existing
receipts; fixed error codes do not expose tokens or protected native values.

## Native observations and scope limits

All five existing Schemathesis roles and all three existing ZAP roles are
required. Schemathesis 4.25.2 initialization, seed and completed native engine
events must agree with metadata and the observed job window. Nonzero request
and operation counters, zero reported network/scanner errors, unique operations
and membership in the actual OpenAPI inventory are checked. Each role and the
union retain their unobserved operation counts. Counters remain explicitly
`PRODUCER_REPORTED`; this consumer does not reauthenticate every HTTP exchange.

ZAP requires the existing pinned 2.17.0 image and isolated scope, completed
scanner outcome, sanitized mode, immutable plan/policy bytes and matching
OpenAPI hash. Risk, rule and instance counts are recomputed from the native
sanitized report. Raw alert names, instances, parameters, paths and native
values are excluded from the observation. The historical report does not
establish request execution counts, browser/AJAX execution, image identity or
complete security-contract coverage; these qualifications stay `UNVERIFIED`.

The receipt always records zero acceptance-qualified categories, fourteen
outstanding #614 categories, `releaseAcceptance: BLOCKED` and
`preAvaloniaVerdict: BLOCKED`. Immutable release-image subject, actual loaded
application build, cryptographic execution attestation and personal approval
remain unverified. Neither a successful security job inside a failed Main run
nor historical evidence for another candidate establishes current qualification.

## Existing same-subject Advisory integration

The [existing release Advisory](sec14-same-subject-advisory.md) optionally accepts
all three flags together:

```bash
--main-security-run-identity https://github.com/NYGsatoshi/Coglatas/actions/runs/<run>/attempts/<attempt> \
--main-security-artifact-id <original-artifact-id> \
--main-security-artifact <original-downloaded.zip>
```

The live Main candidate must equal the release caller's independently supplied
expected candidate; a different Main run is allowed only within the same
repository and exact candidate. The nested observation and original ZIP digest
remain distinct from immutable release-image qualification. No saved observation
JSON is accepted as authority. Absent inputs retain the existing report shape
and scope; partial inputs fail visibly. Optional helper source hashes support
change detection without claiming that on-disk code hashes authenticate loaded
code. The existing non-required release contract lane runs these controls; no
new required context, release prerequisite, schedule, secret or scanner launch
is introduced. Later verification repeats live observations and still requires
original bytes; retained JSON cannot retroauthenticate historical execution.
Read-only release re-verification checks live authority and byte identities
again, bounds the historical observation time between artifact creation and
the new observation, and compares the remaining receipt exactly. It preserves
the original receipt bytes; its historical timestamp is not treated as authority.

## Exact historical execution inspected

The original successful Main run `37982034044`, attempt `1`, candidate
`3335d59d458b50c14071b843a799902429c92689` supplied security job `113996185392`
and `main-security-evidence` artifact `11643296025`. Its original ZIP is
17,395,053 bytes with SHA-256
`eba9678eeb54e1d7909e6a1f31f075bdcb9300ad8ae9f299df09812024239138`.
Live resolution on 2026-10-10 returned `MATCHED`: 398 OpenAPI operations, five
completed native engines, 543,700 producer-reported requests, one union
unobserved operation and three ZAP reports each with zero High alerts. This
remains exact historical evidence with the above limitations.

The independently downloaded artifact `11650854710` from run `38003724242`,
attempt `1`, candidate `2f7e6da4d319c4c9b8ebfd8fba766d295d16fee0` is
17,351,744 bytes with SHA-256
`9d269ac3d11d9c6ecde141c2aa951ca88f77d452d691187f88c7ccd8377b2165`.
The consumer returned `ERROR` because the full Main run failed. Its successful
security job `114068701792` does not erase that failure. Original ZIPs, live API
snapshots, intermediate test failures and scoped receipts are retained privately.

Positive and deliberate-invalid controls use synthetic API/ZIP fixtures and
do not count as product scanner execution. The current three OPEN/HIGH source
dependency alerts for `braces`, GHSA-vfj7-8cjw-p6xm, remain separate blockers;
no suppression, dependency baseline or exception was changed.

The final local suite passed 198 release controls, 39 existing SBOM/source/
inventory controls, 45 unchanged Required Check topology controls and eight
browser launcher controls: **290 tests, zero failures or skips**. This includes
sixteen native artifact/API controls and five optional aggregation controls.
Python 3.13.12 / Ruby 3.4.9 ran in the existing local runner image with networking
disabled and a read-only checkout; native browser launcher controls ran in Node.
Intermediate fixture clock/digest assertions and an incorrect zero-test command
remain preserved in the private development logs. Final hosted qualification
and the currently active Main run require separate evidence.
