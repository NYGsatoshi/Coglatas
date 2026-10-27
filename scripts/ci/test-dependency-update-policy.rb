#!/usr/bin/env ruby
# frozen_string_literal: true

# Guard Mend-hosted Renovate configuration and dependency update ownership and immutable
# SDK/Node image parity. All negative controls are local; no network tokens.
require 'yaml'
require 'json'
require 'pathname'

ROOT = Pathname.new(__dir__).join('../..').realpath

def update_errors(config)
  errors = []
  errors << 'Renovate config must be an object' unless config.is_a?(Hash)
  return errors unless config.is_a?(Hash)

  errors << 'Reviewed Renovate preset missing' unless config.fetch('extends', []).include?('config:recommended')
  errors << 'Dependency Dashboard must remain enabled' unless config['dependencyDashboard'] == true
  errors << 'Dependency updates must not automerge' unless config['automerge'] == false
  errors << 'Renovate concurrency must remain bounded' unless config['prConcurrentLimit'].is_a?(Integer) &&
    config['prConcurrentLimit'].between?(1, 10)
  errors << 'Renovate hourly PR volume must remain bounded' unless config['prHourlyLimit'].is_a?(Integer) &&
    config['prHourlyLimit'].between?(1, 5)

  rules = config.fetch('packageRules', [])
  return errors + ['Renovate packageRules must be an array'] unless rules.is_a?(Array)
  find_rule = lambda { |description| rules.find { |p| p.is_a?(Hash) && p['description'] == description } }
  majors = find_rule.call('Keep major upgrades individually approved')
  errors << 'Unapproved major updates bypass dashboard review' unless
    majors.is_a?(Hash) && majors['matchUpdateTypes'] == ['major'] && majors['dependencyDashboardApproval'] == true

  %w[npm nuget github-actions].each do |manager|
    errors << "Missing explicit #{manager} package update policy" unless
      rules.any? { |p| p.is_a?(Hash) && p.fetch('matchManagers', []).include?(manager) &&
        p['groupName'].is_a?(String) && !p['groupName'].empty? }
  end

  actions = find_rule.call('Update GitHub Actions pins together')
  errors << 'GitHub Actions upgrades need Owner dashboard approval before PR generation' unless
    actions.is_a?(Hash) && actions['matchManagers'] == ['github-actions'] &&
    actions['dependencyDashboardApproval'] == true && actions['enabled'] != false

  protected_actions = find_rule.call('Keep protected GitHub Actions on reviewed SHA allowlist')
  reviewed_actions = JSON.parse((ROOT / 'governance/github-actions-allowlist.json').read).fetch('actions').keys.sort
  errors << 'Protected Actions with exact SHA allowlist must migrate together, never by bot-only SHA replacement' unless
    protected_actions.is_a?(Hash) && protected_actions['matchManagers'] == ['github-actions'] &&
    protected_actions.fetch('matchPackageNames', []).sort == reviewed_actions &&
    protected_actions['enabled'] == false

  runner = find_rule.call('Hold Ubuntu hosted-runner major migration pending execution qualification')
  errors << 'Ubuntu runner upgrades need an executable, reviewed runner migration' unless
    runner.is_a?(Hash) && runner['matchManagers'] == ['github-actions'] &&
    runner['matchDatasources'] == ['github-runners'] &&
    runner['matchPackageNames'] == ['ubuntu'] && runner['allowedVersions'] == '/^24\.04$/'

  python_bootstrap = find_rule.call('Keep Actions Python bootstrap on reviewed 3.12')
  errors << 'Action Python bootstrap must not auto-upgrade to an untested version' unless
    python_bootstrap.is_a?(Hash) && python_bootstrap['matchManagers'] == ['github-actions'] &&
    python_bootstrap['matchPackageNames'] == ['python'] &&
    python_bootstrap['allowedVersions'] == '/^3\.12(\.|$)/'

  action_dotnet = find_rule.call('Keep Actions dotnet-sdk bootstrap aligned with global.json')
  expected_sdk = JSON.parse((ROOT / 'global.json').read).fetch('sdk').fetch('version')
  errors << 'Actions SDK bootstrap must stay aligned with the pinned repository toolchain' unless
    action_dotnet.is_a?(Hash) && action_dotnet['matchManagers'] == ['github-actions'] &&
    action_dotnet['matchPackageNames'] == ['dotnet-sdk'] &&
    action_dotnet['allowedVersions'] == expected_sdk

  sdk = find_rule.call('Require dashboard approval for SDK major migration proposals')
  errors << 'SDK majors must remain discoverable and owner-approved' unless sdk.is_a?(Hash) &&
    sdk['matchFileNames'] == ['global.json', '**/global.json'] && sdk['matchUpdateTypes'] == ['major'] &&
    sdk['dependencyDashboardApproval'] == true && sdk['enabled'] != false

  node = find_rule.call('Retain approved Node 24 LTS for container images')
  errors << 'Docker Node LTS major boundary is missing or broadened' unless node.is_a?(Hash) &&
    node['matchDatasources'] == ['docker'] && node['matchPackageNames'] == ['node'] &&
    node['allowedVersions'] == '/^24([.-]|$)/'
  playwright = find_rule.call('Playwright Docker image must match the root Playwright lockfile')
  errors << 'Playwright image cannot update independently from its npm lockfile' unless
    playwright.is_a?(Hash) && playwright['enabled'] == false &&
    playwright.fetch('matchPackageNames', []).include?('mcr.microsoft.com/playwright')

  npm_cli = find_rule.call('Keep coordinated npm CLI updates out of routine dependency PRs')
  errors << 'npm CLI needs coordinated Docker/CI toolchain migration, not a Renovate manifest-only PR' unless
    npm_cli.is_a?(Hash) && npm_cli['matchManagers'] == ['npm'] &&
    npm_cli['matchPackageNames'] == ['npm'] && npm_cli['enabled'] == false

  gantt = find_rule.call('Hold Gantt major migrations until install-script review')
  errors << 'Gantt v35 must not drop the reviewed v34 install-script approval via Renovate' unless
    gantt.is_a?(Hash) && gantt['matchManagers'] == ['npm'] &&
    gantt['matchPackageNames'] == ['@syncfusion/ej2-angular-gantt'] &&
    gantt['allowedVersions'] == '/^34\./'

  alerts = config.fetch('vulnerabilityAlerts', {})
  errors << 'Renovate vulnerability alert remediation must remain enabled' unless
    alerts.is_a?(Hash) && alerts.fetch('labels', []).include?('security') &&
    alerts['prConcurrentLimit'].is_a?(Integer) && alerts['prConcurrentLimit'].positive?

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
# Node LTS major. Renovate digest updates within that major remain allowed.
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


