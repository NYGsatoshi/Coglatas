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
    store.select({ kind: 'entity', id: record.identity });
    expect(store.inspectedSymbols().length).toBe(1);
    expect(store.inspectedReferences().some((reference) => reference.state === 'Resolved')).toBe(true);
    store.choose('branch-pass'); expect(store.inspectedSymbols()).toEqual([]); expect(store.inspectedReferences()).toEqual([]);
  });
  it.each(['input', 'context', 'source', 'symbol-kind', 'duplicate', 'location', 'reference-state', 'symbol-key', 'declared-type'])('rejects a substituted %s inspection envelope', (mode) => {
    const raw = fixture(); const item = raw.cases[0]; const inspection = item.semanticInspection;
    if (mode === 'symbol-key') inspection.symbols[0].key = '{}';
    if (mode === 'declared-type') inspection.symbols[0].declaredType = { kind: 'coglatas.text' };
    if (mode === 'input') inspection.compilerInputId = raw.cases[1].inputId;
    if (mode === 'context') inspection.context = raw.cases[1].context;
    if (mode === 'source') inspection.sourceDigest = raw.cases[1].sourceDigest;
    if (mode === 'symbol-kind') inspection.symbols[0].kind = 'example.future';
    if (mode === 'duplicate') inspection.symbols.push(structuredClone(inspection.symbols[0]));
    if (mode === 'location') inspection.symbols[0].location.entityId = 'absent';
    if (mode === 'reference-state') inspection.references[0].state = 'UnknownDueToCoverage';
    expect(() => readPreviewBundle(raw)).toThrow();
  });
  it('retains compatibility with a fixture that has no optional inspection, without inferring symbols', async () => {
    const raw = fixture(); for (const item of raw.cases) delete item.semanticInspection;
    const bundle = readPreviewBundle(raw); const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle);
    store.choose('pass'); store.loadResult(); store.select({ kind: 'entity', id: store.context()!.declarations[0].identity });
    expect(store.inspectedSymbols()).toEqual([]);
  });
});
