# Native Node coverage for the offline restore advisor

## Scope

Main baseline `aabae9c7db4ddadda99db42d0cdebe81e5a893a9`; tracking #615 / #1507. Add the existing 23-control offline advisor suite to native Node measurement. Its unusually named unit file is excluded only from V8 hit reporting, consistently with existing node-test files; the unchanged operational advisor remains measured. The workflow guard explicitly retains the invocation and prevents exclusion of that operational source. Sonar source/security/CPD/coverage exclusions, New Code definition, rules and thresholds remain unchanged. No advisor or existing-23-control-suite implementation change; the workflow guard gains only those explicit checks.

## Existing controls

The suite checks deterministic DAG ordering, cycles/self references, duplicate and joined-key identities, bounded counts, incomplete inventory, active triggers and missing data; actual CLI advisory-only output, exit1/exit2, exact/oversize input bounds, directory/symlink/FIFO rejection, inode replacement, growth/shrink, partial reads and closed descriptor/error behavior. All inputs are synthetic offline metadata. No database, restore target or recovery operation is used; REVIEW_ONLY_NOT_RESTORE_VERIFIED remains mandatory and no formal Recovery/RLS/Owner acceptance is granted.

## Local verification and retained failures

- Windows first execution: 21 PASS / 2 FAIL / zero SKIP. Symlink creation returned EPERM; the FIFO control also failed. This is retained, not a Windows qualification. No privilege or platform skip was added.
- Ubuntu with official Node24.22.1: first Linux execution **23 PASS / zero FAIL/SKIP**. Original LCOV reports one operational source, 121/121 physical lines, 15/15 functions and 76/79 branches (96.20%). These Node counters are not Sonar New Code coverage or official Gate evidence.
- The original LCOV passed the unchanged exported shared validator: sourceFiles1 / branches79 / uncovered3. The first local fixed-output CLI invocation rejected a non-producer artifact path; a stdin import invocation also failed its file-entry guard. Correcting only the QA invocation to module eval allowed shared validation. No validator change, LCOV rewrite, post-generation source filtering or invented hit.
- Linux Node archive was obtained from the official vendor and verified against its SHA-256 list: archive `6c172a9ab6718f7b801fb5a7bd486af2ee5e32758086a04b84fcce2aa77095e6`, extracted regular ELF `e32e2d16081cf644f88392934288086e7662529d0d43277c34f7aad2207cfafb`. Only the binary was extracted into the task work area; no archive links/global installation.

Fresh workflow/source guards, full hosted native/protected CI, original LCOV subject/line audit, official Gate, independent current-HEAD review and resulting-Main qualification remain required. All historical failures, held PRs, FCI06 Default C and formal Recovery/security/staging/admission holds remain.

`STUDENT_COUNCIL_PILOT = RELEASE_NO_GO`.

The first routing command ran the existing 11 controls successfully but also requested two duplicate methods under the wrong unittest class, producing two loader errors. Both methods belong to SonarSourceScopeTests and are already included in those 11 controls; the corrected exact selector has no duplicate requests. No test code or assertion was relaxed.
