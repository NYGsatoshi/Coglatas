import { Digest, PreviewCase } from './projectide.contracts';
import { AdmittedScopedSources, scopedSourcesMatch } from './projectide-scoped-sources';
import { AdmittedCompilerOutputs, CompilerOutputFrame, CompilerOutputMode, readCompilerOutputFrame } from './projectide-compiler-outputs.reader';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

export type CompilerRecordingLabel = 'original' | 'diagnostic' | 'budget' | 'cancelled';
export interface CompilerRecordingText { readonly text: string; readonly digest: Digest; }
export interface CompilerRecordingFrame {
  readonly manifest: CompilerRecordingText; readonly receipt: CompilerRecordingText;
  readonly host: Readonly<Record<string, unknown>>; readonly snapshot: CompilerOutputFrame | null;
}
export interface CompilerRecordingProjection {
  readonly format: 'coglatas.local-web-recording-fixture/1'; readonly original: CompilerRecordingFrame;
  readonly diagnostic: CompilerRecordingFrame; readonly budget: CompilerRecordingFrame; readonly cancelled: CompilerRecordingFrame;
}
export interface AdmittedCompilerRecording {
  readonly sources: AdmittedScopedSources; readonly label: CompilerRecordingLabel; readonly runId: string; readonly attemptId: string;
  readonly sessionId: string; readonly originalRunId: string | null; readonly mode: CompilerOutputMode;
  readonly hostStatus: 'Completed' | 'Cancelled'; readonly output: AdmittedCompilerOutputs | null;
  readonly manifest: CompilerRecordingText; readonly receipt: CompilerRecordingText;
}
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const fail = (): never => { throw new Error('unsupported-compiler-recording'); };
const obj = (v: unknown) => object(v) ? v : fail();
const fields = (v: unknown, keys: readonly string[]) => object(v) && Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k));
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const freeze = <T>(v: T): T => { if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); } return v; };
const digest = (v: unknown): v is Digest => fields(v, ['algorithm', 'value']) && obj(v)['algorithm'] === 'sha-256' && typeof obj(v)['value'] === 'string' && /^[a-f0-9]{64}$/u.test(obj(v)['value'] as string);
const guid = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/u.test(v);
const canonical = (v: unknown): string => Array.isArray(v) ? '[' + v.map(canonical).join(',') + ']' : object(v) ?
  '{' + Object.keys(v).sort().map(k => JSON.stringify(k) + ':' + canonical(v[k])).join(',') + '}' : JSON.stringify(v);
