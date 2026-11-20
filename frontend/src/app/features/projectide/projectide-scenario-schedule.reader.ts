import type { Digest, ProjectLocation } from './projectide.contracts';
import type { AdmittedScenario } from './projectide-scenario';
import { readScopedSources, scopedSourcesMatch } from './projectide-scoped-sources';
import { readSimulationComparison } from './projectide-simulation-comparison.reader';
import { type AdmittedSimulation, readSimulationRun } from './projectide-simulation.reader';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';
import { isProjectLocationShape } from './projectide-preview.validation';

export interface ScenarioScheduleProjection { readonly afterCaseKey: string; readonly text: string; readonly digest: Digest; }
interface ScheduledTask {
  readonly location: ProjectLocation; readonly startOffsetTicks: string; readonly finishOffsetTicks: string; readonly totalFloatTicks: string;
}
interface Schedule { readonly finishOffsetTicks: string; readonly tasks: readonly ScheduledTask[]; }
export interface AdmittedScenarioSchedule {
  readonly state: 'Complete' | 'Incomplete';
  readonly modeledFinishDeltaTicks: string | null;
  readonly baseline: Schedule | null;
  readonly hypothesis: Schedule | null;
  readonly beforeRun: AdmittedSimulation;
  readonly afterRun: AdmittedSimulation;
}

const domain = 'coglatas.local-scenario-elapsed-schedule/1';
const fail = (): never => { throw new Error('unsupported-scenario-schedule'); };
const record = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const obj = (v: unknown) => record(v) ? v : fail();
const fields = (value: Record<string, unknown>, names: readonly string[]) =>
  Object.keys(value).length === names.length && names.every(name => Object.hasOwn(value, name));
const equal = (a: unknown, b: unknown): boolean => a === b ||
  Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  record(a) && record(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const ticks = (v: unknown): bigint =>
  typeof v === 'string' && /^(0|[1-9][0-9]{0,18})$/u.test(v) && BigInt(v) <= 9223372036854775807n ? BigInt(v) : fail();
const slot = (context: unknown, node: unknown) => JSON.stringify([context, node]);
function planSlot(value: unknown): string {
  const row = obj(value);
  return slot(row['context'], row['irNodeId']);
}
const freeze = <T>(v: T): T => {
  if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); }
  return v;
};

function readSchedule(value: unknown, run: AdmittedSimulation): Schedule {
  const schedule = obj(value);
  const start = ticks(obj(run.data['manifest'])['startTicks']);
  const horizon = ticks(run.virtualTicks) - start;
  const plan = run.sources.plan?.definition ?? fail();
  const order = plan['definitionOrder'];
  const dependencies = plan['dependencies'];
  const definitions = plan['tasks'];
  const rows = schedule['tasks'];
  if (!fields(schedule, ['finishOffsetTicks', 'tasks']) || ticks(schedule['finishOffsetTicks']) !== horizon ||
      !Array.isArray(rows) || !Array.isArray(order) || !Array.isArray(dependencies) || !Array.isArray(definitions) ||
      rows.length !== order.length || rows.length !== definitions.length || rows.length !== run.completedTasks) return fail();

  const locations = new Map(definitions.map(obj).map(task => [planSlot(task), task['location']]));
  const starts = new Map<string, bigint>();
  const finishes = new Map<string, bigint>();
  for (const event of run.events) {
    (event.kind === 'coglatas.task-start' ? starts : finishes).set(slot(event.context, event.irNodeId), ticks(event.ticks) - start);
  }

  const timings = new Map<string, { start: bigint; finish: bigint; float: bigint; earliest: bigint; latest: bigint }>();
  const tasks = rows.map((raw, index): ScheduledTask => {
    const row = obj(raw);
    const key = planSlot(order[index]);
    const location = row['location'];
    const startOffsetTicks = row['startOffsetTicks'];
    const finishOffsetTicks = row['finishOffsetTicks'];
    const totalFloatTicks = row['totalFloatTicks'];
    if (!fields(row, ['location', 'startOffsetTicks', 'finishOffsetTicks', 'totalFloatTicks']) ||
        !isProjectLocationShape(location) || !equal(location, locations.get(key)) || timings.has(key) ||
        typeof startOffsetTicks !== 'string' || typeof finishOffsetTicks !== 'string' || typeof totalFloatTicks !== 'string') return fail();
    const taskStart = ticks(startOffsetTicks);
    const taskFinish = ticks(finishOffsetTicks);
    const float = ticks(totalFloatTicks);
    if (taskStart !== starts.get(key) || taskFinish !== finishes.get(key) || taskFinish + float > horizon) return fail();
    timings.set(key, { start: taskStart, finish: taskFinish, float, earliest: 0n, latest: horizon });
    return { location, startOffsetTicks, finishOffsetTicks, totalFloatTicks };
  });

  // Check the recorded offsets and float against each dependency. No simulation runs in the browser.
  for (const raw of dependencies) {
    const edge = obj(raw);
    const prerequisite = timings.get(planSlot(edge['prerequisite'])) ?? fail();
    const dependent = timings.get(planSlot(edge['dependent'])) ?? fail();
    const lag = ticks(obj(edge['lag'])['value']);
    const earliest = prerequisite.finish + lag;
    const latest = dependent.start + dependent.float - lag;
    if (earliest > dependent.earliest) dependent.earliest = earliest;
    if (latest < prerequisite.latest) prerequisite.latest = latest;
  }
  for (const task of timings.values()) {
    if (task.start !== task.earliest || task.finish + task.float !== task.latest) return fail();
  }
  return { finishOffsetTicks: horizon.toString(), tasks };
}

