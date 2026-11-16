import { Digest, PreviewCase } from './projectide.contracts';
import { AdmittedRootSource, rootSourceMatches } from './projectide-root-source.reader';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

export type PassRecordingLabel = 'original' | 'diagnostic' | 'budget' | 'eventGap' | 'cancelled' | 'retry';
export type RecordedPassChoice = 'manifest' | 'available' | 'budget';
export interface PassTextFrame { readonly text: string; readonly digest: Digest; }
export interface OriginalPassBody extends PassTextFrame { readonly key: string; readonly domain: string; readonly bytes: number; }
export interface PassSnapshotFrames { readonly frame: PassTextFrame; readonly bodies: readonly OriginalPassBody[]; }
export interface PassRecordingFrame {
  readonly request: PassTextFrame; readonly policy: PassTextFrame; readonly baseExecution: PassTextFrame; readonly materializations: PassTextFrame;
  readonly manifest: PassTextFrame; readonly response: PassTextFrame; readonly receipt: PassTextFrame;
  readonly result: PassTextFrame | null; readonly observation: PassTextFrame | null;
  readonly snapshots: Readonly<Record<RecordedPassChoice, PassSnapshotFrames>> | null;
}
export interface PassRecordingProjection {
  readonly format: 'coglatas.local-web-pass-recording-fixture/1'; readonly source: PassTextFrame; readonly input: PassTextFrame;
  readonly original: PassRecordingFrame; readonly diagnostic: PassRecordingFrame; readonly budget: PassRecordingFrame;
  readonly eventGap: PassRecordingFrame; readonly cancelled: PassRecordingFrame; readonly retry: PassRecordingFrame;
}
export interface AdmittedPassEvent {
  readonly ordinal: number; readonly passId: string; readonly phase: 'Entered' | 'Returned'; readonly gap: string;
  readonly originalBodies: readonly {readonly role: string; readonly domain: string; readonly digest: Digest; readonly bytes: number}[];
}
export interface AdmittedPassRecording {
  readonly sources: AdmittedRootSource; readonly label: PassRecordingLabel; readonly runId: string; readonly attemptId: string; readonly sessionId: string;
  readonly originRunId: string | null; readonly hostStatus: 'Completed' | 'Cancelled'; readonly captureMode: 'Manifest' | 'AvailableOriginalJson';
  readonly events: readonly AdmittedPassEvent[];
  readonly originalCaptureState: string | null; readonly visitedEvents: number | null; readonly retainedEvents: number | null;
  readonly retainedBodyBytes: number | null; readonly maximumBodyBytes: number; readonly maximumEvents: number;
  readonly manifest: PassTextFrame; readonly response: PassTextFrame; readonly receipt: PassTextFrame; readonly observation: PassTextFrame | null;
  readonly manifestData: Readonly<Record<string, unknown>>; readonly responseData: Readonly<Record<string, unknown>>;
  readonly receiptData: Readonly<Record<string, unknown>>; readonly observationData: Readonly<Record<string, unknown>> | null;
}
export interface AdmittedRecordedPasses extends PassTextFrame {
  readonly recording: AdmittedPassRecording; readonly choice: RecordedPassChoice; readonly state: string;
  readonly mode: 'Manifest' | 'AvailableOriginalJson'; readonly maximumBodyBytes: number; readonly retainedBodyBytes: number;
  readonly originalCaptureState: string; readonly bodies: readonly OriginalPassBody[]; readonly data: Readonly<Record<string, unknown>>;
}
const labels = ['original', 'diagnostic', 'budget', 'eventGap', 'cancelled', 'retry'] as const;
const choices = ['manifest', 'available', 'budget'] as const;
const fail = (): never => { throw new Error('unsupported-pass-recording'); };
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const obj = (v: unknown): Record<string, unknown> => object(v) ? v : fail();
const fields = (v: unknown, keys: readonly string[]) => object(v) && Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k));
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const expect = (actual: unknown, expected: unknown): void => { if (!equal(actual, expected)) fail(); };
const integer = (v: unknown, minimum: number, maximum: number): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= minimum && v <= maximum;
const digest = (v: unknown): v is Digest => fields(v, ['algorithm', 'value']) && obj(v)['algorithm'] === 'sha-256' && typeof obj(v)['value'] === 'string' && /^[a-f0-9]{64}$/u.test(obj(v)['value'] as string);
const guid = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/u.test(v);
const bytes = (text: string) => new TextEncoder().encode(text).length;
const freeze = <T>(v: T): T => { if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); } return v; };
function bounded(value: unknown): boolean {
  const pending = [{ value, depth: 0 }]; let nodes = 0; let total = 0;
  while (pending.length) {
    const entry = pending.pop()!;
    if (++nodes > 100_000 || entry.depth > 64) return false;
    if (typeof entry.value === 'string') {
      if (entry.value.length > 4_194_304 || (total += bytes(entry.value)) > 16_777_216) return false;
    } else if (entry.value !== null && typeof entry.value === 'object') {
      if (!Array.isArray(entry.value) && Object.getPrototypeOf(entry.value) !== Object.prototype) return false;
      for (const child of Object.values(entry.value)) pending.push({ value: child, depth: entry.depth + 1 });
    } else if (entry.value !== null && !['number', 'boolean'].includes(typeof entry.value)) return false;
  }
  return true;
}
/** Protocol metadata contains integer counters only. Reject rounded fractional/exponent aliases before interpreting them. */
function metadataIntegers(text: string): void {
  for (let i = 0; i < text.length; i++) {
    if (text[i] === '"') {
      for (i++; i < text.length && text[i] !== '"'; i++) if (text[i] === String.fromCharCode(92)) i++;
    } else if (text[i] === '-' || /[0-9]/u.test(text[i])) {
      const start = i; while (i + 1 < text.length && /[0-9eE+.-]/u.test(text[i + 1])) i++;
      const token = text.slice(start, i + 1);
      if (!/^(?:0|-?[1-9][0-9]*)$/u.test(token) || !Number.isSafeInteger(Number(token))) return fail();
    }
  }
}
async function frame(value: PassTextFrame, domain: string, format: string | null = domain, integers = true): Promise<Record<string, unknown>> {
  if (!fields(value, ['text', 'digest']) || typeof value.text !== 'string' || bytes(value.text) > 4_194_304 || !digest(value.digest)) return fail();
  const parsed = obj(parse(value.text)); // Duplicate members, invalid Unicode and structural bounds are rejected before hashing.
  if (integers) metadataIntegers(value.text);
  if (format !== null && parsed['format'] !== format || !await verify(domain, value.text, value.digest)) return fail();
  return parsed;
}
function axes(result: Record<string, unknown>): Readonly<Record<string, unknown>> {
  const names: Readonly<Record<string, string>> = {
    'coglatas.completed': 'Completed', 'coglatas.not-run': 'NotRun', 'coglatas.cancelled': 'Cancelled',
    'coglatas.timed-out': 'TimedOut', 'coglatas.resource-exhausted': 'ResourceExhausted', 'coglatas.internal-error': 'InternalError',
    'coglatas.complete': 'Complete', 'coglatas.partial': 'Partial', 'coglatas.none': 'None',
    'coglatas.pass': 'Pass', 'coglatas.fail': 'Fail', 'coglatas.inconclusive': 'Inconclusive', 'coglatas.error': 'Error'
  };
  const name = (v: unknown) => typeof v === 'string' && Object.hasOwn(names, v) ? names[v] : fail();
  if (typeof result['evidenceComplete'] !== 'boolean') return fail();
  return { execution: name(result['execution']), coverage: name(result['coverage']), verdict: name(result['verdict']), evidenceComplete: result['evidenceComplete'] };
}


