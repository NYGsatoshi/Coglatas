# Predeclared PostgreSQL baseline capture campaigns

Current active duration eligibility is defined by
[EnvironmentClass and hardware evidence](ENVIRONMENT_COMPATIBILITY.md).
The exact-environment rules below describe immutable legacy campaign declarations
and original replay. CPU SKU equality is no longer an active qualification gate.
Original rejected results stay historical; separately authorized class enrollment
does not rewrite manifests, samples, workflow conclusions or old approvals.

The campaign policy is `perf05-db-campaign-v1`. It changes baseline selection
governance only. It changes no measurement window, fixture version, environment
compatibility key, comparator threshold, duration budget, scenario, authorization
assertion, paging assertion, or structural gate.

## Why a separate campaign policy

The initial PERF-05 promotion evaluated only the earliest capture for each full
environment key. An unstable first capture could leave that key without a usable
baseline, even when a later independently planned collection would be legitimate.
Selecting a favorable later historical capture would make policy depend on results.

This general policy must be merged to main before declaring any new campaign.
Historical captures, initial approved documents and rejected environment groups
remain under their original policy. They are never retroactively enrolled. In
particular, rejected `64dcdf892e0d69b61880c6019ce00c31704aafc70b8ecd3a169c958d1472cb60`
evidence from run `37222734196` remains historical and unapproved. A new prospective
campaign may target that exact key; changing the key to obtain a passing result is
forbidden.

## Immutable declaration before measurement

Commit one manifest at
`performance/baseline-campaigns/manifests/<campaignId>.json` through an independent
reviewed PR to main. `db-baseline-campaign.schema.json` defines the machine-readable
shape; `db_campaign.py` enforces the additional source, digest and chronology rules.
No live manifest is included in the governance rollout PR.

| Field | Fixed declaration |
| --- | --- |
| `campaignId`, `policyVersion` | Unique ID and general campaign policy version |
| `createdAtUtc`, `expiresAtUtc` | UTC declaration time and expiry, at most seven days apart |
| `profile`, `environmentCompatibilityKey` | One exact small or medium profile/environment target |
| `sourceSha`, `sourceRef` | Earlier reviewed main source, already containing the rolled-out policy |
| `fixtureIdentity` | Profile, unchanged fixture version/hash and seed manifest SHA-256 |
| `toolVersions` | Exact runtime, Node, PostgreSQL, Playwright and browser versions |
| `toolSourceDigests`, `contractDigests` | Exact capture instruments and contracts from that source |
| `scenarioSet` | All nine canonical streams in source order, with page size, metric and unit |
| `sampleCount` | All five original samples per stream, in original order |
| `maxCaptureGroups` | A fixed integer from one through three |
| `stabilityRule` | Existing relative MAD rule, including exact comparator digest |
| `selectionAlgorithm` | `earliest-eligible-stable-complete` |
| `earlyStopPolicy` | `never`, or the explicitly predeclared `first-eligible-stable-complete` |
| `eligibilityRule` | Exact source, environment, fixture, toolchain, completeness and structural PASS |
| `authorization` | Approver, exact review comment, rationale, allowed cause and predecessor |

The review comment must identify the campaign ID and canonical manifest SHA-256
before the main push starts its workflow. Its author must match the declared
approver; later edits cannot establish retrospective approval. Canonical JSON
SHA-256 uses sorted object keys, compact separators and unchanged array order,
as implemented by `db_campaign.digest`. A comment ID may be allocated first,
then its final body may be written before merging the declaration. The reference
is pinned in the manifest and its final edit time is checked against the run.

The declaration's main push starts the capture workflow. Exactly one newly added
manifest is allowed per push. Existing manifests are immutable; modifying,
deleting/recreating or reusing an ID cannot start a new campaign. Workflow reruns
and manual dispatch are unavailable. The first workflow attempt is mandatory.
Both declaration SHA and earlier measured source SHA are recorded separately.
Measurement uses the successful exact-source main runtime artifact; it never
rebuilds with different tooling to obtain a compatible key.

Runtime/version fields may be copied from known environment metadata to declare
the intended environment. Historical duration samples cannot be copied into
the new campaign. The hosted runner may produce a different full environment
key; such a group remains rejected. The workflow does not choose a favorable
CPU, dynamically register observed keys or rerun after environment mismatch.

## Fixed, bounded group selection

Each group runs the unchanged small and medium collectors serially, using clean
Compose volumes and the original warm-up. Their structural comparison still
requires every one of the 28 checks. The campaign selects only its declared
profile's nine duration streams; the other profile remains structural evidence.
All page-10 captures are retained without pooling them with canonical page-5
streams. Numeric samples with equal values are valid; duplicate identities,
removed samples and reordered captures are rejected.

An eligible group must be complete, structurally passing and compatible with
every declared identity. Every canonical stream must meet the unchanged
relative MAD maximum of `0.20`. The earliest eligible stable complete group is
the sole candidate. Later stable groups cannot replace it.

