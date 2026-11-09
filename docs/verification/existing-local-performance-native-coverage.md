# Existing local evidence controls in canonical Python coverage

The existing performance contract workflow already runs the complete 26-case
`test_performance_local.py` and nine-case `test_performance_assurance.py` suites.
The canonical Sonar Python producer now selects both unchanged suites once and
includes twelve existing production helpers that they import. All original 53
script entries and 63 production subjects remain, giving 55 entries and 75
declared subjects. Test bodies remain
outside the measured include list. A loader control checks all owned test IDs,
uniqueness, the absence of a replacement loader and exact single selection.

The original producer, branch/parallel process bootstrap, pinned coverage tool,
raw shards, combine, native JSON and XML import remain. Existing Functional,
Pilot and operational JavaScript reports are separate; no second Python report
or overlapping import is added. Source bytes, original tests and assertions,
zero-hit rows and native line/branch accounting must be verified independently.

These controls use synthetic scalar/evidence data, mocked Docker/runtime/network
preflight and ephemeral test SSH signing keys. They validate rejection, signature,
privacy and replay contracts. They do not run a real benchmark or grant a baseline,
environment enrollment, Source promotion, mapping activation or Required Check
promotion. Numerical performance remains indefinitely suspended and
`NOT_EVALUATED` under the existing Owner decision; no policy, acceptance limit,
registry, baseline, campaign, approved Source or duration threshold changes.

Actual supported hosted collection must bind source/run/attempt/artifact digests
to the original raw data and reports, retain original zero ranges, exclude test
bodies and reject duplicate source/report imports. Coverage recognized on a PR
does not establish Main improvement. Measure the resulting Main's actual complete
source mapping, denominator and covered-line/condition delta separately; do not
sum local gains or narrow coverage scope to obtain a passing gate.
