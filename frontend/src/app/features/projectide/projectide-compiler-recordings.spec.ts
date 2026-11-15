import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources } from './projectide-scoped-sources';
import { CompilerRecordingLabel, compilerRecordingMatches, readCompilerRecording } from './projectide-compiler-recordings.reader';
vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/compiler-recordings-v1.json'), 'utf8'));
const bundle = (family = 'root') => readPreviewBundle({ ...structuredClone(original), cases: structuredClone(original.cases.filter((v: { key: string }) => v.key.startsWith('tests-publication-' + family + '-'))) });
const value = () => bundle().cases[0];
const hash = (format: string, text: string) => ({ algorithm: 'sha-256' as const, value: createHash('sha256').update(format + '\0' + text).digest('hex') });
function rewrite(frame: { text: string; digest: unknown }, edit: (data: any) => void) { const data = JSON.parse(frame.text); edit(data); frame.text = JSON.stringify(data); frame.digest = hash(data.format, frame.text); }
function manifest(v: ReturnType<typeof value>, edit: (data: any) => void) {
  const f = v.compilerRecordings!.diagnostic; rewrite(f.manifest, edit); rewrite(f.receipt, d => d.manifestDigest = f.manifest.digest);
}
describe('Observed actual host recording fixture admission', () => {
  it.each(['root', 'true', 'false', 'unknown'])('preserves original records and result axes: %s', async family => {
    for (const v of bundle(family).cases) {
      const sources = (await readScopedSources(v))!; const ids = new Set<string>();
      for (const label of ['original', 'diagnostic', 'budget', 'cancelled'] as const) {
        const result = (await readCompilerRecording(v, sources, label))!; ids.add(result.runId);
        expect(compilerRecordingMatches(v, sources, result)).toBe(true); expect(Object.isFrozen(result)).toBe(true);
        expect(result.hostStatus).toBe(label === 'cancelled' ? 'Cancelled' : 'Completed');
        if (label === 'cancelled') expect(result.output).toBeNull();
        else { expect(result.output!.state).toBe(label === 'original' ? 'ManifestOnly' : label === 'diagnostic' ? 'CapturedAvailableOutputs' : 'BodyBudgetExceeded');
          expect(result.output!.bodies).toHaveLength(label === 'diagnostic' ? 3 : 0); expect(result.output!.data['result']).toEqual(JSON.parse(v.compilerOutputs!.manifest.text).result); }
        expect(result.originalRunId).toBe(label === 'original' ? null : JSON.parse(v.compilerRecordings!.original.manifest.text).runId);
        expect(JSON.parse(result.receipt.text).authority).toBe('NotEvaluated'); expect(JSON.parse(result.receipt.text).retentionPolicy).toBe('NotAdopted');
      }
      expect(ids.size).toBe(4); expect(Object.isFrozen(v.context)).toBe(false);
    }
  });
  it.each(['format','mode','budget','source','input','context','compiler','execution-input','materializations','generation','origin','lineage','session','run','authority','retention','earlier','extra'])(
    'rejects a rehashed false declaration: %s', async name => {
      const v = value(); manifest(v, d => {
        if(name==='format')d.format+='x';if(name==='mode')d.capture.mode='Manifest';if(name==='budget')d.capture.maximumBodyBytes=1;
        if(name==='source')d.sourceDigest.value='b'.repeat(64);if(name==='input')d.inputDigest.value='b'.repeat(64);if(name==='context')d.context.revisionId='00000099-0000-4000-8000-000000000001';
        if(name==='compiler')d.compiler.version='99';if(name==='execution-input')d.executionInputDigest.value='b'.repeat(64);if(name==='materializations')d.materializationsDigest.value='b'.repeat(64);
        if(name==='generation')d.clientGeneration++;if(name==='origin')d.origin.attemptId='00000099-0000-4000-8000-000000000001';if(name==='lineage')d.knownLineage=[];
        if(name==='session')d.sessionId='00000099-0000-4000-8000-000000000001';if(name==='run')d.runId=JSON.parse(v.compilerRecordings!.original.manifest.text).runId;
        if(name==='authority')d.authority='Allowed';if(name==='retention')d.retentionPolicy='Approved';if(name==='earlier')d.capture.earlierIntermediateStates='FullDump';if(name==='extra')d.extra=true;
      }); await expect(readCompilerRecording(v, (await readScopedSources(v))!, 'diagnostic')).rejects.toThrow();
    });
  it.each(['attempt','host-execution','capture','result','authority','trust','eligibility','retention','earlier','state','extra'])(
    'rejects a rehashed false receipt: %s', async name => { const v = value(); rewrite(v.compilerRecordings!.diagnostic.receipt, d => {
      if(name==='attempt')d.attemptId='00000099-0000-4000-8000-000000000001';if(name==='host-execution')d.hostExecutionDigest.value='b'.repeat(64);if(name==='capture')d.captureDigest.value='b'.repeat(64);if(name==='result')d.resultDigest.value='b'.repeat(64);
      if(name==='authority')d.authority='Allowed';if(name==='trust')d.producerTrust='Trusted';if(name==='eligibility')d.eligibility='Eligible';if(name==='retention')d.retentionPolicy='Approved';if(name==='earlier')d.earlierIntermediateStates='FullDump';if(name==='state')d.recordingState='OriginalFailureRecovered';if(name==='extra')d.extra=true;
    }); await expect(readCompilerRecording(v, (await readScopedSources(v))!, 'diagnostic')).rejects.toThrow(); });
  it.each(['body','duplicate','drop','cancel-result','host-status','host-input','host-generation','host-result','package-extra'])(
    'refuses altered produced data or host outcome: %s', async name => { const v = value(); const p = v.compilerRecordings!; const f = p.diagnostic;
      if(name==='body')Object.assign(f.snapshot!.bodies[0], { text: f.snapshot!.bodies[0].text + ' ' });
      if(name==='duplicate')Object.assign(f.snapshot!, { bodies: [f.snapshot!.bodies[0], f.snapshot!.bodies[0], f.snapshot!.bodies[2]] });
      if(name==='drop')Object.assign(f.snapshot!, { bodies: [] });if(name==='cancel-result')Object.assign(p.cancelled.host, { result: v.result });
      if(name==='host-status')Object.assign(f.host, { status: 'Cancelled' });if(name==='host-input')Object.assign(f.host, { requestedInputDigest: { algorithm: 'sha-256', value: 'b'.repeat(64) } });
      if(name==='host-generation')Object.assign(f.host, { clientGeneration: 99 });if(name==='host-result')Object.assign(f.host, { result: { ...v.result, verdict: 'coglatas.fail' } });if(name==='package-extra')Object.assign(p, { extra: true });
      await expect(readCompilerRecording(v, (await readScopedSources(v))!, name==='cancel-result'?'cancelled':'diagnostic')).rejects.toThrow();
    });
  it('withdraws replaced or mutated package ownership and keeps captured IDs unchanged', async () => {
    const v = value(); const sources = (await readScopedSources(v))!; const admitted = (await readCompilerRecording(v, sources))!;
    const ids = [admitted.runId, admitted.attemptId]; expect(compilerRecordingMatches(v, sources, admitted)).toBe(true);
    Object.assign(v, { compilerRecordings: structuredClone(v.compilerRecordings) }); expect(compilerRecordingMatches(v, sources, admitted)).toBe(false);
    const next = (await readCompilerRecording(v, sources))!; expect([next.runId,next.attemptId]).toEqual(ids);
    Object.assign(v.compilerRecordings!.original.receipt, { text: v.compilerRecordings!.original.receipt.text + ' ' }); expect(compilerRecordingMatches(v, sources, next)).toBe(false);
  });
  it('refuses foreign Source admission, unknown selection and missing package without inventing a record', async () => {
    const a = bundle('root').cases[0], b = bundle('true').cases[0]; const admitted = (await readScopedSources(a))!;
    await expect(readCompilerRecording(b, admitted)).rejects.toThrow(); await expect(readCompilerRecording(a, admitted, 'other' as CompilerRecordingLabel)).rejects.toThrow();
    Object.assign(a, { compilerRecordings: null }); await expect(readCompilerRecording(a, admitted)).rejects.toThrow(); expect(await readCompilerRecording(a, (await readScopedSources(a))!)).toBeNull();
  });
  it('rejects duplicate JSON keys and overlarge metadata before admission', async () => {
    const v = value(); const f = v.compilerRecordings!.diagnostic.manifest; const d = JSON.parse(f.text); Object.assign(f, { text: f.text.replace('"runId":', '"runId":"'+d.runId+'","runId":') }); Object.assign(f, { digest: hash(d.format,f.text) });
    await expect(readCompilerRecording(v,(await readScopedSources(v))!,'diagnostic')).rejects.toThrow();
    const huge=value(); Object.assign(huge.compilerRecordings!.diagnostic.manifest,{text:'x'.repeat(4_194_305)}); await expect(readCompilerRecording(huge,(await readScopedSources(huge))!)).rejects.toThrow();
  });
});

