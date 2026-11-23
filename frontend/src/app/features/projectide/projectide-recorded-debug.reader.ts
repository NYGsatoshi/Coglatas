import { Digest, PreviewCase, ProjectLocation } from './projectide.contracts';
import { AdmittedScopedSources, scopedSourcesMatch } from './projectide-scoped-sources';
import { AdmittedSimulation, SimulationEvent, SimulationFrame } from './projectide-simulation.reader';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';
import { isProjectLocationShape } from './projectide-preview.validation';

export interface RecordedDebugProjection {
  readonly format: 'coglatas.local-simulation-debug-recording/1';
  readonly controls: SimulationFrame; readonly manifest: SimulationFrame;
  readonly frames: readonly { readonly action: string; readonly target: ProjectLocation | null;
    readonly snapshot: SimulationFrame; readonly events: SimulationFrame; readonly inspection: SimulationFrame | null }[];
}
export interface RecordedDebugFrame {
  readonly action: string; readonly target: ProjectLocation | null; readonly revision: number;
  readonly state: 'Ready' | 'Paused' | 'Terminated'; readonly reason: string; readonly virtualTicks: string;
  readonly events: readonly SimulationEvent[]; readonly pending: Readonly<Record<string, unknown>> | null;
  readonly breakpoints: readonly Readonly<Record<string, unknown>>[]; readonly inspection: Readonly<Record<string, unknown>> | null;
  readonly snapshot: Readonly<Record<string, unknown>>;
}
export interface AdmittedRecordedDebug {
  readonly simulation: AdmittedSimulation; readonly controls: Readonly<Record<string, unknown>>;
  readonly frames: readonly RecordedDebugFrame[];
}
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const fail = (): never => { throw new Error('unsupported-recorded-debug'); };
const obj = (v: unknown): Record<string, unknown> => object(v) ? v : fail();
const fields = (v: unknown, keys: readonly string[]): boolean => object(v) && Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k));
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const ordered = (v: unknown): unknown => Array.isArray(v) ? v.map(ordered) : object(v) ? Object.fromEntries(Object.keys(v).sort().map(k => [k, ordered(v[k])])) : v;
const key = (v: Record<string, unknown>): string => JSON.stringify([ordered(v['context']), v['irNodeId']]);
const id = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/u.test(v) && v !== '00000000-0000-0000-0000-000000000000';
const integer = (v: unknown, max: number): v is number => typeof v === 'number' && Number.isSafeInteger(v) && v >= 0 && v <= max;
const ticks = (v: unknown): bigint => typeof v === 'string' && /^(0|[1-9][0-9]{0,18})$/u.test(v) && BigInt(v) <= 9223372036854775807n ? BigInt(v) : fail();
const dig = (v: unknown): v is Digest => fields(v, ['algorithm', 'value']) && obj(v)['algorithm'] === 'sha-256' && typeof obj(v)['value'] === 'string' && /^[a-f0-9]{64}$/u.test(obj(v)['value'] as string);
const byteLength = (v: string): number => new TextEncoder().encode(v).length;
const freeze = <T>(v: T): T => { if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); } return v; };
const controlLimits = { MaximumBreakpointBytes: 262144, MaximumBreakpoints: 128, MaximumCommands: 16384,
  MaximumInspectedEdges: 4096, MaximumInspectionBytes: 1048576, MaximumWaitingReasons: 64 };
const commands = ['coglatas.event-step', 'coglatas.task-step-over', 'coglatas.continue', 'coglatas.inspect-fixed-task'];
async function frame(v: unknown, domain: string, maximum = 4_194_304): Promise<unknown> {
  const row = obj(v);
  if (!fields(row, ['text', 'digest']) || typeof row['text'] !== 'string' || byteLength(row['text']) > maximum || !dig(row['digest']) ||
      !await verify(domain, row['text'], row['digest'])) return fail();
  return parse(row['text']);
}

