import { Digest, PreviewCase, ProjectLocation, RevisionContext } from './projectide.contracts';
import { AdmittedScopedSources, scopedSourcesMatch } from './projectide-scoped-sources';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';
import { isProjectLocationShape } from './projectide-preview.validation';

export interface SimulationFrame { readonly text: string; readonly digest: Digest; }
export interface SimulationEvent {
  readonly sequence: number; readonly scheduledSequence: number; readonly kind: 'coglatas.task-start' | 'coglatas.task-end';
  readonly ticks: string; readonly context: RevisionContext; readonly irNodeId: string; readonly location: ProjectLocation;
}
export interface AdmittedSimulation {
  readonly sources: AdmittedScopedSources; readonly digest: Digest; readonly runId: string;
  readonly execution: string; readonly coverage: string; readonly reason: string; readonly virtualTicks: string;
  readonly startedTasks: number; readonly completedTasks: number; readonly events: readonly SimulationEvent[];
  readonly data: Readonly<Record<string, unknown>>;
}
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const fail = (): never => { throw new Error('unsupported-simulation-run'); };
const obj = (v: unknown) => object(v) ? v : fail();
const fields = (v: unknown, keys: readonly string[]) => object(v) && Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k));
const integer = (v: unknown, max: number): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0 && v <= max;
const digest = (v: unknown): v is Digest => fields(v, ['algorithm', 'value']) && obj(v)['algorithm'] === 'sha-256' && typeof obj(v)['value'] === 'string' && /^[a-f0-9]{64}$/u.test(obj(v)['value'] as string);
const ticks = (v: unknown): bigint => typeof v === 'string' && /^(0|[1-9][0-9]{0,18})$/u.test(v) && BigInt(v) <= 9223372036854775807n ? BigInt(v) : fail();
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const ordered = (v: unknown): unknown => Array.isArray(v) ? v.map(ordered) : object(v) ? Object.fromEntries(Object.keys(v).sort().map(k => [k, ordered(v[k])])) : v;
const key = (v: Record<string, unknown>) => JSON.stringify([ordered(v['context']), v['irNodeId']]);
const freeze = <T>(v: T): T => { if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); } return v; };
const engine = { artifactId: 'coglatas.elapsed-fs-simulator', version: '1', digest: { algorithm: 'sha-256', value: 'f00793b35d96a12850c9a34b38d717be99238b194eaddfd5fd7ad1d37c54c401' } };
const policy = 'coglatas.elapsed-fs-end-before-start-context-node-sequence/1';

