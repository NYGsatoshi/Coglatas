# Node regression test coverage classification

Main47f has44 tracked *.node-test.mjs files: frontend/scripts6,scripts/ci1,
tests/ci1,tests/diagnostics1,tests/functional17,tests/ui18. Every one imports
node:test; none is imported by an operational JS/TS module. These are executable
test definitions, not the operational fixture/helper code they exercise. CI still
executes them. Root Sonar currently excludes only CI Python test bodies from
coverage, so the same Node test-body classification was missing.

The actual PR1515/source3b measure view proves119 of151uncovered new lines are
test bodies (39profile+80runner), while29are operational code and3type declaration
lines. Its official Gate123305078396 FAIL59.2%/D/B remains historical evidence.
This change excludes only *.node-test.mjs test bodies from coverage, alongside
the existing Python test-body policy. It does not change measured operational
sources, rules, issue analysis, Source/CPD exclusions, New Code definition or80%
threshold. Security/Reliability findings in these files remain analyzed and must
be fixed or grounded separately. No issue is dismissed by this change. Type-only
declarations and unmeasured operational code remain in the current denominator;
no declaration exclusion is added here. No resulting GatePASS is assumed.

Source guards inventory every matching file, require node:test and test/script
locations, forbid operational imports from test bodies, and prove real measured
operational helpers remain outside the pattern. No tests, assertions, retry,
timeout, canonical acceptance or Product authorization/API/DB contract changes.

Sonar's documented model excludes test code from coverage: https://docs.sonarsource.com/sonarqube-cloud/managing-your-projects/project-analysis/setting-analysis-scope/setting-initial-scope .
SonarScanner for.NET does not support overriding sonar.sources/sonar.tests:
https://docs.sonarsource.com/sonarqube-cloud/analyzing-source-code/scanners/sonarscanner-for-dotnet/using .
The supported coverage-only policy is therefore extended narrowly, preserving
analysis of all test and production files. Full hosted CI/native source-bound
PR/Main analysis and independent review are still required.

STUDENT_COUNCIL_PILOT = RELEASE_NO_GO.
