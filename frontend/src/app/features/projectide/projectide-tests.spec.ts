/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readLocalTests } from './projectide-tests.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());
const raw = () => JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8'));
const current = (key = 'tests-pass') => raw().cases.find((item: { key: string }) => item.key === key)!;
const hash = (domain: string, value: string) => ({ algorithm: 'sha-256', value: createHash('sha256').update(domain + '\0' + value).digest('hex') });

describe('Core-produced local Project Test projection admission', () => {
  it.each([
    ['tests-pass', 'Completed', 'Complete', 'Pass', 'Current', 1],
    ['tests-fail', 'Completed', 'Complete', 'Fail', 'Current', 1],
    ['tests-unknown', 'Completed', 'Partial', 'Inconclusive', 'Current', 1],
    ['tests-empty', 'NotRun', 'None', 'NotRun', 'Current', 0],
    ['tests-unsupported', 'Completed', 'Partial', 'NotRun', 'Current', 0],
    ['tests-cancelled', 'Cancelled', 'None', 'NotRun', 'Current', 0],
    ['tests-stale', 'NotRun', 'None', 'NotRun', 'Stale', 0],
    ['tests-evaluator-mismatch', 'NotRun', 'None', 'NotRun', 'Current', 0],
    ['tests-error', 'Completed', 'Partial', 'Error', 'Current', 1],
    ['tests-exhausted', 'ResourceExhausted', 'None', 'NotRun', 'Current', 0],
    ['tests-unicode', 'Completed', 'Complete', 'Pass', 'Current', 1],
    ['tests-many', 'Completed', 'Complete', 'Pass', 'Current', 33],
  ])('admits the actual %s result without coercing its axes', async (key, execution, coverage, verdict, freshness, count) => {
    const value = readPreviewBundle(raw()).cases.find(item => item.key === key)!;
    const admitted = (await readLocalTests(value))!;
    expect(admitted.run).toMatchObject({ execution, coverage, verdict, freshness, executedAssertions: count });
    expect(Object.isFrozen(admitted.run)).toBe(true);
    expect(Object.isFrozen(admitted.projection)).toBe(true);
    expect(value.result.verdict).toBe(key === 'tests-unsupported' ? 'coglatas.inconclusive' : 'coglatas.pass');
  });

  it.each(['input', 'source', 'plan', 'enum', 'future', 'zero-pass', 'partial-pass', 'stale-pass', 'entity', 'path', 'duplicate-test',
    'duplicate-assertion', 'counter', 'hidden-failure', 'original-assertion', 'original-declaration', 'engine', 'bytes', 'duplicate-member', 'too-deep', 'unicode', 'input-bytes', 'source-bytes', 'document', 'duplicate-assertion-original'])(
    'rejects a substituted %s even when the replacement result digest is recomputed', async mode => {
      const value = current(mode === 'hidden-failure' ? 'tests-fail' : 'tests-pass');
      const p = value.localProjectTests; const run = JSON.parse(p.result.text);
      if (mode === 'input') run.compilerInputId = 'foreign';
      if (mode === 'input-bytes') p.inputText += ' ';
      if (mode === 'source-bytes') value.sourceText += ' ';
      if (mode === 'source') run.sourceDigest.value = 'a'.repeat(64);
      if (mode === 'plan') run.planDigest.value = 'a'.repeat(64);
      if (mode === 'enum') run.verdict = 'coglatas.pass';
      if (mode === 'future') run.execution = 'Future';
      if (mode === 'zero-pass') { run.executedAssertions = 0; run.tests = []; }
      if (mode === 'partial-pass') run.coverage = 'Partial';
      if (mode === 'stale-pass') run.freshness = 'Stale';
      if (mode === 'document') {
        const id = run.tests[0].testId; const declaration = value.declarations.find((item: { identity: string }) => item.identity === id);
        declaration.location.documentId = 'foreign'; run.tests[0].assertions[0].location.documentId = 'foreign';
      }
      if (mode === 'entity') run.tests[0].assertions[0].location.entityId = 'foreign';
      if (mode === 'path') run.tests[0].assertions[0].location.propertyPath += '/target';
      if (mode === 'duplicate-test') run.tests.push(run.tests[0]);
      if (mode === 'duplicate-assertion') { run.tests[0].assertions.push(run.tests[0].assertions[0]); run.executedAssertions++; }
      if (mode === 'counter') run.executedAssertions++;
      if (mode === 'hidden-failure') { run.verdict = 'Pass'; run.tests[0].verdict = 'Pass'; }
      if (mode === 'duplicate-assertion-original') {
        p.assertions[0].text = p.assertions[0].text.replace('"key":"example.ready"', '"key":"example.ready","key":"example.ready"');
        run.tests[0].assertions[0].assertionDigest = hash('coglatas.project-test-assertion/1', p.assertions[0].text);
      }
      if (mode === 'original-assertion') p.assertions[0].text = p.assertions[0].text.replace('example.ready', 'example.other');
      if (mode === 'original-declaration') p.declarations[0].text = '{}';
      if (mode === 'engine') {
        const plan = JSON.parse(p.plan.text); plan.engine.version = '2'; p.plan.text = JSON.stringify(plan);
        p.plan.digest = hash('coglatas.project-test-plan/1', p.plan.text); run.planDigest = p.plan.digest;
      }
      if (mode === 'too-deep') { let nested: unknown = null; for (let i = 0; i < 65; i++) nested = { child: nested }; run.future = nested; }
      if (mode === 'unicode') run.reason = String.fromCharCode(0xd800);
      p.result.text = JSON.stringify(run);
      if (mode === 'duplicate-member') p.result.text = p.result.text.replace('"verdict":"Pass"', '"verdict":"Pass","verdict":"Pass"'); p.result.digest = hash('coglatas.project-test-run/1', p.result.text);
      if (mode === 'bytes') p.result.text += ' ';
      await expect(readLocalTests(value)).rejects.toThrow();
    });
  it('keeps missing projections unavailable', async () => {
    expect(await readLocalTests(readPreviewBundle(raw()).cases[0])).toBeNull();
  });
});

