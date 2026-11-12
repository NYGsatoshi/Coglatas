# Existing security accounting suites in native Python measurement

The common pre-credential Python producer runs the existing provenance suite
and the complete execution-evidence suite. The evidence module owns a load_tests
hook that also loads HTTP, SignalR and manual-replay controls. Those nested suites
are not invoked again in the common loop. All previous test methods and
assertions remain; the loop has 53 script entries and 63 unchanged subjects.

The process-coverage regression checks the actual loaded test IDs against every
evidence-owned and nested test, rejects duplicates, and requires the loop to
select the aggregator and provenance once without separate nested entries.
Changes to the hook cannot silently drop a nested suite or execute it twice.

Pinned tool/bootstrap, raw shards, single combine/XML import, zero-hit ranges,
native branch accounting and test-body exclusion are unchanged. Synthetic
accounting does not approve live provenance, security mapping, adapters, manual
replay, application roles, RLS, recovery or deployment. Owner holds remain.

Require exact-head hosted collection, CI, original PR Gate and independent
review before integration. Requalify resulting Main reports/source identities,
denominator and actual line/condition deltas separately in #615. Local results
do not establish Main gain. No report rewriting, waiver or Release GO.
