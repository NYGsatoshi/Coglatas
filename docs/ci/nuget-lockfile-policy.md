# NuGet lockfile and restore policy

## Scope

The repository commits NuGet `packages.lock.json` files for projects with
direct `PackageReference` items. Current owners are:

- `src/Coglatas.Application`
- `src/Coglatas.Infrastructure`
- `src/Coglatas.Web`
- `tests/Coglatas.Architecture.Tests`
- `tests/Coglatas.Tests`

Projects without direct NuGet package references do not need a lock file solely
for project references. Add a lock file when a project first gains package
references. Do not manually edit NuGet-generated dependency graphs or hashes.

## Enforcement

Each owning project opts into `RestorePackagesWithLockFile` and enables
`RestoreLockedMode` when the environment variable `CI=true`. The PR and
Main build producers also explicitly use `dotnet restore --locked-mode`.
Production Docker copies the three application lockfiles into the pre-restore
layer and uses `--locked-mode` on *both* restore calls, including the
post-copy forced restore. `--force` is safe with locked mode; do **not**
use `--force-evaluate` in an enforcement path because it rewrites the lockfile.

A green restore must not depend on lockfile regeneration or a NuGet cache.
NuGet validates the resolved dependency closure and package content hashes.
This does not eliminate all supply-chain risk or guarantee faster builds.

## Dependency update procedure

1. Start from the current Main SDK in `global.json` and a normal writable
   working tree. Outside CI (with `CI` unset), update the `.csproj` dependency
   manifests as part of a reviewed Renovate/manual dependency update.
2. Run `dotnet restore Coglatas.slnx --force-evaluate` in the updating
   environment **without CI locked-mode enabled**. If the update agent exports
   `CI=true`, use `CI=false dotnet restore Coglatas.slnx --force-evaluate`
   on POSIX shells, or set `CI` to `false` explicitly in that job.
3. Commit all affected generated `packages.lock.json` files alongside the
   manifest changes, including referencing-project lockfiles where NuGet
   updates the transitive graph.
4. Review package version, graph and content-hash changes; check advisories,
   provenance and any package license differences. Never edit `contentHash`
   to appease a failing validation.
5. Verify using `CI=true dotnet restore Coglatas.slnx --locked-mode`
   (PowerShell: `$env:CI='true'; dotnet restore Coglatas.slnx --locked-mode`)
   and the required PR checks. Merge is separately approved.

The current Renovate `nuget` grouping stays in place; this policy does not
automatically merge or approve dependency updates. A malformed bot-generated
lockfile requires a repaired, independently verified update PR, not disabling
locked mode.

## Failure diagnosis

- **NU1004**: the manifest/framework/graph disagrees with the lockfile;
  regenerate it in the dependency-update flow and review the resulting diff.
- **Package hash mismatch**: inspect the registry source, provenance, cache
  integrity and possible upstream change; do not suppress the mismatch.
- **Docker first-restore failure**: check that each committed production
  lockfile is copied before running restore, and that the SDK tag/digest agrees
  with the version in `global.json`.
- **Platform/SDK drift**: reproduce using the pinned SDK and record the
  exact OS/RID and changed dependency graph before modifying files.

Reference: https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files
