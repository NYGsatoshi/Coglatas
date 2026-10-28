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

The new evidence validator is itself measured by twelve positive/negative
Python controls, including actual temporary Git checkout/receipt creation
and equality of emitted/saved receipt bytes. Local branch-inclusive coverage
is 97% (44/45 statements, 22 branches counted), separate from Sonar New Code.
The scanner imports its Cobertura report using the checksum-pinned pure Python
coverage.py 7.17.0 wheel. This measurement does not exclude other production
scripts or convert their unmeasured lines into covered lines.

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


## First hosted collection failure retained

HEAD `e3e087925cbb466068ca5482a4913f8b470a44cb`, Sonar40776872470/1,
job123128503392: actual PostgreSQL-backed .NET tests **2624/2624 PASS,0skip**
and strict TRX verification PASS. Collector announced one OpenCover attachment.
The subsequent verifier failed its exact-one report check; its first diagnostic
did not retain the actual candidate count or report bytes. No C# import, report
publication or new Quality Gate result was completed. This remains a workflow
FAIL, independently of all six ordinary required checks and the exact-head
independent review that passed.

The forward observation repair retains generated coverage on failure and emits
only bounded collector file paths/sizes/counts. It does not pick an arbitrary
report, relax the cardinality/source/visit predicates, or infer that the
collector-announced attachment was successfully imported. Native layout and
failure cause require the next exact-source observation.

The separately processor-limited local Windows collection was invalidated by a
concurrent OpenAPI rebuild in its shared producer checkout, then stopped amid
local disk exhaustion:2242 observed passes,17 failures,0skip,2259 observed cases,
aborted. Its module-binding disagreement and all original local failures remain
retained; this is not a Product defect claim or immutable-source qualification.
Further local compiler work uses an isolated Linux checkout.
