import { Digest, PreviewCase, RevisionContext, sameDigest } from './projectide.contracts';
import { AdmittedScopedSources, scopedSourcesMatch } from './projectide-scoped-sources';
import { AdmittedSimulation } from './projectide-simulation.reader';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

export interface SimulationComparisonProjection { readonly afterCaseKey: string; readonly text: string; readonly digest: Digest; }
export interface SimulationComparisonSummary {
  readonly runId: string; readonly runDigest: Digest; readonly manifestDigest: unknown; readonly inputDigest: Digest; readonly sourceDigest: Digest;
  readonly context: RevisionContext; readonly execution: string; readonly coverage: string; readonly freshness: unknown; readonly reason: string;
  readonly startTicks: string; readonly virtualTicks: string; readonly observedElapsedTicks: string;
  readonly startedTasks: number; readonly completedTasks: number; readonly eventCount: number;
}
export interface AdmittedSimulationComparison {
  readonly before: AdmittedSimulation; readonly after: AdmittedSimulation; readonly digest: Digest; readonly text: string;
  readonly beforeSummary: SimulationComparisonSummary; readonly afterSummary: SimulationComparisonSummary;
  readonly state: 'Complete' | 'Incomplete'; readonly sameInput: boolean; readonly sameLimits: boolean; readonly observedElapsedDelta: string | null;
  readonly data: Readonly<Record<string, unknown>>;
}
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const fail = (): never => { throw new Error('unsupported-simulation-comparison'); };
const obj = (v: unknown) => object(v) ? v : fail();
const fields = (v: unknown, keys: readonly string[]) => object(v) && Object.keys(v).length === keys.length && keys.every(k => Object.hasOwn(v, k));
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const digest = (v: unknown): v is Digest => fields(v, ['algorithm', 'value']) && obj(v)['algorithm'] === 'sha-256' && typeof obj(v)['value'] === 'string' && /^[a-f0-9]{64}$/u.test(obj(v)['value'] as string);
const freeze = <T>(v: T): T => { if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); } return v; };
const identity = (v: unknown): unknown => Array.isArray(v) ? v.map(identity) : object(v) ? Object.fromEntries(Object.keys(v).sort().map(k => [k, identity(v[k])])) :
  typeof v === 'string' && /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/iu.test(v) ? v.toLowerCase() : v;
const contextKey = (v: unknown) => JSON.stringify(identity(v));
function current(value: PreviewCase, run: AdmittedSimulation): boolean {
  return scopedSourcesMatch(value, run.sources) && !!value.simulationRun && sameDigest(value.simulationRun.digest, run.digest);
}
function fixedCaptures(values: readonly { value: PreviewCase; sources: AdmittedScopedSources }[]): boolean {
  const bodies = new Map<string, string>(); const digests = new Map<string, string>(); const versions = new Map<string, string>();
  const retain = (map: Map<string, string>, key: string, value: string) => { if (map.has(key)) return map.get(key) === value; map.set(key, value); return true; };
  for (const { value, sources } of values) {
    if (sources.regions.length > 257 || !retain(digests, contextKey(value.context), value.sourceDigest.value)) return false;
    for (const region of sources.regions) if (!retain(bodies, contextKey(region.context), region.sourceText)) return false;
    const pins = obj(obj(parse(sources.projection.inputText))['settings'])['authorizedExternalSnapshots'];
    if (!Array.isArray(pins) || pins.length > 256) return false;
    for (const raw of pins) {
      const pin = obj(raw); if (!digest(pin['digest']) || typeof pin['snapshotId'] !== 'string' || typeof pin['version'] !== 'string') return false;
      const context = contextKey(pin['context']); const body = pin['digest'].value;
      if (!retain(digests, context, body) || !retain(versions, JSON.stringify([pin['snapshotId'].toLowerCase(), pin['version']]), JSON.stringify([context, body]))) return false;
    }
  }
  return true;
}
function summary(value: PreviewCase, run: AdmittedSimulation): SimulationComparisonSummary {
  const manifest = obj(run.data['manifest']); const start = manifest['startTicks'] as string;
  return structuredClone({ runId: run.runId, runDigest: run.digest, manifestDigest: run.data['manifestDigest'], inputDigest: value.inputDigest, sourceDigest: value.sourceDigest,
    context: value.context, execution: run.execution, coverage: run.coverage, freshness: run.data['freshness'], reason: run.reason,
    startTicks: start, virtualTicks: run.virtualTicks, observedElapsedTicks: String(BigInt(run.virtualTicks) - BigInt(start)),
    startedTasks: run.startedTasks, completedTasks: run.completedTasks, eventCount: run.events.length });
}

/** Validate the actual Core comparison against two separately admitted fixed recordings. Executes no simulator and grants no live authority. */
export async function readSimulationComparison(beforeCase: PreviewCase, before: AdmittedSimulation, afterCase: PreviewCase,
  after: AdmittedSimulation): Promise<AdmittedSimulationComparison | null> {
  if (!current(beforeCase, before) || !current(afterCase, after)) return fail();
  if (beforeCase.simulationComparison == null) return null;
  const projection = structuredClone(beforeCase.simulationComparison);
  if (!fields(projection, ['afterCaseKey', 'text', 'digest']) || projection.afterCaseKey !== afterCase.key ||
      typeof projection.text !== 'string' || new TextEncoder().encode(projection.text).length > 65_536 || !digest(projection.digest) ||
      !equal(beforeCase.context.branch, afterCase.context.branch) || before.runId === after.runId && !sameDigest(before.digest, after.digest) ||
      !fixedCaptures([{ value: beforeCase, sources: before.sources }, { value: afterCase, sources: after.sources }]) ||
      !await verify('coglatas.local-simulation-comparison/1', projection.text, projection.digest)) return fail();
  const data = obj(parse(projection.text)); const beforeSummary = summary(beforeCase, before); const afterSummary = summary(afterCase, after);
  const complete = [before, after].every(run => run.execution === 'Completed' && run.coverage === 'Complete' && run.data['freshness'] === 'Current');
  const sameInput = sameDigest(beforeCase.inputDigest, afterCase.inputDigest);
  const sameLimits = equal(obj(before.data['manifest'])['limits'], obj(after.data['manifest'])['limits']);
  const observedElapsedDelta = complete ? String(BigInt(afterSummary.observedElapsedTicks) - BigInt(beforeSummary.observedElapsedTicks)) : null;
  if (!fields(data, ['format', 'state', 'reason', 'before', 'after', 'sameInput', 'sameLimits', 'observedElapsedDelta', 'verdict', 'evidenceKind', 'authority']) ||
      data['format'] !== 'coglatas.local-simulation-comparison/1' || data['state'] !== (complete ? 'Complete' : 'Incomplete') ||
      data['reason'] !== (complete ? 'coglatas.comparison.complete-recordings' : 'coglatas.comparison.incomplete-or-stale-recording') ||
      !equal(data['before'], beforeSummary) || !equal(data['after'], afterSummary) || data['sameInput'] !== sameInput || data['sameLimits'] !== sameLimits ||
      data['observedElapsedDelta'] !== observedElapsedDelta || data['verdict'] !== 'NotRun' || data['evidenceKind'] !== 'coglatas.simulation' || data['authority'] !== 'NotEvaluated' ||
      !current(beforeCase, before) || !current(afterCase, after) || !equal(beforeCase.simulationComparison, projection)) return fail();
  return freeze({ before, after, digest: projection.digest, text: projection.text, beforeSummary, afterSummary,
    state: complete ? 'Complete' : 'Incomplete', sameInput, sameLimits, observedElapsedDelta, data });
}
