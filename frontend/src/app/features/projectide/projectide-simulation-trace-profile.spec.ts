/// <reference types="node" />
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources } from './projectide-scoped-sources';
import { readSimulationRun } from './projectide-simulation.reader';
import { PreviewCase } from './projectide.contracts';
import { readSimulationTraceProfile } from './projectide-simulation-trace-profile.reader';
vi.stubGlobal('crypto', webcrypto);
const exported = JSON.parse(readFileSync(resolve('public/assets/projectide/simulation-profile-v1.json'), 'utf8'));
const texts = new Map(['root', 'external', 'unknown', 'bounded'].map(family => [family, JSON.stringify({ ...exported,
  cases: exported.cases.filter((value: { key: string }) => value.key.startsWith('tests-simulation-' + family + '-')) })]));
const bundle = (family = 'root') => readPreviewBundle(JSON.parse(texts.get(family)!));
const hash = (text: string) => ({ algorithm: 'sha-256' as const, value: createHash('sha256').update('coglatas.local-simulation-trace-profile/1\0' + text).digest('hex') });
async function admission(value: PreviewCase) { return (await readSimulationRun(value, (await readScopedSources(value))!))!; }
function rewrite(value: PreviewCase, edit: (profile: any) => void): void {
  const data = JSON.parse(value.simulationTraceProfile!.text); edit(data); const text = JSON.stringify(data);
  Object.assign(value, { simulationTraceProfile: { text, digest: hash(text) } });
}
describe('Actual Core trace profile admission', () => {
  it.each(['root', 'external', 'bounded'])('admits all three actual fixed revision kinds without reexecuting: %s', async family => {
    for (const value of bundle(family).cases as PreviewCase[]) {
      const run = await admission(value); const p = (await readSimulationTraceProfile(value, run))!;
      expect(p.simulation).toBe(run); expect(p.state).toBe(family === 'bounded' ? 'Incomplete' : 'Complete');
      expect(p.metrics.recordedElapsedTicks).toBe(family === 'bounded' ? '0' : '3');
      expect(p.metrics.recordedBusyTicks).toBe(family === 'bounded' ? '0' : '3');
      expect(p.metrics.recordedTaskTicks).toBe(family === 'bounded' ? '0' : family === 'external' ? '4' : '3');
      expect(p.metrics.openTasks).toBe(family === 'bounded' ? 2 : 0); expect(p.data['inputKind']).toBe('SimulatedModel');
      expect(p.data['verdict']).toBe('NotRun'); expect(p.data['authority']).toBe('NotEvaluated'); expect(Object.isFrozen(p.metrics)).toBe(true);
      expect(Object.isFrozen(value.context)).toBe(false); expect(Object.isFrozen(value.inputDigest)).toBe(false);
    }
  });
  it.each(['format', 'kind', 'evidence', 'authority', 'verdict', 'capacity', 'attribution', 'run', 'input', 'source', 'manifest', 'plan', 'context', 'as-of', 'period',
    'elapsed', 'busy', 'idle', 'task-time', 'peak', 'open', 'definitions', 'query-zero', 'query-large', 'query-small', 'extra', 'null-metrics', 'numeric-ticks'])(
    'rejects a correctly rehashed inconsistent claim: %s', async mode => {
      const value = bundle().cases[0] as PreviewCase;
      rewrite(value, p => {
        if (mode === 'format') p.format = 'coglatas.local-simulation-trace-profile/2'; if (mode === 'kind') p.inputKind = 'ObservedOperational';
        if (mode === 'evidence') p.evidenceKind = 'coglatas.observation'; if (mode === 'authority') p.authority = 'Allowed';
        if (mode === 'verdict') p.verdict = 'Pass'; if (mode === 'capacity') p.capacityModel = 'coglatas.resource-scheduled';
        if (mode === 'attribution') p.attribution = 'Person';
        if (mode === 'run') p.basis.runDigest.value = 'b'.repeat(64); if (mode === 'input') p.basis.inputDigest.value = 'b'.repeat(64);
        if (mode === 'source') p.basis.sourceDigest.value = 'b'.repeat(64); if (mode === 'manifest') p.basis.manifestDigest.value = 'b'.repeat(64);
        if (mode === 'plan') p.basis.planDigest.value = 'b'.repeat(64); if (mode === 'context') p.basis.context.branch.projectId = 'b'.repeat(36);
        if (mode === 'as-of') p.asOf.kind = 'LiveNow'; if (mode === 'period') p.period.through = '104';
        if (mode === 'elapsed') p.metrics.recordedElapsedTicks = '4'; if (mode === 'busy') p.metrics.recordedBusyTicks = '4';
        if (mode === 'idle') p.metrics.unattributedIdleTicks = '1'; if (mode === 'task-time') p.metrics.recordedTaskTicks = '4';
        if (mode === 'peak') p.metrics.peakActiveTasks++; if (mode === 'open') p.metrics.openTasks++;
        if (mode === 'definitions') p.definitions[0].unit = 'second';
        if (mode === 'query-zero') p.queryBudget.maximumEvents = 0; if (mode === 'query-large') p.queryBudget.maximumEvents = 8193;
        if (mode === 'query-small') p.queryBudget.maximumEvents = 5; if (mode === 'extra') p.eligible = true;
        if (mode === 'null-metrics') p.metrics = null; if (mode === 'numeric-ticks') p.metrics.recordedElapsedTicks = 3;
      });
      await expect(readSimulationTraceProfile(value, await admission(value))).rejects.toThrow();
    });
  it('refuses a bounded recording relabelled as complete even with a new matching digest', async () => {
    const value = bundle('bounded').cases[0] as PreviewCase;
    rewrite(value, p => { p.state = 'Complete'; p.reason = 'coglatas.profile.complete-recording'; });
    await expect(readSimulationTraceProfile(value, await admission(value))).rejects.toThrow();
  });
  it('rejects changed original bytes and duplicate members with valid original-byte hashes', async () => {
    const a = bundle().cases[0] as PreviewCase; Object.assign(a.simulationTraceProfile!, { text: a.simulationTraceProfile!.text + ' ' });
    await expect(readSimulationTraceProfile(a, await admission(a))).rejects.toThrow();
    const b = bundle().cases[0] as PreviewCase; const text = b.simulationTraceProfile!.text.slice(0, -1) + ',"state":"Complete"}';
    Object.assign(b, { simulationTraceProfile: { text, digest: hash(text) } });
    await expect(readSimulationTraceProfile(b, await admission(b))).rejects.toThrow();
  });
  it('rejects a different admitted run and a profile replacement during digest verification', async () => {
    const values = bundle(); const value = values.cases[0] as PreviewCase; const run = await admission(value); const other = await admission(values.cases[1]);
    await expect(readSimulationTraceProfile(value, other)).rejects.toThrow();
    const pending = readSimulationTraceProfile(value, run); Object.assign(value.simulationTraceProfile!, { text: value.simulationTraceProfile!.text + ' ' });
    await expect(pending).rejects.toThrow();
  });
  it('rejects a run replacement during digest verification even when the case object remains unchanged', async () => {
    const value = bundle().cases[0] as PreviewCase; const run = await admission(value); const pending = readSimulationTraceProfile(value, run);
    Object.assign(value, { simulationRun: { ...value.simulationRun!, text: value.simulationRun!.text + ' ' } });
    await expect(pending).rejects.toThrow();
  });
  it('does not manufacture a profile when no profile projection was produced', async () => {
    const value = bundle().cases[0] as PreviewCase; Object.assign(value, { simulationTraceProfile: null });
    expect(await readSimulationTraceProfile(value, await admission(value))).toBeNull();
  });
});

