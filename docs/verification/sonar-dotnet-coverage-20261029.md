# Sonar .NET coverage import

Issue: #1486. Starting Main: `71a2da04ad1cd8089f2a87546d122a85ff64e4d1`.

The trusted push scanner imported Angular LCOV but only built C#. Its separate
Main CI Cobertura report was not imported into Sonar. The scanner now runs the
existing complete backend suite against its own pinned PostgreSQL 18.6 service,
collects OpenCover and imports `sonar.cs.opencover.reportsPaths`. The database,
existing migrations and provider tests remain isolated synthetic test work.

The existing strict TRX verifier rejects failed, skipped and unexecuted tests.
A separate report validator requires one fresh backend OpenCover report,
resolvable production source files and actually visited production sequence
points; test-only, generated-only and empty summary reports cannot qualify.
The artifact retains the original coverage XML, its SHA-256 and checkout
commit/tree. This is a same-job coverage import receipt, not a loaded-binary
attestation, actual browser qualification or release decision.

No coverage threshold, analysis exclusion, formal contract, canonical Functional
owner, retry, deadline, Sonar environment policy or token routing changes.
The PostgreSQL service repeats the Main CI privacy settings before any writes.
Dependencies/tooling restore and tests have no SONAR_TOKEN environment grant;
only the existing scanner begin/end steps receive that secret.

Local validation: eleven coverage evidence controls and fifteen unchanged
credential-routing controls PASS in Linux; pinned SDK10.0.403 Release build
PASS with zero warnings/errors. The first Windows/PostgreSQL full collection
observed four stream-read timeouts, 1,504 passing tests and was aborted rather
than credited (1,508 observed cases, zero skipped). Its TRX and log remain in
local first-failure evidence. Host concurrency versus Docker capacity is only
a candidate explanation. A separately bounded local execution and hosted
exact-head import remain required; no retry-success is called a first pass.
Independent review, required checks and resulting Main also remain required. Historical scanner40741050777/1 SUCCESS and official Quality Gate
FAIL (28.8% New Code coverage, Reliability D, Security E) remain unchanged.
No aggregate Security rating identifies a safe product patch. Analyzer UTF-8,
JS bridge and shell-parser limitations remain open until separately verified.

`STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.