describe('Local Test store lifecycle', () => {
  async function ready(key = 'tests-fail') {
    const store = new ProjectIdeWorkbenchStore(); await store.load(async () => readPreviewBundle(raw()));
    store.choose(key); store.loadResult(); expect(await store.loadLocalTests()).toBe(true); return store;
  }
  it('opens an admitted assertion at its exact Source location and preserves Compiler PASS and Test FAIL', async () => {
    const store = await ready(); const run = store.localTests()!.run;
    const assertion = run.tests[0].assertions[0]; expect(store.navigateTestAssertion(assertion)).toBe(true);
    expect(store.sourceLocation()).toEqual(assertion.location); expect(store.sourceTarget()!.identity).toBe(run.tests[0].testId);
    expect(store.result()!.result.verdict).toBe('coglatas.pass'); expect(run.verdict).toBe('Fail');
  });
  it.each(['load-result', 'context', 'denied', 'reload'])('clears Test state and old assertion navigation on %s', async mode => {
    const store = await ready(); const assertion = store.localTests()!.run.tests[0].assertions[0];
    expect(store.navigateTestAssertion(assertion)).toBe(true);
    if (mode === 'load-result') store.loadResult();
    if (mode === 'context') store.choose('branch-pass');
    if (mode === 'denied') store.clear('denied');
    if (mode === 'reload') await store.load(async () => readPreviewBundle(raw()));
    expect(store.localTests()).toBeNull(); expect(store.sourceTarget()).toBeNull();
    expect(store.navigateTestAssertion(assertion)).toBe(false);
  });
  it('rejects an earlier admitted assertion even when stable IDs are reused', async () => {
    const store = await ready(); const assertion = store.localTests()!.run.tests[0].assertions[0];
    expect(await store.loadLocalTests()).toBe(true);
    expect(store.navigateTestAssertion(assertion)).toBe(false); expect(store.sourceTarget()).toBeNull();
  });
  it('does not retain a result whose original projection was mutated', async () => {
    const store = await ready(); const assertion = store.localTests()!.run.tests[0].assertions[0];
    Object.assign(store.context()!.localProjectTests!.result, { text: '{}' });
    expect(store.localTests()).toBeNull(); expect(store.navigateTestAssertion(assertion)).toBe(false);
    expect(store.sourceTarget()).toBeNull();
  });
  it.each(['sourceMap', 'source', 'manifest', 'declarations'] as const)('invalidates an admitted result after %s materialization replacement', async field => {
    const store = await ready(); const assertion = store.localTests()!.run.tests[0].assertions[0];
    const context = store.context()!; Object.assign(context, { [field]: structuredClone(context[field]) });
    expect(store.localTests()).toBeNull(); expect(store.navigateTestAssertion(assertion)).toBe(false); expect(store.sourceTarget()).toBeNull();
  });
  it('invalidates a result after a nested Source mutation without a signal replacement', async () => {
    const store = await ready(); const assertion = store.localTests()!.run.tests[0].assertions[0];
    Object.assign(store.context()!.source.documents[0], { logicalName: 'changed' });
    expect(store.localTests()).toBeNull(); expect(store.navigateTestAssertion(assertion)).toBe(false); expect(store.sourceTarget()).toBeNull();
  });
  it('invalidates an admitted report when both context headers are replaced in place', async () => {
    const store = await ready(); const assertion = store.localTests()!.run.tests[0].assertions[0];
    const other = readPreviewBundle(raw()).cases.find(item => item.key === 'branch-pass')!.context;
    Object.assign(store.context()!, { context: other, desiredContext: other });
    expect(store.localTests()).toBeNull(); expect(store.navigateTestAssertion(assertion)).toBe(false); expect(store.sourceTarget()).toBeNull();
  });
  it('rejects a late digest admission after permission loss', async () => {
    const store = await ready(); let release!: () => void;
    const gate = new Promise<void>(resolve => { release = resolve; });
    const original = webcrypto.subtle.digest.bind(webcrypto.subtle);
    vi.spyOn(webcrypto.subtle, 'digest').mockImplementationOnce(async (...args) => { await gate; return original(...args); });
    const pending = store.loadLocalTests(); expect(store.testReadState()).toBe('loading');
    store.clear('denied'); release(); expect(await pending).toBe(false);
    expect(store.state()).toBe('denied'); expect(store.localTests()).toBeNull(); expect(store.sourceTarget()).toBeNull();
  });
});
