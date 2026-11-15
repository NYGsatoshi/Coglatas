/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readRootSource } from './projectide-root-source.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { readPredicateRecording, readRecordedPredicatePremises, predicateRecordingMatches, recordedPredicatePremisesMatch } from './projectide-predicate-recordings.reader';

vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/predicate-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
const keys = ['fail', 'pass', 'unknown', 'test-assertion-navigation'];
describe('Actual original predicate recording members', () => {
  it.each(keys)('reads every actual run and only selected original arrays: %s', async key => {
    const value = bundle().cases.find(value => value.key === key)!; const result = value.result;
    const sources = (await readRootSource(value))!;
    const ids = new Set<string>(); const attempts = new Set<string>();
    for (const label of ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const) {
      const recording = (await readPredicateRecording(value, sources, label))!;
      expect(predicateRecordingMatches(value, sources, recording)).toBe(true);
      expect(Object.isFrozen(recording)).toBe(true); expect('bodies' in recording).toBe(false); expect('frame' in recording).toBe(false);
      ids.add(recording.runId); attempts.add(recording.attemptId);
      expect(recording.manifest.text).toBe(value.predicateRecordings![label].manifest.text);
      expect(recording.receipt.text).toBe(value.predicateRecordings![label].receipt.text);
      expect(recording.hostStatus).toBe(label === 'cancelled' ? 'Cancelled' : 'Completed');
      for (const diagnostic of result.diagnostics) {
        const manifest = await readRecordedPredicatePremises(value, sources, recording, diagnostic);
        if (label === 'cancelled') { expect(manifest).toBeNull(); continue; }
        expect(manifest!.mode).toBe('Manifest'); expect(manifest!.bodies).toHaveLength(0);
        const available = (await readRecordedPredicatePremises(value, sources, recording, diagnostic, 'available'))!;
        expect(recordedPredicatePremisesMatch(value, sources, recording, diagnostic, available)).toBe(true);
        expect(available.bodies).toHaveLength(key === 'fail' && (label === 'diagnostic' || label === 'retry') ? 1 : 0);
        for (const body of available.bodies) {
          expect(diagnostic.evidence.premises.some(premise => premise.value === body.digest.value)).toBe(true);
          const originalBody = value.predicateRecordings![label].bodies.find(item => item.digest.value === body.digest.value)!;
          expect(body.text).toBe(originalBody.text); expect(body.bytes).toBe(new TextEncoder().encode(body.text).length);
        }
        const limited = (await readRecordedPredicatePremises(value, sources, recording, diagnostic, 'budget'))!;
        expect(limited.bodies).toHaveLength(0);
        expect(limited.originalCaptureState).toBe(recording.originalCaptureState);
        if (available.bodies.length) expect(limited.state).toBe('BodyBudgetExceeded');
      }
      expect(value.result).toBe(result);
    }
    expect(ids.size).toBe(5); expect(attempts.size).toBe(5);
  });
  it('requires Source and run admission and keeps result bytes separate from metadata selection', async () => {
    const store = new ProjectIdeWorkbenchStore();
    await store.load(async () => bundle()); store.choose('fail'); store.loadResult();
    expect(await store.loadPredicateRecording()).toBe(false); expect(await store.loadRootSource()).toBe(true);
    const result = store.result(); expect(await store.loadPredicateRecording()).toBe(true);
    const originalRun = store.predicateRecording()!; expect(originalRun.originalCaptureState).toBe('ManifestOnly');
    expect(await store.loadPredicateRecording('diagnostic')).toBe(true);
    expect(store.predicateRecording()!.runId).not.toBe(originalRun.runId);
    store.selectDiagnostic(store.result()!.result.diagnostics[0]);
    expect(await store.loadRecordedPredicatePremises()).toBe(true); expect(store.recordedPredicatePremises()!.bodies).toHaveLength(0);
    expect(await store.loadRecordedPredicatePremises('available')).toBe(true); expect(store.recordedPredicatePremises()!.bodies).toHaveLength(1);
    expect(await store.loadPredicateRecording('cancelled')).toBe(true); expect(store.recordedPredicatePremises()).toBeNull();
    expect(store.predicateRecording()!.hostStatus).toBe('Cancelled'); expect(await store.loadRecordedPredicatePremises('available')).toBe(false);
    expect(store.result()).toBe(result); expect(result!.result.verdict).toBe('coglatas.fail');
  });
});

