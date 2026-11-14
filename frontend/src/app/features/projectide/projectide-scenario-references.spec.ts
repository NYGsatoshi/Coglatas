/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readPreparedScenario, scenarioMatches } from './projectide-scenario';
import { coreOriginEditsMatch } from './projectide-scenario-origin-edits';
import { readLocalTests } from './projectide-tests.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';
vi.stubGlobal('crypto', webcrypto); afterEach(() => vi.restoreAllMocks());
const raw = () => JSON.parse(readFileSync(resolve('public/assets/projectide/scenario-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(raw());
const hash = async (domain: string, text: string) => ({ algorithm: 'sha-256', value: Array.from(new Uint8Array(await webcrypto.subtle.digest('SHA-256', new TextEncoder().encode(domain + '\0' + text))), byte => byte.toString(16).padStart(2, '0')).join('') });
async function ready(key: string) { const store = new ProjectIdeWorkbenchStore(); await store.load(async () => bundle()); store.choose(key); store.loadResult(); return store; }
describe('Actual Core Scenario reference consumer journeys', () => {
  it.each(['tests-reference', 'tests-nested-reference'])('admits %s with original assumptions, pinned snapshots and fresh Scenario/Draft source navigation', async key => {
    const store = await ready(key + '-base'); const prepared = store.preparedScenario()!;
    expect(await store.loadScenarioComparison(prepared)).toBe(true); const admitted = store.scenario()!;
    expect(admitted.tests.map(x => x.run.verdict)).toEqual(['Fail', 'Pass', 'Pass']);
    expect(await store.exportScenarioAssumptions(prepared)).toBe(prepared.overlay.text);
    expect(await store.loadScenarioStage(prepared, 'scenario')).toBe(true);
    expect(await store.loadLocalTests()).toBe(true); expect(store.localTests()!.run.verdict).toBe('Pass');
    const assertion = store.localTests()!.run.tests[0].assertions[0]; expect(store.navigateTestAssertion(assertion)).toBe(true);
    expect(store.sourceTarget()!.identity).toBe(assertion.location.entityId); expect(store.sourceLocation()!.context.kind).toBe('coglatas.scenario');
    expect(await store.loadScenarioStage(store.preparedScenario()!, 'draft')).toBe(true); expect(store.localTests()).toBeNull();
    expect(await store.loadLocalTests()).toBe(true); const after = store.localTests()!.run.tests[0].assertions[0];
    expect(store.navigateTestAssertion(after)).toBe(true); expect(store.sourceLocation()!.context.kind).toBe('coglatas.candidate');
    expect(store.localTests()!.run.verdict).toBe('Pass'); expect(new Set(admitted.tests.map(x => x.run.compilerInputId)).size).toBe(3);
  });
  it.each(['tests-missing-reference', 'tests-denied-reference'])('keeps %s incomplete and never accepts comparison or inherited PASS', async key => {
    const b = bundle();
    for (const c of b.cases.filter(x => x.key.startsWith(key))) { const admitted = (await readLocalTests(c))!; expect(admitted.run.verdict).not.toBe('Pass'); await expect(readPreparedScenario(b, c)).rejects.toThrow(); }
    const denied = b.cases.find(x => x.key === key + '-hypothesis')!.materializedSnapshots!;
    expect(JSON.stringify(denied)).not.toContain('Pinned reference hypothesis');
  });
  it.each(['absent', 'missing', 'unavailable', 'id', 'version', 'context', 'digest', 'body', 'duplicate', 'extra', 'coverage', 'oversize'])(
    'rejects %s snapshot substitution even when original local result bytes still say PASS', async mode => {
      const b = raw(); const c = b.cases.find((x: { key: string }) => x.key === 'tests-reference-hypothesis'); const p = c.materializedSnapshots; const entry = p.entries[0];
      if (mode === 'absent') delete c.materializedSnapshots;
      if (mode === 'missing') p.entries = [];
      if (mode === 'unavailable') p.entries = [{ snapshotId: entry.snapshotId, version: entry.version, context: entry.context, admission: 'Unavailable' }];
      if (mode === 'id') entry.snapshotId = 'foreign'; if (mode === 'version') entry.version = '2';
      if (mode === 'context') entry.context.revisionId = 'foreign'; if (mode === 'digest') entry.source.digest.value = 'a'.repeat(64);
      if (mode === 'body') entry.source.text += ' '; if (mode === 'duplicate') p.entries.push(structuredClone(entry));
      if (mode === 'extra') entry.currentAuthority = true; if (mode === 'coverage') entry.declarationsComplete = false;
      if (mode === 'oversize') entry.source.text = 'x'.repeat(4194305);
      await expect(readLocalTests(readPreviewBundle(b).cases.find(x => x.key === c.key)!)).rejects.toThrow();
    });
  it.each(['omit', 'duplicate', 'pin-path', 'future', 'context', 'pin-value', 'fake-set'])(
    'rejects rehashed %s transformations without guessing typed paths or moving pinned targets', async mode => {
      const b = raw(); const p = b.cases.find((x: { key: string }) => x.key === 'tests-reference-base').preparedScenario;
      const envelope = p.appliedValues.scenario; const data = JSON.parse(envelope.text); const row = data.values.find((x: { originEdits: unknown[] }) => x.originEdits.length);
      if (mode === 'omit') row.originEdits = []; if (mode === 'duplicate') row.originEdits.push(structuredClone(row.originEdits[0]));
      if (mode === 'pin-path') row.originEdits[0].path = '/value/context'; if (mode === 'future') row.originEdits[0].kind = 'ResolveReference';
      if (mode === 'context' || mode === 'pin-value') { const value = JSON.parse(row.value.text); if (mode === 'context') value.value.origin.context = b.cases[3].context; else value.value.entityId = 'foreign'; row.value.text = JSON.stringify(value); row.value.digest = await hash('coglatas.local-scenario-value/1', row.value.text); }
      if (mode === 'fake-set') row.originEdits = [{ kind: 'SetOrder', path: '/value', order: [] }];
      envelope.text = JSON.stringify(data); envelope.digest = await hash('coglatas.local-scenario-applied-values/1', envelope.text);
      for (const c of b.cases.filter((x: { key: string }) => x.key.startsWith('tests-reference-'))) c.preparedScenario = structuredClone(p);
      await expect(readPreparedScenario(readPreviewBundle(b), b.cases[3])).rejects.toThrow();
    });
  it('invalidates captured Tests and Scenario after only materialized snapshot bytes change', async () => {
    const b = bundle(); const c = b.cases.find(x => x.key === 'tests-reference-hypothesis')!; const admitted = (await readPreparedScenario(b, c))!;
    const materialized = c.materializedSnapshots as unknown as { entries: { source: { text: string } }[] }; materialized.entries[0].source.text += ' ';
    expect(scenarioMatches(b, c, admitted)).toBe(false);
  });
  it.each(['deny', 'choose'])('cannot publish a reference journey when %s interrupts original snapshot hashing', async mode => {
    const store = await ready('tests-reference-base'); let release!: () => void; const wait = new Promise<void>(resolve => { release = resolve; });
    const original = webcrypto.subtle.digest.bind(webcrypto.subtle); vi.spyOn(webcrypto.subtle, 'digest').mockImplementationOnce(async (...args) => { await wait; return original(...args); });
    const pending = store.loadScenarioComparison(store.preparedScenario()!);
    if (mode === 'deny') store.clear('denied'); else { store.choose('tests-nested-reference-base'); store.loadResult(); }
    release(); expect(await pending).toBe(false); expect(store.scenario()).toBeNull();
  });
  it('refuses edits inside an opaque Unknown basis even when it contains reference-shaped JSON', () => {
    const b = bundle(); const p = b.cases[3].preparedScenario!; const row = JSON.parse(p.appliedValues.scenario.text).values.find((x: { originEdits: unknown[] }) => x.originEdits.length);
    const declared = JSON.parse(row.assumption.text).value; const applied = JSON.parse(row.value.text);
    const unknown = { type: declared.type, state: 'coglatas.unknown', basis: { opaque: declared } };
    const changed = { ...unknown, basis: { opaque: applied } };
    expect(coreOriginEditsMatch(unknown, changed, [{ kind: 'OriginContext', path: '/basis/opaque/value/origin/context' }], b.cases[3].context, b.cases[4].context)).toBe(false);
    expect(coreOriginEditsMatch(unknown, unknown, [], b.cases[3].context, b.cases[4].context)).toBe(true);
  });
  it('validates only a Core-announced Set permutation and refuses a list permutation or pin change', () => {
    const b = bundle(); const p = b.cases[3].preparedScenario!; const row = JSON.parse(p.appliedValues.scenario.text).values.find((x: { originEdits: unknown[] }) => x.originEdits.length);
    const declared = JSON.parse(row.assumption.text).value; const applied = JSON.parse(row.value.text);
    const type = { kind: 'coglatas.set', identity: 'coglatas.canonical-value' }; const before = { type, state: 'coglatas.known', value: [declared, { example: true }] };
    const after = { type, state: 'coglatas.known', value: [{ example: true }, applied] };
    const edits = [{ kind: 'OriginContext', path: '/value/0/value/origin/context' }, { kind: 'SetOrder', path: '/value', order: [1, 0] }];
    expect(coreOriginEditsMatch(before, after, edits, b.cases[3].context, b.cases[4].context)).toBe(true);
    expect(coreOriginEditsMatch({ ...before, type: { kind: 'coglatas.list' } }, after, edits, b.cases[3].context, b.cases[4].context)).toBe(false);
    const altered = structuredClone(after); altered.value[1].value.entityId = 'foreign'; expect(coreOriginEditsMatch(before, altered, edits, b.cases[3].context, b.cases[4].context)).toBe(false);
  });
});
