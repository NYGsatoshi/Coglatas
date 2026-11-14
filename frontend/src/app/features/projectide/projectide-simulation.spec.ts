/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { PreviewCase } from './projectide.contracts';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources } from './projectide-scoped-sources';
import { readSimulationRun } from './projectide-simulation.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
const exported = JSON.parse(readFileSync(resolve('public/assets/projectide/simulation-v1.json'), 'utf8'));
// Each fresh family keeps all actual Core baseline/hypothesis/Draft cross-case bindings.
const texts = new Map(['root', 'external', 'unknown', 'bounded'].map(family => [family, JSON.stringify({ ...exported,
  cases: exported.cases.filter((value: { key: string }) => value.key.startsWith('tests-simulation-' + family + '-')) })]));
const bundle = (family = 'root') => readPreviewBundle(JSON.parse(texts.get(family)!));
const hash = (domain: string, text: string) => ({ algorithm: 'sha-256' as const, value: createHash('sha256').update(domain + '\0' + text).digest('hex') });
function rewrite(value: PreviewCase, edit: (run: any) => void): void {
  const run = JSON.parse(value.simulationRun!.text); edit(run);
  run.manifestDigest = hash('coglatas.simulation-run-manifest/1', JSON.stringify(run.manifest));
  const text = JSON.stringify(run); Object.assign(value, { simulationRun: { text, digest: hash('coglatas.simulation-run/1', text) } });
}
describe('Actual Core simulation result admission', () => {
  it.each(['root', 'external', 'bounded'])('admits actual baseline/hypothesis/Draft run bytes with separate verdicts: %s', async family => {
    const ids = new Set<string>();
    for (const value of bundle(family).cases) {
      const sources = (await readScopedSources(value))!; const run = (await readSimulationRun(value, sources))!;
      expect(run.execution).toBe(family === 'bounded' ? 'ResourceExhausted' : 'Completed');
      expect(run.coverage).toBe(family === 'bounded' ? 'Partial' : 'Complete');
      expect(run.events).toHaveLength(family === 'bounded' ? 2 : family === 'root' ? 6 : 8);
      expect(run.data['verdict']).toBe('NotRun'); expect(run.data['evidenceKind']).toBe('coglatas.simulation');
      expect(value.result.verdict).toBe('coglatas.pass'); ids.add(run.runId); expect(Object.isFrozen(run.events)).toBe(true);
    }
    expect(ids.size).toBe(3);
  });
  it('retains missing required validation as no plan and no run for every revision kind', async () => {
    for (const value of bundle('unknown').cases) {
      const sources = (await readScopedSources(value))!; expect(sources.plan).toBeNull(); expect(await readSimulationRun(value, sources)).toBeNull();
      expect(sources.planNotProducedReason).toBe('coglatas.plan.required-validation-incomplete');
    }
  });
  it.each(['bytes', 'other-input', 'future', 'engine', 'policy', 'source', 'pass', 'counts', 'duplicate', 'context', 'duration', 'causality', 'event-limit', 'trace-limit'])(
    'rejects altered or rehashed inconsistent run claims: %s', async mode => {
      const values = bundle().cases; const value = values[0];
      if (mode === 'bytes') Object.assign(value.simulationRun!, { text: value.simulationRun!.text + ' ' });
      else if (mode === 'other-input') Object.assign(value, { simulationRun: values[1].simulationRun });
      else rewrite(value, run => {
        if (mode === 'future') run.format = 'coglatas.simulation-run/2';
        if (mode === 'engine') run.manifest.engine.digest.value = 'b'.repeat(64);
        if (mode === 'policy') run.manifest.policy = 'coglatas.other-policy/1';
        if (mode === 'source') run.manifest.sourceDigest.value = 'b'.repeat(64);
        if (mode === 'pass') run.verdict = 'Pass';
        if (mode === 'counts') run.completedTasks++;
        if (mode === 'duplicate') run.events[1] = { ...run.events[0], sequence: 2 };
        if (mode === 'context') run.events[0].context = values[1].context;
        if (mode === 'duration') { const end = run.events.find((event: any) => event.kind === 'coglatas.task-end'); end.ticks = String(BigInt(end.ticks) + 1n); }
        if (mode === 'event-limit') run.manifest.limits.maximumEvents = 1;
        if (mode === 'trace-limit') run.manifest.limits.maximumTraceBytes = 2;
        if (mode === 'causality') {
          const child = run.events.findIndex((event: any) => event.kind === 'coglatas.task-start' && event.ticks !== '100');
          const [start] = run.events.splice(child, 1); start.ticks = '100'; run.events.splice(1, 0, start);
          run.events.forEach((event: any, index: number) => event.sequence = index + 1);
        }
      });
      const sources = (await readScopedSources(value))!; let rejected = false;
      try { await readSimulationRun(value, sources); } catch { rejected = true; }
      expect(rejected).toBe(true);
    });
  it('requires explicit Source and run admission, navigates exact external origins, and withdraws changed bytes', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle('external')); store.choose('tests-simulation-external-base'); store.loadResult();
    expect(await store.loadSimulation()).toBe(false); expect(await store.loadScopedSources()).toBe(true); expect(store.simulation()).toBeNull();
    expect(await store.loadSimulation()).toBe(true); const run = store.simulation()!;
    const event = run.events.find(event => event.context.branch.projectId !== store.context()!.context.branch.projectId)!;
    expect(store.navigateSimulationEvent({ ...event })).toBe(false); expect(store.navigateSimulationEvent(event)).toBe(true);
    expect(store.scopedSourceTarget()!.region.context).toEqual(event.context); expect(store.scopedSourceTarget()!.location).toEqual(event.location);
    Object.assign(store.context()!.simulationRun!, { text: store.context()!.simulationRun!.text + ' ' });
    expect(store.simulation()).toBeNull(); expect(store.scopedSourceTarget()).toBeNull(); expect(store.navigateSimulationEvent(event)).toBe(false);
    expect(run.events).toHaveLength(8); expect(Object.isFrozen(run.data)).toBe(true);
  });
  it.each(['choose', 'result', 'sources', 'denied', 'pending'])( 'clears admitted and pending run state at the correct boundary: %s', async mode => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-simulation-root-base'); store.loadResult();
    expect(await store.loadScopedSources()).toBe(true);
    if (mode === 'pending') { const loading = store.loadSimulation(); store.clear('denied'); expect(await loading).toBe(false); }
    else {
      expect(await store.loadSimulation()).toBe(true);
      if (mode === 'choose') store.choose('tests-simulation-root-hypothesis');
      if (mode === 'result') store.loadResult();
      if (mode === 'sources') await store.loadScopedSources();
      if (mode === 'denied') store.clear('denied');
    }
    expect(store.simulation()).toBeNull(); expect(store.simulationReadState()).toBe('idle');
  });
  it('reports changed bytes during admission as unavailable rather than leaving a loading control', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-simulation-root-base'); store.loadResult();
    expect(await store.loadScopedSources()).toBe(true); const pending = store.loadSimulation();
    Object.assign(store.context()!.simulationRun!, { text: store.context()!.simulationRun!.text + ' ' });
    expect(await pending).toBe(false); expect(store.simulation()).toBeNull(); expect(store.simulationReadState()).toBe('unavailable');
  });
});
