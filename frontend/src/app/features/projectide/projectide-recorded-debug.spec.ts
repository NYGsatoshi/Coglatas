/// <reference types="node" />
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { ProjectLocation } from './projectide.contracts';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources } from './projectide-scoped-sources';
import { readSimulationRun } from './projectide-simulation.reader';
import { readRecordedDebug } from './projectide-recorded-debug.reader';
vi.stubGlobal('crypto', webcrypto);
const fixture = JSON.parse(readFileSync(resolve('public/assets/projectide/simulation-debug-v1.json'), 'utf8'));
const text = new Map(['root', 'external', 'unknown', 'bounded'].map(family => [family, JSON.stringify({ ...fixture,
  cases: fixture.cases.filter((value: { key: string }) => value.key.startsWith('tests-simulation-' + family + '-')) })]));
const bundle = (family = 'root') => readPreviewBundle(JSON.parse(text.get(family)!));
const hash = (domain: string, text: string) => ({ algorithm: 'sha-256' as const, value: createHash('sha256').update(domain + '\0' + text).digest('hex') });
function changeFrame(value: any, index: number, edit: (snapshot: any, row: any) => void): void {
  const row = value.simulationDebug.frames[index]; const snapshot = JSON.parse(row.snapshot.text); edit(snapshot, row);
  row.snapshot.text = JSON.stringify(snapshot); row.snapshot.digest = hash('coglatas.simulation-debug-snapshot/1', row.snapshot.text);
}
async function admit(value: any) { const sources = (await readScopedSources(value))!; const run = await readSimulationRun(value, sources); return readRecordedDebug(value, sources, run); }
describe('Original recorded Core debugger admission', () => {
  it.each(['root', 'external', 'bounded'])('admits actual baseline/hypothesis/Draft snapshots without executing commands: %s', async family => {
    for (const value of bundle(family).cases) {
      const debug = (await admit(value))!;
      expect(debug.frames.length).toBe(family === 'root' ? 12 : family === 'external' ? 11 : 10);
      expect(debug.frames[0].state).toBe('Ready'); expect(debug.frames.at(-1)!.state).toBe('Terminated');
      expect(debug.frames.some(frame => frame.state === 'Paused')).toBe(true);
      expect(debug.frames.some(frame => frame.inspection?.['values'] === 'coglatas.fixed-input-defaults')).toBe(true);
      expect(debug.frames[1].breakpoints.map(point => point['resolved'])).toEqual([true, true, false]);
      expect(debug.frames.at(-1)!.events).toEqual(debug.simulation.events); expect(Object.isFrozen(debug.frames[0].snapshot)).toBe(true);
      expect(debug.simulation.data['verdict']).toBe('NotRun');
    }
  });
  it('retains unavailable plans and runs rather than creating a browser debug session', async () => {
    for (const value of bundle('unknown').cases) expect(await admit(value)).toBeNull();
  });
  it.each(['bytes', 'future', 'mixed-run', 'controls', 'revision', 'initial-pause', 'prefix', 'clock', 'pending', 'pending-identity', 'false-terminal', 'counts', 'breakpoint', 'inspection', 'inspection-target', 'inspection-state', 'action', 'dropped-terminal', 'stale-source'])(
    'rejects altered or rehashed inconsistent snapshots: %s', async mode => {
      const values = bundle().cases; const value: any = values[0];
      if (mode === 'bytes') value.simulationDebug.frames[0].snapshot.text += ' ';
      if (mode === 'future') value.simulationDebug.format = 'coglatas.local-simulation-debug-recording/2';
      if (mode === 'mixed-run') value.simulationDebug = values[1].simulationDebug;
      if (mode === 'controls') { const controls = JSON.parse(value.simulationDebug.controls.text); controls.commands.push('example.live-run'); value.simulationDebug.controls.text = JSON.stringify(controls); value.simulationDebug.controls.digest = hash('coglatas.simulation-debug-controls/1', value.simulationDebug.controls.text); }
      if (mode === 'revision') changeFrame(value, 1, snapshot => snapshot.revision++);
      if (mode === 'initial-pause') changeFrame(value, 0, snapshot => snapshot.state = 'Paused');
      if (mode === 'prefix') changeFrame(value, 5, (snapshot, row) => { const events = JSON.parse(row.events.text); events[0].ticks = '999'; row.events.text = JSON.stringify(events); row.events.digest = hash('coglatas.simulation-trace-prefix/1', row.events.text); snapshot.tracePrefixDigest = row.events.digest; });
      if (mode === 'clock') changeFrame(value, 5, snapshot => snapshot.virtualTicks = '999');
      if (mode === 'pending') changeFrame(value, 0, snapshot => snapshot.pending.ticks = '999');
      if (mode === 'pending-identity') changeFrame(value, 0, snapshot => snapshot.pending.location = JSON.parse(values[1].simulationDebug.frames[0].snapshot.text).pending.location);
      if (mode === 'false-terminal') changeFrame(value, 3, snapshot => snapshot.state = 'Terminated');
      if (mode === 'counts') changeFrame(value, 5, snapshot => snapshot.startedTasks++);
      if (mode === 'breakpoint') changeFrame(value, 1, snapshot => snapshot.breakpoints[2].resolved = true);
      if (mode === 'inspection') changeFrame(value, 2, (_snapshot, row) => row.inspection.text += ' ');
      if (mode === 'inspection-target') changeFrame(value, 2, (_snapshot, row) => row.target = JSON.parse(value.simulationDebug.frames[0].snapshot.text).pending.location);
      if (mode === 'inspection-state') changeFrame(value, 2, (snapshot, row) => { const inspection = JSON.parse(row.inspection.text); inspection.state = 'Completed'; row.inspection.text = JSON.stringify(inspection); row.inspection.digest = hash('coglatas.simulation-task-inspection/1', row.inspection.text); snapshot.inspectionDigest = row.inspection.digest; });
      if (mode === 'action') value.simulationDebug.frames[3].action = 'coglatas.event-step';
      if (mode === 'dropped-terminal') value.simulationDebug.frames = value.simulationDebug.frames.slice(0, 4);
      if (mode === 'stale-source') { const sources = (await readScopedSources(value))!; const run = await readSimulationRun(value, sources); value.simulationDebug.frames[0].action = 'example.old'; await expect(readRecordedDebug(value, sources, run)).rejects.toThrow(); return; }
      await expect(admit(value)).rejects.toThrow();
    });
  it.each(['skipped-breakpoint', 'step-over-overrun', 'repeated-resume-stop'])('rejects rehashed command stop violations: %s', async mode => {
    const value: any = bundle().cases[0]; const original = value.simulationDebug.frames;
    if (mode === 'skipped-breakpoint') value.simulationDebug.frames = [...original.slice(0, 3), structuredClone(original[10])];
    if (mode === 'step-over-overrun') {
      const overrun = structuredClone(original[10]); overrun.action = 'coglatas.task-step-over'; overrun.target = original[9].target;
      value.simulationDebug.frames = [...original.slice(0, 9), overrun];
      changeFrame(value, 9, snapshot => snapshot.reason = 'coglatas.debug.task-step-over');
    }
    if (mode === 'repeated-resume-stop') value.simulationDebug.frames = [...original.slice(0, 5), structuredClone(original[3]), ...original.slice(5)];
    value.simulationDebug.frames.forEach((_row: unknown, index: number) => changeFrame(value, index, snapshot => snapshot.revision = index));
    await expect(admit(value)).rejects.toThrow();
  });
  it('preserves breakpoint resume across Inspect before Continue stops at the next breakpoint', async () => {
    const value: any = bundle().cases[0]; const original = value.simulationDebug.frames;
    value.simulationDebug.frames = [...original.slice(0, 5), ...original.slice(7)];
    value.simulationDebug.frames.forEach((_row: unknown, index: number) => changeFrame(value, index, snapshot => snapshot.revision = index));
    const debug = (await admit(value))!;
    expect(debug.frames[5].events).toHaveLength(1);
    expect(debug.frames[5].snapshot['hitBreakpointId']).toBe(JSON.parse(original[7].snapshot.text).hitBreakpointId);
    expect(debug.frames[5].pending!['kind']).toBe('coglatas.task-end');
  });
  it('withdraws a late admission when original recording bytes change during hashing', async () => {
    const value: any = bundle().cases[0]; const sources = (await readScopedSources(value))!; const run = await readSimulationRun(value, sources);
    const pending = readRecordedDebug(value, sources, run); value.simulationDebug.frames[0].action = 'example.changed'; await expect(pending).rejects.toThrow();
  });
});

