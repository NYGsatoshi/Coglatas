/// <reference types="node" />
import {readFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {webcrypto} from 'node:crypto';
import {vi} from 'vitest';
import {readPreviewBundle} from './projectide-preview.adapter';
import {ProjectIdeWorkbenchStore} from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/pass-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
async function readyStore() {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('fail'); store.loadResult();
  expect(await store.loadRootSource()).toBe(true); expect(await store.loadPassRecording('diagnostic')).toBe(true); return store;
}
describe('Selected pass boundary lifecycle', () => {
  it('defaults to metadata and keeps original and viewer gaps separate without a diagnostic', async () => {
    const store = await readyStore(); const result = store.result(); expect(store.diagnostic()).toBeNull();
    expect(await store.loadRecordedPassBoundary(0)).toBe(true); expect(store.recordedPassBoundary()!.state).toBe('ManifestOnly');
    expect(store.recordedPassBoundary()!.bodies).toHaveLength(0);
    expect(await store.loadRecordedPassBoundary(0, 'available')).toBe(true); const retained = store.recordedPassBoundary()!;
    expect(retained.bodies[0].text).toBe(store.context()!.sourceText);
    store.selectDiagnostic(result!.result.diagnostics[0]); expect(store.recordedPassBoundary()).toBe(retained);
    expect(await store.loadRecordedPassBoundary(0, 'budget')).toBe(true); expect(store.recordedPassBoundary()!.state).toBe('BodyBudgetExceeded');
    expect(store.recordedPassBoundary()!.bodies).toHaveLength(0); expect(retained.bodies).toHaveLength(1);
    expect(await store.loadPassRecording('budget')).toBe(true); expect(store.recordedPassBoundary()).toBeNull();
    expect(await store.loadRecordedPassBoundary(0, 'available')).toBe(true); expect(store.recordedPassBoundary()!.state).toBe('NoRecordedOriginalBodies');
    expect(store.result()).toBe(result);
  });
  it.each(['choose','source','recording','clear','remove','replace'] as const)('does not publish a pending selected read after %s', async action => {
    const store = await readyStore(); const value = store.context()!; const pending = store.loadRecordedPassBoundary(0, 'available');
    if(action === 'choose') store.choose('unknown');
    if(action === 'source') await store.loadRootSource();
    if(action === 'recording') await store.loadPassRecording('retry');
    if(action === 'clear') store.clear('denied');
    if(action === 'remove') Object.assign(store.bundle()!,{cases:store.bundle()!.cases.filter(item => item !== value)});
    if(action === 'replace') Object.assign(value.passRecordings!,{diagnostic:structuredClone(value.passRecordings!.diagnostic)});
    expect(await pending).toBe(false); expect(store.recordedPassBoundary()).toBeNull();
  });
  it('keeps only the latest boundary when real local selections overlap', async () => {
    const store = await readyStore();
    const older = store.loadRecordedPassBoundary(0,'available');
    const newer = store.loadRecordedPassBoundary(1,'available');
    expect(await older).toBe(false); expect(await newer).toBe(true);
    expect(store.recordedPassBoundary()!.ordinal).toBe(1);
    expect(store.recordedPassBoundary()!.originalBoundary!.ordinal).toBe(1);
    expect(store.recordedPassBoundaryReadState()).toBe('ready');
  });
  it('clears selected detail before a whole-root read or invalid selection and keeps caller bytes', async () => {
    const store = await readyStore(); expect(await store.loadRecordedPassBoundary(0,'available')).toBe(true);
    const retained = store.recordedPassBoundary()!;
    expect(await store.loadRecordedPasses()).toBe(true); expect(store.recordedPassBoundary()).toBeNull();
    expect(await store.loadRecordedPassBoundary(-1,'available')).toBe(false); expect(store.recordedPassBoundaryReadState()).toBe('unavailable');
    expect(store.recordedPassBoundary()).toBeNull(); expect(retained.bodies).toHaveLength(1);
    expect(await store.loadRecordedPassBoundary(2_147_483_647)).toBe(false);
    expect(store.recordedPassBoundary()!.state).toBe('BoundaryNotRecorded');
  });
  it('withdraws views on a duplicate identity outside the selected case and refuses a cancelled host body', async () => {
    const store = await readyStore(); expect(await store.loadRecordedPassBoundary(0,'available')).toBe(true);
    const run = store.passRecording()!; const foreign = store.bundle()!.cases.find(value => value.key === 'unknown')!;
    const frame = foreign.passRecordings!.original.manifest; const manifest = JSON.parse(frame.text); manifest.runId = run.runId;
    Object.assign(frame,{text:JSON.stringify(manifest)}); expect(store.recordedPassBoundary()).toBeNull();
    const cancelled = await readyStore(); expect(await cancelled.loadPassRecording('cancelled')).toBe(true);
    expect(await cancelled.loadRecordedPassBoundary(0,'available')).toBe(false); expect(cancelled.recordedPassBoundary()).toBeNull();
  });
});