const hash = (domain: string, text: string) => ({ algorithm: 'sha-256' as const, value: createHash('sha256').update(domain + String.fromCharCode(0)).update(text, 'utf8').digest('hex') });
const value = (key = 'fail') => bundle().cases.find(item => item.key === key)!;
function rewrite(frame: { text: string; digest: unknown }, edit: (data: any) => void, domain?: string): void {
  const data = JSON.parse(frame.text); edit(data); frame.text = JSON.stringify(data); frame.digest = hash(domain ?? data.format, frame.text);
}
/** Rebind only digest references after adversarial edits; preserve their false semantic fields for the actual reader to reject. */
function rebind(v: ReturnType<typeof value>): void {
  const projection = v.predicateRecordings!;
  const parent = { diagnostic: 'original', budget: 'diagnostic', retry: 'cancelled' } as const;
  for (const label of ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const) {
    const f = projection[label];
    Object.assign(f.manifest, { digest: hash('coglatas.local-predicate-compiler-recording-manifest/1', f.manifest.text) });
    Object.assign(f.response, { digest: hash('coglatas.local-predicate-compiler-host-response/1', f.response.text) });
    rewrite(f.receipt, data => { data.manifestDigest = f.manifest.digest; data.hostResponseDigest = f.response.digest; });
    for (const [child, ancestor] of Object.entries(parent)) {
      if (ancestor !== label) continue;
      const target = projection[child as keyof typeof parent];
      rewrite(target.manifest, data => {
        if (data.origin) { data.origin.manifestDigest = f.manifest.digest; data.origin.receiptDigest = f.receipt.digest; }
      });
    }
  }
}
async function sources(v: ReturnType<typeof value>) { return (await readRootSource(v))!; }
async function readyStore(key = 'fail') {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose(key); store.loadResult();
  expect(await store.loadRootSource()).toBe(true); expect(await store.loadPredicateRecording('diagnostic')).toBe(true); return store;
}
describe('Original predicate member refusal and freshness', () => {
  it.each(['request', 'policy', 'baseExecution', 'materializations', 'manifest', 'response', 'receipt', 'result', 'predicate'] as const)(
    'rejects altered original bytes without hash normalization: %s', async name => {
      const v = value(); Object.assign(v.predicateRecordings!.diagnostic[name]!, { text: v.predicateRecordings!.diagnostic[name]!.text + ' ' });
      await expect(readPredicateRecording(v, await sources(v))).rejects.toThrow();
    });
  it.each(['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const)('verifies unselected member metadata eagerly: %s', async label => {
    const v = value(); const f = v.predicateRecordings![label].receipt; Object.assign(f, { text: f.text + ' ' });
    await expect(readPredicateRecording(v, await sources(v), 'original')).rejects.toThrow();
  });
  it.each(['fail', 'pass'])('verifies original body bytes even for metadata-only selection: %s', async key => {
    const v = value(key); const body = v.predicateRecordings!.diagnostic.bodies[0];
    Object.assign(body, { text: '[false]' });
    await expect(readPredicateRecording(v, await sources(v), 'original')).rejects.toThrow();
  });
  it.each(['context', 'compiler', 'generation', 'scope', 'session', 'origin', 'lineage'] as const)('rejects rehashed false manifest binding: %s', async name => {
    const v = value(); const f = v.predicateRecordings!.diagnostic;
    rewrite(f.manifest, data => {
      if (name === 'context') data.context.branch.projectId = '00000099-0000-4000-8000-000000000001';
      if (name === 'compiler') data.compiler.version = 'other';
      if (name === 'generation') data.clientGeneration++;
      if (name === 'scope') data.identityScope = 'TrustedRemoteSession';
      if (name === 'session') data.sessionId = '00000099-0000-4000-8000-000000000001';
      if (name === 'origin') data.origin.runId = JSON.parse(v.predicateRecordings!.cancelled.manifest.text).runId;
      if (name === 'lineage') data.knownLineage = [];
    });
    if (name === 'generation') rewrite(f.response, data => data.clientGeneration++);
    rebind(v); await expect(readPredicateRecording(v, await sources(v), 'original')).rejects.toThrow();
  });
  it.each(['attempt', 'authority', 'status', 'reason', 'capture', 'result'] as const)('rejects rehashed false host response: %s', async name => {
    const v = value(); const f = v.predicateRecordings!.diagnostic;
    rewrite(f.response, data => {
      if (name === 'attempt') data.attemptId = JSON.parse(v.predicateRecordings!.original.response.text).attemptId;
      if (name === 'authority') data.authority = 'Approved';
      if (name === 'status') data.hostStatus = 'Cancelled';
      if (name === 'reason') data.reasonCode = 'coglatas.host.completed';
      if (name === 'capture') data.capturePolicyDigest = v.predicateRecordings!.original.policy.digest;
      if (name === 'result') data.resultDigest = v.inputDigest;
    });
    if (name === 'attempt') rewrite(f.receipt, data => data.attemptId = JSON.parse(f.response.text).attemptId);
    rebind(v); await expect(readPredicateRecording(v, await sources(v))).rejects.toThrow();
  });
  it.each(['manifest', 'response', 'policy', 'run', 'predicate', 'state', 'trust'] as const)('rejects rehashed false receipt: %s', async name => {
    const v = value(); const f = v.predicateRecordings!.diagnostic;
    rewrite(f.receipt, data => {
      if (name === 'manifest') data.manifestDigest = v.predicateRecordings!.original.manifest.digest;
      if (name === 'response') data.hostResponseDigest = v.predicateRecordings!.original.response.digest;
      if (name === 'policy') data.capturePolicyDigest = v.predicateRecordings!.original.policy.digest;
      if (name === 'run') data.runId = JSON.parse(v.predicateRecordings!.original.manifest.text).runId;
      if (name === 'predicate') data.predicateRecordDigest = v.predicateRecordings!.original.predicate!.digest;
      if (name === 'state') data.recordingState = 'TrustedOriginalFailure';
      if (name === 'trust') data.producerTrust = 'Approved';
    });
    await expect(readPredicateRecording(v, await sources(v))).rejects.toThrow();
  });
  it.each(['manifest', 'available', 'budget'] as const)('verifies all selected-read metadata modes eagerly: %s', async choice => {
    const v = value(); const f = v.predicateRecordings!.diagnostic.diagnostics[0][choice];
    rewrite(f, data => data.basis.resultDigest = v.inputDigest);
    await expect(readPredicateRecording(v, await sources(v), 'original')).rejects.toThrow();
  });
  it('refuses a whole genuine member substituted for the selected role and a fabricated cancellation body', async () => {
    const v = value(); Object.assign(v.predicateRecordings!, { diagnostic: v.predicateRecordings!.retry });
    await expect(readPredicateRecording(v, await sources(v))).rejects.toThrow();
    const cancelled = value(); Object.assign(cancelled.predicateRecordings!.cancelled, { result: cancelled.predicateRecordings!.diagnostic.result });
    await expect(readPredicateRecording(cancelled, await sources(cancelled))).rejects.toThrow();
  });
  it('refuses duplicate members, rounded metadata numbers, invalid Unicode and oversized frames', async () => {
    const duplicate = value(); const d = duplicate.predicateRecordings!.original.manifest;
    Object.assign(d, { text: d.text.replace('"clientGeneration":7', '"clientGeneration":7,"clientGeneration":7') });
    rebind(duplicate); await expect(readPredicateRecording(duplicate, await sources(duplicate))).rejects.toThrow();
    const rounded = value(); const p = rounded.predicateRecordings!;
    for (const f of [p.original.manifest, p.original.response]) Object.assign(f, { text: f.text.replace('"clientGeneration":7', '"clientGeneration":7.0000000000000001') });
    rebind(rounded); await expect(readPredicateRecording(rounded, await sources(rounded))).rejects.toThrow();
    const unicode = value(); const u = unicode.predicateRecordings!.diagnostic.response;
    rewrite(u, data => data.reasonCode = String.fromCharCode(0xd800)); rebind(unicode);
    await expect(readPredicateRecording(unicode, await sources(unicode))).rejects.toThrow();
    const huge = value(); Object.assign(huge.predicateRecordings!.diagnostic.receipt, { text: 'x'.repeat(4_194_305) });
    await expect(readPredicateRecording(huge, await sources(huge))).rejects.toThrow();
  });
  it('keeps opaque admission, frozen returned frames and exact package/member identity separate from equal digests', async () => {
    const v = value(); const source = await sources(v); const recording = (await readPredicateRecording(v, source, 'diagnostic'))!;
    const diagnostic = v.result.diagnostics[0]; const snapshot = (await readRecordedPredicatePremises(v, source, recording, diagnostic, 'available'))!;
    expect(predicateRecordingMatches(v, source, { ...recording })).toBe(false);
    expect(recordedPredicatePremisesMatch(v, source, recording, diagnostic, { ...snapshot })).toBe(false);
    expect(predicateRecordingMatches(structuredClone(v), source, recording)).toBe(false);
    expect(() => Object.assign(recording.receipt, { text: 'changed' })).toThrow();
    expect(() => (snapshot.bodies as unknown[]).pop()).toThrow();
    const member = v.predicateRecordings!.diagnostic; Object.assign(v.predicateRecordings!, { diagnostic: structuredClone(member) });
    expect(predicateRecordingMatches(v, source, recording)).toBe(false);
    expect(recordedPredicatePremisesMatch(v, source, recording, diagnostic, snapshot)).toBe(false);
    expect(snapshot.bodies[0].text).toBe(member.bodies[0].text);
  });
  it('rejects a member replacement during digest awaits even when all original frame bytes are identical', async () => {
    const v = value(); const source = await sources(v); const pending = readPredicateRecording(v, source, 'diagnostic');
    Object.assign(v.predicateRecordings!, { diagnostic: structuredClone(v.predicateRecordings!.diagnostic) });
    await expect(pending).rejects.toThrow();
  });
  it('refuses same-ID foreign diagnostic objects and no-diagnostic successful arrays', async () => {
    const v = value(); const source = await sources(v); const recording = (await readPredicateRecording(v, source, 'diagnostic'))!;
    await expect(readRecordedPredicatePremises(v, source, recording, structuredClone(v.result.diagnostics[0]), 'available')).rejects.toThrow();
    const pass = value('pass'); const ps = await sources(pass); const pr = (await readPredicateRecording(pass, ps, 'diagnostic'))!;
    expect(pr.originalCaptureState).toBe('CapturedOriginalArrays'); expect(pass.result.diagnostics).toHaveLength(0);
    await expect(readRecordedPredicatePremises(pass, ps, pr, v.result.diagnostics[0], 'available')).rejects.toThrow();
  });
});
describe('Recorded predicate store lifecycle', () => {
  it('withdraws visible bodies on every new metadata read and keeps the original capture gap separate', async () => {
    const store = await readyStore(); const result = store.result(); store.selectDiagnostic(result!.result.diagnostics[0]);
    expect(await store.loadRecordedPredicatePremises('available')).toBe(true); const old = store.recordedPredicatePremises()!;
    const pending = store.loadPredicateRecording('budget'); expect(store.recordedPredicatePremises()).toBeNull();
    expect(await pending).toBe(true); expect(store.predicateRecording()!.originalCaptureState).toBe('BodyBudgetExceeded');
    expect(await store.loadRecordedPredicatePremises('available')).toBe(true); expect(store.recordedPredicatePremises()!.state).toBe('NoAvailableOriginalArrays');
    expect(store.result()).toBe(result); expect(old.bodies).toHaveLength(1);
  });
  it.each(['choose', 'source', 'clear', 'remove', 'replace'] as const)('never republishes an old pending run after lifecycle change: %s', async change => {
    const store = await readyStore(); const v = store.context()!; const pending = store.loadPredicateRecording('retry');
    if (change === 'choose') store.choose('unknown');
    if (change === 'source') await store.loadRootSource();
    if (change === 'clear') store.clear('denied');
    if (change === 'remove') Object.assign(store.bundle()!, { cases: store.bundle()!.cases.filter(item => item !== v) });
    if (change === 'replace') Object.assign(v.predicateRecordings!, { retry: structuredClone(v.predicateRecordings!.retry) });
    expect(await pending).toBe(false); expect(store.predicateRecording()).toBeNull(); expect(store.recordedPredicatePremises()).toBeNull();
  });
  it('keeps a newer metadata selection when an older digest operation finishes last', async () => {
    const store = await readyStore(); const originalCrypto = globalThis.crypto; let release!: (buffer: ArrayBuffer) => void;
    let held: Promise<ArrayBuffer> | null = null; let first = true;
    vi.stubGlobal('crypto', { subtle: { digest: async (...args: Parameters<typeof webcrypto.subtle.digest>) => {
      const result = await webcrypto.subtle.digest(...args);
      if (first) { first = false; held = new Promise<ArrayBuffer>(resolve => release = resolve); await held; } return result;
    } } });
    try {
      const older = store.loadPredicateRecording('original'); await vi.waitFor(() => expect(held).not.toBeNull());
      expect(await store.loadPredicateRecording('retry')).toBe(true); const newer = store.predicateRecording()!;
      release(new ArrayBuffer(0)); expect(await older).toBe(false); expect(store.predicateRecording()).toBe(newer); expect(newer.label).toBe('retry');
    } finally { vi.stubGlobal('crypto', originalCrypto); release?.(new ArrayBuffer(0)); }
  });
  it.each(['selection', 'source', 'recording', 'clear'] as const)('withdraws a pending selected-body read: %s', async change => {
    const store = await readyStore(); store.selectDiagnostic(store.result()!.result.diagnostics[0]);
    const pending = store.loadRecordedPredicatePremises('available');
    if (change === 'selection') store.selectDiagnostic({ ...store.result()!.result.diagnostics[0] });
    if (change === 'source') await store.loadRootSource();
    if (change === 'recording') await store.loadPredicateRecording('original');
    if (change === 'clear') store.clear('unavailable');
    expect(await pending).toBe(false); expect(store.recordedPredicatePremises()).toBeNull();
  });
});
