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

  def validate_sonarcloud_mutation
    Dir.mktmpdir('sonar-trust-fixture') do |dir|
      fixture_root = Pathname.new(dir)
      %w[.github/workflows governance scripts/ci].each do |path|
        FileUtils.mkdir_p(fixture_root.join(path))
      end
      workflow = ROOT.join('.github/workflows/sonarcloud.yml').read
      bridge = ROOT.join('scripts/ci/sonarcloud-ci.py').read
      workflow, bridge = yield(workflow, bridge)
      fixture_root.join('.github/workflows/sonarcloud.yml').write(workflow)
      fixture_root.join('scripts/ci/sonarcloud-ci.py').write(bridge)
      registry = JSON.parse(ROOT.join(TRUST_REGISTRY_PATH).read)
      registry['write_permissions'].select! { |entry| entry['workflow'] == '.github/workflows/sonarcloud.yml' }
      fixture_root.join(TRUST_REGISTRY_PATH).write(JSON.generate(registry))
      WorkflowTrustValidator.new(fixture_root).validate
    end
  end

  def test_sonarcloud_native_app_scanner_with_reviewed_revision_passes
    assert_empty validate_sonarcloud_mutation { |workflow, resolver| [workflow, resolver] }
  end

  def test_sonarcloud_removing_protected_environment_revokes_approval
    findings = validate_sonarcloud_mutation do |workflow, resolver|
      [workflow.sub('name: sonarcloud-analysis', 'name: unprotected'), resolver]
    end
    assert_includes messages(findings), 'privileged checkout'
  end

  def test_sonarcloud_removing_same_repository_guard_revokes_approval
    findings = validate_sonarcloud_mutation do |workflow, resolver|
      [workflow.sub('workflow_run.head_repository.full_name == github.repository', 'true'), resolver]
    end
    assert_includes messages(findings), 'privileged checkout'
  end

  def test_sonarcloud_missing_revision_check_revokes_approval
    findings = validate_sonarcloud_mutation do |workflow, resolver|
      [workflow, resolver.sub('run["head_sha"]', '"unverified"')]
    end
    assert_includes messages(findings), 'privileged checkout'
  end

  def test_sonarcloud_missing_review_requirement_revokes_approval
    findings = validate_sonarcloud_mutation do |workflow, resolver|
      [workflow, resolver.sub('required_reviewers', 'not_required')]
    end
    assert_includes messages(findings), 'privileged checkout'
  end

  def test_sonarcloud_custom_sarif_publisher_requires_independent_review
    injected = [
      '',
      '  publish:',
      '    runs-on: ubuntu-latest',
      '    permissions:',
      '      security-events: write',
      '    steps:',
      '      - run: echo unapproved',
      ''
    ].join("\n")
    findings = validate_sonarcloud_mutation do |workflow, resolver|
      [workflow + injected, resolver]
    end
    refute_empty findings
  end
end
