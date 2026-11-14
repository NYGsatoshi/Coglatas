import { coreOriginEditsMatch } from './projectide-scenario-origin-edits';
import { Digest, PreviewBundle, PreviewCase, ProjectLocation, RevisionContext, PreparedSemanticDiff, sameContext, sameDigest } from './projectide.contracts';
import { readPreviewBundle } from './projectide-preview.adapter';
import { isProjectLocationShape } from './projectide-preview.validation';
import { AdmittedLocalTests } from './projectide-tests.contracts';
import { localTestsMatchCase, parseLocalTestJson, readLocalTests, verifyLocalTestDigest } from './projectide-tests.reader';

export interface PreparedScenario {
  readonly baselineCaseKey: string; readonly scenarioCaseKey: string; readonly draftCaseKey: string;
  readonly overlay: { readonly text: string; readonly digest: Digest };
  readonly appliedValues: { readonly scenario: { readonly text: string; readonly digest: Digest }; readonly draft: { readonly text: string; readonly digest: Digest } };
  readonly draftPlan: { readonly format: 'coglatas.local-scenario-draft/1'; readonly baseInputDigest: Digest;
    readonly scenarioInputDigest: Digest; readonly afterInputDigest: Digest; readonly scenarioContext: RevisionContext; readonly afterContext: RevisionContext };
}
export interface ScenarioAssumption { readonly target: ProjectLocation; readonly kind: 'TaskDuration' | 'TaskInputDefault' | 'MilestoneTarget'; readonly portId: string | null; readonly value: unknown; }
interface Triplet { readonly prepared: PreparedScenario; readonly baseline: PreviewCase; readonly scenario: PreviewCase; readonly draft: PreviewCase; }
export interface AdmittedScenario extends Triplet {
  readonly tests: readonly AdmittedLocalTests[];
  readonly assumptions: readonly ScenarioAssumption[];
  readonly changes: PreparedSemanticDiff['comparison']['changes'];
  readonly captured: string;
  readonly originals: readonly unknown[];
}
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const equal = (a: unknown, b: unknown): boolean => {
  if (a === b) return true;
  if (Array.isArray(a) && Array.isArray(b)) return a.length === b.length && a.every((item, index) => equal(item, b[index]));
  return record(a) && record(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(key => Object.hasOwn(b, key) && equal(a[key], b[key]));
};
const digest = (v: unknown): v is Digest => record(v) && v['algorithm'] === 'sha-256' && typeof v['value'] === 'string' && /^[a-f0-9]{64}$/u.test(v['value']);
const fields = (value: Record<string, unknown>, names: readonly string[]): boolean => Object.keys(value).length === names.length && names.every(name => Object.hasOwn(value, name));
const fail = (): never => { throw new Error('unsupported-prepared-scenario'); };
const cases = (value: Triplet): readonly PreviewCase[] => [value.baseline, value.scenario, value.draft];
const capture = (value: Triplet): string => JSON.stringify(cases(value).map(item => [item.preparedScenario, item.preparedDiff]));
const originals = (value: Triplet): readonly unknown[] => cases(value).flatMap(item => [item.preparedScenario, item.preparedDiff]);
function freeze<T>(value: T): T { if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); } return value; }

