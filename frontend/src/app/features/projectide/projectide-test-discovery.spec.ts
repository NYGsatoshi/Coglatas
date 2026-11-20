/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { parseLocalTestJson } from './projectide-tests.reader';
import { readPreviewBundle } from './projectide-preview.adapter';
import { localTestDiscoveryMatchesCase, readLocalTestDiscovery, localTestJsonWithinByteLimit } from './projectide-test-discovery.reader';
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
  it.each(['tests-missing-reference-base', 'tests-denied-reference-base', 'tests-missing-reference-hypothesis', 'tests-missing-reference-draft', 'tests-denied-reference-hypothesis', 'tests-denied-reference-draft'])('keeps partial binding and local Source visible for %s', async key => {
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
    const digest = crypto.subtle.digest.bind(crypto.subtle); const sourceInputs: string[] = [];
    vi.spyOn(crypto.subtle, 'digest').mockImplementation(async (algorithm, data) => {
      const bytes = new TextDecoder().decode(data);
      if (bytes.startsWith('coglatas.project-source/1\0')) { sourceInputs.push(bytes); value.sourceText = altered; }
      return digest(algorithm, data);
    });
    const pending = readLocalTestDiscovery(value); value.sourceText = originalText;
    await expect(pending).rejects.toThrow(); expect(sourceInputs).toEqual(['coglatas.project-source/1\0' + altered]);
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

describe('Reviewed Source publication, stamps and bounded basis regressions', () => {
  const asset = (name: string) => JSON.parse(readFileSync(resolve('public/assets/projectide/' + name + '.json'), 'utf8'));
  const ready = async (name = 'preview-v1', key = 'tests-pass') => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(asset(name)));
    store.choose(key); store.loadResult(); expect(await store.loadLocalTestDiscovery()).toBe(true); return store;
  };
  it.each(['explicit', 'inspection'])('rejects a self-rehashed non-string reference state from %s binding', async mode => {
    const value = mode === 'explicit' ? asset('execution-plan-v1').cases.find((item: { key: string }) => item.key === 'tests-plan-budget-hypothesis') : current();
    const p = value.localProjectTestDiscovery; const plan = JSON.parse(p.plan.text);
    const references = mode === 'explicit' ? p.binding.references : value.semanticInspection.references;
    references[0].state = ['Resolved'];
    plan.references = references.map((item: { origin: unknown; state: unknown; symbolKey: unknown }) => ({ origin: item.origin, state: item.state, symbolKey: item.symbolKey }));
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); p.inventory.planDigest = p.plan.digest;
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it.each(['compilerInputId', 'inputDigest', 'sourceDigest', 'context'])('rejects an inspection with a foreign own %s stamp', async field => {
    const value = current();
    if (field === 'compilerInputId') value.semanticInspection.compilerInputId = '000000ff-0000-4000-8000-000000000001';
    else if (field === 'context') value.semanticInspection.context.revisionId = '000000ff-0000-4000-8000-000000000001';
    else value.semanticInspection[field].value = 'a'.repeat(64);
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it.each(['context', 'tenantId', 'projectId'])('rejects fully rehashed Source with a foreign root %s', async field => {
    const value = current(); const p = value.localProjectTestDiscovery;
    if (field === 'context') value.source.context.revisionId = '000000ff-0000-4000-8000-000000000001';
    else value.source[field] = '000000ff-0000-4000-8000-000000000001';
    value.sourceText = JSON.stringify(value.source); value.sourceDigest = hash('coglatas.project-source/1', value.sourceText);
    const input = JSON.parse(p.inputText); input.sourceDigest = value.sourceDigest;
    p.inputText = JSON.stringify(input); value.manifest.manifest = input; value.inputDigest = hash('coglatas.compiler-input/1', p.inputText);
    value.semanticInspection.inputDigest = value.inputDigest; value.semanticInspection.sourceDigest = value.sourceDigest;
    p.requestedInputDigest = value.inputDigest; p.inventory.inputDigest = value.inputDigest;
    const plan = JSON.parse(p.plan.text); plan.inputDigest = value.inputDigest;
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); p.inventory.planDigest = p.plan.digest;
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it.each(['old', 'copied'])('preserves the current Source target when an %s row is rejected', async mode => {
    const store = await ready(); const old = store.localTestDiscovery()!.inventory.tests[0];
    expect(await store.loadLocalTestDiscovery()).toBe(true); const admitted = store.localTestDiscovery()!; const row = admitted.inventory.tests[0];
    expect(await store.navigateDiscoveredTest(row)).toBe(true); const target = store.sourceTarget(); expect(target).not.toBeNull();
    expect(await store.navigateDiscoveredTest(mode === 'old' ? old : { ...row })).toBe(false);
    expect(store.testDiscoveryNavigationState()).toBe('ready');
    expect(store.localTestDiscovery()).toBe(admitted); expect(store.sourceTarget()).toBe(target); expect(store.sourceLocation()).toEqual(row.location);
  });
  it('withdraws the Source target if its admitting projection changes in place', async () => {
    const store = await ready(); const row = store.localTestDiscovery()!.inventory.tests[0];
    expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceTarget()).not.toBeNull();
    Object.assign(store.context()!.localProjectTestDiscovery!.inventory.tests[0], { displayName: 'Changed after admission' });
    expect(store.localTestDiscovery()).toBeNull(); expect(store.sourceTarget()).toBeNull(); expect(store.sourceLocation()).toBeNull();
  });
  it('does not publish a late definition target after a newer Inspector selection in the same case', async () => {
    const store = await ready('simulation-v1', 'tests-simulation-root-base'); const admitted = store.localTestDiscovery()!; const row = admitted.inventory.tests[0];
    const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void; let entered!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, value) => { entered(); await gate; return original(algorithm, value); });
    const pending = store.navigateDiscoveredTest(row); await began;
    const task = store.context()!.declarations.find(item => item.kind === 'coglatas.task')!; expect(task).toBeTruthy();
    if (task.recordType !== 'entity') throw new Error('Expected the actual Task entity fixture');
    expect(store.select({ kind: 'entity', id: task.identity })).toBe(true); const selection = store.selection();
    release(); expect(await pending).toBe(false); expect(store.selection()).toBe(selection); expect(store.sourceTarget()).toBeNull(); expect(store.localTestDiscovery()).toBe(admitted);
  });
  it('admits the actual 1001-reference inventory inside the unchanged discovery basis bound', async () => {
    const value = asset('semantic-index-large-v1').cases.find((item: { key: string }) => item.key === 'tests-index-large-base');
    expect(JSON.parse(value.localProjectTestDiscovery.plan.text).references).toHaveLength(1001);
    const admitted = (await readLocalTestDiscovery(value))!;
    expect(admitted.inventory).toMatchObject({ state: 'Ready', discoveredTests: 1 }); expect(new TextEncoder().encode(admitted.basis).byteLength).toBeLessThanOrEqual(4_194_304);
    expect(admitted.inventory.tests[0].location.context).toEqual(value.context);
  });
  it.each(['base', 'hypothesis', 'draft'])('admits the version-alias regional %s inventory and opens its exact local Source', async suffix => {
    const store = await ready('regional-operator-recordings-v1', 'tests-regional-versioned-' + suffix); const row = store.localTestDiscovery()!.inventory.tests[0];
    expect(store.localTestDiscovery()!.inventory).toMatchObject({ state: 'Ready', discoveredTests: 1 }); expect(row.assertionCount).toBe(2);
    expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceTarget()!.identity).toBe(row.testId); expect(store.sourceLocation()).toEqual(row.location);
    expect(row.location.context).toEqual(store.context()!.context); expect(store.localTests()).toBeNull();
  });
});