async function hash(domain: string, value: unknown): Promise<Digest> {
  const bytes = new TextEncoder().encode(domain + '\0' + canonical(value)); const data = await crypto.subtle.digest('SHA-256', bytes);
  return { algorithm: 'sha-256', value: Array.from(new Uint8Array(data), b => b.toString(16).padStart(2, '0')).join('') };
}
async function textFrame(v: CompilerRecordingText, format: string): Promise<Record<string, unknown>> {
  if (!fields(v, ['text', 'digest']) || typeof v.text !== 'string' || new TextEncoder().encode(v.text).length > 262_144 || !digest(v.digest) || !await verify(format, v.text, v.digest)) return fail();
  const data = obj(parse(v.text)); if (data['format'] !== format) return fail(); return data;
}
function bounded(value: unknown): boolean {
  const pending = [{ value, depth: 0 }]; let nodes = 0; let bytes = 0;
  while (pending.length) {
    const item = pending.pop()!; if (++nodes > 100_000 || item.depth > 64) return false;
    if (typeof item.value === 'string') { if (item.value.length > 4_194_304) return false; bytes += new TextEncoder().encode(item.value).length; if (bytes > 8_388_608) return false; }
    else if (item.value !== null && typeof item.value === 'object') {
      if (!Array.isArray(item.value) && Object.getPrototypeOf(item.value) !== Object.prototype) return false;
      for (const child of Object.values(item.value)) pending.push({ value: child, depth: item.depth + 1 });
    } else if (!['number', 'boolean'].includes(typeof item.value) && item.value !== null) return false;
  }
  return true;
}
const bindings = new WeakMap<AdmittedCompilerRecording, { value: PreviewCase; projection: CompilerRecordingProjection; selected: CompilerRecordingFrame; original: CompilerRecordingFrame; selectedText: string; originalText: string }>();
/** Captured fixture ownership only. No current actor access, trusted producer or actual browser execution is inferred. */
export function compilerRecordingMatches(value: PreviewCase, sources: AdmittedScopedSources, admitted: AdmittedCompilerRecording): boolean {
  const binding = bindings.get(admitted); const projection = value.compilerRecordings;
  try { return !!binding && binding.value === value && admitted.sources === sources && scopedSourcesMatch(value, sources) && projection === binding.projection &&
    projection?.[admitted.label] === binding.selected && projection.original === binding.original && bounded(binding.selected) && bounded(binding.original) &&
    JSON.stringify(binding.selected) === binding.selectedText && JSON.stringify(binding.original) === binding.originalText; } catch { return false; }
}
/** Read a recorded fixture invocation, never execute a Compiler or reconstruct an unrecorded earlier state. */
export async function readCompilerRecording(value: PreviewCase, sources: AdmittedScopedSources, label: CompilerRecordingLabel = 'original'): Promise<AdmittedCompilerRecording | null> {
  if (!scopedSourcesMatch(value, sources) || !['original', 'diagnostic', 'budget', 'cancelled'].includes(label)) return fail();
  const projection = value.compilerRecordings; if (projection == null) return null;
  if (!sources.publicationVerified || !fields(projection, ['format', 'original', 'diagnostic', 'budget', 'cancelled']) || projection.format !== 'coglatas.local-web-recording-fixture/1' || !bounded(projection)) return fail();
  const initial = JSON.stringify(projection); const frames = structuredClone(projection); const selected = projection[label]; const original = projection.original;
  const materialized = value.materializedSnapshots?.['entries'] ?? [];
  if (!Array.isArray(materialized) || materialized.length > 32) return fail();
  const snapshots = materialized.map(raw => { const entry = obj(raw); const source = obj(entry['source']);
    if (entry['admission'] !== 'Available' || typeof entry['snapshotId'] !== 'string' || typeof entry['version'] !== 'string' || !digest(source['digest']) ||
        typeof entry['declarationsComplete'] !== 'boolean' || typeof entry['relationsComplete'] !== 'boolean') return fail();
    return { snapshotId: entry['snapshotId'], version: entry['version'], sourceDigest: source['digest'], authorized: true,
      declarationsComplete: entry['declarationsComplete'], relationsComplete: entry['relationsComplete'] };
  }).sort((a, b) => canonical(a) < canonical(b) ? -1 : canonical(a) > canonical(b) ? 1 : 0);
  // This fixture recipe has captured snapshots and no unit/calendar materializations; it is not a general runtime request codec.
  const materializations = { format: 'coglatas.host-materializations/1', snapshots, unitDefinitions: [], calendarDefinitions: [] };
  const materializationsDigest = await hash(materializations.format, materializations);
  const executionInputDigest = await hash('coglatas.compiler-host-input/1', { declaredInputDigest: value.inputDigest, materializationsDigest });
  const parsed = new Map<CompilerRecordingLabel, { manifest: Record<string, unknown>; receipt: Record<string, unknown> }>();
  const runs = new Set<string>(); const attempts = new Set<string>();
  for (const key of ['original', 'diagnostic', 'budget', 'cancelled'] as const) {
    const frame = frames[key]; if (!fields(frame, ['manifest', 'receipt', 'host', 'snapshot'])) return fail();
    const m = await textFrame(frame.manifest, 'coglatas.local-compiler-recording-manifest/1'); const r = await textFrame(frame.receipt, 'coglatas.local-compiler-recording-receipt/1');
    if (!fields(m, ['authority', 'capture', 'clientGeneration', 'compiler', 'context', 'executionInputDigest', 'format', 'identityScope', 'inputDigest', 'knownLineage', 'materializationsDigest', 'origin', 'retentionPolicy', 'runId', 'sessionId', 'sourceDigest']) ||
        !fields(r, ['attemptId', 'authority', 'captureDigest', 'earlierIntermediateStates', 'eligibility', 'executionInputDigest', 'format', 'hostExecutionDigest', 'hostStatus', 'manifestDigest', 'producerTrust', 'reasonCode', 'recordingState', 'resultDigest', 'retentionPolicy', 'runId']) ||
        !guid(m['runId']) || !guid(m['sessionId']) || !guid(r['attemptId']) || runs.has(m['runId']) || attempts.has(r['attemptId'])) return fail();
    runs.add(m['runId']); attempts.add(r['attemptId']);
    const capture = obj(m['capture']); const h = obj(frame.host);
    if (!fields(capture, ['declared', 'earlierIntermediateStates', 'maximumBodyBytes', 'mode']) || capture['declared'] !== 'BeforeExecution' || capture['earlierIntermediateStates'] !== 'NotRecorded' ||
        capture['mode'] !== (key === 'diagnostic' || key === 'budget' ? 'AvailableOutputs' : 'Manifest') || capture['maximumBodyBytes'] !== (key === 'budget' ? 1 : 1_048_576) ||
        m['identityScope'] !== 'OwnedLocalSessionOnly' || m['authority'] !== 'NotEvaluated' || m['retentionPolicy'] !== 'NotAdopted' ||
        !Number.isSafeInteger(m['clientGeneration']) || (m['clientGeneration'] as number) < 0 || !equal(m['inputDigest'], value.inputDigest) || !equal(m['sourceDigest'], value.sourceDigest) ||
        !equal(m['context'], value.context) || !equal(m['compiler'], obj(value.result)['compiler']) || !equal(m['materializationsDigest'], materializationsDigest) || !equal(m['executionInputDigest'], executionInputDigest) ||
        !fields(h, ['status', 'reasonCode', 'attemptId', 'requestedInputDigest', 'clientGeneration', 'execution', 'executionDigest', 'result', 'resultDigest']) ||
        r['runId'] !== m['runId'] || !equal(r['manifestDigest'], frame.manifest.digest) || r['attemptId'] !== h['attemptId'] || h['clientGeneration'] !== m['clientGeneration'] ||
        !equal(h['requestedInputDigest'], value.inputDigest) || !equal(r['executionInputDigest'], executionInputDigest) || r['hostStatus'] !== h['status'] || r['reasonCode'] !== h['reasonCode'] ||
        r['authority'] !== 'NotEvaluated' || r['producerTrust'] !== 'NotEvaluated' || r['eligibility'] !== 'NotEvaluated' || r['retentionPolicy'] !== 'NotAdopted' || r['earlierIntermediateStates'] !== 'NotRecorded') return fail();
    if (key === 'cancelled') {
      if (h['status'] !== 'Cancelled' || h['reasonCode'] !== 'coglatas.host.cancelled' || h['execution'] !== null || h['executionDigest'] !== null || h['result'] !== null || h['resultDigest'] !== null ||
          r['hostExecutionDigest'] !== null || r['resultDigest'] !== null || r['captureDigest'] !== null || r['recordingState'] !== 'HostOutcomeWithoutCompilerResult' || frame.snapshot !== null) return fail();
    } else {
      const execution = obj(h['execution']);
      if (!fields(execution, ['format', 'inputBindingDigest', 'resultDigest']) || execution['format'] !== 'coglatas.host-execution/1' || !equal(execution['inputBindingDigest'], executionInputDigest) ||
          !equal(execution['resultDigest'], value.resultDigest) || !equal(h['executionDigest'], await hash('coglatas.host-execution/1', execution)) || !equal(r['hostExecutionDigest'], h['executionDigest']) ||
          h['status'] !== 'Completed' || h['reasonCode'] !== 'coglatas.host.result-produced' || !equal(h['result'], value.result) || !equal(h['resultDigest'], value.resultDigest) ||
          !equal(r['resultDigest'], value.resultDigest) || r['recordingState'] !== 'OriginalProducedOutputSnapshot' || !frame.snapshot || !equal(r['captureDigest'], frame.snapshot.digest)) return fail();
    }
    parsed.set(key, { manifest: m, receipt: r });
  }
  const first = parsed.get('original')!;
  for (const key of ['original', 'diagnostic', 'budget', 'cancelled'] as const) {
    const m = parsed.get(key)!.manifest;
    if (m['sessionId'] !== first.manifest['sessionId']) return fail();
    if (key === 'original') { if (m['origin'] !== null || !equal(m['knownLineage'], [])) return fail(); }
    else if (!equal(m['knownLineage'], [first.manifest['runId']]) || !equal(m['origin'], { runId: first.manifest['runId'],
      manifestDigest: frames.original.manifest.digest, receiptDigest: frames.original.receipt.digest, attemptId: first.receipt['attemptId'] })) return fail();
  }
  const chosen = parsed.get(label)!; const mode = obj(chosen.manifest['capture'])['mode'] as CompilerOutputMode;
  const output = frames[label].snapshot ? await readCompilerOutputFrame(value, sources, mode, frames[label].snapshot!) : null;
  if (output && output.maximumBodyBytes !== obj(chosen.manifest['capture'])['maximumBodyBytes'] || JSON.stringify(projection) !== initial || value.compilerRecordings !== projection || !scopedSourcesMatch(value, sources)) return fail();
  const admitted = freeze({ sources, label, runId: chosen.manifest['runId'] as string, attemptId: chosen.receipt['attemptId'] as string,
    sessionId: chosen.manifest['sessionId'] as string, originalRunId: label === 'original' ? null : first.manifest['runId'] as string, mode,
    hostStatus: frames[label].host['status'] as 'Completed' | 'Cancelled', output, manifest: frames[label].manifest, receipt: frames[label].receipt });
  bindings.set(admitted, { value, projection, selected, original, selectedText: JSON.stringify(selected), originalText: JSON.stringify(original) });
  return admitted;
}
