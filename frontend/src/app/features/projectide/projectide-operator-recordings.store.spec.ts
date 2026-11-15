/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/operator-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
async function readyStore(key = 'fail'): Promise<ProjectIdeWorkbenchStore> {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose(key); store.loadResult();
  expect(await store.loadRootSource()).toBe(true); expect(await store.loadOperatorRecording('diagnostic')).toBe(true); return store;
}
describe('Recorded operator store lifecycle', () => {
  it('withdraws visible bodies on every new metadata read and keeps the original capture gap separate', async () => {
    const store = await readyStore(); const result = store.result(); store.selectDiagnostic(result!.result.diagnostics[0]);
    expect(await store.loadRecordedOperators('available')).toBe(true); const old = store.recordedOperators()!;
    const pending = store.loadOperatorRecording('budget'); expect(store.recordedOperators()).toBeNull();
    expect(await pending).toBe(true); expect(store.operatorRecording()!.originalCaptureState).toBe('BodyBudgetExceeded');
    expect(await store.loadRecordedOperators('available')).toBe(true); expect(store.recordedOperators()!.state).toBe('NoRecordedOperators');
    expect(store.result()).toBe(result); expect(old.evaluations).toHaveLength(1);
  });
  it.each(['choose', 'source', 'clear', 'remove', 'replace'] as const)('never republishes an old pending run after lifecycle change: %s', async change => {
    const store = await readyStore(); const v = store.context()!; const pending = store.loadOperatorRecording('retry');
    if (change === 'choose') store.choose('unknown');
    if (change === 'source') await store.loadRootSource();
    if (change === 'clear') store.clear('denied');
    if (change === 'remove') Object.assign(store.bundle()!, { cases: store.bundle()!.cases.filter(item => item !== v) });
    if (change === 'replace') Object.assign(v.operatorRecordings!, { retry: structuredClone(v.operatorRecordings!.retry) });
    expect(await pending).toBe(false); expect(store.operatorRecording()).toBeNull(); expect(store.recordedOperators()).toBeNull();
  });
  it('keeps a newer metadata selection when an older digest operation finishes last', async () => {
    const store = await readyStore(); const originalCrypto = globalThis.crypto; let release!: (buffer: ArrayBuffer) => void;
    let held: Promise<ArrayBuffer> | null = null; let first = true;
    vi.stubGlobal('crypto', { subtle: { digest: async (...args: Parameters<typeof webcrypto.subtle.digest>) => {
      const result = await webcrypto.subtle.digest(...args);
      if (first) { first = false; held = new Promise<ArrayBuffer>(resolve => release = resolve); await held; } return result;
    } } });
    try {
      const older = store.loadOperatorRecording('original'); await vi.waitFor(() => expect(held).not.toBeNull());
      expect(await store.loadOperatorRecording('retry')).toBe(true); const newer = store.operatorRecording()!;
      release(new ArrayBuffer(0)); expect(await older).toBe(false); expect(store.operatorRecording()).toBe(newer); expect(newer.label).toBe('retry');
    } finally { vi.stubGlobal('crypto', originalCrypto); release?.(new ArrayBuffer(0)); }
  });
  it.each(['selection', 'source', 'recording', 'clear'] as const)('withdraws a pending selected-body read: %s', async change => {
    const store = await readyStore(); store.selectDiagnostic(store.result()!.result.diagnostics[0]);
    const pending = store.loadRecordedOperators('available');
    if (change === 'selection') store.selectDiagnostic({ ...store.result()!.result.diagnostics[0] });
    if (change === 'source') await store.loadRootSource();
    if (change === 'recording') await store.loadOperatorRecording('original');
    if (change === 'clear') store.clear('unavailable');
    expect(await pending).toBe(false); expect(store.recordedOperators()).toBeNull();
  });
});

describe('Operator global identity and default selection', () => {
  it('requires explicit Source then metadata then actual diagnostic and keeps existing array reads separate', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('operator-nested-duplicates'); store.loadResult();
    expect(await store.loadOperatorRecording()).toBe(false); expect(await store.loadRootSource()).toBe(true);
    expect(await store.loadOperatorRecording()).toBe(true); expect(store.operatorRecording()!.originalCaptureState).toBe('ManifestOnly');
    expect(await store.loadRecordedOperators('available')).toBe(false); expect(store.recordedOperators()).toBeNull();
    expect(await store.loadOperatorRecording('diagnostic')).toBe(true); store.selectDiagnostic(store.result()!.result.diagnostics[0]);
    expect(await store.loadRecordedOperators()).toBe(true); expect(store.recordedOperators()!.evaluations).toHaveLength(0);
    expect(await store.loadRecordedOperators('available')).toBe(true); expect(store.recordedOperators()!.evaluations[0].nodes).toHaveLength(6);
    expect(store.predicateRecording()).toBeNull(); expect(store.recordedPredicatePremises()).toBeNull();
  });
  it('withdraws current views if another member reuses a run identity', async () => {
    const store = await readyStore(); store.selectDiagnostic(store.result()!.result.diagnostics[0]); expect(await store.loadRecordedOperators('available')).toBe(true);
    const current = store.operatorRecording()!; const foreign = store.bundle()!.cases.find(v => v.key === 'unknown')!;
    const frame = foreign.operatorRecordings!.original.manifest; const data = JSON.parse(frame.text); data.runId = current.runId;
    Object.assign(frame, { text: JSON.stringify(data) });
    expect(store.operatorRecording()).toBeNull(); expect(store.recordedOperators()).toBeNull();
  });
});
