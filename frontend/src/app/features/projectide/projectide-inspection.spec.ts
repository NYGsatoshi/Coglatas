/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

const fixture = () => JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8'));

describe('Source-backed semantic inspection', () => {
  it('shows only selected Source symbols and resolved incoming references, then clears on context change', async () => {
    const bundle = readPreviewBundle(fixture()); const store = new ProjectIdeWorkbenchStore();
    await store.load(async () => bundle); store.choose('pass'); store.loadResult();
    const record = store.context()!.declarations.find((record) => record.kind === 'coglatas.project')!;
    if (record.recordType !== 'entity') throw new Error('Expected an entity fixture');
    store.select({ kind: 'entity', id: record.identity });
    expect(store.inspectedSymbols().length).toBe(1);
    expect(store.inspectedReferences().some((reference) => reference.state === 'Resolved')).toBe(true);
    store.choose('branch-pass'); expect(store.inspectedSymbols()).toEqual([]); expect(store.inspectedReferences()).toEqual([]);
  });
  it.each(['input', 'context', 'source', 'symbol-kind', 'duplicate', 'location', 'reference-state', 'symbol-key', 'declared-type', 'location-document'])('rejects a substituted %s inspection envelope', (mode) => {
    const raw = fixture(); const item = raw.cases[0]; const inspection = item.semanticInspection;
    if (mode === 'symbol-key') inspection.symbols[0].key = '{}';
    if (mode === 'declared-type') inspection.symbols[0].declaredType = { kind: 'coglatas.text' };
    if (mode === 'input') inspection.compilerInputId = raw.cases[1].inputId;
    if (mode === 'context') inspection.context = raw.cases[1].context;
    if (mode === 'source') inspection.sourceDigest = raw.cases[1].sourceDigest;
    if (mode === 'symbol-kind') inspection.symbols[0].kind = 'example.future';
    if (mode === 'duplicate') inspection.symbols.push(structuredClone(inspection.symbols[0]));
    if (mode === 'location') inspection.symbols[0].location.entityId = 'absent';
    if (mode === 'location-document') {
      const document = structuredClone(item.source.documents[0]); document.documentId = '00000099-0000-4000-8000-000000000001';
      item.source.documents.push(document); inspection.symbols[0].location.documentId = document.documentId;
    }
    if (mode === 'reference-state') inspection.references[0].state = 'UnknownDueToCoverage';
    expect(() => readPreviewBundle(raw)).toThrow();
  });
  it('retains compatibility with a fixture that has no optional inspection, without inferring symbols', async () => {
    const raw = fixture(); for (const item of raw.cases) delete item.semanticInspection;
    const bundle = readPreviewBundle(raw); const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle);
    store.choose('pass'); store.loadResult(); const record = store.context()!.declarations[0];
    if (record.recordType !== 'entity') throw new Error('Expected an entity fixture');
    store.select({ kind: 'entity', id: record.identity });
    expect(store.inspectedSymbols()).toEqual([]);
  });
  it.each(['declarations', 'documents'])('visits Source %s a bounded number of times while validating many inspection entries', (mode) => {
    const raw = fixture(); const item = raw.cases[0]; raw.cases = [item];
    const count = 64;
    const document = structuredClone(item.source.documents[0]);
    const declaration = structuredClone(item.declarations.find((entry: { kind: string }) => entry.kind === 'coglatas.task'));
    item.source.documents = Array.from({ length: count }, (_, index) => ({ ...structuredClone(document), documentId: `document-${index}` }));
    const documentId = item.source.documents[count - 1].documentId;
    item.declarations = Array.from({ length: count }, (_, index) => {
      const entry = structuredClone(declaration); entry.identity = `entity-${index}`;
      entry.location.entityId = entry.identity; entry.location.documentId = documentId;
      entry.data.entityId = entry.identity; entry.data.documentId = documentId;
      return entry;
    });
    item.relations = []; item.result.diagnostics = []; item.sourceMap = null; item.sourceMapDigest = null;
    item.semanticInspection.symbols = item.declarations.map((entry: typeof declaration) => ({
      key: JSON.stringify({ context: item.context, entityId: entry.identity, portId: null }),
      kind: entry.kind, status: entry.status, location: structuredClone(entry.location), declaredType: null,
    }));
    item.semanticInspection.references = item.declarations.map((entry: typeof declaration) => ({
      origin: structuredClone(entry.location), state: 'Resolved', symbolKey: item.semanticInspection.symbols[0].key,
    }));
    let reads = 0;
    const entries = mode === 'declarations' ? item.declarations : item.source.documents;
    const observed = new Proxy(entries, { get(target, key, receiver) {
      if (typeof key === 'string' && /^\d+$/.test(key)) reads++;
      return Reflect.get(target, key, receiver);
    } });
    if (mode === 'declarations') item.declarations = observed; else item.source.documents = observed;
    expect(readPreviewBundle(raw)).toBe(raw);
    // Structural work bound, not elapsed time or hardware-dependent assurance.
    expect(reads).toBeLessThanOrEqual(10 * count);
  });

});