With `maxCaptureGroups = 3` and `earlyStopPolicy = never`, all three groups run,
even when the first or second qualifies. A predeclared early-stop policy stops
exactly at the first eligible stable complete group. An unstable, wrong-key or
safe incomplete group is retained and rejected, then the next predeclared group
may run. Unsafe or corrupt evidence fails campaign integrity; it cannot be used
to grant baseline acceptance. Cancellation, expiry or missing declared groups
also fails closed.

If all bounded groups fail eligibility/stability, the result is
`BASELINE_UNAVAILABLE` and the selection step fails. There is no rescue retry.
Failure cannot be avoided by adding another campaign ID or changing its count.
A subsequent campaign for the same profile/key requires a reviewed forward
product change or measurement correction, a different approved source, a
substantive matching source diff, its predecessor identity and retained prior
evidence. Baseline failure, missing coverage or a desire for a passing PR are
not allowed causes. A key is therefore neither silently retried nor permanently
disabled against future independently justified engineering changes.

## Prospective environment-assignment transition

The separate rule `perf05-environment-assignment-v1` was explicitly approved by
the repository owner for implementation in
https://github.com/NYGsatoshi/Coglatas/issues/1105#issuecomment-6016721971.
The recorded decision is owner authorization, not an independent human or native
GitHub approving review. Its immutable receipt is under
`baseline-campaigns/policies/`. The qualified rule rollout must reach Main before
any schema-3 transition declaration is created. No live campaign is part of the
rollout. Earlier schema-1/2 declarations and their results remain unchanged.

This rule permits one direct prospective transition within an engineering epoch
when the normally assigned, exhausted campaign has one consistent observed full
environment identity different from its declared target. It does not promise that
the next normally assigned runner will match. The runner label and scheduling
remain unchanged; another mismatch stays rejected and cannot chain into another
transition. A new ID or CPU observation is not a new engineering epoch. Existing
substantive product/measurement correction requirements still govern subsequent
same-scope campaigns. A new initial declaration cannot reset an existing profile.
On introduction and before capture, the exact capture-workflow bytes at the Main
rule-introduction SHA must match both the predecessor declaration and current checkout. Changing runner selection,
workflow scheduling or capture steps cannot use this rule.
Historical replay binds the original workflow without preventing legitimate
future workflow changes for other engineering epochs.

Schema 3 uses the same public environment digest and an explicit
`environment-assignment-transition` cause. It binds the latest predecessor for
that profile, the earlier Main rule-introduction SHA and its fixed owner decision,
the predecessor artifact identity and the complete raw-group digest. Every group
and sample must already be retained on Main. On introduction and before capture,
the validator authenticates the actual failed first-attempt Main-push ZIP and
compares all original canonical files. Later historical replay preserves exact
retained bytes and does not require expired hosted archives.

The target is the sole environment compatibility digest computed from every
predecessor group, without choosing a subset. Complete safe evidence, exact source,
fixture and tool identities are mandatory. Timing values, stability/MAD and
structural performance outcomes do not select or authorize the transition scope;
stable and unstable duration observations follow the same transition rule. Those
outcomes remain fully preserved and still control each campaign's own baseline
eligibility. Source, fixture, tools, comparator, contracts, all nine scenarios,
five ordered samples, declared group bound, early-stop and selection rules are
unchanged. The predecessor must remain `BASELINE_UNAVAILABLE`, unapproved and
without a selected group. Its samples are never a successor baseline.

The successor gets a new ID, canonical digest, fixed authorization reference and
expiry after the qualified rollout and predecessor completion. Its reference
cannot reuse any registered campaign or rule approval. Before the single normal
Main-push campaign, the owner must directly provide these exact standalone lines
at that new fixed reference:

```text
APPROVED_PREMEASUREMENT
CAMPAIGN_ID <new-campaign-id>
MANIFEST_SHA256 <new-canonical-digest>
```

The author must be the repository owner, the exact ID/digest must match, and the
comment's creation and latest edit must precede run creation. A placeholder,
revocation, old approval, changed count/source, mixed assignment, chained/cyclic
transition, unobserved target or historical enrollment fails closed. The executor
does not write this new human measurement authorization. Successful fresh capture
still requires its separate evidence/baseline review and approval.


## Zero-capture premeasurement recovery

A declaration is consumed by its first Main-push workflow even when the workflow
fails before measurement. Such a failure is not a performance result and must not
be rescued by rerunning the workflow or reusing the campaign ID.

The narrow rule `perf05-zero-capture-recovery-v1` permits one replacement only
when the consumed campaign is a schema-3 environment-assignment transition and
its first-attempt Main-push run is proven to have failed at the premeasurement
identity-freeze validator before any capture group started. The live run must be
completed/failing, the first checkout and validator-test steps must have passed,
`Freeze premeasurement campaign identity and reject retries` must have failed,
`Execute only the predeclared serial capture groups` must have been skipped,
and the workflow must have published no artifacts. Run attempt 2+, a started
measurement step, any campaign artifact, cancellation, or another failure stage
is not recoverable under this rule.

