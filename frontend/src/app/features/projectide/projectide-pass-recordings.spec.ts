/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readRootSource } from './projectide-root-source.reader';
import { passRecordingMatches, passRunIdentitiesAreUnique, readPassRecording, readRecordedPasses, recordedPassesMatch } from './projectide-pass-recordings.reader';
vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/pass-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
const keys = original.cases.map((v: {key: string}) => v.key) as string[];
describe('Actual original pass recording admission', () => {
  it.each(keys)('admits every actual run and explicit whole-root pass view without a diagnostic: %s', async key => {
    const data = bundle(); const value = data.cases.find(v => v.key === key)!; const result = value.result;
    const sources = (await readRootSource(value))!; expect(passRunIdentitiesAreUnique(data.cases)).toBe(true);
    const runs = new Set<string>(); const attempts = new Set<string>();
    for (const label of ['original', 'diagnostic', 'budget', 'eventGap', 'cancelled', 'retry'] as const) {
      const recording = (await readPassRecording(value, sources, label))!;
      expect(passRecordingMatches(value, sources, recording)).toBe(true); expect(Object.isFrozen(recording)).toBe(true);
      runs.add(recording.runId); attempts.add(recording.attemptId);
      expect(recording.hostStatus).toBe(label === 'cancelled' ? 'Cancelled' : 'Completed');
      const metadata = await readRecordedPasses(value, sources, recording);
      if (label === 'cancelled') { expect(metadata).toBeNull(); expect(recording.observation).toBeNull(); continue; }
      expect(metadata!.mode).toBe('Manifest'); expect(metadata!.bodies).toHaveLength(0);
      const available = (await readRecordedPasses(value, sources, recording, 'available'))!;
      expect(recordedPassesMatch(value, sources, recording, available)).toBe(true);
      if (label === 'diagnostic' || label === 'retry') { expect(available.state).toBe('AvailableOriginalJson'); expect(available.bodies.length).toBeGreaterThan(0); }
      else { expect(available.state).toBe('NoRecordedOriginalBodies'); expect(available.bodies).toHaveLength(0); }
      const limited = (await readRecordedPasses(value, sources, recording, 'budget'))!;
      expect(limited.bodies).toHaveLength(0); expect(limited.originalCaptureState).toBe(available.originalCaptureState);
      expect(value.result).toBe(result);
      if (key.startsWith('operator-requirement-')) expect(result.evidenceComplete).toBe(false);
    }
    expect(runs.size).toBe(6); expect(attempts.size).toBe(6);
  });
  it('keeps original frame maps private and refuses cloned admission', async () => {
    const value = bundle().cases[0]; const sources = (await readRootSource(value))!;
    const recording = (await readPassRecording(value, sources))!;
    expect(JSON.stringify(sources)).not.toContain('passRecordings');
    expect(JSON.stringify(recording)).not.toContain('"snapshots"'); expect(JSON.stringify(recording)).not.toContain('"bodies"');
    expect(passRecordingMatches(value, sources, {...recording})).toBe(false);
    await expect(readRecordedPasses(value, sources, {...recording}, 'available')).rejects.toThrow('unsupported-pass-recording');
  });
});
