/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/pass-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
async function readyStore(key = 'fail'): Promise<ProjectIdeWorkbenchStore> {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose(key); store.loadResult();
  expect(await store.loadRootSource()).toBe(true); expect(await store.loadPassRecording('diagnostic')).toBe(true); return store;
}
describe('Recorded pass store lifecycle', () => {
  it('withdraws visible bodies on every new metadata read and keeps the original capture gap separate', async () => {
    const store = await readyStore(); const result = store.result(); store.selectDiagnostic(result!.result.diagnostics[0]);
    expect(await store.loadRecordedPasses('available')).toBe(true); const old = store.recordedPasses()!;
    const pending = store.loadPassRecording('budget'); expect(store.recordedPasses()).toBeNull();
    expect(await pending).toBe(true); expect(store.passRecording()!.originalCaptureState).toBe('BodyBudgetExceeded');
    expect(await store.loadRecordedPasses('available')).toBe(true); expect(store.recordedPasses()!.state).toBe('NoRecordedOriginalBodies');
    expect(store.result()).toBe(result); expect(old.bodies.length).toBeGreaterThan(0);
  });
  it.each(['choose', 'source', 'clear', 'remove', 'replace'] as const)('never republishes an old pending run after lifecycle change: %s', async change => {
    const store = await readyStore(); const v = store.context()!; const pending = store.loadPassRecording('retry');
    if (change === 'choose') store.choose('unknown');
    if (change === 'source') await store.loadRootSource();
    if (change === 'clear') store.clear('denied');
    if (change === 'remove') Object.assign(store.bundle()!, { cases: store.bundle()!.cases.filter(item => item !== v) });
    if (change === 'replace') Object.assign(v.passRecordings!, { retry: structuredClone(v.passRecordings!.retry) });
    expect(await pending).toBe(false); expect(store.passRecording()).toBeNull(); expect(store.recordedPasses()).toBeNull();
  });
  it('keeps a newer metadata selection when an older digest operation finishes last', async () => {
    const store = await readyStore(); const originalCrypto = globalThis.crypto; let release!: (buffer: ArrayBuffer) => void;
    let held: Promise<ArrayBuffer> | null = null; let first = true;
    vi.stubGlobal('crypto', { subtle: { digest: async (...args: Parameters<typeof webcrypto.subtle.digest>) => {
      const result = await webcrypto.subtle.digest(...args);
      if (first) { first = false; held = new Promise<ArrayBuffer>(resolve => release = resolve); await held; } return result;
    } } });
    try {
      const older = store.loadPassRecording('original'); await vi.waitFor(() => expect(held).not.toBeNull());
      expect(await store.loadPassRecording('retry')).toBe(true); const newer = store.passRecording()!;
      release(new ArrayBuffer(0)); expect(await older).toBe(false); expect(store.passRecording()).toBe(newer); expect(newer.label).toBe('retry');
    } finally { vi.stubGlobal('crypto', originalCrypto); release?.(new ArrayBuffer(0)); }
  });
  it.each(['source', 'recording', 'clear'] as const)('withdraws a pending selected-body read: %s', async change => {
    const store = await readyStore(); store.selectDiagnostic(store.result()!.result.diagnostics[0]);
    const pending = store.loadRecordedPasses('available');
    if (change === 'source') await store.loadRootSource();
    if (change === 'recording') await store.loadPassRecording('original');
    if (change === 'clear') store.clear('unavailable');
    expect(await pending).toBe(false); expect(store.recordedPasses()).toBeNull();
  });
});

describe('Pass global identity and default selection', () => {
  it('requires explicit Source then metadata and keeps a whole pass view across diagnostic selections', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('operator-nested-duplicates'); store.loadResult();
    expect(await store.loadPassRecording()).toBe(false); expect(await store.loadRootSource()).toBe(true);
    expect(await store.loadPassRecording()).toBe(true); expect(store.passRecording()!.originalCaptureState).toBe('ManifestOnly');
    expect(await store.loadRecordedPasses('available')).toBe(true); expect(store.recordedPasses()!.state).toBe('NoRecordedOriginalBodies');
    expect(await store.loadPassRecording('diagnostic')).toBe(true);
    expect(await store.loadRecordedPasses()).toBe(true); expect(store.recordedPasses()!.bodies).toHaveLength(0);
    expect(await store.loadRecordedPasses('available')).toBe(true); const current = store.recordedPasses()!; expect(current.bodies.length).toBeGreaterThan(0);
    store.selectDiagnostic(store.result()!.result.diagnostics[0]); expect(store.recordedPasses()).toBe(current);
    store.selectDiagnostic({...store.result()!.result.diagnostics[0]}); expect(store.recordedPasses()).toBe(current);
    expect(store.predicateRecording()).toBeNull(); expect(store.recordedPredicatePremises()).toBeNull();
  });
  it('withdraws current views if another member reuses a run identity', async () => {
    const store = await readyStore(); store.selectDiagnostic(store.result()!.result.diagnostics[0]); expect(await store.loadRecordedPasses('available')).toBe(true);
    const current = store.passRecording()!; const foreign = store.bundle()!.cases.find(v => v.key === 'unknown')!;
    const frame = foreign.passRecordings!.original.manifest; const data = JSON.parse(frame.text); data.runId = current.runId;
    Object.assign(frame, { text: JSON.stringify(data) });
    expect(store.passRecording()).toBeNull(); expect(store.recordedPasses()).toBeNull();
  });
});

describe('Invalid pass run feedback', () => {
  it.each(['foreign-member', 'duplicate-run', 'missing-manifest'] as const)('reports an unavailable read while keeping Source and result intact: %s', async kind => {
    const data = bundle(); const value = data.cases.find(v => v.key === 'fail')!; const projection = value.passRecordings!;
    if (kind === 'foreign-member') Object.assign(projection, { diagnostic: data.cases.find(v => v.key === 'unknown')!.passRecordings!.diagnostic });
    if (kind === 'duplicate-run') {
      const manifest = JSON.parse(projection.diagnostic.manifest.text); manifest.runId = JSON.parse(projection.original.manifest.text).runId;
      Object.assign(projection.diagnostic.manifest, { text: JSON.stringify(manifest) });
    }
    if (kind === 'missing-manifest') Object.assign(projection.retry, { manifest: null });
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => data); store.choose('fail'); store.loadResult();
    const result = store.result(); expect(await store.loadRootSource()).toBe(true); const source = store.rootSource();
    expect(await store.loadPassRecording('diagnostic')).toBe(false); expect(store.passRecordingReadState()).toBe('unavailable');
    expect(store.passRecording()).toBeNull(); expect(store.recordedPasses()).toBeNull();
    expect(store.result()).toBe(result); expect(store.rootSource()).toBe(source);
  });
});
