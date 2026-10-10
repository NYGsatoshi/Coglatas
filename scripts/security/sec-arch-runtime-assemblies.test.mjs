import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, open, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { dirname, resolve } from 'node:path';
import { captureRuntimeAssemblyBinding, unverifiedRuntimeAssemblyBinding, runtimeAssemblyNames } from './sec-arch-runtime-assemblies.mjs';

const names = ['Coglatas.Tests', 'Coglatas.Web', 'Coglatas.Application', 'Coglatas.Infrastructure',
  'Coglatas.Domain', 'Coglatas.SecurityArchitecture'];
const canonicalPath = (root, name) => resolve(root,
  name === 'Coglatas.Tests' ? 'tests/Coglatas.Tests' : name === 'Coglatas.SecurityArchitecture' ?
    'tools/Coglatas.SecurityArchitecture' : `src/${name}`, `bin/Release/net10.0/${name}.dll`);
const copiedPath = (root, name) => resolve(root, `tests/Coglatas.Tests/bin/Release/net10.0/${name}.dll`);

async function withAssemblies(action) {
  const root = await mkdtemp(resolve(tmpdir(), 'coglatas-runtime-assemblies-'));
  try {
    for (const name of names) {
      for (const path of new Set([canonicalPath(root, name), copiedPath(root, name)])) {
        await mkdir(dirname(path), { recursive: true });
        await writeFile(path, 'synthetic compiled ' + name, { flag: 'wx' });
      }
    }
    await action(root);
  } finally {
    assert.ok(root.startsWith(resolve(tmpdir(), 'coglatas-runtime-assemblies-')));
    await rm(root, { recursive: true, force: true });
  }
}

test('Local runtime binding emits V2 with all six actual canonical and copied identities', async () => {
  await withAssemblies(async root => {
    const report = await captureRuntimeAssemblyBinding(root);
    assert.equal(report.schemaVersion, 2);
    assert.equal(report.verifierVersion, '2');
    assert.equal(report.assemblyBindingScope, 'SIX_ASSEMBLIES_WITH_LOADED_COPIES');
    assert.deepEqual(Object.keys(report.assemblyDigests), names);
    assert.deepEqual(runtimeAssemblyNames, names);
    for (const name of names)
      assert.equal(report.assemblyDigests[name], createHash('sha256').update('synthetic compiled ' + name).digest('hex'));
    assert.equal(report.fullDependencyQualification, 'SIX_ASSEMBLY_LOCAL_BYTES_RECONCILED');
    assert.equal(report.ownerApproval, undefined);
  });
});

test('Failure before complete capture preserves UNVERIFIED dependency metadata', () => {
  const report = unverifiedRuntimeAssemblyBinding();
  assert.equal(report.schemaVersion, 2);
  assert.equal(report.verifierVersion, '2');
  assert.deepEqual(report.assemblyDigests, {});
  assert.equal(report.fullDependencyQualification, 'UNVERIFIED');
});

test('Changed copied verifier cannot reuse five unchanged product hashes', async () => {
  await withAssemblies(async root => {
    await writeFile(copiedPath(root, 'Coglatas.SecurityArchitecture'), 'different actual copied verifier');
    await assert.rejects(captureRuntimeAssemblyBinding(root), /Loaded dependency copy differs/);
  });
});

test('Changed Tool producer cannot agree with a retained old copied verifier', async () => {
  await withAssemblies(async root => {
    await writeFile(canonicalPath(root, 'Coglatas.SecurityArchitecture'), 'different Tool producer');
    await assert.rejects(captureRuntimeAssemblyBinding(root), /Loaded dependency copy differs/);
  });
});

test('Missing Tool producer or copied verifier fails instead of a partial five-assembly receipt', async () => {
  for (const select of [canonicalPath, copiedPath]) {
    await withAssemblies(async root => {
      await rm(select(root, 'Coglatas.SecurityArchitecture'));
      await assert.rejects(captureRuntimeAssemblyBinding(root));
    });
  }
});

test('Each changed product dependency copy is rejected independently', async () => {
  for (const name of ['Coglatas.Web', 'Coglatas.Application', 'Coglatas.Infrastructure', 'Coglatas.Domain']) {
    await withAssemblies(async root => {
      await writeFile(copiedPath(root, name), 'different loaded product');
      await assert.rejects(captureRuntimeAssemblyBinding(root), /Loaded dependency copy differs/);
    });
  }
});

test('Empty DLL identity cannot qualify a runtime assembly', async () => {
  await withAssemblies(async root => {
    await writeFile(copiedPath(root, 'Coglatas.SecurityArchitecture'), '');
    await assert.rejects(captureRuntimeAssemblyBinding(root), /Empty compiled assembly/);
  });
});

test('Actual oversized sparse DLL is rejected through the bounded stream reader', async () => {
  await withAssemblies(async root => {
    const source = await open(copiedPath(root, 'Coglatas.SecurityArchitecture'), 'r+');
    try { await source.truncate(32 * 1024 * 1024 + 1); }
    finally { await source.close(); }
    await assert.rejects(captureRuntimeAssemblyBinding(root), /Compiled assembly exceeds/);
  });
});

test('Assembly capture does not rewrite retained historical receipt bytes', async () => {
  await withAssemblies(async root => {
    const historical = resolve(root, 'historical-v1.json');
    const original = Buffer.from('{"schemaVersion":1,"verifierVersion":"1","historical":true}\n');
    await writeFile(historical, original, { flag: 'wx' });
    await captureRuntimeAssemblyBinding(root);
    assert.deepEqual(await readFile(historical), original);
  });
});
