/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { readPreviewBundle, resolveLocation } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { Identity } from './projectide.contracts';

const bundle = () => readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8')));
async function ready() {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle());
  store.choose('fail'); store.loadResult(); return store;
}
describe('Problems to Source navigation integrity', () => {
  it('keeps Diagnostic Inspector and Source on the same current record', async () => {
    const store = await ready(); const diagnostic = store.result()!.result.diagnostics[0];
    expect(store.select({ kind: 'diagnostic', id: diagnostic.diagnosticId })).toBe(true);
    expect(store.diagnostic()).toBe(diagnostic); expect(store.navigate()).toBe(true);
    expect(store.sourceTarget()).toBe(store.inspectedRecord());
    expect(store.sourceTarget()!.identity).toBe(diagnostic.primaryLocation.entityId);
    expect(store.sourceTarget()).toBe(resolveLocation(store.context()!, diagnostic.primaryLocation));
  });
  it('rejects an absent Diagnostic and clears the previous target and Inspector', async () => {
    const store = await ready(); store.select({ kind: 'diagnostic', id: store.result()!.result.diagnostics[0].diagnosticId });
    expect(store.navigate()).toBe(true);
    expect(store.select({ kind: 'diagnostic', id: 'absent' as Identity<'Diagnostic'> })).toBe(false);
    expect(store.selection()).toBeNull(); expect(store.diagnostic()).toBeNull(); expect(store.inspectedRecord()).toBeNull();
    expect(store.navigate()).toBe(false); expect(store.sourceTarget()).toBeNull();
  });
  it('never resolves an old Diagnostic against a replacement Result or Context', async () => {
    const store = await ready(); const old = store.result()!.result.diagnostics[0];
    store.select({ kind: 'diagnostic', id: old.diagnosticId }); expect(store.navigate()).toBe(true);
    store.result.set(null); expect(store.navigate()).toBe(false); expect(store.sourceTarget()).toBeNull(); expect(store.selection()).toBeNull();
    store.choose('branch-pass'); store.loadResult();
    expect(store.select({ kind: 'diagnostic', id: old.diagnosticId })).toBe(false); expect(store.inspectedRecord()).toBeNull();
  });
  it.each(['both', 'missing-path', 'span', 'surrogate', 'document', 'map-input', 'map-duplicate', 'revision'])('rejects malformed %s navigation', async (mode) => {
    const store = await ready(); const value = structuredClone(store.context()!);
    const original = value.result.diagnostics[0].primaryLocation;
    let location = { ...original };
    if (mode === 'both') location = { ...location, relationId: 'other' as Identity<'Relation'> };
    if (mode === 'missing-path') location = { ...location, propertyPath: '/payload/absent' };
    if (mode === 'span') location = { ...location, textSpan: { start: Number.MAX_SAFE_INTEGER, length: 1 } };
    if (mode === 'surrogate') location = { ...location, textSpan: { start: value.sourceText.indexOf('🌌') + 1, length: 0 } };
    if (mode === 'document') location = { ...location, documentId: 'absent' as Identity<'Document'> };
    if (mode === 'map-input') Object.assign(value.sourceMap!, { compilerInputId: 'old' });
    if (mode === 'map-duplicate') Object.assign(value.sourceMap!, { nodes: [...value.sourceMap!.nodes, ...value.sourceMap!.nodes] });
    if (mode === 'revision') location = { ...location, context: bundle().cases.find(item => item.key === 'branch-pass')!.context };
    expect(typeof resolveLocation(value, location)).toBe('string');
  });
  it('rejects a Diagnostic object from an earlier bundle even when its stable ID is reused', async () => {
    const store = await ready();
    const previous = store.result()!.result.diagnostics[0];
    await store.load(async () => bundle());
    store.choose('fail');
    store.loadResult();
    expect(store.result()!.result.diagnostics[0].diagnosticId).toBe(previous.diagnosticId);
    expect(store.selectDiagnostic(previous)).toBe(false);
    expect(store.selection()).toBeNull();
  });

  it('uses the Core partial fixture without fabricating a location for document-level diagnostics', async () => {
    const store = await ready();
    store.choose('unsupported');
    store.loadResult();
    const diagnostic = store.result()!.result.diagnostics.find(({ primaryLocation }) =>
      !primaryLocation.entityId && !primaryLocation.relationId,
    )!;
    expect(store.result()!.sourceMap).toBeNull();
    expect(store.selectDiagnostic(diagnostic)).toBe(true);
    expect(store.diagnostic()).toBe(diagnostic);
    expect(store.inspectedRecord()).toBeNull();
    expect(store.navigate()).toBe(false);
    expect(store.navigationState()).toBe('unsupported');
  });

  it('retains the abort reentry boundary while a newer load clears navigation', async () => {
    const store = await ready(); let nested!: Promise<void>;
    const pending = store.load(signal => new Promise(() => signal.addEventListener('abort', () => {
      nested = store.load(async () => bundle());
    })));
    void pending; store.clear('cancelled'); await nested;
    expect(store.state()).toBe('ready'); expect(store.selection()).toBeNull(); expect(store.sourceTarget()).toBeNull();
  });
});
