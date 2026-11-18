/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { localTestDiscoveryMatchesCase, readLocalTestDiscovery } from './projectide-test-discovery.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());
const raw = () => JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8'));
const current = (key = 'tests-pass') => (key === 'tests-discovery-unavailable' ? JSON.parse(readFileSync(resolve('public/assets/projectide/test-discovery-v1.json'), 'utf8')) : raw()).cases.find((item: { key: string }) => item.key === key)!;
const hash = (domain: string, text: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update(domain + '\0' + text).digest('hex') });

describe('Actual Core-produced inventory without a Test run', () => {
  it.each([
    ['tests-pass', 'Ready', 1], ['tests-fail', 'Ready', 1], ['tests-unknown', 'Ready', 1], ['tests-empty', 'Ready', 0],
    ['tests-unsupported', 'Ready', 1], ['tests-cancelled', 'Ready', 1], ['tests-stale', 'StaleInput', 0],
    ['tests-evaluator-mismatch', 'Ready', 1], ['tests-error', 'Ready', 1], ['tests-exhausted', 'ResourceLimit', 0],
    ['tests-unicode', 'Ready', 1], ['tests-many', 'Ready', 33], ['tests-discovery-unavailable', 'Unavailable', 0],
  ])('admits the actual %s inventory independently of its run', async (key, state, count) => {
    const value = current(key); delete value.localProjectTests;
    const admitted = (await readLocalTestDiscovery(value))!;
    expect(admitted.inventory).toEqual(value.localProjectTestDiscovery.inventory);
    expect(admitted.inventory).toMatchObject({ state, discoveredTests: count });
    expect(admitted.inventory).not.toHaveProperty('verdict'); expect(admitted.inventory).not.toHaveProperty('executedAssertions');
    expect(Object.isFrozen(admitted)).toBe(true); expect(Object.isFrozen(admitted.inventory.tests)).toBe(true);
    for (const test of admitted.inventory.tests) { expect(Object.isFrozen(test)).toBe(true); expect(Object.isFrozen(test.location)).toBe(true); }
    if (state !== 'Ready') {
      expect(admitted.inventory.tests).toHaveLength(0); expect(admitted.inventory.inputDigest).toBeNull(); expect(admitted.inventory.planDigest).toBeNull();
    }
  });

  it.each(['format', 'extra-result', 'extra-inventory-pass', 'state', 'input-bytes', 'source-bytes', 'plan-bytes', 'plan-digest',
    'input-stamp', 'plan-stamp', 'requested-stamp', 'count', 'test-id', 'name', 'status', 'assertion-count', 'tenant', 'document',
    'path', 'duplicate-test', 'duplicate-definition', 'definition-bytes', 'order', 'prefix', 'unknown-count', 'engine', 'binding',
    'evaluator', 'missing-binding', 'missing-definitions', 'duplicate-original-member'])(
    'rejects substituted %s metadata, including self-rehashed inconsistent plan bytes', async mode => {
      const value = current(mode === 'order' || mode === 'prefix' ? 'tests-many' : mode === 'unknown-count' ? 'tests-unsupported' : 'tests-pass');
      const p = value.localProjectTestDiscovery; const inventory = p.inventory; const plan = JSON.parse(p.plan.text);
      if (mode === 'format') p.format = 'future';
      if (mode === 'extra-result') p.result = { verdict: 'Pass' };
      if (mode === 'extra-inventory-pass') inventory.verdict = 'Pass';
      if (mode === 'state') inventory.state = 'Future';
      if (mode === 'input-bytes') p.inputText += ' ';
      if (mode === 'source-bytes') value.sourceText += ' ';
      if (mode === 'plan-bytes') p.plan.text += ' ';
      if (mode === 'plan-digest') p.plan.digest.value = 'a'.repeat(64);
      if (mode === 'input-stamp') inventory.inputDigest.value = 'a'.repeat(64);
      if (mode === 'plan-stamp') inventory.planDigest.value = 'a'.repeat(64);
      if (mode === 'requested-stamp') p.requestedInputDigest.value = 'a'.repeat(64);
      if (mode === 'count') inventory.discoveredTests++;
      if (mode === 'test-id') inventory.tests[0].testId = 'foreign';
      if (mode === 'name') inventory.tests[0].displayName = 'Substituted name';
      if (mode === 'status') inventory.tests[0].decodeStatus = 'Unsupported';
      if (mode === 'assertion-count') inventory.tests[0].assertionCount = 0;
      if (mode === 'tenant') inventory.tests[0].location.context.branch.tenantId = 'foreign';
      if (mode === 'document') inventory.tests[0].location.documentId = 'foreign';
      if (mode === 'path') inventory.tests[0].location.propertyPath = '/payload';
      if (mode === 'duplicate-test') { inventory.tests.push(inventory.tests[0]); inventory.discoveredTests++; }
      if (mode === 'duplicate-definition') p.definitions.push(p.definitions[0]);
      if (mode === 'definition-bytes') p.definitions[0].text = '{}';
      if (mode === 'order') [inventory.tests[0], inventory.tests[1]] = [inventory.tests[1], inventory.tests[0]];
      if (mode === 'prefix') { inventory.tests.pop(); inventory.discoveredTests--; }
      if (mode === 'unknown-count') inventory.tests[0].assertionCount = 0;
      if (mode === 'binding') inventory.bindingComplete = false;
      if (mode === 'evaluator') inventory.evaluatorMatches = false;
      if (mode === 'missing-binding') { value.semanticInspection = null; p.binding = null; }
      if (mode === 'missing-definitions') p.definitions = [];
      if (mode === 'engine') plan.engine.version = '2';
      if (mode === 'duplicate-original-member') {
        p.definitions[0].text = p.definitions[0].text.replace('"entityId":', '"entityId":"foreign","entityId":');
        plan.tests[0].declarationDigest = hash('coglatas.project-test-declaration/1', p.definitions[0].text);
      }
      if (mode === 'engine' || mode === 'duplicate-original-member') {
        p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); inventory.planDigest = p.plan.digest;
      }
      await expect(readLocalTestDiscovery(value)).rejects.toThrow();
    });

  it.each(['tests-stale', 'tests-exhausted', 'tests-discovery-unavailable'])('refused %s inventories cannot contain a successful prefix', async key => {
    const value = current(key); const inventory = value.localProjectTestDiscovery.inventory;
    inventory.tests = current().localProjectTestDiscovery.inventory.tests; inventory.discoveredTests = 1;
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('does not fabricate missing metadata and invalidates an owned copy after nested changes', async () => {
    expect(await readLocalTestDiscovery(readPreviewBundle(raw()).cases[0])).toBeNull();
    const value = current(); const admitted = (await readLocalTestDiscovery(value))!;
    value.localProjectTestDiscovery.inventory.tests[0].displayName = 'Changed';
    expect(admitted.inventory.tests[0].displayName).toBe('Input assertion'); expect(localTestDiscoveryMatchesCase(value, admitted)).toBe(false);
  });
});

describe('Independent inventory store lifecycle', () => {
  async function ready(key = 'tests-fail') {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw()));
    store.choose(key); store.loadResult(); expect(await store.loadLocalTestDiscovery()).toBe(true); return store;
  }
  it('opens the exact owned definition while preserving Compiler PASS and a separately loaded Test FAIL', async () => {
    const store = await ready(); expect(store.localTests()).toBeNull();
    expect(await store.loadLocalTests()).toBe(true); const run = store.localTests()!;
    expect(await store.loadLocalTestDiscovery()).toBe(true); expect(store.localTests()).toBe(run);
    expect(run.run.verdict).toBe('Fail'); expect(store.result()!.result.verdict).toBe('coglatas.pass');
    const row = store.localTestDiscovery()!.inventory.tests[0]; expect(await store.navigateDiscoveredTest(row)).toBe(true);
    expect(store.sourceLocation()).toEqual(row.location); expect(store.sourceTarget()!.identity).toBe(row.testId);
  });
  it.each(['load-result', 'context', 'denied', 'cancelled', 'reload', 'test-results'])(
    'clears inventory and original row navigation on %s', async mode => {
      const store = await ready(); const row = store.localTestDiscovery()!.inventory.tests[0]; expect(await store.navigateDiscoveredTest(row)).toBe(true);
      if (mode === 'load-result') store.loadResult();
      if (mode === 'context') store.choose('branch-pass');
      if (mode === 'denied') store.clear('denied');
      if (mode === 'cancelled') store.clear('cancelled');
      if (mode === 'reload') await store.load(async () => readPreviewBundle(raw()));
      if (mode === 'test-results') await store.loadLocalTests();
      expect(store.localTestDiscovery()).toBeNull(); expect(store.sourceTarget()).toBeNull(); expect(await store.navigateDiscoveredTest(row)).toBe(false);
    });
  it('rejects old and copied rows after inventory reload and hides nested materialization changes', async () => {
    const store = await ready(); const old = store.localTestDiscovery()!.inventory.tests[0];
    expect(await store.loadLocalTestDiscovery()).toBe(true); const row = store.localTestDiscovery()!.inventory.tests[0];
    expect(await store.navigateDiscoveredTest(old)).toBe(false); expect(await store.navigateDiscoveredTest({ ...row })).toBe(false);
    expect(await store.navigateDiscoveredTest(row)).toBe(true);
    Object.assign(store.context()!.declarations[0].data, { displayName: 'Changed' });
    expect(store.localTestDiscovery()).toBeNull(); expect(await store.navigateDiscoveredTest(row)).toBe(false);
  });
  it.each(['denied', 'context', 'load-result', 'newer-read'])('does not restore a late inventory after %s', async mode => {
    const store = await ready(); const original = crypto.subtle.digest.bind(crypto.subtle);
    let release!: () => void; let entered!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, data) => { entered(); await gate; return original(algorithm, data); });
    const pending = store.loadLocalTestDiscovery(); await began;
    if (mode === 'denied') store.clear('denied');
    if (mode === 'context') store.choose('branch-pass');
    if (mode === 'load-result') store.loadResult();
    if (mode === 'newer-read') expect(await store.loadLocalTestDiscovery()).toBe(true);
    const newer = store.localTestDiscovery(); release(); expect(await pending).toBe(false);
    if (mode === 'newer-read') expect(store.localTestDiscovery()).toBe(newer); else expect(store.localTestDiscovery()).toBeNull();
  });
});


