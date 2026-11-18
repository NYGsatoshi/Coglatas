/// <reference types="node" />
import { createHash, webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { Digest, PreviewBundle, PreviewCase } from './projectide.contracts';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readScopedSources, scopedSourcesMatch } from './projectide-scoped-sources';
import { readSimulationRun } from './projectide-simulation.reader';
import { readSimulationComparison } from './projectide-simulation-comparison.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());

function bundle(): PreviewBundle {
  return readPreviewBundle(JSON.parse(readFileSync(resolve('public/assets/projectide/simulation-comparison-v1.json'), 'utf8')));
}

function member(data: PreviewBundle, key: string): PreviewCase {
  const value = data.cases.find(item => item.key === key);
  if (!value) throw new Error('missing-original-Core-comparison-case: ' + key);
  return value;
}

async function admitRun(value: PreviewCase) {
  const sources = await readScopedSources(value);
  expect(sources).not.toBeNull();
  const run = await readSimulationRun(value, sources!);
  expect(run).not.toBeNull();
  return run!;
}

async function pair(family = 'root', revision = 'base') {
  const data = bundle();
  const beforeCase = member(data, 'tests-simulation-' + family + '-' + revision);
  const afterCase = member(data, beforeCase.simulationComparison!.afterCaseKey);
  return { data, beforeCase, afterCase, before: await admitRun(beforeCase), after: await admitRun(afterCase),
    beforeRunText: beforeCase.simulationRun!.text, afterRunText: afterCase.simulationRun!.text };
}

function hash(text: string): Digest {
  return { algorithm: 'sha-256', value: createHash('sha256').update('coglatas.local-simulation-comparison/1\0' + text).digest('hex') };
}

function replaceFrame(value: PreviewCase, data: unknown): void {
  const text = JSON.stringify(data);
  Object.assign(value.simulationComparison!, { text, digest: hash(text) });
}

async function expectFrameRefusalAfterFreshAdmission(value: Awaited<ReturnType<typeof pair>>,
  expectedDigestCalls = 1, reason = 'unsupported-simulation-comparison'): Promise<void> {
  // A comparison-frame edit also changes the whole-case Source fingerprint.
  // Re-admit the unchanged original recordings so these checks reach the frame boundary.
  const before = await admitRun(value.beforeCase);
  const after = await admitRun(value.afterCase);
  expect(scopedSourcesMatch(value.beforeCase, before.sources)).toBe(true);
  expect(scopedSourcesMatch(value.afterCase, after.sources)).toBe(true);
  expect(value.beforeCase.simulationRun!.text).toBe(value.beforeRunText);
  expect(value.afterCase.simulationRun!.text).toBe(value.afterRunText);
  expect(before.data).toEqual(value.before.data);
  expect(after.data).toEqual(value.after.data);
  expect(before.digest).toEqual(value.before.digest);
  expect(after.digest).toEqual(value.after.digest);
  const digest = webcrypto.subtle.digest.bind(webcrypto.subtle);
  const observed = vi.spyOn(webcrypto.subtle, 'digest').mockImplementation(async (...arguments_) => {
    expect(arguments_[0]).toBe('SHA-256');
    expect(arguments_[1]).toEqual(new TextEncoder().encode('coglatas.local-simulation-comparison/1\0' + value.beforeCase.simulationComparison!.text));
    return digest(...arguments_);
  });
  try {
    await expect(readSimulationComparison(value.beforeCase, before, value.afterCase, after)).rejects.toThrow(reason);
    expect(observed).toHaveBeenCalledTimes(expectedDigestCalls);
  } finally {
    observed.mockRestore();
  }
}

async function ready(family = 'external') {
  const store = new ProjectIdeWorkbenchStore();
  await store.load(async () => bundle());
  store.choose('tests-simulation-' + family + '-base');
  store.loadResult();
  expect(await store.loadScopedSources()).toBe(true);
  expect(await store.loadSimulation()).toBe(true);
  return store;
}

function holdNextDigest(): { entered: Promise<void>; release: () => void } {
  let release!: () => void;
  let markEntered!: () => void;
  const entered = new Promise<void>(resolveEntered => { markEntered = resolveEntered; });
  const blocked = new Promise<void>(resolveGate => { release = resolveGate; });
  const digest = webcrypto.subtle.digest.bind(webcrypto.subtle);
  vi.spyOn(webcrypto.subtle, 'digest').mockImplementationOnce(async (...arguments_) => {
    markEntered();
    await blocked;
    return digest(...arguments_);
  });
  return { entered, release };
}

