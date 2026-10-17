#!/usr/bin/env ruby
# frozen_string_literal: true

require_relative 'workflow-trust-boundaries-lib'

class WorkflowTrustValidator
  # Keep the event classifier and the effective untrusted checks on the exact
  # same trigger set. Replacing the library constant here ensures review-comment
  # workflows cannot be classified as untrusted without receiving the same
  # token/secret/runner restrictions as pull_request workflows.
  remove_const(:UNTRUSTED_EVENTS) if const_defined?(:UNTRUSTED_EVENTS, false)
  UNTRUSTED_EVENTS = Set[
    'pull_request',
    'pull_request_review',
    'pull_request_review_comment'
  ].freeze

  EVENT_TRUST_CLASSES = {
    'untrusted_pr' => UNTRUSTED_EVENTS,
    'privileged_trusted' => Set[
      'workflow_run',
      'workflow_dispatch',
      'push',
      'repository_dispatch',
      'merge_group',
      'branch_protection_rule'
    ],
    'scheduled_read_only' => Set['schedule'],
    'release_deploy' => Set[
      'release',
      'deployment',
      'deployment_status',
      'page_build'
    ],
    'reusable' => Set['workflow_call']
  }.freeze

  INDEXED_STRING_PROPERTY = /\[\s*(['"])([A-Za-z_][A-Za-z0-9_-]*)\1\s*\]/
  SECRET_CONTEXT_REFERENCE = /(?:\A|[^A-Za-z0-9_])secrets(?:\.|\[|\z)/i
  SECRET_OBJECT_DUMP_REFERENCE = /\btojson\(secrets\)/i
  WORKFLOW_RUN_HEAD_REFERENCE = /(?:\A|[^A-Za-z0-9_])github\.event\.workflow_run\.(?:head_sha|head_branch)\b/i
  WORKFLOW_RUN_DYNAMIC_FIELD_REFERENCE = /(?:\A|[^A-Za-z0-9_])github\.event\.workflow_run\[/i
  CODEQL_PR_WORKFLOW = '.github/workflows/codeql.yml'
  CODEQL_PR_JOB = 'analyze'
  CODEQL_PR_WRITE_PERMISSION = 'security-events:write'

  private

  alias load_allowlist_without_exact_policy_match load_allowlist
  alias validate_workflow_without_event_classification validate_workflow

  def load_allowlist(policy)
    load_allowlist_without_exact_policy_match(policy)

    permission = control(policy, CONTROL_PERMISSION)
    runner = control(policy, CONTROL_RUNNER)
    expected_runner = runner['expected']
    unless expected_runner.is_a?(Hash) && expected_runner['persistent_privileged'] == 'forbidden'
      raise JSON::ParserError, "#{CONTROL_RUNNER} must forbid persistent privileged self-hosted routing"
    end

    canonical = permission.dig('expected', 'write_permissions_allowlist')
    canonical_normalized = canonical.map do |entry|
      [entry['workflow'], Array(entry['permissions']).sort]
    end.sort
    registry_normalized = @allowlist.map do |entry|
      [entry['workflow'], Array(entry['permissions']).sort]
    end.sort

    unless canonical_normalized == registry_normalized
      raise JSON::ParserError,
            "#{TRUST_REGISTRY_PATH} write scopes must exactly match #{CONTROL_PERMISSION} canonical write_permissions_allowlist"
    end

    registry = JSON.parse(root.join(TRUST_REGISTRY_PATH).read(encoding: 'UTF-8'))
    routing = registry['runner_routing']
    unless routing.is_a?(Hash)
      raise JSON::ParserError, "#{TRUST_REGISTRY_PATH} runner_routing must be an object"
    end

    labels = routing['github_hosted_labels']
    groups = routing['github_hosted_groups']
    expressions = routing['approved_dynamic_expressions']
    unless labels.is_a?(Array) && labels.all? { |value| value.is_a?(String) && !value.strip.empty? }
      raise JSON::ParserError, "#{TRUST_REGISTRY_PATH} runner_routing.github_hosted_labels must be a nonempty-string array"
    end
    unless groups.is_a?(Array) && groups.all? { |value| value.is_a?(String) && !value.strip.empty? }
      raise JSON::ParserError, "#{TRUST_REGISTRY_PATH} runner_routing.github_hosted_groups must be a string array"
    end
    unless expressions.is_a?(Array) && expressions.all? { |value| value.is_a?(String) && value.include?('${{') }
      raise JSON::ParserError, "#{TRUST_REGISTRY_PATH} runner_routing.approved_dynamic_expressions must be an Actions-expression array"
    end
    if labels.any? { |value| value.casecmp('self-hosted').zero? }
      raise JSON::ParserError, "#{TRUST_REGISTRY_PATH} must not classify self-hosted as GitHub-hosted"
    end

    @github_hosted_runner_labels = Set.new(labels)
    @github_hosted_runner_groups = Set.new(groups)
    @approved_dynamic_runs_on = Set.new(expressions.map(&:strip))
  end

  def validate_workflow(path, doc)
    validate_workflow_without_event_classification(path, doc)
    validate_event_trust_classification(path, doc)
    validate_privileged_runner_routing(path, doc)
  end

  def validate_event_trust_classification(path, doc)
    on_node = doc['on']
    return if on_node.nil?

    events = event_names(on_node)
    if events.empty?
      add(CONTROL_TRUST, path, on_node.line, 'workflow trigger set is empty; trust class cannot be established')
      return
    end

    classified = EVENT_TRUST_CLASSES.values.reduce(Set.new, &:|)
    (events - classified).sort.each do |event|
      add(
        CONTROL_TRUST,
        path,
        on_node.line,
        "event #{event.inspect} has no GOV-04 trust classification; fail-closed until explicitly classified"
      )
    end
  end

  def validate_untrusted_job(path, job_id, job)
    permissions = job['permissions'] || @documents[path]['permissions']
    writes = write_permissions(permissions)
    forbidden_writes = writes - permitted_untrusted_writes(path, job_id)
    unless forbidden_writes.empty?
      add(
        CONTROL_TRUST,
        path,
        permissions.line,
        "untrusted PR/review job #{job_id} must not receive write permissions: #{forbidden_writes.to_a.sort.join(', ')}"
      )
    end

    if job['environment']
      add(CONTROL_TRUST, path, job['environment'].line, "untrusted PR/review job #{job_id} must not bind a protected environment")
    end

    effective_secret_nodes(path, job).each do |node|
      add(CONTROL_TRUST, path, node.line, "untrusted PR/review job #{job_id} references repository/environment secrets")
    end

    runs_on = job['runs-on']
    if runs_on.nil? && job['uses'].nil?
      add(CONTROL_RUNNER, path, job.line, "untrusted PR/review job #{job_id} must declare runs-on or a reusable workflow")
    elsif runs_on
      validate_runner_routing(path, job_id, runs_on, lane: 'untrusted PR/review')
    end

    secrets_node = job['secrets']
    if secrets_node && (!secrets_node.map? || !secrets_node.value.empty?)
      add(CONTROL_TRUST, path, secrets_node.line, "untrusted reusable-workflow caller #{job_id} must not pass secrets")
    end
  end

  # GitHub requires security-events:write to publish advanced CodeQL results.
  # For pull_request runs from forks the token is still downgraded to read-only,
  # while the Code Scanning service explicitly accepts the PR result upload.
  # Keep this exception bound to the canonical CodeQL job and exact write scope.
  def permitted_untrusted_writes(path, job_id)
    return Set.new unless path == CODEQL_PR_WORKFLOW && job_id == CODEQL_PR_JOB

    entry = @allowlist.find do |candidate|
      candidate['workflow'] == path && candidate['jobs'].include?(job_id)
    end
    return Set.new unless entry && entry['events'].include?('pull_request')

    allowed = Set.new(entry['permissions'])
    return Set.new unless allowed == Set[CODEQL_PR_WRITE_PERMISSION]

    allowed
  end

  def validate_workflow_run_job(path, job_id, job, writes)
    privileged = job_privileged?(path, job, writes)
    return unless privileged

    each_scalar(job) do |node|
      value = node.scalar.to_s
      if workflow_run_head_reference?(value)
        add(
          CONTROL_TRUST,
          path,
          node.line,
          "privileged workflow_run job #{job_id} references triggering head ref/SHA; untrusted head execution is forbidden"
        )
      end
      if value.match?(/\bgh\s+run\s+download\b/i) || value.match?(%r{/actions/runs/[^\s]+/artifacts}i)
        add(
          CONTROL_TRUST,
          path,
          node.line,
          "privileged workflow_run job #{job_id} downloads triggering workflow artifacts without a repository-owned verification boundary"
        )
      end
    end

    uses_nodes(job).each do |uses|
      if uses.scalar.to_s.match?(ARTIFACT_DOWNLOAD_ACTION)
        add(
          CONTROL_TRUST,
          path,
          uses.line,
          "privileged workflow_run job #{job_id} downloads workflow artifacts; unverified artifact execution is forbidden"
        )
      end
    end

    return if writes.empty?

    run_text = run_nodes(job).map(&:scalar).join("\n")
    proof = ['gh api', '/pulls/', '.head.sha']
    missing = proof.reject { |fragment| run_text.include?(fragment) }
    unless missing.empty?
      add(
        CONTROL_TRUST,
        path,
        job.line,
        "write-capable workflow_run job #{job_id} must re-resolve PR/head via authoritative API (missing: #{missing.join(', ')})"
      )
    end
  end

  def validate_privileged_runner_routing(path, doc)
    jobs = doc['jobs']
    return unless jobs&.map?

    jobs.value.each do |job_id, job|
      next unless job.map?

      permissions = job['permissions'] || doc['permissions']
      writes = write_permissions(permissions)
      next unless job_privileged?(path, job, writes)

      runs_on = job['runs-on']
      next if runs_on.nil? # reusable-workflow callers are checked at the callee definition.

      validate_runner_routing(path, job_id, runs_on, lane: 'privileged')
    end
  end

  # A read-only pull_request lane may check out its explicit merge/head SHA when
  # credentials are not persisted. Privileged jobs may only use repository-owned
  # refs that are statically proven; arbitrary expressions fail closed.
  def validate_checkout_steps(path, job_id, job, untrusted:, workflow_run:)
    steps = job['steps']
    return unless steps&.seq?

    permissions = job['permissions'] || @documents[path]['permissions']
    writes = write_permissions(permissions)
    privileged = job_privileged?(path, job, writes)

    steps.value.each do |step|
      next unless step.map?
      uses = step['uses']
      next unless uses&.scalar? && uses.scalar.to_s.match?(%r{\Aactions/checkout@}i)

      with = step['with']
      persist = with&.map? ? with['persist-credentials'] : nil
      if untrusted && !(persist&.scalar? && persist.scalar.to_s.casecmp('false').zero?)
        add(
          CONTROL_PERMISSION,
          path,
          persist&.line || uses.line,
          "untrusted checkout in job #{job_id} must set persist-credentials: false"
        )
      end

      ref = with&.map? ? with['ref'] : nil
      if privileged && ref&.scalar?
        value = ref.scalar.to_s
        if value.match?(DYNAMIC_EXPR) && value.strip != '${{ github.sha }}'
          add(
            CONTROL_TRUST,
            path,
            ref.line,
            "privileged checkout in job #{job_id} uses an arbitrary/dynamic ref expression"
          )
        end
      end

      if workflow_run && privileged && ref&.scalar? && workflow_run_head_reference?(ref.scalar.to_s)
        add(
          CONTROL_TRUST,
          path,
          ref.line,
          "privileged workflow_run checkout in job #{job_id} must not checkout triggering head SHA/ref"
        )
      end
    end
  end

  def validate_secret_debug(path, job_id, job)
    run_nodes(job).each do |run|
      text = run.scalar.to_s
      if secret_reference?(text) && text.match?(/\b(?:echo|printf|cat)\b/i)
        add(CONTROL_TRUST, path, run.line, "job #{job_id} attempts to print/dump secret context")
      end
    end
  end

  def job_privileged?(path, job, writes = nil)
    effective_writes = writes || write_permissions(job['permissions'] || @documents[path]['permissions'])
    !effective_writes.empty? || !effective_secret_nodes(path, job).empty? || !job['environment'].nil?
  end

  # Runtime-effective secret exposure includes workflow-level env inherited by
  # every job plus the complete job subtree (job env, container/services,
  # step env/with/run, and reusable-workflow secret arguments).
  def effective_secret_nodes(path, job)
    result = secret_nodes(job)
    workflow_env = @documents[path]&.[]('env')
    result.concat(secret_nodes(workflow_env)) if workflow_env
    result.uniq { |node| node.object_id }
  end

  def secret_nodes(node)
    return [] if node.nil?

    result = []
    each_scalar(node) { |child| result << child if secret_reference?(child.scalar.to_s) }
    result
  end

  def action_expression_bodies(value)
    value.to_s.scan(/\$\{\{(.*?)\}\}/m).flatten
  end

  def normalize_action_expression(body)
    normalized = body.to_s
    loop do
      updated = normalized.gsub(INDEXED_STRING_PROPERTY) { ".#{Regexp.last_match(2)}" }
      break if updated == normalized

      normalized = updated
    end
    normalized.gsub(/\s+/, '')
  end

  def normalized_action_expression_bodies(value)
    action_expression_bodies(value).map { |body| normalize_action_expression(body) }
  end

  def secret_reference?(value)
    normalized_action_expression_bodies(value).any? do |body|
      body.match?(SECRET_CONTEXT_REFERENCE) || body.match?(SECRET_OBJECT_DUMP_REFERENCE)
    end
  end

  def workflow_run_head_reference?(value)
    normalized_action_expression_bodies(value).any? do |body|
      body.match?(WORKFLOW_RUN_HEAD_REFERENCE) || body.match?(WORKFLOW_RUN_DYNAMIC_FIELD_REFERENCE)
    end
  end

  def validate_runner_routing(path, job_id, runs_on, lane:)
    runner_route_errors(runs_on).each do |message|
      add(CONTROL_RUNNER, path, runs_on.line, "#{lane} job #{job_id} #{message}")
    end
  end

  def runner_route_errors(node)
    if node.scalar?
      return runner_scalar_errors(node.scalar.to_s)
    end

    if node.seq?
      errors = []
      errors << 'has an empty runs-on label sequence; runner trust cannot be proven' if node.value.empty?
      node.value.each do |child|
        if child.scalar?
          errors.concat(runner_scalar_errors(child.scalar.to_s))
        else
          errors << 'has a non-scalar runs-on label; runner trust cannot be proven'
        end
      end
      return errors
    end

    unless node.map?
      return ['has an unsupported runs-on shape; runner trust cannot be proven']
    end

    errors = []
    unknown_keys = node.value.keys - %w[group labels]
    unless unknown_keys.empty?
      errors << "uses unknown runs-on mapping keys: #{unknown_keys.sort.join(', ')}"
    end

    group = node['group']
    if group.nil?
      errors << 'uses mapping-form runs-on without an explicitly registered GitHub-hosted group'
    elsif !group.scalar?
      errors << 'uses a non-scalar runner group; runner trust cannot be proven'
    else
      group_name = group.scalar.to_s
      if group_name.match?(DYNAMIC_EXPR)
        errors << 'uses a dynamic runner group; runner trust cannot be proven statically'
      elsif !@github_hosted_runner_groups.include?(group_name)
        errors << "uses runner group #{group_name.inspect} that is not in the trusted GitHub-hosted group registry"
      end
    end

    labels = node['labels']
    if labels
      if labels.scalar?
        errors.concat(runner_scalar_errors(labels.scalar.to_s))
      elsif labels.seq?
        labels.value.each do |child|
          if child.scalar?
            errors.concat(runner_scalar_errors(child.scalar.to_s))
          else
            errors << 'uses a non-scalar runs-on.labels entry; runner trust cannot be proven'
          end
        end
      else
        errors << 'uses a non-scalar runs-on.labels value; runner trust cannot be proven'
      end
    end

    errors
  end

  def runner_scalar_errors(value)
    normalized = value.to_s.strip
    if normalized.match?(DYNAMIC_EXPR)
      return [] if @approved_dynamic_runs_on.include?(normalized)

      return ['uses a dynamic runs-on expression that is not explicitly approved by the runner routing registry']
    end

    if normalized.casecmp('self-hosted').zero?
      return ['must not use a self-hosted runner; runner trust cannot be proven as GitHub-hosted']
    end

    return [] if @github_hosted_runner_labels.include?(normalized)

    ["uses runner label #{normalized.inspect} that is not in the trusted GitHub-hosted label registry"]
  end
end

if $PROGRAM_NAME == __FILE__
  validator = WorkflowTrustValidator.new(ARGV[0] ? Pathname.new(ARGV[0]) : ROOT)
  findings = validator.validate
  if findings.empty?
    puts 'Workflow trust-boundary validation passed: event trust classes, explicit permissions, write allowlist, normalized secret/head expressions, effective secret inheritance, runner registry, workflow_run, and reusable-workflow boundaries verified.'
    exit 0
  end

  warn 'Workflow trust-boundary validation failed:'
  findings.each { |finding| warn "- #{finding}" }
  exit 1
end