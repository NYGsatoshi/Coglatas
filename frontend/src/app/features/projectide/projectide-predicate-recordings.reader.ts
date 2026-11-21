import { Diagnostic, Digest, PreviewCase } from './projectide.contracts';
import { AdmittedRootSource, rootSourceMatches } from './projectide-root-source.reader';
import { originalJsonArrayValues } from './projectide-original-json';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

export type PredicateRecordingLabel = 'original' | 'diagnostic' | 'budget' | 'cancelled' | 'retry';
export type RecordedPredicateChoice = 'manifest' | 'available' | 'budget';
export interface PredicateTextFrame { readonly text: string; readonly digest: Digest; }
export interface CapturedPredicateBody extends PredicateTextFrame { readonly bytes: number; }
export interface PredicateDiagnosticFrames {
  readonly diagnostic: PredicateTextFrame; readonly manifest: PredicateTextFrame;
  readonly available: PredicateTextFrame; readonly budget: PredicateTextFrame;
}
export interface PredicateRecordingFrame {
  readonly request: PredicateTextFrame; readonly policy: PredicateTextFrame; readonly baseExecution: PredicateTextFrame;
  readonly materializations: PredicateTextFrame; readonly manifest: PredicateTextFrame; readonly response: PredicateTextFrame;
  readonly receipt: PredicateTextFrame; readonly result: PredicateTextFrame | null; readonly predicate: PredicateTextFrame | null;
  readonly bodies: readonly CapturedPredicateBody[]; readonly diagnostics: readonly PredicateDiagnosticFrames[];
}
export interface PredicateRecordingProjection {
  readonly format: 'coglatas.local-web-predicate-recording-fixture/1';
  readonly source: PredicateTextFrame; readonly input: PredicateTextFrame;
  readonly original: PredicateRecordingFrame; readonly diagnostic: PredicateRecordingFrame;
  readonly budget: PredicateRecordingFrame; readonly cancelled: PredicateRecordingFrame; readonly retry: PredicateRecordingFrame;
}
export interface AdmittedPredicateRecording {
  readonly sources: AdmittedRootSource; readonly label: PredicateRecordingLabel;
  readonly runId: string; readonly attemptId: string; readonly sessionId: string; readonly originRunId: string | null;
  readonly hostStatus: 'Completed' | 'Cancelled'; readonly captureMode: 'Manifest' | 'OriginalRootArrays';
  readonly originalCaptureState: string | null; readonly maximumBodyBytes: number; readonly maximumDistinctBodies: number;
  readonly manifest: PredicateTextFrame; readonly response: PredicateTextFrame; readonly receipt: PredicateTextFrame;
  readonly predicate: PredicateTextFrame | null;
  readonly manifestData: Readonly<Record<string, unknown>>; readonly responseData: Readonly<Record<string, unknown>>;
  readonly receiptData: Readonly<Record<string, unknown>>; readonly predicateData: Readonly<Record<string, unknown>> | null;
}
export interface RecordedPredicateReference {
  readonly digest: Digest; readonly availableInOriginalRecord: boolean; readonly bodyCaptured: boolean;
}
export interface AdmittedRecordedPredicatePremises extends PredicateTextFrame {
  readonly recording: AdmittedPredicateRecording; readonly diagnostic: Diagnostic; readonly choice: RecordedPredicateChoice;
  readonly state: string; readonly mode: 'Manifest' | 'AvailableBodies';
  readonly maximumBodyBytes: number; readonly retainedBodyBytes: number; readonly originalCaptureState: string;
  readonly references: readonly RecordedPredicateReference[]; readonly bodies: readonly CapturedPredicateBody[];
  readonly data: Readonly<Record<string, unknown>>;
}
const labels = ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const;
const choices = ['manifest', 'available', 'budget'] as const;
const fail = (): never => { throw new Error('unsupported-predicate-recording'); };
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
async function frame(value: PredicateTextFrame, domain: string, format: string | null = domain, integers = true): Promise<Record<string, unknown>> {
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
interface VerifiedSnapshot {
  readonly frame: PredicateTextFrame; readonly data: Readonly<Record<string, unknown>>;
  readonly references: readonly RecordedPredicateReference[]; readonly bodies: readonly CapturedPredicateBody[];
}
interface VerifiedDiagnostic { readonly data: Readonly<Record<string, unknown>>; readonly snapshots: Readonly<Record<RecordedPredicateChoice, VerifiedSnapshot>>; }
interface VerifiedRun {
  readonly frame: PredicateRecordingFrame; readonly manifest: Record<string, unknown>; readonly response: Record<string, unknown>;
  readonly receipt: Record<string, unknown>; readonly policy: Record<string, unknown>; readonly record: Record<string, unknown> | null;
  readonly diagnostics: readonly VerifiedDiagnostic[];
}
interface RecordingBinding {
  readonly value: PreviewCase; readonly sources: AdmittedRootSource; readonly projection: PredicateRecordingProjection;
  readonly member: PredicateRecordingFrame; readonly label: PredicateRecordingLabel; readonly fingerprint: string; readonly verified: VerifiedRun;
}
const bindings = new WeakMap<AdmittedPredicateRecording, RecordingBinding>();
const premiseBindings = new WeakMap<AdmittedRecordedPredicatePremises, { recording: AdmittedPredicateRecording; diagnostic: Diagnostic }>();

async function verifySnapshots(value: PreviewCase, run: PredicateRecordingFrame, record: Record<string, unknown>, bodyMap: ReadonlyMap<string, CapturedPredicateBody>,
  compiler: unknown): Promise<readonly VerifiedDiagnostic[]> {
  if (!run.result || !run.predicate) return fail();
  const texts = originalJsonArrayValues(run.result.text, 'diagnostics').filter(text => equal(obj(obj(parse(text))['primaryLocation'])['context'], value.context));
  if (!Array.isArray(run.diagnostics) || run.diagnostics.length !== texts.length || texts.length > 20_000) return fail();
  const resultAxes = axes(obj(value.result)); const verified: VerifiedDiagnostic[] = [];
  for (let index = 0; index < texts.length; index++) {
    const entry = run.diagnostics[index];
    if (!fields(entry, ['diagnostic', 'manifest', 'available', 'budget']) || entry.diagnostic.text !== texts[index]) return fail();
    const diagnostic = await frame(entry.diagnostic, 'coglatas.compiler-diagnostic/1', null, false);
    const premises = obj(diagnostic['evidence'])['premises'];
    if (!Array.isArray(premises) || premises.length > 4096 || !premises.every(digest)) return fail();
    const selected = [...bodyMap.values()].filter(body => premises.some(p => equal(p, body.digest)));
    const total = selected.reduce((n, body) => n + body.bytes, 0);
    const snapshots = {} as Record<RecordedPredicateChoice, VerifiedSnapshot>;
    for (const choice of choices) {
      const snapshot = await frame(entry[choice], 'coglatas.local-recorded-predicate-premise-snapshot/1');
      const mode = choice === 'manifest' ? 'Manifest' : 'AvailableBodies'; const maximumBodyBytes = choice === 'budget' ? 1 : 1_048_576;
      const state = mode === 'Manifest' ? 'ManifestOnly' : premises.length === 0 ? 'NoRecordedPremises'
        : total > maximumBodyBytes ? 'BodyBudgetExceeded' : selected.length ? 'CapturedOriginalArrays' : 'NoAvailableOriginalArrays';
      const bodies = state === 'CapturedOriginalArrays' ? selected : [];
      const references = premises.map(reference => ({
        digest: structuredClone(reference), availableInOriginalRecord: bodyMap.has(reference.value),
        bodyCaptured: bodies.some(body => equal(body.digest, reference))
      }));
      expect(snapshot, {
        format: 'coglatas.local-recorded-predicate-premise-snapshot/1', state, mode, maximumBodyBytes,
        retainedBodyBytes: bodies.reduce((n, body) => n + body.bytes, 0), bodyScope: 'SelectedDiagnosticOriginalRootPredicateArrays',
        regionalEvaluation: 'NotRecorded', relationshipStructure: 'NotRecorded', earlierStates: 'NotRecorded',
        runSelection: 'NotEvaluated', authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted',
        basis: { inputDigest: value.inputDigest, resultDigest: value.resultDigest, sourceDigest: value.sourceDigest, context: value.context,
          compiler, diagnosticId: diagnostic['diagnosticId'], diagnosticDigest: entry.diagnostic.digest },
        originalRecord: { digest: run.predicate.digest, metadata: record }, result: resultAxes, references
      });
      snapshots[choice] = { frame: entry[choice], data: freeze(snapshot), references: freeze(references), bodies: freeze([...bodies]) };
    }
    verified.push({ data: freeze(diagnostic), snapshots: freeze(snapshots) });
  }
  return freeze(verified);
}

async function verifyRun(value: PreviewCase, run: PredicateRecordingFrame, label: PredicateRecordingLabel, compiler: unknown): Promise<VerifiedRun> {
  if (!fields(run, ['request', 'policy', 'baseExecution', 'materializations', 'manifest', 'response', 'receipt', 'result', 'predicate', 'bodies', 'diagnostics']) ||
    !Array.isArray(run.bodies) || run.bodies.length > 1024 || !Array.isArray(run.diagnostics)) return fail();
  const [request, policy, base, materializations, manifest, response, receipt] = await Promise.all([
    frame(run.request, 'coglatas.local-predicate-compiler-host-input/1'), frame(run.policy, 'coglatas.local-predicate-capture-policy/1'),
    frame(run.baseExecution, 'coglatas.compiler-host-input/1', null), frame(run.materializations, 'coglatas.host-materializations/1'),
    frame(run.manifest, 'coglatas.local-predicate-compiler-recording-manifest/1'), frame(run.response, 'coglatas.local-predicate-compiler-host-response/1'),
    frame(run.receipt, 'coglatas.local-predicate-compiler-recording-receipt/1')
  ]);
  const mode = label === 'original' ? 'Manifest' : 'OriginalRootArrays'; const maximumBodyBytes = label === 'budget' ? 1 : 1_048_576;
  expect(policy, { format: 'coglatas.local-predicate-capture-policy/1', mode, maximumBodyBytes, maximumDistinctBodies: 1024, scope: 'RootInitialRuleEvaluation' });
  // This separate root fixture recipe has no unit/calendar/external materializations. It is not a general remote codec.
  expect(materializations, { format: 'coglatas.host-materializations/1', snapshots: [], unitDefinitions: [], calendarDefinitions: [] });
  expect(base, { declaredInputDigest: value.inputDigest, materializationsDigest: run.materializations.digest });
  expect(request, { format: 'coglatas.local-predicate-compiler-host-input/1', baseExecutionInputDigest: run.baseExecution.digest,
    declaredInputDigest: value.inputDigest, materializationsDigest: run.materializations.digest, capturePolicy: policy, capturePolicyDigest: run.policy.digest });
  if (!guid(manifest['sessionId']) || !guid(manifest['runId']) || !integer(manifest['clientGeneration'], 0, Number.MAX_SAFE_INTEGER) ||
    !Array.isArray(manifest['knownLineage']) || manifest['knownLineage'].length > 31 || !manifest['knownLineage'].every(guid) ||
    new Set(manifest['knownLineage']).size !== manifest['knownLineage'].length || manifest['knownLineage'].includes(manifest['runId']) ||
    !guid(response['attemptId'])) return fail();
  const origin = manifest['origin'];
  if (origin !== null && (!fields(origin, ['runId', 'manifestDigest', 'receiptDigest', 'attemptId']) || !guid(obj(origin)['runId']) ||
    !guid(obj(origin)['attemptId']) || !digest(obj(origin)['manifestDigest']) || !digest(obj(origin)['receiptDigest']))) return fail();
  expect(manifest, {
    format: 'coglatas.local-predicate-compiler-recording-manifest/1', sessionId: manifest['sessionId'], runId: manifest['runId'],
    executionInputDigest: run.request.digest, baseExecutionInputDigest: run.baseExecution.digest, inputDigest: value.inputDigest,
    sourceDigest: value.sourceDigest, context: value.context, compiler, materializationsDigest: run.materializations.digest,
    clientGeneration: manifest['clientGeneration'], capture: { declared: 'BeforeExecution', policy, policyDigest: run.policy.digest },
    origin, knownLineage: manifest['knownLineage'], identityScope: 'OwnedLocalSessionOnly', authority: 'NotEvaluated', retentionPolicy: 'NotAdopted'
  });
  const cancelled = label === 'cancelled'; const status = cancelled ? 'Cancelled' : 'Completed';
  const reason = cancelled ? 'coglatas.predicate-host.cancelled' : 'coglatas.predicate-host.result-produced';
  let record: Record<string, unknown> | null = null; const bodyMap = new Map<string, CapturedPredicateBody>();
  if (cancelled) {
    if (run.result !== null || run.predicate !== null || run.bodies.length || run.diagnostics.length) return fail();
  } else {
    if (!run.result || !run.predicate || run.result.text !== value.rootSource?.result.text || !equal(run.result.digest, value.resultDigest)) return fail();
    expect(await frame(run.result, 'coglatas.compiler-result/1', 'coglatas.compiler-result/1', false), value.result);
    record = await frame(run.predicate, 'coglatas.local-root-predicate-premise-record/1');
    for (const body of run.bodies) {
      if (!fields(body, ['text', 'digest', 'bytes']) || typeof body.text !== 'string' || !integer(body.bytes, 1, 33_554_432) ||
        body.bytes !== bytes(body.text) || !digest(body.digest) || bodyMap.has(body.digest.value)) return fail();
      const data = parse(body.text);
      if (!Array.isArray(data) || data.length === 0 || !await verify('coglatas.predicate-premises/1', body.text, body.digest)) return fail();
      bodyMap.set(body.digest.value, freeze(body));
    }
    const retained = [...bodyMap.values()].reduce((n, body) => n + body.bytes, 0);
    const resultAxes = axes(obj(value.result)); const state = record['state'];
    const allowed = ['ManifestOnly', 'CapturedOriginalArrays', 'NoOriginalArraysObserved', 'BodyBudgetExceeded', 'BodyCountLimitReached', 'CaptureInterrupted'];
    if (typeof state !== 'string' || !allowed.includes(state) || retained > maximumBodyBytes ||
      mode === 'Manifest' && state !== 'ManifestOnly' || mode !== 'Manifest' && state === 'ManifestOnly' ||
      state === 'CapturedOriginalArrays' && (!bodyMap.size || resultAxes['execution'] !== 'Completed') ||
      state !== 'CapturedOriginalArrays' && bodyMap.size ||
      state === 'CaptureInterrupted' && resultAxes['execution'] === 'Completed' ||
      mode !== 'Manifest' && resultAxes['execution'] !== 'Completed' && state !== 'CaptureInterrupted') return fail();
    expect(record, {
      format: 'coglatas.local-root-predicate-premise-record/1', state,
      capture: { declared: 'BeforeExecution', mode, maximumBodyBytes, maximumDistinctBodies: 1024 }, scope: 'RootInitialRuleEvaluation',
      regionalEvaluation: 'NotRecorded', relationshipStructure: 'NotRecorded', earlierStates: 'NotRecorded', runIdentity: 'NotAllocated',
      authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted',
      retainedBodyBytes: retained, basis: { inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, context: value.context, compiler, resultDigest: value.resultDigest },
      result: resultAxes, bodies: [...bodyMap.values()].sort((a, b) => a.digest.value < b.digest.value ? -1 : a.digest.value > b.digest.value ? 1 : 0).map(body => ({ digest: body.digest, bytes: body.bytes }))
    });
  }
  expect(response, {
    format: 'coglatas.local-predicate-compiler-host-response/1', executionInputDigest: run.request.digest, baseExecutionInputDigest: run.baseExecution.digest,
    materializationsDigest: run.materializations.digest, capturePolicyDigest: run.policy.digest, clientGeneration: manifest['clientGeneration'],
    attemptId: response['attemptId'], hostStatus: status, reasonCode: reason, resultDigest: cancelled ? null : run.result!.digest,
    predicateRecordDigest: cancelled ? null : run.predicate!.digest, identityScope: 'OwnedLocalInvocationOnly',
    authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted'
  });
  expect(receipt, {
    format: 'coglatas.local-predicate-compiler-recording-receipt/1', runId: manifest['runId'], manifestDigest: run.manifest.digest,
    executionInputDigest: run.request.digest, capturePolicyDigest: run.policy.digest, attemptId: response['attemptId'],
    hostStatus: status, reasonCode: reason, hostResponseDigest: run.response.digest, resultDigest: cancelled ? null : run.result!.digest,
    predicateRecordDigest: cancelled ? null : run.predicate!.digest, recordingState: cancelled ? 'HostOutcomeWithoutCompilerResult' : 'OriginalRootPredicateRecording',
    identityScope: 'OwnedLocalSessionAndHostAttempt', earlierIntermediateStates: 'NotRecorded', relationshipStructure: 'NotRecorded',
    authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted'
  });
  const diagnostics = record ? await verifySnapshots(value, run, record, bodyMap, compiler) : [];
  return { frame: freeze(run), manifest: freeze(manifest), response: freeze(response), receipt: freeze(receipt),
    policy: freeze(policy), record: record ? freeze(record) : null, diagnostics };
}

/** Local fixture/member ownership only. Hashes and recorded runs grant no current runtime permission or trusted producer. */
export function predicateRecordingMatches(value: PreviewCase, sources: AdmittedRootSource, admitted: AdmittedPredicateRecording): boolean {
  try {
    const binding = bindings.get(admitted); const projection = value.predicateRecordings;
    return !!binding && binding.value === value && binding.sources === sources && admitted.sources === sources && rootSourceMatches(value, sources) &&
      projection === binding.projection && projection?.[binding.label] === binding.member && bounded(projection) && JSON.stringify(projection) === binding.fingerprint;
  } catch { return false; }
}

/** Eagerly verifies every original member, even for a metadata-only Manifest read. Runs no Compiler. */
export async function readPredicateRecording(value: PreviewCase, sources: AdmittedRootSource,
  label: PredicateRecordingLabel = 'original'): Promise<AdmittedPredicateRecording | null> {
  if (!labels.includes(label) || !rootSourceMatches(value, sources)) return fail();
  const projection = value.predicateRecordings;
  if (projection == null) return null;
  if (!fields(projection, ['format', 'source', 'input', ...labels]) ||
    projection.format !== 'coglatas.local-web-predicate-recording-fixture/1' || !bounded(projection)) return fail();
  const fingerprint = JSON.stringify(projection); const member = projection[label]; const memberRefs = labels.map(name => projection[name]);
  if (new Set(memberRefs).size !== labels.length) return fail();
  const copy = structuredClone(projection);
  expect(await frame(copy.source, 'coglatas.project-source/1', 'coglatas.project-source/1', false), sources.source);
  const input = await frame(copy.input, 'coglatas.compiler-input/1', 'coglatas.compiler-input/1', false);
  if (copy.source.text !== sources.sourceText || copy.input.text !== sources.projection.inputText ||
    !equal(copy.source.digest, value.sourceDigest) || !equal(copy.input.digest, value.inputDigest)) return fail();
  const compiler = obj(input['settings'])['compiler'];
  const verified = new Map<PredicateRecordingLabel, VerifiedRun>();
  for (const name of labels) verified.set(name, await verifyRun(value, copy[name], name, compiler));
  const original = verified.get('original')!; const diagnostic = verified.get('diagnostic')!; const cancelled = verified.get('cancelled')!; const retry = verified.get('retry')!;
  if (new Set([...verified.values()].map(run => run.manifest['runId'])).size !== 5 ||
    new Set([...verified.values()].map(run => run.response['attemptId'])).size !== 5) return fail();
  for (const [name, run] of verified) {
    expect(run.manifest['sessionId'], original.manifest['sessionId']); expect(run.manifest['clientGeneration'], original.manifest['clientGeneration']);
    expect(run.frame.baseExecution, original.frame.baseExecution); expect(run.frame.materializations, original.frame.materializations);
    const ancestor = name === 'diagnostic' ? original : name === 'budget' ? diagnostic : name === 'retry' ? cancelled : null;
    expect(run.manifest['origin'], ancestor === null ? null : { runId: ancestor.manifest['runId'], manifestDigest: ancestor.frame.manifest.digest,
      receiptDigest: ancestor.frame.receipt.digest, attemptId: ancestor.response['attemptId'] });
    expect(run.manifest['knownLineage'], ancestor === null ? [] : [...ancestor.manifest['knownLineage'] as string[], ancestor.manifest['runId']]);
  }
  expect(retry.frame.predicate, diagnostic.frame.predicate);
  const state = diagnostic.record!['state']; const budgetState = verified.get('budget')!.record!['state'];
  expect(budgetState, state === 'NoOriginalArraysObserved' || state === 'CaptureInterrupted' ? state : 'BodyBudgetExceeded');
  if (!rootSourceMatches(value, sources) || value.predicateRecordings !== projection || JSON.stringify(projection) !== fingerprint ||
    labels.some((name, index) => projection[name] !== memberRefs[index])) return fail();
  const selected = verified.get(label)!; const manifest = selected.manifest; const response = selected.response;
  const admitted: AdmittedPredicateRecording = Object.freeze({
    sources, label, runId: manifest['runId'] as string, sessionId: manifest['sessionId'] as string, attemptId: response['attemptId'] as string,
    originRunId: manifest['origin'] === null ? null : obj(manifest['origin'])['runId'] as string,
    hostStatus: response['hostStatus'] as 'Completed' | 'Cancelled', captureMode: selected.policy['mode'] as 'Manifest' | 'OriginalRootArrays',
    maximumBodyBytes: selected.policy['maximumBodyBytes'] as number, maximumDistinctBodies: selected.policy['maximumDistinctBodies'] as number,
    originalCaptureState: selected.record ? selected.record['state'] as string : null, manifest: selected.frame.manifest, response: selected.frame.response,
    receipt: selected.frame.receipt, predicate: selected.frame.predicate, manifestData: selected.manifest, responseData: selected.response,
    receiptData: selected.receipt, predicateData: selected.record
  });
  bindings.set(admitted, { value, sources, projection, member, label, fingerprint, verified: selected });
  return admitted;
}

export function recordedPredicatePremisesMatch(value: PreviewCase, sources: AdmittedRootSource, recording: AdmittedPredicateRecording,
  diagnostic: Diagnostic, admitted: AdmittedRecordedPredicatePremises): boolean {
  try {
    const binding = premiseBindings.get(admitted);
    return !!binding && binding.recording === recording && binding.diagnostic === diagnostic &&
      value.result.diagnostics.includes(diagnostic) && equal(diagnostic.primaryLocation.context, value.context) &&
      predicateRecordingMatches(value, sources, recording);
  } catch { return false; }
}

/** Returns only the selected diagnostic's original arrays from the selected actual member, without evaluation or reconstruction. */
export async function readRecordedPredicatePremises(value: PreviewCase, sources: AdmittedRootSource, recording: AdmittedPredicateRecording,
  diagnostic: Diagnostic, choice: RecordedPredicateChoice = 'manifest'): Promise<AdmittedRecordedPredicatePremises | null> {
  if (!choices.includes(choice) || !predicateRecordingMatches(value, sources, recording) || !value.result.diagnostics.includes(diagnostic) ||
    !equal(diagnostic.primaryLocation.context, value.context)) return fail();
  const binding = bindings.get(recording)!;
  if (recording.hostStatus !== 'Completed' || !binding.verified.record) return null;
  const index = value.result.diagnostics.filter(item => equal(item.primaryLocation.context, value.context)).indexOf(diagnostic);
  const selected = binding.verified.diagnostics[index];
  if (!selected || !equal(selected.data, diagnostic)) return fail();
  const snapshot = selected.snapshots[choice]; const data = snapshot.data;
  const admitted: AdmittedRecordedPredicatePremises = Object.freeze({
    recording, diagnostic, choice, text: snapshot.frame.text, digest: snapshot.frame.digest, data,
    state: data['state'] as string, mode: data['mode'] as 'Manifest' | 'AvailableBodies',
    maximumBodyBytes: data['maximumBodyBytes'] as number, retainedBodyBytes: data['retainedBodyBytes'] as number,
    originalCaptureState: binding.verified.record['state'] as string, references: snapshot.references, bodies: snapshot.bodies
  });
  premiseBindings.set(admitted, { recording, diagnostic });
  return admitted;
}
