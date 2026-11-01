import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { test } from 'node:test';
import { createHash } from 'node:crypto';
import { Actor } from '../../scripts/ci/synthetic-logical-recovery-probe.mjs';
import { fileURLToPath } from 'node:url';
import { assertDisjoint, assertSameSnapshot, awaitOutboxIdle, beneath, corruptCopy,
  hostedContract, resourceNames, validateComposition, validateTar } from '../../scripts/ci/run-synthetic-logical-recovery.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
const image = 'sha256:' + 'a'.repeat(64), project = 'coglatas-recovery-123-1-0123456789ab-target';
const validHost = () => ({ GITHUB_ACTIONS: 'true', GITHUB_REPOSITORY: 'NYGsatoshi/Coglatas',
  GITHUB_REPOSITORY_ID: '1261244608', GITHUB_ACTOR: 'NYGsatoshi', GITHUB_EVENT_NAME: 'workflow_dispatch',
  COGLATAS_LOGICAL_RECOVERY_SELECTED: '1', GITHUB_RUN_ATTEMPT: '1', GITHUB_RUN_ID: '123', GITHUB_SHA: 'b'.repeat(40),
  GITHUB_WORKFLOW_REF: 'NYGsatoshi/Coglatas/.github/workflows/licensed-real-backend-acceptance.yml@refs/heads/test/recovery' });
const configuration = () => ({
  services: { app: { image, environment: {
    ASPNETCORE_ENVIRONMENT: 'Test', Tenancy__AppMode: 'SaaS', Tenancy__TenantResolutionStrategy: 'HeaderForDevelopmentOnly',
    Tenancy__AllowDevelopmentHeaderTenantResolution: 'true', Security__EnableCsrfProtection: 'true',
    Tenancy__SeedOnStartup: 'false', UiShell__SeedOnStartup: 'false', COGLATAS_SEED_ADMIN_ENABLED: 'false',
    COGLATAS_BROWSER_SMOKE_SEED_ENABLED: 'false', COGLATAS_DEMO_DATASET_ENABLED: 'false',
    SecurityCiFixture__Enabled: 'false', LocalAdmin__SeedOnStartup: 'false',
  }, volumes: [{ type: 'volume', source: 'uploads', target: '/app/storage/uploads' },
    { type: 'volume', source: 'protection_keys', target: '/app/data/protection-keys' }] },
  postgres: { image: 'sha256:' + 'c'.repeat(64), volumes: [{ type: 'volume', source: 'pgdata', target: '/var/lib/postgresql' }] } },
  networks: { default: { name: project + '_default', internal: true } },
  volumes: Object.fromEntries(['pgdata', 'uploads', 'protection_keys'].map(key => [key, { name: project + '_' + key }])),
});
const options = { target: true, image, project };

test('only the exact first-attempt owner-dispatched Linux workflow is admitted', () => {
  assert.deepEqual(hostedContract(validHost(), 'linux', []), { runId: '123', attempt: 1, sourceSha: 'b'.repeat(40) });
  for (const mutation of [{ GITHUB_ACTIONS: 'false' }, { GITHUB_REPOSITORY_ID: '0' }, { GITHUB_ACTOR: 'another-user' },
    { GITHUB_EVENT_NAME: 'pull_request' }, { GITHUB_RUN_ATTEMPT: '2' }, { COGLATAS_LOGICAL_RECOVERY_SELECTED: '0' },
    { GITHUB_WORKFLOW_REF: 'other/repo/.github/workflows/licensed-real-backend-acceptance.yml@refs/heads/main' }])
    assert.throws(() => hostedContract({ ...validHost(), ...mutation }, 'linux', []), { code: 'HOST_CONTRACT' });
  assert.throws(() => hostedContract(validHost(), 'win32', []), { code: 'HOST_CONTRACT' });
  assert.throws(() => hostedContract(validHost(), 'linux', ['--host=example.test']), { code: 'HOST_CONTRACT' });
});

