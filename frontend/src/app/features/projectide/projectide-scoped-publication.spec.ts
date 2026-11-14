/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { readScopedSources, resolveScopedSource } from './projectide-scoped-sources';
import { assessLocalEvidence } from './projectide.contracts';
vi.stubGlobal('crypto', webcrypto);
const raw = () => JSON.parse(readFileSync(resolve('public/assets/projectide/scoped-publication-v2.json'), 'utf8'));
describe('Actual scoped semantic publication version two', () => {
  it.each(['root', 'true', 'false', 'unknown'])('admits actual payloads without reevaluating the %s predicate', async truth => {
    const bundle = readPreviewBundle(raw());
    for (const value of bundle.cases.filter(c => c.key.startsWith('tests-publication-' + truth + '-'))) {
      expect(assessLocalEvidence(value)).toBe('missing-evidence');
      const admitted = (await readScopedSources(value))!;
      expect(admitted.publicationVerified).toBe(true); expect(admitted.regions).toHaveLength(truth === 'root' ? 1 : 2);
      expect(assessLocalEvidence(value, admitted.publicationVerified)).toBe(truth === 'true' || truth === 'root' ? 'complete-local-pass' : truth === 'false' ? 'not-passed' : 'partial-coverage');
      for (const region of admitted.regions) for (const record of region.records)
        expect(resolveScopedSource(value, admitted, record.location)?.record).toBe(record);
    }
  });
  it.each(['model-bytes', 'ir-bytes', 'map-bytes', 'regional-model', 'missing-payload', 'schema', 'version', 'producing-stage', 'wrong-input', 'substituted-region'])('rejects incomplete or substituted publication: %s', async mode => {
    const value = raw().cases.find((c: { key: string }) => c.key === 'tests-publication-true-base'); const projection = value.scopedSources;
    if (mode === 'model-bytes') projection.publication.semanticModel.text += ' ';
    if (mode === 'ir-bytes') projection.publication.ir.text += ' ';
    if (mode === 'map-bytes') projection.publication.sourceMap.text += ' ';
    if (mode === 'regional-model') projection.regions[0].semanticModel = projection.regions[1].semanticModel;
    if (mode === 'missing-payload') delete projection.publication;
    if (mode === 'schema') value.result.artifacts[0].schemaId = 'example.other';
    if (mode === 'version') value.result.artifacts[0].version = 3;
    if (mode === 'producing-stage') value.result.stages = value.result.stages.filter((s: { stageId: string }) => s.stageId !== 'coglatas.scoped-publication');
    if (mode === 'wrong-input') projection.publication = raw().cases.find((c: { key: string }) => c.key === 'tests-publication-true-hypothesis').scopedSources.publication;
    if (mode === 'substituted-region') projection.regions[1] = raw().cases.find((c: { key: string }) => c.key === 'tests-publication-false-base').scopedSources.regions[1];
    await expect(readScopedSources(value)).rejects.toThrow();
  });
  it('enables ordinary root references only with current admitted source maps', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-publication-root-base'); store.loadResult();
    const reference = store.result()!.semanticInspection!.references.find(r => r.state === 'Resolved')!;
    expect(store.selectReference(reference)).toBe(false);
    expect(await store.loadScopedSources()).toBe(true); expect(store.selectReference(reference)).toBe(true);
    expect(store.navigateReference('origin')).toBe(true); expect(store.sourceTarget()).not.toBeNull();
    expect(store.navigateReference('definition')).toBe(true);
    Object.assign(store.result()!.result.artifacts[0], { schemaId: 'example.other' });
    expect(store.sourceTarget()).toBeNull(); expect(store.navigateReference('origin')).toBe(false);
  });
  it('withdraws already-read derived completion and symbol displays after in-place byte mutation', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-publication-true-base'); store.loadResult();
    expect(await store.loadScopedSources()).toBe(true);
    const task = store.result()!.declarations.find(row => row.kind === 'coglatas.task')!;
    if (task.recordType !== 'entity') throw new Error('expected-task-entity');
    expect(store.select({ kind: 'entity', id: task.identity })).toBe(true);
    expect(store.inspectedSymbols().length).toBeGreaterThan(0); expect(store.referenceCompletions().length).toBeGreaterThan(0);
    const symbol = store.inspectedSymbols()[0]; expect(await store.findReferences(symbol)).toBe(true); expect(store.referenceSearch()).not.toBeNull();
    Object.assign(store.result()!.scopedSources!.publication!.semanticModel, { text: store.result()!.scopedSources!.publication!.semanticModel.text + ' ' });
    expect(store.hasVerifiedPublication()).toBe(false); expect(store.referenceCompletions()).toEqual([]);
    expect(store.inspectedSymbols()).toEqual([]); expect(store.inspectedReferences()).toEqual([]); expect(store.referenceSearch()).toBeNull();
    expect(store.diagnosticSymbol()).toBeNull(); expect(store.requirementTaskSuggestions()).toEqual([]); expect(store.preparedRename()).toBeNull();
  });
  it('requires actual admission for root reference navigation and clears it across lifecycles', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-publication-true-base'); store.loadResult();
    expect(store.hasVerifiedPublication()).toBe(false); expect(await store.loadScopedSources()).toBe(true);
    expect(store.hasVerifiedPublication()).toBe(true);
    const foreign = store.scopedSources()[1].records[0]; expect(store.navigateScopedSource(foreign)).toBe(true);
    store.choose('tests-publication-false-base'); expect(store.hasVerifiedPublication()).toBe(false); expect(store.scopedSourceTarget()).toBeNull();
    store.loadResult(); expect(await store.loadScopedSources()).toBe(true);
    Object.assign(store.result()!.result.artifacts[0], { schemaId: 'example.other' });
    expect(store.hasVerifiedPublication()).toBe(false); expect(store.scopedSources()).toEqual([]);
    store.clear('denied'); expect(store.hasVerifiedPublication()).toBe(false);
  });
});
