/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { PreviewReadError, readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

const fixture = () => JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8'));

describe('Source-backed semantic inspection', () => {
  it('preserves a newer load started synchronously by an aborted reader', async () => {
    const bundle = readPreviewBundle(fixture()); const store = new ProjectIdeWorkbenchStore();
    let finishFirst!: (value: typeof bundle) => void;
    let finishNested!: (value: typeof bundle) => void;
    let nested!: Promise<void>; let nestedSignal!: AbortSignal; let outerReads = 0;
    const first = store.load((signal) => new Promise((resolve) => {
      finishFirst = resolve;
      signal.addEventListener('abort', () => {
        nested = store.load((next) => new Promise((resolveNext) => { nestedSignal = next; finishNested = resolveNext; }));
      }, { once: true });
    }));
    await store.load(async () => { outerReads++; return bundle; });
    expect(outerReads).toBe(0); expect(store.state()).toBe('loading');
    finishFirst(bundle); await first; expect(store.bundle()).toBeNull();
    finishNested(bundle); await nested; expect(store.bundle()).toBe(bundle);
    store.clear(); expect(nestedSignal.aborted).toBe(true);
  });
  it.each(['denied', 'cancelled'] as const)('preserves a synchronous %s boundary raised by an aborted reader', async (state) => {
    const bundle = readPreviewBundle(fixture()); const store = new ProjectIdeWorkbenchStore();
    let finish!: (value: typeof bundle) => void; let reads = 0;
    const first = store.load((signal) => new Promise((resolve) => {
      finish = resolve; signal.addEventListener('abort', () => store.clear(state), { once: true });
    }));
    await store.load(async () => { reads++; return bundle; });
    finish(bundle); await first;
    expect(reads).toBe(0); expect(store.state()).toBe(state);
    expect(store.bundle()).toBeNull(); expect(store.context()).toBeNull(); expect(store.result()).toBeNull();
  });
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
    expect(() => readPreviewBundle(raw)).toThrow(PreviewReadError);
  });
  it('retains compatibility with a fixture that has no optional inspection, without inferring symbols', async () => {
    const raw = fixture(); for (const item of raw.cases) delete item.semanticInspection;
    const bundle = readPreviewBundle(raw); const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle);
    store.choose('pass'); store.loadResult(); const record = store.context()!.declarations[0];
    if (record.recordType !== 'entity') throw new Error('Expected an entity fixture');
    store.select({ kind: 'entity', id: record.identity });
    expect(store.inspectedSymbols()).toEqual([]);
  });
  it.each(['declarations', 'documents', 'ports'])('visits Source %s a bounded number of times while validating many inspection entries', (mode) => {
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
    item.result.artifacts = item.result.artifacts.filter((artifact: { role: string }) => artifact.role !== 'coglatas.source-map');
    item.semanticInspection.symbols = item.declarations.map((entry: typeof declaration) => ({
      key: JSON.stringify({ context: item.context, entityId: entry.identity, portId: null }),
      kind: entry.kind, status: entry.status, location: structuredClone(entry.location), declaredType: null,
    }));
    item.semanticInspection.references = item.declarations.map((entry: typeof declaration) => ({
      origin: structuredClone(entry.location), state: 'Resolved', symbolKey: item.semanticInspection.symbols[0].key,
    }));
    if (mode === 'ports') {
      const entry = item.declarations[0];
      const type = { kind: 'coglatas.text', schemaVersion: 1, nullable: false, acceptsUnknown: false };
      entry.data.payload.ports = Array.from({ length: count }, (_, index) => ({ portId: `port-${index}`, type }));
      item.declarations = [entry]; item.semanticInspection.references = [];
      item.semanticInspection.symbols = entry.data.payload.ports.map((port: { portId: string }) => ({
        key: JSON.stringify({ context: item.context, entityId: entry.identity, portId: port.portId }),
        kind: type.kind, status: 'Decoded', declaredType: type,
        location: { ...structuredClone(entry.location), propertyPath: '/payload/ports/' + port.portId },
      }));
    }
    let reads = 0;
    const entries = mode === 'declarations' ? item.declarations : mode === 'ports' ? item.declarations[0].data.payload.ports : item.source.documents;
    const observed = new Proxy(entries, { get(target, key, receiver) {
      if (typeof key === 'string' && /^\d+$/.test(key)) reads++;
      return Reflect.get(target, key, receiver);
    } });
    if (mode === 'declarations') item.declarations = observed;
    else if (mode === 'ports') item.declarations[0].data.payload.ports = observed;
    else item.source.documents = observed;
    expect(readPreviewBundle(raw)).toBe(raw);
    // Structural work bound, not elapsed time or hardware-dependent assurance.
    expect(reads).toBeLessThanOrEqual(10 * count);
  });

});
