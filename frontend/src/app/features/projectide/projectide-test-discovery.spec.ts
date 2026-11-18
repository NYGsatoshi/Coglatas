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
    'evaluator', 'missing-inspection', 'missing-definitions', 'duplicate-original-member'])(
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
      if (mode === 'missing-inspection') value.semanticInspection = null;
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
    const row = store.localTestDiscovery()!.inventory.tests[0]; expect(store.navigateDiscoveredTest(row)).toBe(true);
    expect(store.sourceLocation()).toEqual(row.location); expect(store.sourceTarget()!.identity).toBe(row.testId);
  });
  it.each(['load-result', 'context', 'denied', 'cancelled', 'reload', 'test-results'])(
    'clears inventory and original row navigation on %s', async mode => {
      const store = await ready(); const row = store.localTestDiscovery()!.inventory.tests[0]; expect(store.navigateDiscoveredTest(row)).toBe(true);
      if (mode === 'load-result') store.loadResult();
      if (mode === 'context') store.choose('branch-pass');
      if (mode === 'denied') store.clear('denied');
      if (mode === 'cancelled') store.clear('cancelled');
      if (mode === 'reload') await store.load(async () => readPreviewBundle(raw()));
      if (mode === 'test-results') await store.loadLocalTests();
      expect(store.localTestDiscovery()).toBeNull(); expect(store.sourceTarget()).toBeNull(); expect(store.navigateDiscoveredTest(row)).toBe(false);
    });
  it('rejects old and copied rows after inventory reload and hides nested materialization changes', async () => {
    const store = await ready(); const old = store.localTestDiscovery()!.inventory.tests[0];
    expect(await store.loadLocalTestDiscovery()).toBe(true); const row = store.localTestDiscovery()!.inventory.tests[0];
    expect(store.navigateDiscoveredTest(old)).toBe(false); expect(store.navigateDiscoveredTest({ ...row })).toBe(false);
    expect(store.navigateDiscoveredTest(row)).toBe(true);
    Object.assign(store.context()!.declarations[0].data, { displayName: 'Changed' });
    expect(store.localTestDiscovery()).toBeNull(); expect(store.navigateDiscoveredTest(row)).toBe(false);
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
