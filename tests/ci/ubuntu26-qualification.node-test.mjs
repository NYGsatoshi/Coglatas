import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import * as yaml from 'js-yaml';

const WORKFLOW = '.github/workflows/ubuntu26-qualification.yml';
const RUNNER = '${{ (matrix.os == 'ubuntu-26.04' && 'ubuntu-26.04') || 'ubuntu-24.04' }}';
const MATRIX = ['ubuntu-24.04', 'ubuntu-26.04'];
const PATHS = [WORKFLOW, 'governance/workflow-trust-policy.json',
  'tests/ci/ubuntu26-qualification.node-test.mjs', 'docs/ci/ubuntu26-qualification.md'];
const COMMANDS = {
  npm_toolchain: 'npm install --global npm@11.17.0 --no-audit --no-fund --ignore-scripts',
  root_dependencies: 'npm ci --ignore-scripts --no-audit --no-fund',
  contract: 'node scripts/ci/os-portability-contract.mjs --runtime',
  frontend_dependencies: 'npm --prefix frontend ci --ignore-scripts --no-audit --no-fund',
  dotnet_restore: 'dotnet restore Coglatas.slnx',
  dotnet_build: 'dotnet build Coglatas.slnx --configuration Release --no-restore',
  dotnet_tests: '--filter "Portability=CrossPlatform"',
  dotnet_results: 'node scripts/ci/verify-os-portability-results.mjs',
  frontend_build: 'npm --prefix frontend run build',
  frontend_tests: 'npm --prefix frontend test',
  frontend_helpers: 'npm --prefix frontend run test:architecture',
  compat_critical: 'node scripts/ci/verify-compat-critical.mjs --profile os-portability',
  docker_smoke: 'docker compose version'
};

async function inputs() {
  const [workflow, registry, allowlist] = await Promise.all([
    readFile(WORKFLOW, 'utf8').then(yaml.load),
    readFile('governance/workflow-trust-policy.json', 'utf8').then(JSON.parse),
    readFile('governance/github-actions-allowlist.json', 'utf8').then(JSON.parse)
  ]);
  return { workflow, registry, allowlist };
}