describe('Current bundle ownership and non-destructive discovery Source', () => {
  const asset = (name: string) => JSON.parse(readFileSync(resolve('public/assets/projectide/' + name + '.json'), 'utf8'));
  const ready = async (name = 'preview-v1', key = 'tests-pass') => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(asset(name)));
    store.choose(key); store.loadResult(); expect(await store.loadLocalTestDiscovery()).toBe(true); return store;
  };
  it.each(['resolved-without-key', 'unresolved-with-key'])('rejects self-rehashed binding %s', async mode => {
    const value = asset('execution-plan-v1').cases.find((c: { key: string }) => c.key === 'tests-plan-budget-hypothesis');
    const p = value.localProjectTestDiscovery; const plan = JSON.parse(p.plan.text);
    if (mode === 'resolved-without-key') p.binding.references[0].symbolKey = null;
    else p.binding.references[0].state = 'UnknownDueToCoverage';
    plan.references = p.binding.references;
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); p.inventory.planDigest = p.plan.digest;
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('rejects forged retained definitions after Core binding budget exhaustion', async () => {
    const value = current('tests-discovery-unavailable'); const p = value.localProjectTestDiscovery; const plan = JSON.parse(p.plan.text);
    const declaration = value.declarations.find((d: { kind: string }) => d.kind === 'coglatas.project-test');
    expect(plan.tests).toHaveLength(0);
    plan.tests = [{ declarationDigest: hash('coglatas.project-test-declaration/1', p.definitions[0].text), status: declaration.status }]; plan.discoveryComplete = true;
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text);
    Object.assign(p.inventory, { state: 'Ready', inputDigest: value.inputDigest, planDigest: p.plan.digest, discoveredTests: 1, bindingComplete: plan.bindingComplete, evaluatorMatches: true,
      tests: [{ testId: declaration.identity, location: declaration.location, displayName: declaration.data.displayName, decodeStatus: declaration.status, assertionCount: declaration.data.payload.assertions.length }] });
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it.each(['Invalid', 'Unsupported'])('rejects a substituted %s decode status for an unchanged Core-decoded definition', async status => {
    const value = asset('execution-plan-v1').cases.find((c: { key: string }) => c.key === 'tests-plan-budget-hypothesis');
    const p = value.localProjectTestDiscovery; const plan = JSON.parse(p.plan.text);
    const declaration = value.declarations.find((d: { kind: string }) => d.kind === 'coglatas.project-test');
    expect(declaration.status).toBe('Decoded'); declaration.status = status; plan.tests[0].status = status;
    Object.assign(p.inventory.tests[0], { decodeStatus: status, assertionCount: null });
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); p.inventory.planDigest = p.plan.digest;
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it.each(['removed-case', 'replaced-bundle'])('withdraws inventory and Source when the admitting bundle is %s', async mode => {
    const store = await ready(); const row = store.localTestDiscovery()!.inventory.tests[0];
    expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceTarget()).not.toBeNull();
    const bundle = store.bundle()!;
    if (mode === 'removed-case') Object.assign(bundle, { cases: bundle.cases.filter(c => c !== store.context()) });
    else store.bundle.set({ ...bundle, cases: [...bundle.cases] });
    expect(store.localTestDiscovery()).toBeNull(); expect(store.sourceTarget()).toBeNull(); expect(store.sourceLocation()).toBeNull();
  });
  it('retains an Inspector selection when definition Source verification fails', async () => {
    const store = await ready('simulation-v1', 'tests-simulation-root-base');
    const task = store.context()!.declarations.find(d => d.kind === 'coglatas.task')!;
    if (task.recordType !== 'entity') throw new Error('Expected the actual Task entity');
    expect(store.select({ kind: 'entity', id: task.identity })).toBe(true); const selection = store.selection();
    const row = store.localTestDiscovery()!.inventory.tests[0];
    vi.spyOn(crypto.subtle, 'digest').mockResolvedValueOnce(new ArrayBuffer(32));
    expect(await store.navigateDiscoveredTest(row)).toBe(false); expect(store.selection()).toBe(selection); expect(store.inspectedRecord()).toBe(task);
    expect(store.localTestDiscovery()!.inventory.tests).toContain(row);
  });
  it('keeps a loaded regional recording when a pending definition Source action is abandoned', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(asset('regional-operator-recordings-v1')));
    store.choose('tests-regional-versioned-base'); store.loadResult();
    expect(await store.loadRootSource()).toBe(true); expect(await store.loadRegionalOperatorRecording('original')).toBe(true);
    const recording = store.regionalOperatorRecording()!; expect(recording).not.toBeNull(); expect(await store.loadLocalTestDiscovery()).toBe(true);
    const row = store.localTestDiscovery()!.inventory.tests[0]; const original = crypto.subtle.digest.bind(crypto.subtle);
    let release!: () => void; let entered!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, value) => { entered(); await gate; return original(algorithm, value); });
    const pending = store.navigateDiscoveredTest(row); await began;
    const task = store.context()!.declarations.find(d => d.kind === 'coglatas.task')!;
    if (task.recordType !== 'entity') throw new Error('Expected local Task');
    store.select({ kind: 'entity', id: task.identity }); release(); expect(await pending).toBe(false);
    expect(store.regionalOperatorRecording()).toBe(recording); expect(store.sourceTarget()).toBeNull();
    expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceLocation()).toEqual(row.location);
    expect(store.regionalOperatorRecording()).toBe(recording); expect(store.scopedSources()).toHaveLength(0);
  });
  it('lists both typed definitions from the main assertion-navigation case without a legacy report', async () => {
    const value = asset('preview-v1').cases.find((c: { key: string }) => c.key === 'test-assertion-navigation');
    const admitted = await readLocalTestDiscovery(value); expect(admitted).not.toBeNull();
    expect(admitted!.inventory).toMatchObject({ state: 'Ready', discoveredTests: 2 }); expect(value.localProjectTests).toBeUndefined();
  });
  it('keeps the dedicated binding-budget discovery sample free of an unreadable legacy report', () => {
    const value = current('tests-discovery-unavailable'); expect(value.localProjectTestDiscovery.inventory.state).toBe('Unavailable');
    expect(value.localProjectTests).toBeUndefined();
  });
  it('keeps graph-closure readiness separate from FAIL across committed, scenario and candidate Source', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(asset('graph-closure-v1')));
    store.choose('tests-graph-base'); store.loadResult(); expect(await store.loadLocalTests()).toBe(true); const failed = store.localTests();
    expect(failed!.run.verdict).toBe('Fail'); let prior: ReturnType<ProjectIdeWorkbenchStore['localTestDiscovery']> = null;
    for (const key of ['tests-graph-base', 'tests-graph-hypothesis', 'tests-graph-draft']) {
      if (key !== 'tests-graph-base') { store.choose(key); store.loadResult(); }
      expect(await store.loadLocalTestDiscovery()).toBe(true); const current = store.localTestDiscovery()!; const row = current.inventory.tests[0];
      expect(current.inventory).toMatchObject({ state: 'Ready', discoveredTests: 1 }); expect(row).toMatchObject({ decodeStatus: 'Decoded', assertionCount: 2 });
      if (prior) expect(await store.navigateDiscoveredTest(prior.inventory.tests[0])).toBe(false);
      expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceLocation()).toEqual(row.location); expect(row.location.context).toEqual(store.context()!.context);
      if (key === 'tests-graph-base') expect(store.localTests()).toBe(failed); prior = current;
    }
  });

  it.each(['Invalid', 'Unsupported'])('rejects explicit retained %s state conflicting with the same Core inspection', async status => {
    const value = current('tests-pass'); const p = value.localProjectTestDiscovery; const inspection = value.semanticInspection;
    p.binding = { inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, bindingComplete: inspection.bindingComplete,
      retainedTests: inspection.symbols.filter((s: { kind: string }) => s.kind === 'coglatas.project-test').map((s: { location: { entityId: string } }) => ({ testId: s.location.entityId, decodeStatus: status })),
      references: inspection.references.map((r: { origin: unknown; state: string; symbolKey: string | null }) => ({ origin: r.origin, state: r.state, symbolKey: r.symbolKey })) };
    const plan = JSON.parse(p.plan.text); plan.tests[0].status = status;
    value.declarations.find((d: { kind: string }) => d.kind === 'coglatas.project-test').status = status;
    Object.assign(p.inventory.tests[0], { decodeStatus: status, assertionCount: null });
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); p.inventory.planDigest = p.plan.digest;
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it.each([false, true])('settles superseded definition navigation after proof failure=%s', async failProof => {
    const store = await ready('simulation-v1', 'tests-simulation-root-base'); const row = store.localTestDiscovery()!.inventory.tests[0];
    const task = store.context()!.declarations.find(d => d.kind === 'coglatas.task')!;
    if (task.recordType !== 'entity') throw new Error('Expected actual Task');
    expect(store.select({ kind: 'entity', id: task.identity })).toBe(true);
    const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void; let entered!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, value) => { entered(); await gate; return failProof ? new ArrayBuffer(32) : original(algorithm, value); });
    const pending = store.navigateDiscoveredTest(row); await began; expect(store.testDiscoveryNavigationState()).toBe('loading');
    expect(store.navigate()).toBe(false); const selection = store.selection(); release(); expect(await pending).toBe(false);
    expect(store.testDiscoveryNavigationState()).toBe('idle'); expect(store.selection()).toBe(selection);
  });
  it('settles a definition action abandoned by its focus owner', async () => {
    const store = await ready('simulation-v1', 'tests-simulation-root-base'); const row = store.localTestDiscovery()!.inventory.tests[0];
    const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void; let entered!: () => void; let current = true;
    const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, value) => { entered(); await gate; return original(algorithm, value); });
    const pending = store.navigateDiscoveredTest(row, () => current); await began; current = false; release();
    expect(await pending).toBe(false); expect(store.testDiscoveryNavigationState()).toBe('idle'); expect(store.sourceTarget()).toBeNull();
  });
  it('does not settle a newer definition attempt when the older attempt completes', async () => {
    const store = await ready('simulation-v1', 'tests-simulation-root-base'); const row = store.localTestDiscovery()!.inventory.tests[0];
    const original = crypto.subtle.digest.bind(crypto.subtle); let releaseOld!: () => void; let releaseNew!: () => void; let enteredOld!: () => void; let enteredNew!: () => void;
    const oldGate = new Promise<void>(resolve => { releaseOld = resolve; }); const newGate = new Promise<void>(resolve => { releaseNew = resolve; });
    const oldBegan = new Promise<void>(resolve => { enteredOld = resolve; }); const newBegan = new Promise<void>(resolve => { enteredNew = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, value) => { enteredOld(); await oldGate; return original(algorithm, value); })
      .mockImplementationOnce(async (algorithm, value) => { enteredNew(); await newGate; return original(algorithm, value); });
    const older = store.navigateDiscoveredTest(row); await oldBegan; const newer = store.navigateDiscoveredTest(row); await newBegan;
    releaseOld(); expect(await older).toBe(false); expect(store.testDiscoveryNavigationState()).toBe('loading');
    releaseNew(); expect(await newer).toBe(true); expect(store.testDiscoveryNavigationState()).toBe('ready'); expect(store.sourceLocation()).toEqual(row.location);
  });

  it.each(['root-diagnostic-premises', 'predicate-recordings', 'operator-recordings', 'pass-recordings'])('lists both original Test definitions in the served %s profile', async profile => {
    const store = await ready(profile + '-v1', 'test-assertion-navigation'); const inventory = store.localTestDiscovery()!.inventory;
    expect(inventory).toMatchObject({ state: 'Ready', discoveredTests: 2 }); expect(store.localTests()).toBeNull();
    for (const row of inventory.tests) { expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceLocation()).toEqual(row.location); }
  });

  it('rejects coordinated plan and retained-roster forgery after binding exhaustion', async () => {
    const value = current('tests-discovery-unavailable'); const p = value.localProjectTestDiscovery; const plan = JSON.parse(p.plan.text);
    const declaration = value.declarations.find((d: { kind: string }) => d.kind === 'coglatas.project-test');
    expect(p.binding.retainedTests).toHaveLength(0); expect(value.semanticInspection).toBeNull();
    plan.tests = [{ declarationDigest: hash('coglatas.project-test-declaration/1', p.definitions[0].text), status: declaration.status }]; plan.discoveryComplete = true;
    p.binding.retainedTests = [{ testId: declaration.identity, decodeStatus: declaration.status }];
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text);
    Object.assign(p.inventory, { state: 'Ready', inputDigest: value.inputDigest, planDigest: p.plan.digest, discoveredTests: 1, bindingComplete: plan.bindingComplete, evaluatorMatches: true,
      tests: [{ testId: declaration.identity, location: declaration.location, displayName: declaration.data.displayName, decodeStatus: declaration.status, assertionCount: declaration.data.payload.assertions.length }] });
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it.each(['Invalid', 'Unsupported'])('rejects coordinated supplemental %s status without an inspection', async status => {
    const value = asset('execution-plan-v1').cases.find((c: { key: string }) => c.key === 'tests-plan-budget-hypothesis');
    expect(value.semanticInspection).toBeNull(); const p = value.localProjectTestDiscovery; const plan = JSON.parse(p.plan.text);
    value.declarations.find((d: { kind: string }) => d.kind === 'coglatas.project-test').status = status;
    p.binding.retainedTests[0].decodeStatus = status; plan.tests[0].status = status;
    Object.assign(p.inventory.tests[0], { decodeStatus: status, assertionCount: null });
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); p.inventory.planDigest = p.plan.digest;
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('admits independently matching retained evidence in a different order', async () => {
    const value = current('test-assertion-navigation'); const inspection = value.semanticInspection;
    value.localProjectTestDiscovery.binding = { inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, bindingComplete: inspection.bindingComplete,
      retainedTests: inspection.symbols.filter((s: { kind: string }) => s.kind === 'coglatas.project-test').map((s: { location: { entityId: string }; status: string }) => ({ testId: s.location.entityId, decodeStatus: s.status })).reverse(),
      references: inspection.references.map((r: { origin: unknown; state: string; symbolKey: string | null }) => ({ origin: r.origin, state: r.state, symbolKey: r.symbolKey })) };
    expect((await readLocalTestDiscovery(value))!.inventory).toMatchObject({ state: 'Ready', discoveredTests: 2 });
  });
});


