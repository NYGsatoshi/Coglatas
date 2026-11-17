/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readRootSource } from './projectide-root-source.reader';
import { readScopedSources } from './projectide-scoped-sources';
import { readRegionalOperatorRecording, readRecordedRegionalOperators, regionalOperatorRecordingMatches, recordedRegionalOperatorsMatch, regionalOperatorRunIdentitiesAreUnique } from './projectide-regional-operator-recordings.reader';
vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/regional-operator-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
describe('Original actual regional operator recording members', () => {
  it.each(original.cases.map((c: { key: string }) => c.key))('admits every original host pair and exact region diagnostic: %s', async key => {
    const data = bundle(), value = data.cases.find(v => v.key === key)!;
    const result = value.result, sources = (await readScopedSources(value))!;
    expect(regionalOperatorRunIdentitiesAreUnique(data.cases)).toBe(true);
    const runs = new Set<string>(), attempts = new Set<string>();
    for (const label of ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const) {
      const recording = (await readRegionalOperatorRecording(value, sources, label))!;
      expect(regionalOperatorRecordingMatches(value, sources, recording)).toBe(true);
      expect(Object.isFrozen(recording)).toBe(true);
      expect(recording.manifest.text).toBe(value.regionalOperatorRecordings![label].manifest.text);
      expect(recording.hostStatus).toBe(label === 'cancelled' ? 'Cancelled' : 'Completed');
      runs.add(recording.runId); attempts.add(recording.attemptId);
      if (label === 'cancelled') { expect(recording.regions).toEqual([]); continue; }
      expect(recording.regions.length).toBe(value.regionalOperatorRecordings![label].regions.length);
      for (const region of recording.regions) for (const diagnostic of region.diagnostics) {
        const manifest = (await readRecordedRegionalOperators(value, sources, recording, region, diagnostic))!;
        expect(manifest.mode).toBe('Manifest'); expect(manifest.evaluations).toEqual([]);
        const available = (await readRecordedRegionalOperators(value, sources, recording, region, diagnostic, 'available'))!;
        expect(recordedRegionalOperatorsMatch(value, sources, recording, region, diagnostic, available)).toBe(true);
        const originalSnapshot = JSON.parse(value.regionalOperatorRecordings![label].regions[region.publishedOrdinal].diagnostics[region.diagnostics.indexOf(diagnostic)].available.frame.text);
        expect(available.data).toEqual(originalSnapshot);
        expect(available.evaluations.length).toBe(available.state === 'AvailableOperators' ? originalSnapshot.selectedEvaluations : 0);
        const limited = (await readRecordedRegionalOperators(value, sources, recording, region, diagnostic, 'budget'))!;
        expect(limited.evaluations).toEqual([]); expect(limited.originalCaptureState).toBe(available.originalCaptureState);
      }
      expect(value.result).toBe(result);
    }
    expect(runs.size).toBe(5); expect(attempts.size).toBe(5);
  });
  it('exposes external regions only through their admitted scoped Sources', async () => {
    const value = bundle().cases.find(v => v.key === 'tests-regional-versioned-base')!;
    const root = (await readRootSource(value))!;
    const rootRecording = (await readRegionalOperatorRecording(value, root, 'diagnostic'))!;
    expect(rootRecording.regions).toHaveLength(1); expect(rootRecording.regions[0].isRoot).toBe(true);
    const scoped = (await readScopedSources(value))!;
    const scopedRecording = (await readRegionalOperatorRecording(value, scoped, 'diagnostic'))!;
    expect(scopedRecording.regions).toHaveLength(3);
    expect(scopedRecording.regions.filter(r => !r.isRoot)).toHaveLength(2);
  });
});