interface VerifiedPassSnapshot { readonly snapshot: PassSnapshotFrames; readonly data: Readonly<Record<string, unknown>>; }
interface VerifiedPassRun {
  readonly frame: PassRecordingFrame; readonly manifest: Readonly<Record<string, unknown>>; readonly response: Readonly<Record<string, unknown>>;
  readonly receipt: Readonly<Record<string, unknown>>; readonly policy: Readonly<Record<string, unknown>>; readonly record: Readonly<Record<string, unknown>> | null;
  readonly snapshots: Readonly<Record<RecordedPassChoice, VerifiedPassSnapshot>> | null;
}
interface PassBinding {
  readonly value: PreviewCase; readonly sources: AdmittedRootSource; readonly projection: PassRecordingProjection; readonly member: PassRecordingFrame;
  readonly label: PassRecordingLabel; readonly fingerprint: string; readonly verified: VerifiedPassRun;
}
const passBindings = new WeakMap<AdmittedPassRecording, PassBinding>();
const passViewBindings = new WeakMap<AdmittedRecordedPasses, AdmittedPassRecording>();
const projectionOf = (v: PreviewCase): PassRecordingProjection | null | undefined =>
  (v as PreviewCase & { readonly passRecordings?: PassRecordingProjection | null }).passRecordings;
