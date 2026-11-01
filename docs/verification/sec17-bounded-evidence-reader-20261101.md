# Bounded execution-evidence input

This preparation is based on Product Main
`49962886a875d30dd16c822e4b25b1dccd205ae6` and Spec Main
`850a5d9376db11e75235cf55e03ebed4de9c0510`.

The SEC-ARCH, bounded SEC-17 deep and File-policy execution captures share one
regular-file reader. It enforces the existing 64 MiB XML limit before reading,
checks the opened descriptor and stable file metadata, and bounds the read to
the observed size plus one byte. Both SEC-17 failure summaries use the same
reader and retain a null execution digest for unavailable input.

The XML catalogue, owner selection, outcome rules, receipt schemas, source/build
bindings and sanitized diagnostics retain their existing contracts. There is no
new path sandbox, attacker-reachability assertion, Sonar disposition or claim of
complete SEC-14/SEC-17 assurance. All provider findings and Owner holds remain.

Local verification uses a separate synthetic Python 3.12 and Git container with
network disabled during execution. The existing parser/capture and adjoining
accounting cohort passes 121 tests; bounded deep and File-policy suites each
pass 10 tests, with no failures or skips. Six new reader regressions cover exact
bytes, size admission, nonregular metadata, descriptor replacement, metadata
changes and agreement with the parser limit. Both existing actual-Git capture
controls also exercise unavailable-input summaries. Separate private POSIX
controls exercise actual filesystem boundaries. Their retained receipts are
local evidence, not hosted or real-backend acceptance.

The original Main observation and independent current-candidate review remain
separate evidence. Required CI and official Sonar results must qualify the exact
published candidate; these local controls grant no release approval.
