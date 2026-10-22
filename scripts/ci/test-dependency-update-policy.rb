#!/usr/bin/env ruby
# frozen_string_literal: true

# Negative-control checks for patch update cadence, bot ownership and the
# exact production SDK image contract. No package installation or write token.
require 'yaml'
require 'json'
require 'pathname'

ROOT = Pathname.new(__dir__).join('../..').realpath

ECOSYSTEM_KEY = 'package-ecosystem'
SDK_IMAGE_NAMES = %w[dotnet/sdk mcr.microsoft.com/dotnet/sdk].freeze
# Dependabot cron must have a minimum 24-hour interval; stagger daily checks.
EXPECTED_SCHEDULES = {
  'npm' => '0 3 * * *',
  'nuget' => '10 3 * * *',
  'dotnet-sdk' => '20 3 * * *',
  'github-actions' => '30 3 * * *',
  'docker' => '40 3 * * *',
  'docker-compose' => '50 3 * * *'
}.freeze
EXPECTED_DIRECTORIES = {
  'npm' => %w[/ /frontend /coglatas-frontend /tools/frontend-inspections],
  'nuget' => %w[/src/Coglatas.Web /src/Coglatas.Application /src/Coglatas.Infrastructure /tests/Coglatas.Tests],
  'dotnet-sdk' => ['/'],
  'github-actions' => ['/'],
  'docker' => %w[/ /deploy/sakura /infra/docker],
  'docker-compose' => %w[/ /deploy/sakura /deploy/onprem /infra/compose/dev /infra/compose/test /infra/compose/performance]
}.freeze