test('remote Docker endpoints, contexts and TLS credentials are rejected before commands', () => {
  for (const mutation of [{ DOCKER_HOST: 'ssh://example.test' }, { DOCKER_HOST: 'tcp://127.0.0.1:2375' },
    { DOCKER_CONTEXT: 'production' }, { DOCKER_TLS_VERIFY: '1' }, { DOCKER_CERT_PATH: '/private/keys' }])
    assert.throws(() => hostedContract({ ...validHost(), ...mutation }, 'linux', []), { code: 'LOCAL_DOCKER' });
  hostedContract({ ...validHost(), DOCKER_HOST: 'unix:///var/run/docker.sock', DOCKER_CONTEXT: 'default' }, 'linux', []);
});

test('default-off CLI produces only a closed error without attempting recovery', () => {
  const secret = 'SYNTHETIC_PRIVATE_VALUE_MUST_NOT_APPEAR';
  const result = spawnSync(process.execPath, [resolve(root, 'scripts/ci/run-synthetic-logical-recovery.mjs')], {
    encoding: 'utf8', env: { GITHUB_ACTIONS: 'false', SYNCFUSION_LICENSE: secret, DOCKER_HOST: 'ssh://' + secret },
  });
  assert.equal(result.status, 1); assert.equal(result.stdout, '');
  assert.equal(result.stderr, 'Synthetic logical recovery rejected before qualification.\n');
  assert.ok(!result.stderr.includes(secret));
});

test('source, target and fault names are unique closed per-run identities', () => {
  const names = resourceNames('123', '0123456789ab');
  assert.equal(new Set(Object.values(names)).size, 3);
  assert.ok(Object.values(names).every(name => name.startsWith('coglatas-recovery-123-1-0123456789ab-')));
  for (const run of ['../deploy', '0', '001', '123\n']) assert.throws(() => resourceNames(run, '0123456789ab'));
  assert.throws(() => resourceNames('123', 'caller-selected-project'));
});

test('private output paths cannot escape their explicitly owned root', () => {
  const folder = resolve(root, 'artifacts', 'synthetic-example');
  assert.equal(beneath(folder, resolve(folder, 'database.dump')), resolve(folder, 'database.dump'));
  assert.throws(() => beneath(folder, folder), { code: 'PRIVATE_PATH' });
  assert.throws(() => beneath(folder, resolve(folder, '..', 'outside')), { code: 'PRIVATE_PATH' });
});

test('a restore target contains only the same-image app and PostgreSQL with volume mounts', () => {
  assert.equal(validateComposition(configuration(), options), true);
  for (const mutate of [config => { config.services.migrate = {}; }, config => { config.services.app.build = {}; },
    config => { config.services.app.image = 'some-floating-tag'; },
    config => { config.services.app.volumes[0] = { type: 'bind', source: '/srv/deploy', target: '/app/storage/uploads' }; }]) {
    const config = configuration(); mutate(config); assert.throws(() => validateComposition(config, options));
  }
});

test('production mode and every restored seed/bootstrap path are rejected', () => {
  for (const [key, value] of [['ASPNETCORE_ENVIRONMENT', 'Production'], ['Tenancy__AppMode', 'OnPremSingleTenant'],
    ['SecurityCiFixture__Enabled', 'true'], ['LocalAdmin__SeedOnStartup', 'true'], ['COGLATAS_SEED_ADMIN_ENABLED', 'true'],
    ['COGLATAS_BROWSER_SMOKE_SEED_ENABLED', 'true'], ['COGLATAS_DEMO_DATASET_ENABLED', 'true'],
    ['Tenancy__SeedOnStartup', 'true'], ['UiShell__SeedOnStartup', 'true'], ['BootstrapAdmin__Email', 'synthetic@example.test']]) {
    const config = configuration(); config.services.app.environment[key] = value;
    assert.throws(() => validateComposition(config, options), key);
  }
});

