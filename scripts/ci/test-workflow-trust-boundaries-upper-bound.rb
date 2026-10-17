#!/usr/bin/env ruby
# frozen_string_literal: true

require_relative 'test-workflow-trust-boundaries-all'
require_relative 'check-workflow-trust-boundaries-upper-bound'

class WorkflowTrustBoundaryTests
  # Reuse the full GOV-04 fixture suite with the self-contained v2 trust registry.
  def validate(*fixtures, allowlist: [])
    Dir.mktmpdir('gov-04-fixture') do |dir|
      root = Pathname.new(dir)
      FileUtils.mkdir_p(root.join('.github/workflows'))
      FileUtils.mkdir_p(root.join('governance'))
      root.join('governance/policy.json').write(JSON.pretty_generate(policy(allowlist)) + "\n")
      registry = {
        'schema_version' => 2,
        'source_controls' => [CONTROL_PERMISSION, CONTROL_TRUST, CONTROL_RUNNER],
        'constraints' => {
          'persist_credentials' => false,
          'pull_request_target' => 'forbidden',
          'untrusted_pr_self_hosted' => 'forbidden',
          'persistent_privileged_self_hosted' => 'forbidden'
        },
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

  def test_allowlisted_write_permission_may_be_removed
    allow = [{
      'workflow' => '.github/workflows/valid-retired-write-lane.yml',
      'permissions' => ['statuses:write'],
      'events' => ['workflow_run', 'workflow_dispatch'],
      'jobs' => ['evaluate'],
      'reason' => 'compatibility entry retained while the privileged status publisher is retired'
    }]

    assert_empty validate('valid-retired-write-lane.yml', allowlist: allow)
  end
end