/** Read a fixed Core-produced journey; this neither evaluates assertions nor authors an AST. */
export function scenarioTriplet(bundle: PreviewBundle, current: PreviewCase): Triplet | null {
  if (!bundle.cases.includes(current)) return fail();
  const raw: unknown = current.preparedScenario; if (raw == null) return null;
  if (!record(raw) || !fields(raw, ['baselineCaseKey', 'scenarioCaseKey', 'draftCaseKey', 'overlay', 'draftPlan', 'appliedValues']) || !record(raw['overlay']) || !fields(raw['overlay'], ['text', 'digest']) || typeof raw['overlay']['text'] !== 'string' ||
      new TextEncoder().encode(raw['overlay']['text']).byteLength > 300_000 || !digest(raw['overlay']['digest']) || !record(raw['draftPlan']) || !record(raw['appliedValues']) ||
      !fields(raw['appliedValues'], ['scenario', 'draft']) || !boundedEnvelope(raw['appliedValues']['scenario']) || !boundedEnvelope(raw['appliedValues']['draft'])) return fail();
  const baseline = bundle.cases.find(x => x.key === raw['baselineCaseKey']); const scenario = bundle.cases.find(x => x.key === raw['scenarioCaseKey']);
  const draft = bundle.cases.find(x => x.key === raw['draftCaseKey']); const p = raw['draftPlan'];
  if (!fields(p, ['format', 'baseInputDigest', 'scenarioInputDigest', 'afterInputDigest', 'scenarioContext', 'afterContext'])) return fail();
  if (!baseline || !scenario || !draft || new Set([baseline, scenario, draft]).size !== 3 || ![baseline, scenario, draft].includes(current) ||
      baseline.context.kind === 'coglatas.scenario' || scenario.context.kind !== 'coglatas.scenario' || draft.context.kind !== 'coglatas.candidate' ||
      !sameContext(scenario.context.baseContext, baseline.context) || !sameDigest(scenario.context.overlayDigest, raw['overlay']['digest']) ||
      !equal(baseline.context.branch, draft.context.branch) ||
      ![baseline, scenario, draft].every(x => x.freshness === 'Current' && sameContext(x.context, x.desiredContext) && equal(x.preparedScenario, raw)) ||
      p['format'] !== 'coglatas.local-scenario-draft/1' || !digest(p['baseInputDigest']) || !sameDigest(p['baseInputDigest'], baseline.inputDigest) ||
      !digest(p['scenarioInputDigest']) || !sameDigest(p['scenarioInputDigest'], scenario.inputDigest) || !digest(p['afterInputDigest']) || !sameDigest(p['afterInputDigest'], draft.inputDigest) ||
      !equal(p['scenarioContext'], scenario.context) || !equal(p['afterContext'], draft.context)) return fail();
  if (baseline.context.kind === 'coglatas.committed') {
    if (!sameContext(draft.context.baseRevision, baseline.context)) return fail();
  } else if (baseline.context.proposalId !== draft.context.proposalId || baseline.context.candidateRevisionId === draft.context.candidateRevisionId ||
      !sameContext(baseline.context.baseRevision, draft.context.baseRevision) || !sameContext(baseline.context.capturedTargetHead, draft.context.capturedTargetHead)) return fail();
  if (!baseline.preparedDiff || baseline.preparedDiff.afterCaseKey !== scenario.key || !scenario.preparedDiff || scenario.preparedDiff.afterCaseKey !== draft.key ||
      baseline.preparedDiff.comparison.state !== 'Complete' || scenario.preparedDiff.comparison.state !== 'Complete' ||
      !scenario.preparedDiff.comparison.noChanges || scenario.preparedDiff.comparison.changes.length !== 0) return fail();
  return { prepared: raw as unknown as PreparedScenario, baseline, scenario, draft };
}

export function scenarioMatches(bundle: PreviewBundle, current: PreviewCase, admitted: AdmittedScenario): boolean {
  try {
    const next = scenarioTriplet(bundle, current);
    return !!next && next.baseline === admitted.baseline && next.scenario === admitted.scenario && next.draft === admitted.draft &&
      capture(next) === admitted.captured && originals(next).every((value, index) => value === admitted.originals[index]) &&
      cases(next).every((value, index) => localTestsMatchCase(value, admitted.tests[index]));
  } catch { return false; }
}