async function readyStore() {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle('external'));
  store.choose('tests-simulation-external-base'); store.loadResult();
  expect(await store.loadScopedSources()).toBe(true); expect(await store.loadSimulation()).toBe(true); return store;
}
describe('Owned profile store lifecycle', () => {
  it('requires Source and run admission and reads without replacing the run or compiler result', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-simulation-root-base'); store.loadResult();
    expect(await store.loadSimulationTraceProfile()).toBe(false); expect(await store.loadScopedSources()).toBe(true);
    expect(await store.loadSimulationTraceProfile()).toBe(false); expect(await store.loadSimulation()).toBe(true);
    const run = store.simulation(); const result = store.result(); expect(await store.loadSimulationTraceProfile()).toBe(true);
    expect(store.simulationTraceProfile()!.simulation).toBe(run); expect(store.simulation()).toBe(run); expect(store.result()).toBe(result);
  });
  it.each(['choose', 'result', 'sources', 'run', 'denied', 'reload'])(
    'withdraws both pending and published profiles at the lifecycle boundary: %s', async mode => {
      const store = await readyStore(); expect(await store.loadSimulationTraceProfile()).toBe(true); const old = store.simulationTraceProfile()!;
      const pending = store.loadSimulationTraceProfile(); expect(store.simulationTraceProfile()).toBeNull();
      if (mode === 'choose') store.choose('tests-simulation-external-hypothesis'); if (mode === 'result') store.loadResult();
      if (mode === 'sources') await store.loadScopedSources(); if (mode === 'run') await store.loadSimulation();
      if (mode === 'denied') store.clear('denied'); if (mode === 'reload') await store.load(async () => bundle());
      expect(await pending).toBe(false); expect(store.simulationTraceProfile()).toBeNull(); expect(store.simulationTraceProfileReadState()).toBe('idle');
      expect(old.metrics.recordedTaskTicks).toBe('4'); expect(old.simulation.events).toHaveLength(8);
    });
  it.each(['profile', 'run', 'removed', 'same-key-replacement'])(
    'withdraws metrics when the original bytes or captured bundle membership change: %s', async mode => {
      const store = await readyStore(); expect(await store.loadSimulationTraceProfile()).toBe(true);
      const value = store.context()! as PreviewCase; const values = store.bundle()!;
      if (mode === 'profile') Object.assign(value.simulationTraceProfile!, { text: value.simulationTraceProfile!.text + ' ' });
      if (mode === 'run') Object.assign(value, { simulationRun: { ...value.simulationRun!, text: value.simulationRun!.text + ' ' } });
      if (mode === 'removed') Object.assign(values, { cases: values.cases.filter(v => v !== value) });
      if (mode === 'same-key-replacement') Object.assign(values, { cases: values.cases.map(v => v === value ? structuredClone(value) : v) });
      expect(store.simulationTraceProfile()).toBeNull();
    });
  it('lets the newer simultaneous query own the result', async () => {
    const store = await readyStore(); const first = store.loadSimulationTraceProfile(); const second = store.loadSimulationTraceProfile();
    expect(await first).toBe(false); expect(await second).toBe(true); expect(store.simulationTraceProfileReadState()).toBe('ready'); expect(store.simulationTraceProfile()).not.toBeNull();
  });
  it('does not publish a profile replaced during asynchronous verification', async () => {
    const store = await readyStore(); const pending = store.loadSimulationTraceProfile(); const value = store.context()! as PreviewCase;
    Object.assign(value, { simulationTraceProfile: { ...value.simulationTraceProfile!, text: value.simulationTraceProfile!.text + ' ' } });
    expect(await pending).toBe(false); expect(store.simulationTraceProfile()).toBeNull(); expect(store.simulationTraceProfileReadState()).toBe('unavailable');
  });
  it('does not publish a profile when its original case leaves the bundle during verification', async () => {
    const store = await readyStore(); const pending = store.loadSimulationTraceProfile(); const values = store.bundle()!; const value = store.context();
    Object.assign(values, { cases: values.cases.filter(v => v !== value) });
    expect(await pending).toBe(false); expect(store.simulationTraceProfile()).toBeNull(); expect(store.simulationTraceProfileReadState()).toBe('unavailable');
  });
});


