# CI assurance continuation after PR-H

```text
PROGRAM_STATUS = INCOMPLETE
SECURITY_BRANCH_INTEGRATION = HOLD
MAIN_MODIFICATION = PROHIBITED
RELEASE_READY = NOT_ESTABLISHED
GOVERNANCE_IDENTITY_DRIFT = OWNER_HOLD
```

This append-only observation covers 2026-11-06 04:41:52–04:43:16 UTC. Its
Security source is `3865b09a2d3ea3cb82f450c6d19ec643ffbae01f`. Subsequent
observations must use new records and preserve this one. Git content and the
capture hashes detect alteration; this is not signed evidence or permanent
remote archival. The full raw archive remains in the local sibling directory
named in [the capture index](capture-index.json). That index binds every captured
response, including error responses, to its bytes. Neither logs nor artifact
contents were independently authenticated by this metadata capture.

The user reaffirmed approval of D01–D15 during continuation. Their actual text
was not present in this conversation, the prior assurance worktree or its local
evidence records. It is not reconstructed here. The continuation prompt also
ends during PR-N. Those requirements remain unresolved inputs, not cancelled
decisions or permission to invent an integration policy.

## Exact-head refresh

[The native refresh](native-refresh.json) preserves all open Security-targeted
PR head/base pairs, check identities/conclusions, native review submissions,
thread resolution and PR CI jobs. The local archive additionally preserves
all 219 discovered run attempts across all 12 open Security-targeted PRs,
including non-PR-CI workflows and every returned job/step outcome. Paginated
REST responses and GraphQL thread pages were exhausted. Embedded comment
pagination limits are recorded separately. Heads were fetched again at the end;
per-PR movement flags are explicit. This does not reconcile every test ID.

| PR | Current head | Native PR CI / attempt | Required six | Current-base metadata |
| --- | --- | --- | --- | --- |
| #1680 | `d52b365c1049c6d17915b692855a0c5506fd2fd8` | `42137420482/1` | PASS | Recorded base `35fa0b8b`; differs from current Security |
| #1696 | `b3e7e96afa7c721c5b5f092431d513e0b9a06690` | `42138675064/1` | PASS | Recorded base `35fa0b8b`; differs from current Security |
| #1717 | `4ca3d55951fbf3808d00d0c5bf8908d47188157b` | `42141215586/1` | PASS | Matches current Security |
| #1738 | `b34a0503ca0637634b3f492b69f3d3491c12c6d2` | `42141988652/1` | PASS | Matches current Security |
| #1739 | `768c487b394cf81245fad41fed18e73f15d6c9b0` | `42136475667/1` | PASS | Matches current Security |

The unchanged `required_check_contract.py` evaluator was executed against the
live API for all five heads. Each reports `exact_head.decision=pass` and overall
`decision=fail`, because `live required-check ruleset must be active`.
Check execution, current-base compatibility, governance compliance, complete
journey execution and artifact trust remain separate qualifications. No earlier
passing SHA substitutes for any row. In particular, #1717's formerly running
full native PR CI has now completed successfully on its current head.

No current-head independent approval is established. Native CodeRabbit comments
on #1680 and #1717 are attached to older commits, with no unresolved inline
threads observed. Empty native review lists do not establish that an external
CLI review never happened. The previous local CodeRabbit attempts retained
`payload_too_large` and `rate_limit` failures. The fresh local availability
probe failed before CLI execution with
`Wsl/Service/CreateInstance/MountDisk/HCS/ERROR_PATH_NOT_FOUND`; the configured
WSL virtual disk is unavailable. No fresh CodeRabbit review or manual substitute
is credited. Restore the existing review runtime/account access, then review
the exact final candidate diff before integration.

## Governance reconciliation

[The corrected governance projection](governance-observation-v2.json) contains the canonical
source-file digests, expected policy, all three live rulesets, effective branch
rules and classic-protection responses. Main was read only.