export async function readPreparedScenario(bundle: PreviewBundle, current: PreviewCase): Promise<AdmittedScenario | null> {
  readPreviewBundle(bundle); const triplet = scenarioTriplet(bundle, current); if (!triplet) return null;
  const captured = capture(triplet); const owned = originals(triplet); const tests = await Promise.all(cases(triplet).map(readLocalTests));
  if (tests.some(x => !x)) return fail();
  const { baseline, scenario, draft, prepared } = triplet;
  const overlay = parseLocalTestJson(prepared.overlay.text);
  if (!record(overlay) || !fields(overlay, ['format', 'baseInputDigest', 'baseSourceDigest', 'baseContext', 'overrides']) || overlay['format'] !== 'coglatas.local-scenario-overlay/1' || !equal(overlay['baseContext'], baseline.context) ||
      !equal(overlay['baseInputDigest'], baseline.inputDigest) || !equal(overlay['baseSourceDigest'], baseline.sourceDigest) ||
      !Array.isArray(overlay['overrides']) || overlay['overrides'].length === 0 || overlay['overrides'].length > 128) return fail();
  let bytes = 0; const assumptions: ScenarioAssumption[] = []; const keys = new Set<string>();
  for (const item of overlay['overrides']) {
    if (!record(item) || !fields(item, ['target', 'kind', 'portId', 'value']) || !isProjectLocationShape(item['target']) || !sameContext(item['target'].context, baseline.context) ||
        !item['target'].entityId || item['target'].relationId || item['target'].propertyPath !== '' || item['target'].textSpan ||
        !['TaskDuration', 'TaskInputDefault', 'MilestoneTarget'].includes(String(item['kind'])) || !Object.hasOwn(item, 'value') ||
        !(item['portId'] === null || typeof item['portId'] === 'string')) return fail();
    const target = item['target']; const key = target.documentId + '/' + target.entityId + '/' + item['kind'] + '/' + item['portId'];
    bytes += new TextEncoder().encode(JSON.stringify(item['value']) + JSON.stringify(target)).byteLength;
    if (bytes > 262_144 || keys.has(key)) return fail(); keys.add(key);
    const old = baseline.declarations.filter(x => x.identity === target.entityId && x.location.documentId === target.documentId);
    const after = scenario.declarations.filter(x => x.identity === target.entityId && x.location.documentId === target.documentId);
    if (old.length !== 1 || after.length !== 1 || old[0].status !== 'Decoded' || after[0].status !== 'Decoded' || old[0].kind !== after[0].kind) return fail();
    assumptions.push(item as unknown as ScenarioAssumption);
  }
  await verifyAppliedValues(triplet, assumptions, scenario, prepared.appliedValues.scenario);
  await verifyAppliedValues(triplet, assumptions, draft, prepared.appliedValues.draft);
  for (const change of baseline.preparedDiff!.comparison.changes) {
    if (change.recordType !== 'entity' || !assumptions.some(item => item.target.entityId === change.identity &&
        (change.path === (item.kind === 'TaskInputDefault' ? '/payload/ports' : item.kind === 'TaskDuration' ? '/payload/duration' : '/payload/target') ||
         change.path.startsWith((item.kind === 'TaskInputDefault' ? '/payload/ports' : item.kind === 'TaskDuration' ? '/payload/duration' : '/payload/target') + '/')))) return fail();
  }
  // An array-level Core change must not hide another port/type/cardinality edit.
  for (const id of new Set(assumptions.map(item => item.target.entityId))) {
    const before = structuredClone(baseline.declarations.find(item => item.identity === id)!.data);
    const after = structuredClone(scenario.declarations.find(item => item.identity === id)!.data);
    for (const item of assumptions.filter(item => item.target.entityId === id)) for (const node of [before, after]) {
      const payload = node['payload']; if (!record(payload)) return fail();
      if (item.kind === 'TaskInputDefault') {
        if (!Array.isArray(payload['ports'])) return fail();
        const port = payload['ports'].find(value => record(value) && value['portId'] === item.portId);
        if (!record(port)) return fail(); delete port['defaultValue'];
      } else delete payload[item.kind === 'TaskDuration' ? 'duration' : 'target'];
    }
    if (!equal(before, after)) return fail();
  }
  const settings = (value: PreviewCase): Record<string, unknown> => { const manifest = value.manifest['manifest']; if (!record(manifest) || !record(manifest['settings'])) return fail(); return manifest['settings']; };
  const baseSettings = settings(baseline); const scenarioSettings = settings(scenario); const scenarioBinding = scenarioSettings['scenario'];
  if (!equal(baseSettings['scenario'], { kind: 'coglatas.none' }) || !record(scenarioBinding) || !equal(scenarioBinding['baseContext'], baseline.context) || !equal(scenarioBinding['overlayDigest'], prepared.overlay.digest) ||
      !equal(scenarioBinding['assumptions'], overlay['overrides']) || !equal(settings(draft), baseSettings) ||
      !equal({ ...scenarioSettings, scenario: baseSettings['scenario'] }, baseSettings) ||
      !(await verifyLocalTestDigest('coglatas.local-scenario-overlay/1', prepared.overlay.text, prepared.overlay.digest))) return fail();
  readPreviewBundle(bundle);
  const admitted: AdmittedScenario = { ...triplet, tests: Object.freeze(tests as AdmittedLocalTests[]), assumptions: freeze(structuredClone(assumptions)),
    changes: freeze(structuredClone(baseline.preparedDiff!.comparison.changes)), captured, originals: Object.freeze(owned) };
  if (!scenarioMatches(bundle, current, admitted)) return fail();
  return Object.freeze(admitted);
}