describe('Actual regional compound and Requirement original subjects', () => {
  it.each(['base', 'hypothesis', 'draft'])('keeps original nested order, short circuits and duplicate premises in its region: %s', async suffix => {
    const value = bundle().cases.find(v => v.key === 'tests-regional-nested-duplicates-' + suffix)!, sources = (await readScopedSources(value))!;
    const recording = (await readRegionalOperatorRecording(value, sources, 'diagnostic'))!;
    const region = recording.regions.find(r => !r.isRoot && r.diagnostics.length)!, diagnostic = region.diagnostics[0];
    const snapshot = (await readRecordedRegionalOperators(value, sources, recording, region, diagnostic, 'available'))!, evaluation = snapshot.evaluations[0];
    expect(evaluation.nodes).toHaveLength(6); expect(evaluation.nodes[0].evaluatedChildren).toEqual([1, 4]); expect(evaluation.nodes[0].skippedOperands).toBe(1);
    expect(evaluation.nodes.filter(n => n.literal).map(n => n.literal!.premiseOrdinal)).toEqual([0, 1, 2]);
    const premises = JSON.parse(evaluation.evaluationFrame.text).premises as { value: string }[];
    expect(premises).toHaveLength(3); expect(new Set(premises.map(v => v.value)).size).toBe(2);
    expect(recording.startedEvaluations).toBe(1); expect(recording.observedNodes).toBe(6);
  });
  it.each([['false', 6], ['true', 2], ['unknown', 3]] as const)('does not turn missing Requirement evidence into a condition graph: %s', async (truth, visited) => {
    const value = bundle().cases.find(v => v.key === 'tests-regional-requirement-' + truth + '-base')!, result = value.result, sources = (await readScopedSources(value))!;
    expect(result.evidenceComplete).toBe(false);
    for (const label of ['original', 'diagnostic', 'budget', 'retry'] as const) {
      const recording = (await readRegionalOperatorRecording(value, sources, label))!;
      expect(recording.startedEvaluations).toBe(1); expect(recording.completedEvaluations).toBe(1); expect(recording.observedNodes).toBe(visited);
      const region = recording.regions.find(r => !r.isRoot && r.diagnostics.length)!;
      for (const diagnostic of region.diagnostics) {
        const snapshot = (await readRecordedRegionalOperators(value, sources, recording, region, diagnostic, 'available'))!;
        const condition = diagnostic.code === 'coglatas.analysis.requirement-condition-false', captured = label === 'diagnostic' || label === 'retry';
        expect(snapshot.evaluations).toHaveLength(condition && captured ? 1 : 0);
        expect(snapshot.evaluations.flatMap(e => e.nodes)).toHaveLength(condition && captured ? visited : 0);
        if (!condition && captured) { expect(diagnostic.code).toBe('coglatas.analysis.requirement-evidence-unavailable'); expect(snapshot.state).toBe('NoObservedRegionalDiagnostic'); }
        expect(value.result).toBe(result);
      }
    }
  });
});

describe('Private scoped Source admission fingerprints', () => {
  it('keeps all unselected original graph bodies outside serializable Source and run metadata', async () => {
    const value = bundle().cases.find(v => v.key === 'tests-regional-nested-duplicates-base')!, sources = (await readScopedSources(value))!;
    expect(Object.hasOwn(sources, 'basis')).toBe(false);
    const recording = (await readRegionalOperatorRecording(value, sources, 'diagnostic'))!;
    for (const view of [sources, recording]) {
      const serialized = JSON.stringify(view);
      expect(serialized).not.toContain('regionalOperatorRecordings'); expect(serialized).not.toContain('operatorBodies');
      expect(serialized).not.toContain('evaluationFrames'); expect(serialized).not.toContain('evaluatedChildren');
    }
    const region = recording.regions.find(r => !r.isRoot && r.diagnostics.length)!;
    const selected = (await readRecordedRegionalOperators(value, sources, recording, region, region.diagnostics[0], 'available'))!;
    expect(selected.evaluations[0].nodes).toHaveLength(6); expect(JSON.stringify(selected)).toContain('evaluatedChildren');
  });
  it('does not admit a cloned scoped Source object with equal data', async () => {
    const value = bundle().cases.find(v => v.key === 'tests-regional-nested-duplicates-base')!, sources = (await readScopedSources(value))!;
    for (const foreign of [structuredClone(sources), { ...sources }]) await expect(readRegionalOperatorRecording(value, foreign, 'diagnostic')).rejects.toThrow();
  });
});