config_path = ROOT / '.github/renovate.json'
abort('Self-hosted Renovate workflow must be absent for Mend-hosted operation') if
  (ROOT / '.github/workflows/renovate-updates.yml').exist?
abort('Duplicate root Renovate config must not shadow the audited config') if
  (ROOT / 'renovate.json').exist? || (ROOT / 'renovate.json5').exist?
abort('Legacy Dependabot version-update config must be removed') if (ROOT / '.github/dependabot.yml').exist?
config = JSON.parse(config_path.read)
syncfusion_allow_scripts = JSON.parse((ROOT / 'frontend/package.json').read).fetch('allowScripts', {})
abort('Current reviewed Syncfusion v34 install script must remain exact-version approved') unless
  syncfusion_allow_scripts['@syncfusion/ej2-angular-base@34.2.8'] == true
abort('Future Syncfusion base install scripts must remain denied unless separately approved') unless
  syncfusion_allow_scripts['@syncfusion/ej2-angular-base'] == false &&
  syncfusion_allow_scripts.keys.none? { |key| key.start_with?('@syncfusion/ej2-angular-base@35.') }

global_json = JSON.parse((ROOT / 'global.json').read)
sdk = global_json.fetch('sdk', {})
sdk_version = sdk['version']
abort('SDK must be pinned to stable .NET 10') unless sdk_version.is_a?(String) &&
  sdk_version.match?(/\A10\.0\.\d{3}\z/) &&
  sdk['rollForward'] == 'disable' && sdk['allowPrerelease'] == false