const observationFormat = 'coglatas.local-initial-root-pass-observation/1';
const snapshotFormat = 'coglatas.local-initial-root-pass-observation-snapshot/1';
const passOrder = ['coglatas.parse-normalize', 'coglatas.binding', 'coglatas.semantic-validation-ir', 'coglatas.analysis', 'coglatas.publication'];
const eventRoles: readonly (readonly string[])[] = [['source'], ['decoded-source'], ['input'], [], [], ['semantic-model', 'ir', 'source-map'], [], ['outcomes'], [], ['root-result']];
const roleDomains: Readonly<Record<string, string>> = { source: 'coglatas.project-source/1', 'decoded-source': 'coglatas.project-source/1',
  input: 'coglatas.compiler-input/1', 'semantic-model': 'coglatas.semantic-model/1', ir: 'coglatas.project-ir/1',
  'source-map': 'coglatas.semantic-source-map/1', outcomes: 'coglatas.analysis/1', 'root-result': 'coglatas.compiler-result/1' };
const eventGaps = [
  'Only the actual Source bytes passed to the decoder are referenced.', 'The actual decoded Source returned.',
  'External materializations are not pre-enumerated by capture.', 'The original binding model has no published canonical body at this boundary.',
  'Actual binding model and lazy unit/calendar supplies are not serialized here.', 'Only existing original canonical IR outputs are referenced.',
  'Actual binding/validation objects supply this root analysis; no canonical input projection is invented.', 'Original root rule outcomes only; no regional observation is inferred.',
  'The original result constructor inputs have no canonical pass-input body here.', 'Original root result before dependency attachment.'
];
const recordGaps = [
  'No optimization or execution-plan trace is claimed.', 'Materialized supplies are not pre-enumerated or serialized by these hooks.',
  'Binding and validation engine objects without an existing canonical body remain explicit gaps.',
  'Root publication precedes dependency attachment and does not impersonate the final result.',
  'Thrown failures that do not return a compilation do not return an observation.', 'This observation is not FullDump or FullTrace.'
];
function verifyObservation(value: PreviewCase, record: Record<string, unknown>, policy: Record<string, unknown>, compiler: unknown,
  knownBodyBytes: Readonly<{source: number; input: number}>): Map<string, Readonly<Record<string, unknown>>> {
  const state = record['state']; const resultAxes = axes(obj(value.result));
  const wanted = policy['maximumEvents'] === 1 ? 'EventLimitExceeded' : policy['mode'] === 'Manifest' ? 'ManifestOnly' :
    policy['maximumBodyBytes'] === 1 ? 'BodyBudgetExceeded' : 'AvailableOriginalJson';
  // This separate offline recipe covers the ten completed initial-root entry/return boundaries, not arbitrary trace adoption.
  if (resultAxes['execution'] !== 'Completed' || state !== wanted || !Array.isArray(record['events']) ||
    record['events'].length !== (state === 'EventLimitExceeded' ? 0 : 10) || !integer(record['logicalOperationsConsumed'], 0, Number.MAX_SAFE_INTEGER) ||
    !integer(record['retainedBodyBytes'], 0, policy['maximumBodyBytes'] as number) || state !== 'AvailableOriginalJson' && record['retainedBodyBytes'] !== 0) return fail();
  const basis = { inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, context: value.context, compiler, resultDigest: value.resultDigest };
  expect(record, { format: observationFormat, state, scope: 'ActualInitialRootFunctionBoundaries', capture: { declared: 'BeforeCompile', mode: policy['mode'],
    maximumBodyBytes: policy['maximumBodyBytes'], maximumEvents: policy['maximumEvents'] }, visitedEvents: 10,
    retainedEvents: state === 'EventLimitExceeded' ? 0 : 10, retainedBodyBytes: record['retainedBodyBytes'], basis, result: resultAxes,
    logicalOperationsConsumed: record['logicalOperationsConsumed'], events: record['events'], regionalEvaluation: 'NotRecorded',
    compilerRunIdentity: 'NotAllocated', runSelection: 'NotEvaluated', causalMeaning: 'NotRecorded', producerTrust: 'NotEvaluated',
    authority: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted', gaps: recordGaps });
  const refs = new Map<string, Readonly<Record<string, unknown>>>();
  const artifacts = obj(value.result)['artifacts']; if (!Array.isArray(artifacts)) return fail();
  const noIr = !artifacts.some(v => obj(v)['role'] === 'coglatas.ir');
  for (const [i, raw] of record['events'].entries()) {
    const event = obj(raw); const originals = event['originalBodies'];
    if (!Array.isArray(originals)) return fail();
    expect(event, { ordinal: i, passId: passOrder[Math.floor(i / 2)], phase: i % 2 ? 'Returned' : 'Entered', gap: i === 5 && noIr ? 'No original IR was produced.' : eventGaps[i], originalBodies: originals });
    const roles = i === 5 && noIr ? [] : eventRoles[i];
    expect(originals.map(v => obj(v)['role']), roles);
    for (const rawRef of originals) {
      const ref = obj(rawRef); const role = ref['role'] as string;
      if (!digest(ref['digest']) || !integer(ref['bytes'], 1, 4_194_304)) return fail();
      expect(ref, { role, domain: roleDomains[role], digest: ref['digest'], bytes: ref['bytes'] });
      if (role === 'source' || role === 'decoded-source') { expect(ref['digest'], value.sourceDigest); expect(ref['bytes'], knownBodyBytes.source); }
      if (role === 'input') { expect(ref['digest'], value.inputDigest); expect(ref['bytes'], knownBodyBytes.input); }
      if (['semantic-model', 'ir', 'source-map'].includes(role)) {
        const matches = artifacts.filter(v => obj(v)['role'] === 'coglatas.' + role); if (matches.length !== 1) return fail();
        expect(ref['digest'], obj(matches[0])['digest']); expect(obj(matches[0])['inputDigest'], value.inputDigest);
      }
      const key = String(ref['domain']) + ':' + (ref['digest'] as Digest).value;
      if (refs.has(key)) expect(refs.get(key)!['bytes'], ref['bytes']);
      refs.set(key, ref);
    }
  }
  if (state === 'AvailableOriginalJson' && [...refs.values()].reduce((sum, v) => sum + (v['bytes'] as number), 0) !== record['retainedBodyBytes']) return fail();
  return refs;
}

async function verifyPassSnapshots(value: PreviewCase, run: PassRecordingFrame, record: Record<string, unknown>, refs: Map<string, Readonly<Record<string, unknown>>>): Promise<Readonly<Record<RecordedPassChoice, VerifiedPassSnapshot>>> {
  if (!run.snapshots || !fields(run.snapshots, choices)) return fail();
  const verified = {} as Record<RecordedPassChoice, VerifiedPassSnapshot>;
  for (const choice of choices) {
    const view = run.snapshots[choice]; if (!fields(view, ['frame', 'bodies']) || !Array.isArray(view.bodies) || view.bodies.length > 64) return fail();
    const data = await frame(view.frame, snapshotFormat); const mode = choice === 'manifest' ? 'Manifest' : 'AvailableOriginalJson';
    const budget = choice === 'budget' ? 1 : 1_048_576;
    const state = choice === 'manifest' ? 'ManifestOnly' : record['retainedBodyBytes'] === 0 ? 'NoRecordedOriginalBodies' :
      (record['retainedBodyBytes'] as number) > budget ? 'BodyBudgetExceeded' : 'AvailableOriginalJson';
    const expectedBytes = state === 'AvailableOriginalJson' ? record['retainedBodyBytes'] : 0;
    expect(data, { format: snapshotFormat, state, mode, maximumBodyBytes: budget, retainedBodyBytes: expectedBytes, basis: record['basis'],
      originalRecord: { digest: run.observation!.digest, metadata: record }, authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated',
      retentionPolicy: 'NotAdopted', scope: 'OwnedInitialRootOriginalBodiesOnly', regionalEvaluation: 'NotRecorded', compilerRunIdentity: 'NotAllocated',
      runSelection: 'NotEvaluated', causalMeaning: 'NotRecorded' });
    const keys = view.bodies.map(b => b.key); expect(keys, [...keys].sort()); if (new Set(keys).size !== keys.length) return fail();
    if (state !== 'AvailableOriginalJson') { if (keys.length !== 0) return fail(); }
    else { expect(keys, [...refs.keys()].sort()); let retained = 0;
      for (const body of view.bodies) {
        const ref = refs.get(body.key); if (!ref || !fields(body, ['key', 'domain', 'text', 'digest', 'bytes']) ||
          body.domain !== ref['domain'] || !equal(body.digest, ref['digest']) || body.bytes !== ref['bytes'] ||
          typeof body.text !== 'string' || bytes(body.text) !== body.bytes || body.key !== body.domain + ':' + body.digest.value ||
          !await verify(body.domain, body.text, body.digest)) return fail();
        parse(body.text); // Original arrays/numeric bodies retain their tokens; metadata integer restrictions do not apply here.
        retained += body.bytes;
        if (body.domain === 'coglatas.project-source/1' && body.text !== value.sourceText) return fail();
        if (body.domain === 'coglatas.compiler-input/1' && body.text !== value.rootSource?.inputText) return fail();
      }
      if (retained !== expectedBytes) return fail();
    }
    verified[choice] = { snapshot: freeze(view), data: freeze(data) };
  }
  return Object.freeze(verified);
}
async function verifyPassRun(value: PreviewCase, run: PassRecordingFrame, label: PassRecordingLabel, compiler: unknown,
  knownBodyBytes: Readonly<{source: number; input: number}>): Promise<VerifiedPassRun> {
  if (!fields(run, ['request', 'policy', 'baseExecution', 'materializations', 'manifest', 'response', 'receipt', 'result', 'observation', 'snapshots'])) return fail();
  const [request, policy, base, materializations, manifest, response, receipt] = await Promise.all([
    frame(run.request, 'coglatas.local-pass-observation-compiler-host-input/1'), frame(run.policy, 'coglatas.local-pass-observation-capture-policy/1'),
    frame(run.baseExecution, 'coglatas.compiler-host-input/1', null), frame(run.materializations, 'coglatas.host-materializations/1'),
    frame(run.manifest, 'coglatas.local-pass-observation-compiler-recording-manifest/1'),
    frame(run.response, 'coglatas.local-pass-observation-compiler-host-response/1'), frame(run.receipt, 'coglatas.local-pass-observation-compiler-recording-receipt/1')
  ]);
  const mode = label === 'original' ? 'Manifest' : 'AvailableOriginalJson'; const maximumBodyBytes = label === 'budget' ? 1 : 1_048_576;
  expect(policy, { format: 'coglatas.local-pass-observation-capture-policy/1', mode, maximumBodyBytes, maximumEvents: label === 'eventGap' ? 1 : 16, scope: 'ActualInitialRootFunctionBoundaries' });
  expect(materializations, { format: 'coglatas.host-materializations/1', snapshots: [], unitDefinitions: [], calendarDefinitions: [] });
  expect(base, { declaredInputDigest: value.inputDigest, materializationsDigest: run.materializations.digest });
  expect(request, { format: 'coglatas.local-pass-observation-compiler-host-input/1', baseExecutionInputDigest: run.baseExecution.digest,
    declaredInputDigest: value.inputDigest, materializationsDigest: run.materializations.digest, observationPolicy: policy, observationPolicyDigest: run.policy.digest });
  if (!guid(manifest['sessionId']) || !guid(manifest['runId']) || !guid(response['attemptId']) ||
    !integer(manifest['clientGeneration'], 0, Number.MAX_SAFE_INTEGER) || !Array.isArray(manifest['knownLineage']) ||
    manifest['knownLineage'].length > 31 || !manifest['knownLineage'].every(guid) || new Set(manifest['knownLineage']).size !== manifest['knownLineage'].length ||
    manifest['knownLineage'].includes(manifest['runId'])) return fail();
  const origin = manifest['origin'];
  if (origin !== null && (!fields(origin, ['runId', 'manifestDigest', 'receiptDigest', 'attemptId']) || !guid(obj(origin)['runId']) || !guid(obj(origin)['attemptId']) ||
    !digest(obj(origin)['manifestDigest']) || !digest(obj(origin)['receiptDigest']))) return fail();
  expect(manifest, { format: 'coglatas.local-pass-observation-compiler-recording-manifest/1', sessionId: manifest['sessionId'], runId: manifest['runId'],
    executionInputDigest: run.request.digest, baseExecutionInputDigest: run.baseExecution.digest, inputDigest: value.inputDigest, sourceDigest: value.sourceDigest,
    context: value.context, compiler, materializationsDigest: run.materializations.digest, clientGeneration: manifest['clientGeneration'],
    capture: { declared: 'BeforeExecution', policy, policyDigest: run.policy.digest }, origin, knownLineage: manifest['knownLineage'],
    identityScope: 'OwnedLocalSessionOnly', authority: 'NotEvaluated', retentionPolicy: 'NotAdopted' });
  const cancelled = label === 'cancelled'; const status = cancelled ? 'Cancelled' : 'Completed';
  const reason = cancelled ? 'coglatas.pass-observation-host.cancelled' : 'coglatas.pass-observation-host.result-produced';
  let record: Record<string, unknown> | null = null; let snapshots: Readonly<Record<RecordedPassChoice, VerifiedPassSnapshot>> | null = null;
  if (cancelled) {
    if (run.result !== null || run.observation !== null || run.snapshots !== null) return fail();
  } else {
    if (!run.result || !run.observation || run.result.text !== value.rootSource?.result.text || !equal(run.result.digest, value.resultDigest)) return fail();
    expect(await frame(run.result, 'coglatas.compiler-result/1', 'coglatas.compiler-result/1', false), value.result);
    record = await frame(run.observation, 'coglatas.local-initial-root-pass-observation/1');
    const refs = verifyObservation(value, record, policy, compiler, knownBodyBytes);
    snapshots = await verifyPassSnapshots(value, run, record, refs);
  }
  const resultAxes = axes(obj(value.result));
  expect(response, { format: 'coglatas.local-pass-observation-compiler-host-response/1', runId: manifest['runId'], manifestDigest: run.manifest.digest,
    executionInputDigest: run.request.digest, baseExecutionInputDigest: run.baseExecution.digest, materializationsDigest: run.materializations.digest,
    observationPolicyDigest: run.policy.digest, clientGeneration: manifest['clientGeneration'], attemptId: response['attemptId'], hostStatus: status, reasonCode: reason,
    resultDigest: cancelled ? null : run.result!.digest, observationDigest: cancelled ? null : run.observation!.digest,
    compilerExecution: cancelled ? null : resultAxes['execution'], captureState: record ? record['state'] : null,
    identityScope: 'OwnedLocalDeclaredInvocationOnly', regionalEvaluation: 'NotRecorded', authority: 'NotEvaluated',
    producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted' });
  expect(receipt, { format: 'coglatas.local-pass-observation-compiler-recording-receipt/1', runId: manifest['runId'], manifestDigest: run.manifest.digest,
    executionInputDigest: run.request.digest, observationPolicyDigest: run.policy.digest, attemptId: response['attemptId'], hostStatus: status, reasonCode: reason,
    hostResponseDigest: run.response.digest, resultDigest: cancelled ? null : run.result!.digest, observationRecordDigest: cancelled ? null : run.observation!.digest,
    recordingState: cancelled ? 'HostOutcomeWithoutCompilerResult' : 'OriginalInitialRootObservation', captureState: record ? record['state'] : null,
    identityScope: 'OwnedLocalSessionAndHostAttempt', earlierIntermediateStates: 'NotRecorded', regionalEvaluation: 'NotRecorded', authority: 'NotEvaluated',
    producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted' });
  return { frame: freeze(run), manifest: freeze(manifest), response: freeze(response), receipt: freeze(receipt), policy: freeze(policy),
    record: record ? freeze(record) : null, snapshots };
}

/** Structural run-identity uniqueness across this offline fixture; it grants no producer or runtime authority. */
export function passRunIdentitiesAreUnique(cases: readonly PreviewCase[]): boolean {
  try {
    const runs = new Set<string>(); const attempts = new Set<string>(); const sessions = new Set<string>();
    for (const value of cases) {
      const projection = projectionOf(value); if (projection == null) continue;
      if (!fields(projection, ['format', 'source', 'input', ...labels]) || projection.format !== 'coglatas.local-web-pass-recording-fixture/1') return false;
      let session: string | null = null;
      for (const label of labels) {
        const member = projection[label]; if (!member || typeof member.manifest?.text !== 'string' || typeof member.response?.text !== 'string') return false;
        const manifest = obj(parse(member.manifest.text)); const response = obj(parse(member.response.text));
        metadataIntegers(member.manifest.text); metadataIntegers(member.response.text);
        if (!guid(manifest['runId']) || !guid(manifest['sessionId']) || !guid(response['attemptId']) || runs.has(manifest['runId']) || attempts.has(response['attemptId'])) return false;
        runs.add(manifest['runId']); attempts.add(response['attemptId']);
        if (session === null) { session = manifest['sessionId']; if (sessions.has(session)) return false; sessions.add(session); }
        else if (session !== manifest['sessionId']) return false;
      }
    }
    return [...runs].every(id => !attempts.has(id));
  } catch { return false; }
}

/** Exact local member ownership only. Original hashes are not current access, causal meaning or producer trust. */
export function passRecordingMatches(value: PreviewCase, sources: AdmittedRootSource, admitted: AdmittedPassRecording): boolean {
  try {
    const binding = passBindings.get(admitted); const projection = projectionOf(value);
    return !!binding && binding.value === value && binding.sources === sources && admitted.sources === sources && rootSourceMatches(value, sources) &&
      projection === binding.projection && projection?.[binding.label] === binding.member && bounded(projection) && JSON.stringify(projection) === binding.fingerprint;
  } catch { return false; }
}

/** Verify every original run and snapshot mode before admitting one member; no Compiler or record deserializer is installed. */
export async function readPassRecording(value: PreviewCase, sources: AdmittedRootSource,
  label: PassRecordingLabel = 'original'): Promise<AdmittedPassRecording | null> {
  if (!labels.includes(label) || !rootSourceMatches(value, sources)) return fail();
  const projection = projectionOf(value); if (projection == null) return null;
  if (!fields(projection, ['format', 'source', 'input', ...labels]) || projection.format !== 'coglatas.local-web-pass-recording-fixture/1' || !bounded(projection)) return fail();
  const fingerprint = JSON.stringify(projection); const member = projection[label]; const memberRefs = labels.map(name => projection[name]);
  if (new Set(memberRefs).size !== labels.length) return fail(); const copy = freeze(structuredClone(projection));
  expect(await frame(copy.source, 'coglatas.project-source/1', 'coglatas.project-source/1', false), sources.source);
  const input = await frame(copy.input, 'coglatas.compiler-input/1', 'coglatas.compiler-input/1', false);
  if (copy.source.text !== sources.sourceText || copy.input.text !== sources.projection.inputText || !equal(copy.source.digest, value.sourceDigest) ||
    !equal(copy.input.digest, value.inputDigest)) return fail();
  const compiler = obj(input['settings'])['compiler']; const verified = new Map<PassRecordingLabel, VerifiedPassRun>();
  const knownBodyBytes = Object.freeze({source: bytes(copy.source.text), input: bytes(copy.input.text)});
  for (const name of labels) verified.set(name, await verifyPassRun(value, copy[name], name, compiler, knownBodyBytes));
  const original = verified.get('original')!; const diagnostic = verified.get('diagnostic')!; const cancelled = verified.get('cancelled')!; const retry = verified.get('retry')!;
  const runs = new Set([...verified.values()].map(run => run.manifest['runId'])); const attempts = new Set([...verified.values()].map(run => run.response['attemptId']));
  if (runs.size !== 6 || attempts.size !== 6 || [...runs].some(id => attempts.has(id))) return fail();
  for (const [name, run] of verified) {
    expect(run.manifest['sessionId'], original.manifest['sessionId']); expect(run.manifest['clientGeneration'], original.manifest['clientGeneration']);
    expect(run.frame.baseExecution, original.frame.baseExecution); expect(run.frame.materializations, original.frame.materializations);
    const ancestor = name === 'diagnostic' ? original : name === 'budget' ? diagnostic : name === 'eventGap' ? verified.get('budget')! : name === 'retry' ? cancelled : null;
    expect(run.manifest['origin'], ancestor === null ? null : { runId: ancestor.manifest['runId'], manifestDigest: ancestor.frame.manifest.digest,
      receiptDigest: ancestor.frame.receipt.digest, attemptId: ancestor.response['attemptId'] });
    expect(run.manifest['knownLineage'], ancestor === null ? [] : [...ancestor.manifest['knownLineage'] as string[], ancestor.manifest['runId']]);
  }
  expect(retry.frame.observation, diagnostic.frame.observation);
  for (const name of ['original', 'budget', 'eventGap', 'retry'] as const) expect(verified.get(name)!.record!['visitedEvents'], diagnostic.record!['visitedEvents']);
  if (!rootSourceMatches(value, sources) || projectionOf(value) !== projection || JSON.stringify(projection) !== fingerprint ||
    labels.some((name, index) => projection[name] !== memberRefs[index])) return fail();
  const selected = verified.get(label)!; const manifest = selected.manifest; const response = selected.response;
  const admitted: AdmittedPassRecording = Object.freeze({ sources, label, runId: manifest['runId'] as string, sessionId: manifest['sessionId'] as string,
    attemptId: response['attemptId'] as string, originRunId: manifest['origin'] === null ? null : obj(manifest['origin'])['runId'] as string,
    hostStatus: response['hostStatus'] as 'Completed' | 'Cancelled', captureMode: selected.policy['mode'] as 'Manifest' | 'AvailableOriginalJson',
    events: selected.record ? selected.record['events'] as readonly AdmittedPassEvent[] : [],
    originalCaptureState: selected.record ? selected.record['state'] as string : null,
    visitedEvents: selected.record ? selected.record['visitedEvents'] as number : null,
    retainedEvents: selected.record ? selected.record['retainedEvents'] as number : null,
    retainedBodyBytes: selected.record ? selected.record['retainedBodyBytes'] as number : null, maximumBodyBytes: selected.policy['maximumBodyBytes'] as number,
    maximumEvents: selected.policy['maximumEvents'] as number,
    manifest: selected.frame.manifest, response: selected.frame.response, receipt: selected.frame.receipt, observation: selected.frame.observation,
    manifestData: selected.manifest, responseData: selected.response, receiptData: selected.receipt, observationData: selected.record });
  passBindings.set(admitted, { value, sources, projection, member, label, fingerprint, verified: selected }); return admitted;
}


export function recordedPassesMatch(value: PreviewCase, sources: AdmittedRootSource, recording: AdmittedPassRecording, admitted: AdmittedRecordedPasses): boolean {
  return passViewBindings.get(admitted) === recording && admitted.recording === recording && passRecordingMatches(value, sources, recording);
}
/** Explicit root-wide original JSON; metadata never exposes a body map or claims current access. */
export async function readRecordedPasses(value: PreviewCase, sources: AdmittedRootSource, recording: AdmittedPassRecording,
  choice: RecordedPassChoice = 'manifest'): Promise<AdmittedRecordedPasses | null> {
  if (!choices.includes(choice) || !passRecordingMatches(value, sources, recording)) return fail();
  const binding = passBindings.get(recording)!; const view = binding.verified.snapshots?.[choice];
  if (recording.hostStatus !== 'Completed' || !view || !binding.verified.record) return null;
  const data = view.data;
  const admitted: AdmittedRecordedPasses = Object.freeze({ text: view.snapshot.frame.text, digest: view.snapshot.frame.digest, recording, choice,
    state: data['state'] as string, mode: data['mode'] as 'Manifest' | 'AvailableOriginalJson', maximumBodyBytes: data['maximumBodyBytes'] as number,
    retainedBodyBytes: data['retainedBodyBytes'] as number, originalCaptureState: binding.verified.record['state'] as string, bodies: view.snapshot.bodies, data });
  if (!passRecordingMatches(value, sources, recording)) return fail(); passViewBindings.set(admitted, recording); return admitted;
}
