/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

const raw = () => JSON.parse(readFileSync('public/assets/projectide/preview-v1.json', 'utf8'));
async function ready() {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => raw());
  store.choose('document-after'); store.loadResult(); return store;
}
describe('Core-prepared inverse Draft projection', () => {
  it('opens a new exact-current-base candidate without changing either committed snapshot', async () => {
    const store = await ready(); const prepared = store.preparedRevert()!; const current = store.result()!;
    const committedBytes = JSON.stringify(current.source); const currentContext = current.context;
    expect(prepared.plan.changedRecordCount).toBe(1);
    expect(store.loadPreparedRevert(prepared)).toBe(true);
    expect(store.context()?.key).toBe('revert-draft'); expect(store.result()).toBe(store.context());
    const candidate = store.context()!.context;
    expect(candidate.kind).toBe('coglatas.candidate');
    if (candidate.kind !== 'coglatas.candidate') throw new Error('Candidate required');
    expect(candidate.baseRevision).toEqual(currentContext); expect(candidate.capturedTargetHead).toEqual(currentContext);
    expect(JSON.stringify(current.source)).toBe(committedBytes); expect(store.preparedRevert()).toBeNull();
    expect(store.context()!.sourceText).not.toContain('Opaque document annotation');
    expect(store.loadPreparedRevert(prepared)).toBe(false);
  });
  it('rejects copied plans and drops retained preparation across loading, context and denial', async () => {
    const store = await ready(); const prepared = store.preparedRevert()!;
    expect(store.loadPreparedRevert(structuredClone(prepared))).toBe(false);
    store.choose('pass'); expect(store.preparedRevert()).toBeNull(); expect(store.loadPreparedRevert(prepared)).toBe(false);
    store.choose('document-after'); expect(store.preparedRevert()).toBeNull(); store.loadResult(); expect(store.preparedRevert()).not.toBeNull();
    store.clear('denied'); expect(store.preparedRevert()).toBeNull(); expect(store.bundle()).toBeNull(); expect(store.loadPreparedRevert(prepared)).toBe(false);
  });
  for (const mode of ['parent-case', 'reverted-case', 'candidate-case', 'current-digest', 'parent-digest', 'reverted-digest', 'candidate-digest',
    'current-context', 'parent-context', 'reverted-context', 'candidate-context', 'base', 'head', 'empty', 'limit', 'stale', 'aliased-candidate'] as const) {
    it(`rejects a mismatched inverse binding: ${mode}`, () => {
      const bundle = raw(); const current = bundle.cases.find((item: { key: string }) => item.key === 'document-after');
      const prepared = current.preparedRevert; const plan = prepared.plan;
      switch (mode) {
        case 'parent-case': prepared.parentCaseKey = 'absent'; break;
        case 'reverted-case': prepared.revertedCaseKey = 'absent'; break;
        case 'candidate-case': prepared.candidateCaseKey = current.key; break;
        case 'current-digest': plan.currentInputDigest.value = 'a'.repeat(64); break;
        case 'parent-digest': plan.parentInputDigest.value = 'a'.repeat(64); break;
        case 'reverted-digest': plan.revertedInputDigest.value = 'a'.repeat(64); break;
        case 'candidate-digest': plan.candidateInputDigest.value = 'a'.repeat(64); break;
        case 'current-context': plan.currentContext.revisionId = plan.parentContext.revisionId; break;
        case 'parent-context': plan.parentContext.revisionId = plan.currentContext.revisionId; break;
        case 'reverted-context': plan.revertedContext.revisionId = plan.parentContext.revisionId; break;
        case 'candidate-context': plan.candidateContext.candidateRevisionId = 'foreign'; break;
        case 'base': plan.candidateContext.baseRevision = structuredClone(plan.parentContext); break;
        case 'head': plan.candidateContext.capturedTargetHead = structuredClone(plan.parentContext); break;
        case 'empty': plan.changedRecordCount = 0; break;
        case 'limit': plan.changedRecordCount = 4097; break;
        case 'stale': current.freshness = 'Stale'; break;
        case 'aliased-candidate': bundle.cases.push({ ...structuredClone(bundle.cases.find((item: { key: string }) => item.key === 'revert-draft')), key: 'alias' }); break;
      }
      expect(() => readPreviewBundle(bundle)).toThrow();
    });
  }
  it('revalidates the actual retained object before opening its candidate', async () => {
    const store = await ready(); const prepared = store.preparedRevert()!;
    Object.assign(prepared.plan.currentInputDigest, { value: 'a'.repeat(64) });
    expect(store.loadPreparedRevert(prepared)).toBe(false); expect(store.state()).toBe('unsupported'); expect(store.context()).toBeNull();
  });
});
