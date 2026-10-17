#!/usr/bin/env ruby
# frozen_string_literal: true

require 'fileutils'
require 'json'
require 'minitest/autorun'
require 'pathname'
require 'tmpdir'

require_relative 'check-workflow-trust-boundaries'

FIXTURE_DIR = Pathname.new(__dir__).join('fixtures/workflow-trust-boundaries')

class WorkflowTrustBoundaryTests < Minitest::Test
  def policy(allowlist = [])
    {
      'controls' => [
        {
          'id' => CONTROL_PERMISSION,
          'expected' => {
            'persist_credentials' => false,
            'write_permissions_allowlist' => allowlist.map { |entry| { 'workflow' => entry['workflow'], 'permissions' => entry['permissions'] } }
          }
        },
        {
          'id' => CONTROL_TRUST,
          'expected' => {
            'pull_request_target' => 'forbidden'
          }
        },
        {
          'id' => CONTROL_RUNNER,
          'expected' => {
            'untrusted_pr' => 'forbidden',
            'persistent_privileged' => 'forbidden'
          }
        }
      ]
    }
  end

  def runner_routing_policy
    {
      'github_hosted_labels' => ['ubuntu-latest', 'macos-latest'],
      'github_hosted_groups' => [],
      'approved_dynamic_expressions' => [
        "${{ (matrix.language == 'swift' && 'macos-latest') || 'ubuntu-latest' }}"
      ]
    }
  end

  def validate(*fixtures, allowlist: [])
    Dir.mktmpdir('gov-04-fixture') do |dir|
      root = Pathname.new(dir)
      FileUtils.mkdir_p(root.join('.github/workflows'))
      FileUtils.mkdir_p(root.join('governance'))
      root.join('governance/policy.json').write(JSON.pretty_generate(policy(allowlist)) + "\n")
      registry = {
        'schema_version' => 1,
        'source_controls' => [CONTROL_PERMISSION, CONTROL_TRUST, CONTROL_RUNNER],
        'runner_routing' => runner_routing_policy,
        'write_permissions' => allowlist
      }
      root.join(TRUST_REGISTRY_PATH).write(JSON.pretty_generate(registry) + "\n")
      fixtures.each do |fixture|
        FileUtils.cp(FIXTURE_DIR.join(fixture), root.join('.github/workflows', fixture))
      end
      return WorkflowTrustValidator.new(root).validate
    end
  end

  def messages(findings)
    findings.map(&:to_s).join("\n")
  end

  def test_valid_untrusted_fixture_passes
    assert_empty validate('valid-untrusted.yml')
  end

  def test_codeql_pull_request_security_events_write_is_narrowly_allowed
    allow = [{
      'workflow' => '.github/workflows/codeql.yml',
      'permissions' => ['security-events:write'],
      'events' => ['pull_request', 'push', 'schedule'],
      'jobs' => ['analyze'],
      'reason' => 'publish CodeQL findings on pull requests and trusted scans'
    }]
    assert_empty validate('codeql.yml', allowlist: allow)
  end

  def test_write_all_fails
    result = validate('invalid-write-all.yml')
    assert_includes messages(result), 'write-all is forbidden'
    assert_includes messages(result), CONTROL_PERMISSION
  end

  def test_pull_request_target_fails
    result = validate('invalid-pr-target-head.yml')
    assert_includes messages(result), 'pull_request_target is forbidden'
    assert_includes messages(result), CONTROL_TRUST
  end

  def test_workflow_run_head_checkout_fails
    allow = [{
      'workflow' => '.github/workflows/invalid-workflow-run-head.yml',
      'permissions' => ['statuses:write'],
      'events' => ['workflow_run'],
      'jobs' => ['evaluate'],
      'reason' => 'negative fixture exercises privileged workflow_run boundary'
    }]
    result = validate('invalid-workflow-run-head.yml', allowlist: allow)
    assert_includes messages(result), 'triggering head'
  end

  def test_self_hosted_untrusted_pr_fails
    result = validate('invalid-pr-self-hosted.yml')
    assert_includes messages(result), 'self-hosted runner'
    assert_includes messages(result), CONTROL_RUNNER
  end

  def test_persisted_checkout_fails
    result = validate('invalid-pr-persist-credentials.yml')
    assert_includes messages(result), 'persist-credentials: false'
  end

  def test_untrusted_secret_fails
    result = validate('invalid-pr-secret.yml')
    assert_includes messages(result), 'references repository/environment secrets'
  end

  def test_untrusted_environment_fails
    result = validate('invalid-pr-environment.yml')
    assert_includes messages(result), 'must not bind a protected environment'
  end

  def test_narrow_allowlisted_trusted_evaluator_passes
    allow = [{
      'workflow' => '.github/workflows/valid-trusted-evaluator.yml',
      'permissions' => ['statuses:write'],
      'events' => ['workflow_run'],
      'jobs' => ['evaluate'],
      'reason' => 'publish the canonical external-review status after API re-resolution'
    }]
    assert_empty validate('valid-trusted-evaluator.yml', allowlist: allow)
  end

  def test_reusable_workflow_inherits_untrusted_boundary
    result = validate('reusable-untrusted-caller.yml', 'reusable-secret-callee.yml')
    text = messages(result)
    assert_includes text, 'reusable-secret-callee.yml'
    assert_includes text, 'references repository/environment secrets'
    assert_includes text, 'must not bind a protected environment'
  end
end
