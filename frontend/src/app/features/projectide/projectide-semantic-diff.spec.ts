/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

const raw = () => JSON.parse(readFileSync('public/assets/projectide/preview-v1.json', 'utf8'));
async function ready(key = 'draft-before') {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => raw());
  store.choose(key); store.loadResult(); return store;
}

describe('Core-prepared semantic differences', () => {
  for (const mode of ['added', 'removed', 'changed'] as const) {
    it(`preserves an opaque Source documentId member independently from entity ownership: ${mode}`, () => {
      const bundle = raw(); const before = bundle.cases.find((item: { key: string }) => item.key === 'document-before');
      const after = bundle.cases.find((item: { key: string }) => item.key === 'document-after');
      const diff = before.preparedDiff.comparison; const original = diff.changes[0];
      if (mode !== 'added') before.source.documentId = 'Opaque old hint';
      if (mode !== 'removed') after.source.documentId = 'Opaque new hint';
      const change = { ...original, recordType: 'source', identity: '', path: '/documentId', beforeLocation: null, afterLocation: null,
        beforePresent: mode !== 'added', afterPresent: mode !== 'removed', before: before.source.documentId ?? null, after: after.source.documentId ?? null };
      diff.changes = [change]; expect(() => readPreviewBundle(bundle)).not.toThrow();
      if (change.afterPresent) change.after = 'Forged hint'; else change.before = 'Forged hint';
      expect(() => readPreviewBundle(bundle)).toThrow();
    });
  }
  it('reads the real Core-generated opaque envelopes without claiming semantic equality or a compiler pass', async () => {
    const store = await ready('envelope-before'); const diff = store.semanticDiff()!.comparison;
    expect(diff.state).toBe('Incomplete'); expect(diff.noChanges).toBe(false); expect(diff.changes).toHaveLength(5);
    expect(diff.changes.every(row => row.kind === 'Uninterpreted')).toBe(true);
    expect(diff.changes.some(row => row.path === '/future~1field~0' && row.before === 'Source original' && row.after === 'Source changed')).toBe(true);
    expect(diff.changes.some(row => row.recordType === 'source' && row.path === '/documentId' && row.before === 'Opaque Source hint original' && row.after === 'Opaque Source hint changed')).toBe(true);
    const manifest = diff.changes.find(row => row.path === '/manifest')!;
    expect(JSON.stringify(manifest.before)).toContain('Manifest Digest original');
    expect(JSON.stringify(manifest.after)).toContain('Manifest entry changed');
    expect(store.context()?.semanticInspection?.bindingComplete).toBe(false);
    expect(store.context()?.result.verdict).not.toBe('coglatas.pass');
  });
  for (const surface of ['source', 'document', 'document-digest', 'manifest'] as const) {
    it(`admits an uninterpreted original envelope value and rejects a forged value: ${surface}`, () => {
      const bundle = raw(); const before = bundle.cases.find((item: { key: string }) => item.key === 'document-before');
      const after = bundle.cases.find((item: { key: string }) => item.key === 'document-after');
      const diff = before.preparedDiff.comparison; const original = structuredClone(diff.changes[0]);
      const document = surface === 'document' || surface === 'document-digest';
      const path = surface === 'manifest' ? '/manifest' : surface === 'document-digest' ? '/digest' : '/future~1field~0';
      const field = surface === 'manifest' ? 'manifest' : surface === 'document-digest' ? 'digest' : 'future/field~';
      const oldEnvelope = document ? before.source.documents[0] : before.source;
      const newEnvelope = document ? after.source.documents[0] : after.source;
      oldEnvelope[field] = { opaque: 'original' }; newEnvelope[field] = { opaque: 'changed' };
      const change = { ...original, recordType: document ? 'document' : 'source', identity: document ? original.identity : '', path,
        beforePresent: true, afterPresent: true, before: structuredClone(oldEnvelope[field]), after: structuredClone(newEnvelope[field]),
        beforeLocation: document ? original.beforeLocation : null, afterLocation: document ? original.afterLocation : null };
      diff.changes = [change]; expect(() => readPreviewBundle(bundle)).not.toThrow();
      change.after = { opaque: 'forged' }; expect(() => readPreviewBundle(bundle)).toThrow();
    });
  }
  it('displays the exact Core pair and opens each original snapshot', async () => {
    const store = await ready(); const prepared = store.semanticDiff()!;
    expect(prepared.comparison.state).toBe('Complete'); expect(prepared.comparison.noChanges).toBe(false);
    const change = prepared.comparison.changes[0]; expect(change.kind).toBe('DisplayName');
    expect(change.before).toBe('Observe 🌌'); expect(change.after).toBe('Observe renamed 🌌');
    expect(await store.navigateDiff(change, 'before')).toBe(true);
    expect(store.context()?.key).toBe('draft-before'); expect(store.sourceLocation()?.context).toEqual(prepared.comparison.beforeContext);
    expect(await store.navigateDiff(change, 'after')).toBe(true);
    expect(store.context()?.key).toBe('draft-after'); expect(store.sourceLocation()?.context).toEqual(prepared.comparison.afterContext);
    expect(store.semanticDiff()).toBeNull(); expect(await store.navigateDiff(change, 'before')).toBe(false);
  });
  it('separates structural equality from a compiler failure and incomplete coverage', async () => {
    const store = await ready('fail'); expect(store.context()?.result.verdict).toBe('coglatas.fail');
    expect(store.semanticDiff()?.comparison.noChanges).toBe(true);
    store.choose('unsupported'); store.loadResult();
    expect(store.semanticDiff()?.comparison.state).toBe('Incomplete');
    expect(store.semanticDiff()?.comparison.changes).toHaveLength(0); expect(store.semanticDiff()?.comparison.noChanges).toBe(false);
  });
  it('retains Core-generated opaque document changes with exact before/after documents', async () => {
    const store = await ready('document-before'); const diff = store.semanticDiff()!.comparison;
    expect(diff.state).toBe('Incomplete'); expect(diff.noChanges).toBe(false); expect(diff.changes).toHaveLength(1);
    const change = diff.changes[0]; expect(change.recordType).toBe('document'); expect(change.kind).toBe('Uninterpreted');
    expect(change.path).toBe('/extensions'); expect(change.beforePresent).toBe(false); expect(change.afterPresent).toBe(true);
    expect(change.after).toEqual({ annotation: 'Opaque document annotation; no inferred layout semantics' });
    expect(change.beforeLocation?.context).toEqual(diff.beforeContext); expect(change.afterLocation?.context).toEqual(diff.afterContext);
    expect(await store.navigateDiff(change, 'after')).toBe(false);
  });
  it('clears paired bodies on context, load and denial boundaries', async () => {
    const store = await ready(); const change = store.semanticDiff()!.comparison.changes[0];
    store.choose('pass'); expect(store.semanticDiff()).toBeNull(); expect(await store.navigateDiff(change, 'after')).toBe(false);
    store.choose('draft-before'); expect(store.semanticDiff()).toBeNull(); store.loadResult(); expect(store.semanticDiff()).not.toBeNull();
    store.clear('denied'); expect(store.semanticDiff()).toBeNull(); expect(store.bundle()).toBeNull(); expect(await store.navigateDiff(change, 'after')).toBe(false);
  });
  for (const mode of ['digest', 'context', 'target', 'value', 'location', 'identity', 'duplicate', 'empty-claim', 'kind', 'format', 'path', 'rows'] as const) {
    it(`refuses a malformed or mixed projection: ${mode}`, () => {
      const bundle = raw(); const before = bundle.cases.find((item: { key: string }) => item.key === 'draft-before');
      const prepared = before.preparedDiff; const diff = prepared.comparison; const change = diff.changes[0];
      switch (mode) {
        case 'digest': diff.afterInputDigest.value = 'a'.repeat(64); break;
        case 'context': diff.beforeContext.candidateRevisionId = diff.afterContext.candidateRevisionId; break;
        case 'target': prepared.afterCaseKey = 'other-project'; break;
        case 'value': change.after = 'Forged display name'; break;
        case 'location': change.afterLocation.context = diff.beforeContext; break;
        case 'identity': change.identity = 'other'; break;
        case 'duplicate': diff.changes.push(structuredClone(change)); break;
        case 'empty-claim': diff.noChanges = true; break;
        case 'kind': change.kind = 'Impact'; break;
        case 'format': diff.format = 'coglatas.future/2'; break;
        case 'path': change.path = '/displayName~invalid'; break;
        case 'rows': diff.changes = Array.from({ length: 1025 }, () => structuredClone(change)); break;
      }
      expect(() => readPreviewBundle(bundle)).toThrow();
    });
  }
  it('revalidates the pair at navigation instead of trusting an old row handle', async () => {
    const store = await ready(); const prepared = store.semanticDiff()!; const change = prepared.comparison.changes[0];
    Object.assign(prepared.comparison, { afterInputDigest: { algorithm: 'sha-256', value: 'a'.repeat(64) } });
    expect(await store.navigateDiff(change, 'after')).toBe(false); expect(store.state()).toBe('unsupported');
  });
});
