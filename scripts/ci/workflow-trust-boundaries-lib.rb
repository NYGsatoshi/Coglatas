#!/usr/bin/env ruby
# frozen_string_literal: true

require 'json'
require 'pathname'
require 'psych'
require 'set'

ROOT = Pathname.new(__dir__).join('../..').cleanpath unless defined?(ROOT)
WORKFLOW_GLOB = '.github/workflows/**/*.{yml,yaml}'
CONTROL_PERMISSION = 'GOV-WORKFLOW-PERM-001'
CONTROL_TRUST = 'GOV-TRUST-001'
CONTROL_RUNNER = 'GOV-RUNNER-001'
TRUST_REGISTRY_PATH = 'governance/workflow-trust-policy.json'

class YamlNode
  attr_reader :kind, :value, :line

  def initialize(kind, value, line)
    @kind = kind
    @value = value
    @line = line || 1
  end

  def map?
    kind == :map
  end

  def seq?
    kind == :seq
  end

  def scalar?
    kind == :scalar
  end

  def [](key)
    map? ? value[key] : nil
  end

  def scalar
    scalar? ? value : nil
  end
end

class WorkflowYamlParser
  class ParseError < StandardError; end

  def self.parse(path)
    text = path.read(encoding: 'UTF-8')
    stream = Psych.parse_stream(text, filename: path.to_s)
    docs = stream.children
    raise ParseError, 'YAML must contain exactly one document' unless docs.length == 1

    root = convert(docs.first.root)
    raise ParseError, 'workflow YAML root must be a mapping' unless root.map?

    root
  rescue Psych::SyntaxError => e
    line = e.line || 1
    raise ParseError, "YAML parse error at line #{line}: #{e.problem || e.message}"
  end

  def self.convert(node)
    case node
    when Psych::Nodes::Mapping
      result = {}
      node.children.each_slice(2) do |key_node, value_node|
        unless key_node.is_a?(Psych::Nodes::Scalar)
          raise ParseError, "mapping key at line #{key_node.start_line + 1} must be a scalar"
        end
        key = key_node.value
        if result.key?(key)
          raise ParseError, "duplicate mapping key #{key.inspect} at line #{key_node.start_line + 1}"
        end
        result[key] = convert(value_node)
      end
      YamlNode.new(:map, result, node.start_line + 1)
    when Psych::Nodes::Sequence
      YamlNode.new(:seq, node.children.map { |child| convert(child) }, node.start_line + 1)
    when Psych::Nodes::Scalar
      YamlNode.new(:scalar, node.value, node.start_line + 1)
    when Psych::Nodes::Alias
      raise ParseError, "YAML aliases are rejected fail-closed at line #{node.start_line + 1}"
    else
      raise ParseError, "unsupported YAML node #{node.class}"
    end
  end

  private_class_method :convert
end

Finding = Struct.new(:control_id, :path, :line, :message, keyword_init: true) do
  def to_s
    "#{path}:#{line}: [#{control_id}] #{message}"
  end
end

