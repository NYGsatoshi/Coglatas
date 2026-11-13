#!/usr/bin/env ruby
# frozen_string_literal: true

# Inspect YAML syntax only; do not instantiate types, resolve aliases or execute input.
require 'json'
require 'psych'

source = STDIN.read
if source.bytesize > 1_048_576
  puts JSON.generate({ 'errors' => [{ 'line' => 1, 'reason' => 'oversized YAML source' }], 'uses' => [] })
  exit 0
end

errors = []
references = []

def pairs(node, errors)
  return [] unless node.is_a?(Psych::Nodes::Mapping)
  seen = {}
  node.children.each_slice(2).filter_map do |key, value|
    unless key.is_a?(Psych::Nodes::Scalar)
      errors << { 'line' => key.start_line + 1, 'reason' => 'non-scalar YAML mapping key' }
      next
    end
    if seen.key?(key.value)
      errors << { 'line' => key.start_line + 1, 'reason' => 'duplicate YAML mapping key' }
    end
    seen[key.value] = true
    if key.value == '<<'
      errors << { 'line' => key.start_line + 1, 'reason' => 'YAML merge key unsupported inside jobs' }
    end
    [key.value, key, value]
  end
end

def inspect_uses_pair(name, key, value, errors, references)
  return unless name == 'uses'
  if value.is_a?(Psych::Nodes::Scalar)
    references << { 'line' => key.start_line + 1, 'target' => value.value }
  else
    errors << { 'line' => key.start_line + 1, 'reason' => 'non-scalar uses value' }
  end
end

def inspect_jobs(root, errors, references)
  jobs_nodes = pairs(root, errors).filter_map { |name, _key, value| value if name == 'jobs' }
  if jobs_nodes.length != 1
    errors << { 'line' => 1, 'reason' => 'expected exactly one jobs mapping' }
    return
  end
  jobs = jobs_nodes.first
  unless jobs.is_a?(Psych::Nodes::Mapping)
    errors << { 'line' => jobs.start_line + 1, 'reason' => 'unsupported jobs structure' }
    return
  end
  if jobs.style == Psych::Nodes::Mapping::FLOW
    errors << { 'line' => jobs.start_line + 1, 'reason' => 'unsupported flow-style jobs' }
    return
  end
  pairs(jobs, errors).each do |_job_name, job_key, job|
    unless job.is_a?(Psych::Nodes::Mapping)
      errors << { 'line' => job_key.start_line + 1, 'reason' => 'unsupported job structure' }
      next
    end
    if job.style == Psych::Nodes::Mapping::FLOW
      errors << { 'line' => job.start_line + 1, 'reason' => 'unsupported flow-style job' }
      next
    end
    pairs(job, errors).each do |key_name, key, value|
      inspect_uses_pair(key_name, key, value, errors, references)
      next unless key_name == 'steps'
      unless value.is_a?(Psych::Nodes::Sequence)
        errors << { 'line' => key.start_line + 1, 'reason' => 'unsupported steps structure' }
        next
      end
      if value.style == Psych::Nodes::Sequence::FLOW
        errors << { 'line' => value.start_line + 1, 'reason' => 'unsupported flow-style steps' }
        next
      end
      value.children.each do |step|
        unless step.is_a?(Psych::Nodes::Mapping)
          errors << { 'line' => step.start_line + 1, 'reason' => 'unsupported step structure' }
          next
        end
        if step.style == Psych::Nodes::Mapping::FLOW
          errors << { 'line' => step.start_line + 1, 'reason' => 'unsupported flow-style step' }
          next
        end
        pairs(step, errors).each do |step_key, key_node, step_value|
          inspect_uses_pair(step_key, key_node, step_value, errors, references)
        end
      end
    end
  end
end

begin
  stream = Psych.parse_stream(source)
  if stream.children.length != 1
    errors << { 'line' => 1, 'reason' => 'expected one YAML document' }
  elsif !stream.children.first.root.is_a?(Psych::Nodes::Mapping)
    errors << { 'line' => 1, 'reason' => 'invalid workflow YAML root' }
  else
    inspect_jobs(stream.children.first.root, errors, references)
  end
rescue Psych::Exception, SystemStackError
  errors << { 'line' => 1, 'reason' => 'unparseable workflow YAML' }
end
puts JSON.generate({ 'errors' => errors, 'uses' => references })
