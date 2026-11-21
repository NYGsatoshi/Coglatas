import { Diagnostic, Digest, PreviewCase } from './projectide.contracts';
import { AdmittedRootSource, rootSourceMatches } from './projectide-root-source.reader';
import { originalJsonArrayValues } from './projectide-original-json';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';
import { DeclaredOperatorNode, OperatorTextFrame, OriginalOperatorBody, OriginalOperatorNode, originalOperatorMember, validateOriginalOperatorGraph } from './projectide-operator-recording-structure';

export type OperatorRecordingLabel = 'original' | 'diagnostic' | 'budget' | 'cancelled' | 'retry';
export type RecordedOperatorChoice = 'manifest' | 'available' | 'budget';
export interface OperatorSnapshotFrames {
  readonly frame: OperatorTextFrame; readonly operatorBodies: readonly OriginalOperatorBody[]; readonly evaluationFrames: readonly OriginalOperatorBody[];
}
export interface OperatorDiagnosticFrames {
  readonly diagnostic: OperatorTextFrame; readonly manifest: OperatorSnapshotFrames; readonly available: OperatorSnapshotFrames; readonly budget: OperatorSnapshotFrames;
}
export interface OperatorRecordingFrame {
  readonly request: OperatorTextFrame; readonly policy: OperatorTextFrame; readonly baseExecution: OperatorTextFrame; readonly materializations: OperatorTextFrame;
  readonly manifest: OperatorTextFrame; readonly response: OperatorTextFrame; readonly receipt: OperatorTextFrame;
  readonly result: OperatorTextFrame | null; readonly observation: OperatorTextFrame | null; readonly diagnostics: readonly OperatorDiagnosticFrames[];
}
export interface OperatorRecordingProjection {
  readonly format: 'coglatas.local-web-operator-recording-fixture/1'; readonly source: OperatorTextFrame; readonly input: OperatorTextFrame;
  readonly original: OperatorRecordingFrame; readonly diagnostic: OperatorRecordingFrame; readonly budget: OperatorRecordingFrame;
  readonly cancelled: OperatorRecordingFrame; readonly retry: OperatorRecordingFrame;
}
export interface AdmittedOperatorRecording {
  readonly sources: AdmittedRootSource; readonly label: OperatorRecordingLabel; readonly runId: string; readonly attemptId: string; readonly sessionId: string;
  readonly originRunId: string | null; readonly hostStatus: 'Completed' | 'Cancelled'; readonly captureMode: 'Manifest' | 'ObservedOperators';
  readonly originalCaptureState: string | null; readonly startedEvaluations: number | null; readonly completedEvaluations: number | null;
  readonly observedNodes: number | null; readonly retainedBodyBytes: number | null; readonly maximumBodyBytes: number; readonly maximumNodes: number; readonly maximumEvaluations: number;
  readonly manifest: OperatorTextFrame; readonly response: OperatorTextFrame; readonly receipt: OperatorTextFrame; readonly observation: OperatorTextFrame | null;
  readonly manifestData: Readonly<Record<string, unknown>>; readonly responseData: Readonly<Record<string, unknown>>;
  readonly receiptData: Readonly<Record<string, unknown>>; readonly observationData: Readonly<Record<string, unknown>> | null;
}
export interface AdmittedOperatorEvaluation {
  readonly ordinal: number; readonly ruleId: string; readonly ruleVersion: number; readonly nodes: readonly OriginalOperatorNode[];
  readonly evaluationFrame: OriginalOperatorBody; readonly metadata: Readonly<Record<string, unknown>>;
}
export interface AdmittedRecordedOperators extends OperatorTextFrame {
  readonly recording: AdmittedOperatorRecording; readonly diagnostic: Diagnostic; readonly choice: RecordedOperatorChoice;
  readonly state: string; readonly mode: 'Manifest' | 'AvailableOperators'; readonly maximumBodyBytes: number;
  readonly retainedBodyBytes: number; readonly retainedEvaluationFrameBytes: number; readonly originalCaptureState: string;
  readonly evaluations: readonly AdmittedOperatorEvaluation[]; readonly data: Readonly<Record<string, unknown>>;
}
const labels = ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const;
const choices = ['manifest', 'available', 'budget'] as const;
const fail = (): never => { throw new Error('unsupported-operator-recording'); };
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
async function frame(value: OperatorTextFrame, domain: string, format: string | null = domain, integers = true): Promise<Record<string, unknown>> {
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

interface VerifiedOperatorEvaluation {
  readonly metadata: Readonly<Record<string, unknown>>; readonly predicateText: string; readonly original: Readonly<Record<string, unknown>>;
}
interface VerifiedOperatorSnapshot { readonly snapshot: OperatorSnapshotFrames; readonly data: Readonly<Record<string, unknown>>; readonly evaluations: readonly AdmittedOperatorEvaluation[]; }
interface VerifiedOperatorDiagnostic { readonly data: Readonly<Record<string, unknown>>; readonly snapshots: Readonly<Record<RecordedOperatorChoice, VerifiedOperatorSnapshot>>; }
interface VerifiedOperatorRun {
  readonly frame: OperatorRecordingFrame; readonly manifest: Readonly<Record<string, unknown>>; readonly response: Readonly<Record<string, unknown>>;
  readonly receipt: Readonly<Record<string, unknown>>; readonly policy: Readonly<Record<string, unknown>>; readonly record: Readonly<Record<string, unknown>> | null;
  readonly diagnostics: readonly VerifiedOperatorDiagnostic[];
}
interface OperatorBinding {
  readonly value: PreviewCase; readonly sources: AdmittedRootSource; readonly projection: OperatorRecordingProjection; readonly member: OperatorRecordingFrame;
  readonly label: OperatorRecordingLabel; readonly fingerprint: string; readonly verified: VerifiedOperatorRun;
}
const operatorBindings = new WeakMap<AdmittedOperatorRecording, OperatorBinding>();
const operatorViewBindings = new WeakMap<AdmittedRecordedOperators, { recording: AdmittedOperatorRecording; diagnostic: Diagnostic }>();

async function verifyObservation(value: PreviewCase, run: OperatorRecordingFrame, record: Record<string, unknown>, compiler: unknown,
  sourceText: string): Promise<readonly VerifiedOperatorEvaluation[]> {
  if (!run.observation || !run.result || !Array.isArray(record['evaluations']) || record['evaluations'].length > 128 ||
    !integer(record['startedEvaluations'], 0, 128) || !integer(record['completedEvaluations'], 0, 128) ||
    !integer(record['observedNodes'], 0, 1024) || !integer(record['retainedBodyBytes'], 0, 1_048_576)) return fail();
  const state = record['state']; const captureMode = obj(await frame(run.policy, 'coglatas.local-predicate-observation-capture-policy/1'))['mode'];
  const maximumBodyBytes = obj(parse(run.policy.text))['maximumBodyBytes'];
  const resultAxes = axes(obj(value.result));
  // This v1 offline recipe covers completed initial-root Boolean constraints and Requirement conditions, with explicit Manifest/body gaps.
  if (resultAxes['execution'] !== 'Completed' || !['ManifestOnly', 'RecordedInitialRootEvaluations', 'NoActualEvaluations', 'BodyBudgetExceeded'].includes(state as string) ||
    captureMode === 'Manifest' && state !== 'ManifestOnly' || captureMode !== 'Manifest' && state === 'ManifestOnly') return fail();
  const source = obj(parse(sourceText)); const declarations = source['declarations'];
  if (!Array.isArray(declarations)) return fail();
  const originalPayloads = originalJsonArrayValues(sourceText, 'declarations', 'payload');
  const resultTexts = originalJsonArrayValues(run.result.text, 'diagnostics');
  const rootDiagnostics = await Promise.all(resultTexts.map(async text => ({ text, data: obj(parse(text)),
    digest: { algorithm: 'sha-256' as const, value: await digestText('coglatas.compiler-diagnostic/1', text) } })));
  const originals = originalJsonArrayValues(run.observation.text, 'evaluations', 'originalObservation');
  const verified: VerifiedOperatorEvaluation[] = []; let observed = 0; let retained = 0;
  for (const [index, raw] of record['evaluations'].entries()) {
    const metadata = obj(raw); const original = obj(metadata['originalObservation']);
    const requirement = metadata['ruleId'] === 'coglatas.core.requirements';
    if (metadata['ordinal'] !== index || !['coglatas.core.constraints', 'coglatas.core.requirements'].includes(metadata['ruleId'] as string) || metadata['ruleVersion'] !== 1 ||
      !fields(metadata, ['ordinal', 'ruleId', 'ruleVersion', 'location', 'originalObservation', 'originalObservationDigest', 'originalRootDiagnosticLinks', 'publishedDiagnostics']) ||
      !digest(metadata['originalObservationDigest']) || !Array.isArray(metadata['publishedDiagnostics']) ||
      !await verify('coglatas.local-predicate-evaluation-record/1', originals[index], metadata['originalObservationDigest'])) return fail();
    const location = obj(metadata['location']);
    const declarationIndex = declarations.findIndex(v => object(v) && v['entityId'] === location['entityId'] && v['documentId'] === location['documentId']);
    if (declarationIndex < 0 || obj(declarations[declarationIndex])['kind'] !== (requirement ? 'coglatas.requirement' : 'coglatas.constraint')) return fail();
    expect(location, { format: 'coglatas.project-location/1', schemaId: 'coglatas.project-location', schemaVersion: 1, canonicalEncodingVersion: 1,
      context: value.context, documentId: location['documentId'], entityId: location['entityId'], propertyPath: '' });
    const predicateText = originalOperatorMember(originalPayloads[declarationIndex], requirement ? 'condition' : 'predicate');
    if (!digest(original['predicateDigest']) || !await verify('coglatas.predicate/1', predicateText, original['predicateDigest']) ||
      !digest(original['evaluationDigest']) || !Array.isArray(original['nodes']) || !integer(original['observedNodes'], 1, 1024) ||
      original['observedNodes'] !== original['nodes'].length || !integer(original['retainedBodyBytes'], 1, 1_048_576) ||
      !['True', 'False', 'Unknown'].includes(original['truth'] as string)) return fail();
    let bodyBytes = 0; const nodeDigests = new Set<string>();
    for (const [ordinal, item] of original['nodes'].entries()) {
      if (!fields(item, ['ordinal', 'digest', 'bytes'])) return fail(); const node = obj(item);
      if (node['ordinal'] !== ordinal || !digest(node['digest']) || !integer(node['bytes'], 1, 1_048_576) || nodeDigests.has(node['digest'].value)) return fail();
      nodeDigests.add(node['digest'].value); bodyBytes += node['bytes'];
    }
    if (bodyBytes !== original['retainedBodyBytes']) return fail();
    expect(original, { format: 'coglatas.local-predicate-evaluation-record/1', state: 'RecordedOperators', truth: original['truth'], execution: 'Completed',
      capture: { declared: 'BeforeEvaluation', mode: 'ObservedOperators', maximumNodes: 1024 - observed, maximumBodyBytes: 1_048_576 - retained },
      predicateDigest: original['predicateDigest'], evaluationDigest: original['evaluationDigest'], observedNodes: original['observedNodes'], retainedBodyBytes: bodyBytes,
      scope: 'ActualStandaloneBooleanEvaluation', relationshipStructure: 'ObservedBooleanEvaluationTree', regionalEvaluation: 'NotRecorded',
      compilerRunIdentity: 'NotAllocated', runSelection: 'NotEvaluated', causalMeaning: 'NotRecorded', authority: 'NotEvaluated', producerTrust: 'NotEvaluated',
      eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted', nodes: original['nodes'] });
    // Missing evidence is a separate diagnostic; a condition graph cannot explain or satisfy it.
    const conditionCodes = requirement ? ['coglatas.analysis.requirement-condition-false'] : ['coglatas.analysis.constraint-false', 'coglatas.analysis.constraint-unknown'];
    const actualDiagnostics = rootDiagnostics.filter(d => d.data['ruleId'] === metadata['ruleId'] && equal(d.data['primaryLocation'], location) &&
      (!requirement || conditionCodes.includes(d.data['code'] as string)));
    const code = requirement ? conditionCodes[0] : original['truth'] === 'False' ? conditionCodes[0] : conditionCodes[1];
    const emitsConditionDiagnostic = original['truth'] === 'False' || !requirement && original['truth'] === 'Unknown';
    if (emitsConditionDiagnostic ? actualDiagnostics.length !== 1 || actualDiagnostics[0].data['code'] !== code : actualDiagnostics.length !== 0) return fail();
    expect(metadata['originalRootDiagnosticLinks'], actualDiagnostics.length);
    expect(metadata['publishedDiagnostics'], actualDiagnostics.map(d => ({ diagnosticId: d.data['diagnosticId'], digest: d.digest })));
    observed += original['observedNodes']; retained += bodyBytes;
    verified.push({ metadata: freeze(metadata), predicateText, original: freeze(original) });
  }
  if (state === 'RecordedInitialRootEvaluations') {
    if (!verified.length || maximumBodyBytes !== 1_048_576 || record['startedEvaluations'] !== verified.length || record['completedEvaluations'] !== verified.length ||
      record['observedNodes'] !== observed || record['retainedBodyBytes'] !== retained) return fail();
  } else {
    if (verified.length || record['retainedBodyBytes'] !== 0) return fail();
    if (state === 'BodyBudgetExceeded') {
      if (maximumBodyBytes !== 1 || record['startedEvaluations'] === 0 || record['startedEvaluations'] !== record['completedEvaluations'] || record['observedNodes'] === 0) return fail();
    } else if (state === 'NoActualEvaluations') {
      if (record['startedEvaluations'] !== 0 || record['completedEvaluations'] !== 0 || record['observedNodes'] !== 0) return fail();
    } else if (record['completedEvaluations'] !== record['startedEvaluations'] || record['observedNodes'] < record['startedEvaluations'] ||
      (record['startedEvaluations'] === 0) !== (record['observedNodes'] === 0)) return fail();
    if (state === 'NoActualEvaluations' && rootDiagnostics.some(d => equal(obj(d.data['primaryLocation'])['context'], value.context) &&
      ['coglatas.analysis.constraint-false', 'coglatas.analysis.constraint-unknown', 'coglatas.analysis.requirement-condition-false',
        'coglatas.analysis.requirement-evidence-unavailable'].includes(d.data['code'] as string))) return fail();
  }
  expect(record, { format: 'coglatas.local-initial-root-predicate-observation/1', state, scope: 'ActualInitialRootEvaluationOnly',
    regionalEvaluation: 'NotRecorded', compilerRunIdentity: 'NotAllocated', runSelection: 'NotEvaluated', authority: 'NotEvaluated', producerTrust: 'NotEvaluated',
    eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted', causalMeaning: 'NotRecorded', startedEvaluations: record['startedEvaluations'],
    completedEvaluations: record['completedEvaluations'], observedNodes: record['observedNodes'], retainedBodyBytes: record['retainedBodyBytes'],
    capture: { declared: 'BeforeCompile', mode: captureMode, maximumEvaluations: 128, maximumNodes: 1024, maximumBodyBytes },
    basis: { inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, context: value.context, compiler, resultDigest: value.resultDigest },
    result: { ...resultAxes, execution: obj(value.result)['execution'] }, evaluations: record['evaluations'] });
  return freeze(verified);
}
async function digestText(domain: string, text: string): Promise<string> {
  const tag = new TextEncoder().encode(domain + String.fromCharCode(0)); const body = new TextEncoder().encode(text);
  const data = new Uint8Array(tag.length + body.length); data.set(tag); data.set(body, tag.length);
  const hash = new Uint8Array(await crypto.subtle.digest('SHA-256', data));
  return [...hash].map(value => value.toString(16).padStart(2, '0')).join('');
}

async function bodyMap(values: readonly OriginalOperatorBody[], domain: string, maximum: number): Promise<ReadonlyMap<string, OriginalOperatorBody>> {
  if (!Array.isArray(values) || values.length > maximum) return fail(); const map = new Map<string, OriginalOperatorBody>(); let previous = '';
  for (const value of values) {
    if (!fields(value, ['text', 'digest', 'bytes']) || typeof value.text !== 'string' || !digest(value.digest) ||
      !integer(value.bytes, 1, 1_048_576) || value.bytes !== bytes(value.text) || value.digest.value <= previous) return fail();
    await frame({ text: value.text, digest: value.digest }, domain); previous = value.digest.value;
    map.set(value.digest.value, freeze(value));
  }
  return map;
}
async function verifyOperatorSnapshots(value: PreviewCase, run: OperatorRecordingFrame, record: Record<string, unknown>,
  evaluations: readonly VerifiedOperatorEvaluation[]): Promise<readonly VerifiedOperatorDiagnostic[]> {
  if (!run.result || !run.observation) return fail();
  const resultTexts = originalJsonArrayValues(run.result.text, 'diagnostics').filter(text => equal(obj(obj(parse(text))['primaryLocation'])['context'], value.context));
  if (run.diagnostics.length !== resultTexts.length || resultTexts.length > 20_000) return fail();
  const verified: VerifiedOperatorDiagnostic[] = [];
  for (const [index, text] of resultTexts.entries()) {
    const entry = run.diagnostics[index]; if (!fields(entry, ['diagnostic', 'manifest', 'available', 'budget']) || entry.diagnostic.text !== text) return fail();
    const diagnostic = await frame(entry.diagnostic, 'coglatas.compiler-diagnostic/1', null, false);
    const selected = evaluations.filter(e => (e.metadata['publishedDiagnostics'] as readonly Record<string, unknown>[])
      .some(d => d['diagnosticId'] === diagnostic['diagnosticId'] && equal(d['digest'], entry.diagnostic.digest)));
    if (!fields(entry.available, ['frame', 'operatorBodies', 'evaluationFrames'])) return fail();
    const availableBodies = await bodyMap(entry.available.operatorBodies, 'coglatas.observed-predicate-node/1', 1024);
    const availableEvaluations = await bodyMap(entry.available.evaluationFrames, 'coglatas.local-predicate-evaluation/1', 128);
    const nodeMetadata = new Map<string, DeclaredOperatorNode>(); const evaluationMetadata = new Map<string, Record<string, unknown>>();
    for (const e of selected) {
      for (const n of e.original['nodes'] as readonly DeclaredOperatorNode[]) {
        if (nodeMetadata.has(n.digest.value) && !equal(nodeMetadata.get(n.digest.value), n)) return fail();
        nodeMetadata.set(n.digest.value, n);
      }
      const ef = e.original['evaluationDigest'] as Digest; evaluationMetadata.set(ef.value, e.original);
    }
    expect([...availableBodies.keys()], [...nodeMetadata.keys()].sort());
    expect([...availableEvaluations.keys()], [...evaluationMetadata.keys()].sort());
    const actualGraphs: AdmittedOperatorEvaluation[] = [];
    for (const e of selected) {
      const declared = e.original['nodes'] as readonly DeclaredOperatorNode[];
      const evaluationFrame = availableEvaluations.get((e.original['evaluationDigest'] as Digest).value); if (!evaluationFrame) return fail();
      const originals = declared.map(n => availableBodies.get(n.digest.value) ?? fail());
      const nodes = await validateOriginalOperatorGraph(e.predicateText, declared, originals, evaluationFrame);
      if (obj(parse(evaluationFrame.text))['truth'] !== e.original['truth']) return fail();
      actualGraphs.push(freeze({ ordinal: e.metadata['ordinal'] as number, ruleId: e.metadata['ruleId'] as string,
        ruleVersion: e.metadata['ruleVersion'] as number, nodes, evaluationFrame, metadata: e.metadata }));
    }
    const totalBodies = [...availableBodies.values()].reduce((n, b) => n + b.bytes, 0);
    const totalEvaluations = [...availableEvaluations.values()].reduce((n, b) => n + b.bytes, 0);
    if (totalBodies > 1_048_576) return fail();
    const snapshots = {} as Record<RecordedOperatorChoice, VerifiedOperatorSnapshot>;
    for (const choice of choices) {
      const view = entry[choice]; if (!fields(view, ['frame', 'operatorBodies', 'evaluationFrames'])) return fail();
      const snapshot = await frame(view.frame, 'coglatas.local-initial-root-predicate-observation-snapshot/1');
      const mode = choice === 'manifest' ? 'Manifest' : 'AvailableOperators'; const maximumBodyBytes = choice === 'budget' ? 1 : 1_048_576;
      const captured = record['state'] === 'RecordedInitialRootEvaluations' || record['state'] === 'NoActualEvaluations';
      const state = mode === 'Manifest' ? 'ManifestOnly' : !captured ? 'NoRecordedOperators' : !selected.length ? 'NoObservedPublishedDiagnostic'
        : totalBodies > maximumBodyBytes ? 'BodyBudgetExceeded' : 'AvailableOperators';
      const available = state === 'AvailableOperators';
      expect(view.operatorBodies, available ? entry.available.operatorBodies : []);
      expect(view.evaluationFrames, available ? entry.available.evaluationFrames : []);
      expect(snapshot, { format: 'coglatas.local-initial-root-predicate-observation-snapshot/1', state, mode, maximumBodyBytes,
        retainedBodyBytes: available ? totalBodies : 0, retainedEvaluationFrameBytes: available ? totalEvaluations : 0, selectedEvaluations: selected.length,
        scope: 'OriginalInitialRootPredicateFrames', regionalEvaluation: 'NotRecorded', compilerRunIdentity: 'NotAllocated', runSelection: 'NotEvaluated',
        causalMeaning: 'NotRecorded', authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted',
        selection: !captured ? 'NotRecorded' : selected.length ? 'ObservedInitialRootEvaluations' : 'NoObservedPublishedDiagnostic',
        originalRecord: { digest: run.observation.digest, metadata: record }, diagnostic: { diagnosticId: diagnostic['diagnosticId'], digest: entry.diagnostic.digest },
        evaluations: selected.map(e => ({ ordinal: e.metadata['ordinal'], metadata: e.metadata,
          evaluationFrame: { digest: e.original['evaluationDigest'], bytes: availableEvaluations.get((e.original['evaluationDigest'] as Digest).value)!.bytes,
            bodyCaptured: available },
          nodes: (e.original['nodes'] as readonly DeclaredOperatorNode[]).map(n => ({ ...n, bodyCaptured: available })) })) });
      snapshots[choice] = { snapshot: freeze(view), data: freeze(snapshot), evaluations: freeze(available ? actualGraphs : []) };
    }
    verified.push({ data: freeze(diagnostic), snapshots: freeze(snapshots) });
  }
  return freeze(verified);
}

async function verifyOperatorRun(value: PreviewCase, run: OperatorRecordingFrame, label: OperatorRecordingLabel, compiler: unknown,
  sourceText: string): Promise<VerifiedOperatorRun> {
  if (!fields(run, ['request', 'policy', 'baseExecution', 'materializations', 'manifest', 'response', 'receipt', 'result', 'observation', 'diagnostics']) ||
    !Array.isArray(run.diagnostics) || run.diagnostics.length > 20_000) return fail();
  const [request, policy, base, materializations, manifest, response, receipt] = await Promise.all([
    frame(run.request, 'coglatas.local-predicate-observation-compiler-host-input/1'), frame(run.policy, 'coglatas.local-predicate-observation-capture-policy/1'),
    frame(run.baseExecution, 'coglatas.compiler-host-input/1', null), frame(run.materializations, 'coglatas.host-materializations/1'),
    frame(run.manifest, 'coglatas.local-predicate-observation-compiler-recording-manifest/1'),
    frame(run.response, 'coglatas.local-predicate-observation-compiler-host-response/1'), frame(run.receipt, 'coglatas.local-predicate-observation-compiler-recording-receipt/1')
  ]);
  const mode = label === 'original' ? 'Manifest' : 'ObservedOperators'; const maximumBodyBytes = label === 'budget' ? 1 : 1_048_576;
  expect(policy, { format: 'coglatas.local-predicate-observation-capture-policy/1', mode, maximumBodyBytes, maximumEvaluations: 128,
    maximumNodes: 1024, scope: 'ActualInitialRootEvaluationOnly' });
  expect(materializations, { format: 'coglatas.host-materializations/1', snapshots: [], unitDefinitions: [], calendarDefinitions: [] });
  expect(base, { declaredInputDigest: value.inputDigest, materializationsDigest: run.materializations.digest });
  expect(request, { format: 'coglatas.local-predicate-observation-compiler-host-input/1', baseExecutionInputDigest: run.baseExecution.digest,
    declaredInputDigest: value.inputDigest, materializationsDigest: run.materializations.digest, observationPolicy: policy, observationPolicyDigest: run.policy.digest });
  if (!guid(manifest['sessionId']) || !guid(manifest['runId']) || !guid(response['attemptId']) ||
    !integer(manifest['clientGeneration'], 0, Number.MAX_SAFE_INTEGER) || !Array.isArray(manifest['knownLineage']) ||
    manifest['knownLineage'].length > 31 || !manifest['knownLineage'].every(guid) || new Set(manifest['knownLineage']).size !== manifest['knownLineage'].length ||
    manifest['knownLineage'].includes(manifest['runId'])) return fail();
  const origin = manifest['origin'];
  if (origin !== null && (!fields(origin, ['runId', 'manifestDigest', 'receiptDigest', 'attemptId']) || !guid(obj(origin)['runId']) || !guid(obj(origin)['attemptId']) ||
    !digest(obj(origin)['manifestDigest']) || !digest(obj(origin)['receiptDigest']))) return fail();
  expect(manifest, { format: 'coglatas.local-predicate-observation-compiler-recording-manifest/1', sessionId: manifest['sessionId'], runId: manifest['runId'],
    executionInputDigest: run.request.digest, baseExecutionInputDigest: run.baseExecution.digest, inputDigest: value.inputDigest, sourceDigest: value.sourceDigest,
    context: value.context, compiler, materializationsDigest: run.materializations.digest, clientGeneration: manifest['clientGeneration'],
    capture: { declared: 'BeforeExecution', policy, policyDigest: run.policy.digest }, origin, knownLineage: manifest['knownLineage'],
    identityScope: 'OwnedLocalSessionOnly', authority: 'NotEvaluated', retentionPolicy: 'NotAdopted' });
  const cancelled = label === 'cancelled'; const status = cancelled ? 'Cancelled' : 'Completed';
  const reason = cancelled ? 'coglatas.predicate-observation-host.cancelled' : 'coglatas.predicate-observation-host.result-produced';
  let record: Record<string, unknown> | null = null; let diagnostics: readonly VerifiedOperatorDiagnostic[] = [];
  if (cancelled) {
    if (run.result !== null || run.observation !== null || run.diagnostics.length) return fail();
  } else {
    if (!run.result || !run.observation || run.result.text !== value.rootSource?.result.text || !equal(run.result.digest, value.resultDigest)) return fail();
    expect(await frame(run.result, 'coglatas.compiler-result/1', 'coglatas.compiler-result/1', false), value.result);
    record = await frame(run.observation, 'coglatas.local-initial-root-predicate-observation/1');
    const evaluations = await verifyObservation(value, run, record, compiler, sourceText);
    diagnostics = await verifyOperatorSnapshots(value, run, record, evaluations);
  }
  const resultAxes = axes(obj(value.result));
  expect(response, { format: 'coglatas.local-predicate-observation-compiler-host-response/1', runId: manifest['runId'], manifestDigest: run.manifest.digest,
    executionInputDigest: run.request.digest, baseExecutionInputDigest: run.baseExecution.digest, materializationsDigest: run.materializations.digest,
    observationPolicyDigest: run.policy.digest, clientGeneration: manifest['clientGeneration'], attemptId: response['attemptId'], hostStatus: status, reasonCode: reason,
    resultDigest: cancelled ? null : run.result!.digest, observationDigest: cancelled ? null : run.observation!.digest,
    compilerExecution: cancelled ? null : resultAxes['execution'], captureState: record ? record['state'] : null,
    identityScope: 'OwnedLocalDeclaredInvocationOnly', regionalEvaluation: 'NotRecorded', authority: 'NotEvaluated',
    producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted' });
  expect(receipt, { format: 'coglatas.local-predicate-observation-compiler-recording-receipt/1', runId: manifest['runId'], manifestDigest: run.manifest.digest,
    executionInputDigest: run.request.digest, observationPolicyDigest: run.policy.digest, attemptId: response['attemptId'], hostStatus: status, reasonCode: reason,
    hostResponseDigest: run.response.digest, resultDigest: cancelled ? null : run.result!.digest, observationRecordDigest: cancelled ? null : run.observation!.digest,
    recordingState: cancelled ? 'HostOutcomeWithoutCompilerResult' : 'OriginalInitialRootObservation', captureState: record ? record['state'] : null,
    identityScope: 'OwnedLocalSessionAndHostAttempt', earlierIntermediateStates: 'NotRecorded', regionalEvaluation: 'NotRecorded', authority: 'NotEvaluated',
    producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted' });
  return { frame: freeze(run), manifest: freeze(manifest), response: freeze(response), receipt: freeze(receipt), policy: freeze(policy),
    record: record ? freeze(record) : null, diagnostics };
}

/** Structural run-identity uniqueness across this offline fixture; it grants no producer or runtime authority. */
export function operatorRunIdentitiesAreUnique(cases: readonly PreviewCase[]): boolean {
  try {
    const runs = new Set<string>(); const attempts = new Set<string>(); const sessions = new Set<string>();
    for (const value of cases) {
      const projection = value.operatorRecordings; if (projection == null) continue;
      if (!fields(projection, ['format', 'source', 'input', ...labels]) || projection.format !== 'coglatas.local-web-operator-recording-fixture/1') return false;
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
export function operatorRecordingMatches(value: PreviewCase, sources: AdmittedRootSource, admitted: AdmittedOperatorRecording): boolean {
  try {
    const binding = operatorBindings.get(admitted); const projection = value.operatorRecordings;
    return !!binding && binding.value === value && binding.sources === sources && admitted.sources === sources && rootSourceMatches(value, sources) &&
      projection === binding.projection && projection?.[binding.label] === binding.member && bounded(projection) && JSON.stringify(projection) === binding.fingerprint;
  } catch { return false; }
}

/** Verify every original run and snapshot mode before admitting one member; no Compiler or record deserializer is installed. */
export async function readOperatorRecording(value: PreviewCase, sources: AdmittedRootSource,
  label: OperatorRecordingLabel = 'original'): Promise<AdmittedOperatorRecording | null> {
  if (!labels.includes(label) || !rootSourceMatches(value, sources)) return fail();
  const projection = value.operatorRecordings; if (projection == null) return null;
  if (!fields(projection, ['format', 'source', 'input', ...labels]) || projection.format !== 'coglatas.local-web-operator-recording-fixture/1' || !bounded(projection)) return fail();
  const fingerprint = JSON.stringify(projection); const member = projection[label]; const memberRefs = labels.map(name => projection[name]);
  if (new Set(memberRefs).size !== labels.length) return fail(); const copy = freeze(structuredClone(projection));
  expect(await frame(copy.source, 'coglatas.project-source/1', 'coglatas.project-source/1', false), sources.source);
  const input = await frame(copy.input, 'coglatas.compiler-input/1', 'coglatas.compiler-input/1', false);
  if (copy.source.text !== sources.sourceText || copy.input.text !== sources.projection.inputText || !equal(copy.source.digest, value.sourceDigest) ||
    !equal(copy.input.digest, value.inputDigest)) return fail();
  const compiler = obj(input['settings'])['compiler']; const verified = new Map<OperatorRecordingLabel, VerifiedOperatorRun>();
  for (const name of labels) verified.set(name, await verifyOperatorRun(value, copy[name], name, compiler, copy.source.text));
  const original = verified.get('original')!; const diagnostic = verified.get('diagnostic')!; const cancelled = verified.get('cancelled')!; const retry = verified.get('retry')!;
  const runs = new Set([...verified.values()].map(run => run.manifest['runId'])); const attempts = new Set([...verified.values()].map(run => run.response['attemptId']));
  if (runs.size !== 5 || attempts.size !== 5 || [...runs].some(id => attempts.has(id))) return fail();
  for (const [name, run] of verified) {
    expect(run.manifest['sessionId'], original.manifest['sessionId']); expect(run.manifest['clientGeneration'], original.manifest['clientGeneration']);
    expect(run.frame.baseExecution, original.frame.baseExecution); expect(run.frame.materializations, original.frame.materializations);
    const ancestor = name === 'diagnostic' ? original : name === 'budget' ? diagnostic : name === 'retry' ? cancelled : null;
    expect(run.manifest['origin'], ancestor === null ? null : { runId: ancestor.manifest['runId'], manifestDigest: ancestor.frame.manifest.digest,
      receiptDigest: ancestor.frame.receipt.digest, attemptId: ancestor.response['attemptId'] });
    expect(run.manifest['knownLineage'], ancestor === null ? [] : [...ancestor.manifest['knownLineage'] as string[], ancestor.manifest['runId']]);
  }
  for (const name of ['original', 'budget', 'retry'] as const)
    for (const counter of ['startedEvaluations', 'completedEvaluations', 'observedNodes'])
      expect(verified.get(name)!.record![counter], diagnostic.record![counter]);
  expect(retry.frame.observation, diagnostic.frame.observation);
  expect(verified.get('budget')!.record!['state'], diagnostic.record!['state'] === 'NoActualEvaluations' ? 'NoActualEvaluations' : 'BodyBudgetExceeded');
  if (!rootSourceMatches(value, sources) || value.operatorRecordings !== projection || JSON.stringify(projection) !== fingerprint ||
    labels.some((name, index) => projection[name] !== memberRefs[index])) return fail();
  const selected = verified.get(label)!; const manifest = selected.manifest; const response = selected.response;
  const admitted: AdmittedOperatorRecording = Object.freeze({ sources, label, runId: manifest['runId'] as string, sessionId: manifest['sessionId'] as string,
    attemptId: response['attemptId'] as string, originRunId: manifest['origin'] === null ? null : obj(manifest['origin'])['runId'] as string,
    hostStatus: response['hostStatus'] as 'Completed' | 'Cancelled', captureMode: selected.policy['mode'] as 'Manifest' | 'ObservedOperators',
    originalCaptureState: selected.record ? selected.record['state'] as string : null,
    startedEvaluations: selected.record ? selected.record['startedEvaluations'] as number : null,
    completedEvaluations: selected.record ? selected.record['completedEvaluations'] as number : null,
    observedNodes: selected.record ? selected.record['observedNodes'] as number : null,
    retainedBodyBytes: selected.record ? selected.record['retainedBodyBytes'] as number : null, maximumBodyBytes: selected.policy['maximumBodyBytes'] as number,
    maximumNodes: selected.policy['maximumNodes'] as number, maximumEvaluations: selected.policy['maximumEvaluations'] as number,
    manifest: selected.frame.manifest, response: selected.frame.response, receipt: selected.frame.receipt, observation: selected.frame.observation,
    manifestData: selected.manifest, responseData: selected.response, receiptData: selected.receipt, observationData: selected.record });
  operatorBindings.set(admitted, { value, sources, projection, member, label, fingerprint, verified: selected }); return admitted;
}

export function recordedOperatorsMatch(value: PreviewCase, sources: AdmittedRootSource, recording: AdmittedOperatorRecording,
  diagnostic: Diagnostic, admitted: AdmittedRecordedOperators): boolean {
  try {
    const binding = operatorViewBindings.get(admitted);
    return !!binding && binding.recording === recording && binding.diagnostic === diagnostic && value.result.diagnostics.includes(diagnostic) &&
      equal(diagnostic.primaryLocation.context, value.context) && operatorRecordingMatches(value, sources, recording);
  } catch { return false; }
}
/** Return only the selected actual diagnostic's original frames; metadata and capture/viewer gaps stay distinct. */
export async function readRecordedOperators(value: PreviewCase, sources: AdmittedRootSource, recording: AdmittedOperatorRecording,
  diagnostic: Diagnostic, choice: RecordedOperatorChoice = 'manifest'): Promise<AdmittedRecordedOperators | null> {
  if (!choices.includes(choice) || !operatorRecordingMatches(value, sources, recording) || !value.result.diagnostics.includes(diagnostic) ||
    !equal(diagnostic.primaryLocation.context, value.context)) return fail();
  const binding = operatorBindings.get(recording)!; if (recording.hostStatus !== 'Completed' || !binding.verified.record) return null;
  const index = value.result.diagnostics.filter(d => equal(d.primaryLocation.context, value.context)).indexOf(diagnostic);
  const selected = binding.verified.diagnostics[index]; if (!selected || !equal(selected.data, diagnostic)) return fail();
  const view = selected.snapshots[choice]; const data = view.data;
  const admitted: AdmittedRecordedOperators = Object.freeze({ text: view.snapshot.frame.text, digest: view.snapshot.frame.digest, recording, diagnostic, choice,
    state: data['state'] as string, mode: data['mode'] as 'Manifest' | 'AvailableOperators', maximumBodyBytes: data['maximumBodyBytes'] as number,
    retainedBodyBytes: data['retainedBodyBytes'] as number, retainedEvaluationFrameBytes: data['retainedEvaluationFrameBytes'] as number,
    originalCaptureState: binding.verified.record['state'] as string, evaluations: view.evaluations, data });
  if (!operatorRecordingMatches(value, sources, recording)) return fail(); operatorViewBindings.set(admitted, { recording, diagnostic }); return admitted;
}
