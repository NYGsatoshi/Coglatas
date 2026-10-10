import test from 'node:test';
import assert from 'node:assert/strict';
import { resolve } from 'node:path';
import { mkdtemp, mkdir, rm, writeFile, readFile, symlink } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { execFile } from 'node:child_process';
import { promisify } from 'node:util';
import { runtimeArguments, insideDirectory, preparePrivateDirectory, postgresStorageArguments, verifyPostgresStorage } from './sec-arch-runtime-options.mjs';
// Keep producer identity controls in the existing runtime fixture verification lane.
import './sec-arch-runtime-assemblies.test.mjs';

test('Runtime private inventory is explicit and preserves common fixture arguments', () => {
  const options = runtimeArguments(['--candidate-sha', '1'.repeat(40), 'artifacts/sec-arch/runtime.json',
    '--private-inventory-directory', resolve('private-synthetic')]);
  assert.equal(options.candidateSha, '1'.repeat(40));
  assert.equal(options.privateDirectory, resolve('private-synthetic'));
  assert.equal(options.report, 'artifacts/sec-arch/runtime.json');
  assert.equal(runtimeArguments(['--candidate-sha', '1'.repeat(40)]).privateDirectory, undefined);
});

test('Missing relative duplicate or unrelated arguments cannot select private output', () => {
  for (const args of [ ['--private-inventory-directory'], ['--private-inventory-directory', 'relative'],
    ['--private-inventory-directory', resolve('one'), '--private-inventory-directory', resolve('two')],
    ['--unknown', resolve('one')], ['--private-inventory-directory', resolve('one') + '\0'] ])
    assert.throws(() => runtimeArguments(args));
});

test('Volatile PostgreSQL storage is explicit, bounded and cannot alter database settings', () => {
  assert.equal(runtimeArguments([]).postgresStorage, 'disk');
  assert.equal(runtimeArguments(['--postgres-storage', 'disk']).postgresStorage, 'disk');
  assert.equal(runtimeArguments(['--postgres-storage', 'tmpfs']).postgresStorage, 'tmpfs');
  assert.deepEqual(postgresStorageArguments('disk'), []);
  assert.deepEqual(postgresStorageArguments('tmpfs'),
    ['--tmpfs', '/var/lib/postgresql:rw,size=2147483648,mode=1777']);
  for (const args of [['--postgres-storage'], ['--postgres-storage', 'fsync-off'],
    ['--postgres-storage', 'tmpfs', '--postgres-storage', 'disk'],
    ['--postgres-storage', '/arbitrary/path']]) assert.throws(() => runtimeArguments(args));
  assert.throws(() => postgresStorageArguments('unbounded'));
});

test('Actual PostgreSQL storage needs both the Docker identity and filesystem type', () => {
  const volatile = { Mounts: [], HostConfig: { Tmpfs: { '/var/lib/postgresql': 'rw,size=2147483648,mode=1777' } } };
  const disk = { Mounts: [{ Destination: '/var/lib/postgresql', Type: 'volume' }], HostConfig: { Tmpfs: {} } };
  assert.equal(verifyPostgresStorage('tmpfs', volatile, 'tmpfs'), 'tmpfs');
  assert.equal(verifyPostgresStorage('disk', disk, 'ext2/ext3'), 'volume');
  for (const [mode, inspection, filesystem] of [
    ['tmpfs', volatile, 'ext2/ext3'], ['tmpfs', disk, 'tmpfs'], ['disk', disk, 'tmpfs'],
    ['disk', volatile, 'ext2/ext3'], ['tmpfs', { Mounts: [], HostConfig: { Tmpfs: { '/var/lib/postgresql': 'rw' } } }, 'tmpfs'],
    ['tmpfs', { Mounts: [], HostConfig: { Tmpfs: { '/unrelated': 'rw,size=2147483648,mode=1777' } } }, 'tmpfs'],
  ]) assert.throws(() => verifyPostgresStorage(mode, inspection, filesystem));
});

test('Resolved checkout descendants and lexical siblings have distinct disclosure scope', () => {
  const root = resolve('synthetic-public');
  assert.equal(insideDirectory(root, root), true);
  assert.equal(insideDirectory(root, resolve(root, 'artifacts')), true);
  assert.equal(insideDirectory(root, resolve(root, '..', 'synthetic-public-other')), false);
  assert.equal(insideDirectory(root, resolve(root, '..', 'private')), false);
});

test('Private evidence requires an exclusive directory outside actual Git checkouts and aliases', async () => {
  const temporary = await mkdtemp(resolve(tmpdir(), 'coglatas-runtime-private-'));
  const publicRoot = resolve(temporary, 'public');
  const execute = promisify(execFile);
  const inspect = async parent => {
    try { await execute('git', ['-C', parent, 'rev-parse', '--show-toplevel']); return { code: 0 }; }
    catch (error) { return { code: error.code }; }
  };
  try {
    await mkdir(publicRoot);
    await execute('git', ['init', '--quiet', publicRoot]);
    await assert.rejects(preparePrivateDirectory(publicRoot, resolve(publicRoot, 'private'), inspect));
    const sibling = resolve(temporary, 'other-public');
    await mkdir(sibling);
    await execute('git', ['init', '--quiet', sibling]);
    await assert.rejects(preparePrivateDirectory(publicRoot, resolve(sibling, 'private'), inspect));
    const alias = resolve(temporary, 'public-alias');
    await symlink(publicRoot, alias, process.platform === 'win32' ? 'junction' : 'dir');
    await assert.rejects(preparePrivateDirectory(publicRoot, resolve(alias, 'private'), inspect));
    const privateRoot = await preparePrivateDirectory(publicRoot, resolve(temporary, 'private'), inspect);
    const receipt = resolve(privateRoot, 'historical.json');
    await writeFile(receipt, 'preserve historical bytes', { flag: 'wx' });
    await assert.rejects(preparePrivateDirectory(publicRoot, privateRoot, inspect));
    assert.equal(await readFile(receipt, 'utf8'), 'preserve historical bytes');
    await assert.rejects(preparePrivateDirectory(publicRoot, resolve(temporary, 'indeterminate'),
      async () => ({ code: 1 })));
  } finally {
    assert.ok(temporary.startsWith(resolve(tmpdir(), 'coglatas-runtime-private-')));
    await rm(temporary, { recursive: true, force: true });
  }
});
