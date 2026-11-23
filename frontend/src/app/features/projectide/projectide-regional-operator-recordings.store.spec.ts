/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
afterEach(() => { vi.restoreAllMocks(); vi.stubGlobal('crypto', webcrypto); });
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/regional-operator-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
async function readyStore(): Promise<ProjectIdeWorkbenchStore> {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-regional-versioned-base'); store.loadResult();
  expect(await store.loadScopedSources()).toBe(true); expect(await store.loadRegionalOperatorRecording('diagnostic')).toBe(true); return store;
}
function selectObserved(store: ProjectIdeWorkbenchStore): void {
  const region = store.regionalOperatorRecording()!.regions.find(r => r.diagnostics.some(d => d.code === 'coglatas.analysis.constraint-false'))!;
  const diagnostic = region.diagnostics.find(d => d.code === 'coglatas.analysis.constraint-false')!;
  expect(store.selectRegionalOperatorDiagnostic(region, diagnostic)).toBe(true);
}
describe('Regional operator workbench lifecycle', () => {
  it('requires explicit Source, recording, exact region diagnostic and body reads', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-regional-versioned-base'); store.loadResult();
    const result = store.result(); expect(await store.loadRegionalOperatorRecording()).toBe(false);
    expect(await store.loadRootSource()).toBe(true); expect(await store.loadRegionalOperatorRecording()).toBe(true);
    expect(store.regionalOperatorRecording()!.regions).toHaveLength(1); expect(store.regionalOperatorRecording()!.originalCaptureState).toBe('ManifestOnly');
    expect(await store.loadRecordedRegionalOperators('available')).toBe(false);
    expect(await store.loadScopedSources()).toBe(true); expect(store.regionalOperatorRecording()).toBeNull();
    expect(await store.loadRegionalOperatorRecording('diagnostic')).toBe(true); selectObserved(store);
    expect(await store.loadRecordedRegionalOperators()).toBe(true); expect(store.recordedRegionalOperators()!.evaluations).toEqual([]);
    expect(await store.loadRecordedRegionalOperators('available')).toBe(true); expect(store.recordedRegionalOperators()!.evaluations).toHaveLength(1);
    expect(store.operatorRecording()).toBeNull(); expect(store.recordedOperators()).toBeNull(); expect(store.result()).toBe(result);
  });
  it('separates original capture gaps from selected view budgets and withdraws bodies on a new run', async () => {
    const store = await readyStore(); selectObserved(store); expect(await store.loadRecordedRegionalOperators('available')).toBe(true);
    const original = store.recordedRegionalOperators()!; const result = store.result();
    expect(await store.loadRecordedRegionalOperators('budget')).toBe(true); expect(store.recordedRegionalOperators()!.state).toBe('BodyBudgetExceeded');
    expect(store.recordedRegionalOperators()!.originalCaptureState).toBe('RecordedRegionalEvaluations');
    const pending = store.loadRegionalOperatorRecording('budget'); expect(store.regionalOperatorSelection()).toBeNull(); expect(store.recordedRegionalOperators()).toBeNull();
    expect(await pending).toBe(true); selectObserved(store); expect(await store.loadRecordedRegionalOperators('available')).toBe(true);
    expect(store.recordedRegionalOperators()!.state).toBe('NoRecordedOperators'); expect(store.regionalOperatorRecording()!.originalCaptureState).toBe('BodyBudgetExceeded');
    expect(original.evaluations).toHaveLength(1); expect(store.result()).toBe(result);
  });
  it('navigates the selected external diagnostic to the exact captured Source version', async () => {
    const store = await readyStore(); selectObserved(store); const selected = store.regionalOperatorSelection()!;
    expect(store.navigateRegionalOperatorDiagnostic()).toBe(true); expect(store.scopedSourceTarget()!.location).toBe(selected.diagnostic.primaryLocation);
    expect(store.scopedSourceTarget()!.region.context).toEqual(selected.region.context); expect(store.regionalOperatorSelection()).toBe(selected);
    expect(await store.loadRecordedRegionalOperators('available')).toBe(true);
  });
  it('refuses cloned diagnostic members and the other captured version', async () => {
    const store = await readyStore(); selectObserved(store); const selected = store.regionalOperatorSelection()!;
    const foreignRegion = store.regionalOperatorRecording()!.regions.find(r => r !== selected.region && r.diagnostics.length)!;
    expect(store.selectRegionalOperatorDiagnostic(selected.region, { ...selected.diagnostic })).toBe(false);
    expect(store.selectRegionalOperatorDiagnostic(foreignRegion, selected.diagnostic)).toBe(false);
    expect(store.regionalOperatorSelection()).toBeNull(); expect(store.recordedRegionalOperators()).toBeNull();
  });
  it.each(['choose', 'root-source', 'scoped-source', 'clear', 'remove', 'replace'] as const)('refuses an older pending run after lifecycle change: %s', async change => {
    const store = await readyStore(), value = store.context()!, pending = store.loadRegionalOperatorRecording('retry');
    if (change === 'choose') store.choose('tests-regional-unknown-base');
    if (change === 'root-source') await store.loadRootSource();
    if (change === 'scoped-source') await store.loadScopedSources();
    if (change === 'clear') store.clear('denied');
    if (change === 'remove') Object.assign(store.bundle()!, { cases: store.bundle()!.cases.filter(v => v !== value) });
    if (change === 'replace') Object.assign(value.regionalOperatorRecordings!, { retry: structuredClone(value.regionalOperatorRecordings!.retry) });
    expect(await pending).toBe(false); expect(store.regionalOperatorRecording()).toBeNull(); expect(store.regionalOperatorSelection()).toBeNull(); expect(store.recordedRegionalOperators()).toBeNull();
  });
  it.each(['selection', 'root-source', 'scoped-source', 'recording', 'clear'] as const)('withdraws a pending selected-body read: %s', async change => {
    const store = await readyStore(); selectObserved(store); const pending = store.loadRecordedRegionalOperators('available');
    if (change === 'selection') store.clearSelection();
    if (change === 'root-source') await store.loadRootSource();
    if (change === 'scoped-source') await store.loadScopedSources();
    if (change === 'recording') await store.loadRegionalOperatorRecording('original');
    if (change === 'clear') store.clear('unavailable');
    expect(await pending).toBe(false); expect(store.recordedRegionalOperators()).toBeNull();
  });
  it('keeps newer selected view state when an older body read resolves afterward', async () => {
    const store = await readyStore(); selectObserved(store);
    const old = store.loadRecordedRegionalOperators('available'), newer = store.loadRecordedRegionalOperators('budget');
    expect(await old).toBe(false); expect(await newer).toBe(true); expect(store.recordedRegionalOperators()!.choice).toBe('budget');
    expect(store.recordedRegionalOperatorReadState()).toBe('ready');
  });
  it('keeps the newer host pair when an older original hash operation finishes last', async () => {
    const store = await readyStore(); let release!: () => void, held: Promise<void> | null = null, first = true;
    vi.stubGlobal('crypto', { subtle: { digest: async (...args: Parameters<typeof webcrypto.subtle.digest>) => {
      const result = await webcrypto.subtle.digest(...args);
      if (first) { first = false; held = new Promise<void>(resolve => release = resolve); await held; } return result;
    } } });
    try {
      const older = store.loadRegionalOperatorRecording('original'); await vi.waitFor(() => expect(held).not.toBeNull());
      expect(await store.loadRegionalOperatorRecording('retry')).toBe(true); const newer = store.regionalOperatorRecording()!;
      release(); expect(await older).toBe(false); expect(store.regionalOperatorRecording()).toBe(newer); expect(newer.label).toBe('retry');
      expect(store.regionalOperatorRecordingReadState()).toBe('ready');
    } finally { release?.(); }
  });
  it('publishes cancellation as host metadata without Compiler result or a selectable region', async () => {
    const store = await readyStore(); selectObserved(store); expect(await store.loadRecordedRegionalOperators('available')).toBe(true);
    expect(await store.loadRegionalOperatorRecording('cancelled')).toBe(true); expect(store.regionalOperatorRecording()!.hostStatus).toBe('Cancelled');
    expect(store.regionalOperatorRecording()!.regions).toEqual([]); expect(store.regionalOperatorRecording()!.observation).toBeNull();
    expect(store.regionalOperatorSelection()).toBeNull(); expect(await store.loadRecordedRegionalOperators()).toBe(false);
  });
  it('withdraws metadata and bodies when another case reuses the run identity', async () => {
    const store = await readyStore(); selectObserved(store); expect(await store.loadRecordedRegionalOperators('available')).toBe(true);
    const run = store.regionalOperatorRecording()!, foreign = store.bundle()!.cases[0].regionalOperatorRecordings!.original.manifest;
    const data = JSON.parse(foreign.text); data.runId = run.runId; Object.assign(foreign, { text: JSON.stringify(data) });
    expect(store.regionalOperatorRecording()).toBeNull(); expect(store.regionalOperatorSelection()).toBeNull(); expect(store.recordedRegionalOperators()).toBeNull();
  });
});