The replacement uses schema 4 and `premeasurement-validator-recovery`. It must:

- bind the consumed campaign ID, canonical manifest digest, first Main declaration
  SHA, exact failed run ID/timestamps and the fixed failure step;
- keep the exact environment target, measured source, fixture, tools, contracts,
  scenario order, sample count, MAD threshold, group bound, early-stop policy,
  selection algorithm and original environment-assignment-transition identity;
- use a new campaign ID, creation/expiry window and fixed owner authorization
  reference;
- be declared only after this recovery rule is already on Main;
- preserve identical capture-workflow bytes from the consumed declaration through
  the recovery-rule rollout and the replacement declaration;
- obtain a fresh exact-ID/digest owner premeasurement approval before its single
  Main-push capture.

Recovery cannot chain. A recovery declaration cannot itself be recovered under
this rule, and it cannot become the predecessor of another environment-assignment
transition. The consumed run receives no duration, baseline, #1046 or acceptance
credit. This rule corrects only a proven zero-measurement governance failure; it
does not create a general retry mechanism.

Approval comments use explicit standalone directives. Narrative text such as
`predecessor remains REJECTED historical evidence` is not a revocation. A line
beginning with the standalone directive `NOT_APPROVED`, `NOT APPROVED`,
`REJECTED` or `REVOKED` still invalidates authorization.

## Evidence and separate approval

`perf05-campaign-<campaignId>` contains the immutable manifest, declaration,
ordered raw groups, individual group decisions and replayable aggregate result.
All safe samples from failed collectors are retained. Raw SQL, parameter values,
responses, credentials, cookies, tokens and production/user content are excluded
by the existing capture allowlist and the campaign's strict nested schemas.
The upload's actual artifact ID and SHA-256 are published separately to avoid a
self-referential archive digest. Artifacts are retained for 90 days; copy all
required safe evidence into a reviewed approval PR before expiry.

Capture produces `BASELINE_CANDIDATE` with `approved: false`. It does not update
baselines, grant duration acceptance, merge a product PR or approve itself.
Review promotion in a separate PR containing:

1. The exact `manifest.json`, `declaration.json`, `raw-groups.json` and
   `campaign-result.json` from the trusted capture archive under
   `performance/baseline-campaigns/evidence/<campaignId>/`.
2. One approval entry in `performance/baseline-campaigns/approvals.json`, with
   approver/reference, approval time, actual artifact identity and fixed evidence
   and baseline directories.
3. All nine baseline documents at
   `performance/baselines/db/<profile>/<environmentCompatibilityKey>/<scenario>.json`.
   Use `baseline_documents` in `verify-performance-db-campaign-baselines.py`
   to obtain canonical documents; its default is unapproved. Only a reviewed
   promotion uses `approved=True`.

On introduction, the approval validator fetches exact GitHub run and artifact
metadata, downloads the actual bounded ZIP, verifies its archive digest and
compares retained evidence with the original archive. Cross-host redirects strip
the Authorization header. Replay recomputes the full structural and stability
decision and earliest-group selection; self-declared digests are insufficient.
The captured workflow must be a completed, successful first-attempt main push.
The source and declaration must already be in approved main history.

Each baseline records the campaign, source, declaration, run/attempt, artifact,
environment, fixture, tool versions, raw-group digest, sample digest/count/order,
comparator, stability decision, selected ordinal and prior rejected groups.
After approval enters main, both the approval and retained evidence are immutable.
Replay uses pinned source contracts, so future legitimate tool/fixture changes
cannot rewrite a historical campaign. Approved historical evidence remains
verifiable when its hosted artifact expires; new approvals require the live
original archive. Existing legacy baselines retain their separate validator.

Historical replay loads the pinned structural aggregator, fixture version and
contracts. Its shared capture allowlist and statistical helpers currently use
the compatible main implementation. A future incompatible change to these
helpers must introduce explicit replay compatibility before rollout; it cannot
silently reinterpret or invalidate previously approved campaign evidence.

One approved baseline group per profile/environment is supported. Replacing an
existing group is a moving baseline and is forbidden by this campaign policy.
Candidate-self baselines, mixed groups and ambiguous canonical/variant groups
are forbidden. Compatibility and the existing comparator still independently
control every actual duration comparison.

## Verification

```bash
python3 -m unittest discover -s tests/ci -p 'test_performance_db_campaign.py'
python3 scripts/performance/db_campaign.py validate-repository --base-ref <base-main-sha>
python3 scripts/ci/verify-performance-db-campaign-baselines.py \
  --base-ref <base-main-sha> --head-sha <candidate-head-sha>
```

These checks validate governance, not a product performance result. A final
product PR still requires its exact-SHA API, structural DB and both duration
profiles, required checks and licensed gates. Its post-merge main evidence must
be evaluated independently.