async function readyRecordingStore() { const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-publication-root-base'); store.loadResult(); expect(await store.loadScopedSources()).toBe(true); return store; }
describe('Owned recorded invocation lifecycle', () => {
  it('requires Source admission and keeps separate diagnostic/cancelled outcomes', async () => {
    const store=new ProjectIdeWorkbenchStore(); await store.load(async()=>bundle()); store.choose('tests-publication-root-base'); store.loadResult();
    expect(await store.loadCompilerRecording()).toBe(false); expect(await store.loadScopedSources()).toBe(true); const result=store.result();
    expect(await store.loadCompilerRecording()).toBe(true); const original=store.compilerRecording()!; expect(original.output!.bodies).toHaveLength(0);
    expect(await store.loadCompilerRecording('diagnostic')).toBe(true); const diagnostic=store.compilerRecording()!; expect(diagnostic.runId).not.toBe(original.runId); expect(diagnostic.attemptId).not.toBe(original.attemptId); expect(diagnostic.originalRunId).toBe(original.runId); expect(diagnostic.output!.bodies).toHaveLength(3);
    expect(await store.loadCompilerRecording('cancelled')).toBe(true); expect(store.compilerRecording()!.output).toBeNull(); expect(store.result()).toBe(result);
  });
  it.each(['choose','result','sources','denied','reload'])('withdraws pending and published records: %s', async mode => {
    const store=await readyRecordingStore(); expect(await store.loadCompilerRecording('diagnostic')).toBe(true); const old=store.compilerRecording()!;
    const pending=store.loadCompilerRecording(); expect(store.compilerRecording()).toBeNull();
    if(mode==='choose')store.choose('tests-publication-root-hypothesis'); if(mode==='result')store.loadResult(); if(mode==='sources')await store.loadScopedSources(); if(mode==='denied')store.clear('denied'); if(mode==='reload')await store.load(async()=>bundle());
    expect(await pending).toBe(false); expect(store.compilerRecording()).toBeNull(); expect(store.compilerRecordingReadState()).toBe('idle'); expect(old.output!.bodies).toHaveLength(3);
  });
  it.each(['frame','removed','same-key-replacement'])('withdraws records on mutation or lost ownership: %s', async mode => {
    const store=await readyRecordingStore(); expect(await store.loadCompilerRecording('diagnostic')).toBe(true); const v=store.context()!; const b=store.bundle()!;
    if(mode==='frame')Object.assign(v.compilerRecordings!.diagnostic.receipt,{text:v.compilerRecordings!.diagnostic.receipt.text+' '});
    if(mode==='removed')Object.assign(b,{cases:b.cases.filter(x=>x!==v)}); if(mode==='same-key-replacement')Object.assign(b,{cases:b.cases.map(x=>x===v?structuredClone(v):x)});
    expect(store.compilerRecording()).toBeNull();
  });
  it('lets the newer selected recording own publication', async()=>{const store=await readyRecordingStore();const old=store.loadCompilerRecording('diagnostic');const current=store.loadCompilerRecording('cancelled');expect(await old).toBe(false);expect(await current).toBe(true);expect(store.compilerRecording()!.hostStatus).toBe('Cancelled');expect(store.compilerRecording()!.output).toBeNull();});
  it('keeps recording and separate output reads independently visible',async()=>{const store=await readyRecordingStore();expect(await store.loadCompilerRecording('diagnostic')).toBe(true);const record=store.compilerRecording();expect(await store.loadCompilerOutputs()).toBe(true);expect(store.compilerRecording()).toBe(record);const output=store.compilerOutputs();expect(await store.loadCompilerRecording('budget')).toBe(true);expect(store.compilerOutputs()).toBe(output);});
  it('does not publish modified record bytes during asynchronous verification',async()=>{const store=await readyRecordingStore();const pending=store.loadCompilerRecording('diagnostic');const f=store.context()!.compilerRecordings!.diagnostic.receipt;Object.assign(f,{text:f.text+' '});expect(await pending).toBe(false);expect(store.compilerRecording()).toBeNull();expect(store.compilerRecordingReadState()).toBe('unavailable');});
});
