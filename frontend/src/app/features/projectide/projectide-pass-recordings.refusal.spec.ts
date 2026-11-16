/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { webcrypto, createHash } from 'node:crypto';
import { vi } from 'vitest';
import { PreviewCase } from './projectide.contracts';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readRootSource } from './projectide-root-source.reader';
import { PassRecordingProjection, PassRecordingFrame, PassTextFrame, passRecordingMatches, passRunIdentitiesAreUnique,
  readPassRecording, readRecordedPasses, recordedPassesMatch } from './projectide-pass-recordings.reader';
vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/pass-recordings-v1.json'), 'utf8'));
const bundle = () => readPreviewBundle(structuredClone(original));
const projection = (v: PreviewCase) => (v as PreviewCase & {passRecordings: PassRecordingProjection}).passRecordings;
const put = (v: object, key: string, value: unknown) => Object.defineProperty(v, key, {value, configurable: true, writable: true, enumerable: true});
function rewrite(frame: PassTextFrame, change: (data: Record<string, unknown>) => void) {
  const data = JSON.parse(frame.text) as Record<string, unknown>; change(data); const text = JSON.stringify(data);
  const domain = data['format'] as string;
  put(frame, 'text', text); put(frame, 'digest', {algorithm: 'sha-256', value: createHash('sha256').update(domain).update(Buffer.from([0])).update(text).digest('hex')});
}
function changedObservation(run: PassRecordingFrame, change: (data: Record<string, unknown>) => void) {
  rewrite(run.observation!, change);
  for (const view of Object.values(run.snapshots!)) rewrite(view.frame, data => {data['originalRecord'] = {digest: run.observation!.digest, metadata: JSON.parse(run.observation!.text)};});
  rewrite(run.response, data => {data['observationDigest'] = run.observation!.digest;});
  rewrite(run.receipt, data => {data['observationRecordDigest'] = run.observation!.digest; data['hostResponseDigest'] = run.response.digest;});
}
const events = (d: Record<string, unknown>) => d['events'] as Record<string, unknown>[];
const variants: readonly [string, (p: PassRecordingProjection) => void][] = [
  ['changed event ordinal with repaired hashes', p => changedObservation(p.diagnostic, d => {events(d)[0]['ordinal'] = 1;})],
  ['changed actual pass with repaired hashes', p => changedObservation(p.diagnostic, d => {events(d)[0]['passId'] = 'coglatas.publication';})],
  ['changed entry phase with repaired hashes', p => changedObservation(p.diagnostic, d => {events(d)[0]['phase'] = 'Returned';})],
  ['erased explicit boundary gap with repaired hashes', p => changedObservation(p.diagnostic, d => {events(d)[3]['gap'] = '';})],
  ['changed visited counter with repaired hashes', p => changedObservation(p.diagnostic, d => {d['visitedEvents'] = 11;})],
  ['changed retained counter with repaired hashes', p => changedObservation(p.diagnostic, d => {d['retainedEvents'] = 9;})],
  ['changed original body role with repaired hashes', p => changedObservation(p.diagnostic, d => {(events(d)[0]['originalBodies'] as Record<string, unknown>[])[0]['role'] = 'input';})],
  ['changed original byte count with repaired hashes', p => changedObservation(p.diagnostic, d => {const b = (events(d)[0]['originalBodies'] as Record<string, unknown>[])[0]; b['bytes'] = Number(b['bytes']) + 1;})],
  ['injected event with repaired hashes', p => changedObservation(p.diagnostic, d => {events(d).push(events(d)[0]);})],
  ['viewer changes original capture state', p => rewrite(p.diagnostic.snapshots!.available.frame, d => {d['originalRecord'] = null;})],
  ['viewer changes budget', p => rewrite(p.diagnostic.snapshots!.budget.frame, d => {d['maximumBodyBytes'] = 1_048_576;})],
  ['missing original body', p => put(p.diagnostic.snapshots!.available, 'bodies', p.diagnostic.snapshots!.available.bodies.slice(1))],
  ['duplicate original body', p => put(p.diagnostic.snapshots!.available, 'bodies', [...p.diagnostic.snapshots!.available.bodies, p.diagnostic.snapshots!.available.bodies[0]])],
  ['cancelled member acquires another real result', p => put(p.cancelled, 'result', p.retry.result)],
  ['cancelled member acquires another real snapshot', p => put(p.cancelled, 'snapshots', p.retry.snapshots)],
  ['genuine complete diagnostic pair replaces intended retry', p => put(p, 'retry', structuredClone(p.diagnostic))],
  ['genuine record from another context', p => put(p, 'diagnostic', structuredClone(projection(bundle().cases[1]).diagnostic))]
];
describe('Original pass recording refusal', () => {
  it.each(variants)('refuses %s', async (_, change) => {
    const value = bundle().cases[0]; change(projection(value)); const sources = (await readRootSource(value))!;
    await expect(readPassRecording(value, sources, 'diagnostic')).rejects.toThrow('unsupported-pass-recording');
  });
  it('withdraws retained and in-flight admissions after an equal-byte projection replacement', async () => {
    const value = bundle().cases[0]; const sources = (await readRootSource(value))!;
    const recording = (await readPassRecording(value, sources, 'diagnostic'))!;
    const retained = (await readRecordedPasses(value, sources, recording, 'available'))!;
    const pending = readPassRecording(value, sources, 'retry');
    put(value, 'passRecordings', structuredClone(projection(value)));
    await expect(pending).rejects.toThrow('unsupported-pass-recording');
    expect(passRecordingMatches(value, sources, recording)).toBe(false); expect(recordedPassesMatch(value, sources, recording, retained)).toBe(false);
    await expect(readRecordedPasses(value, sources, recording, 'available')).rejects.toThrow('unsupported-pass-recording');
    expect(retained.bodies.length).toBeGreaterThan(0);
    const current = (await readPassRecording(value, sources, 'retry'))!; expect(passRecordingMatches(value, sources, current)).toBe(true);
  });
  it('refuses copied view identity and repeated global run/session identities', async () => {
    const data = bundle(); const value = data.cases[0]; const sources = (await readRootSource(value))!;
    const recording = (await readPassRecording(value, sources, 'diagnostic'))!;
    const view = (await readRecordedPasses(value, sources, recording, 'available'))!;
    expect(recordedPassesMatch(value, sources, recording, {...view})).toBe(false);
    put(data.cases[1], 'passRecordings', structuredClone(projection(value)));
    expect(passRunIdentitiesAreUnique(data.cases)).toBe(false);
  });
});

