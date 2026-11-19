/// <reference types="node" />
import { createHash, webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { PreviewCase, sameContext } from './projectide.contracts';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources } from './projectide-scoped-sources';
import { AdmittedSemanticIndex, readSemanticIndex, semanticIndexMatches } from './projectide-semantic-index.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());

// Original producer bytes are data, not Product-owned test implementations.
const asset = (large = false) => JSON.parse(readFileSync(resolve('public/assets/projectide/',
  large ? 'semantic-index-large-v1.json' : 'semantic-index-v1.json'), 'utf8'));
const originalBundle = () => readPreviewBundle(asset());
const one = () => originalBundle().cases.find(row => row.key === 'tests-index-base')!;
const bounded = () => {
  const raw = asset(true);
  return readPreviewBundle({ ...raw, cases: raw.cases.filter((row: { key: string }) => row.key === 'tests-index-bounded-base') }).cases[0];
};
const parsedIndex = (row: PreviewCase) => JSON.parse(row.semanticIndex!.index.text) as Record<string, unknown>;
const rehash = (row: PreviewCase, text: string) => Object.assign(row.semanticIndex!.index, {
  text, digest: { algorithm: 'sha-256', value: createHash('sha256').update('coglatas.semantic-index/1\0' + text).digest('hex') }
});
async function admit(row: PreviewCase): Promise<AdmittedSemanticIndex> {
  const sources = await readScopedSources(row);
  expect(sources).not.toBeNull();
  const index = await readSemanticIndex(row, sources!);
  expect(index).not.toBeNull();
  return index!;
}
async function refuse(row: PreviewCase, reason = 'unsupported-semantic-index'): Promise<void> {
  // Acquire fresh Source admission after the derivative, so failure cannot be
  // explained merely by the fingerprint changing after Source admission.
  const sources = await readScopedSources(row);
  expect(sources).not.toBeNull();
  await expect(readSemanticIndex(row, sources!)).rejects.toThrow(reason);
}
async function storeWithIndex(): Promise<ProjectIdeWorkbenchStore> {
  const store = new ProjectIdeWorkbenchStore();
  await store.load(async () => originalBundle());
  store.choose('tests-index-base'); store.loadResult();
  expect(await store.loadScopedSources()).toBe(true);
  expect(await store.loadSemanticIndex()).toBe(true);
  return store;
}
function manyReferences(index: AdmittedSemanticIndex) {
  const group = index.projection.referenceSearches.find(group => group.pages.length > 1 && group.pages[0].referenceIndices.length === 32);
  expect(group).toBeDefined();
  return index.symbols.find(symbol => symbol.key === group!.targetKey)!;
}
function delayedHash() {
  let release!: () => void;
  let entered = false;
  const blocker = new Promise<void>(resume => { release = resume; });
  const actual = crypto.subtle.digest.bind(crypto.subtle);
  const spy = vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, bytes) => {
    entered = true; await blocker; return actual(algorithm, bytes);
  });
  return { release, assertEntered: () => expect(entered).toBe(true), restore: () => spy.mockRestore() };
}
function absent(store: ProjectIdeWorkbenchStore) {
  expect(store.semanticIndex()).toBeNull(); expect(store.selectedIndexedSymbol()).toBeNull();
  expect(store.indexedReferenceQuery()).toBeNull(); expect(store.indexedCompletionQuery()).toBeNull();
  expect(store.scopedSourceTarget()).toBeNull();
}

