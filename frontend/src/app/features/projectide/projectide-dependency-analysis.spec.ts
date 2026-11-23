import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());
const bundle = (file: string) => readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide', file), 'utf8')));

describe('Actual Core dependency failures', () => {
  it.each([['dependency-analysis-v1.json', 'tests-dependency'], ['graph-closure-v1.json', 'tests-graph']])('retains %s Compiler FAIL across baseline, Scenario and Draft while local Tests can pass', async (file, prefix) => {
    const store = new ProjectIdeWorkbenchStore();
    await store.load(async () => bundle(file)); store.choose(prefix + '-base'); store.loadResult();
    expect(store.result()!.result.verdict).toBe('coglatas.fail');
    const prepared = store.preparedScenario()!;
    expect(await store.loadScenarioComparison(prepared)).toBe(true);
    const admitted = store.scenario()!;
    expect(admitted.tests.map(item => item.run.verdict)).toEqual(['Fail', 'Pass', 'Pass']);
    for (const stage of ['scenario', 'draft'] as const) {
      expect(await store.loadScenarioStage(store.preparedScenario()!, stage)).toBe(true);
      expect(store.result()!.result.verdict).toBe('coglatas.fail');
      expect(store.result()!.result.coverage).toBe('coglatas.partial');
      expect(await store.loadLocalTests()).toBe(true);
      expect(store.localTests()!.run.verdict).toBe('Pass');
      const summary = store.result()!.result.diagnostics.find(item => item.code === 'coglatas.analysis.dependency-finding')!;
      expect(summary.primaryLocation.context).toEqual(store.result()!.context);
      expect(store.result()!.sourceMap).toBeNull();
    }
    store.clear('denied'); expect(store.result()).toBeNull(); expect(store.scenario()).toBeNull(); expect(store.localTests()).toBeNull();
  });
});
