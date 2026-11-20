/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import { readPreviewBundle, resolveLocation } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
import type { Diagnostic, PreviewBundle, PreviewCase } from './projectide.contracts';

const bundle = (): PreviewBundle => readPreviewBundle(JSON.parse(
  readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8'),
));

async function ready(change?: (value: PreviewCase) => void) {
  const data = bundle();
  const value = data.cases.find(item => item.key === 'fail')!;
  if (change) change(value);
  const store = new ProjectIdeWorkbenchStore();
  await store.load(async () => data);
  store.choose('fail');
  store.loadResult();
  const constraint = value.declarations.find(item => item.kind === 'coglatas.constraint')!;
  if (constraint.recordType !== 'entity') throw new Error('Expected the exported constraint entity');
  expect(resolveLocation(value, constraint.location)).toBe(constraint);
  expect(store.select({ kind: 'entity', id: constraint.identity })).toBe(true);
  return { store, value, constraint };
}

describe('Independent Inspector identity and lifecycle candidates', () => {
  it('ignores an equal entity UUID in another known document without invalidating the selected record', async () => {
    const { store, value, constraint } = await ready(value => {
      const original = value.result.diagnostics[0];
      const foreignDocument = '00000091-0000-4000-8000-000000000001';
      Object.assign(value.source, { documents: [...value.source.documents, {
        documentId: foreignDocument, logicalName: 'synthetic-other-document',
      }] });
      Object.assign(value.result, { diagnostics: [original, {
        ...original, diagnosticId: 'synthetic-other-document-finding',
        primaryLocation: { ...original.primaryLocation, documentId: foreignDocument },
      }] });
    });
    expect(resolveLocation(value, constraint.location)).toBe(constraint);
    expect(store.relatedDiagnostics()).toEqual([value.result.diagnostics[0]]);
  });

  it('ignores an equal raw UUID used as a relation identity', async () => {
    const { store, value, constraint } = await ready(value => {
      const original = value.result.diagnostics[0];
      const { entityId, ...location } = original.primaryLocation;
      Object.assign(value.result, { diagnostics: [original, {
        ...original, diagnosticId: 'synthetic-opposite-typed-finding',
        primaryLocation: { ...location, relationId: entityId },
      }] });
    });
    expect(resolveLocation(value, constraint.location)).toBe(constraint);
    expect(store.relatedDiagnostics()).toEqual([value.result.diagnostics[0]]);
  });

  it('preserves compiler order and the exact property location when selecting a related finding', async () => {
    const { store, value, constraint } = await ready(value => {
      const original = value.result.diagnostics[0];
      const property: Diagnostic = {
        ...original, diagnosticId: 'synthetic-property-finding' as Diagnostic['diagnosticId'],
        primaryLocation: { ...original.primaryLocation, propertyPath: '/payload/classification' },
      };
      Object.assign(value.result, { diagnostics: [property, original] });
    });
    const [property, original] = value.result.diagnostics;
    expect(store.relatedDiagnostics()).toEqual([property, original]);
    expect(store.selectDiagnostic(property)).toBe(true);
    expect(store.navigate()).toBe(true);
    expect(store.sourceLocation()).toBe(property.primaryLocation);
    expect(store.sourceLocation()).not.toBe(constraint.location);
    expect(store.sourceTarget()).toBe(constraint);
    expect(store.relatedDiagnostics()).toEqual([property, original]);
  });

  it.each(['tenantId', 'projectId', 'branchId', 'revisionId'])(
    'rejects an injected diagnostic with a foreign %s and clears its previous Source target', async dimension => {
      const { store, value } = await ready();
      const original = store.relatedDiagnostics()[0];
      expect(store.selectDiagnostic(original)).toBe(true);
      expect(store.navigate()).toBe(true);
      const altered = structuredClone(value);
      const location = altered.result.diagnostics[0].primaryLocation;
      if (dimension === 'revisionId') Object.assign(location.context, { revisionId: 'foreign-revision' });
      else Object.assign(location.context.branch, { [dimension]: 'foreign-scope' });
      // Bypass the reader deliberately: the store's direct navigation must still fail closed.
      store.context.set(altered);
      store.result.set(altered);
      expect(store.relatedDiagnostics()).toEqual([]);
      expect(store.selectDiagnostic(altered.result.diagnostics[0])).toBe(true);
      expect(store.navigate()).toBe(false);
      expect(store.sourceTarget()).toBeNull();
      expect(store.sourceLocation()).toBeNull();
    },
  );

  it('rejects an equal cloned Diagnostic object while retaining the genuine object', async () => {
    const { store } = await ready();
    const original = store.relatedDiagnostics()[0];
    expect(store.selectDiagnostic(original)).toBe(true);
    expect(store.navigate()).toBe(true);
    expect(store.selectDiagnostic(structuredClone(original))).toBe(false);
    expect(store.relatedDiagnostics()).toEqual([]);
    expect(store.sourceTarget()).toBeNull();
    expect(store.selectDiagnostic(original)).toBe(true);
    expect(store.relatedDiagnostics()).toEqual([original]);
  });

  it.each(['Stale', 'Undetermined'] as const)('does not expose related findings for %s results', async freshness => {
    const { store, value } = await ready();
    const altered = { ...value, freshness };
    store.context.set(altered);
    store.result.set(altered);
    expect(store.relatedDiagnostics()).toEqual([]);
    expect(store.navigate()).toBe(false);
    expect(store.sourceTarget()).toBeNull();
  });

  it('does not let an ignored-abort older read restore a cleared Inspector', async () => {
    const { store } = await ready();
    const original = store.relatedDiagnostics()[0];
    let finish!: (value: PreviewBundle) => void;
    const previous = store.load(() => new Promise(resolve => { finish = resolve; }));
    await store.load(async () => bundle());
    store.choose('fail');
    store.loadResult();
    const replacement = store.result()!.result.diagnostics[0];
    expect(store.selectDiagnostic(replacement)).toBe(true);
    const newerResult = store.result();
    finish(bundle());
    await previous;
    expect(store.result()).toBe(newerResult);
    expect(store.relatedDiagnostics()).toHaveLength(1);
    expect(store.relatedDiagnostics()[0]).toBe(replacement);
    expect(store.selectDiagnostic(original)).toBe(false);
    expect(store.sourceTarget()).toBeNull();
  });
});