The original public projection is immutable in commit
`13b450aa090b0a8e78792ea79d49ea4d156395ee` and in the raw local archive.
Its first native attempts `42164786876/1` (LegacyNameGuard) and
`42164786888/1` (PR CI) rejected four unrelated profile URLs copied from the
branch API. The second projection retains only branch identity, head SHA and
protection fields, with the original projection digest recorded. Governance
facts and raw evidence are unchanged. This is an evidence-publication content
defect, not a product defect, flaky test or successful execution of downstream
build, browser and security owners. Downstream failures/skips remain first-
attempt evidence; later success does not erase them.

| Dimension | Expected | Observed | Qualification |
| --- | --- | --- | --- |
| Registered strict identity | `22302146`, Public Main Protection - Strict External Review, active | Same identity disabled | RULESET_IDENTITY_MATCHED=false |
| Second registered identity | PRreview (`22302157`), active | Disabled | Registered pair not enforced |
| Alternate identity | No implicit substitution | `24643016`, Coglatas Main Protection - Owner Only, active | Not an approved replacement |
| Contexts/application | Six registry contexts, app `15368` | Exact parity in active and disabled strict rulesets | CHECK_TOPOLOGY_VALID for this bounded dimension |
| Strict status | Up-to-date required checks | `strict_required_status_checks_policy=true` | Preserved on default branch |
| Target | Default branch | All three include only `~DEFAULT_BRANCH`, exclude none | Security not included |
| Review | Policy requires PR, zero approvals, no mandatory Code Owner review, resolved threads | Active and disabled strict rulesets match those native parameters | Mission independent review remains a separate unmet obligation |
| Last push/stale review | Both false in current policy | Both false in live PR parameters | No inferred one-approval rule from a ruleset name |
| Bypass | Current policy allows none | All three have empty bypass actors; caller cannot bypass | No bypass added |
| Deletion/non-fast-forward | Prohibited | Active ruleset prohibits both | Preserved on default branch |
| Update restriction | Import payload separately proposes Owner Merge Authority | No live `update` rule returned | Name “Owner Only” does not establish update-actor restriction |
| Merge methods | Registered disabled strict allows merge/squash/rebase | Active permits merge/squash | Explicit configuration difference |
| Security protection | Separately prove applicability | Branch `protected=false`, effective rules `[]`, classic protection HTTP 404 | SECURITY_BRANCH_PROTECTION_VALID=false |

The authoritative policy and six-check registry conflict with the retained
administration import templates: the templates describe an owner always-bypass,
four checks and a separate update restriction. They are not the live state or
an instruction to import those settings. Their exact source bytes are bound in
the observation. Applying them would change the current six-check/no-bypass
boundary and is not a valid identity-only correction.

RULESET_ENFORCEMENT_VALID=false refers to the registered policy contract.
It does not mean the alternate active ruleset has stopped enforcing its six
checks on Main. CHECK_EXECUTION_VALID is recorded per exact PR head above.
INDEPENDENT_REVIEW_VALID remains NOT_ESTABLISHED.

## Owner resolution options

1. **Restore registered identities while preserving the current protections.**
   Review the captured parameter differences, then separately approve activation
   of `22302146` and `22302157` with the current six context/app/strict identities,
   current explicit review policy and no bypass. Keep `24643016` active during
   reconciliation to avoid a protection gap. Independently verify effective
   rules before separately deciding whether to retire the redundant ruleset.
   Risks: overlapping rules combine; stale import templates could weaken or
   unexpectedly restrict access; merge-method differences need an explicit
   decision. This is the recommended minimally disruptive identity resolution
   if the existing registered contract remains Owner intent.
2. **Adopt the alternate identity through a reviewed policy migration.** Separately
   approve exact policy/registry/documentation changes naming `24643016`, and
   explicitly resolve the second registered ruleset and import-template drift.
   Preserve all six context/app/strict identities and security/review requirements.
   Risks: accepting live state as policy can legitimize accidental drift;
   identity substitution alone proves neither independent review nor Security
   protection. No policy migration is approved by this evidence record.
3. **Retain the hold.** Keep settings and contracts unchanged while gathering
   the missing decision text and independent review. This avoids an unintended
   administrative change but leaves Security unprotected and integration blocked.

Security protection needs its own approved payload: exact branch inclusion,
all six producer identities, strictness, review semantics, no unapproved bypass,
and update/deletion restrictions. It must not be inferred from a default-branch
ruleset. Any change to enforcement, ruleset identity, review, bypass, branch
scope, merge methods or required-check execution contract needs separate
explicit approval and independent post-change verification. None was applied.

