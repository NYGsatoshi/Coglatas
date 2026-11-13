/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { assessLocalEvidence, sameContext } from './projectide.contracts';
import { readPreviewBundle } from './projectide-preview.adapter';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

const raw = () => JSON.parse(readFileSync('public/assets/projectide/preview-v1.json', 'utf8'));
async function ready() {
  const bundle = readPreviewBundle(raw()); const before = bundle.cases.find(item => item.key === 'draft-before')!;
  const after = bundle.cases.find(item => item.key === 'draft-after')!;
  const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle); store.choose(before.key); store.loadResult();
  const record = before.declarations.find(item => item.kind === 'coglatas.task' && item.recordType === 'entity')!;
  if (record.recordType !== 'entity') throw new Error('Expected a Task entity');
  expect(store.select({ kind: 'entity', id: record.identity })).toBe(true);
  return { store, before, after, record };
}

describe('Core-prepared Draft Rename and candidate contexts', () => {
  it('loads the whole prepared Draft pair, preserving entity identity and discarding old selections', async () => {
    const { store, before, after, record } = await ready(); const prepared = store.preparedRename()!;
    expect(prepared.plan.previousName).toBe('Observe 🌌'); expect(prepared.plan.newName).toBe('Observe renamed 🌌');
    expect(before.context.kind).toBe('coglatas.candidate'); expect(after.context.kind).toBe('coglatas.candidate');
    expect(sameContext(before.context, after.context)).toBe(false); expect(assessLocalEvidence(before)).toBe('complete-local-pass');
    expect(store.loadPreparedRename(prepared)).toBe(true);
    expect(store.result()).toBe(after); expect(store.context()).toBe(after); expect(store.selection()).toBeNull();
    expect(store.sourceTarget()).toBeNull(); expect(store.preparedRename()).toBeNull();
    expect(after.declarations.find(item => item.identity === record.identity)?.data['displayName']).toBe('Observe renamed 🌌');
    expect(record.data['displayName']).toBe('Observe 🌌'); expect(store.loadPreparedRename(prepared)).toBe(false);
  });

  it.each(['before-input', 'after-input', 'old-name', 'new-name', 'target', 'origins', 'coverage', 'missing-after', 'proposal', 'base', 'head',
    'after-references', 'after-origin', 'after-target', 'after-coverage'])
  ('rejects an inconsistent %s pair before exposing it', mode => {
    const data = raw(); const before = data.cases.find((item: { key: string }) => item.key === 'draft-before');
    const prepared = before.preparedRename; const plan = prepared.plan;
    if (mode === 'before-input') plan.beforeInputDigest = before.sourceDigest;
    if (mode === 'after-input') plan.afterInputDigest = before.inputDigest;
    if (mode === 'old-name') plan.previousName = 'injected';
    if (mode === 'new-name') plan.newName = 'injected';
    if (mode === 'target') plan.target.entityId = before.context.proposalId;
    if (mode === 'origins') plan.referenceOrigins = [];
    if (mode === 'coverage') plan.referenceCoverageComplete = !plan.referenceCoverageComplete;
    if (mode === 'missing-after') prepared.afterCaseKey = 'pass';
    if (mode === 'proposal') plan.afterContext.proposalId = before.context.baseRevision.revisionId;
    if (mode === 'base') plan.beforeContext.baseRevision.revisionId = before.context.proposalId;
    if (mode === 'head') plan.afterContext.capturedTargetHead.revisionId = before.context.proposalId;
    const after = data.cases.find((item: { key: string }) => item.key === 'draft-after');
    if (mode === 'after-references') after.semanticInspection.references = [];
    if (mode === 'after-origin') after.semanticInspection.references[0].origin.propertyPath = '/different-origin';
    if (mode === 'after-target') after.semanticInspection.references[0].symbolKey = after.semanticInspection.symbols.find((item: { kind: string }) => item.kind === 'coglatas.project').key;
    if (mode === 'after-coverage') after.semanticInspection.bindingComplete = !plan.referenceCoverageComplete;
    expect(() => readPreviewBundle(data)).toThrow();
  });

  it.each(['mixed', 'missing-base', 'wrong-branch', 'future', 'scenario'])('refuses malformed %s contexts', mode => {
    const data = raw(); const context = data.cases.find((item: { key: string }) => item.key === 'draft-before').context;
    if (mode === 'mixed') context.revisionId = context.candidateRevisionId;
    if (mode === 'missing-base') delete context.baseRevision;
    if (mode === 'wrong-branch') context.capturedTargetHead.branch.branchId = context.proposalId;
    if (mode === 'future') context.futureMeaning = true;
    if (mode === 'scenario') context.kind = 'coglatas.scenario';
    expect(() => readPreviewBundle(data)).toThrow();
  });

  it('rechecks the exact retained pair and current selection on activation', async () => {
    const { store, before, after } = await ready(); const prepared = store.preparedRename()!;
    expect(store.loadPreparedRename({ ...prepared })).toBe(false);
    Object.assign(after, { inputDigest: before.inputDigest });
    expect(store.loadPreparedRename(prepared)).toBe(false); expect(store.context()).toBe(before);
    store.clearSelection(); expect(store.preparedRename()).toBeNull(); expect(store.loadPreparedRename(prepared)).toBe(false);
    store.choose('pass'); store.loadResult(); expect(store.loadPreparedRename(prepared)).toBe(false);
    store.clear('denied'); expect(store.preparedRename()).toBeNull();
  });

  it('treats malformed manifest candidate contexts as stale evidence without throwing', async () => {
    const { before } = await ready();
    const manifest = before.manifest['manifest'] as Record<string, unknown>;
    const context = manifest['sourceContext'] as Record<string, unknown>;
    // The manifest Source stamp is validated by the common evidence reader, not inferred from a candidate ID.
    expect(assessLocalEvidence(before)).toBe('complete-local-pass');
    Object.assign(context, { baseRevision: null });
    expect(() => assessLocalEvidence(before)).not.toThrow(); expect(assessLocalEvidence(before)).toBe('stale');
  });
  it('rechecks the retained after-reference roster when the prepared pair is activated', async () => {
    const { store, before, after } = await ready(); const prepared = store.preparedRename()!;
    Object.assign(after.semanticInspection!, { references: [] });
    expect(store.loadPreparedRename(prepared)).toBe(false); expect(store.context()).toBe(before);
  });
  it('keeps a legacy projection inert when its optional inspection is absent', async () => {
    const data = raw(); for (const item of data.cases) delete item.semanticInspection;
    const bundle = readPreviewBundle(data); const before = bundle.cases.find(item => item.key === 'draft-before')!;
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle); store.choose(before.key); store.loadResult();
    const task = before.declarations.find(item => item.kind === 'coglatas.task' && item.recordType === 'entity')!;
    if (task.recordType !== 'entity') throw new Error('Expected a Task entity');
    store.select({ kind: 'entity', id: task.identity });
    expect(store.preparedRename()).toBeNull(); expect(store.loadPreparedRename(before.preparedRename!)).toBe(false);
  });
});
