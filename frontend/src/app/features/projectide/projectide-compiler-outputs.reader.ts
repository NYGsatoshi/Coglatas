import { Digest, PreviewCase } from './projectide.contracts';
import { AdmittedScopedSources, scopedSourcesMatch } from './projectide-scoped-sources';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

export type CompilerOutputMode = 'Manifest' | 'AvailableOutputs';
export interface CompilerOutputBody { readonly role: string; readonly text: string; readonly digest: Digest; }
export interface CompilerOutputFrame { readonly text: string; readonly digest: Digest; readonly retainedBodyBytes: number; readonly bodies: readonly CompilerOutputBody[]; }
export interface CompilerOutputProjection { readonly manifest: CompilerOutputFrame; readonly available: CompilerOutputFrame; }
export type CompilerOutputCase = PreviewCase & { readonly compilerOutputs?: CompilerOutputProjection | null };
export interface AdmittedCompilerOutputs {
  readonly sources: AdmittedScopedSources; readonly mode: CompilerOutputMode;
  readonly state: 'ManifestOnly' | 'CapturedAvailableOutputs' | 'BodyBudgetExceeded';
  readonly digest: Digest; readonly text: string; readonly data: Readonly<Record<string, unknown>>;
  readonly maximumBodyBytes: number; readonly retainedBodyBytes: number; readonly bodies: readonly CompilerOutputBody[];
}
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const fail = (): never => { throw new Error('unsupported-compiler-output-snapshot'); };
const obj = (v: unknown) => object(v) ? v : fail();
const fields = (v: unknown, keys: readonly string[]) => object(v) && Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k));
const digest = (v: unknown): v is Digest => fields(v, ['algorithm', 'value']) && obj(v)['algorithm'] === 'sha-256' && typeof obj(v)['value'] === 'string' && /^[a-f0-9]{64}$/u.test(obj(v)['value'] as string);
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const freeze = <T>(v: T): T => { if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); } return v; };
const bytes = (v: string) => new TextEncoder().encode(v).length;
function axis(v: unknown): string {
  const values: Readonly<Record<string, string>> = { 'coglatas.completed': 'Completed', 'coglatas.not-run': 'NotRun', 'coglatas.cancelled': 'Cancelled',
    'coglatas.timed-out': 'TimedOut', 'coglatas.resource-exhausted': 'ResourceExhausted', 'coglatas.internal-error': 'InternalError',
    'coglatas.complete': 'Complete', 'coglatas.partial': 'Partial', 'coglatas.none': 'None', 'coglatas.pass': 'Pass', 'coglatas.fail': 'Fail',
    'coglatas.inconclusive': 'Inconclusive', 'coglatas.error': 'Error' };
  return typeof v === 'string' && Object.hasOwn(values, v) ? values[v] : fail();
}
/** Read original already-produced Compiler output bytes. Does not execute, persist or grant current runtime access. */
export async function readCompilerOutputs(value: CompilerOutputCase, sources: AdmittedScopedSources, mode: CompilerOutputMode = 'Manifest'): Promise<AdmittedCompilerOutputs | null> {
  if (!scopedSourcesMatch(value, sources) || !['Manifest', 'AvailableOutputs'].includes(mode)) return fail();
  if (value.compilerOutputs == null) return null;
  if (!sources.publicationVerified || !sources.projection.publication || !fields(value.compilerOutputs, ['manifest', 'available'])) return fail();
  const selected = mode === 'Manifest' ? value.compilerOutputs.manifest : value.compilerOutputs.available;
  const admitted = await readCompilerOutputFrame(value, sources, mode, selected);
  if (!scopedSourcesMatch(value, sources) || !equal(mode === 'Manifest' ? value.compilerOutputs.manifest : value.compilerOutputs.available, selected)) return fail();
  return admitted;
}
/** Validate one original frame against its admitted publication; recording ownership is checked by the recording reader. */
export async function readCompilerOutputFrame(value: PreviewCase, sources: AdmittedScopedSources, mode: CompilerOutputMode, selected: CompilerOutputFrame): Promise<AdmittedCompilerOutputs> {
  if (!scopedSourcesMatch(value, sources) || !['Manifest', 'AvailableOutputs'].includes(mode) || !sources.publicationVerified || !sources.projection.publication) return fail();
  const frame = structuredClone(selected);
  if (!fields(frame, ['text', 'digest', 'retainedBodyBytes', 'bodies']) || typeof frame.text !== 'string' || bytes(frame.text) > 262_144 || !digest(frame.digest) ||
      !Number.isSafeInteger(frame.retainedBodyBytes) || frame.retainedBodyBytes < 0 || frame.retainedBodyBytes > 33_554_432 || !Array.isArray(frame.bodies) || frame.bodies.length > 3 ||
      frame.bodies.some(b => !fields(b, ['role', 'text', 'digest']) || typeof b.role !== 'string' || typeof b.text !== 'string' || bytes(b.text) > 4_194_304 || !digest(b.digest)) ||
      !await verify('coglatas.local-compiler-output-snapshot/1', frame.text, frame.digest)) return fail();
  const data = obj(parse(frame.text)); const maximumBodyBytes = data['maximumBodyBytes'];
  if (typeof maximumBodyBytes !== 'number' || !Number.isSafeInteger(maximumBodyBytes) || maximumBodyBytes < 1 || maximumBodyBytes > 33_554_432) return fail();
  const publication = sources.projection.publication;
  const actualBodies: CompilerOutputBody[] = [{ role: 'coglatas.ir', ...publication.ir }, { role: 'coglatas.semantic-model', ...publication.semanticModel }, { role: 'coglatas.source-map', ...publication.sourceMap }];
  const producedBytes = actualBodies.reduce((n, body) => n + bytes(body.text), 0);
  const state = mode === 'Manifest' ? 'ManifestOnly' : producedBytes > maximumBodyBytes ? 'BodyBudgetExceeded' : 'CapturedAvailableOutputs';
  const expectedBodies = state === 'CapturedAvailableOutputs' ? actualBodies : [];
  const retainedBodyBytes = expectedBodies.reduce((n, body) => n + bytes(body.text), 0);
  if (!equal(frame.bodies, expectedBodies) || frame.retainedBodyBytes !== retainedBodyBytes) return fail();
  const result = obj(value.result); const artifacts = result['artifacts'];
  if (!Array.isArray(artifacts) || artifacts.length !== 3) return fail();
  const expected = { format: 'coglatas.local-compiler-output-snapshot/1', state, mode, maximumBodyBytes,
    captureKind: 'AvailableResultSnapshot', intermediateStates: 'NotRecorded', runIdentity: 'NotAllocated',
    authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted',
    basis: { inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, context: value.context, compiler: result['compiler'], resultDigest: value.resultDigest },
    result: { execution: axis(result['execution']), coverage: axis(result['coverage']), verdict: axis(result['verdict']), evidenceComplete: result['evidenceComplete'] },
    desired: { inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, context: value.context },
    freshness: { state: 'Current', desiredInputDigest: value.inputDigest, changedDimensions: [] },
    stages: result['stages'], outputs: artifacts.map(reference => ({ reference, body: state === 'CapturedAvailableOutputs' ? 'Captured' : 'NotCaptured' })),
    gaps: ['Original Source bodies are not retained by this produced-output snapshot.', 'Earlier intermediate states were not recorded.',
      'This snapshot is not FullDump or FullTrace.', 'Diagnostic reruns require a separately allocated execution and origin; no prior execution is reconstructed.'] };
  if (!equal(data, expected) || !scopedSourcesMatch(value, sources) || !equal(selected, frame)) return fail();
  return freeze({ sources, mode, state, digest: frame.digest, text: frame.text, data, maximumBodyBytes, retainedBodyBytes, bodies: frame.bodies });
}
