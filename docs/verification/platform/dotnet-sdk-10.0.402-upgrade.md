# Coordinated .NET SDK 10.0.402 upgrade — candidate

## Scope
Owner-authorized on 2026-10-19 in Dependabot PR #1268, without a Main merge. .NET SDK 10.0.402 was released on 2026-10-13 with .NET 10.0.13 runtime. The existing ASP.NET runtime image 10.0.13 remains unchanged.

- `global.json` pins SDK **10.0.402**, `rollForward: disable`, `allowPrerelease: false`.
- Production/dev/security Docker build images, root/Sakura/on-prem/local/test migration Compose and performance PR/standard images use `mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa`.
- Node Toolchain Preflight, performance environment validation, and Qodana packet current-head verifier expect SDK 10.0.402.
- Frozen historical Qodana packet worktrees require 10.0.401 **installed alongside** the new SDK. Their original preimage/payload SHA-256 evidence stays unchanged; historical packet proofs must not be rewritten.
- Do not bypass publication/security/functional/required-check gates or change production DB/RLS/configuration.

## Qualification and PR topology
- PR #1268 is the canonical SDK/toolchain upgrade. The existing Node 24 digest-only PR #1263 must be based on #1268 to avoid duplicating the SDK rollout.
- All mandatory same-revision PR CI results are required before merge. Until CI finishes, results remain NOT_VERIFIED. No Main merge or deployment is authorized by this candidate.
