# Supplemental native Windows coverage

The existing mandatory Linux backend OpenCover cohort and original native report
remain required. Production assemblies use deterministic source paths so the
scanner can bind both platform reports to its source mapping. The test project
is then rebuilt without dependencies and with physical source paths, because
existing HTTP, SignalR and worker evidence recorders read and hash their actual
`CallerFilePath` source. Production assemblies are not rebuilt by that step.
Before the test-only rebuild, the original native Coverlet source-root mapping
is retained. The rebuild generates physical reference mappings despite leaving
deterministic production PDBs intact; using that replacement mapping prevents
instrumentation of production modules. The workflow restores the byte-identical
original build mapping before collection and compares it again afterward. Both
original metadata files remain in the artifact. This restores an actual native
instrumentation input; no collected report, path, hit or condition is rewritten.
The artifact also retains the original Linux TRX, pre-rebuild outputs and the
seven executed DLL/PDB pairs. Existing native binary checks compare each project
output with its test-directory copy and require the full source revision. All
six production pairs must remain unchanged across the test-only rebuild and
collection. The sidecar binds their digests, original mapping and TRX to the
source/run/attempt; the changed test assembly is retained separately.
The Linux verifier maps the exact `/_/` source prefix to the current checkout,
rejects noncanonical paths, and preserves report bytes and zero-hit points.
The trusted push analysis additionally runs the existing portable .NET cohort
on a fresh Windows runner, with the repository-pinned SDK and Coverlet collector.
The Windows job has read-only contents permission and no analysis environment
or Sonar token. Failure prevents the dependent analysis job from publishing.

Coverlet emits its native deterministic Cobertura report. The artifact retains
the original collector report and native VSTest attachment, original TRX, seven
executed project DLL/PDB pairs and a source/run/attempt receipt. The one imported
`verified.cobertura.xml` is byte-identical to the collector report. There is no
XML rewriting, hit synthesis, zero-hit removal or duplicate report import.

Producer verification compares deterministic C# source paths and bytes with the
exact Git revision, validates source line bounds and native condition counts,
and requires actual nonempty all-passed tests. It compares original project
outputs with executed test-directory binaries and requires the full revision in
each DLL. The Linux consumer repeats raw/source/TRX/binary digest checks against
the expected SHA, tree, workflow run and attempt before analysis credentials.
Git selection is an explicit absolute host executable outside the checkout.
This CI verifier requires a direct installation file: original entries and all
parent components reject repository containment, symlinks and junctions before
resolution. It launches the checked canonical file. This bounded CI policy
does not change the separate C# CLI's host-alias compatibility contract.

Known regex and OpenAPI compiler-generated virtual documents retain their
original absolute `obj` filenames and ranges in the report and receipt. They
are explicitly unmapped generated evidence, never Git-source coverage credit.
All other nondeterministic or unmappable source paths fail verification.
Neither the existing analysis scope nor its coverage exclusions are changed.

This adds a candidate collection/import path for native Windows-only branches. It does not
establish a PR Gate result or Main coverage gain by itself. The original #1778
official Gate failure at `e8d5c1c6b9c199b1c16ba539ea56e9eeb87c1def`
(New Coverage70.1%) remains retained. Qualification requires the actual hosted
Windows artifact, independent review, official native parser/source mapping,
the full denominator and resulting Main covered-line/condition measurements.
Mixed-format results must not be estimated by adding local percentages.
No security finding disposition or release acceptance is granted.
