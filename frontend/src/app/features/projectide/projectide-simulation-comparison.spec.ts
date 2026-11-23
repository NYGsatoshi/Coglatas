/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { PreviewCase } from './projectide.contracts';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources } from './projectide-scoped-sources';
import { readSimulationRun } from './projectide-simulation.reader';
import { readSimulationComparison } from './projectide-simulation-comparison.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
const original = readFileSync(resolve('public/assets/projectide/simulation-comparison-v1.json'), 'utf8');
const bundle = () => readPreviewBundle(JSON.parse(original));
const hash = (text: string) => ({ algorithm: 'sha-256' as const, value: createHash('sha256').update('coglatas.local-simulation-comparison/1' + '\0' + text).digest('hex') });
async function admission(value: PreviewCase) { return (await readSimulationRun(value, (await readScopedSources(value))!))!; }
async function pair(family = 'root', revision = 'base') {
  const values = bundle(); const beforeCase = values.cases.find(v => v.key === 'tests-simulation-' + family + '-' + revision)!;
  const afterCase = values.cases.find(v => v.key === beforeCase.simulationComparison!.afterCaseKey)!;
  return { values, beforeCase, afterCase, before: await admission(beforeCase), after: await admission(afterCase) };
}
async function ready() {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-simulation-external-base'); store.loadResult();
  expect(await store.loadScopedSources()).toBe(true); expect(await store.loadSimulation()).toBe(true); return store;
}
function rewrite(value: PreviewCase, edit: (data: any) => void) {
  const data = JSON.parse(value.simulationComparison!.text); edit(data); const text = JSON.stringify(data);
  Object.assign(value, { simulationComparison: { ...value.simulationComparison, text, digest: hash(text) } });
}
describe('Actual Core immutable run comparisons', () => {
  it.each(['root', 'external', 'bounded'].flatMap(family => ['base', 'hypothesis'].map(revision => [family, revision])))(
    'admits both original recordings and the exact Core comparison: %s %s', async (family, revision) => {
      const f = await pair(family, revision); const admitted = (await readSimulationComparison(f.beforeCase, f.before, f.afterCase, f.after))!;
      expect(admitted.before).toBe(f.before); expect(admitted.after).toBe(f.after); expect(admitted.sameInput).toBe(false); expect(admitted.sameLimits).toBe(true);
      expect(admitted.state).toBe(family === 'bounded' ? 'Incomplete' : 'Complete'); expect(admitted.observedElapsedDelta).toBe(family === 'bounded' ? null : '0');
      expect(admitted.beforeSummary.runDigest).toEqual(f.before.digest); expect(admitted.afterSummary.runDigest).toEqual(f.after.digest);
      expect(admitted.data['verdict']).toBe('NotRun'); expect(admitted.data['authority']).toBe('NotEvaluated'); expect(Object.isFrozen(admitted.data)).toBe(true);
      expect(Object.isFrozen(f.beforeCase.context)).toBe(false); expect(Object.isFrozen(f.afterCase.inputDigest)).toBe(false);
    });
  it.each(['bytes', 'format', 'before-digest', 'after-digest', 'source', 'context', 'count', 'clock', 'elapsed', 'delta', 'same-input', 'same-limits', 'pass', 'authority', 'extra', 'null-summary', 'numeric-delta', 'swapped'])(
    'rejects altered or rehashed comparison claims: %s', async mode => {
      const values = bundle(); const beforeCase = values.cases[0]; const afterCase = values.cases.find(v => v.key === beforeCase.simulationComparison!.afterCaseKey)!;
      if (mode === 'bytes') Object.assign(beforeCase.simulationComparison!, { text: beforeCase.simulationComparison!.text + ' ' });
      else rewrite(beforeCase, data => {
        if (mode === 'format') data.format = 'coglatas.local-simulation-comparison/2';
        if (mode === 'before-digest') data.before.runDigest.value = 'b'.repeat(64); if (mode === 'after-digest') data.after.runDigest.value = 'b'.repeat(64);
        if (mode === 'source') data.after.sourceDigest.value = 'b'.repeat(64); if (mode === 'context') data.after.context = data.before.context;
        if (mode === 'count') data.after.completedTasks++; if (mode === 'clock') data.after.virtualTicks = '104';
        if (mode === 'elapsed') data.after.observedElapsedTicks = '4'; if (mode === 'delta') data.observedElapsedDelta = '1';
        if (mode === 'same-input') data.sameInput = true; if (mode === 'same-limits') data.sameLimits = false;
        if (mode === 'pass') data.verdict = 'Pass'; if (mode === 'authority') data.authority = 'Allowed';
        if (mode === 'extra') data.eligible = true; if (mode === 'null-summary') data.after = null;
        if (mode === 'numeric-delta') data.observedElapsedDelta = 0; if (mode === 'swapped') [data.before, data.after] = [data.after, data.before];
      });
      const before = await admission(beforeCase); const after = await admission(afterCase);
      await expect(readSimulationComparison(beforeCase, before, afterCase, after)).rejects.toThrow();
    });
  it('does not turn two bounded prefixes into a complete comparison', async () => {
    const values = bundle(); const beforeCase = values.cases.find(v => v.key === 'tests-simulation-bounded-base')!;
    rewrite(beforeCase, data => { data.state = 'Complete'; data.reason = 'coglatas.comparison.complete-recordings'; data.observedElapsedDelta = '0'; });
    const afterCase = values.cases.find(v => v.key === beforeCase.simulationComparison!.afterCaseKey)!;
    await expect(readSimulationComparison(beforeCase, await admission(beforeCase), afterCase, await admission(afterCase))).rejects.toThrow();
  });
  it('rejects duplicate JSON members even when the original-byte digest is correct', async () => {
    const values = bundle(); const beforeCase = values.cases[0]; const original = beforeCase.simulationComparison!.text;
    const text = original.slice(0, -1) + ',"state":"Complete"}'; Object.assign(beforeCase.simulationComparison!, { text, digest: hash(text) });
    const afterCase = values.cases.find(v => v.key === beforeCase.simulationComparison!.afterCaseKey)!;
    await expect(readSimulationComparison(beforeCase, await admission(beforeCase), afterCase, await admission(afterCase))).rejects.toThrow();
  });
  it('rejects a different admitted recording and changed bytes during digest verification', async () => {
    const f = await pair(); await expect(readSimulationComparison(f.beforeCase, f.after, f.afterCase, f.after)).rejects.toThrow();
    const pending = readSimulationComparison(f.beforeCase, f.before, f.afterCase, f.after);
    Object.assign(f.afterCase.simulationRun!, { text: f.afterCase.simulationRun!.text + ' ' }); await expect(pending).rejects.toThrow();
  });
  it('requires explicit current Source and run admission before loading the second fixed input', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-simulation-root-base'); store.loadResult();
    expect(await store.loadSimulationComparison()).toBe(false); expect(await store.loadScopedSources()).toBe(true); expect(await store.loadSimulationComparison()).toBe(false);
    expect(await store.loadSimulation()).toBe(true); const current = store.simulation(); expect(await store.loadSimulationComparison()).toBe(true);
    expect(store.simulation()).toBe(current); expect(store.context()!.key).toBe('tests-simulation-root-base');
    expect(store.simulationComparison()!.after.sources.regions.length).toBeGreaterThan(0);
  });
  it.each(['choose', 'result', 'sources', 'run', 'denied', 'reload'])(
    'withdraws both pending and admitted pairs at the current lifecycle boundary: %s', async mode => {
      const store = await ready(); expect(await store.loadSimulationComparison()).toBe(true); const old = store.simulationComparison()!;
      const pending = store.loadSimulationComparison();
      if (mode === 'choose') store.choose('tests-simulation-external-hypothesis'); if (mode === 'result') store.loadResult();
      if (mode === 'sources') await store.loadScopedSources(); if (mode === 'run') await store.loadSimulation();
      if (mode === 'denied') store.clear('denied'); if (mode === 'reload') await store.load(async () => bundle());
      expect(await pending).toBe(false); expect(store.simulationComparison()).toBeNull(); expect(store.simulationComparisonReadState()).toBe('idle');
      expect(old.after.events).toHaveLength(8); expect(old.before.events).toHaveLength(8);
    });
  it.each(['before', 'after', 'removed', 'same-key-replacement'])(
    'withdraws summaries when either original input changes or leaves the captured bundle: %s', async mode => {
      const store = await ready(); expect(await store.loadSimulationComparison()).toBe(true); const current = store.simulation();
      const before = store.context()!; const values = store.bundle()!; const after = values.cases.find(v => v.key === before.simulationComparison!.afterCaseKey)!;
      if (mode === 'before') Object.assign(before.simulationRun!, { text: before.simulationRun!.text + ' ' });
      if (mode === 'after') Object.assign(after.simulationRun!, { text: after.simulationRun!.text + ' ' });
      if (mode === 'removed') Object.assign(values, { cases: values.cases.filter(v => v !== after) });
      if (mode === 'same-key-replacement') Object.assign(values, { cases: values.cases.map(v => v === after ? structuredClone(after) : v) });
      expect(store.simulationComparison()).toBeNull(); if (mode !== 'before') expect(store.simulation()).toBe(current);
    });
  it('does not let the earlier pending pair replace the latest request', async () => {
    const store = await ready(); const first = store.loadSimulationComparison(); const second = store.loadSimulationComparison();
    expect(await first).toBe(false); expect(await second).toBe(true); expect(store.simulationComparisonReadState()).toBe('ready'); expect(store.simulationComparison()).not.toBeNull();
  });
  it('reports changed second-input bytes during admission as unavailable', async () => {
    const store = await ready(); const pending = store.loadSimulationComparison();
    const after = store.bundle()!.cases.find(v => v.key === store.context()!.simulationComparison!.afterCaseKey)!;
    Object.assign(after.simulationRun!, { text: after.simulationRun!.text + ' ' });
    expect(await pending).toBe(false); expect(store.simulationComparison()).toBeNull(); expect(store.simulationComparisonReadState()).toBe('unavailable');
  });
});
