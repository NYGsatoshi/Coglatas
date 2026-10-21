#!/usr/bin/env ruby
# frozen_string_literal: true

# Fail-closed configuration guard for dependency update ownership and PR safety.
# Does not call registries, execute package manager scripts, or grant write tokens.
require 'json'
require 'yaml'
require 'pathname'

ROOT = Pathname.new(__dir__).join('../..').realpath

def check_policy(dependabot, renovate)
  errors = []
  updates = dependabot.fetch('updates', [])
  by_ecosystem = updates.to_h { |entry| [entry.fetch('package-ecosystem', ''), entry] }

  %w[npm nuget docker docker-compose].each do |ecosystem|
    entry = by_ecosystem[ecosystem]
    unless entry
      errors << "Dependabot #{ecosystem} ecosystem is missing"
      next
    end
    errors << "#{ecosystem} must check daily" unless entry.dig('schedule', 'interval') == 'daily'
    errors << "#{ecosystem} patch cooldown must be one day" unless entry.dig('cooldown', 'default-days') == 1
  end

  %w[npm nuget].each do |ecosystem|
    entry = by_ecosystem[ecosystem]
    next unless entry
    cooldown = entry.fetch('cooldown', {})
    errors << "#{ecosystem} patch cooldown must be one day" unless cooldown['semver-patch-days'] == 1
    errors << "#{ecosystem} security update group missing" unless entry.fetch('groups', {}).values.any? do |group|
      group['applies-to'] == 'security-updates' && group.fetch('patterns', []).include?('*')
    end
  end

  npm_groups = by_ecosystem.fetch('npm', {}).fetch('groups', {})
  angular = npm_groups.fetch('npm-angular-version-updates', {})
  other = npm_groups.fetch('npm-compatible-version-updates', {})
  expected_angular = %w[@angular/* @angular-devkit/*]
  errors << 'Angular must have a focused version update group' unless
    angular['applies-to'] == 'version-updates' && (expected_angular - angular.fetch('patterns', [])).empty?
  errors << 'Generic npm updates must exclude Angular and NgRx' unless
    (expected_angular + ['@ngrx/*'] - other.fetch('exclude-patterns', [])).empty?
  errors << 'Generic npm updates must cover other dependencies' unless other.fetch('patterns', []).include?('*')

  %w[docker docker-compose].each do |ecosystem|
    ignored = by_ecosystem.fetch(ecosystem, {}).fetch('ignore', []).map { |entry| entry['dependency-name'] }
    unless ignored.include?('dotnet/sdk') && ignored.include?('mcr.microsoft.com/dotnet/sdk')
      errors << "#{ecosystem} must defer SDK images to Renovate"
    end
  end

  errors << 'Renovate must never automerge' unless renovate['automerge'] == false
  managers = renovate.fetch('enabledManagers', [])
  errors << 'Renovate managers must be restricted' unless
    managers.sort == %w[docker-compose dockerfile nuget].sort
  rules = renovate.fetch('packageRules', [])
  nuget_off = rules.any? do |rule|
    rule['enabled'] == false && rule.fetch('matchManagers', []) == ['nuget'] &&
      ( %w[nuget msbuild-sdk] - rule.fetch('matchDepTypes', []) ).empty?
  end
  docker_off = rules.any? do |rule|
    rule['enabled'] == false && ( %w[dockerfile docker-compose] - rule.fetch('matchManagers', []) ).empty? &&
      !rule.key?('matchPackageNames')
  end
  errors << 'Non-SDK NuGet updates must remain with Dependabot' unless nuget_off
  errors << 'Non-SDK Docker updates must remain with Dependabot' unless docker_off

  sdk = rules.find do |rule|
    rule['enabled'] == true && rule.fetch('matchManagers', []) == ['nuget'] &&
      rule.fetch('matchDepTypes', []) == ['dotnet-sdk']
  end
  image = rules.find do |rule|
    rule['enabled'] == true &&
      ( %w[dockerfile docker-compose] - rule.fetch('matchManagers', []) ).empty? &&
      rule.fetch('matchPackageNames', []) == ['mcr.microsoft.com/dotnet/sdk']
  end
  errors << 'Renovate must explicitly enable only global.json SDK pins' unless sdk
  errors << 'Renovate must explicitly enable the SDK Docker images' unless image
  if sdk && image
    errors << 'SDK and image versions must be grouped for one review' unless
      sdk['groupSlug'] == image['groupSlug'] && !sdk['groupSlug'].to_s.empty?
    errors << 'SDK updates must stay in .NET 10' unless
      sdk['allowedVersions'] == '/^10\\.0\\./' && image['allowedVersions'] == '/^10\\.0\\./'
  end
  errors
end

dependabot = YAML.safe_load((ROOT / '.github/dependabot.yml').read, aliases: false)
renovate = JSON.parse((ROOT / 'renovate.json').read)
errors = check_policy(dependabot, renovate)
abort(errors.join("\n")) unless errors.empty?

# Negative self-tests ensure that regressions in the validator do not silently
# turn automatic merging, duplicate bot ownership, or slower patch detection on.
mutations = {
  'automatic merge' => ->(d, r) { r['automerge'] = true },
  'weekly npm' => ->(d, r) { d['updates'].find { |u| u['package-ecosystem'] == 'npm' }['schedule']['interval'] = 'weekly' },
  'duplicate Docker ownership' => ->(d, r) { d['updates'].find { |u| u['package-ecosystem'] == 'docker' }['ignore'] = [] },
  'unrestricted managers' => ->(d, r) { r['enabledManagers'] << 'npm' },
  'SDK image disengaged' => ->(d, r) { r['packageRules'].find { |rule| rule['matchPackageNames'] == ['mcr.microsoft.com/dotnet/sdk'] }['enabled'] = false }
}
mutations.each do |name, change|
  dep_copy = Marshal.load(Marshal.dump(dependabot))
  ren_copy = Marshal.load(Marshal.dump(renovate))
  change.call(dep_copy, ren_copy)
  abort("Guard failed its negative test: #{name}") if check_policy(dep_copy, ren_copy).empty?
end

puts "Dependency update ownership/policy: PASS (#{mutations.size} negative controls)"
