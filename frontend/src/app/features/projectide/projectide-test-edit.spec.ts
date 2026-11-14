/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readPreparedTestEdit, testEditPair } from './projectide-test-edit';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());
const raw = () => JSON.parse(readFileSync(resolve('public/assets/projectide/test-edit-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(raw());
async function ready() { const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-edit-before'); store.loadResult(); return store; }

describe('Canonical Source Test definition preview', () => {
  it('verifies the actual Core-prepared before and after sources and original definition bytes', async () => {
    const b = bundle(); const before = b.cases.find(item => item.key === 'tests-edit-before')!;
    const result = (await readPreparedTestEdit(b, before))!;
    expect(result.after.key).toBe('tests-edit-after'); expect(JSON.parse(result.definitionText).payload.assertions[0].expected.value).toBe(true);
    expect(JSON.parse(result.prepared.importedText).payload.assertions[0].expected.value).toBe(false);
  });
  it('exports without mutation and loads only the exact current prepared candidate', async () => {
    const store = await ready(); expect(await store.loadLocalTests()).toBe(true); expect(store.localTests()!.run.verdict).toBe('Fail');
    const prepared = store.preparedTestEdit()!; const before = store.context();
    expect(await store.exportPreparedTestDefinition(prepared)).toBe(prepared.definitionText); expect(store.context()).toBe(before);
    expect(await store.loadPreparedTestEdit(structuredClone(prepared))).toBe(false);
    expect(await store.loadPreparedTestEdit(prepared)).toBe(true); expect(store.context()!.key).toBe('tests-edit-after');
    expect(store.localTests()).toBeNull(); expect(store.sourceTarget()).toBeNull();
    expect(await store.loadLocalTests()).toBe(true); expect(store.localTests()!.run.verdict).toBe('Pass');
    expect(await store.loadPreparedTestEdit(prepared)).toBe(false);
  });
  it.each(['case', 'format', 'input', 'source', 'context', 'document', 'identity', 'exported', 'imported', 'duplicate', 'future-reference'])(
    'rejects a substituted %s preview before exposing a candidate', async mode => {
      const b = raw(); const before = b.cases[0]; const p = before.preparedTestEdit;
      if (mode === 'case') p.afterCaseKey = 'missing';
      if (mode === 'format') p.plan.format = 'future';
      if (mode === 'input') p.plan.beforeInputDigest.value = 'a'.repeat(64);
      if (mode === 'source') p.plan.afterSourceDigest.value = 'a'.repeat(64);
      if (mode === 'context') p.plan.beforeContext.candidateRevisionId = 'foreign';
      if (mode === 'document') p.plan.target.documentId = 'foreign';
      if (mode === 'identity') p.plan.target.entityId = 'foreign';
      if (mode === 'exported') p.definitionText = '{}';
      if (mode === 'imported') p.importedText = p.definitionText;
      if (mode === 'duplicate') p.importedText = p.importedText.replace('"entityId":', '"entityId":"duplicate","entityId":');
      if (mode === 'future-reference') { const value = JSON.parse(p.importedText); value.payload.assertions[0].target.context = b.cases[1].context; p.importedText = JSON.stringify(value); }
      await expect(readPreparedTestEdit(readPreviewBundle(b), before)).rejects.toThrow();
    });
  it('rejects contexts outside the provided immutable bundle', () => {
    const b = bundle(); expect(() => testEditPair(b, structuredClone(b.cases[0]))).toThrow();
  });
  it.each(['load', 'export'])('preserves a newer selection queued between admission and the %s continuation', async operation => {
    const store = await ready(); const prepared = store.preparedTestEdit()!;
    const admitted = (await readPreparedTestEdit(store.bundle()!, store.context()!))!;
    const boundary = store as unknown as { admitTestEdit: () => Promise<typeof admitted> };
    vi.spyOn(boundary, 'admitTestEdit').mockImplementation(async () => {
      queueMicrotask(() => { store.choose('tests-edit-before'); store.loadResult(); }); return admitted;
    });
    const result = operation === 'load' ? await store.loadPreparedTestEdit(prepared) : await store.exportPreparedTestDefinition(prepared);
    expect(result).toBe(operation === 'load' ? false : null);
    expect(store.context()!.key).toBe('tests-edit-before'); expect(store.preparedTestEdit()).toBe(prepared);
  });
  it('does not restore a prepared candidate after a denied boundary interrupts hashing', async () => {
    const store = await ready(); const p = store.preparedTestEdit()!; let release!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; }); const original = webcrypto.subtle.digest.bind(webcrypto.subtle);
    vi.spyOn(webcrypto.subtle, 'digest').mockImplementationOnce(async (...args) => { await gate; return original(...args); });
    const pending = store.loadPreparedTestEdit(p); store.clear('denied'); release();
    expect(await pending).toBe(false); expect(store.state()).toBe('denied'); expect(store.context()).toBeNull(); expect(store.preparedTestEdit()).toBeNull();
  });
});