/** Admits original bounded Core snapshots against an already admitted terminal run. Executes no commands and creates no transitions. */
export async function readRecordedDebug(value: PreviewCase, sources: AdmittedScopedSources, simulation: AdmittedSimulation | null): Promise<AdmittedRecordedDebug | null> {
  if (!scopedSourcesMatch(value, sources)) return fail();
  const original = (value as PreviewCase & { simulationDebug?: RecordedDebugProjection }).simulationDebug;
  if (original == null) return null;
  if (!sources.plan || !simulation || simulation.sources !== sources || !equal(simulation.digest, value.simulationRun?.digest) ||
      !fields(original, ['format', 'controls', 'manifest', 'frames']) || original.format !== 'coglatas.local-simulation-debug-recording/1' ||
      !Array.isArray(original.frames) || original.frames.length < 1 || original.frames.length > 64 || byteLength(JSON.stringify(original)) > 16_777_216) return fail();
  const projection = structuredClone(original);
  const manifest = obj(await frame(projection.manifest, 'coglatas.simulation-run-manifest/1'));
  if (!equal(manifest, simulation.data['manifest']) || !equal(projection.manifest.digest, simulation.data['manifestDigest'])) return fail();
  const controls = obj(await frame(projection.controls, 'coglatas.simulation-debug-controls/1'));
  if (!fields(controls, ['format', 'manifestDigest', 'engine', 'points', 'commands', 'limits']) ||
      controls['format'] !== 'coglatas.simulation-debug-controls/1' || !equal(controls['manifestDigest'], projection.manifest.digest) ||
      !equal(controls['engine'], manifest['engine']) || !equal(controls['points'], ['coglatas.task-start', 'coglatas.task-end']) ||
      !equal(controls['commands'], commands) || !equal(controls['limits'], controlLimits)) return fail();
  const taskRows = sources.plan.definition['tasks']; const edgeRows = sources.plan.definition['dependencies'];
  if (!Array.isArray(taskRows) || !Array.isArray(edgeRows)) return fail();
  const tasks = taskRows.map(obj); const taskIndex = new Map(tasks.map(task => [key(task), task]));
  const edges = edgeRows.map(obj);
  const resolve = (location: unknown): Record<string, unknown> | null => isProjectLocationShape(location) ? tasks.find(task => equal(task['location'], location)) ?? null : null;
  const frames: RecordedDebugFrame[] = []; let previousOperations = -1; let previousCount = 0; let resumeSequence: number | null = null;
  for (const [index, raw] of projection.frames.entries()) {
    if (!fields(raw, ['action', 'target', 'snapshot', 'events', 'inspection']) || typeof raw.action !== 'string' ||
        raw.target !== null && !resolve(raw.target)) return fail();
    const snapshot = obj(await frame(raw.snapshot, 'coglatas.simulation-debug-snapshot/1'));
    const events = await frame(raw.events, 'coglatas.simulation-trace-prefix/1', obj(manifest['limits'])['maximumTraceBytes'] as number);
    if (!fields(snapshot, ['format', 'controlsDigest', 'manifestDigest', 'inputDigest', 'planDigest', 'revision', 'state', 'reason', 'virtualTicks', 'inputFreshness', 'logicalOperationsConsumed',
        'startedTasks', 'completedTasks', 'eventCount', 'traceCoverage', 'tracePrefixDigest', 'pending', 'breakpoints', 'hitBreakpointId', 'inspectionDigest', 'resultDigest']) ||
        snapshot['format'] !== 'coglatas.simulation-debug-snapshot/1' || !equal(snapshot['controlsDigest'], projection.controls.digest) ||
        !equal(snapshot['manifestDigest'], projection.manifest.digest) || !equal(snapshot['inputDigest'], value.inputDigest) || !equal(snapshot['planDigest'], sources.plan.digest) ||
        snapshot['revision'] !== index || snapshot['inputFreshness'] !== 'Current' ||
        !['Ready', 'Paused', 'Terminated'].includes(snapshot['state'] as string) ||
        !integer(snapshot['logicalOperationsConsumed'], obj(manifest['limits'])['maximumOperations'] as number) || snapshot['logicalOperationsConsumed'] < previousOperations ||
        !Array.isArray(events) || events.length < previousCount || events.length > simulation.events.length ||
        !events.every((event, i) => equal(event, simulation.events[i])) || snapshot['eventCount'] !== events.length ||
        !equal(snapshot['tracePrefixDigest'], raw.events.digest)) return fail();
    const starts = new Map<string, bigint>(); const ends = new Map<string, bigint>();
    for (const event of events) (obj(event)['kind'] === 'coglatas.task-start' ? starts : ends).set(key(obj(event)), ticks(obj(event)['ticks']));
    const now = events.length ? ticks(obj(events.at(-1))['ticks']) : ticks(manifest['startTicks']);
    if (ticks(snapshot['virtualTicks']) !== now || snapshot['startedTasks'] !== starts.size || snapshot['completedTasks'] !== ends.size) return fail();
    const terminal = snapshot['state'] === 'Terminated';
    if (terminal ? !equal(snapshot['resultDigest'], simulation.digest) || snapshot['traceCoverage'] !== simulation.coverage || events.length !== simulation.events.length || snapshot['pending'] !== null || snapshot['logicalOperationsConsumed'] !== simulation.data['logicalOperationsConsumed']
      : snapshot['resultDigest'] !== null || snapshot['traceCoverage'] !== 'Prefix' || snapshot['pending'] === null) return fail();
    let pending: Record<string, unknown> | null = null;
    if (snapshot['pending'] !== null) {
      pending = obj(snapshot['pending']); const task = resolve(pending['location']); const time = ticks(pending['ticks']);
      if (!fields(pending, ['scheduledSequence', 'kind', 'ticks', 'irNodeId', 'location']) || !task || task['irNodeId'] !== pending['irNodeId'] ||
          !integer(pending['scheduledSequence'], tasks.length * 2) || pending['scheduledSequence'] < 1 || time < now ||
          events.some(event => obj(event)['scheduledSequence'] === pending!['scheduledSequence'])) return fail();
      const taskKey = key(task); const next = simulation.events[events.length];
      if (next && !equal(pending, { scheduledSequence: next.scheduledSequence, kind: next.kind, ticks: next.ticks, irNodeId: next.irNodeId, location: next.location })) return fail();
      if (pending['kind'] === 'coglatas.task-end') {
        if (!starts.has(taskKey) || ends.has(taskKey) || time !== starts.get(taskKey)! + ticks(obj(task['duration'])['value'])) return fail();
      } else if (pending['kind'] === 'coglatas.task-start') {
        if (starts.has(taskKey) || edges.filter(edge => key(obj(edge['dependent'])) === taskKey).some(edge => {
          const end = ends.get(key(obj(edge['prerequisite']))); return end === undefined || time < end + ticks(obj(edge['lag'])['value']);
        })) return fail();
      } else return fail();
    }
    if (!Array.isArray(snapshot['breakpoints']) || snapshot['breakpoints'].length > controlLimits.MaximumBreakpoints) return fail();
    const breakpoints = snapshot['breakpoints'].map(obj); const breakpointIds = new Set<string>(); let breakpointBytes = 0;
    for (const point of breakpoints) {
      const request = obj(point['request']); const bound = resolve(request['location']);
      if (!fields(point, ['request', 'resolved', 'boundLocation', 'reason']) || !fields(request, ['breakpointId', 'planDigest', 'location', 'point']) ||
          !id(request['breakpointId']) || breakpointIds.has(request['breakpointId']) || !dig(request['planDigest']) || !isProjectLocationShape(request['location']) ||
          typeof request['point'] !== 'number' || !Number.isInteger(request['point']) || request['point'] < -2147483648 || request['point'] > 2147483647) return fail();
      breakpointIds.add(request['breakpointId']); breakpointBytes += byteLength(JSON.stringify(request));
      const reason = !equal(request['planDigest'], sources.plan.digest) ? 'coglatas.debug.wrong-plan' : ![1, 2].includes(request['point']) ? 'coglatas.debug.point-unsupported' : !bound ? 'coglatas.debug.position-unresolved' : 'coglatas.debug.resolved';
      if (point['reason'] !== reason || point['resolved'] !== (reason === 'coglatas.debug.resolved') ||
          !equal(point['boundLocation'], reason === 'coglatas.debug.resolved' ? bound!['location'] : null)) return fail();
    }
    if (breakpointBytes > controlLimits.MaximumBreakpointBytes || breakpoints.some((point, i) => i > 0 &&
        (obj(breakpoints[i - 1]['request'])['breakpointId'] as string) >= (obj(point['request'])['breakpointId'] as string))) return fail();
    const matchingBreakpoint = (event: Record<string, unknown>) => breakpoints.find(point => point['resolved'] &&
      equal(point['boundLocation'], event['location']) && obj(point['request'])['point'] === (event['kind'] === 'coglatas.task-start' ? 1 : 2));
    const hit = snapshot['hitBreakpointId'];
    if (hit !== null) {
      const point = breakpoints.find(point => obj(point['request'])['breakpointId'] === hit);
      if (!point || !point['resolved'] || !pending || snapshot['reason'] !== 'coglatas.debug.breakpoint' || snapshot['state'] !== 'Paused' ||
          !equal(point['boundLocation'], pending['location']) || obj(point['request'])['point'] !== (pending['kind'] === 'coglatas.task-start' ? 1 : 2)) return fail();
    } else if (snapshot['reason'] === 'coglatas.debug.breakpoint') return fail();
    const previous = frames.at(-1);
    if (!previous) {
      if (snapshot['state'] === 'Paused' || raw.action !== 'coglatas.recorded-preparation' || raw.target !== null || snapshot['reason'] !== 'coglatas.debug.prepared' || events.length || breakpoints.length || raw.inspection !== null) return fail();
    } else {
      if (raw.action === 'coglatas.recorded-preparation' || ![...commands, 'coglatas.recorded-set-breakpoints'].includes(raw.action)) return fail();
      if (previous.state === 'Terminated' && raw.action !== 'coglatas.inspect-fixed-task') return fail();
      if (['coglatas.event-step', 'coglatas.continue', 'coglatas.task-step-over'].includes(raw.action) && !terminal && snapshot['state'] !== 'Paused') return fail();
      if (raw.action !== 'coglatas.recorded-set-breakpoints' && !equal(breakpoints, previous.breakpoints)) return fail();
      if (raw.action === 'coglatas.inspect-fixed-task' || raw.action === 'coglatas.recorded-set-breakpoints') {
        if (events.length !== previous.events.length || snapshot['logicalOperationsConsumed'] !== previous.snapshot['logicalOperationsConsumed'] || snapshot['state'] !== previous.state || !equal(pending, previous.pending) || hit !== null ||
            snapshot['reason'] !== (raw.action === 'coglatas.inspect-fixed-task' ? 'coglatas.debug.inspected' : 'coglatas.debug.breakpoints-set')) return fail();
        if (raw.action === 'coglatas.recorded-set-breakpoints' && !equal(snapshot['inspectionDigest'], previous.snapshot['inspectionDigest'])) return fail();
      } else if (raw.action === 'coglatas.event-step') {
        if (events.length - previous.events.length > 1 || !terminal && events.length === previous.events.length || snapshot['reason'] !== 'coglatas.debug.event-step') return fail();
      } else if (raw.action === 'coglatas.continue') {
        if (!['coglatas.debug.continued', 'coglatas.debug.breakpoint'].includes(snapshot['reason'] as string) || snapshot['reason'] === 'coglatas.debug.continued' && !terminal) return fail();
      } else if (raw.action === 'coglatas.task-step-over') {
        const task = resolve(raw.target);
        if (!task || previous.pending?.['kind'] !== 'coglatas.task-start' || !equal(previous.pending['location'], raw.target) ||
            !['coglatas.debug.task-step-over', 'coglatas.debug.breakpoint'].includes(snapshot['reason'] as string) ||
            !terminal && snapshot['reason'] === 'coglatas.debug.task-step-over' && !ends.has(key(task))) return fail();
      }
      if (raw.action === 'coglatas.event-step') resumeSequence = null;
      if (raw.action === 'coglatas.continue' || raw.action === 'coglatas.task-step-over') {
        if (raw.action === 'coglatas.task-step-over') resumeSequence = previous.pending!['scheduledSequence'] as number;
        const added = events.slice(previous.events.length).map(obj); let selectedEnded = false;
        for (const event of added) {
          if (selectedEnded || matchingBreakpoint(event) && resumeSequence !== event['scheduledSequence']) return fail();
          resumeSequence = null;
          selectedEnded = raw.action === 'coglatas.task-step-over' && event['kind'] === 'coglatas.task-end' && equal(event['location'], raw.target);
        }
        if (hit !== null) {
          if (selectedEnded || !pending || resumeSequence === pending['scheduledSequence'] ||
              obj(matchingBreakpoint(pending)!['request'])['breakpointId'] !== hit) return fail();
          resumeSequence = pending['scheduledSequence'] as number;
        } else if (!added.length && previous.pending && simulation.data['execution'] !== 'Cancelled' &&
            matchingBreakpoint(previous.pending) && resumeSequence !== previous.pending['scheduledSequence']) return fail();
      }
    }
    if ((raw.action === 'coglatas.inspect-fixed-task' || raw.action === 'coglatas.task-step-over') !== (raw.target !== null)) return fail();
    let inspection: Record<string, unknown> | null = null;
    if (raw.inspection !== null) {
      inspection = obj(await frame(raw.inspection, 'coglatas.simulation-task-inspection/1', controlLimits.MaximumInspectionBytes));
      if (!equal(snapshot['inspectionDigest'], raw.inspection.digest)) return fail();
      if (raw.action === 'coglatas.inspect-fixed-task') inspectFrame(inspection, resolve(raw.target)!, now, starts, ends, edges, projection.manifest.digest, sources.plan.digest);
      else if (raw.action !== 'coglatas.recorded-set-breakpoints' || !previous?.inspection || !equal(inspection, previous.inspection)) return fail();
    } else if (snapshot['inspectionDigest'] !== null || raw.action === 'coglatas.inspect-fixed-task') return fail();
    frames.push({ action: raw.action, target: raw.target, revision: index, state: snapshot['state'] as RecordedDebugFrame['state'], reason: snapshot['reason'] as string,
      virtualTicks: snapshot['virtualTicks'] as string, events: events as unknown as SimulationEvent[], pending, breakpoints, inspection, snapshot });
    previousOperations = snapshot['logicalOperationsConsumed']; previousCount = events.length;
  }
  if (frames.at(-1)?.state !== 'Terminated' || !scopedSourcesMatch(value, sources)) return fail();
  return freeze({ simulation, controls, frames });
}

