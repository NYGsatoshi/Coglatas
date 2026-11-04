/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { BoundReference, PreviewCase } from './projectide.contracts';

const bundle = () => readPreviewBundle(JSON.parse(readFileSync('public/assets/projectide/preview-v1.json', 'utf8')));

async function ready(key = 'fail', change?: (result: PreviewCase) => void) {
  const fixture = bundle();
  const result = fixture.cases.find((item) => item.key === key)!;
  change?.(result);
  const store = new ProjectIdeWorkbenchStore();
  await store.load(async () => fixture);
  store.choose(key);
  store.loadResult();
  return { store, result };
}

describe('Source-backed local reference navigation', () => {
  it.each(['fail', 'unknown'])('navigates distinct relation origins and definitions under %s coverage', async (key) => {
    const { store, result } = await ready(key);
    const relation = result.relations.find((item) => item.recordType === 'relation')!;
    expect(store.select({ kind: 'relation', id: relation.identity })).toBe(true);
    const references = store.referenceRows();
    expect(references.map((item) => item.origin.propertyPath)).toEqual(['/source', '/target']);
    for (const reference of references) {
      expect(store.selectReference(reference)).toBe(true);
      expect(store.sourceTarget()).toBeNull();
      const symbol = result.semanticInspection!.symbols.find((item) => item.key === reference.symbolKey)!;
      expect(store.referenceSymbol()).toBe(symbol);
      expect(store.navigateReference('origin')).toBe(true);
      expect(store.sourceLocation()).toBe(reference.origin);
      expect(store.sourceTarget()).toBe(relation);
      expect(store.navigateReference('definition')).toBe(true);
      expect(store.sourceLocation()).toBe(symbol.location);
      expect(store.sourceTarget()?.identity).toBe(symbol.location.entityId);
    }
  });

  it.each([
    'missing-key', 'missing-symbol', 'duplicate-symbol', 'ambiguous', 'unknown-coverage', 'unsupported',
    'out-of-scope', 'missing-within-scope', 'origin-path', 'origin-span', 'origin-document',
    'external-key', 'target-span', 'wrong-map', 'wrong-map-location', 'wrong-input', 'wrong-source', 'wrong-revision',
  ])('rejects %s and clears a previous target', async (mode) => {
    const { store, result } = await ready();
    const inspection = result.semanticInspection!;
    const reference = inspection.references[0];
    const symbol = inspection.symbols.find((item) => item.key === reference.symbolKey)!;
    expect(store.selectDiagnostic(result.result.diagnostics[0])).toBe(true);
    expect(store.navigate()).toBe(true);
    expect(store.sourceTarget()).not.toBeNull();
    if (mode === 'missing-key') Object.assign(reference, { symbolKey: null });
    if (mode === 'missing-symbol') Object.assign(inspection, { symbols: inspection.symbols.filter((item) => item !== symbol) });
    if (mode === 'duplicate-symbol') Object.assign(inspection, { symbols: [...inspection.symbols, { ...symbol }] });
    const states: Record<string, BoundReference['state']> = {
      ambiguous: 'Ambiguous', 'unknown-coverage': 'UnknownDueToCoverage', unsupported: 'Unsupported',
      'out-of-scope': 'OutOfScope', 'missing-within-scope': 'MissingWithinCompleteScope',
    };
    if (states[mode]) Object.assign(reference, { state: states[mode], symbolKey: null });
    if (mode === 'origin-path') Object.assign(reference.origin, { propertyPath: '/missing' });
    if (mode === 'origin-span') Object.assign(reference.origin, { textSpan: { start: result.sourceText.length, length: 1 } });
    if (mode === 'origin-document') Object.assign(reference.origin, { documentId: result.inputId });
    if (mode === 'external-key') Object.assign(reference, { symbolKey: bundle().cases.find((item) => item.key === 'branch-pass')!.semanticInspection!.references[0].symbolKey });
    if (mode === 'target-span') Object.assign(symbol.location, { textSpan: { start: result.sourceText.length, length: 1 } });
    if (mode === 'wrong-map') Object.assign(result.sourceMap!, { compilerInputId: result.context.revisionId });
    if (mode === 'wrong-map-location') Object.assign(result.sourceMap!.relations[0].location, { propertyPath: '/target' });
    if (mode === 'wrong-input') Object.assign(inspection, { compilerInputId: result.context.revisionId });
    if (mode === 'wrong-source') Object.assign(inspection, { sourceDigest: inspection.inputDigest });
    if (mode === 'wrong-revision') Object.assign(reference.origin, { context: bundle().cases.find((item) => item.key === 'branch-pass')!.context });
    expect(store.selectReference(reference)).toBe(false);
    expect(store.selectedReference()).toBeNull();
    expect(store.sourceTarget()).toBeNull();
    expect(store.navigateReference('definition')).toBe(false);
  });

  it.each(['unsupported', 'stale-pass'])('refuses an unqualified %s result', async (key) => {
    const { store, result } = await ready(key);
    const reference = result.semanticInspection!.references.find((item) => item.state === 'Resolved')!;
    expect(store.selectReference(reference)).toBe(false);
    expect(store.navigateReference('definition')).toBe(false);
    expect(store.sourceLocation()).toBeNull();
  });

  it('keeps the Inspector anchor and invalidates navigation across Result, map and context replacement', async () => {
    const { store, result } = await ready();
    const reference = result.semanticInspection!.references[0];
    const diagnostic = result.result.diagnostics[0];
    expect(store.selectDiagnostic(diagnostic)).toBe(true);
    expect(store.selectReference(reference)).toBe(true);
    expect(store.diagnostic()).toBe(diagnostic);
    expect(store.navigateReference('definition')).toBe(true);
    const replacement = { ...result, sourceMap: { ...result.sourceMap! } };
    store.context.set(replacement);
    store.result.set(replacement);
    expect(store.selectedReference()).toBeNull();
    expect(store.sourceTarget()).toBeNull();
    expect(store.navigateReference('definition')).toBe(false);
    store.loadResult();
    expect(store.selectReference(reference)).toBe(true);
    expect(store.selectDiagnostic(diagnostic)).toBe(true);
    expect(store.selectedReference()).toBeNull();
    store.choose('branch-pass');
    store.loadResult();
    expect(store.selectReference(reference)).toBe(false);
  });

  it('rejects a late result after cancellation synchronously starts a newer load', async () => {
    const { store, result } = await ready();
    const previous = result.semanticInspection!.references[0];
    expect(store.selectReference(previous)).toBe(true);
    expect(store.navigateReference('definition')).toBe(true);
    let finish!: () => void;
    let newer!: Promise<void>;
    const old = store.load((signal) => new Promise((resolve) => {
      finish = () => resolve({ ...bundle(), cases: [result] });
      signal.addEventListener('abort', () => { newer = store.load(async () => bundle()); });
    }));
    store.clear('cancelled');
    await newer;
    finish();
    await old;
    store.choose('fail');
    store.loadResult();
    expect(store.sourceTarget()).toBeNull();
    expect(store.selectReference(previous)).toBe(false);
  });
});