describe('Owned summary recording navigation', () => {
  it('returns the exact existing recording only through its current summary', async () => {
    const store = await readyStore(); const run = store.simulation(); const result = store.result();
    expect(await store.loadSimulationTraceProfile()).toBe(true); const basis = store.simulationTraceProfile()!;
    expect(store.simulationTraceProfileRun(basis)).toBe(run); expect(store.simulation()).toBe(run); expect(store.result()).toBe(result);
    expect(store.simulationTraceProfileRun({ ...basis })).toBeNull(); expect(store.simulationTraceProfile()).toBe(basis);
    expect(await store.loadSimulationTraceProfile()).toBe(true); expect(store.simulationTraceProfileRun(basis)).toBeNull();
    expect(store.simulationTraceProfileRun(store.simulationTraceProfile()!)).toBe(run);
  });
  it.each(['sources', 'run', 'denied', 'choose'] as const)('refuses an older summary after %s', async mode => {
    const store = await readyStore(); expect(await store.loadSimulationTraceProfile()).toBe(true); const basis = store.simulationTraceProfile()!;
    const original = basis.text;
    if (mode === 'sources') await store.loadScopedSources();
    if (mode === 'run') await store.loadSimulation();
    if (mode === 'denied') store.clear('denied');
    if (mode === 'choose') store.choose('tests-simulation-external-hypothesis');
    expect(store.simulationTraceProfileRun(basis)).toBeNull(); expect(basis.text).toBe(original);
  });
});
