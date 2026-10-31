# Python measurement for existing privacy and setup-probe controls

The existing pre-credential Python coverage loop now also runs the unchanged
`test-postgres-log-privacy.py` and `test-functional-setup-failure-probe.py` suites.
Only their exact tracked operational modules are added to the rooted measurement
include list. Existing entries, bootstrap, rules, source analysis, coverage
exclusions, thresholds, credentials, permissions and 15-second subprocess bounds
are preserved. No operational or existing acceptance assertion changes.

The privacy suite imports its exact sibling module and mocks every external
runner/subprocess call; the bounded silent-log case also mocks sleep. Its nine
cases cover safe/unsafe settings, required denial/error evidence, canary
non-disclosure, invalid target and failed-runner sanitization. The seven setup
probe cases always inject a fake/forbidden runner, covering finite positive
observation, missing daemon/image/config, unpinned/option-like references,
oversize config and non-disclosure. Neither suite invokes Docker, a database,
network or an operator target. CLI/default-run paths are not exercised by them
and remain genuine zero coverage.

One producer regression reuses both existing suites in separate real Python
processes. It verifies exact tracked identities and real positive/uncalled-line
evidence in original CoverageData and standard Cobertura XML. Existing bootstrap
and outside-source negative controls remain. The existing workflow guard also
requires both suite entries inside the pre-credential measurement block.

Local portable Python3.12.14 with the already-owned pinned coverage7.17.0 package
passed the unchanged suites 9/9 and 7/7, the complete process suite 8/8 and the
existing source-scope suite 8/8. A separate standard measured run retained its
original combined data/XML: privacy has 50 positive/13 zero lines and 16/18
branches; setup probe has 34 positive/12 zero lines and 8/10 branches. The
bootstrap already-imported warning is retained. No hits, maps or reports were
rewritten, no package was installed for this candidate, and these figures are
not Sonar New Code coverage or an official Main improvement forecast.

Hosted Linux/current-source checks, original report and zero audit, independent
review and resulting Main qualification remain required. Initial source-bound
Eba inventory63/46 missed lines and current Main a3ed Gate65.7% / ReliabilityD /
SecurityE remain separate evidence. The held receipt-coverage candidate is not
an implicit dependency of this branch; reconcile normally before combining.

Files intake/scanner, RLS, Recovery, staging/admission and formal release holds
remain unchanged. `STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.
