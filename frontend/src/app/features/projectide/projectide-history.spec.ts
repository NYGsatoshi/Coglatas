/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

const raw = () => JSON.parse(readFileSync('public/assets/projectide/preview-v1.json', 'utf8'));
async function ready() {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => raw());
  store.choose('document-before'); store.loadResult(); return store;
}

describe('Core-reconstructed synthetic history', () => {
  it('keeps reconstruction and Source compatibility separate and opens the exact historical case', async () => {
    const store = await ready(); const history = store.history()!;
    expect(history.state).toBe('Ready'); expect(history.sourceAdmissionStatus).toBe('Unsupported');
    expect(history.missingAnchorState).toBe('Unavailable'); expect(history.compactedCheckpointVerified).toBe(true);
    expect(history.revisions.map(row => row.storage)).toEqual(['Checkpoint', 'TypedDelta']);
    expect(history.deltaRecordCounts).toEqual({ documents: 1, declarations: 0, relations: 0 });
    const after = history.revisions[1]; expect(store.openHistoryRevision(after)).toBe(true);
    expect(store.context()?.key).toBe('document-after'); expect(store.context()?.context).toEqual(after.context);
    expect(store.result()).toBe(store.context()); expect(store.history()).toBeNull(); expect(store.openHistoryRevision(after)).toBe(false);
  });
  it('drops old history on context, loading and denied boundaries without admitting copied rows', async () => {
    const store = await ready(); const row = store.history()!.revisions[1];
    expect(store.openHistoryRevision(structuredClone(row))).toBe(false);
    store.choose('pass'); expect(store.history()).toBeNull(); expect(store.openHistoryRevision(row)).toBe(false);
    store.choose('document-before'); expect(store.history()).toBeNull(); store.loadResult(); expect(store.history()).not.toBeNull();
    store.clear('denied'); expect(store.history()).toBeNull(); expect(store.bundle()).toBeNull();
  });
  for (const mode of ['missing-head', 'digest', 'context', 'self-parent', 'order', 'duplicate-row', 'duplicate-path', 'gap', 'storage', 'missing-outcome', 'compaction', 'counts', 'unsafe-sequence', 'roster-limit'] as const) {
    it(`refuses inconsistent historical bindings: ${mode}`, () => {
      const bundle = raw(); const before = bundle.cases.find((item: { key: string }) => item.key === 'document-before');
      const history = before.preparedHistory; const row = history.revisions[1];
      switch (mode) {
        case 'missing-head': history.headCaseKey = 'absent'; break;
        case 'digest': row.sourceDigest.value = 'a'.repeat(64); break;
        case 'context': row.context.branch.projectId = 'foreign'; break;
        case 'self-parent': row.parents = [structuredClone(row.context)]; break;
        case 'order': row.branchSequence = 0; break;
        case 'duplicate-row': history.revisions.push(structuredClone(row)); break;
        case 'duplicate-path': history.reconstructionPath.push(structuredClone(row.context)); break;
        case 'gap': row.parents = []; break;
        case 'storage': row.storage = 'Checkpoint'; break;
        case 'missing-outcome': history.missingAnchorState = 'Ready'; break;
        case 'compaction': history.compactedCheckpointVerified = false; break;
        case 'counts': history.deltaRecordCounts.documents = -1; break;
        case 'unsafe-sequence': row.branchSequence = Number.MAX_SAFE_INTEGER + 1; break;
        case 'roster-limit': history.revisions = Array.from({ length: 257 }, () => structuredClone(row)); break;
      }
      expect(() => readPreviewBundle(bundle)).toThrow();
    });
  }
  it('revalidates retained history when a version is opened', async () => {
    const store = await ready(); const history = store.history()!; const row = history.revisions[1];
    Object.assign(row, { sourceDigest: { algorithm: 'sha-256', value: 'a'.repeat(64) } });
    expect(store.openHistoryRevision(row)).toBe(false); expect(store.state()).toBe('unsupported'); expect(store.context()).toBeNull();
  });
  it('rejects an otherwise bound delta crossing Branches within one Project', () => {
    const bundle = raw();
    const before = bundle.cases.find((item: { key: string }) => item.key === 'document-before');
    const after = bundle.cases.find((item: { key: string }) => item.key === 'document-after');
    const history = before.preparedHistory;
    const replacement = structuredClone(after.context);
    replacement.branch.branchId = '00000000-0000-0000-0000-000000000099';
    const changed = JSON.parse(JSON.stringify(after).split(after.context.branch.branchId).join(replacement.branch.branchId));
    delete changed.preparedRevert;
    delete before.preparedDiff;
    bundle.cases[bundle.cases.indexOf(after)] = changed;
    history.revisions[1].context = structuredClone(replacement);
    history.reconstructionPath[1] = structuredClone(replacement);
    delete before.preparedHistory;
    expect(() => readPreviewBundle(bundle)).not.toThrow();
    before.preparedHistory = history;
    expect(() => readPreviewBundle(bundle)).toThrow();
  });
});