describe('Reviewed inventory admission regressions', () => {
  const profile = (name: string) => JSON.parse(readFileSync(resolve('public/assets/projectide/' + name + '.json'), 'utf8'));
  it.each(['tests-plan-budget-base', 'tests-plan-budget-hypothesis', 'tests-plan-budget-draft'])('lists definitions independently of Compiler execution for %s', async key => {
    const value = profile('execution-plan-v1').cases.find((item: { key: string }) => item.key === key);
    delete value.localProjectTests;
    const admitted = (await readLocalTestDiscovery(value))!;
    expect(admitted.inventory).toMatchObject({ state: 'Ready', discoveredTests: 1, bindingComplete: true });
    expect(admitted.inventory.tests[0].assertionCount).toBe(2);
  });
  it.each(['tests-missing-reference-base', 'tests-denied-reference-base'])('keeps partial binding and local Source visible for %s', async key => {
    const data = profile('scenario-v1'); for (const value of data.cases) delete value.localProjectTests;
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(data)); store.choose(key); store.loadResult();
    expect(await store.loadLocalTestDiscovery()).toBe(true);
    const inventory = store.localTestDiscovery()!.inventory; expect(inventory).toMatchObject({ state: 'Ready', bindingComplete: false, discoveredTests: 1 });
    const row = inventory.tests[0]; expect(row).toMatchObject({ decodeStatus: 'Decoded', assertionCount: 2 });
    expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceTarget()!.identity).toBe(row.testId); expect(store.localTests()).toBeNull();
  });
  it('rejects a self-rehashed false resource refusal for a plan that fits its actual bounds', async () => {
    const value = current(); const data = value.localProjectTestDiscovery; const plan = JSON.parse(data.plan.text);
    plan.withinLimits = false; data.plan.text = JSON.stringify(plan); data.plan.digest = hash('coglatas.project-test-plan/1', data.plan.text);
    Object.assign(data.inventory, { state: 'ResourceLimit', inputDigest: null, planDigest: null, tests: [], discoveredTests: 0, bindingComplete: false, evaluatorMatches: false });
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('verifies original declaration bytes even when discovery is stale', async () => {
    const value = current('tests-stale'); value.localProjectTestDiscovery.definitions[0].text += ' ';
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('cannot relabel an original unsupported schema as a decoded definition', async () => {
    const value = current('tests-unsupported'); const data = value.localProjectTestDiscovery; const plan = JSON.parse(data.plan.text);
    const declaration = value.declarations.find((item: { kind: string }) => item.kind === 'coglatas.project-test');
    expect(declaration.data.schemaVersion).not.toBe(1); declaration.status = 'Decoded';
    data.inventory.tests[0].decodeStatus = 'Decoded'; data.inventory.tests[0].assertionCount = declaration.data.payload.assertions.length;
    plan.tests[0].status = 'Decoded'; data.plan.text = JSON.stringify(plan); data.plan.digest = hash('coglatas.project-test-plan/1', data.plan.text); data.inventory.planDigest = data.plan.digest;
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('validates one immutable Source snapshot throughout all asynchronous hashes', async () => {
    const value = current(); const originalText = value.sourceText; const altered = originalText + ' '; value.sourceText = altered;
    const digest = crypto.subtle.digest.bind(crypto.subtle);
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, data) => { value.sourceText = altered; return digest(algorithm, data); });
    const pending = readLocalTestDiscovery(value); value.sourceText = originalText;
    await expect(pending).rejects.toThrow();
  });
  it('opens a version-2 definition without a separate prior Scoped Sources action', async () => {
    const data = profile('simulation-v1'); const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(data));
    store.choose('tests-simulation-root-base'); store.loadResult(); expect(store.scopedSources()).toHaveLength(0);
    expect(await store.loadLocalTestDiscovery()).toBe(true); const row = store.localTestDiscovery()!.inventory.tests[0];
    expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceTarget()!.identity).toBe(row.testId); expect(store.localTests()).toBeNull();
  });
});


