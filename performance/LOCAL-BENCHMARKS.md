# Local performance evidence migration (staged)

## Current boundary

This is the bootstrap governance proposal for moving PERF-04/PERF-05
measurement to an isolated local Docker host. It does not activate a new
required check or declare migration complete. Existing `performance-fast`,
its GitHub Actions integration, hosted workflows, thresholds, baselines,
failed cohorts, CODEOWNERS and ruleset remain in place until ordinary
protected migration merge and qualification.

`performance/local-policy.json` is deliberately staged with no signers,
EnvironmentClass approvals, local baseline enrollments or campaigns.
Verification always returns `requiredCheckCredit=false` in this implementation.
Changing the phase to `active` is rejected. A separately reviewed activation
implementation is required; a configuration toggle cannot publish a PASS.

No product code or performance threshold is changed. No historical failure
is rescued. The fixed Announcement recovery collector and its byte digests
remain unchanged. The local Announcement explorer imports its observation
helpers through a separate entry point and creates a new non-acceptance record.

## Execution ownership and commands

Use Ubuntu 24.04 on WSL2 or a compatible Linux Docker Engine. Hardware is
observed rather than assumed from Docker limits. Other Ubuntu versions may
supply diagnostics but never implicitly compare to an approved class.
Use an output directory outside the clean source checkout.

```bash
python3 scripts/performance/performance-local.py preflight \
  --base-sha <exact-main> --mode LOCAL_ACCEPTANCE --output /var/tmp/perf-preflight

# Full fresh-stack preflight: health/authentication/fixture/query capture,
# image identities, volume/network isolation, runtime fingerprints; no samples.
python3 scripts/performance/performance-local.py runtime-preflight all \
  --source-sha <exact-candidate> --base-sha <exact-main> \
  --id <unique-preflight-id> --db-runtime production \
  --output-root /var/tmp/coglatas-performance

python3 scripts/performance/performance-local.py api fast \
  --source-sha <exact-candidate> --base-sha <exact-main> \
  --id <unique-diagnostic-id> --output-root /var/tmp/coglatas-performance
# Replace fast with regression or diagnostic for five paired baseline/current groups.

python3 scripts/performance/performance-local.py db structural \
  --source-sha <exact-candidate> --base-sha <exact-main> \
  --id <unique-diagnostic-id> --output-root /var/tmp/coglatas-performance
# duration preserves the canonical duration streams without acceptance credit.
# announcement runs one separate 5 x 20 detailed diagnostic, never a replacement.
```

API source is pinned by exact candidate and the unchanged `api-k6.json`
baseline SHA. Five regression groups use baseline then current on the same
host, with an independent fresh fixture for every side. Authentication,
warm-up and mutation preparation retain the canonical boundaries. The local
adapter preserves all custom k6 scalar points in original order, allowlists
their fields, and derives percentiles using the existing comparator's
linear-interpolation method. Error/timeout counts and integer active-clock
ticks are independently replayed. No sample is selected or pooled.

DB collection reuses `db-probe.py`, `db_gate.py` and `db-compare.py`.
Every command capture, both page sizes and all sample ordinals remain.
Canonical page-5 duration streams are reconstructed from command durations.
The unchanged requirements are API 78/78, structural 28/28, small 9/9,
medium 9/9 and relative MAD <= 0.20.

The complete runtime preflight precedes the exclusive measurement-started
marker. It consumes no measurement attempt. Reusing an output ID is rejected.
A failed acceptance campaign cannot restart with another ID because the
trusted campaign fixes its evidence ID, source/tree/base/baseline, tool
digests, classes, hardware, k6 image, expiry and attempt 1. No retry loop exists.

Process output is kept private. Failures retain stage, exception class,
sanitized code, component, operation, process status and collected sample
count. Timeout/signal handling stops owned process groups and scopes Docker
cleanup to the generated project. Cleanup failure is blocking. Partial
samples and failed group status remain available.

## Forward enrollment and acceptance

Before any official local acceptance, the owner must separately approve:

1. Actual canonical EnvironmentClasses from full runtime preflight, with
   physical-host and guest/cgroup/Docker resources recorded separately.
2. A dedicated Ed25519 public key and signer identity, validity interval and
   revocation policy. Keep its private key outside repositories and Actions.
3. An API enrollment for the existing pinned Main source on the local class.
   This does not advance the API source baseline.
4. Separate small/medium local DB baseline enrollment campaigns, fixed before
   capture, retaining every ordered sample/group and failed disposition.
   Local DB documents use
   `performance/baselines/db/local/<profile>/<class-digest>/`.
   Existing hosted catalogs, sample bytes and approvals remain immutable.
5. A prospective acceptance campaign for a justified new candidate.

An approval object records the actual owner, approval time and native
GitHub approval reference. This proposal supplies no such approval and must
not be used to invent one. Capturing/promoting local DB baselines and
authenticating owner approval references in activation governance remain
follow-up work. Exploratory diagnostic data cannot be enrolled retroactively.