test('source configuration admits only its three executing services and the repository build', () => {
  const config = configuration();
  config.services.app.build = { context: resolve(root), dockerfile: 'Dockerfile' };
  config.services.migrate = { image };
  const sourceOptions = { ...options, target: false };
  assert.equal(validateComposition(config, sourceOptions), true);
  for (const mutate of [value => { value.services.browser = { network_mode: 'host' }; },
    value => { value.services.app.build.context = resolve(root, '..'); },
    value => { value.services.migrate.privileged = true; },
    value => { value.services.app.environment.SecurityCiFixture__Enabled = 'unexpected'; }]) {
    const actual = structuredClone(config); mutate(actual);
    assert.throws(() => validateComposition(actual, sourceOptions));
  }
});

test('public ports, host/shared namespaces, external volumes and routed networks are rejected', () => {
  for (const mutate of [config => { config.services.app.ports = [{ target: 8080, published: '8080', host_ip: '0.0.0.0' }]; },
    config => { config.services.app.network_mode = 'host'; }, config => { config.services.app.pid = 'host'; },
    config => { config.services.postgres.privileged = true; }, config => { config.volumes.pgdata.external = true; },
    config => { config.volumes.pgdata.name = 'deploy_pgdata'; }, config => { config.volumes.pgdata.driver_opts = { device: '/srv/live' }; },
    config => { config.networks.default.internal = false; }, config => { config.networks.default.name = 'deploy_default'; },
    config => { config.networks.default.driver_opts = { 'com.docker.network.bridge.gateway_mode_ipv4': 'routed' }; }]) {
    const config = configuration(); mutate(config); assert.throws(() => validateComposition(config, options));
  }
});

test('source and target cannot alias a database, upload or key volume', () => {
  assertDisjoint(['source-files', 'source-keys', 'source-db'], ['target-files', 'target-keys', 'target-db']);
  for (const target of [['target-files', 'source-keys', 'target-db'], ['target-files', 'target-files', 'target-db'], []])
    assert.throws(() => assertDisjoint(['source-files', 'source-keys', 'source-db'], target), { code: 'VOLUME_ALIAS' });
});

test('Outbox drain is a bounded readiness observation and never changes its rows', async () => {
  const samples = ['2', '1', '0']; let reads = 0, pauses = 0;
  await awaitOutboxIdle(() => { reads++; return samples.shift(); }, async () => { pauses++; });
  assert.equal(reads, 3); assert.equal(pauses, 2);
  reads = 0;
  await assert.rejects(awaitOutboxIdle(() => { reads++; return '1'; }, async () => {}), { code: 'OUTBOX_NOT_IDLE' });
  assert.equal(reads, 30);
  await assert.rejects(awaitOutboxIdle(() => 'unknown', async () => {}), { code: 'OUTBOX_SCHEMA' });
});

test('row/schema/catalog/migration/key/file/sequence changes cannot pass equal counts', () => {
  const checkpoint = { schema: 'raw-schema', rows: [['public', 'audit_logs', 1, 'original-row']],
    catalog: { constraints: ['original-check'], migrations: ['migration-1', 'migration-2'] },
    uploads: [['file', 3, 'original-file']], keys: [['key', 'original-key']], sequences: [['sequence', 1, false]] };
  assertSameSnapshot(checkpoint, structuredClone(checkpoint));
  for (const mutate of [data => { data.schema = 'different-schema'; }, data => { data.rows[0][3] = 'altered-row'; },
    data => { data.catalog.constraints[0] = 'weakened-check'; }, data => { data.catalog.migrations.reverse(); },
    data => { data.uploads[0][2] = 'altered-file'; }, data => { data.keys[0][1] = 'different-key'; },
    data => { data.sequences[0][2] = true; }]) {
    const actual = structuredClone(checkpoint); mutate(actual);
    assert.throws(() => assertSameSnapshot(checkpoint, actual), { code: 'STRICT_SNAPSHOT' });
  }
});

test('failure injection creates a distinct truncated copy and leaves the original untouched', () => {
  const original = Buffer.concat([Buffer.from('PGDMP'), Buffer.alloc(1024, 7)]), before = Buffer.from(original);
  const corrupt = corruptCopy(original);
  assert.equal(corrupt.length, 128); assert.deepEqual(original, before);
  corrupt[0] = 0; assert.deepEqual(original, before);
  assert.throws(() => corruptCopy(Buffer.alloc(1024)), { code: 'DUMP_FORMAT' });
});

