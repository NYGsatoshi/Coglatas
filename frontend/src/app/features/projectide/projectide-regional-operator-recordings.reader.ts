import type { Diagnostic, Digest, PreviewCase, RevisionContext } from './projectide.contracts';
import { sameContext } from './projectide.contracts';
import { isProjectLocationShape, isRevisionContextShape } from './projectide-preview.validation';
import { rootSourceMatches, type AdmittedRootSource } from './projectide-root-source.reader';
import { scopedSourcesMatch, type AdmittedScopedSources } from './projectide-scoped-sources';
import { originalJsonArrayValues } from './projectide-original-json';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify, readSnapshotMaterialization } from './projectide-tests.reader';
import { originalOperatorMember, validateOriginalOperatorGraph, type DeclaredOperatorNode, type OperatorTextFrame,
  type OriginalOperatorBody, type OriginalOperatorNode } from './projectide-operator-recording-structure';
import type { OperatorDiagnosticFrames, OperatorSnapshotFrames, OperatorRecordingLabel, RecordedOperatorChoice } from './projectide-operator-recordings.reader';

export interface RegionalOperatorRegionFrames {
  readonly binding: OperatorTextFrame; readonly source: OperatorTextFrame; readonly region: OperatorTextFrame;
  readonly diagnostics: readonly OperatorDiagnosticFrames[];
}
export interface RegionalOperatorRecordingFrame {
  readonly request: OperatorTextFrame; readonly policy: OperatorTextFrame; readonly baseExecution: OperatorTextFrame;
  readonly materializations: OperatorTextFrame; readonly manifest: OperatorTextFrame; readonly response: OperatorTextFrame; readonly receipt: OperatorTextFrame;
  readonly result: OperatorTextFrame | null; readonly observation: OperatorTextFrame | null;
  readonly dependencyAnalysis: OperatorTextFrame | null; readonly bindingClosure: OperatorTextFrame | null;
  readonly regions: readonly RegionalOperatorRegionFrames[];
}
export interface RegionalOperatorRecordingProjection {
  readonly format: 'coglatas.local-web-regional-operator-recording-fixture/1'; readonly source: OperatorTextFrame; readonly input: OperatorTextFrame;
  readonly original: RegionalOperatorRecordingFrame; readonly diagnostic: RegionalOperatorRecordingFrame; readonly budget: RegionalOperatorRecordingFrame;
  readonly cancelled: RegionalOperatorRecordingFrame; readonly retry: RegionalOperatorRecordingFrame;
}
export type RegionalOperatorSources = AdmittedRootSource | AdmittedScopedSources;
export interface AdmittedRegionalOperatorRegion {
  readonly publishedOrdinal: number; readonly callOrdinal: number | null; readonly isRoot: boolean; readonly context: RevisionContext; readonly bindingDigest: Digest; readonly publishedDigest: Digest;
  readonly diagnostics: readonly Diagnostic[]; readonly metadata: Readonly<Record<string, unknown>> | null;
}
export interface AdmittedRegionalOperatorRecording {
  readonly sources: RegionalOperatorSources; readonly label: OperatorRecordingLabel; readonly runId: string; readonly sessionId: string; readonly attemptId: string;
  readonly originRunId: string | null; readonly hostStatus: 'Completed' | 'Cancelled'; readonly captureMode: 'Manifest' | 'ObservedOperators';
  readonly maximumBodyBytes: number; readonly maximumNodes: number; readonly maximumEvaluations: number;
  readonly originalCaptureState: string | null; readonly completedRegionCalls: number | null; readonly startedEvaluations: number | null;
  readonly completedEvaluations: number | null; readonly observedNodes: number | null; readonly retainedBodyBytes: number | null;
  readonly manifest: OperatorTextFrame; readonly response: OperatorTextFrame; readonly receipt: OperatorTextFrame; readonly observation: OperatorTextFrame | null;
  readonly manifestData: Readonly<Record<string, unknown>>; readonly responseData: Readonly<Record<string, unknown>>;
  readonly receiptData: Readonly<Record<string, unknown>>; readonly observationData: Readonly<Record<string, unknown>> | null;
  readonly regions: readonly AdmittedRegionalOperatorRegion[];
}
export interface AdmittedRegionalOperatorEvaluation {
  readonly ordinal: number; readonly ruleId: string; readonly ruleVersion: number; readonly metadata: Readonly<Record<string, unknown>>;
  readonly nodes: readonly OriginalOperatorNode[]; readonly evaluationFrame: OriginalOperatorBody;
}
export interface AdmittedRecordedRegionalOperators extends OperatorTextFrame {
  readonly recording: AdmittedRegionalOperatorRecording; readonly region: AdmittedRegionalOperatorRegion; readonly diagnostic: Diagnostic;
  readonly choice: RecordedOperatorChoice; readonly state: string; readonly mode: 'Manifest' | 'AvailableOperators'; readonly maximumBodyBytes: number;
  readonly originalCaptureState: string; readonly retainedBodyBytes: number; readonly retainedEvaluationFrameBytes: number;
  readonly evaluations: readonly AdmittedRegionalOperatorEvaluation[]; readonly data: Readonly<Record<string, unknown>>;
}
const labels = ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const;
const choices = ['manifest', 'available', 'budget'] as const;
const fail = (): never => { throw new Error('unsupported-regional-operator-recording'); };
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


interface VerifiedEvaluation { readonly metadata: Readonly<Record<string, unknown>>; readonly original: Readonly<Record<string, unknown>>; readonly predicateText: string; }
interface VerifiedSnapshot { readonly snapshot: OperatorSnapshotFrames; readonly data: Readonly<Record<string, unknown>>; readonly evaluations: readonly AdmittedRegionalOperatorEvaluation[]; }
interface VerifiedDiagnostic { readonly data: Diagnostic; readonly snapshots: Readonly<Record<RecordedOperatorChoice, VerifiedSnapshot>>; }
interface VerifiedRegion { readonly frame: RegionalOperatorRegionFrames; readonly binding: Readonly<Record<string, unknown>>; readonly source: Readonly<Record<string, unknown>>;
  readonly data: Readonly<Record<string, unknown>>; readonly diagnostics: readonly VerifiedDiagnostic[]; readonly metadata: Readonly<Record<string, unknown>> | null; }