describe('Independent semantic index reader and ownership candidates', () => {
  it('binds genuine committed Scenario and Draft indexes to their unchanged original regional bytes', async () => {
    const bundle = originalBundle();
    const identities = new Set<string>();
    for (const key of ['tests-index-base', 'tests-index-hypothesis', 'tests-index-draft']) {
      const row = bundle.cases.find(value => value.key === key)!;
      const original = JSON.stringify(row);
      const index = await admit(row); const data = parsedIndex(row);
      expect(index.symbols).toHaveLength(48); expect(index.references).toHaveLength(40);
      expect(index.symbols).toEqual(data['symbols']); expect(index.references).toEqual(data['references']);
      expect(index.indexCoverageComplete).toBe(true); expect(Object.isFrozen(index)).toBe(true);
      expect(index.projection.index.text).toBe(row.semanticIndex!.index.text);
      expect(index.symbols.some(symbol => !sameContext(symbol.location.context, row.context))).toBe(true);
      expect(index.symbols.some(symbol => symbol.displayName?.includes('日本語'))).toBe(true);
      identities.add(JSON.stringify(row.context)); expect(JSON.stringify(row)).toBe(original);
    }
    expect(identities.size).toBe(3);
  });

  it('keeps every incomplete original closure unavailable to the store', async () => {
    for (const key of ['tests-index-partial-base', 'tests-index-partial-hypothesis', 'tests-index-partial-draft']) {
      const store = new ProjectIdeWorkbenchStore(); await store.load(async () => originalBundle());
      store.choose(key); store.loadResult();
      expect(parsedIndex(store.context()!)['state']).toBe('Incomplete');
      expect(await store.loadScopedSources()).toBe(false); expect(await store.loadSemanticIndex()).toBe(false);
      absent(store);
    }
  });

  it('refuses the original wide Core index at its earlier scoped Source transport boundary', async () => {
    const raw = asset(true); const wide = raw.cases.find((row: { key: string }) => row.key === 'tests-index-large-base');
    const data = JSON.parse(wide.semanticIndex.index.text);
    expect(data.symbols).toHaveLength(1005); expect(data.references).toHaveLength(1001);
    expect(data.state).toBe('Complete'); expect(wide.scopedSources).toBeUndefined();
    const store = new ProjectIdeWorkbenchStore();
    await store.load(async () => readPreviewBundle({ ...raw, cases: [wide] }));
    expect(store.state()).toBe('wrong-scope'); expect(await store.loadSemanticIndex()).toBe(false); absent(store);
  });

  it('rejects a rehashed display-name substitution despite fresh original Source admission', async () => {
    const row = one(); const data = parsedIndex(row);
    const symbols = data['symbols'] as { displayName: string | null }[];
    const named = symbols.find(symbol => symbol.displayName !== null)!;
    named.displayName = 'independent substituted private name';
    rehash(row, JSON.stringify(data)); await refuse(row);
  });

  it('rejects substituted result input and binding digests independently of a correct frame hash', async () => {
    for (const dimension of ['result', 'input', 'binding']) {
      const row = one();
      if (dimension === 'result') Object.assign(row.semanticIndex!, { resultDigest: { algorithm: 'sha-256', value: 'e'.repeat(64) } });
      else {
        const data = parsedIndex(row);
        if (dimension === 'binding') data['bindingClosureDigest'] = { algorithm: 'sha-256', value: 'e'.repeat(64) };
        else {
          data['rootInputDigest'] = { algorithm: 'sha-256', value: 'e'.repeat(64) };
        }
        rehash(row, JSON.stringify(data));
      }
      await refuse(row);
    }
  });

  it('rejects noncanonical original text even when its domain hash is recomputed', async () => {
    const row = one(); rehash(row, row.semanticIndex!.index.text + '\n'); await refuse(row);
  });

  it('rejects duplicate JSON members even when their original-byte hash is correct', async () => {
    const row = one(); rehash(row, '{"state":"Complete",' + row.semanticIndex!.index.text.slice(1)); await refuse(row, 'unsupported-local-test-projection');
  });

  it('rejects an unchanged index body carrying a foreign original hash', async () => {
    const row = one(); Object.assign(row.semanticIndex!.index.digest, { value: 'c'.repeat(64) }); await refuse(row);
  });

  it('rejects duplicate and missing regional symbol rosters after correct rehashing', async () => {
    for (const duplicate of [false, true]) {
      const row = one(); const data = parsedIndex(row); const symbols = data['symbols'] as unknown[];
      if (duplicate) symbols.push(structuredClone(symbols[0])); else symbols.splice(1, 1);
      rehash(row, JSON.stringify(data)); await refuse(row);
    }
  });

  it('rejects a prepared definition rebound to another genuine original symbol', async () => {
    const row = one(); const definition = row.semanticIndex!.definitions[0];
    const other = row.semanticIndex!.referenceSearches.find(group => group.targetKey !== definition.targetKey)!;
    Object.assign(definition, { targetKey: other.targetKey }); await refuse(row);
  });

  it('retains the original empty 256-scan continuation and its late actual member', async () => {
    const row = bounded(); const index = await admit(row);
    expect(index.symbols).toHaveLength(165); expect(index.references).toHaveLength(301);
    const group = index.projection.referenceSearches.find(group => group.pages[0].scannedReferences === 256 &&
      group.pages[0].referenceIndices.length === 0 && group.pages.slice(1).some(page => page.referenceIndices.length > 0))!;
    expect(group).toBeDefined(); expect(group.pages[0].scanComplete).toBe(false);
    expect(group.pages.at(-1)!.scanComplete).toBe(true);
    const members = group.pages.flatMap(page => page.referenceIndices).map(at => index.references[at]);
    expect(members).toEqual(index.references.filter(reference => reference.symbolKey === group.targetKey));
    expect(members.length).toBeGreaterThan(0);
    for (const page of group.pages) { expect(page.scannedReferences).toBeLessThanOrEqual(256); expect(page.referenceIndices.length).toBeLessThanOrEqual(32); }
  });

  it('rejects omitted empty pages and late references borrowed into the first scan', async () => {
    for (const omit of [false, true]) {
      const row = bounded(); const group = row.semanticIndex!.referenceSearches.find(group =>
        group.pages[0].scannedReferences === 256 && group.pages[0].referenceIndices.length === 0 && group.pages.slice(1).some(page => page.referenceIndices.length > 0))!;
      expect(group).toBeDefined();
      if (omit) Object.assign(group, { pages: group.pages.slice(1) });
      else Object.assign(group.pages[0], { referenceIndices: group.pages.find(page => page.referenceIndices.length > 0)!.referenceIndices });
      await refuse(row);
    }
  });

  it('rejects reference pages with altered scan counts repeated members or oversized result lists', async () => {
    for (const mode of ['scan', 'repeat', 'overflow']) {
      const row = one(); const group = row.semanticIndex!.referenceSearches.find(group => group.pages[0].referenceIndices.length === 32)!;
      const page = group.pages[0];
      if (mode === 'scan') Object.assign(page, { scannedReferences: page.scannedReferences + 1 });
      else if (mode === 'repeat') Object.assign(page, { referenceIndices: [page.referenceIndices[0], ...page.referenceIndices.slice(0, -1)] });
      else Object.assign(page, { referenceIndices: [...page.referenceIndices, group.pages[1].referenceIndices[0]] });
      await refuse(row);
    }
  });

  it('rejects completion pages with changed kind foreign candidates or fabricated scan completion', async () => {
    for (const mode of ['kind', 'member', 'complete']) {
      const row = one(); const group = row.semanticIndex!.completions.find(group => group.pages[0].symbolKeys.length > 0)!;
      const page = group.pages[0];
      if (mode === 'kind') Object.assign(page, { expectedKind: 'coglatas.foreign' });
      else if (mode === 'member') Object.assign(page, { symbolKeys: [group.originKey, ...page.symbolKeys.slice(1)] });
      else Object.assign(page, { scanComplete: !page.scanComplete });
      await refuse(row);
    }
  });

  it('binds admitted index identity to the exact case and exact admitted Source object', async () => {
    const row = one(); const sources = (await readScopedSources(row))!; const index = (await readSemanticIndex(row, sources))!;
    expect(semanticIndexMatches(row, sources, index)).toBe(true);
    expect(semanticIndexMatches({ ...row }, sources, index)).toBe(false);
    expect(semanticIndexMatches(row, sources, { ...index })).toBe(false);
    expect(semanticIndexMatches(row, (await readScopedSources(row))!, index)).toBe(false);
    const copy = [...structuredClone(index.symbols)]; copy.splice(0); expect(index.symbols).toHaveLength(48);
    expect(Object.isFrozen(index.symbols[0])).toBe(true); expect(Object.isFrozen(index.references)).toBe(true);
  });

  it('opens an actual dependency reference and its exact original definition through current query membership', async () => {
    const store = await storeWithIndex(); const index = store.semanticIndex()!;
    const reference = index.references.find(reference => !sameContext(reference.origin.context, store.context()!.context))!;
    expect(reference.origin.relationId).toBeDefined();
    const target = index.symbols.find(symbol => symbol.key === reference.symbolKey)!;
    expect(store.selectIndexedSymbol(target)).toBe(true); expect(store.findIndexedReferences(target)).toBe(true);
    let query = store.indexedReferenceQuery()!; let moves = 0;
    while (!store.indexedReferenceRows(query).includes(reference)) {
      expect(++moves).toBeLessThan(4); expect(store.moveIndexedReferences(query, 1)).toBe(true); query = store.indexedReferenceQuery()!;
    }
    expect(store.navigateIndexedReference(query, reference)).toBe(true);
    expect(store.scopedSourceTarget()!.location).toBe(reference.origin);
    expect(store.scopedSourceTarget()!.record.recordType).toBe('relation');
    expect(store.navigateIndexedReference(query, reference, true)).toBe(true);
    expect(store.scopedSourceTarget()!.location).toBe(target.location);
    expect(store.scopedSourceTarget()!.region.context).toEqual(target.location.context);
  });

  it('refuses copied query members and an old page after advancing the original 32-result page', async () => {
    const store = await storeWithIndex(); const index = store.semanticIndex()!; const target = manyReferences(index);
    expect(store.selectIndexedSymbol({ ...target })).toBe(false); expect(store.selectIndexedSymbol(target)).toBe(true);
    expect(store.findIndexedReferences(target)).toBe(true); const first = store.indexedReferenceQuery()!;
    const firstRows = store.indexedReferenceRows(first); expect(firstRows).toHaveLength(32);
    expect(store.navigateIndexedReference({ ...first }, firstRows[0])).toBe(false);
    expect(store.navigateIndexedReference(first, { ...firstRows[0] })).toBe(false);
    expect(store.navigateIndexedReference(first, firstRows[0])).toBe(true);
    expect(store.moveIndexedReferences(first, 1)).toBe(true); const next = store.indexedReferenceQuery()!;
    expect(store.scopedSourceTarget()).toBeNull(); expect(store.indexedReferenceRows(first)).toEqual([]);
    expect(store.navigateIndexedReference(first, firstRows[0])).toBe(false);
    expect(store.navigateIndexedReference(next, firstRows[0])).toBe(false);
    expect([...firstRows, ...store.indexedReferenceRows(next)]).toEqual(index.references.filter(row => row.symbolKey === target.key));
  });

  it('requires the current typed candidate instance and separately admits document navigation', async () => {
    const store = await storeWithIndex(); const index = store.semanticIndex()!;
    const port = index.symbols.find(symbol => symbol.declaredType?.['kind'] === 'coglatas.entity-reference')!;
    expect(store.selectIndexedSymbol(port)).toBe(true); expect(store.completeIndexedReference(port)).toBe(true);
    const query = store.indexedCompletionQuery()!; const candidates = store.indexedCompletionRows(query); expect(candidates.length).toBeGreaterThan(1);
    expect(store.navigateIndexedCandidate(query, { ...candidates[0] })).toBe(false);
    expect(store.navigateIndexedCandidate({ ...query }, candidates[0])).toBe(false);
    expect(store.navigateIndexedCandidate(query, candidates[0])).toBe(true);
    expect(store.scopedSourceTarget()!.location).toBe(candidates[0].location);
    const document = index.symbols.find(symbol => symbol.isDocument)!;
    expect(store.selectIndexedSymbol(document)).toBe(true); expect(store.indexedCompletionQuery()).toBeNull();
    expect(store.navigateIndexedSymbol(document)).toBe(true); expect(store.scopedSourceTarget()!.location).toBe(document.location);
    expect(store.navigateIndexedCandidate(query, candidates[0])).toBe(false);
  });

  it('withdraws a pending index after permission loss without changing caller-retained original bytes', async () => {
    const store = await storeWithIndex(); const old = store.semanticIndex()!; const bytes = old.projection.index.text;
    const gate = delayedHash(); const pending = store.loadSemanticIndex(); gate.assertEntered();
    store.clear('denied'); gate.release(); expect(await pending).toBe(false); gate.restore();
    absent(store); expect(store.state()).toBe('denied'); expect(old.projection.index.text).toBe(bytes);
  });

  it('withdraws an aborted pending index while retaining its original Compiler result', async () => {
    const store = await storeWithIndex(); const result = store.result(); const gate = delayedHash();
    const cancellation = new AbortController(); const pending = store.loadSemanticIndex(cancellation.signal); gate.assertEntered();
    cancellation.abort(); gate.release(); expect(await pending).toBe(false); gate.restore();
    absent(store); expect(store.result()).toBe(result); expect(store.semanticIndexReadState()).toBe('idle');
  });

  it('preserves a newer admitted index when an older cancelled hash finally returns', async () => {
    const store = await storeWithIndex(); const gate = delayedHash(); const cancellation = new AbortController();
    const pending = store.loadSemanticIndex(cancellation.signal); gate.assertEntered();
    expect(await store.loadSemanticIndex()).toBe(true); const newer = store.semanticIndex(); expect(newer).not.toBeNull();
    cancellation.abort(); gate.release(); expect(await pending).toBe(false); gate.restore();
    expect(store.semanticIndex()).toBe(newer); expect(store.semanticIndexReadState()).toBe('ready');
  });

  it('preserves a newer refused state when an older original hash finally returns', async () => {
    const store = await storeWithIndex(); const gate = delayedHash(); const pending = store.loadSemanticIndex(); gate.assertEntered();
    store.choose('tests-index-partial-base'); store.loadResult();
    expect(await store.loadScopedSources()).toBe(false); expect(await store.loadSemanticIndex()).toBe(false);
    gate.release(); expect(await pending).toBe(false); gate.restore(); absent(store);
    expect(store.semanticIndexReadState()).toBe('unavailable');
    expect(store.context()!.key).toBe('tests-index-partial-base');
  });

  it('finishes a mutated pending read as unavailable and permits separate fresh readmission', async () => {
    const store = await storeWithIndex(); const row = store.context()!; const name = row.projectName;
    const gate = delayedHash(); const pending = store.loadSemanticIndex(); gate.assertEntered();
    Object.assign(row, { projectName: name + ' independent mutation' }); gate.release(); expect(await pending).toBe(false); gate.restore();
    absent(store); expect(store.semanticIndexReadState()).toBe('unavailable');
    Object.assign(row, { projectName: name }); expect(await store.loadScopedSources()).toBe(true);
    expect(await store.loadSemanticIndex()).toBe(true); expect(store.semanticIndexReadState()).toBe('ready');
  });

  it('requires a fresh index and member after an unchanged Source is explicitly readmitted', async () => {
    const store = await storeWithIndex(); const old = store.semanticIndex()!; const target = manyReferences(old);
    expect(store.selectIndexedSymbol(target)).toBe(true); expect(store.findIndexedReferences(target)).toBe(true);
    const query = store.indexedReferenceQuery()!; const reference = store.indexedReferenceRows(query)[0];
    expect(store.navigateIndexedReference(query, reference)).toBe(true);
    expect(await store.loadScopedSources()).toBe(true); absent(store);
    expect(store.selectIndexedSymbol(target)).toBe(false); expect(await store.loadSemanticIndex()).toBe(true);
    expect(store.semanticIndex()).not.toBe(old); expect(store.selectIndexedSymbol(target)).toBe(false);
    expect(store.navigateIndexedReference(query, reference)).toBe(false);
    expect(old.projection.index.text).toBe(store.semanticIndex()!.projection.index.text);
  });
});