describe('Fixed sample plan and asynchronous membership regressions', () => {
  const profile = (name: string) => JSON.parse(readFileSync(resolve('public/assets/projectide/' + name + '.json'), 'utf8'));
  const readyInventory = (value: any) => {
    const p = value.localProjectTestDiscovery; const plan = JSON.parse(p.plan.text);
    const declaration = value.declarations.find((d: any) => d.kind === 'coglatas.project-test');
    Object.assign(p.inventory, { state: 'Ready', inputDigest: value.inputDigest, planDigest: p.plan.digest, discoveredTests: 1,
      bindingComplete: plan.bindingComplete, evaluatorMatches: true, tests: [{ testId: declaration.identity, location: declaration.location,
        displayName: declaration.data.displayName, decodeStatus: declaration.status, assertionCount: declaration.data.payload.assertions.length }] });
  };
  it('rejects a rehashed raised assertion limit that changes an original resource refusal to Ready', async () => {
    const value = current('tests-exhausted'); const p = value.localProjectTestDiscovery; const plan = JSON.parse(p.plan.text);
    expect(plan.limits.MaximumAssertions).toBe(1); plan.limits.MaximumAssertions = 2; plan.withinLimits = true;
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); readyInventory(value);
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('rejects a coordinated rehashed binding-completeness substitution without an inspection', async () => {
    const value = profile('execution-plan-v1').cases.find((c: any) => c.key === 'tests-plan-budget-hypothesis');
    expect(value.semanticInspection).toBeNull(); const p = value.localProjectTestDiscovery; const plan = JSON.parse(p.plan.text);
    expect(plan.bindingComplete).toBe(true); plan.bindingComplete = false; p.binding.bindingComplete = false;
    p.plan.text = JSON.stringify(plan); p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text);
    p.inventory.planDigest = p.plan.digest; p.inventory.bindingComplete = false;
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('rejects replacing the independently intended stale requested input with the case input', async () => {
    const value = current('tests-stale'); const p = value.localProjectTestDiscovery;
    expect(p.requestedInputDigest).not.toEqual(value.inputDigest); p.requestedInputDigest = value.inputDigest; readyInventory(value);
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('settles its own read unavailable when the admitting case is removed during hashing', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-pass'); store.loadResult();
    const context = store.context(); const bundle = store.bundle()!; const original = crypto.subtle.digest.bind(crypto.subtle);
    let release!: () => void; let entered!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, bytes) => { entered(); await gate; return original(algorithm, bytes); });
    const pending = store.loadLocalTestDiscovery(); await began; Object.assign(bundle, { cases: bundle.cases.filter(c => c !== context) });
    expect(() => readPreviewBundle(bundle)).not.toThrow(); release(); expect(await pending).toBe(false);
    expect(store.localTestDiscovery()).toBeNull(); expect(store.testDiscoveryReadState()).toBe('unavailable');
  });
  it.each(['base', 'hypothesis', 'draft'])('admits nested reference %s as one decoded assertion', async suffix => {
    const value = profile('scenario-v1').cases.find((c: any) => c.key === 'tests-nested-reference-' + suffix);
    expect(value).toBeDefined(); const admitted = (await readLocalTestDiscovery(value))!;
    expect(admitted.inventory).toMatchObject({ state: 'Ready', bindingComplete: true, discoveredTests: 1 });
    expect(admitted.inventory.tests[0]).toMatchObject({ decodeStatus: 'Decoded', assertionCount: 1 });
  });
  it('keeps dependency analysis definitions current across all three Source contexts', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(profile('dependency-analysis-v1')));
    let previous: ReturnType<ProjectIdeWorkbenchStore['localTestDiscovery']> = null;
    for (const key of ['tests-dependency-base', 'tests-dependency-hypothesis', 'tests-dependency-draft']) {
      store.choose(key); store.loadResult(); expect(await store.loadLocalTestDiscovery()).toBe(true);
      const admitted = store.localTestDiscovery()!; expect(admitted.inventory).toMatchObject({ state: 'Ready', discoveredTests: 1 });
      const row = admitted.inventory.tests[0]; expect(row).toMatchObject({ decodeStatus: 'Decoded', assertionCount: 2 });
      if (previous) expect(await store.navigateDiscoveredTest(previous.inventory.tests[0])).toBe(false);
      expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceLocation()).toEqual(row.location);
      expect(row.location.context).toEqual(store.context()!.context); expect(store.localTests()).toBeNull(); previous = admitted;
    }
  });
  it.each(['base', 'hypothesis', 'draft'])('admits scoped %s definitions and refuses missing captured Source bodies', async suffix => {
    const data = profile('scoped-sources-v1'); const value = data.cases.find((c: any) => c.key === 'tests-scoped-' + suffix);
    expect(value).toBeDefined(); const admitted = (await readLocalTestDiscovery(value))!;
    expect(admitted.inventory).toMatchObject({ state: 'Ready', discoveredTests: 1 });
    expect(admitted.inventory.tests[0]).toMatchObject({ decodeStatus: 'Decoded', assertionCount: 2 });
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(data)); store.choose(value.key); store.loadResult();
    expect(await store.loadLocalTestDiscovery()).toBe(true); const row = store.localTestDiscovery()!.inventory.tests[0];
    expect(await store.navigateDiscoveredTest(row)).toBe(true); expect(store.sourceLocation()).toEqual(row.location);
    const snapshots = value.materializedSnapshots; expect(snapshots.entries).toHaveLength(2); expect(new Set(snapshots.entries.map((entry: any) => entry.source.digest.value)).size).toBe(2); delete snapshots.entries[1].source;
    expect(await store.navigateDiscoveredTest(row)).toBe(false); expect(store.localTestDiscovery()).toBeNull(); expect(store.localTests()).toBeNull();
  });
});