errors = update_errors(config) + image_errors({}, sdk_version) + node_lts_image_errors({})
abort(errors.join("\n")) unless errors.empty?

mutations = {
  'drop reviewed managers' => ->(x) { x['extends'] = [] },
  'disable dashboard' => ->(x) { x['dependencyDashboard'] = false },
  'enable automerge' => ->(x) { x['automerge'] = true },
  'unbounded PRs' => ->(x) { x['prConcurrentLimit'] = 0 },
  'bypass major approval' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Keep major upgrades individually approved' }['dependencyDashboardApproval'] = false },
  'disable SDK proposals' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Require dashboard approval for SDK major migration proposals' }['enabled'] = false },
  'unreviewed Node 26' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Retain approved Node 24 LTS for container images' }['allowedVersions'] = '/^26/' },
  'unreviewed Playwright image' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Playwright Docker image must match the root Playwright lockfile' }['enabled'] = true },
  'disable security alert PRs' => ->(x) { x['vulnerabilityAlerts']['prConcurrentLimit'] = 0 },
  'bypass reviewed Actions SHA gate' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Update GitHub Actions pins together' }['dependencyDashboardApproval'] = false },
  'unsanctioned npm CLI proposal' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Keep coordinated npm CLI updates out of routine dependency PRs' }['enabled'] = true },
  'unreviewed Gantt major proposal' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Hold Gantt major migrations until install-script review' }['allowedVersions'] = '/^35\\./' },
  'allow bot-only protected Action bump' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Keep protected GitHub Actions on reviewed SHA allowlist' }['enabled'] = true },
  'omit one protected Action from manual-review inventory' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Keep protected GitHub Actions on reviewed SHA allowlist' }['matchPackageNames'].pop },
  'unqualified Ubuntu runner major' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Hold Ubuntu hosted-runner major migration pending execution qualification' }['allowedVersions'] = '26.04' },
  'unqualified Python bootstrap version' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Keep Actions Python bootstrap on reviewed 3.12' }['allowedVersions'] = '3.15' },
  'Actions SDK drifts from global.json' => ->(x) { x['packageRules'].find { |p| p['description'] == 'Keep Actions dotnet-sdk bootstrap aligned with global.json' }['allowedVersions'] = '10.0.999' }
}
mutations.each do |name, change|
  copy = JSON.parse(JSON.generate(config))
  change.call(copy)
  abort("Guard missed negative Renovate mutation: #{name}") if update_errors(copy).empty?
end
bad_sdk = { 'Dockerfile' => (ROOT / 'Dockerfile').read.sub(
  /mcr\.microsoft\.com\/dotnet\/sdk:#{Regexp.escape(sdk_version)}/, 'mcr.microsoft.com/dotnet/sdk:10.0.999'
) }
abort('Guard missed SDK image/version mismatch') if image_errors(bad_sdk, sdk_version).empty?

bad_node_major = {
  'Dockerfile' => (ROOT / 'Dockerfile').read.sub(/FROM node:24@sha256:/, 'FROM node:26@sha256:')
}
abort('Guard missed unreviewed Node major') if node_lts_image_errors(bad_node_major).empty?
bad_node_digest = {
  'infra/docker/frontend-dev.Dockerfile' => (ROOT / 'infra/docker/frontend-dev.Dockerfile').read.sub(/@sha256:[a-f0-9]{64}/, '')
}
abort('Guard missed unpinned Node LTS image') if node_lts_image_errors(bad_node_digest).empty?

puts "Mend-hosted Renovate policy, SDK/image parity and Node LTS: PASS (#{mutations.length + 3} negative controls)"