describe('Independent recorded comparison bytes and pair lifecycle', () => {
  it('admits all genuine complete and bounded Core pairs with separate input and recording identities', async () => {
    for (const family of ['root', 'external', 'bounded']) {
      for (const revision of ['base', 'hypothesis']) {
        const value = await pair(family, revision);
        const admitted = (await readSimulationComparison(value.beforeCase, value.before, value.afterCase, value.after))!;
        expect(admitted.before).toBe(value.before);
        expect(admitted.after).toBe(value.after);
        expect(admitted.beforeSummary.runDigest).toEqual(value.before.digest);
        expect(admitted.afterSummary.runDigest).toEqual(value.after.digest);
        expect(admitted.sameInput).toBe(false);
        expect(admitted.sameLimits).toBe(true);
        expect(admitted.state).toBe(family === 'bounded' ? 'Incomplete' : 'Complete');
        const elapsedBefore = BigInt(admitted.beforeSummary.virtualTicks) - BigInt(admitted.beforeSummary.startTicks);
        const elapsedAfter = BigInt(admitted.afterSummary.virtualTicks) - BigInt(admitted.afterSummary.startTicks);
        expect(admitted.observedElapsedDelta).toBe(family === 'bounded' ? null : String(elapsedAfter - elapsedBefore));
        expect(admitted.data['verdict']).toBe('NotRun');
        expect(admitted.data['authority']).toBe('NotEvaluated');
      }
    }
  });

  it('refuses a rehashed false complete label and elapsed delta for two real bounded prefixes', async () => {
    const value = await pair('bounded');
    const original = (await readSimulationComparison(value.beforeCase, value.before, value.afterCase, value.after))!;
    expect(original.state).toBe('Incomplete');
    expect(original.observedElapsedDelta).toBeNull();
    const data = JSON.parse(original.text);
    data.state = 'Complete';
    data.reason = 'coglatas.comparison.complete-recordings';
    data.observedElapsedDelta = '0';
    replaceFrame(value.beforeCase, data);
    await expectFrameRefusalAfterFreshAdmission(value);
    expect(original.state).toBe('Incomplete');
  });

  it('refuses independently rehashed counts clocks and exact-integer delta substitutions', async () => {
    for (const field of ['count', 'clock', 'delta', 'numeric-delta']) {
      const value = await pair('external');
      const data = JSON.parse(value.beforeCase.simulationComparison!.text);
      if (field === 'count') data.after.eventCount++;
      if (field === 'clock') data.after.virtualTicks = String(BigInt(data.after.virtualTicks) + 1n);
      if (field === 'delta') data.observedElapsedDelta = '9007199254740993';
      if (field === 'numeric-delta') data.observedElapsedDelta = 0;
      replaceFrame(value.beforeCase, data);
      await expectFrameRefusalAfterFreshAdmission(value);
    }
  });

  it('refuses rehashed swapped summaries and substituted original recording digests', async () => {
    for (const mode of ['swapped', 'digest']) {
      const value = await pair();
      const data = JSON.parse(value.beforeCase.simulationComparison!.text);
      if (mode === 'swapped') [data.before, data.after] = [data.after, data.before];
      else data.after.runDigest = data.before.runDigest;
      replaceFrame(value.beforeCase, data);
      await expectFrameRefusalAfterFreshAdmission(value);
    }
  });

  it('refuses borrowed genuine admitted recordings instead of accepting matching report structure', async () => {
    const value = await pair('external');
    expect(await readSimulationComparison(value.beforeCase, value.before, value.afterCase, value.after)).not.toBeNull();
    await expect(readSimulationComparison(value.beforeCase, value.after, value.afterCase, value.after)).rejects.toThrow('unsupported-simulation-comparison');
    await expect(readSimulationComparison(value.beforeCase, value.before, value.afterCase, value.before)).rejects.toThrow('unsupported-simulation-comparison');
  });

  it('refuses current-scope replacement in either original case after both runs were admitted', async () => {
    for (const side of ['before', 'after'] as const) {
      const value = await pair();
      const changed = side === 'before' ? value.beforeCase : value.afterCase;
      Object.assign(changed.context.branch, { projectId: 'foreign-project' });
      await expect(readSimulationComparison(value.beforeCase, value.before, value.afterCase, value.after)).rejects.toThrow('unsupported-simulation-comparison');
    }
  });

  it('refuses rehashed future formats authority grants and result-pass claims', async () => {
    for (const mode of ['format', 'authority', 'verdict', 'extra']) {
      const value = await pair();
      const data = JSON.parse(value.beforeCase.simulationComparison!.text);
      if (mode === 'format') data.format = 'coglatas.local-simulation-comparison/2';
      if (mode === 'authority') data.authority = 'Allowed';
      if (mode === 'verdict') data.verdict = 'Pass';
      if (mode === 'extra') data.eligible = true;
      replaceFrame(value.beforeCase, data);
      await expectFrameRefusalAfterFreshAdmission(value);
    }
  });

  it('refuses duplicate original JSON members and oversized UTF8 frames despite correct hashes', async () => {
    for (const mode of ['duplicate', 'bytes']) {
      const value = await pair();
      const original = value.beforeCase.simulationComparison!.text;
      const text = mode === 'duplicate'
        ? original.slice(0, -1) + ',"state":"Complete"}'
        : original + ' '.repeat(65_537);
      Object.assign(value.beforeCase.simulationComparison!, { text, digest: hash(text) });
      await expectFrameRefusalAfterFreshAdmission(value, mode === 'bytes' ? 0 : 1,
        mode === 'duplicate' ? 'unsupported-local-test-projection' : 'unsupported-simulation-comparison');
    }
  });

  it('retains frozen detached summaries without freezing or granting ownership to caller cases', async () => {
    const value = await pair('external');
    const admitted = (await readSimulationComparison(value.beforeCase, value.before, value.afterCase, value.after))!;
    const originalBytes = admitted.text;
    const originalSummary = JSON.stringify(admitted.afterSummary);
    expect(Object.isFrozen(admitted)).toBe(true);
    expect(Object.isFrozen(admitted.afterSummary.context.branch)).toBe(true);
    expect(Object.isFrozen(admitted.data)).toBe(true);
    expect(Object.isFrozen(value.afterCase.context)).toBe(false);
    Object.assign(value.beforeCase.simulationComparison!, { text: '{}' });
    Object.assign(value.afterCase.inputDigest, { value: 'a'.repeat(64) });
    expect(admitted.text).toBe(originalBytes);
    expect(JSON.stringify(admitted.afterSummary)).toBe(originalSummary);
    await expect(readSimulationComparison(value.beforeCase, value.before, value.afterCase, value.after)).rejects.toThrow('unsupported-simulation-comparison');
  });

  it('rejects changed second-run bytes during the actual comparison digest boundary', async () => {
    const value = await pair();
    const gate = holdNextDigest();
    const pending = readSimulationComparison(value.beforeCase, value.before, value.afterCase, value.after);
    await gate.entered;
    Object.assign(value.afterCase.simulationRun!, { text: value.afterCase.simulationRun!.text + ' ' });
    gate.release();
    await expect(pending).rejects.toThrow('unsupported-simulation-comparison');
  });

  it('rejects comparison projection substitution during the actual digest boundary', async () => {
    const value = await pair();
    const gate = holdNextDigest();
    const pending = readSimulationComparison(value.beforeCase, value.before, value.afterCase, value.after);
    await gate.entered;
    const text = value.beforeCase.simulationComparison!.text + ' ';
    Object.assign(value.beforeCase.simulationComparison!, { text, digest: hash(text) });
    gate.release();
    await expect(pending).rejects.toThrow('unsupported-simulation-comparison');
  });

  it('requires current Source and run admission and compares without replacing the owned current run', async () => {
    const store = new ProjectIdeWorkbenchStore();
    await store.load(async () => bundle());
    store.choose('tests-simulation-root-base');
    store.loadResult();
    expect(await store.loadSimulationComparison()).toBe(false);
    expect(await store.loadScopedSources()).toBe(true);
    expect(await store.loadSimulationComparison()).toBe(false);
    expect(await store.loadSimulation()).toBe(true);
    const originalRun = store.simulation();
    const result = store.result();
    const context = store.context();
    expect(await store.loadSimulationComparison()).toBe(true);
    expect(store.simulation()).toBe(originalRun);
    expect(store.result()).toBe(result);
    expect(store.context()).toBe(context);
    expect(store.simulationComparison()!.before).toBe(originalRun);
  });

  it('withdraws pending comparisons when a result Source or recording admission is replaced', async () => {
    for (const mode of ['result', 'sources', 'run']) {
      const store = await ready();
      expect(await store.loadSimulationComparison()).toBe(true);
      const prior = store.simulationComparison()!;
      const gate = holdNextDigest();
      const pending = store.loadSimulationComparison();
      await gate.entered;
      if (mode === 'result') store.loadResult();
      if (mode === 'sources') expect(await store.loadScopedSources()).toBe(true);
      if (mode === 'run') expect(await store.loadSimulation()).toBe(true);
      gate.release();
      expect(await pending).toBe(false);
      expect(store.simulationComparison()).toBeNull();
      expect(store.simulationComparisonReadState()).toBe('idle');
      expect(prior.before.events.length).toBeGreaterThan(0);
      expect(prior.after.events.length).toBeGreaterThan(0);
    }
  });

  it('clears both summaries on access loss and cannot restore them from an older pending read', async () => {
    const store = await ready();
    expect(await store.loadSimulationComparison()).toBe(true);
    const original = store.simulationComparison()!;
    const gate = holdNextDigest();
    const pending = store.loadSimulationComparison();
    await gate.entered;
    store.clear('denied');
    gate.release();
    expect(await pending).toBe(false);
    expect(store.state()).toBe('denied');
    expect(store.context()).toBeNull();
    expect(store.simulation()).toBeNull();
    expect(store.simulationComparison()).toBeNull();
    expect(original.beforeSummary.runId).toBe(original.before.runId);
    expect(original.afterSummary.runId).toBe(original.after.runId);
  });

  it('preserves the exact newer comparison when an older request finishes later', async () => {
    const store = await ready();
    const gate = holdNextDigest();
    const older = store.loadSimulationComparison();
    await gate.entered;
    expect(await store.loadSimulationComparison()).toBe(true);
    const newer = store.simulationComparison();
    const currentRun = store.simulation();
    const context = store.context();
    gate.release();
    expect(await older).toBe(false);
    expect(store.simulationComparison()).toBe(newer);
    expect(store.simulation()).toBe(currentRun);
    expect(store.context()).toBe(context);
    expect(store.simulationComparisonReadState()).toBe('ready');
  });

  it('withdraws a pair when its second original case is removed or replaced by an equal clone', async () => {
    for (const remove of [true, false]) {
      const store = await ready();
      expect(await store.loadSimulationComparison()).toBe(true);
      const before = store.context()!;
      const data = store.bundle()!;
      const after = member(data, before.simulationComparison!.afterCaseKey);
      const currentRun = store.simulation();
      Object.assign(data, { cases: remove ? data.cases.filter(item => item !== after)
        : data.cases.map(item => item === after ? structuredClone(after) : item) });
      expect(store.simulationComparison()).toBeNull();
      expect(store.simulation()).toBe(currentRun);
    }
  });

  it('withdraws current summaries after either original recording changes while preserving detached evidence', async () => {
    for (const side of ['before', 'after'] as const) {
      const store = await ready();
      expect(await store.loadSimulationComparison()).toBe(true);
      const retained = store.simulationComparison()!;
      const text = retained.text;
      const before = store.context()!;
      const after = member(store.bundle()!, before.simulationComparison!.afterCaseKey);
      const changed = side === 'before' ? before : after;
      Object.assign(changed.simulationRun!, { text: changed.simulationRun!.text + ' ' });
      expect(store.simulationComparison()).toBeNull();
      expect(retained.text).toBe(text);
      expect(retained.data['authority']).toBe('NotEvaluated');
    }
  });

  it('cannot publish a delayed pair after its selected second case leaves the bundle', async () => {
    const store = await ready();
    const before = store.context()!;
    const data = store.bundle()!;
    const after = member(data, before.simulationComparison!.afterCaseKey);
    const gate = holdNextDigest();
    const pending = store.loadSimulationComparison();
    await gate.entered;
    Object.assign(data, { cases: data.cases.filter(item => item !== after) });
    gate.release();
    expect(await pending).toBe(false);
    expect(store.simulationComparison()).toBeNull();
    expect(store.context()).toBe(before);
  });
});
