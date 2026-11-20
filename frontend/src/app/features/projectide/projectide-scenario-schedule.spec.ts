/// <reference types="node" />
import { createHash, webcrypto } from 'node:crypto';
import { readFileSync } from 'node:fs';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readPreparedScenario } from './projectide-scenario';
import { readScenarioSchedule, scenarioScheduleMatches } from './projectide-scenario-schedule.reader';
import { ProjectIdeWorkbenchStore } from './projectide-workbench.store';

vi.stubGlobal('crypto', webcrypto);
const fixture = readFileSync('public/assets/projectide/scenario-schedule-v1.json', 'utf8');
const domain = 'coglatas.local-scenario-elapsed-schedule/1';
const hash = (text: string, format = domain) => ({ algorithm: 'sha-256' as const, value: createHash('sha256').update(format + '\0' + text).digest('hex') });
const bundle = () => readPreviewBundle(JSON.parse(fixture));
async function prepared(key = 'tests-simulation-root-base') {
  const values = bundle();
  const baseline = values.cases.find(item => item.key === key)!;
  return (await readPreparedScenario(values, baseline))!;
}
const root = 'tests-simulation-root-base';
const bounded = 'tests-simulation-bounded-base';

describe('Core Scenario schedule evidence', () => {
  it('admits the two fixed recordings with the exact finish change and total slack', async () => {
    const scenario = await prepared();
    const result = (await readScenarioSchedule(scenario))!;
    expect(result.state).toBe('Complete');
    expect(result.modeledFinishDeltaTicks).toBe('2');
    expect(result.baseline?.finishOffsetTicks).toBe('7');
    expect(result.hypothesis?.finishOffsetTicks).toBe('9');
    const early = result.baseline?.tasks.find(task => task.location.entityId?.startsWith('00000007-'));
    expect(early?.totalFloatTicks).toBe('5');
    expect(Object.isFrozen(result.baseline?.tasks)).toBe(true);
    expect(scenarioScheduleMatches(scenario, result)).toBe(true);
    Object.assign(scenario.scenario.simulationRun!, { text: 'changed' });
    expect(scenarioScheduleMatches(scenario, result)).toBe(false);
  });

  it.each(['baseline', 'scenario'] as const)('uses the admitted Task identity for reordered %s event contexts', async stage => {
    const scenario = await prepared();
    const original = (await readScenarioSchedule(scenario))!;
    const run = scenario[stage].simulationRun!;
    const recording = JSON.parse(run.text);
    const context = recording.events[0].context;
    recording.events[0].context = Object.fromEntries(Object.entries(context).reverse());
    expect(JSON.stringify(recording.events[0].context)).not.toBe(JSON.stringify(context));
    const runText = JSON.stringify(recording);
    const runDigest = hash(runText, 'coglatas.simulation-run/1');
    Object.assign(run, { text: runText, digest: runDigest });

    const comparison = scenario.baseline.simulationComparison!;
    const pair = JSON.parse(comparison.text);
    pair[stage === 'baseline' ? 'before' : 'after'].runDigest = runDigest;
    const pairText = JSON.stringify(pair);
    Object.assign(comparison, { text: pairText, digest: hash(pairText, 'coglatas.local-simulation-comparison/1') });

    const schedule = scenario.baseline.scenarioScheduleImpact!;
    const projection = JSON.parse(schedule.text);
    projection[stage === 'baseline' ? 'baselineRunDigest' : 'scenarioRunDigest'] = runDigest;
    const scheduleText = JSON.stringify(projection);
    Object.assign(schedule, { text: scheduleText, digest: hash(scheduleText) });

    const admitted = (await readScenarioSchedule(scenario))!;
    expect(admitted.state).toBe('Complete');
    expect(admitted.modeledFinishDeltaTicks).toBe(original.modeledFinishDeltaTicks);
    expect(admitted.baseline).toEqual(original.baseline);
    expect(admitted.hypothesis).toEqual(original.hypothesis);
  });

  it('can show the bounded comparison without a finish or a delta', async () => {
    const result = (await readScenarioSchedule(await prepared(bounded)))!;
    expect(result.state).toBe('Incomplete');
    expect(result.modeledFinishDeltaTicks).toBeNull();
    expect(result.baseline).toBeNull();
    expect(result.hypothesis).toBeNull();
  });

  it.each([
    ['overlay', data => data.overlayDigest.value = 'f'.repeat(64)],
    ['other run', data => data.scenarioRunDigest = data.baselineRunDigest],
    ['finish delta', data => data.modeledFinishDeltaTicks = '0'],
    ['numeric tick', data => data.baseline.tasks[0].startOffsetTicks = 0],
    ['task schedule', data => data.baseline.tasks[0].finishOffsetTicks = '3'],
    ['total slack', data => data.baseline.tasks[0].totalFloatTicks = '4'],
    ['task context', data => data.baseline.tasks[0].location = data.hypothesis.tasks[0].location],
    ['row order', data => data.baseline.tasks.reverse()],
    ['duplicated row', data => data.baseline.tasks[1] = data.baseline.tasks[0]],
  ] as const)('refuses %s even when the envelope digest is recalculated', async (_mode, change) => {
    const scenario = await prepared();
    const projection = scenario.baseline.scenarioScheduleImpact!;
    const data = JSON.parse(projection.text);
    change(data);
    const text = JSON.stringify(data);
    Object.assign(projection, { text, digest: hash(text) });
    await expect(readScenarioSchedule(scenario)).rejects.toThrow('unsupported-scenario-schedule');
  });

  it('withholds schedule evidence after a replacement and keeps the verified Scenario layers', async () => {
    const values = bundle();
    const store = new ProjectIdeWorkbenchStore();
    await store.load(async () => values);
    store.choose(root);
    store.loadResult();
    expect(await store.loadScenarioComparison(store.preparedScenario()!)).toBe(true);
    expect(store.scenarioSchedule()).toMatchObject({ state: 'Complete' });
    const current = store.scenario()!;
    Object.assign(current.scenario.simulationRun!, { text: 'changed' });
    expect(store.scenarioSchedule()).toBe('unavailable');
    expect(store.scenario()).toBe(current);
    store.choose(bounded);
    store.loadResult();
    expect(store.scenarioSchedule()).toBeNull();
    expect(await store.loadScenarioComparison(store.preparedScenario()!)).toBe(true);
    expect(store.scenarioSchedule()).toMatchObject({ state: 'Incomplete' });
    expect(await store.loadScenarioStage(store.preparedScenario()!, 'scenario')).toBe(true);
    expect(store.scenarioSchedule()).toBeNull();
  });
});
