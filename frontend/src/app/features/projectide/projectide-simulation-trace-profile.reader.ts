import { Digest, PreviewCase, sameDigest } from './projectide.contracts';
import { scopedSourcesMatch } from './projectide-scoped-sources';
import { AdmittedSimulation, SimulationFrame } from './projectide-simulation.reader';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

export interface TraceProfileMetrics {
  readonly recordedElapsedTicks: string; readonly recordedBusyTicks: string; readonly unattributedIdleTicks: string;
  readonly recordedTaskTicks: string; readonly peakActiveTasks: number; readonly openTasks: number;
}
export interface AdmittedTraceProfile {
  readonly simulation: AdmittedSimulation; readonly digest: Digest; readonly text: string;
  readonly state: 'Complete' | 'Incomplete'; readonly metrics: TraceProfileMetrics;
  readonly startTicks: string; readonly throughTicks: string; readonly maximumEvents: number;
  readonly data: Readonly<Record<string, unknown>>;
}
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const fail = (): never => { throw new Error('unsupported-simulation-trace-profile'); };
const obj = (v: unknown) => object(v) ? v : fail();
const fields = (v: unknown, keys: readonly string[]) => object(v) && Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k));
const digest = (v: unknown): v is Digest => fields(v, ['algorithm', 'value']) && obj(v)['algorithm'] === 'sha-256' && typeof obj(v)['value'] === 'string' && /^[a-f0-9]{64}$/u.test(obj(v)['value'] as string);
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const ordered = (v: unknown): unknown => Array.isArray(v) ? v.map(ordered) : object(v) ? Object.fromEntries(Object.keys(v).sort().map(k => [k, ordered(v[k])])) : v;
const freeze = <T>(v: T): T => { if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); } return v; };
const definitions = [
  { metric: 'recordedElapsedTicks', unit: 'virtual-tick', definition: 'Last committed tick minus fixed start, over this recorded prefix only.' },
  { metric: 'recordedBusyTicks', unit: 'virtual-tick', definition: 'Union of recorded intervals with at least one active Task; overlapping Tasks count once.' },
  { metric: 'unattributedIdleTicks', unit: 'virtual-tick', definition: 'Recorded elapsed ticks with no active Task; no dependency, approval, Resource or person cause is assigned.' },
  { metric: 'recordedTaskTicks', unit: 'task-virtual-tick', definition: 'Sum of active Task intervals, counting each overlapping Task separately; open Tasks end at the recorded boundary.' },
  { metric: 'peakActiveTasks', unit: 'task-count', definition: "Maximum active Task count after a committed transition in the engine's declared event order, including zero-duration transitions." },
  { metric: 'openTasks', unit: 'task-count', definition: 'Started but not ended Tasks at the recorded boundary; incomplete prefixes are not whole-run totals.' },
];
function current(value: PreviewCase, run: AdmittedSimulation): boolean {
  return scopedSourcesMatch(value, run.sources) && !!value.simulationRun && sameDigest(value.simulationRun.digest, run.digest);
}
/** Admit original Core profile bytes against the separately admitted immutable recording. Generates no events or current authority. */
export async function readSimulationTraceProfile(value: PreviewCase, run: AdmittedSimulation): Promise<AdmittedTraceProfile | null> {
  if (!current(value, run)) return fail();
  if (value.simulationTraceProfile == null) return null;
  const frame = structuredClone(value.simulationTraceProfile);
  if (!fields(frame, ['text', 'digest']) || typeof frame.text !== 'string' || new TextEncoder().encode(frame.text).length > 65_536 || !digest(frame.digest) ||
      !await verify('coglatas.local-simulation-trace-profile/1', frame.text, frame.digest)) return fail();
  const data = obj(parse(frame.text)); const query = obj(data['queryBudget']); const maximumEvents = query['maximumEvents'];
  if (!fields(query, ['maximumEvents']) || typeof maximumEvents !== 'number' || !Number.isSafeInteger(maximumEvents) || maximumEvents < 1 || maximumEvents > 8192 ||
      run.events.length > maximumEvents || run.execution === 'NotRun' || run.data['freshness'] !== 'Current') return fail();
  const manifest = obj(run.data['manifest']); const start = BigInt(manifest['startTicks'] as string);
  let previous = start; let busy = 0n; let taskTicks = 0n; let peak = 0; const active = new Set<string>();
  for (const row of run.events) {
    const time = BigInt(row.ticks); const delta = time - previous; if (delta < 0n) return fail();
    if (active.size) busy += delta; taskTicks += delta * BigInt(active.size); previous = time;
    const key = JSON.stringify([ordered(row.context), row.irNodeId]);
    if (row.kind === 'coglatas.task-start') { if (active.has(key)) return fail(); active.add(key); }
    else if (row.kind === 'coglatas.task-end') { if (!active.delete(key)) return fail(); }
    else return fail();
    peak = Math.max(peak, active.size);
  }
  if (previous !== BigInt(run.virtualTicks)) return fail();
  const elapsed = previous - start; const complete = run.execution === 'Completed' && run.coverage === 'Complete';
  const metrics: TraceProfileMetrics = { recordedElapsedTicks: String(elapsed), recordedBusyTicks: String(busy), unattributedIdleTicks: String(elapsed - busy),
    recordedTaskTicks: String(taskTicks), peakActiveTasks: peak, openTasks: active.size };
  const state = complete ? 'Complete' : 'Incomplete';
  const expected = { format: 'coglatas.local-simulation-trace-profile/1', state,
    reason: complete ? 'coglatas.profile.complete-recording' : 'coglatas.profile.recorded-prefix', inputKind: 'SimulatedModel', evidenceKind: 'coglatas.simulation',
    verdict: 'NotRun', authority: 'NotEvaluated', queryBudget: { maximumEvents }, capacityModel: 'coglatas.unlimited', attribution: 'Unattributed',
    basis: { runId: run.runId, runDigest: run.digest, inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, manifestDigest: run.data['manifestDigest'],
      planDigest: manifest['planDigest'], context: value.context, engine: manifest['engine'] },
    asOf: { kind: 'FixedRecording', runDigest: run.digest, inputDigest: value.inputDigest },
    recording: { execution: run.execution, coverage: run.coverage, freshness: run.data['freshness'], reason: run.reason, events: run.events.length,
      startedTasks: run.startedTasks, completedTasks: run.completedTasks },
    period: { kind: 'VirtualTicks', start: manifest['startTicks'], through: run.virtualTicks }, metrics, definitions };
  if (!equal(data, expected) || !current(value, run) || !equal(value.simulationTraceProfile, frame)) return fail();
  return freeze({ simulation: run, digest: frame.digest, text: frame.text, state, metrics, startTicks: String(start), throughTicks: run.virtualTicks, maximumEvents, data });
}
