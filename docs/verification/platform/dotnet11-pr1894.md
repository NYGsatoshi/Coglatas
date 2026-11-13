# .NET 11 GA migration candidate: PR #1894

This candidate consolidates the closed, unmerged experiment PR #1556 into PR #1894.
It is **Draft / NO MERGE / NO DEPLOY**. Main remains on .NET 10.

## Dependency decision

- SDK: exact `11.0.100` with SDK roll-forward and prerelease disabled.
- All nine C# project targets: `net11.0`.
- ASP.NET Core runtime, OpenAPI and Microsoft extensions move to the reviewed `11.0.0` candidate; Docker base images retain immutable digests.
- EF Core (including Design, Relational and InMemory) stays on stable `10.0.15`; `dotnet-ef` stays at `10.0.15`; Npgsql EF Core provider stays at `10.0.3`.
- Npgsql 10 restricts the supported EF Core major version to 10. The available Npgsql 11 RC3 has exact RC3 EF Core dependencies; mixing it with EF Core 11 GA would reintroduce NU1107/NU1608.
- SignalR client `11.0.0` and server `11.0.0` require a real-backend integration regression.

## Verification boundary

All five NuGet lockfiles were regenerated using the exact .NET 11 GA SDK and committed from the successful CI restore. The isolated workflow now **requires the canonical NuGet lock policy and `dotnet restore --locked-mode`** before compilation and architecture/portability tests. This validates only the candidate smoke suite, not the full release gate. Never edit package hashes by hand.

Do not approve this PR until final-HEAD official CI (including locked restore and negative mutation tests), security/release gates, PostgreSQL migrations and rollback, HTTP and OpenAPI contracts, full frontend tests, runtime containers, and branch protection pass. Historical results from #1556 do not establish qualification for #1894.

## PR CI regression (2026-11-13)

The final PR CI run for HEAD `139a660` exposed two blockers not covered by the isolated .NET smoke suite:

- Frontend third-party pin check still required SignalR `10.0.11` despite the coordinated `11.0.0` manifest and integrity-pinned lockfile. The exact pin assertion now requires `11.0.0` and retains manifest/root/installed integrity validation.
- Docker runtime and prebuilt runtime kept Ubuntu 24.04's `libapt-pkg6.0t64` despite the digest-pinned .NET 11 ASP.NET Core image using Ubuntu 26.04 `resolute`. Both runtime Dockerfiles now explicitly require Ubuntu resolute, upgrade `apt` and `libapt-pkg7.0`, and enforce the patched minimum `3.2.1ubuntu0.1` for both. The existing `curl`, `openssl`, and `libssl3t64` package steps are preserved.

These changes must be independently rerun on the new exact HEAD. The failing historical job remains evidence, not a pass. Production release and merger gates remain unchanged.