class WorkflowTrustValidator
  UNTRUSTED_EVENTS = Set['pull_request', 'pull_request_review'].freeze
  PRIVILEGED_EVENTS = Set['workflow_run', 'workflow_dispatch', 'push', 'release'].freeze
  SECRET_EXPR = /\$\{\{[^}]*\bsecrets\./i
  PR_HEAD_EXPR = /\$\{\{[^}]*\b(?:github\.event\.pull_request\.head|github\.head_ref)\b/i
  WORKFLOW_RUN_HEAD_EXPR = /\$\{\{[^}]*\bgithub\.event\.workflow_run\.(?:head_sha|head_branch)\b/i
  DYNAMIC_EXPR = /\$\{\{/
  ARTIFACT_DOWNLOAD_ACTION = %r{(?:^|/)(?:download-artifact|action-download-artifact)@}i

  attr_reader :root, :findings

  def initialize(root)
    @root = Pathname.new(root)
    joined = File.expand_path(@root.to_s)
    if root.to_s.include?("..") || root.to_s.include?("/") || root.to_s.include?("\\")
      raise "Invalid path: path traversal detected in #{root}"
    end
    @findings = []
    @documents = {}
    @allowlist = []
  end

  def validate
    policy = load_policy
    load_allowlist(policy)
    paths = Dir[root.join(WORKFLOW_GLOB).to_s].sort.map { |p| Pathname.new(p) }
    if paths.empty?
      add(CONTROL_TRUST, '.github/workflows', 1, 'no workflow YAML files found; validation is fail-closed')
      return findings
    end

    paths.each { |path| load_document(path) }
    @documents.each { |relative, document| validate_workflow(relative, document) }
    validate_allowlist_usage
    validate_local_reusable_edges
    findings.sort_by! { |f| [f.path, f.line, f.control_id, f.message] }
  rescue Errno::ENOENT, JSON::ParserError => e
    add(CONTROL_TRUST, 'governance/policy.json', 1, "cannot load governance policy: #{e.message}")
    findings
  end

  private

  def add(control_id, path, line, message)
    findings << Finding.new(control_id: control_id, path: path.to_s, line: line || 1, message: message)
  end

  def load_policy
    JSON.parse(root.join('governance/policy.json').read(encoding: 'UTF-8'))
  end

  def control(policy, id)
    controls = policy['controls']
    matches = controls.is_a?(Array) ? controls.select { |entry| entry.is_a?(Hash) && entry['id'] == id } : []
    raise JSON::ParserError, "policy must define exactly one #{id}" unless matches.length == 1

    matches.first
  end

  def load_allowlist(policy)
    permission = control(policy, CONTROL_PERMISSION)
    trust = control(policy, CONTROL_TRUST)
    runner = control(policy, CONTROL_RUNNER)

    expected_permission = permission['expected']
    expected_trust = trust['expected']
    expected_runner = runner['expected']
    unless expected_permission.is_a?(Hash) && expected_permission['persist_credentials'] == false
      raise JSON::ParserError, "#{CONTROL_PERMISSION} must require persist_credentials=false"
    end
    unless expected_trust.is_a?(Hash) && expected_trust['pull_request_target'] == 'forbidden'
      raise JSON::ParserError, "#{CONTROL_TRUST} must forbid pull_request_target"
    end
    unless expected_runner.is_a?(Hash) && expected_runner['untrusted_pr'] == 'forbidden'
      raise JSON::ParserError, "#{CONTROL_RUNNER} must forbid untrusted PR self-hosted routing"
    end

    canonical = expected_permission['write_permissions_allowlist']
    raise JSON::ParserError, "#{CONTROL_PERMISSION} write_permissions_allowlist must be an array" unless canonical.is_a?(Array)

    registry = JSON.parse(root.join(TRUST_REGISTRY_PATH).read(encoding: 'UTF-8'))
    unless registry['source_controls'] == [CONTROL_PERMISSION, CONTROL_TRUST, CONTROL_RUNNER]
      raise JSON::ParserError, "#{TRUST_REGISTRY_PATH} must bind exactly to GOV-04 source controls"
    end
    raw = registry['write_permissions']
    raise JSON::ParserError, "#{TRUST_REGISTRY_PATH} write_permissions must be an array" unless raw.is_a?(Array)

    @allowlist = raw.map do |entry|
      unless entry.is_a?(Hash) && entry['workflow'].is_a?(String) && entry['permissions'].is_a?(Array) &&
             entry['events'].is_a?(Array) && entry['jobs'].is_a?(Array) && entry['reason'].is_a?(String) && !entry['reason'].strip.empty?
        raise JSON::ParserError, "#{TRUST_REGISTRY_PATH} entries require workflow, permissions, events, jobs, and reason"
      end
      entry
    end

    canonical.each do |entry|
      next unless entry.is_a?(Hash)
      match = @allowlist.find { |candidate| candidate['workflow'] == entry['workflow'] }
      unless match && Set.new(entry['permissions']) <= Set.new(match['permissions'])
        raise JSON::ParserError, "#{TRUST_REGISTRY_PATH} must preserve canonical #{CONTROL_PERMISSION} allowlist entry for #{entry['workflow']}"
      end
    end
  end

  def relative(path)
    path.relative_path_from(root).to_s
  end

  def load_document(path)
    @documents[relative(path)] = WorkflowYamlParser.parse(path)
  rescue WorkflowYamlParser::ParseError => e
    line = e.message[/line (\d+)/, 1]&.to_i || 1
    add(CONTROL_TRUST, relative(path), line, e.message)
  end

  def validate_workflow(path, doc)
    on_node = doc['on']
    jobs = doc['jobs']
    permissions = doc['permissions']

    add(CONTROL_TRUST, path, doc.line, 'workflow must declare top-level on') if on_node.nil?
    unless jobs&.map?
      add(CONTROL_TRUST, path, jobs&.line || doc.line, 'workflow must declare jobs as a mapping')
      return
    end
    if permissions.nil?
      add(CONTROL_PERMISSION, path, doc.line, 'top-level permissions must be explicit')
    else
      validate_permissions_shape(path, permissions, 'top-level')
    end

    events = event_names(on_node)
    if events.include?('pull_request_target')
      add(CONTROL_TRUST, path, on_node&.line || doc.line, 'pull_request_target is forbidden by GOV-TRUST-001')
    end

    untrusted = !(events & UNTRUSTED_EVENTS).empty?
    workflow_run = events.include?('workflow_run')

    jobs.value.each do |job_id, job|
      unless job.map?
        add(CONTROL_TRUST, path, job.line, "job #{job_id.inspect} must be a mapping")
        next
      end
      job_permissions = job['permissions'] || permissions
      validate_permissions_shape(path, job['permissions'], "job #{job_id}") if job['permissions']
      writes = write_permissions(job_permissions)
      validate_write_allowlist(path, job_id, writes, events, job_permissions || job)

      validate_untrusted_job(path, job_id, job) if untrusted
      validate_workflow_run_job(path, job_id, job, writes) if workflow_run
      validate_checkout_steps(path, job_id, job, untrusted: untrusted, workflow_run: workflow_run)
      validate_secret_debug(path, job_id, job)
    end
  end

  def event_names(node)
    return Set.new if node.nil?
    if node.map?
      Set.new(node.value.keys)
    elsif node.seq?
      Set.new(node.value.filter_map(&:scalar))
    elsif node.scalar?
      Set[node.scalar]
    else
      Set.new
    end
  end

  def validate_permissions_shape(path, node, scope)
    return if node.nil?
    if node.scalar?
      value = node.scalar.to_s.downcase
      if value == 'write-all'
        add(CONTROL_PERMISSION, path, node.line, "#{scope} permissions: write-all is forbidden")
      elsif value == 'read-all'
        add(CONTROL_PERMISSION, path, node.line, "#{scope} permissions: read-all is broader than the minimal explicit default")
      else
        add(CONTROL_PERMISSION, path, node.line, "#{scope} permissions must be a mapping or empty mapping, not #{node.scalar.inspect}")
      end
      return
    end
    unless node.map?
      add(CONTROL_PERMISSION, path, node.line, "#{scope} permissions must be a mapping")
      return
    end

    node.value.each do |name, value|
      unless value.scalar? && %w[read write none].include?(value.scalar.to_s.downcase)
        add(CONTROL_PERMISSION, path, value.line, "#{scope} permission #{name.inspect} must be read, write, or none")
      end
    end
  end

  def write_permissions(node)
    return Set.new unless node&.map?
    Set.new(node.value.filter_map do |name, value|
      "#{name}:write" if value.scalar? && value.scalar.to_s.casecmp('write').zero?
    end)
  end

  def validate_write_allowlist(path, job_id, writes, events, node)
    return if writes.empty?
    entry = @allowlist.find { |candidate| candidate['workflow'] == path && candidate['jobs'].include?(job_id) }
    if entry.nil?
      add(CONTROL_PERMISSION, path, node.line, "job #{job_id} has write permissions #{writes.to_a.sort.join(', ')} but is not allowlisted")
      return
    end

    allowed = Set.new(entry['permissions'])
    extra = writes - allowed
    missing = allowed - writes
    add(CONTROL_PERMISSION, path, node.line, "job #{job_id} has unallowlisted write permissions: #{extra.to_a.sort.join(', ')}") unless extra.empty?
    add(CONTROL_PERMISSION, path, node.line, "job #{job_id} allowlist contains unused write permissions: #{missing.to_a.sort.join(', ')}") unless missing.empty?

    allowed_events = Set.new(entry['events'])
    unexpected_events = events - allowed_events
    unless unexpected_events.empty?
      add(CONTROL_PERMISSION, path, node.line, "write-capable job #{job_id} is reachable from events not in policy scope: #{unexpected_events.to_a.sort.join(', ')}")
    end
  end

  def validate_allowlist_usage
    @allowlist.each do |entry|
      path = entry['workflow']
      doc = @documents[path]
      if doc.nil?
        add(CONTROL_PERMISSION, path, 1, 'write-permission allowlist references a missing workflow')
        next
      end
      jobs = doc['jobs']
      next unless jobs&.map?
      entry['jobs'].each do |job_id|
        job = jobs[job_id]
        if job.nil?
          add(CONTROL_PERMISSION, path, jobs.line, "write-permission allowlist references missing job #{job_id.inspect}")
          next
        end
        writes = write_permissions(job['permissions'] || doc['permissions'])
        allowed = Set.new(entry['permissions'])
        if writes.empty?
          add(CONTROL_PERMISSION, path, job.line, "write-permission allowlist job #{job_id} has no effective write permission")
        elsif writes != allowed
          add(CONTROL_PERMISSION, path, job.line, "write-permission allowlist for job #{job_id} does not exactly match effective writes")
        end
      end
    end
  end

  def validate_untrusted_job(path, job_id, job)
    permissions = job['permissions'] || @documents[path]['permissions']
    writes = write_permissions(permissions)
    unless writes.empty?
      add(CONTROL_TRUST, path, permissions.line, "untrusted PR/review job #{job_id} must not receive write permissions: #{writes.to_a.sort.join(', ')}")
    end

    if job['environment']
      add(CONTROL_TRUST, path, job['environment'].line, "untrusted PR/review job #{job_id} must not bind a protected environment")
    end

    secret_nodes(job).each do |node|
      add(CONTROL_TRUST, path, node.line, "untrusted PR/review job #{job_id} references repository/environment secrets")
    end

    runs_on = job['runs-on']
    if runs_on.nil? && job['uses'].nil?
      add(CONTROL_RUNNER, path, job.line, "untrusted PR/review job #{job_id} must declare runs-on or a reusable workflow")
    elsif runs_on
      labels = scalar_values(runs_on)
      if labels.any? { |value| value.casecmp('self-hosted').zero? }
        add(CONTROL_RUNNER, path, runs_on.line, "untrusted PR/review job #{job_id} must not use self-hosted runner labels")
      end
      if labels.any? { |value| value.match?(DYNAMIC_EXPR) }
        add(CONTROL_RUNNER, path, runs_on.line, "untrusted PR/review job #{job_id} has dynamic runs-on routing; runner trust cannot be proven statically")
      end
    end

    secrets_node = job['secrets']
    if secrets_node && (!secrets_node.map? || !secrets_node.value.empty?)
      add(CONTROL_TRUST, path, secrets_node.line, "untrusted reusable-workflow caller #{job_id} must not pass secrets")
    end
  end

  def validate_workflow_run_job(path, job_id, job, writes)
    privileged = !writes.empty? || !secret_nodes(job).empty? || !job['environment'].nil?
    return unless privileged

    each_scalar(job) do |node|
      value = node.scalar.to_s
      if value.match?(WORKFLOW_RUN_HEAD_EXPR)
        add(CONTROL_TRUST, path, node.line, "privileged workflow_run job #{job_id} references triggering head ref/SHA; untrusted head execution is forbidden")
      end
      if value.match?(/\bgh\s+run\s+download\b/i) || value.match?(%r{/actions/runs/[^\s]+/artifacts}i)
        add(CONTROL_TRUST, path, node.line, "privileged workflow_run job #{job_id} downloads triggering workflow artifacts without a repository-owned verification boundary")
      end
    end

    uses_nodes(job).each do |uses|
      if uses.scalar.to_s.match?(ARTIFACT_DOWNLOAD_ACTION)
        add(CONTROL_TRUST, path, uses.line, "privileged workflow_run job #{job_id} downloads workflow artifacts; unverified artifact execution is forbidden")
      end
    end

    return if writes.empty?

    run_text = run_nodes(job).map(&:scalar).join("\n")
    proof = ['gh api', '/pulls/', '.head.sha']
    missing = proof.reject { |fragment| run_text.include?(fragment) }
    unless missing.empty?
      add(CONTROL_TRUST, path, job.line, "write-capable workflow_run job #{job_id} must re-resolve PR/head via authoritative API (missing: #{missing.join(', ')})")
    end
  end

  def validate_checkout_steps(path, job_id, job, untrusted:, workflow_run:)
    steps = job['steps']
    return unless steps&.seq?
    steps.value.each do |step|
      next unless step.map?
      uses = step['uses']
      next unless uses&.scalar? && uses.scalar.to_s.match?(%r{\Aactions/checkout@}i)

      with = step['with']
      persist = with&.map? ? with['persist-credentials'] : nil
      if untrusted && !(persist&.scalar? && persist.scalar.to_s.casecmp('false').zero?)
        add(CONTROL_PERMISSION, path, persist&.line || uses.line, "untrusted checkout in job #{job_id} must set persist-credentials: false")
      end

      ref = with&.map? ? with['ref'] : nil
      if untrusted && ref&.scalar?
        value = ref.scalar.to_s
        if value.match?(PR_HEAD_EXPR) || (value.match?(DYNAMIC_EXPR) && value.strip != '${{ github.sha }}')
          add(CONTROL_TRUST, path, ref.line, "untrusted checkout in job #{job_id} uses an arbitrary/dynamic ref expression")
        end
      end
      if workflow_run && ref&.scalar? && ref.scalar.to_s.match?(WORKFLOW_RUN_HEAD_EXPR)
        add(CONTROL_TRUST, path, ref.line, "workflow_run checkout in job #{job_id} must not checkout triggering head SHA/ref")
      end
    end
  end

  def validate_secret_debug(path, job_id, job)
    run_nodes(job).each do |run|
      text = run.scalar.to_s
      if text.match?(/(?:echo|printf|cat)\s+[^\n]*\$\{\{[^}]*\bsecrets\./i) || text.match?(/\$\{\{\s*toJson\(secrets\)\s*\}\}/i)
        add(CONTROL_TRUST, path, run.line, "job #{job_id} attempts to print/dump secret context")
      end
    end
  end

  def validate_local_reusable_edges
    @documents.each do |caller_path, caller|
      events = event_names(caller['on'])
      inherited_untrusted = !(events & UNTRUSTED_EVENTS).empty?
      jobs = caller['jobs']
      next unless jobs&.map?
      jobs.value.each do |job_id, job|
        next unless job.map?
        uses = job['uses']
        next unless uses&.scalar?
        target = uses.scalar.to_s
        next unless target.start_with?('./.github/workflows/')

        callee_path = target.sub(%r{\A\./}, '')
        callee = @documents[callee_path]
        if callee.nil?
          add(CONTROL_TRUST, caller_path, uses.line, "reusable workflow #{target.inspect} does not resolve to a scanned local workflow")
          next
        end
        unless event_names(callee['on']).include?('workflow_call')
          add(CONTROL_TRUST, caller_path, uses.line, "local reusable workflow #{target.inspect} must declare workflow_call")
        end
        next unless inherited_untrusted

        callee_jobs = callee['jobs']
        next unless callee_jobs&.map?
        callee_jobs.value.each do |callee_job_id, callee_job|
          next unless callee_job.map?
          validate_untrusted_job(callee_path, callee_job_id, callee_job)
          validate_checkout_steps(callee_path, callee_job_id, callee_job, untrusted: true, workflow_run: false)
        end
      end
    end
  end

  def scalar_values(node)
    return [node.scalar.to_s] if node.scalar?
    return node.value.filter_map { |child| child.scalar.to_s if child.scalar? } if node.seq?
    []
  end

  def each_scalar(node, &block)
    if node.scalar?
      yield node
    elsif node.seq?
      node.value.each { |child| each_scalar(child, &block) }
    elsif node.map?
      node.value.each_value { |child| each_scalar(child, &block) }
    end
  end

  def secret_nodes(node)
    result = []
    each_scalar(node) { |child| result << child if child.scalar.to_s.match?(SECRET_EXPR) }
    result
  end

  def uses_nodes(job)
    result = []
    each_map(job) do |mapping|
      uses = mapping['uses']
      result << uses if uses&.scalar?
    end
    result
  end

  def run_nodes(job)
    result = []
    each_map(job) do |mapping|
      run = mapping['run']
      result << run if run&.scalar?
    end
    result
  end

  def each_map(node, &block)
    if node.map?
      yield node
      node.value.each_value { |child| each_map(child, &block) }
    elsif node.seq?
      node.value.each { |child| each_map(child, &block) }
    end
  end
end

if $PROGRAM_NAME == __FILE__
  validator = WorkflowTrustValidator.new(ARGV[0] ? Pathname.new(ARGV[0]) : ROOT)
  findings = validator.validate
  if findings.empty?
    puts 'Workflow trust-boundary validation passed: explicit permissions, write allowlist, untrusted PR, checkout, secret/environment, runner, workflow_run, and reusable-workflow boundaries verified.'
    exit 0
  end

  warn 'Workflow trust-boundary validation failed:'
  findings.each { |finding| warn "- #{finding}" }
  exit 1
end
