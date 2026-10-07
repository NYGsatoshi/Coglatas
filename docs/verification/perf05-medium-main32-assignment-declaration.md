# Prospective Medium Main32 environment assignment declaration

NOT approval. No measurement authorized. Successor capture count = 0.

This is one direct prospective successor under the already owner-approved
`perf05-environment-assignment-v1`. No new campaign measurement, result,
baseline, approval-ledger record, or transition-rule change is introduced.
The declaration PR must stay open and unmerged until the owner authorizes this
exact Campaign ID and canonical manifest digest at the new fixed reference.

## Fixed prospective identity

- Campaign ID: `perf05-medium-20261007-main32-amd9v45-assignment-v1`.
- Canonical manifest SHA-256: `f3eabe64864458654e2c157b2a5ba125372f8bf2e652bb30bf79972f8650a3d4`.
- Profile: Medium; schema 3; policy `perf05-db-campaign-v1`.
- Qualified current Main: `de46efbcadb4828b68ce14e4b3d263af7d1a4331`.
- Rule-introduction Main: `361bf453b0ddc53af98fe790b30b83b683e81205`, in the qualified Main's first-parent lineage.
- Unchanged measured Main source: `32a17bde8f7f21ab8670265ae75d21ed081e27b9`.
- Target full environment digest: `e3fd3025c8dd91dc45f3447350915f978ee1e9e6555264a5456b65b4896a0095`.
- Created UTC: `2026-10-07T08:55:41Z`; JST: `2026-10-07T17:55:41+09:00`.
- Expires UTC: `2026-10-10T08:55:41Z`; JST: `2026-10-10T17:55:41+09:00`.
- New fixed authorization reference: https://github.com/NYGsatoshi/Coglatas/issues/606#issuecomment-6034448645.
- The fixed reference is an executor reservation marked `NOT approval`,
  `no measurement authorized`, and `NOT_APPROVED`. Neither existing rule
  approval nor predecessor approval authorizes this successor.

## Authenticated immutable predecessor

- Campaign ID: `perf05-medium-20261006-main32-intel8370c-publicdigest`.
- Canonical manifest digest: `0bc9a6442350949d0c5e4b2c46058f9e178d6de4be6e5d70327e3e7083100bf5`.
- Run/attempt: `37457509298/1`; declaration SHA:
  `b03b0be86d086ecefad38760479037f11d5de079`.
- Raw artifact `11411325219`; actual ZIP SHA-256:
  `7726ef3617a2d1452b977ce95ff51cf3a31ceef60315a97eded8ec7a1efd1bdf`.
- Metadata artifact `11411415134`; actual ZIP SHA-256:
  `c6b94255290193cbad401be2f970db4cc396e0829293cfeb346115d063d8b6bf`.
- Canonical ordered raw-groups digest: `dabfb4634c28f3ac399901466c081b39c48a2707f778adfb04737462a6670e0a`.
- Result: `BASELINE_UNAVAILABLE / ENVIRONMENT_MISMATCH`.
  Ordered groups 1, 2, and 3 remain permanently rejected for wrong environment;
  selected group is null and approved is false. No group is promoted or enrolled.
- Both fresh GitHub metadata identities and actual downloaded ZIP bytes were
  authenticated. Pinned-source selection replay exactly reproduces the retained
  rejected result; retained Main evidence and the live original raw archive match.
- All three complete groups have the identical canonical actual environment
  digest `e3fd3025c8dd91dc45f3447350915f978ee1e9e6555264a5456b65b4896a0095`. It differs from the
  predecessor's declared Intel8370C digest
  `e9c07b1d9fcc3bf7cdc9dd282f44c138d57ce0a0e14ab82db7ca1e5d4ea57443`.
  Timing, latency, stability, and MAD never choose the assignment.

## Canonical target environment identity

The following payload comes from the pinned measured-source comparator's
canonical identity generator, not manual CPU-name inference. Recomputing every
predecessor group's complete fingerprint reproduces the same target digest.
The CPU model describes physical host hardware; the assigned runner has four
logical CPUs.