function repairOriginalOrigins(p: PassRecordingProjection) {
  for (const [child, parent] of [['diagnostic', 'original'], ['budget', 'diagnostic'], ['eventGap', 'budget'], ['retry', 'cancelled']] as const) {
    rewrite(p[child].manifest, d => {d['origin'] = {runId: JSON.parse(p[parent].manifest.text).runId, manifestDigest: p[parent].manifest.digest,
      receiptDigest: p[parent].receipt.digest, attemptId: JSON.parse(p[parent].response.text).attemptId};});
    rewrite(p[child].response, d => {d['manifestDigest'] = p[child].manifest.digest;});
    rewrite(p[child].receipt, d => {d['manifestDigest'] = p[child].manifest.digest; d['hostResponseDigest'] = p[child].response.digest;});
  }
}
const knownByteCases = (['original', 'budget'] as const).flatMap(label => (['source', 'input'] as const).flatMap(role =>
  ([-1, 1] as const).map(delta => ({label, role, delta}))));
describe('Known original Source and input lengths in body-free capture', () => {
  it.each(knownByteCases)('refuses fully rehashed $label $role byte count delta $delta', async ({label, role, delta}) => {
    const value = bundle().cases[0]; const p = projection(value);
    changedObservation(p[label], d => {
      for (const event of events(d)) for (const ref of event['originalBodies'] as Record<string, unknown>[]) {
        if (role === 'source' ? ['source', 'decoded-source'].includes(ref['role'] as string) : ref['role'] === 'input')
          ref['bytes'] = Number(ref['bytes']) + delta;
      }
    });
    repairOriginalOrigins(p); const sources = (await readRootSource(value))!;
    await expect(readPassRecording(value, sources, label)).rejects.toThrow('unsupported-pass-recording');
  });
});
