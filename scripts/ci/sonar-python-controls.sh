#!/usr/bin/env bash
# Canonical Python Sonar control inventory and dispatch, relocated verbatim from the workflow.
# All lanes parse this single source; retain the original indentation.
set -euo pipefail

          for control in \
            scripts/ci/test-sonar-python-process-coverage.py \
            tests/ci/test_synthetic_logical_recovery_ci.py \
            scripts/ci/test-sonar-dotnet-coverage.py \
            scripts/ci/test-sonar-angular-coverage.py \
            scripts/ci/test-sonar-python-sharding.py \
            scripts/ci/test-sonar-openapi-reproduction.py \
            scripts/ci/test-sonar-scanner-identity-trial.py \
            scripts/ci/test-sonar-windows-coverage.py \
            scripts/ci/test-required-pr-checks.py \
            scripts/ci/test-publication-readiness.py \
            scripts/ci/test-coglatas-rename.py \
            scripts/ci/test_github_action_pins.py \
            scripts/ci/test_verify_sbom_vulnerability_gate.py \
            scripts/ci/test_verify_sbom.py \
            scripts/ci/test_verify_grype_sbom_source.py \
            tests/ci/test_security_finding_policy.py \
            tests/ci/test_sec17_realtime_finding_candidate.py \
            tests/ci/test_sec04_deep_ci.py \
            scripts/ci/test_sec17_deep_execution.py \
            scripts/ci/test_sec17_file_policy_execution.py \
            scripts/ci/test-mbj02-trx-receipt.py \
            scripts/ci/test-workspace-icon-disclosure-policy.py \
            scripts/ci/test-main-runtime-artifact.py \
            deploy/sakura/test_validate_pilot_intake_policy.py \
            tests/ci/test_release_image_graph.py \
            tests/ci/test_release_supply_chain.py \
            tests/ci/test_release_main_security_evidence.py \
            tests/ci/test_release_browser_evidence.py \
            tests/ci/test_release_assurance_advisory.py \
            tests/ci/test_performance_contract.py \
            tests/ci/test_performance_comparator.py \
            tests/ci/test_performance_comparator_hardening.py \
            tests/ci/test_performance_db_campaign.py \
            scripts/ci/test_specification_contract.py \
            scripts/ci/test_specification_contract_check.py \
            scripts/ci/test_sec_arch_worker_accounting.py \
            scripts/ci/test_sec_arch_rls_matrix.py \
            scripts/ci/test_sec_arch_github_provenance.py \
            scripts/ci/test_sec_arch_evidence.py \
            scripts/ci/test_sec_arch_reconcile.py \
            scripts/ci/test-mvp-a-final-checks.py \
            scripts/ci/test-functional-diagnostics.py \
            scripts/ci/test-postgres-log-privacy.py \
            scripts/ci/test-functional-setup-failure-probe.py \
            tests/ci/test_security_zap_attribution.py \
            tests/ci/test_performance_db_ci.py \
            scripts/quality/test_qodana_reconciliation.py \
            scripts/ci/qodana-packets/test_verify.py \
            scripts/ci/qodana-packets/test_r2_verify.py \
            scripts/ci/qodana-packets/test_r2_candidate.py \
            scripts/ci/test-functional-evidence.py \
            scripts/ci/test_nuget_dependency_snapshot.py \
            scripts/ci/test-nuget-lock-policy.py \
            scripts/ci/test_audit_sbom_component_inventory.py \
            tests/ci/test_performance_api.py \
            tests/ci/test_performance_api_diagnostics.py \
            tests/ci/test_performance_api_collection.py \
            tests/ci/test_performance_environment.py \
            scripts/ci/test_av_mig_contract_boundary.py \
            scripts/ci/test_av_mig_contract_boundary_hardening.py \
            scripts/ci/test_av_mig_contract_boundary_adversarial.py \
            tests/ci/test_performance_assurance.py \
            tests/ci/test_performance_local.py \
            tests/ci/test_performance_db.py \
            tests/ci/test_performance_db_candidate.py \
            tests/ci/test_performance_db_assignment_transition.py \
            tests/ci/test_performance_db_approval_history.py \
            tests/ci/test_performance_environment_class.py \
            tests/ci/test_performance_public_digest.py \
            tests/ci/test_performance_runtime_compatibility.py \
            tests/ci/test_performance_diagnostic_recovery.py \
            scripts/ci/test_av_mig_production_cli.py; do
            case "$control" in
              tests/ci/test_performance_db.py|\
              tests/ci/test_performance_db_candidate.py|\
              tests/ci/test_performance_db_assignment_transition.py|\
              tests/ci/test_performance_db_approval_history.py|\
              tests/ci/test_performance_environment_class.py|\
              tests/ci/test_performance_public_digest.py|\
              tests/ci/test_performance_runtime_compatibility.py|\
              tests/ci/test_performance_diagnostic_recovery.py)
                COVERAGE_PROCESS_START="$COVERAGE_RCFILE" \
                  PYTHONPATH="$PWD/scripts/ci/sonar-python-coverage-bootstrap:$PYTHONPATH" \
                  python3 -B -m coverage run -m unittest discover -s tests/ci -p "${control##*/}"
                continue
                ;;
            esac
            if [[ "$control" == "tests/ci/test_performance_local.py" ]]; then
              COVERAGE_PROCESS_START="$COVERAGE_RCFILE" \
                PYTHONPATH="$PWD/scripts/ci/sonar-python-coverage-bootstrap:$PYTHONPATH" \
                python3 -B -m coverage run -m unittest discover -s tests/ci -p "${control##*/}"
              continue
            fi
            COVERAGE_PROCESS_START="$COVERAGE_RCFILE" \
              PYTHONPATH="$PWD/scripts/ci/sonar-python-coverage-bootstrap:$PYTHONPATH" \
              python3 -B -m coverage run "$control"
          done
