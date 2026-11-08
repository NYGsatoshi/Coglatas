/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { assessLocalEvidence, hasCompleteCurrentPass, PreviewBundle, PreviewCase } from './projectide.contracts';
import { readPreviewBundle } from './projectide-preview.adapter';

const fixture = (): PreviewCase => {
  const raw = JSON.parse(readFileSync(resolve('public/assets/projectide/preview-v1.json'), 'utf8'));
  const value = readPreviewBundle(raw) as PreviewBundle;
  const current = value.cases.find((c) => c.key === 'pass');
  if (!current) throw new Error('Required .NET synthetic PASS fixture missing');
  return current;
};
const altered = (apply: (value: PreviewCase) => void): PreviewCase => {
  const value = structuredClone(fixture());
  apply(value);
  return value;
};

describe('Independent local Core evidence display qualification', () => {
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
