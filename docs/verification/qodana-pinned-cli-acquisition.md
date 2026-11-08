# Qodana pinned CLI acquisition

Main `3d1f17f2b0cc2d9f1d8d24b7e50de17ea744cabd`, CI
`42465305779` attempts 1, 2 and 3 failed Community initialization with HTTP 504
while the pinned action downloaded its CLI. No Community inspection inventory
was produced. These failures remain failures. Cloud's independent full scan
passed; it does not substitute for Community's missing result.

The Community workflow installs the same official Linux x86_64 CLI 2026.2.2
using bounded HTTPS transfers, then uses the pinned action's supported
`use-installed-cli` input. The archive SHA-256 remains
`b1fc01fb5825f4c6382f9cedcfe97d4a8eeaaddc9f5384a3b5870ca63e231c6f`,
matching the original pinned action. Verification precedes extraction and
execution. The fixed public Release Asset API (asset `585558698`) is tried first,
with the same release's browser URL as a bounded fallback. Both require HTTPS
including redirects, and both must satisfy the same digest. No token is used.
This uses GitHub's documented binary asset retrieval contract:
<https://docs.github.com/en/rest/releases/assets#get-a-release-asset>.
Each installation uses a fresh runner temporary directory; nothing
is promoted to a global cache. Failed acquisition or verification fails the job.

External PRs restore this executable from the immutable PR base alongside the
existing Qodana policy. Action/image pins, inspection budgets, full/PR scan
modes, build artifact lineage, assertions and coverage contracts are unchanged.

The non-required, read-only Linux acquisition workflow executes refusal controls,
downloads the actual authenticated release and runs its native version command.
It separately injects an API transfer failure and supplies the already authenticated
archive on the browser fallback, then checks actual installation/version execution
and binary identity. That control tests fallback logic without claiming a second
real network transfer or adding a production URL/hash override.
That proves acquisition only. Protected PR checks and independent source review
must still complete, followed by both full Qodana scans on the resulting Main.
No Sonar coverage improvement, Security disposition or release approval is implied.

The first repair HEAD `9d1b65ae` passed all four native refusal tests but its
direct browser-URL download also failed with HTTP 504 in acquisition run
`42470034306/1`. Static analysis `42470034371/1` independently failed while
downloading actionlint, before linting. Neither is claimed as a passing run.

At subsequent HEAD `a34d0622`, acquisition run `42470208586/1` passed the four
native refusal tests and downloaded, verified, extracted and executed CLI
2026.2.2 on Linux. That is acquisition evidence, not a full Qodana scan.

Workflow static analysis encountered the same actionlint acquisition failure.
Its existing actionlint 1.7.12 and zizmor 1.30.1 downloads now use their fixed
official binary API assets (`384924896` and `552067642`) before the original
release URLs. Both original SHA-256 values, versions, Python controls, lint
commands and enforcement decisions remain unchanged. Both transfer routes are
bounded and require HTTPS redirects. No credential or lint suppression is added.
This independently scoped repair does not promote or merge PR #1676.

CodeQL initialization also repeatedly failed on the original bundle browser
release routes. A local Python archive override at historical HEAD `a6780a99`
passed actual Python analysis, but source review identified that it bypassed the
original feature-flag/overlay-aware version selector. That approach is withdrawn.

Instead, warm the action's normal runner tool cache with the official combined
2.27.4 Linux bundle (public asset `692235033`), SHA-256
`da3e952c7fe978a0b6b93e75d9cbabbdf8535667ca0810bd4b2ae37bc840ffee`.
Only the combined bundle is suitable for the normal shared-version cache.
Verification precedes extraction; the completion marker follows the actual CLI
version check. Existing complete entries are retained. Incomplete entries remain
untouched and the original selector proceeds. If both optional warm transfer
routes fail, no bundle is extracted or marked complete, and the original selector
and acquisition proceed. A detected integrity mismatch still fails before
extraction or execution. No existing cache entry is deleted or replaced by the
new warm step; the pinned action retains its own original cache cleanup behavior.

The original action inputs and selector remain unchanged: its normal version
resolution happens before lookup, and it can use this ordinary cache entry only
when it selects 2.27.4. It can still select its bundled default, feature-flag
update/rollback or overlay-compatible version; a different selected version takes
its original acquisition path. No `tools` override, pinned-version alias, feature
flag, action mutation or global environment override is added. This is runner
cache preparation, not an independent version-selection policy. Future original
version changes require fresh transport qualification; the cached version must
never override them.

All four language/build-mode entries, action pins, queries, analysis categories,
permissions and enforcement remain unchanged. All four actual current analyses
and original SARIF must qualify the final HEAD. Cache preparation alone is not
analysis or security acceptance.

The acquisition workflow adds a separate read-only initialization control. It
prepares the actual different version 2.27.3 in a job-isolated runner cache shared
by warming and initialization. It seeds the pinned action's normal feature-flag
cache with a controlled response selecting 2.27.3, executes the production warm
source with both transfers failing, and checks that no 2.27.4 executable or marker
exists. It then invokes the unchanged action's normal resolver without a `tools`
override and asserts its actual selected version/path outputs and CLI execution.
The cached feature response and cache/temp directories are fixtures confined to
this read-only job; no live feature response, analysis, SARIF publication or
product-workflow override is claimed.
