# D10-B prepared Shadow capture candidate — OWNER HOLD

This is an **inactive engineering proposal**, prepared after D10-A's 18 negative/
positive controls and 40 existing archive/provenance controls passed on Linux.
It is not an authorized execution path. No file is placed under .github/workflows,
no active Playwright configuration imports the reporter and no artifact is
published. The workflow also has a literal false job condition and an unconditional
exit-1 admission step. The proposal remains blocked even if copied accidentally.

The [admission register](admission.json) keeps every execution/publication/runner
authorization false. The [workflow template](workflow.yml.disabled),
[additive config wrapper](playwright-shadow.config.ts.proposed) and
[observer/exporter proposal](playwright-shadow-capture-reporter.mjs.proposed) are
concrete review inputs, not runtime qualification. They require a future separately
approved source composition containing #1760, #1717 and #1755. None is assumed
integrated into SecurityTestImplimation. #1755's historical rejection is unchanged.

## Exact activation decisions still required

1. Explicit Owner authorization for one new non-required Shadow runner and one
   sanitized native case artifact owner. No Required Check replacement/promotion.
2. Admission of the exact workflow/config/exporter bytes and independently supplied
   D10-A expected policy. A candidate JSON approval flag cannot approve itself.
3. Independent provenance and exact source/input matching for an existing prebuilt
   frontend. No fallback build or unverified artifact reuse. Run/Artifact IDs remain
   null until admitted. The template intentionally does not invent a download path.
4. Worker-parity resolution. The original default config is one worker; the exact
   protected prebuilt invocation can use two. The original helper sends any config
   CLI passthrough back to one worker. This proposal preserves that policy and adds
   no --workers bypass. It does **not** establish parity with the protected two-worker
   invocation. Owner admission must select a path satisfying the original budget
   requirement before activation; changing the helper or protected lane is outside
   this proposal.
5. Complete locked dependencies, independent original 24-spec --list/source
   applicability discovery, numeric workflow/job frame from GitHub API, and an
   exclusively owned 0700 runner-temp bundle directory. The comparison source
   envelope is private and is never uploaded. These setup bindings are deliberately
   not treated as implemented merely because an observer template exists.
6. Current-head CodeRabbit, complete positive/negative execution controls, source
   composition checks, all six native required contexts and governance compliance.

The wrapper imports the original root config and only appends the reused memory
observer's adapter. Original projects, source/config anchors, selection, assertions,
retries, deadlines and original reporters remain represented unchanged. The
original runner receives the config path; its existing worker policy is retained,
with unresolved worker parity explicitly held as above. There is no sharding.

The exporter compares actual onBegin discovery with a separately generated,
canonical source-list envelope before observing results. It preserves every
native failure/retry and does not override the native conclusion. It creates only
source.json, applicability.json, native.json and capture-receipt.json, with hashed
case IDs and fixed metadata; no titles, diagnostic bodies, traces or attachments
are copied. Writes use Linux pinned directory handles, no-follow opens and
exclusive 0600 files. A failed or incomplete capture is evidence, never a pass.
The template retains original reporter output privately and prints only a fixed
numeric exit summary. HTML/JUnit/trace ownership remains with the existing lane;
the Shadow upload paths name only the four sanitized case files.

Numeric producer fields in the receipt remain claims. After the sole producing
job and run actually finish, an independent operator must invoke D10-A against the
server Artifact ID and actual downloaded bytes. Verification cannot run inside
the still-in-progress producer job or trust its own JSON receipt. The v1 D10-A
contract rejects multi-job and rerun ambiguity, unsuccessful producers, wrong
source/run identity, incomplete reports and every unsupported provenance claim.

Only after independent source, native and artifact provenance qualification may
Phase C compose results. A Shadow result never becomes Required Check acceptance.
No new token permissions, registry settings, bypass actors, source baselines,
scanner thresholds, release approval or Main modification is authorized here.
The template's proposed actions:read permission and publication remain unapproved.

Local validation is limited to JSON/YAML structure, immutable pins, inertness,
proposal syntax and content/reference policy. It is not native capture execution,
producer authentication or acceptance. Current source/native counts, producer Job
ID, capture Artifact ID and archive/report digests remain unavailable. No runtime
overhead or whole-CI speedup is claimed. Rollback removes these documentation-only
proposal files, preserving all preparation and failed/review evidence.

```text
PROGRAM_STATUS=INCOMPLETE
SECURITY_BRANCH_INTEGRATION=HOLD
MAIN_UNCHANGED=true
GOVERNANCE_IDENTITY_DRIFT=OWNER_HOLD
TRUSTED_SONAR_IMPORT=NOT_VERIFIED
PR_J_TO_N_ACCEPTANCE=OPEN
REQUIRED_GATE_QUALIFIED=false
D10_B_EXECUTION=OWNER_HOLD
D10_B_PUBLICATION=OWNER_HOLD
```