interface VerifiedRun { readonly frame: RegionalOperatorRecordingFrame; readonly manifest: Readonly<Record<string, unknown>>; readonly response: Readonly<Record<string, unknown>>;
  readonly receipt: Readonly<Record<string, unknown>>; readonly policy: Readonly<Record<string, unknown>>; readonly record: Readonly<Record<string, unknown>> | null;
  readonly regions: readonly VerifiedRegion[]; }
interface RecordingBinding { readonly value: PreviewCase; readonly sources: RegionalOperatorSources; readonly projection: RegionalOperatorRecordingProjection;
  readonly member: RegionalOperatorRecordingFrame; readonly label: OperatorRecordingLabel; readonly fingerprint: string; readonly verified: VerifiedRun; }
const recordingBindings = new WeakMap<AdmittedRegionalOperatorRecording, RecordingBinding>();
const regionBindings = new WeakMap<AdmittedRegionalOperatorRegion, { recording: AdmittedRegionalOperatorRecording; verified: VerifiedRegion }>();
const viewBindings = new WeakMap<AdmittedRecordedRegionalOperators, { recording: AdmittedRegionalOperatorRecording; region: AdmittedRegionalOperatorRegion; diagnostic: Diagnostic }>();
const regionalFormat = 'coglatas.local-regional-predicate-observation/1';
const snapshotFormat = 'coglatas.local-regional-predicate-observation-snapshot/1';
const hostPrefix = 'coglatas.local-regional-predicate-observation-';
const list = (v: unknown, maximum: number): unknown[] => Array.isArray(v) && v.length <= maximum ? v : fail();
const canonical = (v: unknown): string => Array.isArray(v) ? '[' + v.map(canonical).join(',') + ']' : object(v)
  ? '{' + Object.keys(v).sort().map(k => JSON.stringify(k) + ':' + canonical(v[k])).join(',') + '}' : JSON.stringify(v);
export function regionalOperatorSourcesMatch(value: PreviewCase, sources: RegionalOperatorSources): boolean {
  try { return 'kind' in sources ? rootSourceMatches(value, sources as AdmittedRootSource) : scopedSourcesMatch(value, sources as AdmittedScopedSources); } catch { return false; }
}
function capturedMaterializations(value: PreviewCase): unknown {
  const snapshots = value.materializedSnapshots == null ? [] : list(obj(value.materializedSnapshots)['entries'], 32).map(item => {
    const e = obj(item); const source = obj(e['source']);
    if (e['admission'] !== 'Available' || typeof e['version'] !== 'string' || e['version'].length > 128 || !digest(source['digest']) ||
      typeof e['declarationsComplete'] !== 'boolean' || typeof e['relationsComplete'] !== 'boolean') return fail();
    return { snapshotId: e['snapshotId'], version: e['version'], sourceDigest: source['digest'], authorized: true,
      declarationsComplete: e['declarationsComplete'], relationsComplete: e['relationsComplete'] };
  }).sort((a, b) => canonical(a) < canonical(b) ? -1 : canonical(a) > canonical(b) ? 1 : 0);
  return { format: 'coglatas.host-materializations/1', snapshots, unitDefinitions: [], calendarDefinitions: [] };
}

interface RawRegion { readonly frame: RegionalOperatorRegionFrames; readonly binding: Readonly<Record<string, unknown>>;
  readonly source: Readonly<Record<string, unknown>>; readonly data: Readonly<Record<string, unknown>>;
  readonly diagnosticData: readonly { readonly text: string; readonly digest: Digest; readonly data: Diagnostic }[]; }