describe('Discovery ownership across supplementary binding and Source verification', () => {
  const data = (profile: string) => JSON.parse(readFileSync(resolve('public/assets/projectide/' + profile + '.json'), 'utf8'));
  it.each(['input', 'source', 'complete', 'references', 'unknown-state', 'foreign-origin'])('rejects altered supplementary binding %s', async mode => {
    const value = data('execution-plan-v1').cases.find((item: { key: string }) => item.key === 'tests-plan-budget-hypothesis');
    const p = value.localProjectTestDiscovery; expect(value.semanticInspection).toBeNull(); expect(p.binding).toBeTruthy();
    if (mode === 'input') p.binding.inputDigest.value = 'a'.repeat(64);
    if (mode === 'source') p.binding.sourceDigest.value = 'a'.repeat(64);
    if (mode === 'complete') p.binding.bindingComplete = false;
    if (mode === 'references') p.binding.references.pop();
    if (mode === 'unknown-state') p.binding.references[0].state = 'Future';
    if (mode === 'foreign-origin') {
      const plan = JSON.parse(p.plan.text); p.binding.references[0].origin.context.branch.tenantId = 'foreign'; plan.references = p.binding.references;
      p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); p.inventory.planDigest = p.plan.digest;
    }
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('preserves an unrelated recorded frame and captured Source when the legacy profile has no inventory', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(data('simulation-debug-v1')));
    store.choose('tests-simulation-root-base'); store.loadResult(); expect(await store.loadScopedSources()).toBe(true);
    expect(await store.loadSimulation()).toBe(true); expect(await store.loadRecordedDebug()).toBe(true);
    const debug = store.recordedDebug()!; expect(store.selectRecordedDebugFrame(debug, 2)).toBe(true); const frame = store.recordedDebugFrame()!;
    expect(store.navigateRecordedDebugLocation(frame, frame.inspection!['location'] as never)).toBe(true);
    const target = store.scopedSourceTarget(); expect(target).not.toBeNull(); expect(await store.loadLocalTestDiscovery()).toBe(false);
    expect(store.scopedSourceTarget()).toBe(target); expect(store.recordedDebug()).toBe(debug); expect(store.recordedDebugFrame()).toBe(frame);
  });
  it.each(['denied', 'context', 'result', 'newer-inventory'])('cannot finish an old version-2 Source action after %s', async mode => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(data('simulation-v1')));
    store.choose('tests-simulation-root-base'); store.loadResult(); expect(await store.loadLocalTestDiscovery()).toBe(true);
    const row = store.localTestDiscovery()!.inventory.tests[0]; const original = crypto.subtle.digest.bind(crypto.subtle);
    let release!: () => void; let entered!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, value) => { entered(); await gate; return original(algorithm, value); });
    const pending = store.navigateDiscoveredTest(row); await began;
    if (mode === 'denied') store.clear('denied');
    if (mode === 'context') store.choose('tests-simulation-root-draft');
    if (mode === 'result') store.loadResult();
    if (mode === 'newer-inventory') expect(await store.loadLocalTestDiscovery()).toBe(true);
    release(); expect(await pending).toBe(false); expect(store.sourceTarget()).toBeNull();
    if (mode === 'denied') expect(store.state()).toBe('denied');
  });
});