```json
{
  "runnerOs": "Linux",
  "runnerImage": "ubuntu24",
  "cpuModel": "AMD EPYC 9V45 96-Core Processor",
  "cpuCount": 4,
  "dotnetRuntime": "Host:\n  Version:      10.0.12\n  Architecture: x64\n  Commit:       95017c711e\n  RID:          linux-x64\n\n.NET SDKs installed:\n  No SDKs were found.\n\n.NET runtimes installed:\n  Microsoft.AspNetCore.App 10.0.12 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]\n  Microsoft.NETCore.App 10.0.12 [/usr/share/dotnet/shared/Microsoft.NETCore.App]\n\nOther architectures found:\n  None\n\nEnvironment variables:\n  DOTNET_RUNNING_IN_CONTAINER              [true]\n  DOTNET_VERSION                           [10.0.12]\n\nglobal.json file:\n  Not found\n\nLearn more:\n  https://aka.ms/dotnet/info\n\nDownload .NET:\n  https://aka.ms/dotnet/download",
  "node": "v24.20.0",
  "postgresql": "18.6",
  "playwright": "1.63.0",
  "browser": "Google Chrome for Testing 153.0.8010.12",
  "postgresImage": "sha256:c293117fcecda7344b5480222e813b9f673d7abd69b1dd95eff239b768b04f59",
  "browserImage": "sha256:2c1f4e0fd6450f43ddb46d60c2a6df30855a8588e165b1f2559fb0eda8d7ff35",
  "fixtureProfile": "medium",
  "fixtureHash": "7dfa25e30fb7f87cd1457400171b75c0374713279bee3a4bc429a5d9fba7ca96",
  "fixtureVersion": 2,
  "applicationRuntime": {
    "configHash": "a2fcf843339f739111f650cfc27447c5ce7fb69110daf577ad301a9e15bc8fec",
    "mode": "production",
    "packageHash": "04285509192981a74e18f003c3567976a420307e5572bd23430d77985a4ec0f4",
    "schemaVersion": 1
  }
}
```

## Unchanged measurement contract

- Fixture: Medium v2, SHA-256 `7dfa25e30fb7f87cd1457400171b75c0374713279bee3a4bc429a5d9fba7ca96`;
  dataset manifest SHA-256 `86b179dcae9748c3157f1775cf5e9cc432ab5030c90b1dd1161c7007a4c22772`.
- Comparator: `scripts/performance/compare.py`,
  SHA-256 `5142d5a52da773c460637b25a85168242565d69ff7d103270d24a11a32d4cd91`.
- The manifest retains every predecessor tool version, all eight tool-source
  digests and all six contract digests exactly. They validate against immutable
  measured-source Git bytes, rather than current product code.
- Nine scenarios remain in their original order: workspace.list, project.list,
  task.list, task.my-tasks, file.list, conversation.list,
  conversation.messages, notification.list, announcement.list.
- Five ordered samples; relative MAD <= 0.20; maximum three serial groups;
  earlyStopPolicy never; earliest eligible stable complete selection.
- Ordinary runner assignment; unchanged workflow, source, fixture, scenarios,
  thresholds, comparator, scheduling, and sample/group bounds. No host search,
  runner-label manipulation, or retry until a preferred host appears.
- One direct transition only; no chain, cycle, prior-scope revisit, epoch reset,
  retrospective enrollment, backdating, or approval transfer.

## Qualified Main authority and preapproval validation

Main CI `37591714478/1` PASS on `de46efbcadb4828b68ce14e4b3d263af7d1a4331`. All six protected contexts
pass: build-test, frontend-test, security-scan, publication-readiness,
functional-fast, performance-fast. Functional-full, Licensed and applicable
Extended acceptance, Qodana Community/Cloud, CodeQL, SBOM, and API Performance
also pass on this exact Main.

Fresh Main Security artifact `11470523834`, actual ZIP SHA-256
`0c4a75aa46c9308ed0f238870261ffe5b5d0aee7b73bfd0b0c3345833f54e264`,
authenticates to this run/Main. alpha-owner, alpha-restricted and beta-owner
each have ZAP High=0. Required active rules, nonzero OpenAPI coverage, auth,
sanitizer and fail-closed scanner integrity pass. SEC-05 has 37 passing cases
including both protected invites denials; all five Main Schemathesis roles pass.
The original failed Main evidence is retained and never reused as acceptance.

Live governance evaluator PASS; rulesets22302146/22302157 retain the intended
owner-only review topology and protected checks, with empty bypass actors and
current_user_can_bypass never. CODEOWNERS retains `* @NYGsatoshi` and only that owner in every explicit entry.

Local declaration-only validation checks schema 3, the actual transition
validator and live owner rule decision, authenticated predecessor, canonical
environment identity, exact source/fixture/tool/comparator/contracts,
scenario order and bounds, new identity/digest, valid expiry, and fixed-reference
nonapproval. Normal applicable PR gates must also pass before the owner request.
This document contains no new campaign samples or measurement result.

## Mandatory stop

Request owner exact premeasurement approval at the new fixed reference only
after the declaration PR's applicable required checks are complete. Keep this
declaration PR unmerged and successor capture count at zero. A later approved
Main push must revalidate the exact fixed authorization before capture.

Small #1104 remains approved and immutable. #1046 stays Draft/Open/unmerged and
its historical 77/78 PASS plus one UNSTABLE cohort stays rejected. No final #1046
synchronization/acceptance, Avalonia, ProjectIDE, or deferred performance scope is
started.