function boundedEnvelope(value: unknown): value is { text: string; digest: Digest } {
  return record(value) && fields(value, ['text', 'digest']) && typeof value['text'] === 'string' &&
    new TextEncoder().encode(value['text']).byteLength <= 4_194_304 && digest(value['digest']);
}
function targetValue(stage: PreviewCase, item: ScenarioAssumption): unknown {
  const declarations = stage.declarations.filter(row => row.identity === item.target.entityId && row.location.documentId === item.target.documentId);
  if (declarations.length !== 1 || declarations[0].status !== 'Decoded') return fail();
  const declaration = declarations[0]; const payload = declaration.data['payload']; if (!record(payload)) return fail();
  if (item.kind === 'TaskInputDefault') {
    if (declaration.kind !== 'coglatas.task' || typeof item.portId !== 'string' || !Array.isArray(payload['ports'])) return fail();
    const ports = payload['ports'].filter(port => record(port) && port['portId'] === item.portId);
    if (ports.length !== 1 || !record(ports[0]) || ports[0]['direction'] !== 'coglatas.input') return fail();
    return ports[0]['defaultValue'];
  }
  if (item.portId !== null || declaration.kind !== (item.kind === 'TaskDuration' ? 'coglatas.task' : 'coglatas.milestone')) return fail();
  return payload[item.kind === 'TaskDuration' ? 'duration' : 'target'];
}
/** Check original Core projection bytes against both the declared overlay and the exact applied Source. */
async function verifyAppliedValues(triplet: Triplet, assumptions: readonly ScenarioAssumption[], stage: PreviewCase, envelope: { text: string; digest: Digest }): Promise<void> {
  const data = parseLocalTestJson(envelope.text);
  if (!record(data) || !fields(data, ['format', 'overlayDigest', 'baseInputDigest', 'scenarioInputDigest', 'afterInputDigest', 'afterSourceDigest', 'afterContext', 'values']) ||
      data['format'] !== 'coglatas.local-scenario-applied-values/1' || !equal(data['overlayDigest'], triplet.prepared.overlay.digest) ||
      !equal(data['baseInputDigest'], triplet.baseline.inputDigest) || !equal(data['scenarioInputDigest'], triplet.scenario.inputDigest) ||
      !equal(data['afterInputDigest'], stage.inputDigest) || !equal(data['afterSourceDigest'], stage.sourceDigest) || !equal(data['afterContext'], stage.context) ||
      !Array.isArray(data['values']) || data['values'].length !== assumptions.length) return fail();
  let bytes = 0;
  for (const [index, value] of data['values'].entries()) {
    const item = assumptions[index];
    if (!record(value) || !fields(value, ['target', 'kind', 'portId', 'assumption', 'value', 'originEdits']) ||
        !equal(value['target'], { ...item.target, context: stage.context }) || value['kind'] !== item.kind || value['portId'] !== item.portId ||
        !boundedEnvelope(value['assumption']) || !boundedEnvelope(value['value']) ||
        !coreOriginEditsMatch(item.value, parseLocalTestJson(value['value'].text), value['originEdits'], triplet.baseline.context, stage.context)) return fail();
    const declared = value['assumption']; const applied = value['value'];
    bytes += new TextEncoder().encode(applied.text + JSON.stringify(value['target'])).byteLength;
    if (bytes > 262_144 || !equal(parseLocalTestJson(declared.text), item) || !equal(parseLocalTestJson(applied.text), targetValue(stage, item)) ||
        !(await verifyLocalTestDigest('coglatas.local-scenario-assumption/1', declared.text, declared.digest)) ||
        !(await verifyLocalTestDigest('coglatas.local-scenario-value/1', applied.text, applied.digest))) return fail();
  }
  if (!(await verifyLocalTestDigest('coglatas.local-scenario-applied-values/1', envelope.text, envelope.digest))) return fail();
}
