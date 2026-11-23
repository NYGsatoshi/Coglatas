/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources } from './projectide-scoped-sources';
import { readLocalTests } from './projectide-tests.reader';
import { assessLocalEvidence } from './projectide.contracts';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
const raw = () => JSON.parse(readFileSync(resolve('public/assets/projectide/execution-plan-v1.json'), 'utf8'));
describe('Actual immutable plan definition admission', () => {
  it.each(['root', 'external', 'unknown'])('reads actual baseline/hypothesis/Draft definitions and independent Test results: %s', async family => {
    const values = readPreviewBundle(raw()).cases.filter(c => c.key.startsWith('tests-plan-' + family + '-'));
    for (const [index, value] of values.entries()) {
      const admitted = (await readScopedSources(value))!;
      expect(admitted.publicationVerified).toBe(true); expect((await readLocalTests(value))!.run.verdict).toBe(index === 0 ? 'Fail' : 'Pass');
      if (family === 'unknown') {
        expect(admitted.plan).toBeNull(); expect(admitted.planNotProducedReason).toBe('coglatas.plan.required-validation-incomplete');
        expect(assessLocalEvidence(value, true)).toBe('partial-coverage');
      } else {
        expect(admitted.plan!.definition['execution']).toBe('coglatas.not-instantiated'); expect(admitted.plan!.definition['tasks']).toHaveLength(family === 'root' ? 3 : 4); expect(admitted.plan!.definition['dependencies']).toHaveLength(family === 'root' ? 2 : 3);
        expect(admitted.planNotProducedReason).toBeNull(); expect(assessLocalEvidence(value, true)).toBe('complete-local-pass');
      }
    }
  });
  it('never assigns different bodies to one captured Source or snapshot version within the plan bundle', () => {
    const bodies = new Map<string, string>(); const snapshots = new Map<string, string>();
    for (const value of raw().cases) {
      const key = JSON.stringify(value.context); const digest = value.sourceDigest.value;
      if (bodies.has(key)) expect(bodies.get(key)).toBe(digest); else bodies.set(key, digest);
      for (const entry of value.materializedSnapshots?.entries ?? []) {
        const context = JSON.stringify(entry.context); const snapshot = JSON.stringify([entry.snapshotId, entry.version]);
        if (bodies.has(context)) expect(bodies.get(context)).toBe(entry.source.digest.value); else bodies.set(context, entry.source.digest.value);
        if (snapshots.has(snapshot)) expect(snapshots.get(snapshot)).toBe(entry.source.digest.value); else snapshots.set(snapshot, entry.source.digest.value);
      }
    }
  });
  it('retains the200k budget refusal rather than treating missing plan publication as PASS', async () => {
    const values = readPreviewBundle(raw()).cases.filter(c => c.key.startsWith('tests-plan-budget-'));
    expect((await readScopedSources(values[0]))!.plan).not.toBeNull();
    for (const value of values.slice(1)) {
      expect(await readScopedSources(value)).toBeNull(); expect(value.result.execution).toBe('coglatas.resource-exhausted');
      expect(value.result.verdict).not.toBe('coglatas.pass'); expect(value.result.artifacts).toEqual([]);
    }
  });
  it.each(['bytes', 'missing', 'other-input', 'stage', 'future-format'])('rejects substituted plan bytes or producing stage: %s', async mode => {
    const value = raw().cases[0];
    if (mode === 'bytes') value.scopedSources.publication.plan.text += ' ';
    if (mode === 'missing') delete value.scopedSources.publication.plan;
    if (mode === 'other-input') value.scopedSources.publication.plan = raw().cases[1].scopedSources.publication.plan;
    if (mode === 'stage') value.result.stages = value.result.stages.filter((s: { stageId: string }) => s.stageId !== 'coglatas.plan-definition');
    if (mode === 'future-format') value.scopedSources.publication.plan.text = value.scopedSources.publication.plan.text.replace('coglatas.execution-plan-definition/1', 'coglatas.execution-plan-definition/2');
    await expect(readScopedSources(value)).rejects.toThrow();
  });
  it('requires explicit byte admission and withdraws definitions on mutation and input/denial boundaries', async () => {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw())); store.choose('tests-plan-external-base'); store.loadResult();
    expect(store.planDefinition()).toBeNull(); expect(await store.loadScopedSources()).toBe(true); expect(store.planDefinition()!.taskCount).toBe(4);
    Object.assign(store.result()!.scopedSources!.publication!.plan!, { text: store.result()!.scopedSources!.publication!.plan!.text + ' ' });
    expect(store.planDefinition()).toBeNull(); expect(store.hasVerifiedPublication()).toBe(false);
    store.choose('tests-plan-root-base'); store.loadResult(); expect(await store.loadScopedSources()).toBe(true); expect(store.planDefinition()).not.toBeNull();
    store.choose('tests-plan-unknown-base'); expect(store.planDefinition()).toBeNull(); store.loadResult(); expect(await store.loadScopedSources()).toBe(true);
    expect(store.planNotProducedReason()).toBe('coglatas.plan.required-validation-incomplete'); store.clear('denied'); expect(store.planNotProducedReason()).toBeNull();
  });
});