Missing approvals block with `CAMPAIGN_UNAVAILABLE`,
`ENVIRONMENT_INCOMPATIBLE`, `BASELINE_UNAVAILABLE` or signer failure.
The new namespace is not searched for a favorable hardware cohort. Windows
and Linux results never silently compare. The current unsupported/unobserved
host is not assigned a fabricated EnvironmentClass.

After those dependencies and the activation implementation are approved:

```bash
python3 scripts/performance/performance-local.py acceptance all \
  --source-sha <exact-candidate> --base-sha <exact-main> \
  --id <approved-evidence-id> --campaign-id <approved-campaign-id> \
  --output-root /var/tmp/coglatas-performance
python3 scripts/performance/performance-local.py seal \
  --output /var/tmp/coglatas-performance/<approved-evidence-id> --signer <approved-identity>

# A distinct signing invocation. A hardware/agent-backed key is preferred.
python3 scripts/performance/performance-local.py sign \
  --bundle /var/tmp/coglatas-performance/<approved-evidence-id>/bundle \
  --key <dedicated-key-outside-repository>

python3 scripts/performance/performance-local.py verify \
  --bundle <bundle> --expected-sha <exact-head> --expected-base <exact-base> \
  --expected-tree <exact-tree> --pr-number <number-or-zero> --output <verification.json>
```

The signed manifest binds repository/PR/source/tree/base/baseline, campaign,
hardware/runtime/fixture/workload/comparator identities, k6 image ID,
every file SHA-256, all group order, execution interval, completeness,
attempt and predecessor inventory. The detached SSH signature has its own
namespace. The public-only `performance/local-allowed-signers` registry must
exactly match the approved metadata; verification does not write key material. Unknown/revoked signers, altered signatures, extra/missing files,
unsafe paths, duplicate JSON fields, expired evidence, wrong SHAs, missing
samples and non-PASS statistical outcomes are blocking. A claimed result
must exactly equal independent replay. Signing cannot change a failed result.

Diagnostic bundles have a separate unsigned integrity manifest. `replay`
can verify their hashes; it reports `LOCAL_DIAGNOSTIC`, no signature and zero
acceptance credit. `verify` cannot promote them.

## GitHub pilot and activation work

`performance-local-evidence.yml` is verification-only and manual, running
only from Main. It checks out trusted Main, resolves the live PR/Main source
and Git tree through GitHub, then reads an immutable same-repository evidence
commit's `bundle/` through the Git tree/blob API as data outside the workspace.
It never checks out evidence code and uses
no caches or signing secrets. The pilot has only contents-read permission
and never emits `performance-fast`.

The intended final gate must additionally authenticate owner governance,
publish the existing required context from the same regular integration,
keep revalidation independent of local measurements, and remove all hosted
collector entry points by a normal migration PR. The bootstrap does not
claim those activation steps are complete.

Current measured call sites on Main:

| Workflow | Line | Collector |
| --- | ---: | --- |
| performance-api.yml | 97 | run-api-k6.sh |
| performance-api.yml | 133 | run-api-diagnostics.sh |
| performance-db.yml | 137 | db-probe.py |
| performance-db-candidate.yml | 166 | db-probe.py |
| performance-db-baseline-capture.yml | 72 | db_campaign.py capture |

`main-build-artifacts.yml:532` calls `performance-environment.yml`; its two
`with-environment.sh` invocations perform environment/fixture checks without
an API/DB duration collector. `performance-environment-pr.yml` is likewise
environment validation. #1046 separately adds Main DB integration and remains
subject to its unresolved acceptance blockers.

`audit-workflows --require-migrated` currently fails and lists the remaining
collectors. Its narrow command inventory is an audit aid, not a proof against
arbitrary dynamically constructed shell commands; activation must review the
complete workflow/helper call graph and extend enforcement accordingly.

The bootstrap PR must complete existing required checks normally. If required
checks block migration, stop for the owner's concrete governance decision.
Never remove contexts, alter rulesets, bypass protection, or repeat a failed
benchmark merely to obtain green.

## Qualification and stop boundary

No Docker runtime measurement was executed on the current Windows host:
WSL2 Ubuntu 22.04 fails to mount its missing disk; Docker/Compose are unavailable.
CPU is Intel Core Ultra 7 155H, 16 physical/22 logical cores; observed RAM is
14,505,033,728 bytes, with roughly 2 GiB available at audit time. A working
Ubuntu 24.04/Docker Linux environment and sufficient available memory are
needed before runtime preflight. No numeric local acceptance minimum is
approved yet; do not infer one from the hosted 4-vCPU/16-GiB class.

Unit/synthetic signature tests cannot establish runtime correctness, host
suitability, product causality or acceptance. k6 scalar stream ordering,
paired Docker runs, production DB builds, announcement sidecars and cleanup
under real interruption still need Linux/Docker evidence before activation.

#1046, #606 and #1056 remain open. Original API 37726296594/1 and DB
37656457914/1 failures and replacement diagnostic 37730145413/1 remain
unchanged. New Main qualification must run independently after #1046's
ordinary merge. Avalonia/ProjectIDE implementation has not begun.
