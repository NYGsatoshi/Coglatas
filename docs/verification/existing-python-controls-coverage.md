# Existing Python operational control collection

The common native Python coverage producer now registers six complete existing
suites for Functional evidence, NuGet snapshots, SBOM inventory and API
normalization, diagnostics and collection. Its existing 40 suites and 51
subjects remain. Nine additional existing operational modules are measured,
including the shared Performance helpers and Functional CLI modules used by
these controls. The single producer and scanner import remain unchanged.

The first direct-script baseline on Main `84352242` exposed an inert execution:
`test_performance_api.py` returned zero without running its cases, because it
was written for unittest discovery and had no script entry point. The other
five suites actually passed 31, 5, 6, 10 and 3 tests. An explicit discovery run
of the unchanged API module then passed all 27 original cases. The original
empty direct-run log and the discovery result are retained separately. A
conventional `unittest.main()` guard now makes the producer's direct invocation
execute those same cases. Existing test methods and assertions are unchanged.

The include registry and its strict equality control are expanded together.
No test body becomes a coverage subject. Branch/parallel collection, pinned
coverage wheel, subprocess bootstrap, original source paths, zero-hit ranges,
missing arcs, report bytes and scanner scope/thresholds remain intact. The
original 51 source hit maps must be compared with their qualified native
reference rather than replaced or inferred from added local totals.

Full native execution, independent source/report verification, hosted checks,
the original official PR Gate and independent review are required. Counts of
46 suites and 60 subjects describe the configured scope, not a test pass or a
projected Main gain. Resulting Main coverage and its denominator must be
measured after an eligible ordinary merge. Local cohorts are not added to
earlier Main deltas.

These are offline synthetic controls, including mocked collector/network
boundaries. They do not execute a live Performance campaign, enroll hardware,
contact a deployed pilot, promote held Reader/security branches, change a
baseline/waiver or authorize a release. The separate Governance assertion
exception remains held; no known failing control is selected out for green.