function errorsFor(w, registry, allowlist) {
  const errors = [];
  const check = (condition, message) => { if (!condition) errors.push(message); };
  const same = (a, b) => JSON.stringify(a) === JSON.stringify(b);
  check(same(w.permissions, { contents: 'read' }), 'read-only permissions');
  check(same(Object.keys(w.jobs ?? {}), ['qualification']), 'one qualification job');
  check(same(w.on?.pull_request?.branches, ['main']), 'only main PR targets');
  check(same(w.on?.pull_request?.paths, PATHS), 'bounded PR paths');
  check(Object.hasOwn(w.on ?? {}, 'workflow_dispatch'), 'manual dispatch');
  check(!Object.hasOwn(w.on ?? {}, 'pull_request_target'), 'no privileged triggers');

  const job = w.jobs?.qualification ?? {};
  check(same(job.strategy?.matrix?.os, MATRIX), 'exact OS comparison');
  check(same(Object.keys(job.strategy?.matrix ?? {}), ['os']), 'no extra matrix dimensions');
  check(job.strategy?.['fail-fast'] === false, 'both legs report');
  check(job.strategy?.['max-parallel'] === 2, 'parallelism bounded');
  check(job['runs-on'] === RUNNER, 'bounded runner selector');
  check(job['timeout-minutes'] === 75, 'timeout bounded');
  check(!Object.hasOwn(job, 'continue-on-error'), 'fail-closed job');
  const routing = registry?.runner_routing ?? {};
  check(MATRIX.every((label) => routing.github_hosted_labels?.includes(label)), 'approved hosted labels');
  check(routing.approved_dynamic_expressions?.includes(RUNNER), 'approved bounded expression');

  const steps = job.steps ?? [];
  check(Array.isArray(steps), 'steps array');
  const ids = steps.filter((s) => s?.id).map((s) => s.id);
  check(new Set(ids).size === ids.length, 'unique step IDs');
  const byId = Object.fromEntries(steps.filter((s) => s?.id).map((s) => [s.id, s]));
  for (const s of steps) check(!Object.hasOwn(s, 'continue-on-error'), 'do not ignore step failures');
  for (const [id, expected] of Object.entries(COMMANDS)) {
    check(typeof byId[id]?.run === 'string' && byId[id].run.includes(expected), 'required command: ' + id);
  }
  check(byId.contract?.run?.includes('node --test tests/ci/ubuntu26-qualification.node-test.mjs'),
    'qualification tests must execute');
  check(byId.contract?.run?.includes('tests/ci/os-portability-contract.node-test.mjs'),
    'existing portability tests must execute');
  check(byId.dotnet_tests?.run?.includes('--logger "trx;LogFileName=os-portability.trx"'), 'TRX generated');
  check(byId.dotnet_results?.run?.includes('--trx artifacts/os-portability/os-portability.trx'), 'TRX checked');
  check(byId.checkout?.with?.['persist-credentials'] === false, 'checkout credentials disabled');
  check(byId.setup_dotnet?.with?.['global-json-file'] === 'global.json', 'pinned dotnet');
  check(String(byId.setup_node?.with?.['node-version']) === '24', 'pinned Node 24');
  const upload = steps.find((s) => s?.name === 'Upload immutable-run comparison evidence');
  const evidence = steps.find((s) => s?.name === 'Write sanitized per-runner evidence');
  for (const [s, action] of [
    [byId.checkout, 'actions/checkout'],
    [byId.setup_dotnet, 'actions/setup-dotnet'],
    [byId.setup_node, 'actions/setup-node'],
    [upload, 'actions/upload-artifact']
  ]) {
    const sha = allowlist.actions?.[action]?.sha;
    check(typeof sha === 'string' && s?.uses === action + '@' + sha, 'immutable action: ' + action);
  }
  check(evidence?.if === 'always()', 'failure evidence required');
  check(evidence?.env?.PORTABILITY_MATRIX_OS === '${{ matrix.os }}', 'exact matrix identity');
  check(evidence?.run?.includes('scripts/ci/write-os-portability-evidence.mjs'), 'sanitized evidence writer');
  check(upload?.if === 'always()', 'failure artifact upload required');
  check(upload?.with?.['if-no-files-found'] === 'error', 'missing evidence fails');
  check(upload?.with?.name === 'ubuntu26-qualification-${{ matrix.os }}-${{ github.run_attempt }}', 'OS-distinct run artifacts');
  return errors;
}

test('checked-in qualification gates are complete and bounded', async () => {
  const { workflow, registry, allowlist } = await inputs();
  assert.deepEqual(errorsFor(workflow, registry, allowlist), []);
});

test('mutation controls reject false positives and security regressions', async () => {
  const { workflow, registry, allowlist } = await inputs();
  const mutations = [
    (w) => { w.jobs.qualification.strategy.matrix.os.pop(); },
    (w) => { w.jobs.qualification['runs-on'] = 'unsafe-matrix-os'; },
    (w) => { w.jobs.qualification.strategy['fail-fast'] = true; },
    (w) => { w.jobs.qualification.steps.find((s) => s.id === 'frontend_tests')['continue-on-error'] = true; },
    (w) => { w.jobs.qualification.steps = w.jobs.qualification.steps.filter((s) => s.id !== 'dotnet_results'); },
    (w) => { w.jobs.qualification.steps.find((s) => s.id === 'checkout').with['persist-credentials'] = true; },
    (w) => { w.jobs.qualification.steps.find((s) => s.id === 'setup_node').uses = 'actions/setup-node@v7'; },
    (w) => { w.jobs.qualification.steps.find((s) => s.name === 'Write sanitized per-runner evidence').if = 'success()'; },
    (w) => { w.on.pull_request.paths = ['**']; },
    (w) => { w.permissions = { contents: 'write' }; }
  ];
  for (const [index, mutate] of mutations.entries()) {
    const w = structuredClone(workflow);
    mutate(w);
    assert.ok(errorsFor(w, registry, allowlist).length > 0, 'mutation ' + index);
  }
  const badLabel = structuredClone(registry);
  badLabel.runner_routing.github_hosted_labels =
    badLabel.runner_routing.github_hosted_labels.filter((x) => x !== 'ubuntu-26.04');
  assert.ok(errorsFor(workflow, badLabel, allowlist).length > 0);
  const badExpression = structuredClone(registry);
  badExpression.runner_routing.approved_dynamic_expressions = [];
  assert.ok(errorsFor(workflow, badExpression, allowlist).length > 0);
});
