#!/usr/bin/env ruby
# frozen_string_literal: true

require_relative 'test-workflow-trust-boundaries'

class WorkflowTrustBoundaryTests
  def test_privileged_dynamic_checkout_fails
    allow = [{
      'workflow' => '.github/workflows/invalid-privileged-dynamic-checkout.yml',
      'permissions' => ['contents:write'],
      'events' => ['workflow_dispatch'],
      'jobs' => ['mutate'],
      'reason' => 'negative fixture exercises privileged checkout ref boundary'
    }]
    result = validate('invalid-privileged-dynamic-checkout.yml', allowlist: allow)
    assert_includes messages(result), 'privileged checkout'
    assert_includes messages(result), 'arbitrary/dynamic ref expression'
  end

  def test_privileged_broad_write_fails_when_scope_is_not_allowlisted
    allow = [{
      'workflow' => '.github/workflows/invalid-privileged-broad-write.yml',
      'permissions' => ['contents:write'],
      'events' => ['workflow_dispatch'],
      'jobs' => ['mutate'],
      'reason' => 'negative fixture allows only the narrow write scope actually justified'
    }]
    result = validate('invalid-privileged-broad-write.yml', allowlist: allow)
    assert_includes messages(result), 'unallowlisted write permissions: issues:write'
    assert_includes messages(result), CONTROL_PERMISSION
  end

  def test_privileged_self_hosted_runner_fails
    allow = [{
      'workflow' => '.github/workflows/invalid-privileged-self-hosted.yml',
      'permissions' => ['contents:write'],
      'events' => ['workflow_dispatch'],
      'jobs' => ['mutate'],
      'reason' => 'negative fixture isolates privileged runner routing policy'
    }]
    result = validate('invalid-privileged-self-hosted.yml', allowlist: allow)
    assert_includes messages(result), 'must not use a self-hosted runner'
    assert_includes messages(result), CONTROL_RUNNER
  end

  def test_unknown_event_fails_closed
    result = validate('invalid-unclassified-event.yml')
    assert_includes messages(result), 'has no GOV-04 trust classification'
    assert_includes messages(result), CONTROL_TRUST
  end

  def test_untrusted_runner_group_mapping_fails_closed
    result = validate('invalid-pr-runner-group.yml')
    text = messages(result)
    assert_includes text, 'runner group "persistent-runners"'
    assert_includes text, 'not in the trusted GitHub-hosted group registry'
    assert_includes text, CONTROL_RUNNER
  end

  def test_privileged_runner_group_mapping_fails_closed
    allow = [{
      'workflow' => '.github/workflows/invalid-privileged-runner-group.yml',
      'permissions' => ['contents:write'],
      'events' => ['workflow_dispatch'],
      'jobs' => ['mutate'],
      'reason' => 'negative fixture proves mapping-form runner groups fail closed'
    }]
    result = validate('invalid-privileged-runner-group.yml', allowlist: allow)
    text = messages(result)
    assert_includes text, 'runner group "persistent-runners"'
    assert_includes text, 'not in the trusted GitHub-hosted group registry'
    assert_includes text, CONTROL_RUNNER
  end

  def test_secret_index_syntax_is_detected
    result = validate('invalid-pr-secret-index.yml')
    assert_includes messages(result), 'references repository/environment secrets'
    assert_includes messages(result), CONTROL_TRUST
  end

  def test_workflow_level_env_secret_is_effective_for_untrusted_job
    result = validate('invalid-pr-workflow-env-secret.yml')
    text = messages(result)
    assert_includes text, 'references repository/environment secrets'
    assert_includes text, CONTROL_TRUST
  end

  def test_workflow_run_indexed_head_reference_fails
    allow = [{
      'workflow' => '.github/workflows/invalid-workflow-run-head-index.yml',
      'permissions' => ['statuses:write'],
      'events' => ['workflow_run'],
      'jobs' => ['evaluate'],
      'reason' => 'negative fixture proves indexed workflow_run head access is normalized'
    }]
    result = validate('invalid-workflow-run-head-index.yml', allowlist: allow)
    text = messages(result)
    assert_includes text, 'references triggering head ref/SHA'
    assert_includes text, CONTROL_TRUST
  end

  def test_secret_object_dump_remains_detected
    result = validate('invalid-pr-secret-object-dump.yml')
    text = messages(result)
    assert_includes text, 'references repository/environment secrets'
    assert_includes text, 'attempts to print/dump secret context'
    assert_includes text, CONTROL_TRUST
  end
end