/** Reads actual original Core bytes and transition membership/consistency. Generates no schedule or events and grants no current authority. */
export async function readSimulationRun(value: PreviewCase, sources: AdmittedScopedSources): Promise<AdmittedSimulation | null> {
  if (!scopedSourcesMatch(value, sources)) return fail();
  if (value.simulationRun == null) return null;
  if (!sources.plan || !fields(value.simulationRun, ['text', 'digest']) || typeof value.simulationRun.text !== 'string' ||
      new TextEncoder().encode(value.simulationRun.text).length > 4_194_304 || !digest(value.simulationRun.digest)) return fail();
  const frame = structuredClone(value.simulationRun);
  if (!await verify('coglatas.simulation-run/1', frame.text, frame.digest)) return fail();
  const data = obj(parse(frame.text)); const manifest = obj(data['manifest']); const limits = obj(manifest['limits']);
  if (!fields(data, ['format', 'manifest', 'manifestDigest', 'execution', 'coverage', 'freshness', 'verdict', 'verdictReason', 'evidenceKind', 'reason', 'virtualTicks', 'logicalOperationsConsumed', 'startedTasks', 'completedTasks', 'traceCoverage', 'events']) ||
      !fields(manifest, ['format', 'runId', 'planDigest', 'compilerInputId', 'inputDigest', 'sourceDigest', 'engine', 'startTicks', 'stochastic', 'sideEffects', 'policy', 'limits', 'originRunDigest']) ||
      data['format'] !== 'coglatas.simulation-run/1' || manifest['format'] !== 'coglatas.simulation-run-manifest/1' ||
      !digest(data['manifestDigest']) || !await verify('coglatas.simulation-run-manifest/1', JSON.stringify(manifest), data['manifestDigest']) ||
      typeof manifest['runId'] !== 'string' || !/^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/u.test(manifest['runId']) || manifest['runId'] === '00000000-0000-0000-0000-000000000000' ||
      manifest['compilerInputId'] !== value.inputId || !equal(manifest['inputDigest'], value.inputDigest) || !equal(manifest['sourceDigest'], value.sourceDigest) ||
      !equal(manifest['planDigest'], sources.plan.digest) || !equal(manifest['engine'], engine) || manifest['policy'] !== policy ||
      manifest['stochastic'] !== 'coglatas.none' || manifest['sideEffects'] !== 'coglatas.none' || manifest['originRunDigest'] !== null && !digest(manifest['originRunDigest']) ||
      data['freshness'] !== 'Current' || data['verdict'] !== 'NotRun' || data['verdictReason'] !== 'coglatas.no-outcome-assertions' || data['evidenceKind'] !== 'coglatas.simulation' ||
      !fields(limits, ['maximumEvents', 'maximumOperations', 'maximumElapsedTicks', 'maximumTraceBytes']) ||
      !integer(limits['maximumEvents'], 8192) || limits['maximumEvents'] < 1 || !integer(limits['maximumOperations'], 1000000) || limits['maximumOperations'] < 1 ||
      !integer(limits['maximumTraceBytes'], 2097152) || limits['maximumTraceBytes'] < 2 || !integer(data['logicalOperationsConsumed'], limits['maximumOperations'])) return fail();
  const start = ticks(manifest['startTicks']); const elapsed = ticks(limits['maximumElapsedTicks']);
  const tasks = sources.plan.definition['tasks']; const dependencies = sources.plan.definition['dependencies'];
  if (!Array.isArray(tasks) || !Array.isArray(dependencies) || !Array.isArray(data['events']) || data['events'].length > limits['maximumEvents'] ||
      new TextEncoder().encode(JSON.stringify(data['events'])).length > limits['maximumTraceBytes']) return fail();
  const taskIndex = new Map(tasks.map(obj).map(task => [key(task), task]));
  const incoming = new Map([...taskIndex.keys()].map(k => [k, [] as { prerequisite: string; lag: bigint }[]]));
  for (const raw of dependencies) {
    const edge = obj(raw); const dependent = key(obj(edge['dependent'])); const prerequisite = key(obj(edge['prerequisite']));
    if (!incoming.has(dependent) || !taskIndex.has(prerequisite)) return fail();
    incoming.get(dependent)!.push({ prerequisite, lag: ticks(obj(edge['lag'])['value']) });
  }
  const starts = new Map<string, bigint>(); const ends = new Map<string, bigint>(); const scheduled = new Set<number>(); let previous = start;
  for (const [index, raw] of data['events'].entries()) {
    const event = obj(raw); const taskKey = key(event); const task = taskIndex.get(taskKey); const time = ticks(event['ticks']);
    if (!fields(event, ['sequence', 'scheduledSequence', 'kind', 'ticks', 'context', 'irNodeId', 'location']) || !task ||
        event['sequence'] !== index + 1 || !integer(event['scheduledSequence'], tasks.length * 2) || event['scheduledSequence'] < 1 || scheduled.has(event['scheduledSequence']) ||
        !isProjectLocationShape(event['location']) || !equal(event['location'], task['location']) || time < previous || time - start > elapsed) return fail();
    scheduled.add(event['scheduledSequence']); previous = time;
    if (event['kind'] === 'coglatas.task-start') {
      if (starts.has(taskKey) || incoming.get(taskKey)!.some(edge => !ends.has(edge.prerequisite) || time < ends.get(edge.prerequisite)! + edge.lag)) return fail();
      starts.set(taskKey, time);
    } else if (event['kind'] === 'coglatas.task-end') {
      if (!starts.has(taskKey) || ends.has(taskKey) || time - starts.get(taskKey)! !== ticks(obj(task['duration'])['value'])) return fail();
      ends.set(taskKey, time);
    } else return fail();
  }
  const state = data['execution']; const count = data['events'].length;
  const coverage = state === 'Completed' ? 'Complete' : count ? 'Partial' : 'None';
  if (data['startedTasks'] !== starts.size || data['completedTasks'] !== ends.size || ticks(data['virtualTicks']) !== previous ||
      data['coverage'] !== coverage || data['traceCoverage'] !== coverage) return fail();
  if (state === 'Completed') { if (!tasks.length || ends.size !== tasks.length || data['reason'] !== 'coglatas.simulation.completed') return fail(); }
  else if (state === 'ResourceExhausted') {
    if (!['coglatas.simulation.event-bound', 'coglatas.simulation.time-bound', 'coglatas.simulation.operation-bound', 'coglatas.simulation.trace-bound'].includes(data['reason'] as string) || ends.size === tasks.length) return fail();
    if (data['reason'] === 'coglatas.simulation.event-bound' && count !== limits['maximumEvents']) return fail();
  } else if (state === 'Cancelled') { if (data['reason'] !== 'coglatas.cancelled' || ends.size === tasks.length) return fail(); }
  else if (state === 'InternalError') { if (data['reason'] !== 'coglatas.simulation.tick-overflow') return fail(); }
  else if (state === 'NotRun') { if (tasks.length || count || data['reason'] !== 'coglatas.simulation.empty-plan') return fail(); }
  else return fail();
  if (!scopedSourcesMatch(value, sources)) return fail();
  return freeze({ sources, digest: frame.digest, runId: manifest['runId'], execution: state, coverage, reason: data['reason'] as string,
    virtualTicks: data['virtualTicks'] as string, startedTasks: starts.size, completedTasks: ends.size, events: data['events'] as unknown as SimulationEvent[], data });
}
