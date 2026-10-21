#!/usr/bin/env ruby
# frozen_string_literal: true

# Negative-control checks for patch update cadence, bot ownership and the
# exact production SDK image contract. No package installation or write token.
require 'yaml'
require 'json'
require 'pathname'

ROOT = Pathname.new(__dir__).join('../..').realpath

def check_policy(dependabot)
  errors = []
  updates = dependabot.fetch('updates', [])
  by_ecosystem = updates.to_h { |entry| [entry.fetch('package-ecosystem', ''), entry] }

  # Six-hour, seven-day polling with staggered starts to avoid registry spikes.
  expected_cron = {
    'npm' => '0 0,6,12,18 * * *',
    'nuget' => '10 0,6,12,18 * * *',
    'dotnet-sdk' => '20 0,6,12,18 * * *',
    'github-actions' => '30 0,6,12,18 * * *',
    'docker' => '40 0,6,12,18 * * *',
    'docker-compose' => '50 0,6,12,18 * * *'
  }
  expected_cron.each do |ecosystem, expression|
    entry = by_ecosystem[ecosystem]
    unless entry
      errors << "Dependabot #{ecosystem} ecosystem is missing"
      next
    end
    schedule = entry.fetch('schedule', {})
    errors << "#{ecosystem} must poll six-hourly including weekends" unless
      schedule['interval'] == 'cron' &&
      schedule['cronjob'] == expression &&
      schedule['timezone'] == 'Asia/Tokyo'
    # GitHub applies a three-day default even when cooldown is absent.
    # A wildcard exclusion explicitly removes that delay for version PR creation.
    cooldown = entry.fetch('cooldown', {})
    errors << "#{ecosystem} must exclude all dependencies from cooldown" unless
      cooldown['exclude'] == ['*']
    errors << "#{ecosystem} PR limit must remain positive" unless
      entry.fetch('open-pull-requests-limit', 0).is_a?(Integer) &&
      entry['open-pull-requests-limit'] > 0
  end
  %w[npm nuget].each do |ecosystem|
    entry = by_ecosystem[ecosystem]
    next unless entry
    errors << "#{ecosystem} security update group missing" unless entry.fetch('groups', {}).values.any? do |group|
      group['applies-to'] == 'security-updates' && group.fetch('patterns', []).include?('*')
    end
  end

  sdk = by_ecosystem.fetch('dotnet-sdk', {})
  errors << '.NET SDK updater must own the root global.json' unless sdk['directory'] == '/'
  errors << '.NET SDK version proposals must not be blocked by major ignore' if sdk.fetch('ignore', []).any? do |item|
    item['dependency-name'] == '*' && item.fetch('update-types', []).include?('version-update:semver-major')
  end

  npm_groups = by_ecosystem.fetch('npm', {}).fetch('groups', {})
  angular = npm_groups.fetch('npm-angular-version-updates', {})
  other = npm_groups.fetch('npm-compatible-version-updates', {})
  expected_angular = %w[@angular/* @angular-devkit/*]
  errors << 'Angular must have a focused update group' unless
    angular['applies-to'] == 'version-updates' && (expected_angular - angular.fetch('patterns', [])).empty?
  errors << 'Generic npm updates must exclude Angular and NgRx' unless
    (expected_angular + ['@ngrx/*'] - other.fetch('exclude-patterns', [])).empty?

  %w[docker docker-compose].each do |ecosystem|
    entry = by_ecosystem.fetch(ecosystem, {})
    ignored = entry.fetch('ignore', []).map { |item| item['dependency-name'] }
    errors << "#{ecosystem} must retain .NET SDK image update coverage" if
      ignored.any? { |name| ['dotnet/sdk', 'mcr.microsoft.com/dotnet/sdk'].include?(name) }
    errors << "#{ecosystem} must surface Node major candidate PRs" if
      entry.fetch('ignore', []).any? { |item|
        item['dependency-name'] == 'node' &&
        item.fetch('update-types', []).include?('version-update:semver-major')
      }
    groups = entry.fetch('groups', {})
    sdk_group = groups.fetch(ecosystem == 'docker' ? 'docker-sdk-updates' : 'docker-compose-sdk-updates', {})
    others = groups.fetch(ecosystem == 'docker' ? 'docker-image-updates' : 'docker-compose-image-updates', {})
    patterns = ['dotnet/sdk', 'mcr.microsoft.com/dotnet/sdk']
    errors << "#{ecosystem} SDK images need a focused version-update group" unless
      sdk_group['applies-to'] == 'version-updates' &&
      (patterns - sdk_group.fetch('patterns', [])).empty?
    errors << "#{ecosystem} unrelated images must remain separate" unless
      others.fetch('patterns', []).include?('*') &&
      (patterns - others.fetch('exclude-patterns', [])).empty?
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

dependabot = YAML.safe_load((ROOT / '.github/dependabot.yml').read, aliases: false)
global_json = JSON.parse((ROOT / 'global.json').read)
sdk = global_json.fetch('sdk', {})
sdk_version = sdk['version']
abort('SDK must be pinned to stable .NET 10') unless sdk_version.is_a?(String) &&
  sdk_version.match?(/\A10\.0\.\d{3}\z/) &&
  sdk['rollForward'] == 'disable' && sdk['allowPrerelease'] == false

errors = check_policy(dependabot) + image_errors({}, sdk_version)
abort(errors.join("\n")) unless errors.empty?

mutations = {
  'weekly npm' => ->(d) { d['updates'].find { |u| u['package-ecosystem'] == 'npm' }['schedule']['interval'] = 'weekly' },
  'omit weekend' => ->(d) { d['updates'].find { |u| u['package-ecosystem'] == 'nuget' }['schedule']['cronjob'] = '10 0,6,12,18 * * 1-5' },
  'restore default cooldown' => ->(d) { d['updates'].find { |u| u['package-ecosystem'] == 'docker' }.delete('cooldown') },
  'narrow cooldown exclusion' => ->(d) { d['updates'].find { |u| u['package-ecosystem'] == 'npm' }['cooldown']['exclude'] = ['@angular/*'] },
  'no native SDK' => ->(d) { d['updates'].reject! { |u| u['package-ecosystem'] == 'dotnet-sdk' } },
  'ignore SDK major candidate' => ->(d) { d['updates'].find { |u| u['package-ecosystem'] == 'dotnet-sdk' }['ignore'] = [{'dependency-name'=>'*', 'update-types'=>['version-update:semver-major']}] },
  'disable SDK Docker update' => ->(d) { d['updates'].find { |u| u['package-ecosystem'] == 'docker' }['ignore'] << {'dependency-name' => 'dotnet/sdk'} },
  'merge unrelated Docker updates with SDK' => ->(d) { d['updates'].find { |u| u['package-ecosystem'] == 'docker' }['groups']['docker-image-updates']['exclude-patterns'] = [] }
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

puts "Dependabot-only patch policy, SDK/image parity: PASS (#{mutations.length + 1} negative controls)"
