/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

const fixture = () => JSON.parse(readFileSync('public/assets/projectide/preview-v1.json', 'utf8'));
async function ready() {
  const bundle = readPreviewBundle(fixture());
  const result = bundle.cases.find((item) => item.key === 'pass')!;
  const store = new ProjectIdeWorkbenchStore();
  await store.load(async () => bundle); store.choose('pass'); store.loadResult();
  const task = result.declarations.find((item) => item.kind === 'coglatas.task' && item.recordType === 'entity')!;
  if (task.recordType !== 'entity') throw new Error('Expected an entity fixture');
  expect(store.select({ kind: 'entity', id: task.identity })).toBe(true);
  return { store, result };
}

describe('Core-derived typed reference suggestions', () => {
  it('opens a canonical suggested declaration without changing Source', async () => {
    const { store, result } = await ready();
    const source = result.sourceText;
    const pages = store.referenceCompletions(); expect(pages).toHaveLength(1);
    const page = pages[0]; expect(page.expectedKind).toBe('coglatas.task');
    expect(page.candidates).toHaveLength(1); expect(page.coverageComplete).toBe(true);
    expect(store.navigateCompletion(page, page.candidates[0])).toBe(true);
    expect(store.sourceTarget()?.kind).toBe('coglatas.task'); expect(result.sourceText).toBe(source);
  });
  it.each(['input', 'kind', 'symbol', 'name', 'duplicate', 'origin'])('rejects a malformed %s projection', (mode) => {
    const raw = fixture(); const result = raw.cases.find((item: { key: string }) => item.key === 'pass');
    const page = result.referenceCompletions[0];
    if (mode === 'input') page.inputDigest = result.sourceDigest;
    if (mode === 'kind') page.expectedKind = 'coglatas.milestone';
    if (mode === 'symbol') page.candidates[0].symbolKey = 'unavailable';
    if (mode === 'name') page.candidates[0].displayName = 'injected';
    if (mode === 'duplicate') page.candidates.push({ ...page.candidates[0] });
    if (mode === 'origin') page.originKey = page.candidates[0].symbolKey;
    expect(() => readPreviewBundle(raw)).toThrow();
  });
  it('rejects copied candidates and old selections across clear, result and context boundaries', async () => {
    const { store } = await ready(); const page = store.referenceCompletions()[0]; const candidate = page.candidates[0];
    expect(store.navigateCompletion(page, { ...candidate })).toBe(false);
    expect(store.navigateCompletion({ ...page }, candidate)).toBe(false);
    expect(store.navigateCompletion(page, candidate)).toBe(true);
    store.clearSelection(); expect(store.referenceCompletions()).toEqual([]);
    expect(store.navigateCompletion(page, candidate)).toBe(false); expect(store.sourceTarget()).toBeNull();
    store.choose('branch-pass'); store.loadResult(); expect(store.navigateCompletion(page, candidate)).toBe(false);
    store.clear('denied'); expect(store.referenceCompletions()).toEqual([]);
  });
});
