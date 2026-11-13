/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

const raw = () => JSON.parse(readFileSync('public/assets/projectide/preview-v1.json', 'utf8'));
async function ready() {
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => raw()); store.choose('requirement-candidates'); store.loadResult();
  const record = store.context()!.declarations.find(item => item.kind === 'coglatas.requirement')!;
  if (record.recordType !== 'entity') throw new Error('Requirement entity fixture required');
  store.select({ kind: 'entity', id: record.identity }); return store;
}

describe('Core-derived Requirement Task candidates', () => {
  it('opens only a retained candidate from the selected Requirement without changing Source or proving satisfaction', async () => {
    const store = await ready(); const input = store.context()!; const text = input.sourceText;
    const page = store.requirementTaskSuggestions()[0]; expect(page.candidateOnly).toBe(true); expect(page.candidates).toHaveLength(1);
    expect(page.candidates[0].reason).toBe('DeclaredTaskInSnapshot'); expect(input.result.verdict).toBe('coglatas.inconclusive');
    expect(store.navigateTaskSuggestion(page, { ...page.candidates[0] })).toBe(false);
    expect(store.navigateTaskSuggestion(page, page.candidates[0])).toBe(true);
    expect(store.sourceLocation()?.entityId).toBe(input.declarations.find(item => item.kind === 'coglatas.task')?.identity);
    expect(store.context()).toBe(input); expect(store.context()?.sourceText).toBe(text);
    store.choose('pass'); expect(store.requirementTaskSuggestions()).toEqual([]); expect(store.navigateTaskSuggestion(page, page.candidates[0])).toBe(false);
    store.clear('denied'); expect(store.bundle()).toBeNull(); expect(store.requirementTaskSuggestions()).toEqual([]);
  });
  for (const mode of ['digest', 'origin', 'candidate-kind', 'name', 'reason', 'satisfaction', 'coverage', 'duplicate-page', 'duplicate-candidate', 'too-many'] as const) {
    it(`refuses a malformed candidate projection: ${mode}`, () => {
      const data = raw(); const item = data.cases.find((entry: { key: string }) => entry.key === 'requirement-candidates');
      const page = item.requirementTaskSuggestions[0]; const candidate = page.candidates[0];
      switch (mode) {
        case 'digest': page.inputDigest.value = 'a'.repeat(64); break;
        case 'origin': page.originKey = candidate.symbolKey; break;
        case 'candidate-kind': candidate.symbolKey = page.originKey; break;
        case 'name': candidate.displayName = 'Forged'; break;
        case 'reason': candidate.reason = 'SatisfiesRequirement'; break;
        case 'satisfaction': page.candidateOnly = false; break;
        case 'coverage': page.coverageComplete = false; break;
        case 'duplicate-page': item.requirementTaskSuggestions.push(structuredClone(page)); break;
        case 'duplicate-candidate': page.candidates.push(structuredClone(candidate)); break;
        case 'too-many': page.candidates = Array.from({ length: 33 }, () => structuredClone(candidate)); break;
      }
      expect(() => readPreviewBundle(data)).toThrow();
    });
  }
  it('keeps legacy inspection-free fixtures readable with candidate queries inert', async () => {
    const data = raw(); for (const item of data.cases) delete item.semanticInspection;
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => data); store.choose('requirement-candidates'); store.loadResult();
    const record = store.context()!.declarations.find(item => item.kind === 'coglatas.requirement')!;
    if (record.recordType !== 'entity') throw new Error('Requirement entity fixture required');
    store.select({ kind: 'entity', id: record.identity });
    expect(store.requirementTaskSuggestions()).toEqual([]);
  });
  it('rejects an old page whose origin was changed to another valid Requirement after the list was read', async () => {
    const store = await ready(); const page = store.requirementTaskSuggestions()[0]; const result = store.context()!;
    const other = result.requirementTaskSuggestions!.find(item => item.originKey !== page.originKey)!;
    Object.assign(page, { originKey: other.originKey }); Object.assign(result, { requirementTaskSuggestions: [page] });
    expect(() => readPreviewBundle(store.bundle())).not.toThrow();
    expect(store.navigateTaskSuggestion(page, page.candidates[0])).toBe(false); expect(store.sourceLocation()).toBeNull();
  });
  it('rechecks mutable projection metadata when opening a retained candidate', async () => {
    const store = await ready(); const page = store.requirementTaskSuggestions()[0]; Object.assign(page.candidates[0], { reason: 'SatisfiesRequirement' });
    expect(store.navigateTaskSuggestion(page, page.candidates[0])).toBe(false); expect(store.sourceLocation()).toBeNull();
  });
});
