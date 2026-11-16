/// <reference types="node" />
import { webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { readReferenceSearchPage, referenceSearchPageMatches } from './projectide-reference-pages.reader';
import { assessLocalEvidence } from './projectide.contracts';

vi.stubGlobal('crypto', webcrypto);
const raw = () => JSON.parse(readFileSync('public/assets/projectide/preview-v1.json', 'utf8'));
const read = () => readPreviewBundle(raw());
async function ready(withSource = true) {
  const bundle = read(); const result = bundle.cases.find(item => item.key === 'reference-pages')!;
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle); store.choose(result.key); store.loadResult();
  if (withSource) expect(await store.loadRootSource()).toBe(true);
  const symbol = result.semanticInspection!.symbols.find(item => item.kind === 'coglatas.project')!;
  expect(store.select({ kind: 'entity', id: symbol.location.entityId! })).toBe(true);
  return { store, result, symbol };
}
describe('Core-prepared reference pages', () => {
  it('keeps an optional page projection inert when the legacy inspection is omitted', () => {
    const fixture = raw(); for (const item of fixture.cases) delete item.semanticInspection;
    const result = readPreviewBundle(fixture).cases.find(item => item.key === 'reference-pages')!;
    const original = read().cases.find(item => item.key === 'reference-pages')!.semanticInspection!.symbols[0];
    expect(result.semanticInspection).toBeUndefined(); expect(readReferenceSearchPage(result, original)).toBeNull();
  });
  it('retains all 35 actual canonical origins across 32 plus 3 without qualifying missing evidence', async () => {
    const {store, result, symbol} = await ready();
    const original = result.semanticInspection!.references.filter(item => item.state === 'Resolved' && item.symbolKey === symbol.key);
    expect(original).toHaveLength(35); expect(await store.findReferences(symbol)).toBe(true);
    const first = store.referencePage()!;
    expect(first).toMatchObject({pageIndex: 0, pageCount: 2, indexCoverageComplete: true, scanComplete: false, scannedReferences: 32});
    expect(first.references).toHaveLength(32); expect(store.referenceSearch()!.truncated).toBe(true);
    expect(store.moveReferencePage(first, 1)).toBe(true); const second = store.referencePage()!;
    expect(second).toMatchObject({pageIndex: 1, pageCount: 2, indexCoverageComplete: true, scanComplete: true, scannedReferences: 3});
    expect(second.references).toHaveLength(3); expect(store.referenceSearch()!.truncated).toBe(false);
    [...first.references, ...second.references].forEach((reference, index) => expect(reference).toBe(original[index]));
    expect(new Set(original.map(item => item.origin.entityId)).size).toBe(35);
    expect(store.selectReferencePageOrigin(second, second.references[2])).toBe(true);
    expect(store.navigateReference('origin')).toBe(true); expect(store.sourceLocation()).toBe(original[34].origin);
    expect(store.inspectedSymbols()).toContain(symbol);
    expect(assessLocalEvidence(result)).toBe('partial-coverage');
    expect(result.result.evidenceComplete).toBe(false);
    expect(result.result.coverage).toBe('coglatas.partial');
    expect(store.moveReferencePage(second, 0)).toBe(true); expect(store.sourceTarget()).toBeNull(); expect(store.selectedReference()).toBeNull();
    expect(store.referencePage()!.references).toEqual(first.references);
  });
  it('requires explicit admission of the retained root Source', async () => {
    const {store, symbol} = await ready(false);
    expect(await store.findReferences(symbol)).toBe(false); expect(store.referencePage()).toBeNull(); expect(store.referenceSearch()).toBeNull();
  });
  it('rejects forged and foreign pages, page indices, and origins outside the displayed page', async () => {
    const {store, result, symbol} = await ready(); const other = await ready();
    expect(await store.findReferences(symbol)).toBe(true); expect(await other.store.findReferences(other.symbol)).toBe(true);
    const page = store.referencePage()!; const foreign = other.store.referencePage()!;
    expect(referenceSearchPageMatches(result, {...page})).toBe(false); expect(referenceSearchPageMatches(result, foreign)).toBe(false);
    for (const index of [-1, 2, .5, Number.NaN]) expect(store.moveReferencePage(page, index)).toBe(false);
    expect(store.moveReferencePage(foreign, 1)).toBe(false); expect(store.referencePage()).toBe(page);
    expect(store.selectReferencePageOrigin(page, {...page.references[0]})).toBe(false);
    expect(store.moveReferencePage(page, 1)).toBe(true); const second = store.referencePage()!;
    expect(store.selectReferencePageOrigin(page, page.references[0])).toBe(false);
    expect(store.selectReferencePageOrigin(second, page.references[0])).toBe(false);
    expect(store.moveReferencePage(page, 0)).toBe(false); expect(store.referencePage()).toBe(second);
    expect(readReferenceSearchPage(result, {...symbol})).toBeNull();
  });
  it.each(['cancel', 'source-reload', 'selection', 'result', 'context', 'clear'])(
    'withdraws page-owned navigation after %s and does not resurrect an old page', async mode => {
      const {store, symbol} = await ready(); expect(await store.findReferences(symbol)).toBe(true); const page = store.referencePage()!;
      expect(store.selectReferencePageOrigin(page, page.references[0])).toBe(true); expect(store.navigateReference('origin')).toBe(true);
      if (mode === 'cancel') store.cancelReferenceSearch();
      if (mode === 'source-reload') await store.loadRootSource();
      if (mode === 'selection') store.clearSelection();
      if (mode === 'result') store.loadResult();
      if (mode === 'context') { store.choose('pass'); store.loadResult(); }
      if (mode === 'clear') store.clear('denied');
      expect(store.referencePage()).toBeNull(); expect(store.referenceSearch()).toBeNull(); expect(store.sourceTarget()).toBeNull();
      expect(store.selectReferencePageOrigin(page, page.references[0])).toBe(false); expect(store.moveReferencePage(page, 1)).toBe(false);
    });
  it('owns immutable page and source metadata without changing legacy mutable readers', async () => {
    const {store, result, symbol} = await ready(); expect(await store.findReferences(symbol)).toBe(true); const page = store.referencePage()!;
    expect(() => Object.assign(page, {pageIndex: 1})).toThrow(); expect(() => (page.references as unknown[]).pop()).toThrow();
    expect(() => Object.assign(page.references[0], {symbolKey: 'other'})).toThrow();
    expect(() => Object.assign(result.sourceMap!, {compilerInputId: 'other'})).toThrow();
    expect(() => Object.assign(result.referenceSearches![0].pages[0], {scanComplete: true})).toThrow();
    expect(readPreviewBundle(store.bundle())).toBe(store.bundle());
    const legacy = store.bundle()!.cases.find(item => item.key === 'pass')!; expect(Object.isFrozen(legacy)).toBe(false);
    expect(store.referencePage()).toBe(page);
  });
  it.each(['digest', 'target', 'reorder', 'duplicate', 'foreign-index', 'scanned', 'coverage', 'early-end', 'missing-page', 'extra-page', 'cursor', 'extra-field', 'null-page'])(
    'refuses malformed prepared page metadata: %s', mode => {
      const bundle = raw(); const result = bundle.cases.find((item: {key:string}) => item.key === 'reference-pages');
      const group = result.referenceSearches[0]; const page = group.pages[0];
      if (mode === 'digest') group.inputDigest.value = 'f'.repeat(64);
      if (mode === 'target') group.targetKey = 'foreign';
      if (mode === 'reorder') page.referenceIndices.reverse();
      if (mode === 'duplicate') page.referenceIndices[1] = page.referenceIndices[0];
      if (mode === 'foreign-index') page.referenceIndices[0] = 34;
      if (mode === 'scanned') page.scannedReferences++;
      if (mode === 'coverage') page.indexCoverageComplete = false;
      if (mode === 'early-end') page.scanComplete = true;
      if (mode === 'missing-page') group.pages.pop();
      if (mode === 'extra-page') group.pages.push(group.pages[1]);
      if (mode === 'cursor') page.cursor = 'offset:32';
      if (mode === 'extra-field') group.authorized = true;
      if (mode === 'null-page') group.pages[0] = null;
      expect(() => readPreviewBundle(bundle)).toThrow();
    });
});
