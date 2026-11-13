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
  it('displays the exact Core pair and opens each original snapshot', async () => {
    const store = await ready(); const prepared = store.semanticDiff()!;
    expect(prepared.comparison.state).toBe('Complete'); expect(prepared.comparison.noChanges).toBe(false);
    const change = prepared.comparison.changes[0]; expect(change.kind).toBe('DisplayName');
    expect(change.before).toBe('Observe 🌌'); expect(change.after).toBe('Observe renamed 🌌');
    expect(store.navigateDiff(change, 'before')).toBe(true);
    expect(store.context()?.key).toBe('draft-before'); expect(store.sourceLocation()?.context).toEqual(prepared.comparison.beforeContext);
    expect(store.navigateDiff(change, 'after')).toBe(true);
    expect(store.context()?.key).toBe('draft-after'); expect(store.sourceLocation()?.context).toEqual(prepared.comparison.afterContext);
    expect(store.semanticDiff()).toBeNull(); expect(store.navigateDiff(change, 'before')).toBe(false);
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
    expect(store.navigateDiff(change, 'after')).toBe(false);
  });
  it('clears paired bodies on context, load and denial boundaries', async () => {
    const store = await ready(); const change = store.semanticDiff()!.comparison.changes[0];
    store.choose('pass'); expect(store.semanticDiff()).toBeNull(); expect(store.navigateDiff(change, 'after')).toBe(false);
    store.choose('draft-before'); expect(store.semanticDiff()).toBeNull(); store.loadResult(); expect(store.semanticDiff()).not.toBeNull();
    store.clear('denied'); expect(store.semanticDiff()).toBeNull(); expect(store.bundle()).toBeNull(); expect(store.navigateDiff(change, 'after')).toBe(false);
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
    expect(store.navigateDiff(change, 'after')).toBe(false); expect(store.state()).toBe('unsupported');
  });
});