function inspectFrame(inspection: Record<string, unknown>, task: Record<string, unknown>, now: bigint,
  starts: Map<string, bigint>, ends: Map<string, bigint>, edges: Record<string, unknown>[], manifest: Digest, plan: Digest): void {
  const taskKey = key(task); const start = starts.get(taskKey); const finish = ends.get(taskKey); const duration = ticks(obj(task['duration'])['value']);
  const incoming = edges.filter(edge => key(obj(edge['dependent'])) === taskKey);
  const missing = incoming.filter(edge => !ends.has(key(obj(edge['prerequisite']))));
  const readyAt = incoming.reduce((time, edge) => { const end = ends.get(key(obj(edge['prerequisite']))); return end === undefined ? time : time > end + ticks(obj(edge['lag'])['value']) ? time : end + ticks(obj(edge['lag'])['value']); }, 0n);
  const waitingLag = !missing.length && start === undefined && readyAt > now;
  const state = finish !== undefined ? 'Completed' : start !== undefined ? 'Running' : missing.length ? 'WaitingPrerequisites' : waitingLag ? 'WaitingLag' : 'Ready';
  const expectedWaits = missing.length ? incoming.slice(0, controlLimits.MaximumInspectedEdges).filter(edge => !ends.has(key(obj(edge['prerequisite'])))).slice(0, controlLimits.MaximumWaitingReasons)
    .map(edge => ({ kind: 'coglatas.waiting-prerequisite', prerequisite: edge['prerequisite'], location: edge['location'] })) : waitingLag ? [{ kind: 'coglatas.waiting-lag', readyAtTicks: readyAt.toString() }] : [];
  const waitingCount = missing.length || (waitingLag ? 1 : 0);
  if (!fields(inspection, ['format', 'manifestDigest', 'planDigest', 'location', 'context', 'irNodeId', 'state', 'virtualTicks', 'startTicks', 'finishTicks', 'remainingTicks', 'duration', 'ports', 'values', 'waitingReasonCount', 'waitingReasons', 'waitingCoverage']) ||
      inspection['format'] !== 'coglatas.simulation-task-inspection/1' || !equal(inspection['manifestDigest'], manifest) || !equal(inspection['planDigest'], plan) ||
      !equal(inspection['location'], task['location']) || !equal(inspection['context'], task['context']) || inspection['irNodeId'] !== task['irNodeId'] ||
      inspection['state'] !== state || ticks(inspection['virtualTicks']) !== now || inspection['startTicks'] !== (start?.toString() ?? null) ||
      inspection['finishTicks'] !== (finish?.toString() ?? null) || inspection['remainingTicks'] !== (state === 'Running' ? (start! + duration - now).toString() : null) ||
      !equal(inspection['duration'], task['duration']) || !equal(inspection['ports'], task['ports']) || inspection['values'] !== 'coglatas.fixed-input-defaults' ||
      inspection['waitingReasonCount'] !== waitingCount || !equal(inspection['waitingReasons'], expectedWaits) ||
      inspection['waitingCoverage'] !== (expectedWaits.length === waitingCount ? 'Complete' : 'Partial')) return fail();
}