def check_policy(dependabot)
  errors = []
  updates = dependabot.fetch('updates', [])
  return ['Dependabot updates must be a list'] unless updates.is_a?(Array)

  # Keep every entry. A second entry with the same ecosystem cannot mask an
  # unreviewed directory or a bypass of the image/cooldown policy.
  entries_by_ecosystem = Hash.new { |hash, ecosystem| hash[ecosystem] = [] }
  updates.each_with_index do |entry, index|
    unless entry.is_a?(Hash) && entry[ECOSYSTEM_KEY].is_a?(String)
      errors << "Dependabot update[#{index}] needs a package ecosystem"
      next
    end
    ecosystem = entry.fetch(ECOSYSTEM_KEY)
    entries_by_ecosystem[ecosystem] << entry
    unless EXPECTED_SCHEDULES.key?(ecosystem)
      errors << "Unreviewed Dependabot ecosystem #{ecosystem}"
      next
    end

    directories = if entry.key?('directories')
                    entry['directories']
                  elsif entry.key?('directory')
                    [entry['directory']]
                  end
    expected_directories = EXPECTED_DIRECTORIES.fetch(ecosystem)
    unless directories.is_a?(Array) && directories.all? { |dir| dir.is_a?(String) } &&
           directories.length == expected_directories.length &&
           directories.sort == expected_directories.sort &&
           !(entry.key?('directory') && entry.key?('directories'))
      errors << "#{ecosystem} must monitor exactly the approved directories #{expected_directories.join(', ')}"
    end

    schedule = entry.fetch('schedule', {})
    errors << "#{ecosystem} must poll daily including weekends (minimum 24-hour cron interval)" unless
      schedule.is_a?(Hash) && schedule['interval'] == 'cron' &&
      schedule['cronjob'] == EXPECTED_SCHEDULES.fetch(ecosystem) &&
      schedule['timezone'] == 'Asia/Tokyo'

    cooldown = entry.fetch('cooldown', {})
    errors << "#{ecosystem} must exclude all dependencies from cooldown" unless
      cooldown.is_a?(Hash) && cooldown.keys == ['exclude'] && cooldown['exclude'] == ['*']

    limit = entry.fetch('open-pull-requests-limit', nil)
    errors << "#{ecosystem} PR limit must remain positive" unless limit.is_a?(Integer) && limit.positive?

    if %w[npm nuget].include?(ecosystem)
      groups = entry.fetch('groups', {})
      errors << "#{ecosystem} security update group missing" unless groups.is_a?(Hash) &&
        groups.values.any? do |group|
          group.is_a?(Hash) && group['applies-to'] == 'security-updates' &&
            group.fetch('patterns', []).include?('*')
        end
    end

    case ecosystem
    when 'dotnet-sdk'
      # All SDK versions may be *proposed*. The pin remains exact and merging
      # a new major requires a separate approval, not a bot ignore rule.
      errors << '.NET SDK version proposals must not be blocked by ignore rules' if
        entry.key?('ignore') && !entry['ignore'].empty?
    when 'npm'
      groups = entry.fetch('groups', {})
      angular = groups.fetch('npm-angular-version-updates', {})
      other = groups.fetch('npm-compatible-version-updates', {})
      expected = %w[@angular/* @angular-devkit/*]
      errors << 'Angular must have a focused update group' unless
        angular['applies-to'] == 'version-updates' &&
        (expected - angular.fetch('patterns', [])).empty?
      errors << 'Generic npm updates must exclude Angular and NgRx' unless
        (expected + ['@ngrx/*'] - other.fetch('exclude-patterns', [])).empty? &&
        other.fetch('patterns', []).include?('*')
    when 'docker', 'docker-compose'
      ignored = entry.fetch('ignore', [])
      errors << "#{ecosystem} ignore rules must remain explicit" unless ignored.is_a?(Array)
      ignored = [] unless ignored.is_a?(Array)
      errors << "#{ecosystem} must retain .NET SDK image update coverage" if
        ignored.any? { |item| item.is_a?(Hash) && SDK_IMAGE_NAMES.include?(item['dependency-name']) }
      # Dependabot cannot distinguish Current from LTS Node releases.
      # Hold Node major proposals until the owner approves a new LTS baseline,
      # but keep Node 24 patch/digest updates and other images eligible.
      node_holds = ignored.select { |item| item.is_a?(Hash) && item['dependency-name'] == 'node' }
      errors << "#{ecosystem} must hold Node major updates until LTS migration approval" unless
        node_holds.length == 1 && node_holds.first.keys.sort == ['dependency-name', 'update-types'] &&
        node_holds.first['update-types'] == ['version-update:semver-major']
      groups = entry.fetch('groups', {})
      prefix = ecosystem == 'docker' ? 'docker' : 'docker-compose'
      sdk_group = groups.fetch("#{prefix}-sdk-updates", {})
      others = groups.fetch("#{prefix}-image-updates", {})
      errors << "#{ecosystem} SDK images need a focused version-update group" unless
        sdk_group['applies-to'] == 'version-updates' &&
        (SDK_IMAGE_NAMES - sdk_group.fetch('patterns', [])).empty?
      errors << "#{ecosystem} unrelated images must remain separate" unless
        others.fetch('patterns', []).include?('*') &&
        (SDK_IMAGE_NAMES - others.fetch('exclude-patterns', [])).empty?
    end
  end

  # Reject missing, duplicate or unrecognized configurations. Dependabot
  # legitimately permits multiple entries per ecosystem, but this repository
  # deliberately maintains one audited multi-directory entry for each.
  EXPECTED_SCHEDULES.each_key do |ecosystem|
    count = entries_by_ecosystem[ecosystem].length
    errors << "#{ecosystem} must have exactly one fully reviewed update entry; found #{count}" unless count == 1
  end
  errors
end

def image_errors(source, sdk_version)
  errors = []
  candidates = [
    'Dockerfile', 'docker-compose.yml',
    *Dir.glob(ROOT.join('infra/docker/*.Dockerfile').to_s).map { |p| Pathname.new(p).relative_path_from(ROOT).to_s },
    *Dir.glob(ROOT.join('infra/compose/**/*.yml').to_s).map { |p| Pathname.new(p).relative_path_from(ROOT).to_s },
    *Dir.glob(ROOT.join('deploy/**/*.yml').to_s).map { |p| Pathname.new(p).relative_path_from(ROOT).to_s }
  ]
  matches = 0
  candidates.uniq.each do |name|
    path = ROOT / name
    next unless path.file?
    contents = source.fetch(name, nil) || path.read
    next unless contents.include?('mcr.microsoft.com/dotnet/sdk:')
    contents.scan(%r{mcr\.microsoft\.com/dotnet/sdk:[^\s'"]+}).each do |reference|
      matches += 1
      match = %r{\Amcr\.microsoft\.com/dotnet/sdk:(\d+\.\d+\.\d+)@sha256:([a-f0-9]{64})\z}.match(reference)
      if !match || match[1] != sdk_version
        errors << "#{name}: SDK image must match global.json #{sdk_version} and use an immutable digest"
      end
    end
  end
  errors << 'SDK image manifests unexpectedly missing' if matches < 3
  errors
end

# The production build and development images must stay on an owner-approved
# Node LTS major. Dependabot digest updates within that major remain allowed.
NODE_LTS_DOCKER_IMAGES = {
  'Dockerfile' => /\AFROM node:24@sha256:[0-9a-f]{64} AS frontend-deps\z/,
  'infra/docker/frontend-dev.Dockerfile' => /\AFROM node:24-alpine@sha256:[0-9a-f]{64}\z/
}.freeze

def node_lts_image_errors(sources)
  NODE_LTS_DOCKER_IMAGES.filter_map do |name, expected|
    path = ROOT / name
    unless path.file?
      next "#{name}: approved LTS Node Dockerfile must exist"
    end
    text = sources.fetch(name, nil) || path.read
    node_references = text.lines.map(&:strip).select { |line| line.start_with?('FROM node:') }
    if node_references.length != 1 || !expected.match?(node_references.first)
      "#{name}: the only Node base image must be immutable SHA-pinned Node 24 LTS"
    end
  end
end

dependabot = YAML.safe_load((ROOT / '.github/dependabot.yml').read, aliases: false)
global_json = JSON.parse((ROOT / 'global.json').read)
sdk = global_json.fetch('sdk', {})
sdk_version = sdk['version']
abort('SDK must be pinned to stable .NET 10') unless sdk_version.is_a?(String) &&
  sdk_version.match?(/\A10\.0\.\d{3}\z/) &&
  sdk['rollForward'] == 'disable' && sdk['allowPrerelease'] == false

errors = check_policy(dependabot) + image_errors({}, sdk_version) + node_lts_image_errors({})
abort(errors.join("\n")) unless errors.empty?

mutations = {
  'weekly npm' => ->(d) { d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'npm' }['schedule']['interval'] = 'weekly' },
  'omit weekend' => ->(d) { d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'nuget' }['schedule']['cronjob'] = '10 3 * * 1-5' },
  'reject subdaily npm cron' => ->(d) { d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'npm' }['schedule']['cronjob'] = '0 0,6,12,18 * * *' },
  'reject subdaily Docker cron' => ->(d) { d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }['schedule']['cronjob'] = '40 */6 * * *' },
  'restore default cooldown' => ->(d) { d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }.delete('cooldown') },
  'narrow cooldown exclusion' => ->(d) { d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'npm' }['cooldown']['exclude'] = ['@angular/*'] },
  'no native SDK' => ->(d) { d['updates'].reject! { |u| u[ECOSYSTEM_KEY] == 'dotnet-sdk' } },
  'ignore SDK major candidate' => ->(d) { d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'dotnet-sdk' }['ignore'] = [{'dependency-name'=>'*', 'update-types'=>['version-update:semver-major']}] },
  'remove Docker Node LTS hold' => ->(d) {
    d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }['ignore'].reject! { |r| r['dependency-name'] == 'node' }
  },
  'remove Compose Node LTS hold' => ->(d) {
    d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker-compose' }['ignore'].reject! { |r| r['dependency-name'] == 'node' }
  },
  'overbroad Node hold' => ->(d) {
    d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }['ignore'].find { |r| r['dependency-name'] == 'node' }.delete('update-types')
  },
  'partial Node major hold' => ->(d) {
    d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }['ignore'].find { |r| r['dependency-name'] == 'node' }['versions'] = ['26.0.0']
  },
  'disable SDK Docker update' => ->(d) { d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }['ignore'] << {'dependency-name' => 'dotnet/sdk'} },
  'merge unrelated Docker updates with SDK' => ->(d) { d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }['groups']['docker-image-updates']['exclude-patterns'] = [] },
  'duplicate earlier bad Docker entry' => ->(d) {
    docker = d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }
    d['updates'].unshift(Marshal.load(Marshal.dump(docker)).merge('directories' => ['/infra/docker']))
  },
  'duplicate later bad Docker entry' => ->(d) {
    docker = d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }
    d['updates'] << Marshal.load(Marshal.dump(docker)).merge('cooldown' => {'default-days' => 7})
  },
  'duplicate valid Docker entry' => ->(d) {
    docker = d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }
    d['updates'] << Marshal.load(Marshal.dump(docker))
  },
  'missing performance compose directory' => ->(d) {
    d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker-compose' }['directories'].delete('/infra/compose/performance')
  },
  'missing SDK Dockerfile directory' => ->(d) {
    d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'docker' }['directories'].delete('/infra/docker')
  },
  'unreviewed npm directory' => ->(d) {
    d['updates'].find { |u| u[ECOSYSTEM_KEY] == 'npm' }['directories'] << '/new-unknown-directory'
  },
  'unreviewed ecosystem' => ->(d) {
    d['updates'] << {'package-ecosystem' => 'gitsubmodule', 'directory' => '/', 'schedule' => {'interval' => 'daily'}}
  }
}

