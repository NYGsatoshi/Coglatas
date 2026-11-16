import assert from 'node:assert/strict';
import { test } from 'node:test';
import { spawnSync } from 'node:child_process';
import { portableCatalog } from '../../scripts/ci/synthetic-portable-recovery-catalog.mjs';

const fields = ['relations', 'constraints', 'indexes', 'triggers',
  'functions', 'policies', 'schemas', 'defaults', 'roles', 'memberships', 'migrations'];
const empty = () => Object.fromEntries(fields.map(field => [field, []]));

await test('Main-compatible catalog retains all original categories and an empty trigger inventory', () => {
  const projected = portableCatalog(empty(), []);
  assert.deepEqual(Object.keys(projected.catalog), fields);
  assert.equal(projected.version, 1);
  assert.equal(projected.catalog.triggers.version, 1);
  assert.equal(projected.catalog.triggers.rowCount, 0);
  assert.deepEqual(projected.catalog.triggers.firingOrders, []);
});

await test('catalog mutations to every non-trigger category remain evidence-bearing', () => {
  const original = portableCatalog(empty(), []);
  for (const field of fields.filter(value => value !== 'triggers')) {
    const changed = empty();
    changed[field] = [['changed-catalog-member']];
    assert.notDeepEqual(portableCatalog(changed, []), original);
  }
});

await test('unknown, missing, and malformed categories or incomplete trigger inventories fail closed', () => {
  const variants = [
    null, [], {}, { ...empty(), unknown: [] },
    { ...empty(), constraints: null }, { ...empty(), triggers: [['unexpected']] },
  ];
  for (const variant of variants) {
    assert.throws(() => portableCatalog(variant, []), /Private portable catalog rejected|Synthetic trigger inventory rejected/);
  }
});

await test('CLI uses the exact Main FK-slot planner without dropping archive elements', () => {
  const script = 'scripts/ci/synthetic-portable-recovery-catalog.mjs';
  const native = Buffer.from('; preserved comment\n' +
    '1; 2615 2200 SCHEMA - public postgres\n' +
    '7; 2606 900 FK CONSTRAINT public earlier fk_a postgres\n' +
    '9; 2606 100 FK CONSTRAINT public later fk_z postgres\n');
  const planned = spawnSync(process.execPath, [script, 'plan'], { input: native });
  assert.equal(planned.status, 0);
  const expected = Buffer.from('; preserved comment\n' +
    '1; 2615 2200 SCHEMA - public postgres\n' +
    '9; 2606 100 FK CONSTRAINT public later fk_z postgres\n' +
    '7; 2606 900 FK CONSTRAINT public earlier fk_a postgres\n');
  assert.deepEqual(planned.stdout, expected);
  const invalid = spawnSync(process.execPath, [script, 'plan'],
    { input: Buffer.from('invalid private archive content\n') });
  assert.notEqual(invalid.status, 0);
  assert.equal(invalid.stdout.length, 0);
  assert.ok(!invalid.stderr.toString().includes('private archive content'));
});

await test('CLI privately exposes only pinned SQL and rejects malformed input without echoing it', () => {
  const script = 'scripts/ci/synthetic-portable-recovery-catalog.mjs';
  const query = spawnSync(process.execPath, [script, 'queries'], { encoding: 'utf8' });
  assert.equal(query.status, 0);
  const sql = JSON.parse(query.stdout);
  assert.deepEqual(Object.keys(sql), ['catalog', 'triggers']);
  assert.ok(sql.catalog.includes('pg_get_indexdef'));
  assert.ok(sql.triggers.includes('pg_trigger'));
  const projected = spawnSync(process.execPath, [script, 'project'],
    { encoding: 'utf8', input: JSON.stringify({ catalog: empty(), triggers: [] }) });
  assert.equal(projected.status, 0);
  assert.deepEqual(JSON.parse(projected.stdout), portableCatalog(empty(), []));
  const canary = 'private-canary-do-not-log';
  const rejected = spawnSync(process.execPath, [script, 'project'],
    { encoding: 'utf8', input: JSON.stringify({ catalog: canary, triggers: [] }) });
  assert.notEqual(rejected.status, 0);
  assert.ok(!rejected.stderr.includes(canary));
  assert.equal(rejected.stdout, '');
});