## Existing work and remaining program gaps

The 45-capability ledger remains the version in PR #1738 at
`b34a0503ca0637634b3f492b69f3d3491c12c6d2`; it is referenced rather than
recreated or imported into this unmerged branch. Its baseline, original actor
failure, first inventory failure, cancelled intermediate heads, SEC-05 37-case
reconciliation and incomplete 404-operation/eight-Hub classifications remain
intact. Later success does not overwrite those observations.

PR-I can reuse existing Sonar native producers and all Qodana parity,
artifact-provenance, signed-subject and Cloud-layout tools. Cloud-layout
preflight explicitly does not upload; signature checks retain
`qualifiedForGateReuse=false`. Both existing analyzers must remain until real
same-report Cloud ingestion and all output/failure contracts are independently
qualified. #1500's three historical parity pairs are not current-head proof.

PR-J still needs applicable source/executed identity reconciliation and new
high-risk real-backend journeys. This refresh preserves execution identities;
metadata success does not resolve #611's original unknown failures or prove
product causality. No retry, timeout change, skip list or assertion change was
introduced.

PR-K already has layer tests and deliberately invalid ProjectIDE platform/
Environment fixtures. They currently run within the build producer; independent
architecture-lane execution remains separate #828 work. #795 explicitly approves
paired Angular Web and Windows/Avalonia directions; a browser viewport or shared
fixture is not a real Windows host. Existing axe/snapshot owners remain active;
new baseline approval and a seventh required context remain OWNER_HOLD.

PR-L reuses #1696 and #1680/#1717. Historical job times and different source/base
pairs do not qualify a whole-CI speedup. Protected sharding remains disabled;
trusted artifact ID/digest, complete disjoint executed-case union and compatible
repeated full-DAG measurements remain unmet admission requirements.

PR-M requires a separate decision for each verified gap. No new scanner, load,
soak, mutation, visual or OAST tool is admitted by this refresh. No production or
third-party load traffic was sent. Source/configuration, local execution,
branch-native execution, trusted qualification, schedule registration and
nightly activation must retain separate states. Branch-only schedules cannot
establish default-branch activation while Main modification is prohibited.

PR-N remains incomplete: the supplied contract is truncated, implementation
slices are not all qualified, and governance/review holds remain unresolved.

## Decision and PR-N recovery addendum

The Owner has now supplied the exact approved [D01–D15 wording](approved-decisions.md),
the [complete PR-N contract and exit criteria](pr-n-contract.md), and explicit
authority to persist them in this existing Draft PR. The earlier missing-input
statements above describe the original observation; those inputs are now
recovered. Their recovery does not resolve governance, review, trusted Sonar
import or PR-J–N acceptance, and grants no settings or integration authority.

The [machine-readable contract](program-contract.json) and
[independent state/HOLD register](program-status.json) preserve those boundaries.
The [preservation manifest](evidence-preservation.json) pins the unchanged
45-capability baseline and original source/run observations; this addendum
retains the entire preceding README as an append-only historical record.

The prescribed CodeRabbit CLI is available and authenticated in the existing
`Ubuntu` distribution when selected explicitly with `wsl -d Ubuntu`. The default
`Ubuntu-22.04` distribution still has its original missing-disk error. No disk,
registration or default distribution was changed. Availability is not completed
review; all failed attempts remain retained and no manual review is credited.

Focused commands for this documentation/validation-contract slice:

```powershell
python docs/verification/ci-assurance-continuation-20261106/validate-contract.py --verify-preservation
python docs/verification/ci-assurance-continuation-20261106/test-contract.py
```

These checks validate contract structure, references and source preservation.
They do not authenticate native artifacts, implement the final cross-domain
verifier or establish program acceptance. Current-source native CI and mandatory
independent review require their own retained evidence and qualification.

Clarification of the original Exact-head refresh table: it lists the five heads
evaluated by the unchanged required-check evaluator. The native-refresh record
and hash-bound archive cover all twelve checkpoint Security-targeted PRs,
including the seven heads outside that bounded evaluator table. This addendum
resolves the review ambiguity without altering the original observation.
