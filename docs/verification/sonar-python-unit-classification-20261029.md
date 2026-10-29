# Audited Python unit-body coverage classification

Base Main0ea944a4602e9484392c8f2592752ffd98f34c5c. Official Main new coverage
56.8%/ReliabilityD/SecurityE remains FAIL. The existing test-*.py and Node test
coverage classification misses underscore-named Python unit bodies; the public
file breakdown includes such bodies at zero measured coverage. This is separate
from unmeasured operational logic, which must remain visible and gain real tests.

Inventory:56tracked underscore-named Python files.54 contain unittest/pytest
imports and actual test methods. Do not exempt the shared
scripts/ci/test_av_mig_contract_boundary.py: operational test_av_mig_production_cli
imports it. Both production/runtime CLI drivers also stay measured. Narrow
reviewed directory/name patterns therefore match only53 unit bodies. No broad
**/test_*.py exemption. AST guard verifies framework/methods, a non-empty real
inventory, contained regular source, protected drivers/shared module and no
operational import of a classified unit body. Binary selection uses a fully
qualified Git default or the documented explicit owner-trusted override.

Only sonar.coverage.exclusions changes. Analysis/CPD exclusions, issue/security/
reliability rules,80%/A/A thresholds, New Code definition, scanners and tests are
unchanged. Existing genuine test classification remains. This does not claim
80%, officialGatePASS or removal of SecurityE; fresh exact-source native/official
analysis/current independent review and post-merge Main are required.

Audited53 unit paths:

- deploy/sakura/test_validate_pilot_intake_policy.py
- scripts/ci/qodana-packets/test_r2_candidate.py
- scripts/ci/qodana-packets/test_r2_verify.py
- scripts/ci/qodana-packets/test_verify.py
- scripts/ci/test_audit_sbom_component_inventory.py
- scripts/ci/test_av_mig_contract_boundary_adversarial.py
- scripts/ci/test_av_mig_contract_boundary_hardening.py
- scripts/ci/test_github_action_pins.py
- scripts/ci/test_nuget_dependency_snapshot.py
- scripts/ci/test_sec17_deep_execution.py
- scripts/ci/test_sec17_file_policy_execution.py
- scripts/ci/test_sec_arch_evidence.py
- scripts/ci/test_sec_arch_github_provenance.py
- scripts/ci/test_sec_arch_http_accounting.py
- scripts/ci/test_sec_arch_manual_replay.py
- scripts/ci/test_sec_arch_reconcile.py
- scripts/ci/test_sec_arch_rls_matrix.py
- scripts/ci/test_sec_arch_signalr_accounting.py
- scripts/ci/test_sec_arch_worker_accounting.py
- scripts/ci/test_specification_contract.py
- scripts/ci/test_specification_contract_check.py
- scripts/ci/test_verify_grype_sbom_source.py
- scripts/ci/test_verify_sbom.py
- scripts/ci/test_verify_sbom_vulnerability_gate.py
- scripts/quality/test_qodana_reconciliation.py
- tests/ci/test_performance_api.py
- tests/ci/test_performance_api_collection.py
- tests/ci/test_performance_api_diagnostics.py
- tests/ci/test_performance_assurance.py
- tests/ci/test_performance_comparator.py
- tests/ci/test_performance_comparator_hardening.py
- tests/ci/test_performance_contract.py
- tests/ci/test_performance_db.py
- tests/ci/test_performance_db_approval_history.py
- tests/ci/test_performance_db_assignment_transition.py
- tests/ci/test_performance_db_campaign.py
- tests/ci/test_performance_db_candidate.py
- tests/ci/test_performance_db_ci.py
- tests/ci/test_performance_diagnostic_recovery.py
- tests/ci/test_performance_environment.py
- tests/ci/test_performance_environment_class.py
- tests/ci/test_performance_local.py
- tests/ci/test_performance_public_digest.py
- tests/ci/test_performance_runtime_compatibility.py
- tests/ci/test_release_assurance_advisory.py
- tests/ci/test_release_browser_attachment.py
- tests/ci/test_release_browser_evidence.py
- tests/ci/test_release_image_graph.py
- tests/ci/test_release_main_security_evidence.py
- tests/ci/test_release_supply_chain.py
- tests/ci/test_sec04_deep_ci.py
- tests/ci/test_security_finding_policy.py
- tests/ci/test_security_zap_attribution.py


No formal authorization/model/RLS/schema/FCI06/Recovery/HOLD or release change.
STUDENT_COUNCIL_PILOT=RELEASE_NO_GO.

Local verification:10 selected source/coverage guards PASS. Two actual property
mutations are rejected by the new structural guard: a broad test_*.py pattern
would exempt executable drivers, and a broader contract pattern would exempt
the shared runtime-imported module. Both mutations were restored; no testcase,
threshold, runtime source or production behavior is removed. Full hosted23-test
routing/static analysis and native/official rescan remain required. A local
selection harness initially chose one test class for methods across two classes;
that harness failed before executing tests or changing properties, and was fixed.
