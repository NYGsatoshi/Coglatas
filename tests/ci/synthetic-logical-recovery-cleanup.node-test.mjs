import assert from 'node:assert/strict';
import { test } from 'node:test';
import { cleanupOwnedProjects, resourceNames } from '../../scripts/ci/run-synthetic-logical-recovery.mjs';

const names = resourceNames('123', '0123456789ab');
const listing = project => ['ps', '-aq', '--filter', 'label=com.docker.compose.project=' + project];
const output = value => ({ stdout: Buffer.from(value) });

function fixture(records, { residual = {}, fail } = {}) {
  const calls = [], removed = new Set();
  const invoke = (operation, args) => { calls.push([operation, ...args]); fail?.(operation, args); };
  return {
    calls,
    docker(args) {
      invoke('docker', args);
      if (args[0] === 'rm') { assert.deepEqual(args.slice(0, 2), ['rm', '--force']); return output(''); }
      const project = args.at(-1).slice('label=com.docker.compose.project='.length);
      if (args[0] === 'ps') {
        assert.deepEqual(args, listing(project));
        return output(removed.has(project) ? residual.container ?? '' :
          records.filter(record => record.listedProject === project).map(record => record.id).join('\n'));
      }
      assert.ok(['volume', 'network'].includes(args[0]));
      assert.deepEqual(args.slice(1), ['ls', '-q', '--filter', 'label=com.docker.compose.project=' + project]);
      assert.ok(removed.has(project));
      return output(residual[args[0]] ?? '');
    },
    inspect(id) {
      invoke('inspect', [id]);
      const record = records.find(record => record.id === id);
      assert.ok(record);
      return { Config: { Labels: { 'com.docker.compose.project': record.project,
        'com.docker.compose.service': record.service } } };
    },
    compose(project, args) {
      invoke('compose', [project, ...args]);
      assert.deepEqual(args, ['down', '--volumes', '--remove-orphans']);
      removed.add(project); return output('');
    },
  };
}

const owned = (service, project = names.source) => ({ id: service + '-container', service,
  project, listedProject: project });

await test('actual cleanup removes only the owned source helper before Compose and checks every resource kind', () => {
  const io = fixture(['postgres', 'app', 'migrate', 'mechanism-helper'].map(service => owned(service)));
  cleanupOwnedProjects(new Set([names.source]), names.source, io);
  assert.deepEqual(io.calls, [
    ['docker', ...listing(names.source)],
    ...['postgres', 'app', 'migrate', 'mechanism-helper'].map(service => ['inspect', service + '-container']),
    ['docker', 'rm', '--force', 'mechanism-helper-container'],
    ['compose', names.source, 'down', '--volumes', '--remove-orphans'],
    ['docker', ...listing(names.source)],
    ['docker', 'volume', 'ls', '-q', '--filter', 'label=com.docker.compose.project=' + names.source],
    ['docker', 'network', 'ls', '-q', '--filter', 'label=com.docker.compose.project=' + names.source],
  ]);
});

await test('created target and fault projects use only their own inspected services and Compose cleanup', () => {
  const io = fixture([owned('app', names.target), owned('postgres', names.fault)]);
  cleanupOwnedProjects(new Set([names.target, names.fault]), names.source, io);
  assert.deepEqual(io.calls.filter(call => call[0] === 'compose'),
    [names.target, names.fault].map(project => ['compose', project, 'down', '--volumes', '--remove-orphans']));
  assert.ok(!io.calls.some(call => call[0] === 'docker' && call[1] === 'rm'));
});

await test('foreign project, unrecognized service and helper outside source cannot authorize deletion', () => {
  for (const record of [
    { ...owned('app'), project: 'foreign-project' },
    { ...owned('unexpected') },
    owned('mechanism-helper', names.target),
  ]) {
    const io = fixture([record]);
    assert.throws(() => cleanupOwnedProjects(new Set([record.listedProject]), names.source, io), { code: 'CLEANUP_OWNER' });
    assert.ok(!io.calls.some(call => call[0] === 'compose' || call[1] === 'rm'));
  }
});

await test('a foreign container discovered after an owned helper still cannot reach Compose or foreign deletion', () => {
  const foreign = { ...owned('app'), project: 'foreign-project' };
  const io = fixture([owned('mechanism-helper'), foreign]);
  assert.throws(() => cleanupOwnedProjects(new Set([names.source]), names.source, io), { code: 'CLEANUP_OWNER' });
  assert.deepEqual(io.calls.filter(call => call[1] === 'rm'), [['docker', 'rm', '--force', 'mechanism-helper-container']]);
  assert.ok(!io.calls.some(call => call[0] === 'compose'));
});

await test('any residual container, volume or network is a failed cleanup rather than success', () => {
  for (const kind of ['container', 'volume', 'network']) {
    const io = fixture([], { residual: { [kind]: 'owned-resource-remains\n' } });
    assert.throws(() => cleanupOwnedProjects(new Set([names.source]), names.source, io), { code: 'CLEANUP_REMAINS' });
    assert.equal(io.calls.filter(call => call[0] === 'compose').length, 1);
  }
});

await test('the first Docker, inspection or Compose error propagates without retry or later-project deletion', () => {
  for (const stage of ['list', 'inspect', 'remove-helper', 'compose', 'volume', 'network']) {
    const error = new Error('Synthetic cleanup I/O failure'); let failures = 0;
    const io = fixture([owned('mechanism-helper')], { fail(operation, args) {
      const expected = { list: 'ps', inspect: 'inspect', 'remove-helper': 'rm', compose: 'compose', volume: 'volume', network: 'network' }[stage];
      if ((operation === 'docker' ? args[0] : operation) === expected) { failures++; throw error; }
    } });
    assert.throws(() => cleanupOwnedProjects(new Set([names.source, names.target]), names.source, io), caught => caught === error);
    assert.equal(failures, 1);
    assert.ok(!JSON.stringify(io.calls).includes(names.target));
  }
});
