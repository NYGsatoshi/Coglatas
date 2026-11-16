/// <reference types="node" />
import {readFileSync} from 'node:fs';
import {resolve} from 'node:path';
import {webcrypto} from 'node:crypto';
import {vi} from 'vitest';
import {readPreviewBundle} from './projectide-preview.adapter';
import {readRootSource} from './projectide-root-source.reader';
import {readPassRecording, readRecordedPasses} from './projectide-pass-recordings.reader';
import {readRecordedPassBoundary, recordedPassBoundaryMatches} from './projectide-pass-boundary.reader';
vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/pass-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
const keys = original.cases.map((v: {key: string}) => v.key) as string[];
describe('Selected actual pass boundary', () => {
  it.each(keys)('preserves every original boundary, gap and exact selected body reference: %s', async key => {
    const value = bundle().cases.find(v => v.key === key)!; const result = value.result; const sources = (await readRootSource(value))!;
    for (const label of ['original', 'diagnostic', 'budget', 'eventGap', 'cancelled', 'retry'] as const) {
      const run = (await readPassRecording(value, sources, label))!;
      if (label === 'cancelled') { expect(await readRecordedPassBoundary(value, sources, run, 0)).toBeNull(); continue; }
      const whole = (await readRecordedPasses(value, sources, run, 'available'))!;
      if (run.events.length === 0) {
        const missing = (await readRecordedPassBoundary(value, sources, run, 0, 'AvailableOriginalJson'))!;
        expect(missing.state).toBe('BoundaryNotRecorded'); expect(missing.originalBoundary).toBeNull(); expect(missing.bodies).toHaveLength(0); continue;
      }
      for (const event of run.events) {
        const manifest = (await readRecordedPassBoundary(value, sources, run, event.ordinal))!;
        expect(manifest.state).toBe('ManifestOnly'); expect(manifest.bodies).toHaveLength(0); expect(manifest.originalBoundary).toBe(event);
        const available = (await readRecordedPassBoundary(value, sources, run, event.ordinal, 'AvailableOriginalJson'))!;
        expect(recordedPassBoundaryMatches(value, sources, run, available)).toBe(true);
        expect(available.originalBoundary).toBe(event); expect(available.originalRecordDigest).toBe(run.observation!.digest);
        const wanted = new Set(event.originalBodies.map(r => r.domain + ':' + r.digest.value));
        const expected = whole.bodies.filter(body => wanted.has(body.key));
        expect(available.bodies).toEqual(expected);
        for (const body of available.bodies) expect(whole.bodies.find(originalBody => originalBody.key === body.key)).toBe(body);
        expect(available.retainedBodyBytes).toBe(expected.reduce((sum, body) => sum + body.bytes, 0));
        expect(available.state).toBe(expected.length === 0 ? 'NoRecordedOriginalBodies' : 'AvailableOriginalJson');
        expect(Object.isFrozen(available)).toBe(true); expect(Object.isFrozen(available.bodies)).toBe(true);
        const limited = (await readRecordedPassBoundary(value, sources, run, event.ordinal, 'AvailableOriginalJson', 1))!;
        expect(limited.bodies).toHaveLength(0); expect(limited.originalCaptureState).toBe(run.originalCaptureState);
        expect(limited.state).toBe(expected.length ? 'BodyBudgetExceeded' : 'NoRecordedOriginalBodies');
      }
    }
    expect(value.result).toBe(result);
    if (key.startsWith('operator-requirement-')) expect(value.result.evidenceComplete).toBe(false);
  });
  it('uses the selected body budget independently from the whole original view budget', async () => {
    const value = bundle().cases[0]; const sources = (await readRootSource(value))!; const run = (await readPassRecording(value, sources, 'diagnostic'))!;
    const sourceBytes = new TextEncoder().encode(value.sourceText).length;
    const exact = (await readRecordedPassBoundary(value, sources, run, 0, 'AvailableOriginalJson', sourceBytes))!;
    expect(exact.state).toBe('AvailableOriginalJson'); expect(exact.bodies).toHaveLength(1); expect(exact.bodies[0].text).toBe(value.sourceText);
    const small = (await readRecordedPassBoundary(value, sources, run, 0, 'AvailableOriginalJson', sourceBytes - 1))!;
    expect(small.state).toBe('BodyBudgetExceeded'); expect(small.bodies).toHaveLength(0); expect(small.retainedBodyBytes).toBe(0);
    expect(exact.bodies).toHaveLength(1); expect(run.retainedBodyBytes).toBeGreaterThan(sourceBytes);
  });
  it('refuses cloned or foreign ownership and withdraws binding after original member replacement', async () => {
    const value = bundle().cases[0]; const sources = (await readRootSource(value))!; const run = (await readPassRecording(value, sources, 'diagnostic'))!;
    const boundary = (await readRecordedPassBoundary(value, sources, run, 0))!;
    expect(recordedPassBoundaryMatches(value, sources, run, {...boundary})).toBe(false);
    await expect(readRecordedPassBoundary(value, sources, {...run}, 0)).rejects.toThrow('unsupported-pass-boundary');
    const otherSources = (await readRootSource(value))!;
    expect(recordedPassBoundaryMatches(value, otherSources, run, boundary)).toBe(false);
    await expect(readRecordedPassBoundary(value, otherSources, run, 0)).rejects.toThrow('unsupported-pass-boundary');
    Object.assign(value.passRecordings!, {diagnostic: structuredClone(value.passRecordings!.diagnostic)});
    expect(recordedPassBoundaryMatches(value, sources, run, boundary)).toBe(false);
  });
  it('rejects invalid selectors and budget aliases without inventing an unavailable boundary', async () => {
    const value = bundle().cases[0]; const sources = (await readRootSource(value))!; const run = (await readPassRecording(value, sources, 'diagnostic'))!;
    for (const ordinal of [-1, 0.5, Number.NaN, 2_147_483_648])
      await expect(readRecordedPassBoundary(value, sources, run, ordinal)).rejects.toThrow('unsupported-pass-boundary');
    for (const bytes of [0, 0.5, 33_554_433])
      await expect(readRecordedPassBoundary(value, sources, run, 0, 'AvailableOriginalJson', bytes)).rejects.toThrow('unsupported-pass-boundary');
    const missing = (await readRecordedPassBoundary(value, sources, run, 2_147_483_647))!;
    expect(missing.state).toBe('BoundaryNotRecorded'); expect(missing.bodies).toHaveLength(0); expect(missing.originalBoundary).toBeNull();
  });
});