describe('Reviewed discovery boundary and lifetime failures', () => {
  async function ready() { const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-fail'); store.loadResult(); return store; }
  it('refuses changed original definition bytes even when Core retained no definitions', async () => {
    const value = current('tests-discovery-unavailable'); value.localProjectTestDiscovery.definitions[0].text += ' ';
    await expect(readLocalTestDiscovery(value)).rejects.toThrow();
  });
  it('rejects excessive aggregate definition bytes without serializing the whole projection', async () => {
    const value = current(); const projection = value.localProjectTestDiscovery;
    projection.definitions = Array.from({ length: 256 }, () => ({ ...projection.definitions[0], text: 'x'.repeat(1_048_576) }));
    const stringify = JSON.stringify; let wholeProjectionSerializations = 0;
    vi.spyOn(JSON, 'stringify').mockImplementation((value, ...args) => {
      if (value === projection) { wholeProjectionSerializations++; throw new Error('unexpected whole projection serialization'); }
      return stringify(value, ...args);
    });
    const valid = current(); const clone = globalThis.structuredClone.bind(globalThis); let oversizedClones = 0; let validClones = 0;
    vi.spyOn(globalThis, 'structuredClone').mockImplementation((input, options) => {
      if (input === projection) { oversizedClones++; throw new Error('oversized projection reached clone'); }
      if (input === valid.localProjectTestDiscovery) validClones++;
      return clone(input, options);
    });
    await expect(readLocalTestDiscovery(value)).rejects.toThrow(); expect(wholeProjectionSerializations).toBe(0); expect(oversizedClones).toBe(0);
    expect(await readLocalTestDiscovery(valid)).not.toBeNull(); expect(validClones).toBe(1);
  });
  it('directly rejects duplicate original definition members independently of plan admission', () => {
    const original = current().localProjectTestDiscovery.definitions[0].text;
    expect(parseLocalTestJson(original)).toHaveProperty('entityId');
    expect(() => parseLocalTestJson(original.replace('"entityId":', '"entityId":"foreign","entityId":'))).toThrow();
  });
  it('withdraws roster and owned Source instead of throwing after malformed live binding changes', async () => {
    const store = await ready(); expect(await store.loadLocalTestDiscovery()).toBe(true);
    const row = store.localTestDiscovery()!.inventory.tests[0]; expect(await store.navigateDiscoveredTest(row)).toBe(true);
    Object.assign(store.context()!.semanticInspection!, { references: null });
    expect(() => store.localTestDiscovery()).not.toThrow(); expect(store.localTestDiscovery()).toBeNull();
    expect(() => store.sourceTarget()).not.toThrow(); expect(store.sourceTarget()).toBeNull(); expect(store.sourceLocation()).toBeNull();
  });
  it.each([false, true])('settles its replaced admitting bundle after reader failure=%s', async failure => {
    const store = await ready(); const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void; let entered!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, bytes) => { entered(); await gate; return failure ? new ArrayBuffer(32) : original(algorithm, bytes); });
    const pending = store.loadLocalTestDiscovery(); await began; const bundle = store.bundle()!; store.bundle.set({ ...bundle, cases: [...bundle.cases] });
    release(); expect(await pending).toBe(false); expect(store.testDiscoveryReadState()).toBe('unavailable'); expect(store.localTestDiscovery()).toBeNull();
  });
  it('does not let an older report materialization failure clear newer discovery and its Source', async () => {
    const store = await ready(); const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void; let entered!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, bytes) => { entered(); await gate; return original(algorithm, bytes); });
    const pending = store.loadLocalTests(); await began; const context = store.context()!; Object.assign(context, { declarations: [...context.declarations] });
    expect(await store.loadLocalTestDiscovery()).toBe(true); const inventory = store.localTestDiscovery()!; const row = inventory.inventory.tests[0];
    expect(await store.navigateDiscoveredTest(row)).toBe(true); const target = store.sourceTarget(); release(); expect(await pending).toBe(false);
    expect(store.localTestDiscovery()).toBe(inventory); expect(store.sourceTarget()).toBe(target); expect(store.sourceLocation()).toEqual(row.location);
    expect(store.localTests()).toBeNull(); expect(store.testReadState()).toBe('unavailable');
  });
});


