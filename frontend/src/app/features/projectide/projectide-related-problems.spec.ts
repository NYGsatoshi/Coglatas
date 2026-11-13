/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import { SourceRecord } from './projectide.contracts';

const bundle = () => readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8')));
const selectRecord = (store: ProjectIdeWorkbenchStore, record: SourceRecord) => store.select(
  record.recordType === 'entity' ? { kind: 'entity', id: record.identity } : { kind: 'relation', id: record.identity },
);
async function ready() {
  const store = new ProjectIdeWorkbenchStore();
  await store.load(async () => bundle());
  store.choose('fail');
  store.loadResult();
  return store;
}

describe('Inspector related Problems', () => {
  it('opens an accepted related finding and navigates to its exact primary location', async () => {
    const store = await ready();
    const record = store.context()!.declarations.find(item => item.kind === 'coglatas.constraint')!;
    expect(selectRecord(store, record)).toBe(true);
    expect(store.relatedDiagnostics().map(item => item.code)).toEqual(['coglatas.analysis.constraint-false']);
    const diagnostic = store.relatedDiagnostics()[0];
    expect(store.selectDiagnostic(diagnostic)).toBe(true);
    expect(store.diagnostic()).toBe(diagnostic);
    expect(store.navigate()).toBe(true);
    expect(store.sourceLocation()).toBe(diagnostic.primaryLocation);
    expect(store.sourceTarget()).toBe(record);
  });

  it('does not offer unrelated record or relation findings, or unloaded results', async () => {
    const store = await ready();
    for (const record of [...store.context()!.declarations, ...store.context()!.relations]) {
      expect(selectRecord(store, record)).toBe(true);
      expect(store.relatedDiagnostics()).toHaveLength(record.kind === 'coglatas.constraint' ? 1 : 0);
    }
    store.choose('fail');
    const constraint = store.context()!.declarations.find(item => item.kind === 'coglatas.constraint')!;
    selectRecord(store, constraint);
    expect(store.relatedDiagnostics()).toEqual([]);
  });

  it.each(['clear', 'reload-result', 'replace-result', 'replace-context', 'cancel'])(
    'clears related findings and rejects retained objects after %s', async mode => {
      const store = await ready();
      const previous = store.result()!.result.diagnostics[0];
      expect(store.selectDiagnostic(previous)).toBe(true);
      expect(store.relatedDiagnostics()).toEqual([previous]);
      if (mode === 'clear') store.clearSelection();
      if (mode === 'reload-result') store.loadResult();
      if (mode === 'replace-result') store.result.set(null);
      if (mode === 'replace-context') store.choose('branch-pass');
      if (mode === 'cancel') store.clear('cancelled');
      expect(store.relatedDiagnostics()).toEqual([]);
      await store.load(async () => bundle());
      store.choose('fail');
      store.loadResult();
      expect(store.selectDiagnostic(previous)).toBe(false);
      expect(store.relatedDiagnostics()).toEqual([]);
      expect(store.sourceTarget()).toBeNull();
    },
  );
});
