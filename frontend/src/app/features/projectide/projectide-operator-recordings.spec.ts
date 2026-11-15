/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readRootSource } from './projectide-root-source.reader';
import { operatorRecordingMatches, operatorRunIdentitiesAreUnique, readOperatorRecording, readRecordedOperators, recordedOperatorsMatch } from './projectide-operator-recordings.reader';

vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/operator-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
const keys = ['fail', 'pass', 'unknown', 'test-assertion-navigation', 'operator-and-short-circuit', 'operator-nested-duplicates', 'operator-or-unknown', 'operator-not-pass'];
describe('Actual original operator recording members', () => {
  it.each(keys)('admits every real host run and only the selected original diagnostic frames: %s', async key => {
    const data = bundle(); const value = data.cases.find(v => v.key === key)!; const result = value.result;
    const sources = (await readRootSource(value))!; expect(operatorRunIdentitiesAreUnique(data.cases)).toBe(true);
    const runs = new Set<string>(); const attempts = new Set<string>();
    for (const label of ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const) {
      const recording = (await readOperatorRecording(value, sources, label))!;
      expect(operatorRecordingMatches(value, sources, recording)).toBe(true); expect(Object.isFrozen(recording)).toBe(true);
      runs.add(recording.runId); attempts.add(recording.attemptId);
      expect(recording.manifest.text).toBe(value.operatorRecordings![label].manifest.text);
      expect(recording.hostStatus).toBe(label === 'cancelled' ? 'Cancelled' : 'Completed');
      for (const diagnostic of result.diagnostics) {
        const metadata = await readRecordedOperators(value, sources, recording, diagnostic);
        if (label === 'cancelled') { expect(metadata).toBeNull(); continue; }
        expect(metadata!.mode).toBe('Manifest'); expect(metadata!.evaluations).toHaveLength(0);
        const available = (await readRecordedOperators(value, sources, recording, diagnostic, 'available'))!;
        expect(recordedOperatorsMatch(value, sources, recording, diagnostic, available)).toBe(true);
        const recorded = label === 'diagnostic' || label === 'retry';
        const count = recorded ? ({ fail: 1, unknown: 1, 'operator-and-short-circuit': 2, 'operator-nested-duplicates': 6, 'operator-or-unknown': 3 } as Record<string, number>)[key] ?? 0 : 0;
        expect(available.evaluations.reduce((n, e) => n + e.nodes.length, 0)).toBe(count);
        const limited = (await readRecordedOperators(value, sources, recording, diagnostic, 'budget'))!;
        expect(limited.evaluations).toHaveLength(0); expect(limited.originalCaptureState).toBe(available.originalCaptureState);
        if (count) expect(limited.state).toBe('BodyBudgetExceeded');
        expect(value.result).toBe(result);
      }
    }
    expect(runs.size).toBe(5); expect(attempts.size).toBe(5);
  });
  it('keeps actual execution counters in Manifest without inventing retained graphs', async () => {
    for (const key of keys) {
      const value = bundle().cases.find(v => v.key === key)!; const sources = (await readRootSource(value))!;
      const recording = (await readOperatorRecording(value, sources))!;
      const expected = JSON.parse(value.operatorRecordings!.diagnostic.observation!.text);
      expect(recording.originalCaptureState).toBe('ManifestOnly');
      for (const counter of ['startedEvaluations', 'completedEvaluations', 'observedNodes']) expect(recording.observationData![counter]).toBe(expected[counter]);
      expect(recording.observationData!['retainedBodyBytes']).toBe(0); expect(recording.observationData!['evaluations']).toEqual([]);
      if (key !== 'test-assertion-navigation') expect(recording.observationData!['observedNodes']).toBeGreaterThan(0);
    }
  });
  it('keeps every full body fingerprint private through metadata admission', async () => {
    const value = bundle().cases.find(v => v.key === 'operator-nested-duplicates')!; const sources = (await readRootSource(value))!;
    expect(JSON.stringify(sources)).not.toContain('operatorRecordings');
    const recording = (await readOperatorRecording(value, sources))!;
    expect(recording.originalCaptureState).toBe('ManifestOnly'); expect(JSON.stringify(recording)).not.toContain('operatorRecordings');
    expect(JSON.stringify(recording)).not.toContain('evaluatedChildren');
    Object.defineProperty(value, 'operatorRecordings', { value: structuredClone(value.operatorRecordings!) });
    expect(operatorRecordingMatches(value, sources, recording)).toBe(false);
  });
  it('shows actual nested order, short circuits and duplicate premises from original supplied frames', async () => {
    const value = bundle().cases.find(v => v.key === 'operator-nested-duplicates')!; const sources = (await readRootSource(value))!;
    const recording = (await readOperatorRecording(value, sources, 'diagnostic'))!;
    const snapshot = (await readRecordedOperators(value, sources, recording, value.result.diagnostics[0], 'available'))!;
    const graph = snapshot.evaluations[0]; expect(graph.nodes).toHaveLength(6);
    expect(graph.nodes[0].evaluatedChildren).toEqual([1, 4]); expect(graph.nodes[0].skippedOperands).toBe(1);
    const premises = JSON.parse(graph.evaluationFrame.text).premises as { value: string }[];
    expect(premises).toHaveLength(3); expect(new Set(premises.map(v => v.value)).size).toBe(2);
  });
});