it('matches native JSON UTF-8 byte boundaries for Unicode, escaped and aliased DTO values', () => {
  const shared = { value: '日本語😀' }; const values: unknown[] = [null, true, false, 1.25, -1e30,
    { 'quoted"key': ['\n\t\u0000', '\ud800', '\udc00', 'é日本語😀', null, true, 123] }, [shared, shared], { omitted: undefined, included: 'yes' }];
  for (const value of values) { const bytes = new TextEncoder().encode(JSON.stringify(value)).length;
    expect(localTestJsonWithinByteLimit(value, bytes)).toBe(true); expect(localTestJsonWithinByteLimit(value, bytes - 1)).toBe(false);
  }
  const cycle: any = {}; cycle.self = cycle; expect(localTestJsonWithinByteLimit(cycle)).toBe(false);
});
it('admits every original shipped discovery case across all 23 fixed profiles', async () => {
  const files = readdirSync(resolve('public/assets/projectide')).filter(name => name.endsWith('.json')).sort(); expect(files).toHaveLength(23);
  let projected = 0; const states: Record<string, number> = {};
  for (const file of files) for (const value of JSON.parse(readFileSync(resolve('public/assets/projectide', file), 'utf8')).cases) {
    if (!value.localProjectTestDiscovery) continue;
    const admitted = (await readLocalTestDiscovery(value))!; expect(admitted).not.toBeNull();
    expect(admitted.inventory).toEqual(value.localProjectTestDiscovery.inventory); projected++;
    states[admitted.inventory.state] = (states[admitted.inventory.state] ?? 0) + 1;
    if (/^tests-(scenario|reference|regional)-/u.test(value.key)) {
      expect(admitted.inventory).toMatchObject({ state: 'Ready', discoveredTests: 1, bindingComplete: true });
      const root = value.key.startsWith('tests-scenario-') || value.key.startsWith('tests-regional-root-');
      const pins = value.manifest.manifest.settings.authorizedExternalSnapshots;
      if (value.key.startsWith('tests-scenario-')) expect(pins).toEqual([]);
      if (value.key.startsWith('tests-reference-')) {
        expect(pins).toHaveLength(1); const captured = value.materializedSnapshots.entries[0];
        expect(captured).toMatchObject({ admission: 'Available', snapshotId: pins[0].snapshotId, version: pins[0].version, context: pins[0].context });
        expect(captured.source.digest).toEqual(pins[0].digest);
      }
      expect(admitted.inventory.tests[0]).toMatchObject({ decodeStatus: 'Decoded', assertionCount: root ? 1 : 2 });
    }
  }
  expect(projected).toBe(178); expect(states).toEqual({ Ready: 175, StaleInput: 1, ResourceLimit: 1, Unavailable: 1 });
});


