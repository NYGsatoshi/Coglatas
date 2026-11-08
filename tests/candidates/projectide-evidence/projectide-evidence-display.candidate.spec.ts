/// <reference types="node" />
import { describe, expect, it } from 'vitest';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { assessLocalEvidence, hasCompleteCurrentPass } from './projectide.contracts';
import type { PreviewBundle, PreviewCase } from './projectide.contracts';
import '@angular/compiler';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

const fixture = (): PreviewCase => {
  const raw = JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8'));
  // Fixture was already exported by Core; this isolated suite tests presentation only.
  const value = raw as PreviewBundle;
  const current = value.cases.find((c) => c.key === 'pass');
  if (!current) throw new Error('Required .NET synthetic PASS fixture missing');
  return current;
};
const altered = (apply: (value: PreviewCase) => void): PreviewCase => {
  const value = structuredClone(fixture());
  apply(value);
  return value;
};
const bundle = (...cases: PreviewCase[]): PreviewBundle => ({
  format: 'coglatas.web-preview/1', mode: 'synthetic-fixture', coreProduct: 'independent-test', cases,
});

describe('Independent local Core evidence display qualification', () => {
  it('requires explicit boolean completeness, including when loading is bypassed', () => {
    for (const evidenceComplete of [undefined, null, 'true', 1]) {
      expect(assessLocalEvidence(altered((v) => Object.assign(v.result, { evidenceComplete })))).toBe('missing-evidence');
    }
  });

  it('never shows green for unsupported source records or unresolved inspection inputs', () => {
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.declarations[0], { status: 'Unsupported' })))).toBe('partial-coverage');
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.semanticInspection!, { bindingComplete: false })))).toBe('partial-coverage');
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.semanticInspection!.symbols[0], { status: 'Invalid' })))).toBe('partial-coverage');
  });

  it('checks Tenant, Project, Branch and Revision independently of a matching digest', () => {
    for (const key of ['tenantId', 'projectId', 'branchId']) {
      expect(assessLocalEvidence(altered((v) =>
        Object.assign(v.desiredContext.branch, { [key]: 'foreign' })))).toBe('stale');
      expect(assessLocalEvidence(altered((v) =>
        Object.assign(v.result.boundInputStamp.sourceContext.branch, { [key]: 'foreign' })))).toBe('stale');
    }
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.result.boundInputStamp.sourceContext, { revisionId: 'foreign' })))).toBe('stale');
  });

  it('checks the separately supplied manifest envelope and Source identity', () => {
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.manifest, { compilerInputId: 'foreign' })))).toBe('stale');
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.manifest, { inputDigest: { algorithm: 'sha-256', value: 'b'.repeat(64) } })))).toBe('stale');
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.manifest['manifest'] as object, { sourceDigest: { algorithm: 'sha-256', value: 'b'.repeat(64) } })))).toBe('stale');
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.manifest['manifest'] as object, { sourceContext: null })))).toBe('stale');
  });

  it('clears an accepted pass before reloading, cancellation and context reselection', async () => {
    const store = new ProjectIdeWorkbenchStore();
    const pass = fixture();
    const data = bundle(pass);
    await store.load(async () => data);
    store.choose(pass.key);
    store.loadResult();
    expect(assessLocalEvidence(store.result())).toBe('complete-local-pass');
    store.choose('missing');
    expect(assessLocalEvidence(store.result())).toBe('not-loaded');
    store.choose(pass.key);
    store.loadResult();
    let finish!: (value: PreviewBundle) => void;
    const pending = store.load(() => new Promise((resolve) => { finish = resolve; }));
    expect(assessLocalEvidence(store.result())).toBe('not-loaded');
    store.clear('cancelled');
    finish(data);
    await pending;
    expect(store.state()).toBe('cancelled');
    expect(store.bundle()).toBeNull();
    expect(assessLocalEvidence(store.result())).toBe('not-loaded');
  });

  it('rejects a delayed older read even if the adapter ignores its abort signal', async () => {
    const store = new ProjectIdeWorkbenchStore();
    const oldBundle = bundle(fixture());
    const nextBundle = bundle();
    let finish!: (value: PreviewBundle) => void;
    const old = store.load(() => new Promise((resolve) => { finish = resolve; }));
    await store.load(async () => nextBundle);
    finish(oldBundle);
    await old;
    expect(store.bundle()).toBe(nextBundle);
    expect(store.context()).toBeNull();
    expect(assessLocalEvidence(store.result())).toBe('not-loaded');
  });

  it('never treats an unloaded result or stale desired revision as a local pass', () => {
    expect(assessLocalEvidence(null)).toBe('not-loaded');
    expect(assessLocalEvidence(altered((v) => Object.assign(v, { freshness: 'Stale' })))).toBe('stale');
    expect(hasCompleteCurrentPass(altered((v) => Object.assign(v, { freshness: 'Stale' })))).toBe(false);
  });

  it('distinguishes all execution, coverage, completeness and verdict axes', () => {
    expect(assessLocalEvidence(fixture())).toBe('complete-local-pass');
    expect(assessLocalEvidence(altered((v) => Object.assign(v.result, {
      execution: 'coglatas.cancelled',
    })))).toBe('not-executed');
    expect(assessLocalEvidence(altered((v) => Object.assign(v.result, {
      coverage: 'coglatas.partial',
    })))).toBe('partial-coverage');
    expect(assessLocalEvidence(altered((v) => Object.assign(v.result, {
      evidenceComplete: false,
    })))).toBe('missing-evidence');
    expect(assessLocalEvidence(altered((v) => Object.assign(v.result, {
      verdict: 'coglatas.fail',
    })))).toBe('not-passed');
  });

  it('rejects forged compiler input identity even with a nominal PASS verdict', () => {
    const forged = altered((v) => Object.assign(v.result, { compilerInputId: 'foreign' }));
    expect(assessLocalEvidence(forged)).toBe('stale');
    expect(hasCompleteCurrentPass(forged)).toBe(false);
  });

  it('rejects substituted source or manifest digests and desired context', () => {
    const bad = 'b'.repeat(64);
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.result.boundInputStamp.sourceDigest, { value: bad })))).toBe('stale');
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.result.boundInputStamp.inputDigest, { value: bad })))).toBe('stale');
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.result.inputDigest, { value: bad })))).toBe('stale');
    expect(assessLocalEvidence(altered((v) =>
      Object.assign(v.desiredContext, { revisionId: 'different' })))).toBe('stale');
  });
});