async function verifyRegions(value: PreviewCase, run: RegionalOperatorRecordingFrame): Promise<readonly RawRegion[]> {
  if (!run.dependencyAnalysis || !run.bindingClosure || !Array.isArray(run.regions) || run.regions.length > 33) return fail();
  const analysis = await frame(run.dependencyAnalysis, 'coglatas.compiler-dependency-analysis/1');
  const closure = await frame(run.bindingClosure, 'coglatas.semantic-binding-closure/1');
  if (!equal(analysis['rootInputDigest'], value.inputDigest) || !equal(closure['inputDigest'], value.inputDigest) ||
    !equal(analysis['bindingClosureDigest'], run.bindingClosure.digest)) return fail();
  const declared = list(analysis['regions'], 33).map(obj); const bindings = list(closure['regions'], 33).map(obj);
  if (declared.length !== run.regions.length) return fail(); const found = new Set<string>(); const result: RawRegion[] = [];
  for (const [index, item] of run.regions.entries()) {
    if (!fields(item, ['binding', 'source', 'region', 'diagnostics']) || !Array.isArray(item.diagnostics) || item.diagnostics.length > 4096) return fail();
    const binding = await frame(item.binding, 'coglatas.semantic-binding-region/1');
    const source = await frame(item.source, 'coglatas.project-source/1', 'coglatas.project-source/1', false);
    const region = await frame(item.region, 'coglatas.compiler-dependency-region/1', 'coglatas.compiler-dependency-region/1', false);
    if (!digest(item.binding.digest) || found.has(item.binding.digest.value) || !equal(declared[index], { digest: item.region.digest, region }) ||
      bindings.filter(v => equal(v, { digest: item.binding.digest, region: binding })).length !== 1 ||
      !equal(binding['rootInputDigest'], value.inputDigest) || !equal(region['rootInputDigest'], value.inputDigest) ||
      !equal(region['bindingRegionDigest'], item.binding.digest) || !equal(binding['sourceDigest'], item.source.digest) ||
      !equal(region['sourceDigest'], item.source.digest) || !isRevisionContextShape(source['context']) ||
      !equal(source['context'], binding['context']) || !equal(source['context'], region['context']) || typeof binding['isRoot'] !== 'boolean') return fail();
    found.add(item.binding.digest.value);
    if (binding['isRoot']) { if (item.source.text !== value.sourceText || !equal(item.source.digest, value.sourceDigest) || !equal(source['context'], value.context)) return fail(); }
    else {
      const candidates = value.materializedSnapshots == null ? [] : list(obj(value.materializedSnapshots)['entries'], 32).map(obj).filter(e =>
        e['admission'] === 'Available' && equal(e['context'], source['context']) && obj(e['source'])['text'] === item.source.text &&
        equal(obj(e['source'])['digest'], item.source.digest));
      if (candidates.length !== 1) return fail();
    }
    const diagnosticTexts = originalJsonArrayValues(item.region.text, 'diagnostics'); const identities = new Set<string>();
    if (diagnosticTexts.length !== item.diagnostics.length) return fail();
    const diagnostics: { text: string; digest: Digest; data: Diagnostic }[] = [];
    for (const [i, original] of diagnosticTexts.entries()) {
      const projected = item.diagnostics[i]; if (!fields(projected, ['diagnostic', 'manifest', 'available', 'budget']) || projected.diagnostic.text !== original) return fail();
      const d = await frame(projected.diagnostic, 'coglatas.compiler-diagnostic/1', null, false);
      if (typeof d['diagnosticId'] !== 'string' || identities.has(d['diagnosticId']) || typeof d['ruleId'] !== 'string' ||
        !integer(d['ruleVersion'], 0, Number.MAX_SAFE_INTEGER) || typeof d['code'] !== 'string' || !isProjectLocationShape(d['primaryLocation']) ||
        !equal(d['primaryLocation'].context, source['context'])) return fail();
      identities.add(d['diagnosticId']); diagnostics.push({ text: original, digest: projected.diagnostic.digest, data: d as unknown as Diagnostic });
    }
    result.push({ frame: item, binding: freeze(binding), source: freeze(source), data: freeze(region), diagnosticData: freeze(diagnostics) });
  }
  return freeze(result);
}
async function verifyRegionalObservation(value: PreviewCase, run: RegionalOperatorRecordingFrame, record: Record<string, unknown>, policy: Record<string, unknown>,
  regions: readonly RawRegion[]): Promise<readonly VerifiedEvaluation[]> {
  const state = record['state'];
  if (!run.observation || !fields(record, ['format', 'state', 'scope', 'initialRootEvaluation', 'rootSummaryDiagnosticLinks', 'compilerRunIdentity', 'runSelection',
    'causalMeaning', 'authority', 'producerTrust', 'eligibility', 'retentionPolicy', 'completedRegionCalls', 'startedEvaluations', 'completedEvaluations', 'observedNodes',
    'retainedBodyBytes', 'capture', 'basis', 'result', 'regions', 'evaluations']) || !integer(record['completedRegionCalls'], 0, 33) ||
    !integer(record['startedEvaluations'], 0, Number.MAX_SAFE_INTEGER) || !integer(record['completedEvaluations'], 0, record['startedEvaluations'] as number) ||
    !integer(record['observedNodes'], 0, Number.MAX_SAFE_INTEGER) || !integer(record['retainedBodyBytes'], 0, 1_048_576) ||
    !['ManifestOnly', 'RecordedRegionalEvaluations', 'NoActualRegionalEvaluations', 'BodyBudgetExceeded', 'NodeLimitReached', 'EvaluationLimitReached',
      'CaptureInterrupted', 'MetadataBudgetExceeded'].includes(state as string) ||
    policy['mode'] === 'Manifest' && !['ManifestOnly', 'CaptureInterrupted', 'MetadataBudgetExceeded'].includes(state as string) ||
    policy['mode'] !== 'Manifest' && state === 'ManifestOnly') return fail();
  const calls = list(record['regions'], 33).map(obj); const entries = list(record['evaluations'], 128).map(obj);
  if (state !== 'MetadataBudgetExceeded' && calls.length !== record['completedRegionCalls']) return fail();
  if (state === 'MetadataBudgetExceeded' && calls.length !== 0) return fail();
  let end = 0;
  for (const [index, call] of calls.entries()) {
    if (!fields(call, ['ordinal', 'bindingDigest', 'context', 'sourceDigest', 'derivedBindingInputDigest', 'regionResultDigest', 'regionalResult',
      'publishedRegionDigest', 'firstEvaluationOrdinal', 'startedEvaluationExclusiveEnd', 'retainedRegion']) || call['ordinal'] !== index ||
      !digest(call['bindingDigest']) || !digest(call['sourceDigest']) || !digest(call['derivedBindingInputDigest']) || !digest(call['regionResultDigest']) ||
      !isRevisionContextShape(call['context']) || call['firstEvaluationOrdinal'] !== end ||
      !integer(call['startedEvaluationExclusiveEnd'], end, record['startedEvaluations'] as number) || typeof call['retainedRegion'] !== 'boolean') return fail();
    end = call['startedEvaluationExclusiveEnd'] as number;
    const owned = regions.filter(r => equal(r.frame.binding.digest, call['bindingDigest']));
    if (call['retainedRegion']) {
      if (owned.length !== 1 || !equal(owned[0].frame.region.digest, call['publishedRegionDigest']) || !equal(owned[0].frame.source.digest, call['sourceDigest']) ||
        !equal(owned[0].source['context'], call['context']) || obj(call['regionalResult'])['execution'] !== owned[0].data['execution']) return fail();
    } else if (call['publishedRegionDigest'] !== null || owned.length) return fail();
    const a = obj(call['regionalResult']);
    if (!fields(a, ['execution', 'coverage', 'verdict', 'evidenceComplete']) || !['coglatas.completed', 'coglatas.resource-exhausted', 'coglatas.cancelled',
      'coglatas.not-run', 'coglatas.timed-out', 'coglatas.internal-error'].includes(a['execution'] as string) ||
      !['Complete', 'Partial', 'None'].includes(a['coverage'] as string) || !['Pass', 'Fail', 'Inconclusive', 'Error', 'NotRun'].includes(a['verdict'] as string) ||
      typeof a['evidenceComplete'] !== 'boolean') return fail();
  }
  if (calls.length && end !== record['startedEvaluations']) return fail();
  const originals = originalJsonArrayValues(run.observation.text, 'evaluations', 'originalObservation');
  const verified: VerifiedEvaluation[] = []; let nodes = 0; let retained = 0;
  for (const [index, metadata] of entries.entries()) {
    const original = obj(metadata['originalObservation']); const requirement = metadata['ruleId'] === 'coglatas.core.requirements';
    const region = regions.find(r => equal(r.frame.binding.digest, metadata['bindingRegionDigest']) && equal(r.frame.region.digest, metadata['publishedRegionDigest']));
    const call = calls.find(c => equal(c['bindingDigest'], metadata['bindingRegionDigest']));
    if (!region || !call || metadata['ordinal'] !== index || index < (call['firstEvaluationOrdinal'] as number) || index >= (call['startedEvaluationExclusiveEnd'] as number) ||
      !fields(metadata, ['ordinal', 'bindingRegionDigest', 'publishedRegionDigest', 'rootInputDigest', 'context', 'sourceDigest', 'ruleId', 'ruleVersion', 'location',
        'originalObservation', 'originalObservationDigest', 'regionalDiagnosticLinks', 'diagnostics']) ||
      !['coglatas.core.constraints', 'coglatas.core.requirements'].includes(metadata['ruleId'] as string) || metadata['ruleVersion'] !== 1 ||
      !equal(metadata['rootInputDigest'], value.inputDigest) || !equal(metadata['sourceDigest'], region.frame.source.digest) ||
      !equal(metadata['context'], region.source['context']) || !digest(metadata['originalObservationDigest']) ||
      !await verify('coglatas.local-predicate-evaluation-record/1', originals[index], metadata['originalObservationDigest'])) return fail();
    const location = obj(metadata['location']); const declarations = list(region.source['declarations'], 100_000).map(obj);
    const at = declarations.findIndex(d => d['entityId'] === location['entityId'] && d['documentId'] === location['documentId']);
    if (at < 0 || declarations[at]['kind'] !== (requirement ? 'coglatas.requirement' : 'coglatas.constraint')) return fail();
    expect(location, { format: 'coglatas.project-location/1', schemaId: 'coglatas.project-location', schemaVersion: 1, canonicalEncodingVersion: 1,
      context: region.source['context'], documentId: location['documentId'], entityId: location['entityId'], propertyPath: '' });
    const payloads = originalJsonArrayValues(region.frame.source.text, 'declarations', 'payload');
    const predicateText = originalOperatorMember(payloads[at], requirement ? 'condition' : 'predicate');
    if (!digest(original['predicateDigest']) || !await verify('coglatas.predicate/1', predicateText, original['predicateDigest']) || !digest(original['evaluationDigest']) ||
      !Array.isArray(original['nodes']) || !integer(original['observedNodes'], 1, 1024) || original['nodes'].length !== original['observedNodes'] ||
      !integer(original['retainedBodyBytes'], 1, 1_048_576)) return fail();
    let bodyBytes = 0; const seen = new Set<string>();
    for (const [ordinal, item] of original['nodes'].entries()) {
      if (!fields(item, ['ordinal', 'digest', 'bytes'])) return fail(); const n = obj(item);
      if (n['ordinal'] !== ordinal || !digest(n['digest']) || !integer(n['bytes'], 1, 1_048_576) || seen.has(n['digest'].value)) return fail();
      seen.add(n['digest'].value); bodyBytes += n['bytes'];
    }
    expect(original, { format: 'coglatas.local-predicate-evaluation-record/1', state: 'RecordedOperators', truth: original['truth'], execution: 'Completed',
      capture: { declared: 'BeforeEvaluation', mode: 'ObservedOperators', maximumNodes: 1024 - nodes, maximumBodyBytes: 1_048_576 - retained },
      predicateDigest: original['predicateDigest'], evaluationDigest: original['evaluationDigest'], observedNodes: original['observedNodes'], retainedBodyBytes: bodyBytes,
      scope: 'ActualStandaloneBooleanEvaluation', relationshipStructure: 'ObservedBooleanEvaluationTree', regionalEvaluation: 'NotRecorded', compilerRunIdentity: 'NotAllocated',
      runSelection: 'NotEvaluated', causalMeaning: 'NotRecorded', authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted', nodes: original['nodes'] });
    if (!['True', 'False', 'Unknown'].includes(original['truth'] as string)) return fail();
    const conditionCodes = requirement ? ['coglatas.analysis.requirement-condition-false'] : ['coglatas.analysis.constraint-false', 'coglatas.analysis.constraint-unknown'];
    const linked = region.diagnosticData.filter(d => d.data.ruleId === metadata['ruleId'] && equal(d.data.primaryLocation, location) &&
      (!requirement || conditionCodes.includes(d.data.code)));
    const emitted = original['truth'] === 'False' || !requirement && original['truth'] === 'Unknown';
    if (emitted ? linked.length !== 1 || linked[0].data.code !== (original['truth'] === 'False' ? conditionCodes[0] : conditionCodes[1]) : linked.length !== 0) return fail();
    expect(metadata['regionalDiagnosticLinks'], linked.length); expect(metadata['diagnostics'], linked.map(d => ({ diagnosticId: d.data.diagnosticId, digest: d.digest })));
    nodes += original['observedNodes'] as number; retained += bodyBytes; verified.push({ metadata: freeze(metadata), original: freeze(original), predicateText });
  }
  if (state === 'RecordedRegionalEvaluations') {
    if (!verified.length || record['startedEvaluations'] !== verified.length || record['completedEvaluations'] !== verified.length ||
      record['observedNodes'] !== nodes || record['retainedBodyBytes'] !== retained || nodes > 1024 || retained > 1_048_576) return fail();
  } else if (verified.length || record['retainedBodyBytes'] !== 0) return fail();
  if (state === 'NoActualRegionalEvaluations' && (record['startedEvaluations'] !== 0 || record['completedEvaluations'] !== 0 || record['observedNodes'] !== 0)) return fail();
  if (state === 'ManifestOnly' && (record['startedEvaluations'] !== record['completedEvaluations'] ||
    (record['observedNodes'] as number) < (record['startedEvaluations'] as number) ||
    (record['startedEvaluations'] === 0) !== (record['observedNodes'] === 0))) return fail();
  if (state === 'BodyBudgetExceeded' && (record['startedEvaluations'] === 0 || record['observedNodes'] === 0)) return fail();
  if (state === 'EvaluationLimitReached' && (record['startedEvaluations'] as number) <= 128 ||
    state === 'NodeLimitReached' && (record['observedNodes'] as number) < 1024) return fail();
  expect(record, { format: regionalFormat, state, scope: 'ActualRegionalEvaluationsOnly', initialRootEvaluation: 'NotRecorded', rootSummaryDiagnosticLinks: 'NotRecorded',
    compilerRunIdentity: 'NotAllocated', runSelection: 'NotEvaluated', causalMeaning: 'NotRecorded', authority: 'NotEvaluated', producerTrust: 'NotEvaluated',
    eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted', completedRegionCalls: record['completedRegionCalls'], startedEvaluations: record['startedEvaluations'],
    completedEvaluations: record['completedEvaluations'], observedNodes: record['observedNodes'], retainedBodyBytes: record['retainedBodyBytes'],
    capture: { declared: 'BeforeCompile', mode: policy['mode'], maximumEvaluations: 128, maximumNodes: 1024, maximumBodyBytes: policy['maximumBodyBytes'] },
    basis: { rootInputDigest: value.inputDigest, sourceDigest: value.sourceDigest, resultDigest: value.resultDigest },
    result: { ...axes(obj(value.result)), execution: value.result.execution }, regions: record['regions'], evaluations: record['evaluations'] });
  return freeze(verified);
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

async function verifySnapshots(run: RegionalOperatorRecordingFrame, region: RawRegion, record: Record<string, unknown>,
  evaluations: readonly VerifiedEvaluation[]): Promise<readonly VerifiedDiagnostic[]> {
  if (!run.observation) return fail(); const verified: VerifiedDiagnostic[] = [];
  for (const [index, diagnostic] of region.diagnosticData.entries()) {
    const entry = region.frame.diagnostics[index];
    const selected = evaluations.filter(e => equal(e.metadata['bindingRegionDigest'], region.frame.binding.digest) &&
      equal(e.metadata['publishedRegionDigest'], region.frame.region.digest) && list(e.metadata['diagnostics'], 4096).map(obj)
        .some(d => d['diagnosticId'] === diagnostic.data.diagnosticId && equal(d['digest'], diagnostic.digest)));
    if (!fields(entry.available, ['frame', 'operatorBodies', 'evaluationFrames'])) return fail();
    const bodies = await bodyMap(entry.available.operatorBodies, 'coglatas.observed-predicate-node/1', 1024);
    const frames = await bodyMap(entry.available.evaluationFrames, 'coglatas.local-predicate-evaluation/1', 128);
    const wantedBodies = new Set(selected.flatMap(e => (e.original['nodes'] as readonly DeclaredOperatorNode[]).map(n => n.digest.value)));
    const wantedFrames = new Set(selected.map(e => (e.original['evaluationDigest'] as Digest).value));
    if (bodies.size !== wantedBodies.size || [...bodies.keys()].some(k => !wantedBodies.has(k)) || frames.size !== wantedFrames.size ||
      [...frames.keys()].some(k => !wantedFrames.has(k))) return fail();
    const actual: AdmittedRegionalOperatorEvaluation[] = [];
    for (const e of selected) {
      const nodes = e.original['nodes'] as readonly DeclaredOperatorNode[]; const evaluation = frames.get((e.original['evaluationDigest'] as Digest).value);
      if (!evaluation) return fail();
      const graph = await validateOriginalOperatorGraph(e.predicateText, nodes, nodes.map(n => bodies.get(n.digest.value) ?? fail()), evaluation);
      const parsed = obj(parse(evaluation.text)); if (parsed['truth'] !== e.original['truth']) return fail();
      actual.push({ ordinal: e.metadata['ordinal'] as number, ruleId: e.metadata['ruleId'] as string, ruleVersion: e.metadata['ruleVersion'] as number,
        metadata: e.metadata, nodes: graph, evaluationFrame: evaluation });
    }
    const totalBytes = [...bodies.values()].reduce((n, b) => n + b.bytes, 0);
    const totalEvaluationBytes = [...frames.values()].reduce((n, f) => n + f.bytes, 0);
    const budgetNeeded = selected.reduce((n, e) => n + (e.original['retainedBodyBytes'] as number), 0);
    const snapshots = {} as Record<RecordedOperatorChoice, VerifiedSnapshot>;
    for (const choice of choices) {
      const projected = entry[choice]; if (!fields(projected, ['frame', 'operatorBodies', 'evaluationFrames'])) return fail();
      const data = await frame(projected.frame, snapshotFormat); const mode = choice === 'manifest' ? 'Manifest' : 'AvailableOperators';
      const maximumBodyBytes = choice === 'budget' ? 1 : 1_048_576;
      const captured = record['state'] === 'RecordedRegionalEvaluations' || record['state'] === 'NoActualRegionalEvaluations';
      const state = mode === 'Manifest' ? 'ManifestOnly' : !captured ? 'NoRecordedOperators' : !selected.length ? 'NoObservedRegionalDiagnostic'
        : budgetNeeded > maximumBodyBytes ? 'BodyBudgetExceeded' : 'AvailableOperators';
      const available = state === 'AvailableOperators';
      expect(projected.operatorBodies, available ? entry.available.operatorBodies : []); expect(projected.evaluationFrames, available ? entry.available.evaluationFrames : []);
      expect(data, { format: snapshotFormat, state, mode, maximumBodyBytes, scope: 'OriginalRegionalPredicateFrames', initialRootEvaluation: 'NotRecorded',
        rootSummaryDiagnosticLinks: 'NotRecorded', compilerRunIdentity: 'NotAllocated', runSelection: 'NotEvaluated', causalMeaning: 'NotRecorded',
        authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted',
        selectedEvaluations: selected.length, retainedBodyBytes: available ? totalBytes : 0, retainedEvaluationFrameBytes: available ? totalEvaluationBytes : 0,
        selection: !captured ? 'NotRecorded' : selected.length ? 'ObservedRegionalEvaluations' : 'NoObservedRegionalDiagnostic',
        originalRecord: { digest: run.observation.digest, captureState: record['state'] },
        region: { bindingDigest: region.frame.binding.digest, publishedDigest: region.frame.region.digest },
        diagnostic: { diagnosticId: diagnostic.data.diagnosticId, digest: diagnostic.digest },
        evaluations: selected.map(e => ({ ordinal: e.metadata['ordinal'], metadata: e.metadata,
          evaluationFrame: { digest: e.original['evaluationDigest'], bytes: frames.get((e.original['evaluationDigest'] as Digest).value)!.bytes, bodyCaptured: available },
          nodes: (e.original['nodes'] as readonly DeclaredOperatorNode[]).map(n => ({ ...n, bodyCaptured: available })) })) });
      snapshots[choice] = { snapshot: freeze(projected), data: freeze(data), evaluations: freeze(available ? actual : []) };
    }
    verified.push({ data: freeze(diagnostic.data), snapshots: freeze(snapshots) });
  }
  return freeze(verified);
}
async function verifyRun(value: PreviewCase, run: RegionalOperatorRecordingFrame, label: OperatorRecordingLabel, compiler: unknown): Promise<VerifiedRun> {
  if (!fields(run, ['request', 'policy', 'baseExecution', 'materializations', 'manifest', 'response', 'receipt', 'result', 'observation',
    'dependencyAnalysis', 'bindingClosure', 'regions']) || !Array.isArray(run.regions) || run.regions.length > 33) return fail();
  const [request, policy, base, materializations, manifest, response, receipt] = await Promise.all([
    frame(run.request, hostPrefix + 'compiler-host-input/1'), frame(run.policy, hostPrefix + 'capture-policy/1'),
    frame(run.baseExecution, 'coglatas.compiler-host-input/1', null), frame(run.materializations, 'coglatas.host-materializations/1'),
    frame(run.manifest, hostPrefix + 'compiler-recording-manifest/1'), frame(run.response, hostPrefix + 'compiler-host-response/1'),
    frame(run.receipt, hostPrefix + 'compiler-recording-receipt/1')]);
  const mode = label === 'original' ? 'Manifest' : 'ObservedOperators'; const maximumBodyBytes = label === 'budget' ? 1 : 1_048_576;
  expect(policy, { format: hostPrefix + 'capture-policy/1', mode, maximumEvaluations: 128, maximumNodes: 1024, maximumBodyBytes, scope: 'ActualRegionalEvaluationsOnly' });
  expect(materializations, capturedMaterializations(value)); expect(base, { declaredInputDigest: value.inputDigest, materializationsDigest: run.materializations.digest });
  expect(request, { format: hostPrefix + 'compiler-host-input/1', baseExecutionInputDigest: run.baseExecution.digest, declaredInputDigest: value.inputDigest,
    materializationsDigest: run.materializations.digest, observationPolicy: policy, observationPolicyDigest: run.policy.digest });
  if (!guid(manifest['sessionId']) || !guid(manifest['runId']) || !guid(response['attemptId']) || !integer(manifest['clientGeneration'], 0, Number.MAX_SAFE_INTEGER) ||
    !Array.isArray(manifest['knownLineage']) || manifest['knownLineage'].length > 31 || !manifest['knownLineage'].every(guid) ||
    new Set(manifest['knownLineage']).size !== manifest['knownLineage'].length || manifest['knownLineage'].includes(manifest['runId'])) return fail();
  const origin = manifest['origin'];
  if (origin !== null && (!fields(origin, ['runId', 'manifestDigest', 'receiptDigest', 'attemptId']) || !guid(obj(origin)['runId']) ||
    !guid(obj(origin)['attemptId']) || !digest(obj(origin)['manifestDigest']) || !digest(obj(origin)['receiptDigest']))) return fail();
  expect(manifest, { format: hostPrefix + 'compiler-recording-manifest/1', sessionId: manifest['sessionId'], runId: manifest['runId'],
    executionInputDigest: run.request.digest, baseExecutionInputDigest: run.baseExecution.digest, inputDigest: value.inputDigest, sourceDigest: value.sourceDigest,
    context: value.context, compiler, materializationsDigest: run.materializations.digest, clientGeneration: manifest['clientGeneration'],
    capture: { declared: 'BeforeExecution', policy, policyDigest: run.policy.digest }, origin, knownLineage: manifest['knownLineage'],
    identityScope: 'OwnedLocalSessionOnly', authority: 'NotEvaluated', retentionPolicy: 'NotAdopted' });
  let record: Record<string, unknown> | null = null; const regions: VerifiedRegion[] = [];
  const cancelled = label === 'cancelled';
  if (cancelled) { if (run.result !== null || run.observation !== null || run.dependencyAnalysis !== null || run.bindingClosure !== null || run.regions.length) return fail(); }
  else {
    if (!run.result || !run.observation || run.result.text !== value.rootSource?.result.text || !equal(run.result.digest, value.resultDigest)) return fail();
    expect(await frame(run.result, 'coglatas.compiler-result/1', 'coglatas.compiler-result/1', false), value.result);
    record = await frame(run.observation, regionalFormat); const raw = await verifyRegions(value, run);
    const evaluations = await verifyRegionalObservation(value, run, record, policy, raw);
    for (const region of raw) regions.push({ frame: freeze(region.frame), binding: region.binding, source: region.source, data: region.data,
      diagnostics: await verifySnapshots(run, region, record, evaluations), metadata: list(record['regions'], 33).map(obj)
        .find(m => equal(m['bindingDigest'], region.frame.binding.digest) && equal(m['publishedRegionDigest'], region.frame.region.digest)) ?? null });
  }
  const resultAxes = axes(obj(value.result)); const hostStatus = cancelled ? 'Cancelled' : 'Completed';
  const reasonCode = 'coglatas.regional-predicate-observation-host.' + (cancelled ? 'cancelled' : 'result-produced');
  expect(response, { format: hostPrefix + 'compiler-host-response/1', runId: manifest['runId'], manifestDigest: run.manifest.digest,
    executionInputDigest: run.request.digest, baseExecutionInputDigest: run.baseExecution.digest, materializationsDigest: run.materializations.digest,
    observationPolicyDigest: run.policy.digest, clientGeneration: manifest['clientGeneration'], attemptId: response['attemptId'], hostStatus, reasonCode,
    resultDigest: cancelled ? null : run.result!.digest, observationDigest: cancelled ? null : run.observation!.digest,
    compilerExecution: cancelled ? null : resultAxes['execution'], compilerCoverage: cancelled ? null : resultAxes['coverage'], compilerVerdict: cancelled ? null : resultAxes['verdict'],
    evidenceComplete: cancelled ? null : resultAxes['evidenceComplete'], captureState: record?.['state'] ?? null,
    identityScope: 'OwnedLocalDeclaredInvocationOnly', captureScope: 'ActualRegionalEvaluationsOnly', initialRootEvaluation: 'NotRecorded',
    rootSummaryDiagnosticLinks: 'NotRecorded', authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted' });
  expect(receipt, { format: hostPrefix + 'compiler-recording-receipt/1', runId: manifest['runId'], manifestDigest: run.manifest.digest,
    executionInputDigest: run.request.digest, observationPolicyDigest: run.policy.digest, attemptId: response['attemptId'], hostStatus, reasonCode,
    hostResponseDigest: run.response.digest, resultDigest: cancelled ? null : run.result!.digest, observationRecordDigest: cancelled ? null : run.observation!.digest,
    recordingState: cancelled ? 'HostOutcomeWithoutCompilerResult' : 'OriginalRegionalObservation', captureState: record?.['state'] ?? null,
    identityScope: 'OwnedLocalSessionAndHostAttempt', earlierIntermediateStates: 'NotRecorded', captureScope: 'ActualRegionalEvaluationsOnly', initialRootEvaluation: 'NotRecorded',
    rootSummaryDiagnosticLinks: 'NotRecorded', authority: 'NotEvaluated', producerTrust: 'NotEvaluated', eligibility: 'NotEvaluated', retentionPolicy: 'NotAdopted' });
  return { frame: freeze(run), policy: freeze(policy), manifest: freeze(manifest), response: freeze(response), receipt: freeze(receipt), record: record ? freeze(record) : null, regions: freeze(regions) };
}

export function regionalOperatorRunIdentitiesAreUnique(cases: readonly PreviewCase[]): boolean {
  try {
    const runs = new Set<string>(), attempts = new Set<string>(), sessions = new Set<string>();
    for (const value of cases) {
      const p = value.regionalOperatorRecordings; if (p == null) continue;
      if (!fields(p, ['format', 'source', 'input', ...labels]) || p.format !== 'coglatas.local-web-regional-operator-recording-fixture/1') return false;
      let session: string | null = null;
      for (const label of labels) {
        const m = obj(parse(p[label].manifest.text)), r = obj(parse(p[label].response.text)); metadataIntegers(p[label].manifest.text); metadataIntegers(p[label].response.text);
        if (!guid(m['runId']) || !guid(m['sessionId']) || !guid(r['attemptId']) || runs.has(m['runId']) || attempts.has(r['attemptId'])) return false;
        runs.add(m['runId']); attempts.add(r['attemptId']);
        if (session === null) { session = m['sessionId']; if (sessions.has(session)) return false; sessions.add(session); }
        else if (session !== m['sessionId']) return false;
      }
    }
    return [...runs].every(id => !attempts.has(id));
  } catch { return false; }
}
export function regionalOperatorRecordingMatches(value: PreviewCase, sources: RegionalOperatorSources, admitted: AdmittedRegionalOperatorRecording): boolean {
  try {
    const b = recordingBindings.get(admitted), p = value.regionalOperatorRecordings;
    return !!b && b.value === value && b.sources === sources && admitted.sources === sources && regionalOperatorSourcesMatch(value, sources) &&
      p === b.projection && p?.[b.label] === b.member && bounded(p) && JSON.stringify(p) === b.fingerprint;
  } catch { return false; }
}
/** Admit actual original local fixture frames. No remote Compiler or current authority is installed. */
export async function readRegionalOperatorRecording(value: PreviewCase, sources: RegionalOperatorSources,
  label: OperatorRecordingLabel = 'original'): Promise<AdmittedRegionalOperatorRecording | null> {
  if (!labels.includes(label) || !regionalOperatorSourcesMatch(value, sources)) return fail();
  const projection = value.regionalOperatorRecordings; if (projection == null) return null;
  if (!fields(projection, ['format', 'source', 'input', ...labels]) || projection.format !== 'coglatas.local-web-regional-operator-recording-fixture/1' || !bounded(projection)) return fail();
  const fingerprint = JSON.stringify(projection), member = projection[label]; const memberRefs = labels.map(l => projection[l]);
  if (new Set(memberRefs).size !== labels.length) return fail(); const copy = freeze(structuredClone(projection));
  const source = await frame(copy.source, 'coglatas.project-source/1', 'coglatas.project-source/1', false);
  const input = await frame(copy.input, 'coglatas.compiler-input/1', 'coglatas.compiler-input/1', false);
  if (copy.source.text !== value.sourceText || !equal(copy.source.digest, value.sourceDigest) || !equal(copy.input.digest, value.inputDigest) ||
    !equal(input, value.manifest['manifest']) || !equal(source['context'], value.context) || !await readSnapshotMaterialization(value)) return fail();
  if ('kind' in sources) { if ((sources as AdmittedRootSource).sourceText !== copy.source.text) return fail(); }
  else if ((sources as AdmittedScopedSources).regions.filter(r => sameContext(r.context, value.context) && r.sourceText === copy.source.text).length !== 1) return fail();
  const verified = new Map<OperatorRecordingLabel, VerifiedRun>();
  for (const name of labels) verified.set(name, await verifyRun(value, copy[name], name, obj(input['settings'])['compiler']));
  const original = verified.get('original')!, diagnostic = verified.get('diagnostic')!, cancelled = verified.get('cancelled')!;
  const runs = new Set([...verified.values()].map(v => v.manifest['runId'])), attempts = new Set([...verified.values()].map(v => v.response['attemptId']));
  if (runs.size !== 5 || attempts.size !== 5 || [...runs].some(id => attempts.has(id))) return fail();
  for (const [name, run] of verified) {
    expect(run.manifest['sessionId'], original.manifest['sessionId']); expect(run.manifest['clientGeneration'], original.manifest['clientGeneration']);
    expect(run.frame.baseExecution, original.frame.baseExecution); expect(run.frame.materializations, original.frame.materializations);
    const ancestor = name === 'diagnostic' ? original : name === 'budget' ? diagnostic : name === 'retry' ? cancelled : null;
    expect(run.manifest['origin'], ancestor ? { runId: ancestor.manifest['runId'], manifestDigest: ancestor.frame.manifest.digest,
      receiptDigest: ancestor.frame.receipt.digest, attemptId: ancestor.response['attemptId'] } : null);
    expect(run.manifest['knownLineage'], ancestor ? [...ancestor.manifest['knownLineage'] as string[], ancestor.manifest['runId']] : []);
  }
  // These explicit fixture runs have identical Compiler inputs/results; capture policy cannot change actual traversal.
  for (const name of ['original', 'budget', 'retry'] as const) {
    const record = verified.get(name)!.record!;
    for (const key of ['completedRegionCalls', 'startedEvaluations', 'completedEvaluations', 'observedNodes', 'regions']) expect(record[key], diagnostic.record![key]);
  }
  expect(verified.get('retry')!.frame.observation, diagnostic.frame.observation);
  if (!regionalOperatorSourcesMatch(value, sources) || projection !== value.regionalOperatorRecordings || JSON.stringify(projection) !== fingerprint ||
    labels.some((l, i) => projection[l] !== memberRefs[i])) return fail();
  const selected = verified.get(label)!, manifest = selected.manifest, response = selected.response, record = selected.record;
  const exposed: AdmittedRegionalOperatorRegion[] = []; const links: { exposed: AdmittedRegionalOperatorRegion; verified: VerifiedRegion }[] = [];
  for (const [index, region] of selected.regions.entries()) {
    if ('kind' in sources && !region.binding['isRoot']) continue;
    if (!('kind' in sources) && (sources as AdmittedScopedSources).regions.filter(r => equal(r.context, region.source['context']) && r.sourceText === region.frame.source.text &&
      equal(r.diagnostics, region.diagnostics.map(d => d.data))).length !== 1) return fail();
    const view: AdmittedRegionalOperatorRegion = freeze({ publishedOrdinal: index, callOrdinal: region.metadata ? region.metadata['ordinal'] as number : null,
      isRoot: region.binding['isRoot'] as boolean, context: region.source['context'] as RevisionContext, bindingDigest: region.frame.binding.digest,
      publishedDigest: region.frame.region.digest, diagnostics: region.diagnostics.map(d => d.data), metadata: region.metadata });
    exposed.push(view); links.push({ exposed: view, verified: region });
  }
  const admitted: AdmittedRegionalOperatorRecording = Object.freeze({ sources, label, runId: manifest['runId'] as string, sessionId: manifest['sessionId'] as string,
    attemptId: response['attemptId'] as string, originRunId: manifest['origin'] === null ? null : obj(manifest['origin'])['runId'] as string,
    hostStatus: response['hostStatus'] as 'Completed' | 'Cancelled', captureMode: selected.policy['mode'] as 'Manifest' | 'ObservedOperators',
    maximumBodyBytes: selected.policy['maximumBodyBytes'] as number, maximumNodes: selected.policy['maximumNodes'] as number, maximumEvaluations: selected.policy['maximumEvaluations'] as number,
    originalCaptureState: record ? record['state'] as string : null, completedRegionCalls: record ? record['completedRegionCalls'] as number : null,
    startedEvaluations: record ? record['startedEvaluations'] as number : null, completedEvaluations: record ? record['completedEvaluations'] as number : null,
    observedNodes: record ? record['observedNodes'] as number : null, retainedBodyBytes: record ? record['retainedBodyBytes'] as number : null,
    manifest: selected.frame.manifest, response: selected.frame.response, receipt: selected.frame.receipt, observation: selected.frame.observation,
    manifestData: selected.manifest, responseData: selected.response, receiptData: selected.receipt, observationData: record, regions: Object.freeze(exposed) });
  recordingBindings.set(admitted, { value, sources, projection, member, label, fingerprint, verified: selected });
  for (const l of links) regionBindings.set(l.exposed, { recording: admitted, verified: l.verified }); return admitted;
}
export function recordedRegionalOperatorsMatch(value: PreviewCase, sources: RegionalOperatorSources, recording: AdmittedRegionalOperatorRecording,
  region: AdmittedRegionalOperatorRegion, diagnostic: Diagnostic, admitted: AdmittedRecordedRegionalOperators): boolean {
  try {
    const b = viewBindings.get(admitted), r = regionBindings.get(region);
    return !!b && !!r && b.recording === recording && b.region === region && b.diagnostic === diagnostic && r.recording === recording &&
      recording.regions.includes(region) && region.diagnostics.includes(diagnostic) && regionalOperatorRecordingMatches(value, sources, recording);
  } catch { return false; }
}
export async function readRecordedRegionalOperators(value: PreviewCase, sources: RegionalOperatorSources, recording: AdmittedRegionalOperatorRecording,
  region: AdmittedRegionalOperatorRegion, diagnostic: Diagnostic, choice: RecordedOperatorChoice = 'manifest'): Promise<AdmittedRecordedRegionalOperators | null> {
  const bound = regionBindings.get(region);
  if (!choices.includes(choice) || !bound || bound.recording !== recording || !recording.regions.includes(region) || !region.diagnostics.includes(diagnostic) ||
    !regionalOperatorRecordingMatches(value, sources, recording)) return fail();
  if (recording.hostStatus !== 'Completed') return null;
  const selected = bound.verified.diagnostics[region.diagnostics.indexOf(diagnostic)]; if (!selected || selected.data !== diagnostic) return fail();
  const view = selected.snapshots[choice], data = view.data;
  const admitted: AdmittedRecordedRegionalOperators = Object.freeze({ text: view.snapshot.frame.text, digest: view.snapshot.frame.digest,
    recording, region, diagnostic, choice, state: data['state'] as string, mode: data['mode'] as 'Manifest' | 'AvailableOperators',
    maximumBodyBytes: data['maximumBodyBytes'] as number, originalCaptureState: recording.originalCaptureState!, retainedBodyBytes: data['retainedBodyBytes'] as number,
    retainedEvaluationFrameBytes: data['retainedEvaluationFrameBytes'] as number, evaluations: view.evaluations, data });
  if (!regionalOperatorRecordingMatches(value, sources, recording)) return fail(); viewBindings.set(admitted, { recording, region, diagnostic }); return admitted;
}
