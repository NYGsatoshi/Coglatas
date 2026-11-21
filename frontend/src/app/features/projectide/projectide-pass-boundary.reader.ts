import { Digest, PreviewCase } from './projectide.contracts';
import { AdmittedRootSource } from './projectide-root-source.reader';
import {
  AdmittedPassEvent, AdmittedPassRecording, AdmittedRecordedPasses, OriginalPassBody,
  passRecordingMatches, readRecordedPasses, recordedPassesMatch,
} from './projectide-pass-recordings.reader';

export type RecordedPassBoundaryState = 'BoundaryNotRecorded' | 'ManifestOnly' | 'NoRecordedOriginalBodies' |
  'AvailableOriginalJson' | 'BodyBudgetExceeded';

/** Derived selection over verified original frames, not a new native producer frame or access grant. */
export interface AdmittedRecordedPassBoundary {
  readonly recording: AdmittedPassRecording;
  readonly ordinal: number;
  readonly mode: 'Manifest' | 'AvailableOriginalJson';
  readonly state: RecordedPassBoundaryState;
  readonly maximumBodyBytes: number;
  readonly retainedBodyBytes: number;
  readonly originalCaptureState: string;
  readonly originalRecordDigest: Digest;
  readonly originalBoundary: AdmittedPassEvent | null;
  readonly bodies: readonly OriginalPassBody[];
}

interface BoundaryBinding {
  readonly value: PreviewCase;
  readonly sources: AdmittedRootSource;
  readonly recording: AdmittedPassRecording;
  readonly originalView: AdmittedRecordedPasses;
}
const bindings = new WeakMap<AdmittedRecordedPassBoundary, BoundaryBinding>();
const refuse = (): never => { throw new Error('unsupported-pass-boundary'); };

export function recordedPassBoundaryMatches(value: PreviewCase, sources: AdmittedRootSource,
  recording: AdmittedPassRecording, boundary: AdmittedRecordedPassBoundary): boolean {
  const binding = bindings.get(boundary);
  return !!binding && binding.value === value && binding.sources === sources && binding.recording === recording &&
    boundary.recording === recording && recordedPassesMatch(value, sources, recording, binding.originalView);
}

/** Select retained boundary facts; a later local view cannot fill an earlier event or body capture gap. */
export async function readRecordedPassBoundary(value: PreviewCase, sources: AdmittedRootSource,
  recording: AdmittedPassRecording, ordinal: number, mode: 'Manifest' | 'AvailableOriginalJson' = 'Manifest',
  maximumBodyBytes = 1_048_576): Promise<AdmittedRecordedPassBoundary | null> {
  if (!Number.isSafeInteger(ordinal) || ordinal < 0 || ordinal > 2_147_483_647 ||
    !['Manifest', 'AvailableOriginalJson'].includes(mode) || !Number.isSafeInteger(maximumBodyBytes) ||
    maximumBodyBytes < 1 || maximumBodyBytes > 33_554_432 || !passRecordingMatches(value, sources, recording)) return refuse();
  const originalView = await readRecordedPasses(value, sources, recording, mode === 'Manifest' ? 'manifest' : 'available');
  if (!originalView) return null;
  if (!recordedPassesMatch(value, sources, recording, originalView) || !recording.observation ||
    recording.originalCaptureState === null) return refuse();
  const event = recording.events[ordinal] ?? null;
  let state: RecordedPassBoundaryState = event === null ? 'BoundaryNotRecorded' : mode === 'Manifest' ? 'ManifestOnly' : 'AvailableOriginalJson';
  let bodies: readonly OriginalPassBody[] = Object.freeze([]);
  let retainedBodyBytes = 0;
  if (event && mode === 'AvailableOriginalJson') {
    const keys = new Set(event.originalBodies.map(r => r.domain + ':' + r.digest.value));
    const selected = originalView.bodies.filter(body => keys.has(body.key));
    const selectedBytes = selected.reduce((sum, body) => sum + body.bytes, 0);
    if (keys.size === 0 || selected.length !== keys.size) state = 'NoRecordedOriginalBodies';
    else if (selectedBytes > maximumBodyBytes) state = 'BodyBudgetExceeded';
    else { bodies = Object.freeze(selected); retainedBodyBytes = selectedBytes; }
  }
  const admitted: AdmittedRecordedPassBoundary = Object.freeze({
    recording, ordinal, mode, state, maximumBodyBytes, retainedBodyBytes,
    originalCaptureState: recording.originalCaptureState, originalRecordDigest: recording.observation.digest,
    originalBoundary: event, bodies,
  });
  if (!recordedPassesMatch(value, sources, recording, originalView)) return refuse();
  bindings.set(admitted, {value, sources, recording, originalView}); return admitted;
}
