# J17 synthetic Windows UIA3 investigation — NO-MERGE

Authority: [Owner J16/J17](https://github.com/NYGsatoshi/Coglatas/issues/2197#issuecomment-7234936491), with CI/test source ownership retained under #795. This diagnostic feature is based on `SecurityTestImplimation@5c9228731c4063c4a598af6686b1d226f000422d`; its external Draft PR must target that branch and remain NO-MERGE. This is an isolated technology investigation, with no product solution, default dependency, framework, protected check or Native product implementation change. No integration outside ProjectIDE canonical is authorized by this work.

The separate synthetic Desktop host uses Avalonia.Desktop and Avalonia.Themes.Fluent **12.1.6**. Its test-only automation harness uses FlaUI.UIA3 **5.0.0**. Both use isolated SDK **11.0.100** and preserve the exact evaluated package lock graphs. The harness explicitly spells its existing normalized `net11.0-windows7.0` target to satisfy the unchanged repository lock guard; this is not a Windows 7 support promise or a product framework choice. `origin-assets.json` pins the preceding investigation source bundle and inputs.

## Actual Windows observations

The nonrequired workflow runs only for this exact same-repository diagnostic branch, a Security-targeted Draft PR and its NO-MERGE title. It checks input-desktop feasibility before restoring/building or starting an app. A service session, inaccessible or locked input desktop produces **NOT_RUN** evidence and a failing diagnostic job, never a headless substitute or PASS. No desktop is unlocked or switched.

The harness launches only the built synthetic host and binds all UIA elements to its owned child PID. It checks the accessible input name, actual keyboard text, Tab focus, Enter activation and window/process closure. Before every keyboard action it rechecks the interactive desktop and the child's foreground ownership. Focus drift fails the probe without sending more input. Cleanup uses only the original child process handle; the bounded parent fallback additionally compares PID, creation instant, executable path and hash. It never terminates a process tree or a process selected by name.

The artifact contains a finite source/observation receipt, binary hashes and dependency verification. It contains no desktop screenshot, unrelated UI tree, user/session data or private child-ownership file. Ten receipt mutation controls refuse headless/NOT_RUN, missing observations, string-valued booleans, failed cleanup and invented IME/product/dependency-adoption credit. These controls establish validator behavior, not Windows UI execution.

IME composition and screen-reader acceptance remain **NOT_RUN**. A successful UIA accessible-name observation is not a screen-reader claim. This synthetic app has no Coglatas product references, server, account, tenant or operational provider. Actual Native product acceptance remains blocked by #1144/#1148 and the other existing #769/#794/#795 conditions. Angular Web verification remains separate and unchanged.

## Dependencies and evidence

The host resolves 25 packages and UIA harness 13. NuGet is restricted to the explicit nuget.org source and source mapping. Locked restore validates NuGet content identities; the probe separately verifies every cryptographic package signature and the exact frozen license declarations/files. Package archive hashes are recorded separately from NuGet lock content hashes. Missing or failed verification fails the diagnostic before UI execution.

`dependency-inventory.json` records exact versions, content identities, licenses and native assets from the original investigation. The four file-based license texts are retained under `licenses/` with repository LF line endings; `licenses/identities.json` preserves original-byte and repository-text hashes. Package verification checks the original license bytes inside each package. The other declarations are MIT. These are investigation dependencies, with no product Tier, signer-policy, notices/SBOM or dependency baseline adoption. Existing Dependency Review and all mandatory repository checks remain applicable and unchanged.

Local cross-compilation, headless results and prepared configuration cannot qualify this actual Windows lane. Actual execution is pending until the exact hosted job and original artifact are inspected. Passing this diagnostic cannot close #769/#794/#795, adopt a formal evidence contract, activate RLS/Trust, authorize a shared Merge or qualify a Release.