export function scenarioScheduleMatches(scenario: AdmittedScenario, schedule: AdmittedScenarioSchedule): boolean {
  return scopedSourcesMatch(scenario.baseline, schedule.beforeRun.sources) && scopedSourcesMatch(scenario.scenario, schedule.afterRun.sources);
}

export async function readScenarioSchedule(scenario: AdmittedScenario): Promise<AdmittedScenarioSchedule | null> {
  const { baseline, scenario: hypothesis } = scenario;
  if (baseline.scenarioScheduleImpact == null) return null;
  const projection = structuredClone(baseline.scenarioScheduleImpact);
  const digest = obj(projection.digest);
  if (!fields(obj(projection), ['afterCaseKey', 'text', 'digest']) || projection.afterCaseKey !== hypothesis.key ||
      typeof projection.text !== 'string' || new TextEncoder().encode(projection.text).length > 4_194_304 ||
      !fields(digest, ['algorithm', 'value']) || digest['algorithm'] !== 'sha-256' || typeof digest['value'] !== 'string' ||
      !/^[a-f0-9]{64}$/u.test(digest['value']) || !await verify(domain, projection.text, projection.digest)) return fail();

  const beforeSources = await readScopedSources(baseline) ?? fail();
  const afterSources = await readScopedSources(hypothesis) ?? fail();
  const beforeRun = await readSimulationRun(baseline, beforeSources) ?? fail();
  const afterRun = await readSimulationRun(hypothesis, afterSources) ?? fail();
  const pair = await readSimulationComparison(baseline, beforeRun, hypothesis, afterRun) ?? fail();
  const data = obj(parse(projection.text));
  const state = pair.state;
  if (!fields(data, ['format', 'overlayDigest', 'baselineRunDigest', 'scenarioRunDigest', 'state', 'reason', 'modeledFinishDeltaTicks', 'baseline', 'hypothesis']) ||
      data['format'] !== domain || !equal(data['overlayDigest'], scenario.prepared.overlay.digest) ||
      !equal(data['baselineRunDigest'], beforeRun.digest) || !equal(data['scenarioRunDigest'], afterRun.digest) ||
      data['state'] !== state || data['modeledFinishDeltaTicks'] !== pair.observedElapsedDelta ||
      !equal(baseline.scenarioScheduleImpact, projection)) return fail();

  if (state === 'Incomplete') {
    if (data['reason'] !== 'coglatas.comparison.incomplete-or-stale-recording' || data['baseline'] !== null || data['hypothesis'] !== null) return fail();
    return freeze({ state, modeledFinishDeltaTicks: null, baseline: null, hypothesis: null, beforeRun, afterRun });
  }
  if (data['reason'] !== 'coglatas.schedule-impact.elapsed-model' || !pair.sameLimits ||
      pair.beforeSummary.startTicks !== pair.afterSummary.startTicks) return fail();
  return freeze({ state, modeledFinishDeltaTicks: pair.observedElapsedDelta, baseline: readSchedule(data['baseline'], beforeRun),
    hypothesis: readSchedule(data['hypothesis'], afterRun), beforeRun, afterRun });
}
