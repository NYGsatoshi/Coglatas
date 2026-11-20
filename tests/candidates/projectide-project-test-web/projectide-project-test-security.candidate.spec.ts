/// <reference types="node" />
import { createHash, webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import type { Digest, PreviewBundle, PreviewCase } from './projectide.contracts';
import { localTestsMatchCase, readLocalTests } from './projectide-tests.reader';
import { readPreparedTestEdit } from './projectide-test-edit';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());

function bundle(asset = 'preview-v1.json'): PreviewBundle {
  return readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide', asset), 'utf8')));
}

function member(data: PreviewBundle, key: string): PreviewCase {
  const value = data.cases.find(item => item.key === key);
  if (!value) throw new Error('missing-original-Core-case: ' + key);
  return value;
}

function original(key = 'tests-pass', asset = 'preview-v1.json'): PreviewCase {
  return member(bundle(asset), key);
}

function hash(domain: string, text: string): Digest {
  return { algorithm: 'sha-256', value: createHash('sha256').update(domain + '\0' + text).digest('hex') };
}

function replaceRun(value: PreviewCase, run: unknown): void {
  const text = JSON.stringify(run);
  Object.assign(value.localProjectTests!, { result: { text, digest: hash('coglatas.project-test-run/1', text) } });
}

async function storeFor(key = 'tests-fail', asset = 'preview-v1.json'): Promise<ProjectIdeWorkbenchStore> {
  const store = new ProjectIdeWorkbenchStore();
  await store.load(async () => bundle(asset));
  store.choose(key);
  store.loadResult();
  expect(store.context()?.key).toBe(key);
  return store;
}

function holdNextDigest(): { entered: Promise<void>; release: () => void } {
  let release!: () => void;
  let markEntered!: () => void;
  const entered = new Promise<void>(resolveEntered => { markEntered = resolveEntered; });
  const blocked = new Promise<void>(resolveGate => { release = resolveGate; });
  const digest = webcrypto.subtle.digest.bind(webcrypto.subtle);
  vi.spyOn(webcrypto.subtle, 'digest').mockImplementationOnce(async (...arguments_) => {
    markEntered();
    await blocked;
    return digest(...arguments_);
  });
  return { entered, release };
}