function tar(name, type = '0', content = Buffer.from('synthetic')) {
  const header = Buffer.alloc(512);
  header.write(name); header.write('0000600\0', 100); header.write('0000000\0', 108); header.write('0000000\0', 116);
  header.write(content.length.toString(8).padStart(11, '0') + '\0', 124);
  header.write('00000000000\0', 136); header.fill(32, 148, 156); header.write(type, 156);
  const sum = [...header].reduce((a, b) => a + b, 0);
  header.write(sum.toString(8).padStart(6, '0') + '\0 ', 148);
  return Buffer.concat([header, content, Buffer.alloc((512 - content.length % 512) % 512), Buffer.alloc(1024)]);
}

test('tar validation admits regular original bytes but rejects traversal, links, devices and corruption', () => {
  assert.deepEqual(validateTar(tar('./file.txt')), { files: 1, expanded: 9 });
  for (const name of ['../outside', '/outside', './directory/../../outside', 'directory\\outside'])
    assert.throws(() => validateTar(tar(name)), { code: 'TAR_PATH' });
  for (const type of ['1', '2', '3', '4', '6', 'L', 'x']) assert.throws(() => validateTar(tar('./file', type)));
  const invalid = tar('./file'); invalid[1] ^= 1; assert.throws(() => validateTar(invalid));
  assert.throws(() => validateTar(tar('./file').subarray(0, 512)));
});

test('private raw material cannot enter the explicit workflow upload boundary', () => {
  const workflow = readFileSync(resolve(root, '.github/workflows/licensed-real-backend-acceptance.yml'), 'utf8');
  assert.ok(workflow.includes('artifacts/synthetic-logical-recovery/*/receipt.json'));
  const upload = workflow.slice(workflow.indexOf('- name: Upload closed synthetic logical recovery receipt'),
    workflow.indexOf('- name: Run canonical FCI-04'));
  assert.ok(upload.includes('retention-days: 7'));
  for (const forbidden of ['database.dump', 'uploads.tar', 'keys.tar', 'RUNNER_TEMP', 'probe-checkpoint.json'])
    assert.ok(!upload.includes(forbidden));
});

test('the actual API probe rejects foreign file bytes and canaries even with a denial status', async () => {
  const bytes = Buffer.from('ORIGINAL_SYNTHETIC_PRIVATE_FILE_CANARY\n');
  const foreign = { tenant: 'security-beta', fileId: '11111111-1111-1111-1111-111111111111',
    fileBytes: bytes.length, fileSha256: createHash('sha256').update(bytes).digest('hex'),
    denialMarkers: ['private-file.txt', 'private/storage/key', 'foreign@example.test'] };
  const owner = new Actor(foreign), caller = new Actor({ tenant: 'security-alpha' });
  const originalFetch = globalThis.fetch;
  try {
    const requests = [];
    globalThis.fetch = async (url, options) => {
      requests.push({ url, tenant: options.headers['X-Tenant-Slug'], redirect: options.redirect });
      return new Response(bytes, { status: 200 });
    };
    const actualOwnedBytes = await owner.ownFile();
    assert.deepEqual(actualOwnedBytes, bytes);
    for (const status of [400, 403, 404]) {
      for (const body of [bytes, Buffer.concat([Buffer.from('denied: '), bytes]), bytes.toString('utf8').trim(),
        'denied private-file.txt', 'denied foreign@example.test']) {
        globalThis.fetch = async () => new Response(body, { status });
        await assert.rejects(() => caller.foreignFile(foreign, actualOwnedBytes));
      }
      globalThis.fetch = async () => new Response('{"error":"not_found"}', { status });
      await caller.foreignFile(foreign, actualOwnedBytes);
    }
    assert.deepEqual(requests, [{ url: 'http://127.0.0.1:8080/api/files/' + foreign.fileId + '/download',
      tenant: 'security-beta', redirect: 'manual' }]);
    await assert.rejects(() => caller.foreignFile(foreign, Buffer.from('wrong oracle')));
  } finally { globalThis.fetch = originalFetch; }
});