mutations.each do |name, change|
  copy = Marshal.load(Marshal.dump(dependabot))
  change.call(copy)
  abort("Guard missed negative policy mutation: #{name}") if check_policy(copy).empty?
end
bad = { 'Dockerfile' => (ROOT / 'Dockerfile').read.sub(
  /mcr\.microsoft\.com\/dotnet\/sdk:#{Regexp.escape(sdk_version)}/,
  'mcr.microsoft.com/dotnet/sdk:10.0.999'
) }
abort('Guard missed SDK image/version mismatch') if image_errors(bad, sdk_version).empty?

bad_node_major = {
  'Dockerfile' => (ROOT / 'Dockerfile').read.sub(/FROM node:24@sha256:/, 'FROM node:26@sha256:')
}
abort('Guard missed unreviewed Node major') if node_lts_image_errors(bad_node_major).empty?
bad_node_digest = {
  'infra/docker/frontend-dev.Dockerfile' => (ROOT / 'infra/docker/frontend-dev.Dockerfile').read.sub(/@sha256:[a-f0-9]{64}/, '')
}
abort('Guard missed unpinned Node LTS image') if node_lts_image_errors(bad_node_digest).empty?

puts "Dependabot-only patch policy, SDK/image parity and Node LTS: PASS (#{mutations.length + 3} negative controls)"