describe('Recorded debugger store lifecycle', () => {
  async function ready() {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-simulation-root-base'); store.loadResult();
    expect(await store.loadRecordedDebug()).toBe(false); expect(await store.loadScopedSources()).toBe(true);
    expect(await store.loadRecordedDebug()).toBe(false); expect(await store.loadSimulation()).toBe(true); return store;
  }
  it('requires separately admitted Source/run and navigates only a current original frame location', async () => {
    const store = await ready(); expect(await store.loadRecordedDebug()).toBe(true);
    const debug = store.recordedDebug()!; const first = store.recordedDebugFrame()!;
    const location = first.pending!['location'] as ProjectLocation;
    expect(store.navigateRecordedDebugLocation(first, location)).toBe(true); expect(store.scopedSourceTarget()!.location).toBe(location);
    expect(store.navigateRecordedDebugLocation({ ...first }, location)).toBe(false);
    expect(store.navigateRecordedDebugLocation(first, structuredClone(location))).toBe(false);
    expect(store.selectRecordedDebugFrame(debug, 5)).toBe(true); expect(store.scopedSourceTarget()).toBeNull();
    expect(store.navigateRecordedDebugLocation(first, location)).toBe(false);
    const current = store.recordedDebugFrame()!;
    expect(store.navigateRecordedDebugLocation(current, current.events[0].location)).toBe(true);
    expect(store.selectRecordedDebugFrame({ ...debug }, 0)).toBe(false);
    expect(store.selectRecordedDebugFrame(debug, -1)).toBe(false); expect(store.selectRecordedDebugFrame(debug, debug.frames.length)).toBe(false);
  });
  it.each(['context', 'reload-sources', 'reload-run', 'denied', 'bytes'])('withdraws recorded controls and targets at %s', async mode => {
    const store = await ready(); expect(await store.loadRecordedDebug()).toBe(true); const frame = store.recordedDebugFrame()!;
    expect(store.navigateRecordedDebugLocation(frame, frame.pending!['location'] as ProjectLocation)).toBe(true);
    if (mode === 'context') store.choose('tests-simulation-root-draft');
    if (mode === 'reload-sources') { const pending = store.loadScopedSources(); expect(store.recordedDebug()).toBeNull(); await pending; }
    if (mode === 'reload-run') { const pending = store.loadSimulation(); expect(store.recordedDebug()).toBeNull(); await pending; }
    if (mode === 'denied') store.clear('denied');
    if (mode === 'bytes') Object.assign(store.context()!.simulationDebug!.frames[0], { action: 'example.changed' });
    expect(store.recordedDebug()).toBeNull(); expect(store.recordedDebugFrame()).toBeNull(); expect(store.scopedSourceTarget()).toBeNull();
  });
  it('discards a late recorded read after changing the current context', async () => {
    const store = await ready(); const pending = store.loadRecordedDebug(); store.choose('tests-simulation-root-draft');
    expect(await pending).toBe(false); expect(store.recordedDebug()).toBeNull(); expect(store.recordedDebugReadState()).toBe('idle');
  });
});
