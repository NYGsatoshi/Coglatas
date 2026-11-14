import { readLocalTests } from './projectide-tests.reader';
/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { readScopedSources, resolveScopedSource, scopedSourcesMatch } from './projectide-scoped-sources';
import { contextRevision } from './projectide.contracts';
vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());
const raw = () => JSON.parse(readFileSync(resolve('public/assets/projectide/scoped-sources-v1.json'), 'utf8'));
const fixture = () => readPreviewBundle(raw()).cases[0];

describe('Original-byte fixed scoped source readers', () => {
  it('opens exact Core Sources with stable nodes in separate captured versions while Compiler still FAILs', async () => {
    const value = fixture(); const admitted = (await readScopedSources(value))!;
    expect(admitted.regions).toHaveLength(3); expect(value.result.verdict).toBe('coglatas.fail'); expect(value.sourceMap).toBeNull();
    const versions = admitted.regions.filter(region => region.context.branch.projectId !== value.context.branch.projectId);
    expect(versions).toHaveLength(2); expect(versions[0].context.branch.projectId).toBe(versions[1].context.branch.projectId);
    expect(contextRevision(versions[0].context)).not.toBe(contextRevision(versions[1].context));
    const first = versions[0].records.find(row => row.kind === 'coglatas.project')!;
    const second = versions[1].records.find(row => row.identity === first.identity)!;
    expect(resolveScopedSource(value, admitted, first.location)?.region.sourceText).toBe(versions[0].sourceText);
    expect(resolveScopedSource(value, admitted, second.location)?.region.sourceText).toBe(versions[1].sourceText);
    expect(versions[0].sourceText).not.toBe(versions[1].sourceText);
    expect(Object.isFrozen(admitted)).toBe(true); expect(Object.isFrozen(versions[0].records)).toBe(true);
  });
  it('explicitly loads Sources and clears foreign selections on result, input and denial boundaries', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-scoped-base'); store.loadResult();
    expect(store.scopedSources()).toEqual([]); expect(await store.loadScopedSources()).toBe(true);
    const region = store.scopedSources().find(region => region.context.branch.projectId !== store.context()!.context.branch.projectId)!;
    expect(store.navigateScopedSource(region.records[0])).toBe(true); expect(store.scopedSourceTarget()!.region).toBe(region);
    store.clearSelection(); expect(store.scopedSourceTarget()).toBeNull();
    expect(store.navigateScopedSource(region.records[0])).toBe(true); store.choose('tests-scoped-hypothesis');
    expect(store.scopedSources()).toEqual([]); expect(store.scopedSourceTarget()).toBeNull();
    store.loadResult(); expect(await store.loadScopedSources()).toBe(true); store.clear('denied');
    expect(store.scopedSources()).toEqual([]); expect(store.scopedSourceTarget()).toBeNull(); expect(store.scopedReadState()).toBe('idle');
  });
  it.each(['format', 'input', 'result', 'map', 'duplicate-context', 'missing-region', 'source-body', 'denied', 'partial', 'node-payload'])('refuses substituted or incomplete scoped projection: %s', async mode => {
    const value = raw().cases[0];
    if (mode === 'format') value.scopedSources.format = 'example.future/2';
    if (mode === 'input') value.scopedSources.inputText = raw().cases[1].scopedSources.inputText;
    if (mode === 'result') value.scopedSources.result = raw().cases[1].scopedSources.result;
    if (mode === 'map') value.scopedSources.regions[0].sourceMap = value.scopedSources.regions[1].sourceMap;
    if (mode === 'duplicate-context') value.scopedSources.regions[1].context = value.scopedSources.regions[0].context;
    if (mode === 'missing-region') value.scopedSources.regions.pop();
    if (mode === 'source-body') value.materializedSnapshots.entries[0].source.text += ' ';
    if (mode === 'denied') value.materializedSnapshots.entries[0].admission = 'Unavailable';
    if (mode === 'partial') value.materializedSnapshots.entries[0].relationsComplete = false;
    if (mode === 'node-payload') {
      const ir = JSON.parse(value.scopedSources.regions[0].ir.text); ir.nodes[0].payload = { fabricated: true };
      value.scopedSources.regions[0].ir.text = JSON.stringify(ir);
    }
    await expect(readScopedSources(value)).rejects.toThrow();
  });
  it('invalidates admission after same-object result/materialization replacement', async () => {
    const value = fixture(); const admitted = (await readScopedSources(value))!;
    Object.assign(value, { materializedSnapshots: structuredClone(value.materializedSnapshots) });
    expect(scopedSourcesMatch(value, admitted)).toBe(true);
    Object.assign(value, { sourceText: value.sourceText + ' ' });
    expect(scopedSourcesMatch(value, admitted)).toBe(false);
    expect(resolveScopedSource(value, admitted, admitted.regions[1].records[0].location)).toBeNull();
  });
  it('rejects unknown revisions and invalid source paths without borrowing another version', async () => {
    const value = fixture(); const admitted = (await readScopedSources(value))!; const record = admitted.regions[1].records[0];
    const location = structuredClone(record.location);
    Object.assign(location.context, { revisionId: 'ffffffff-ffff-4fff-8fff-ffffffffffff' });
    expect(resolveScopedSource(value, admitted, location)).toBeNull();
    expect(resolveScopedSource(value, admitted, { ...record.location, propertyPath: '/missing' })).toBeNull();
    expect(resolveScopedSource(value, admitted, { ...record.location, textSpan: { start: 0, length: 9999999 } })).toBeNull();
  });
  it('late admission cannot publish into or erase a newer result lifecycle', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-scoped-base'); store.loadResult();
    const original = crypto.subtle.digest.bind(crypto.subtle); let release!: () => void;
    const gate = new Promise<void>(resolve => release = resolve);
    vi.spyOn(crypto.subtle, 'digest').mockImplementationOnce(async (algorithm, data) => { await gate; return original(algorithm, data); });
    const old = store.loadScopedSources(); store.choose('tests-scoped-hypothesis'); store.loadResult();
    expect(await store.loadScopedSources()).toBe(true); const current = store.scopedSources(); release();
    expect(await old).toBe(false); expect(store.scopedSources()).toEqual(current); expect(store.scopedReadState()).toBe('ready');
  });
  it('opens the actual foreign failure through its admitted diagnostic certificate and rejects a copied row', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-scoped-base'); store.loadResult();
    expect(await store.loadScopedSources()).toBe(true);
    const region = store.scopedSources().find(region => region.diagnostics.some(row => row.code === 'coglatas.analysis.constraint-false'))!;
    const diagnostic = region.diagnostics.find(row => row.code === 'coglatas.analysis.constraint-false')!;
    expect(store.navigateScopedDiagnostic(region, diagnostic)).toBe(true);
    expect(store.scopedSourceTarget()!.location).toEqual(diagnostic.primaryLocation); expect(store.result()!.result.verdict).toBe('coglatas.fail');
    expect(store.navigateScopedDiagnostic(region, structuredClone(diagnostic))).toBe(false); expect(store.scopedSourceTarget()).toBeNull();
  });
  it('uses the actual supplied pin for equivalent aliases without inventing missing Source bodies or Test PASS', async () => {
    const bundle = readPreviewBundle(raw());
    const rawPins = bundle.cases[0].manifest['manifest'] as { settings: { authorizedExternalSnapshots: unknown[] } };
    expect(rawPins.settings.authorizedExternalSnapshots).toHaveLength(3);
    const admitted = (await readScopedSources(bundle.cases[0]))!; expect(admitted.regions).toHaveLength(3);
    expect((await readLocalTests(bundle.cases[0]))!.run.verdict).toBe('Fail');
    expect((await readLocalTests(bundle.cases[1]))!.run.verdict).toBe('Pass');
    expect(bundle.cases[1].result.verdict).toBe('coglatas.fail');
  });
});
