import { sameContext } from './projectide.contracts';
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources } from './projectide-scoped-sources';
import { readSemanticIndex, semanticIndexMatches } from './projectide-semantic-index.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());
const raw = () => JSON.parse(readFileSync(resolve('public/assets/projectide/semantic-index-v1.json'), 'utf8'));
const value = () => readPreviewBundle(raw()).cases[0];
const ready = async () => { const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-index-base'); store.loadResult();
  expect(await store.loadScopedSources()).toBe(true); expect(await store.loadSemanticIndex()).toBe(true); return store; };
const rehash = (row: ReturnType<typeof value>) => { const frame = row.semanticIndex!.index;
  (frame.digest as { value: string }).value = createHash('sha256').update('coglatas.semantic-index/1\0' + frame.text).digest('hex'); };

describe('Whole admitted binding index and Core-prepared queries', () => {
  const rawLargeBundle = () => JSON.parse(readFileSync(resolve('public/assets/projectide/semantic-index-large-v1.json'), 'utf8'));
  const boundedBundle = () => {
    const data = rawLargeBundle(); return readPreviewBundle({ ...data, cases: data.cases.filter((r: { key: string }) => r.key === 'tests-index-bounded-base') });
  };
  it('rejects a complete Core index at the earlier missing scoped transport binding', async () => {
    const raw = rawLargeBundle(); expect(raw.cases).toHaveLength(2); const row = raw.cases.find((r: { key: string }) => r.key === 'tests-index-large-base');
    const data = JSON.parse(row.semanticIndex.index.text); expect(data.state).toBe('Complete'); expect(data.indexCoverageComplete).toBe(true);
    expect(data.symbols.length * data.references.length).toBeGreaterThan(1_000_000); expect(row.scopedSources).toBeUndefined();
    expect(() => readPreviewBundle(raw)).toThrow('wrong-scope');
    const wideOnly = { ...raw, cases: [row] }; expect(() => readPreviewBundle(wideOnly)).toThrow('wrong-scope');
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(wideOnly));
    expect(store.state()).toBe('wrong-scope'); expect(await store.loadSemanticIndex()).toBe(false);
    expect(store.semanticIndex()).toBeNull(); expect(store.selectedIndexedSymbol()).toBeNull();
    expect(store.indexedReferenceQuery()).toBeNull(); expect(store.scopedSourceTarget()).toBeNull();
  });
  it('admits original bounded Core pages including an empty 256-reference continuation', async () => {
    const row = boundedBundle().cases[0];
    const source = await readScopedSources(row); expect(source).not.toBeNull();
    const index = await readSemanticIndex(row, source!); expect(index).not.toBeNull(); expect(index!.references.length).toBeGreaterThan(256);
    const group = index!.projection.referenceSearches.find(g => g.pages[0].referenceIndices.length === 0 && g.pages[0].scannedReferences === 256 && g.pages.some(p => p.referenceIndices.length > 0))!;
    expect(group).toBeDefined(); expect(group.pages.at(-1)!.scanComplete).toBe(true);
    expect(group.pages.every(p => p.scannedReferences <= 256 && p.referenceIndices.length <= 32)).toBe(true);
    expect(group.pages.flatMap(p => p.referenceIndices).map(i => index!.references[i])).toEqual(index!.references.filter(r => r.symbolKey === group.targetKey));
  });
  it.each(['omitted-empty-page', 'foreign-scan-member'])('refuses altered bounded %s after original Source admission', async mode => {
    const row = boundedBundle().cases[0];
    const projection = row.semanticIndex! as any;
    const group = projection.referenceSearches.find((g: any) => g.pages[0].referenceIndices.length === 0 && g.pages[0].scannedReferences === 256 && g.pages.some((p: any) => p.referenceIndices.length > 0));
    expect(group).toBeDefined();
    if (mode === 'omitted-empty-page') group.pages.shift();
    else group.pages[0].referenceIndices = [...group.pages.find((p: any) => p.referenceIndices.length > 0).referenceIndices];
    const source = await readScopedSources(row); expect(source).not.toBeNull();
    await expect(readSemanticIndex(row, source!)).rejects.toThrow();
  });
  it('retains exact contexts, original queries and Unicode names across committed, hypothetical and Draft roots', async () => {
    for (const row of readPreviewBundle(raw()).cases.slice(0, 3)) {
      const source = (await readScopedSources(row))!; const index = (await readSemanticIndex(row, source))!;
      expect(index).not.toBeNull(); expect(source.regions).toHaveLength(3); expect(index.indexCoverageComplete).toBe(true);
      const data = JSON.parse(index.projection.index.text); expect(index.symbols).toEqual(data.symbols); expect(index.references).toEqual(data.references);
      expect(index.symbols.some(s => s.displayName?.includes('日本語'))).toBe(true);
      const versions = index.symbols.filter(s => s.kind === 'coglatas.project'); expect(new Set(versions.map(s => s.key)).size).toBe(versions.length);
      expect(index.projection.definitions.every((d, i) => d.referenceIndex === i && d.targetKey === index.references[i].symbolKey)).toBe(true);
      expect(semanticIndexMatches(row, source, index)).toBe(true); expect(semanticIndexMatches({ ...row }, source, index)).toBe(false);
      expect(semanticIndexMatches(row, source, { ...index })).toBe(false); expect(Object.isFrozen(index.symbols)).toBe(true);
      expect(row.semanticInspection!.references.length).toBeLessThanOrEqual(index.references.length);
    }
  });
  it('keeps incomplete captured closure unavailable without claiming a complete index', async () => {
    for (const row of readPreviewBundle(raw()).cases.slice(3)) {
      expect(JSON.parse(row.semanticIndex!.index.text).state).toBe('Incomplete');
      expect(row.semanticIndex!.referenceSearches).toEqual([]); expect(await readScopedSources(row)).toBeNull();
      const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose(row.key); store.loadResult();
      expect(await store.loadScopedSources()).toBe(false); expect(await store.loadSemanticIndex()).toBe(false); expect(store.semanticIndex()).toBeNull();
    }
  });
  it.each(['format', 'result', 'hash', 'text', 'definition', 'reference-membership', 'scan', 'completion-kind', 'completion-membership', 'unknown-field', 'duplicate-key', 'rehash-kind'])(
    'refuses altered %s independently of successful Source admission', async mode => {
      const row = value(); const projection = row.semanticIndex! as any;
      if (mode === 'format') projection.format += '-future';
      if (mode === 'result') projection.resultDigest.value = 'f'.repeat(64);
      if (mode === 'hash') projection.index.digest.value = 'f'.repeat(64);
      if (mode === 'text') projection.index.text += ' ';
      if (mode === 'definition') projection.definitions[0].targetKey = projection.definitions[1].targetKey;
      if (mode === 'reference-membership') projection.referenceSearches.find((g: any) => g.pages.some((p: any) => p.referenceIndices.length)).pages[0].referenceIndices[0] = 999999;
      if (mode === 'scan') projection.referenceSearches[0].pages[0].scannedReferences++;
      if (mode === 'completion-kind') projection.completions[0].pages[0].expectedKind = 'coglatas.future';
      if (mode === 'completion-membership') projection.completions.find((g: any) => g.pages.some((p: any) => p.symbolKeys.length)).pages[0].symbolKeys.push(projection.completions[0].originKey);
      if (mode === 'unknown-field') projection.authority = true;
      if (mode === 'duplicate-key') { projection.index.text = projection.index.text.replace('{', '{"format":"duplicate",'); rehash(row); }
      if (mode === 'rehash-kind') { projection.index.text = projection.index.text.replace('"kind":"coglatas.project"', '"kind":"example.unknown"'); rehash(row); }
      const source = await readScopedSources(row); expect(source).not.toBeNull();
      await expect(readSemanticIndex(row, source!)).rejects.toThrow();
    });
  it('withdraws a retained admission after any case mutation or Source replacement', async () => {
    const row = value(); const source = (await readScopedSources(row))!; const index = (await readSemanticIndex(row, source))!;
    const fresh = (await readScopedSources(row))!; expect(semanticIndexMatches(row, fresh, index)).toBe(false);
    (row as any).projectName += ' changed'; expect(semanticIndexMatches(row, source, index)).toBe(false); expect(await readSemanticIndex(row, source)).toBeNull();
  });
  it('adopts both real reference pages and rejects copied, old-page and foreign query members', async () => {
    const store = await ready(); const index = store.semanticIndex()!;
    const group = index.projection.referenceSearches.find(g => g.pages.length > 1)!; expect(group).toBeDefined();
    const symbol = index.symbols.find(s => s.key === group.targetKey)!;
    expect(store.selectIndexedSymbol({ ...symbol })).toBe(false); expect(store.selectIndexedSymbol(symbol)).toBe(true);
    expect(store.findIndexedReferences(symbol)).toBe(true); const first = store.indexedReferenceQuery()!;
    const firstRows = store.indexedReferenceRows(first); expect(firstRows).toHaveLength(32); expect(first.pages[0].scanComplete).toBe(false);
    expect(store.navigateIndexedReference({ ...first }, firstRows[0])).toBe(false);
    expect(store.navigateIndexedReference(first, { ...firstRows[0] })).toBe(false);
    expect(store.navigateIndexedReference(first, firstRows[0], true)).toBe(true); expect(store.scopedSourceTarget()).not.toBeNull();
    expect(store.moveIndexedReferences(first, 1)).toBe(true); const second = store.indexedReferenceQuery()!;
    expect(store.scopedSourceTarget()).toBeNull(); expect(store.indexedReferenceRows(first)).toEqual([]);
    expect(store.navigateIndexedReference(second, firstRows[0])).toBe(false); expect(store.moveIndexedReferences(first, 1)).toBe(false);
    expect(store.indexedReferenceRows(second).length).toBeGreaterThan(0); expect(second.pages[second.at].scanComplete).toBe(true);
    expect([...firstRows, ...store.indexedReferenceRows(second)]).toEqual(index.references.filter(r => r.symbolKey === symbol.key));
    store.clear('denied'); expect(store.semanticIndex()).toBeNull(); expect(store.indexedReferenceQuery()).toBeNull();
    expect(store.navigateIndexedReference(second, index.references[0])).toBe(false);
  });
  it('preserves typed candidate identity and original regional document navigation', async () => {
    const store = await ready(); const index = store.semanticIndex()!;
    const port = index.symbols.find(s => s.declaredType?.['kind'] === 'coglatas.entity-reference')!;
    expect(store.selectIndexedSymbol(port)).toBe(true); expect(store.completeIndexedReference(port)).toBe(true);
    const query = store.indexedCompletionQuery()!; const candidates = store.indexedCompletionRows(query); expect(candidates).toEqual(index.symbols.filter(s => !s.isDocument && s.declaredType === null && s.kind === 'coglatas.project'));
    expect(store.navigateIndexedCandidate({ ...query }, candidates[0])).toBe(false);
    expect(store.navigateIndexedCandidate(query, { ...candidates[0] })).toBe(false);
    expect(store.navigateIndexedCandidate(query, candidates[0])).toBe(true);
    expect(store.scopedSourceTarget()!.region.context).toEqual(candidates[0].location.context);
    const document = index.symbols.find(s => s.isDocument)!; expect(store.selectIndexedSymbol(document)).toBe(true);
    expect(store.indexedCompletionQuery()).toBeNull(); expect(store.navigateIndexedSymbol(document)).toBe(true);
    expect(store.scopedSourceTarget()!.location).toBe(document.location); expect(store.scopedSourceTarget()!.record.location.documentId).toBe(document.location.documentId);
  });
  it('requires explicit original Source admission and current object membership', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-index-base'); store.loadResult();
    expect(await store.loadSemanticIndex()).toBe(false); expect(store.semanticIndex()).toBeNull();
    expect(await store.loadScopedSources()).toBe(true); expect(await store.loadSemanticIndex()).toBe(true);
    const index = store.semanticIndex()!; expect(store.moveSemanticSymbols(0.5)).toBe(false);
    expect(store.selectIndexedSymbol(index.symbols[0])).toBe(true); store.choose('tests-index-hypothesis');
    expect(store.semanticIndex()).toBeNull(); expect(store.selectedIndexedSymbol()).toBeNull();
    expect(store.selectIndexedSymbol(index.symbols[0])).toBe(false);
  });
  it.each(['denied', 'context', 'cancelled', 'newer-ready', 'newer-refused'])(
    'fences a pending original-byte read after %s', async mode => {
      const store = await ready(); const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void;
      const gate = new Promise<void>(resolve => release = resolve);
      vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (...args) => { await gate; return original(...args); });
      const controller = new AbortController(); const pending = store.loadSemanticIndex(controller.signal);
      if (mode === 'denied') store.clear('denied');
      if (mode === 'context') store.choose('tests-index-hypothesis');
      if (mode === 'cancelled') controller.abort();
      if (mode === 'newer-ready') expect(await store.loadSemanticIndex()).toBe(true);
      if (mode === 'newer-refused') { await store.loadScopedSources(); store.clear('denied'); expect(await store.loadSemanticIndex()).toBe(false); }
      const newer = store.semanticIndex(); release(); expect(await pending).toBe(false);
      expect(store.semanticIndex()).toBe(newer); if (mode !== 'newer-ready') expect(newer).toBeNull();
    });

  it.each(['fulfilled', 'rejected'])('finishes the owning read after retained input mutation and %s digest', async mode => {
    const store = await ready(); const row = store.context()!; const name = row.projectName;
    const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void;
    const gate = new Promise<void>(resolve => release = resolve);
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (...args) => {
      await gate; if (mode === 'rejected') throw new Error('private digest failure'); return original(...args);
    });
    const pending = store.loadSemanticIndex(); expect(store.semanticIndexReadState()).toBe('loading');
    (row as { projectName: string }).projectName += ' changed'; release();
    expect(await pending).toBe(false); expect(store.semanticIndexReadState()).toBe('unavailable');
    expect(store.semanticIndex()).toBeNull(); expect(store.selectedIndexedSymbol()).toBeNull();
    expect(store.indexedReferenceQuery()).toBeNull(); expect(store.scopedSourceTarget()).toBeNull();
    (row as { projectName: string }).projectName = name;
    expect(await store.loadScopedSources()).toBe(true); expect(await store.loadSemanticIndex()).toBe(true);
    expect(store.semanticIndexReadState()).toBe('ready');
  });

  it('opens an actual dependency-owned relation reference and its exact Core definition', async () => {
    const store = await ready(); const index = store.semanticIndex()!;
    const reference = index.references.find(r => !sameContext(r.origin.context, store.context()!.context))!;
    expect(reference).toBeDefined(); expect(reference.origin.relationId).toBeDefined();
    const target = index.symbols.find(s => s.key === reference.symbolKey)!;
    expect(store.selectIndexedSymbol(target)).toBe(true); expect(store.findIndexedReferences(target)).toBe(true);
    let query = store.indexedReferenceQuery()!;
    while (!store.indexedReferenceRows(query).includes(reference)) { expect(store.moveIndexedReferences(query, 1)).toBe(true); query = store.indexedReferenceQuery()!; }
    expect(store.navigateIndexedReference(query, reference)).toBe(true);
    expect(store.scopedSourceTarget()!.record.recordType).toBe('relation'); expect(store.scopedSourceTarget()!.location).toBe(reference.origin);
    expect(store.navigateIndexedReference(query, reference, true)).toBe(true);
    expect(store.scopedSourceTarget()!.location).toBe(target.location); expect(store.scopedSourceTarget()!.region.context).toEqual(reference.origin.context);
  });

});