it('opens the regional root-only Test definition without borrowed captured Sources or a Test run', async () => {
  const data = JSON.parse(readFileSync(resolve('public/assets/projectide/regional-operator-recordings-v1.json'), 'utf8'));
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(data)); store.choose('tests-regional-root-base'); store.loadResult();
  expect(await store.loadLocalTestDiscovery()).toBe(true); const row = store.localTestDiscovery()!.inventory.tests[0];
  expect(row).toMatchObject({ decodeStatus: 'Decoded', assertionCount: 1 }); expect(await store.navigateDiscoveredTest(row)).toBe(true);
  expect(store.sourceLocation()).toEqual(row.location); expect(store.localTests()).toBeNull(); expect(store.scopedSources()).toHaveLength(0);
});


it.each([false, true])('settles an invalid current case list after reader failure=%s', async failure => {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-pass'); store.loadResult();
  const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void; let entered!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
  vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, bytes) => { entered(); await gate; return failure ? new ArrayBuffer(32) : original(algorithm, bytes); });
  const pending = store.loadLocalTestDiscovery(); await began; Object.assign(store.bundle()!, { cases: null }); release();
  expect(await pending).toBe(false); expect(store.testDiscoveryReadState()).toBe('unavailable'); expect(store.localTestDiscovery()).toBeNull();
});

it.each([false, true])('settles definition Source navigation when the live case list is null after proof failure=%s', async failProof => {
  const store = new ProjectIdeWorkbenchStore();
  await store.load(async () => readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/simulation-v1.json'), 'utf8'))));
  store.choose('tests-simulation-root-base'); store.loadResult(); expect(await store.loadLocalTestDiscovery()).toBe(true);
  const row = store.localTestDiscovery()!.inventory.tests[0]; const bundle = store.bundle()!;
  const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void; let entered!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; }); const began = new Promise<void>(resolve => { entered = resolve; });
  vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, bytes) => { entered(); await gate; return failProof ? new ArrayBuffer(32) : original(algorithm, bytes); });
  const pending = store.navigateDiscoveredTest(row); await began; Object.assign(bundle, { cases: null }); release();
  await expect(pending).resolves.toBe(false); expect(store.testDiscoveryNavigationState()).toBe('idle');
  expect(store.localTestDiscovery()).toBeNull(); expect(store.sourceTarget()).toBeNull();
});
