/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readPreparedScenario, scenarioMatches, scenarioTriplet } from './projectide-scenario';
import { isRevisionContextShape } from './projectide-preview.validation';
import { sameContext, contextRevision } from './projectide.contracts';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());
const raw = () => JSON.parse(readFileSync(resolve('public/assets/projectide/scenario-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(raw());
async function ready() { const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose('tests-scenario-base'); store.loadResult(); return store; }

describe('Fixed-base Scenario Core journey admission', () => {
  it('reads actual Core assumptions, source changes and three independently bound result inputs', async () => {
    const b = bundle(); const admitted = (await readPreparedScenario(b, b.cases[0]))!;
    expect(admitted.tests.map(x => x.run.verdict)).toEqual(['Fail', 'Pass', 'Pass']);
    expect(new Set(admitted.tests.map(x => x.run.compilerInputId)).size).toBe(3);
    expect(admitted.assumptions).toHaveLength(1); expect(admitted.assumptions[0].kind).toBe('TaskInputDefault');
    expect(admitted.changes).toHaveLength(1); expect(admitted.scenario.context.kind).toBe('coglatas.scenario');
    expect(Object.isFrozen(admitted.tests)).toBe(true); expect(Object.isFrozen(admitted.assumptions)).toBe(true); expect(Object.isFrozen(admitted.changes)).toBe(true);
    expect(scenarioMatches(b, b.cases[0], admitted)).toBe(true); expect(scenarioMatches(b, structuredClone(b.cases[0]), admitted)).toBe(false);
  });
  it.each(['id', 'revision', 'overlay', 'base', 'branch', 'nested', 'extra'])(
    'keeps the complete %s Scenario context distinct and refuses unsupported context shapes', mode => {
      const context = raw().cases[1].context; const copy = structuredClone(context);
      expect(isRevisionContextShape(context)).toBe(true); expect(sameContext(context, copy)).toBe(true);
      expect(contextRevision(context)).toBe(context.scenarioRevisionId);
      if (mode === 'id') copy.scenarioId = 'foreign'; if (mode === 'revision') copy.scenarioRevisionId = 'foreign';
      if (mode === 'overlay') copy.overlayDigest.value = 'a'.repeat(64);
      if (mode === 'base') copy.baseContext.revisionId = 'foreign';
      if (mode === 'branch') copy.branch.branchId = 'foreign';
      if (mode === 'nested') copy.baseContext = structuredClone(context);
      if (mode === 'extra') copy.revisionId = 'mixed';
      expect(sameContext(context, copy)).toBe(false);
      if (['branch', 'nested', 'extra'].includes(mode)) expect(isRevisionContextShape(copy)).toBe(false);
    });
  it.each(['key', 'format', 'base-input', 'scenario-input', 'draft-input', 'overlay-digest', 'overlay-text', 'duplicate', 'target', 'kind'])(
    'rejects substituted %s metadata across all three members', async mode => {
      const b = raw(); for (const c of b.cases) {
        const p = c.preparedScenario;
        if (mode === 'key') p.scenarioCaseKey = 'missing';
        if (mode === 'format') p.draftPlan.format = 'future';
        if (mode === 'base-input') p.draftPlan.baseInputDigest.value = 'a'.repeat(64);
        if (mode === 'scenario-input') p.draftPlan.scenarioInputDigest.value = 'a'.repeat(64);
        if (mode === 'draft-input') p.draftPlan.afterInputDigest.value = 'a'.repeat(64);
        if (mode === 'overlay-digest') p.overlay.digest.value = 'a'.repeat(64);
        if (mode === 'overlay-text') p.overlay.text = '{}';
        if (mode === 'duplicate') p.overlay.text = p.overlay.text.replace('"baseContext":', '"baseContext":{},"baseContext":');
        if (mode === 'target' || mode === 'kind') { const o = JSON.parse(p.overlay.text); if (mode === 'target') o.overrides[0].target.entityId = 'foreign'; else o.overrides[0].kind = 'GrantPermission'; p.overlay.text = JSON.stringify(o); }
      }
      await expect(async () => readPreparedScenario(readPreviewBundle(b), b.cases[0])).rejects.toThrow();
    });
  it.each(['stage', 'source', 'base', 'overlay', 'target', 'kind', 'port', 'assumption', 'value', 'extra', 'missing', 'duplicate', 'hash', 'duplicate-json'])(
    'rejects rehashed %s substitutions in the Core applied-value projection', async mode => {
      const b = raw(); const p = b.cases[0].preparedScenario;
      const envelope = p.appliedValues.scenario; const data = JSON.parse(envelope.text); const row = data.values[0];
      const hash = async (domain: string, text: string) => ({ algorithm: 'sha-256', value: Array.from(new Uint8Array(await webcrypto.subtle.digest('SHA-256', new TextEncoder().encode(domain + '\0' + text))), byte => byte.toString(16).padStart(2, '0')).join('') });
      if (mode === 'stage') data.afterInputDigest = b.cases[2].inputDigest;
      if (mode === 'source') data.afterSourceDigest = b.cases[0].sourceDigest;
      if (mode === 'base') data.baseInputDigest = b.cases[1].inputDigest;
      if (mode === 'overlay') data.overlayDigest.value = 'a'.repeat(64);
      if (mode === 'target') row.target.context = b.cases[0].context;
      if (mode === 'kind') row.kind = 'MilestoneTarget';
      if (mode === 'port') row.portId = null;
      if (mode === 'assumption') { const declared = JSON.parse(row.assumption.text); declared.value.value = false; row.assumption.text = JSON.stringify(declared); row.assumption.digest = await hash('coglatas.local-scenario-assumption/1', row.assumption.text); }
      if (mode === 'value') { const applied = JSON.parse(row.value.text); applied.value = false; row.value.text = JSON.stringify(applied); row.value.digest = await hash('coglatas.local-scenario-value/1', row.value.text); }
      if (mode === 'extra') row.extra = true;
      if (mode === 'missing') data.values = [];
      if (mode === 'duplicate') data.values.push(structuredClone(row));
      envelope.text = JSON.stringify(data);
      if (mode === 'duplicate-json') envelope.text = envelope.text.replace('"format":', '"format":"future","format":');
      envelope.digest = await hash('coglatas.local-scenario-applied-values/1', envelope.text);
      if (mode === 'hash') envelope.digest.value = 'a'.repeat(64);
      for (const c of b.cases) c.preparedScenario = structuredClone(p);
      await expect(readPreparedScenario(readPreviewBundle(b), b.cases[0])).rejects.toThrow();
    });
  it('refuses an omitted or future projection rather than silently using the old equality check', async () => {
    for (const mode of ['missing', 'future']) {
      const b = raw(); for (const c of b.cases) { if (mode === 'missing') delete c.preparedScenario.appliedValues; else c.preparedScenario.appliedValues.extra = true; }
      await expect(readPreparedScenario(readPreviewBundle(b), b.cases[0])).rejects.toThrow();
    }
  });
  it('invalidates a captured projection when only the original projection bytes are changed', async () => {
    const b = bundle(); const admitted = (await readPreparedScenario(b, b.cases[0]))!;
    const p = b.cases[0].preparedScenario as unknown as { appliedValues: { scenario: { text: string } } };
    p.appliedValues.scenario.text += ' ';
    expect(scenarioMatches(b, b.cases[0], admitted)).toBe(false);
  });
  it('never exposes current comparison after a materialized case or metadata is changed', async () => {
    const store = await ready(); expect(await store.loadScenarioComparison(store.preparedScenario()!)).toBe(true); const admitted = store.scenario()!;
    (store.bundle()!.cases[1].source as Record<string, unknown>)['changed'] = true;
    expect(store.scenario()).toBeNull(); expect(scenarioMatches(store.bundle()!, store.context()!, admitted)).toBe(false);
  });
  it('exports canonical assumptions and enforces baseline to Scenario to Draft without reusing old Tests', async () => {
    const store = await ready(); const p = store.preparedScenario()!;
    expect(await store.exportScenarioAssumptions(p)).toBe(p.overlay.text);
    expect(await store.loadScenarioStage(structuredClone(p), 'scenario')).toBe(false);
    expect(await store.loadScenarioStage(p, 'draft')).toBe(false);
    expect(await store.loadLocalTests()).toBe(true); expect(store.localTests()!.run.verdict).toBe('Fail');
    expect(await store.loadScenarioStage(p, 'scenario')).toBe(true); expect(store.context()!.context.kind).toBe('coglatas.scenario');
    expect(store.localTests()).toBeNull(); expect(store.scenario()!.tests.map(x => x.run.verdict)).toEqual(['Fail', 'Pass', 'Pass']);
    expect(await store.loadScenarioStage(store.preparedScenario()!, 'draft')).toBe(true); expect(store.context()!.context.kind).toBe('coglatas.candidate');
    expect(await store.loadLocalTests()).toBe(true); expect(store.localTests()!.run.verdict).toBe('Pass'); expect(store.sourceTarget()).toBeNull();
  });
  it.each(['comparison', 'export', 'stage'])('discards a newer selection queued before the final %s continuation', async operation => {
    const store = await ready(); const p = store.preparedScenario()!; const admitted = (await readPreparedScenario(store.bundle()!, store.context()!))!;
    const boundary = store as unknown as { admitScenario: () => Promise<typeof admitted> };
    vi.spyOn(boundary, 'admitScenario').mockImplementation(async () => { queueMicrotask(() => { store.choose('tests-scenario-base'); store.loadResult(); }); return admitted; });
    const result = operation === 'comparison' ? await store.loadScenarioComparison(p) : operation === 'export' ? await store.exportScenarioAssumptions(p) : await store.loadScenarioStage(p, 'scenario');
    expect(result).toBe(operation === 'export' ? null : false); expect(store.context()!.key).toBe('tests-scenario-base'); expect(store.scenario()).toBeNull();
  });
  it('cannot restore baseline rows or a candidate after denial interrupts hashing', async () => {
    const store = await ready(); let release!: () => void; const gate = new Promise<void>(resolve => { release = resolve; });
    const original = webcrypto.subtle.digest.bind(webcrypto.subtle);
    vi.spyOn(webcrypto.subtle, 'digest').mockImplementationOnce(async (...args) => { await gate; return original(...args); });
    const pending = store.loadScenarioStage(store.preparedScenario()!, 'scenario'); store.clear('denied'); release();
    expect(await pending).toBe(false); expect(store.state()).toBe('denied'); expect(store.context()).toBeNull(); expect(store.scenario()).toBeNull();
  });
  it('rejects an external copied member before creating a triplet', () => { const b = bundle(); expect(() => scenarioTriplet(b, structuredClone(b.cases[0]))).toThrow(); });
});