describe('Independent Project Test original bytes and owned reports', () => {
  it('preserves genuine Core outcome axes including a Compiler pass with a Test failure', async () => {
    const expectations = [
      ['tests-pass', 'Completed', 'Complete', 'Pass', 'Current', 1],
      ['tests-fail', 'Completed', 'Complete', 'Fail', 'Current', 1],
      ['tests-unknown', 'Completed', 'Partial', 'Inconclusive', 'Current', 1],
      ['tests-empty', 'NotRun', 'None', 'NotRun', 'Current', 0],
      ['tests-cancelled', 'Cancelled', 'None', 'NotRun', 'Current', 0],
      ['tests-stale', 'NotRun', 'None', 'NotRun', 'Stale', 0],
      ['tests-evaluator-mismatch', 'NotRun', 'None', 'NotRun', 'Current', 0],
      ['tests-exhausted', 'ResourceExhausted', 'None', 'NotRun', 'Current', 0],
    ] as const;
    for (const [key, execution, coverage, verdict, freshness, executedAssertions] of expectations) {
      const value = original(key);
      const compilerBytes = JSON.stringify(value.result);
      const accepted = await readLocalTests(value);
      expect(accepted?.run).toMatchObject({ execution, coverage, verdict, freshness, executedAssertions });
      expect(JSON.stringify(value.result)).toBe(compilerBytes);
      expect(value.result.verdict).toBe('coglatas.pass');
      expect(Object.isFrozen(accepted!.run)).toBe(true);
    }
  });

  it('refuses rehashed passing-row aliases that replace an actual failing assertion', async () => {
    for (const prefix of ['', 'x']) {
      const value = original('tests-dependency-base', 'dependency-analysis-v1.json');
      const genuine = (await readLocalTests(value))!;
      expect(genuine.run.verdict).toBe('Fail');
      expect(genuine.run.executedAssertions).toBe(2);
      expect(genuine.run.tests[0].assertions.map(row => row.verdict)).toEqual(['Pass', 'Fail']);
      const run = JSON.parse(value.localProjectTests!.result.text);
      run.tests[0].assertions[1] = structuredClone(run.tests[0].assertions[0]);
      run.tests[0].assertions[1].location.propertyPath = prefix + run.tests[0].assertions[0].location.propertyPath;
      run.tests[0].verdict = 'Pass';
      run.verdict = 'Pass';
      replaceRun(value, run);
      expect(JSON.parse(value.localProjectTests!.result.text).executedAssertions).toBe(2);
      await expect(readLocalTests(value)).rejects.toThrow('unsupported-local-test-projection');
      expect(genuine.run.tests[0].assertions[1].verdict).toBe('Fail');
    }
  });

  it('accepts the original escaped Unicode assertion key and rejects a rehashed noncanonical pointer', async () => {
    const value = original('tests-unicode');
    const accepted = (await readLocalTests(value))!;
    const location = accepted.run.tests[0].assertions[0].location;
    expect(location.propertyPath).toContain('~1');
    expect(location.propertyPath).toContain('~0');
    const run = JSON.parse(value.localProjectTests!.result.text);
    run.tests[0].assertions[0].location.propertyPath = location.propertyPath.replace('~0', '~');
    replaceRun(value, run);
    await expect(readLocalTests(value)).rejects.toThrow('unsupported-local-test-projection');
  });

  it('refuses a rehashed duplicate result member without admitting ambiguous JSON', async () => {
    const value = original();
    const projection = value.localProjectTests!;
    const text = projection.result.text.replace('"verdict":"Pass"', '"verdict":"Fail","verdict":"Pass"');
    expect(text).not.toBe(projection.result.text);
    Object.assign(projection, { result: { text, digest: hash('coglatas.project-test-run/1', text) } });
    await expect(readLocalTests(value)).rejects.toThrow();
  });

  it('refuses rehashed foreign assertion locations and mismatched Compiler input identity', async () => {
    for (const mode of ['location', 'input']) {
      const value = original();
      const run = JSON.parse(value.localProjectTests!.result.text);
      if (mode === 'location') run.tests[0].assertions[0].location.context.branch.projectId = 'foreign-project';
      else run.compilerInputId = 'foreign-compiler-input';
      replaceRun(value, run);
      await expect(readLocalTests(value)).rejects.toThrow('unsupported-local-test-projection');
    }
  });

  it('refuses an altered original assertion even after its row and run hashes are recomputed', async () => {
    const value = original();
    const projection = value.localProjectTests!;
    const assertion = JSON.parse(projection.assertions[0].text);
    assertion.key = 'different-original-key';
    const changedText = JSON.stringify(assertion);
    Object.assign(projection.assertions[0], { text: changedText });
    const run = JSON.parse(projection.result.text);
    run.tests[0].assertions[0].assertionDigest = hash('coglatas.project-test-assertion/1', changedText);
    replaceRun(value, run);
    await expect(readLocalTests(value)).rejects.toThrow('unsupported-local-test-projection');
  });

  it('refuses rehashed complete-pass labels on absent incomplete and interrupted execution', async () => {
    for (const key of ['tests-empty', 'tests-unknown', 'tests-cancelled', 'tests-exhausted']) {
      const value = original(key);
      expect((await readLocalTests(value))!.run.verdict).not.toBe('Pass');
      const run = JSON.parse(value.localProjectTests!.result.text);
      run.verdict = 'Pass';
      run.coverage = 'Complete';
      run.evidenceKind = 'coglatas.model-proof';
      replaceRun(value, run);
      await expect(readLocalTests(value)).rejects.toThrow('unsupported-local-test-projection');
    }
  });

  it('owns detached frozen results while detecting later caller projection substitution', async () => {
    const value = original('tests-fail');
    const accepted = (await readLocalTests(value))!;
    const originalBytes = accepted.projection.result.text;
    expect(Object.isFrozen(accepted.run.tests[0].assertions[0].location)).toBe(true);
    expect(Object.isFrozen(accepted.projection.assertions)).toBe(true);
    Object.assign(value.localProjectTests!.result, { text: '{}' });
    expect(localTestsMatchCase(value, accepted)).toBe(false);
    expect(accepted.projection.result.text).toBe(originalBytes);
    expect(accepted.run.verdict).toBe('Fail');
  });

  it('reads exact prepared definition bytes and separately admits the genuine before and after outcomes', async () => {
    const data = bundle('test-edit-v1.json');
    const before = member(data, 'tests-edit-before');
    const accepted = (await readPreparedTestEdit(data, before))!;
    const first = (await readLocalTests(before))!;
    const second = (await readLocalTests(accepted.after))!;
    expect(accepted.definitionText).toBe(first.projection.declarations[0].text);
    expect(first.run.verdict).toBe('Fail');
    expect(second.run.verdict).toBe('Pass');
    expect(accepted.after).toBe(member(data, 'tests-edit-after'));
    expect(first.run.inputDigest).not.toEqual(second.run.inputDigest);
  });

  it('refuses semantically equal rehashed definition whitespace at reader export and application boundaries', async () => {
    const store = await storeFor('tests-edit-before', 'test-edit-v1.json');
    const prepared = store.preparedTestEdit()!;
    const before = store.context()!;
    const originalText = prepared.definitionText;
    const changed = originalText + '\n ';
    expect(JSON.parse(changed)).toEqual(JSON.parse(originalText));
    Object.assign(prepared, { definitionText: changed });
    Object.assign(prepared.plan, { beforeDeclarationDigest: hash('coglatas.project-test-declaration/1', changed) });
    await expect(readPreparedTestEdit(store.bundle()!, before)).rejects.toThrow('unsupported-prepared-test-edit');
    expect(await store.exportPreparedTestDefinition(prepared)).toBeNull();
    expect(await store.loadPreparedTestEdit(prepared)).toBe(false);
    expect(store.context()).toBe(before);
  });

  it('exports the owned definition without changing context and refuses a cloned prepared command', async () => {
    const store = await storeFor('tests-edit-before', 'test-edit-v1.json');
    const prepared = store.preparedTestEdit()!;
    const before = store.context();
    expect(await store.exportPreparedTestDefinition(prepared)).toBe(prepared.definitionText);
    expect(store.context()).toBe(before);
    expect(await store.loadPreparedTestEdit(structuredClone(prepared))).toBe(false);
    expect(store.context()).toBe(before);
    expect(await store.loadPreparedTestEdit(prepared)).toBe(true);
    expect(store.context()?.key).toBe('tests-edit-after');
    expect(store.localTests()).toBeNull();
    expect(await store.loadLocalTests()).toBe(true);
    expect(store.localTests()!.run.verdict).toBe('Pass');
  });

  it('rejects delayed original-byte admission after the caller changes its captured declaration basis', async () => {
    const value = original();
    const gate = holdNextDigest();
    const pending = readLocalTests(value);
    await gate.entered;
    Object.assign(value.declarations[0].data, { displayName: 'changed while digest was pending' });
    gate.release();
    await expect(pending).rejects.toThrow('unsupported-local-test-projection');
  });

  it('withdraws pending Test reads at permission loss without restoring a previous assertion', async () => {
    const store = await storeFor();
    expect(await store.loadLocalTests()).toBe(true);
    const assertion = store.localTests()!.run.tests[0].assertions[0];
    expect(store.navigateTestAssertion(assertion)).toBe(true);
    const gate = holdNextDigest();
    const pending = store.loadLocalTests();
    await gate.entered;
    expect(store.testReadState()).toBe('loading');
    store.clear('denied');
    gate.release();
    expect(await pending).toBe(false);
    expect(store.state()).toBe('denied');
    expect(store.localTests()).toBeNull();
    expect(store.context()).toBeNull();
    expect(store.sourceTarget()).toBeNull();
    expect(store.navigateTestAssertion(assertion)).toBe(false);
  });

  it('preserves the exact newer admitted report when an older digest request completes', async () => {
    const store = await storeFor();
    const gate = holdNextDigest();
    const older = store.loadLocalTests();
    await gate.entered;
    store.choose('tests-pass');
    store.loadResult();
    expect(await store.loadLocalTests()).toBe(true);
    const newer = store.localTests();
    const result = store.result();
    const assertion = newer!.run.tests[0].assertions[0];
    expect(store.navigateTestAssertion(assertion)).toBe(true);
    const source = store.sourceTarget();
    gate.release();
    expect(await older).toBe(false);
    expect(store.localTests()).toBe(newer);
    expect(store.result()).toBe(result);
    expect(store.sourceTarget()).toBe(source);
    expect(store.localTests()!.run.verdict).toBe('Pass');
  });

  it('retires old and cloned assertion objects after a fresh same-identity report admission', async () => {
    const store = await storeFor();
    expect(await store.loadLocalTests()).toBe(true);
    const old = store.localTests()!.run.tests[0].assertions[0];
    expect(await store.loadLocalTests()).toBe(true);
    const current = store.localTests()!.run.tests[0].assertions[0];
    expect(current).not.toBe(old);
    expect(current.location).toEqual(old.location);
    expect(store.navigateTestAssertion(old)).toBe(false);
    expect(store.navigateTestAssertion(structuredClone(current))).toBe(false);
    expect(store.navigateTestAssertion(current)).toBe(true);
    expect(store.sourceLocation()).toEqual(current.location);
  });

  it('withdraws an admitted report when an equal materialization instance replaces its captured basis', async () => {
    for (const field of ['sourceMap', 'source', 'manifest', 'declarations'] as const) {
      const store = await storeFor();
      expect(await store.loadLocalTests()).toBe(true);
      const assertion = store.localTests()!.run.tests[0].assertions[0];
      const context = store.context()!;
      Object.assign(context, { [field]: structuredClone(context[field]) });
      expect(store.localTests()).toBeNull();
      expect(store.navigateTestAssertion(assertion)).toBe(false);
      expect(store.sourceTarget()).toBeNull();
    }
  });

  it('rejects a delayed prepared application after access is cleared', async () => {
    const store = await storeFor('tests-edit-before', 'test-edit-v1.json');
    const prepared = store.preparedTestEdit()!;
    const gate = holdNextDigest();
    const pending = store.loadPreparedTestEdit(prepared);
    await gate.entered;
    store.clear('denied');
    gate.release();
    expect(await pending).toBe(false);
    expect(store.state()).toBe('denied');
    expect(store.context()).toBeNull();
    expect(store.preparedTestEdit()).toBeNull();
    expect(store.localTests()).toBeNull();
  });

  it('does not export an old prepared definition after a newer result and Test report are selected', async () => {
    const store = await storeFor('tests-edit-before', 'test-edit-v1.json');
    const prepared = store.preparedTestEdit()!;
    const gate = holdNextDigest();
    const pending = store.exportPreparedTestDefinition(prepared);
    await gate.entered;
    store.choose('tests-edit-after');
    store.loadResult();
    expect(await store.loadLocalTests()).toBe(true);
    const newer = store.localTests();
    const context = store.context();
    gate.release();
    expect(await pending).toBeNull();
    expect(store.context()).toBe(context);
    expect(store.localTests()).toBe(newer);
    expect(newer!.run.verdict).toBe('Pass');
  });
});
