import { Diagnostic, Digest, Identity, PreviewCase, ProjectLocation, RevisionContext, SourceRecord, sameContext } from './projectide.contracts';
import { isProjectLocationShape, isRevisionContextShape } from './projectide-preview.validation';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify, readSnapshotMaterialization } from './projectide-tests.reader';
import { validatePreviewCaseBindings } from './projectide-preview.adapter';

interface Frame { readonly text: string; readonly digest: Digest; }
export interface ScopedSourceProjection {
  readonly format: 'coglatas.local-scoped-source-projection/1' | 'coglatas.local-scoped-source-projection/2'; readonly inputText: string;
  readonly result: Frame; readonly analysis: Frame; readonly binding: Frame; readonly graph: Frame; readonly ir: Frame; readonly sourceMap: Frame;
  readonly publication?: { readonly semanticModel: Frame; readonly ir: Frame; readonly sourceMap: Frame; readonly plan?: Frame };
  readonly regions: readonly { readonly context: RevisionContext; readonly ir: Frame; readonly sourceMap: Frame; readonly semanticModel?: Frame }[];
}
export interface ScopedSourceRegion {
  readonly context: RevisionContext; readonly sourceText: string; readonly records: readonly SourceRecord[]; readonly diagnostics: readonly Diagnostic[];
}
export interface AdmittedScopedSources {
  readonly basis: string; readonly projection: ScopedSourceProjection; readonly regions: readonly ScopedSourceRegion[]; readonly publicationVerified: boolean; readonly plan: { readonly definition: Record<string, unknown>; readonly digest: Digest; readonly taskCount: number; readonly dependencyCount: number } | null; readonly planNotProducedReason: string | null;
}
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const fail = (): never => { throw new Error('unsupported-scoped-sources'); };
const obj = (v: unknown): Record<string, unknown> => object(v) ? v : fail();
const list = (v: unknown, max = 100_000): unknown[] => Array.isArray(v) && v.length <= max ? v : fail();
const fields = (v: unknown, keys: readonly string[]) => Object.keys(obj(v)).length === keys.length && keys.every(k => Object.hasOwn(obj(v), k));
const dig = (v: unknown): v is Digest => object(v) && fields(v, ['algorithm', 'value']) && v['algorithm'] === 'sha-256' && typeof v['value'] === 'string' && /^[a-f0-9]{64}$/u.test(v['value']);
const id = (v: unknown): v is string => typeof v === 'string' && /^[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}$/u.test(v);
const basis = (value: PreviewCase) => JSON.stringify(value);
const freeze = <T>(value: T): T => { if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); } return value; };
export function scopedSourcesMatch(value: PreviewCase, admitted: AdmittedScopedSources): boolean {
  return value.freshness === 'Current' && sameContext(value.context, value.desiredContext) && basis(value) === admitted.basis;
}
async function frame(value: unknown, domain: string): Promise<Record<string, unknown>> {
  const v = obj(value);
  if (!fields(v, ['text', 'digest']) || typeof v['text'] !== 'string' || new TextEncoder().encode(v['text']).length > 4_194_304 || !dig(v['digest']) ||
      !await verify(domain, v['text'], v['digest'])) return fail();
  return obj(parse(v['text']));
}
async function nodeIdentity(context: RevisionContext, entity: string): Promise<{ id: string; digest: Digest }> {
  const bytes = new Uint8Array(await crypto.subtle.digest('SHA-256', new TextEncoder().encode(JSON.stringify([
    'coglatas.id/IRNodeId/v1', 1, [{ entityId: entity, projectId: context.branch.projectId, tenantId: context.branch.tenantId }, 'coglatas.declaration', 'coglatas.primary']]))));
  const value = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join(''); bytes[6] = (bytes[6] & 15) | 128; bytes[8] = (bytes[8] & 63) | 128;
  const hex = Array.from(bytes.subarray(0, 16), b => b.toString(16).padStart(2, '0')).join('');
  return { id: hex.slice(0, 8) + '-' + hex.slice(8, 12) + '-' + hex.slice(12, 16) + '-' + hex.slice(16, 20) + '-' + hex.slice(20), digest: { algorithm: 'sha-256', value } };
}
function sourceRecords(source: Record<string, unknown>, context: RevisionContext): SourceRecord[] {
  const docs = list(source['documents'], 1024).map(obj); const docIds = new Set(docs.map(d => d['documentId']));
  if (!docs.length || docIds.size !== docs.length || [...docIds].some(v => !id(v))) return fail();
  const rows = [...docs.map(doc => ({ type: 'entity', data: obj(doc['root']), document: doc['documentId'] })),
    ...list(source['declarations']).map(obj).map(data => ({ type: 'entity', data, document: data['documentId'] })),
    ...list(source['relations']).map(obj).map(data => ({ type: 'relation', data, document: data['documentId'] }))];
  const seen = new Set<string>();
  return rows.map(row => {
    const identity = row.data[row.type === 'entity' ? 'entityId' : 'relationId'];
    if (!id(identity) || !docIds.has(row.document) || typeof row.data['kind'] !== 'string' || seen.has(row.type + identity)) return fail();
    seen.add(row.type + identity);
    const location = { format: 'coglatas.project-location/1', schemaId: 'coglatas.project-location', schemaVersion: 1, canonicalEncodingVersion: 1,
      context, documentId: row.document, [row.type === 'entity' ? 'entityId' : 'relationId']: identity, propertyPath: '' };
    if (!isProjectLocationShape(location)) return fail();
    const common = { kind: row.data['kind'], status: 'Decoded' as const, data: row.data, location };
    return row.type === 'entity' ? { ...common, recordType: 'entity', identity: identity as Identity<'Entity'> } : { ...common, recordType: 'relation', identity: identity as Identity<'Relation'> };
  });
}

/** Original-byte/hash/membership reader only; .NET owns semantic evaluation and current runtime authority is separate. */
export async function readScopedSources(value: PreviewCase): Promise<AdmittedScopedSources | null> {
  if (value.scopedSources == null) return null;
  validatePreviewCaseBindings(value);
  const captured = basis(value); if (new TextEncoder().encode(captured).length > 16_777_216) return fail();
  const v = structuredClone(value); const projection = v.scopedSources!;
  const published = projection.format === 'coglatas.local-scoped-source-projection/2';
  if (!fields(projection, ['format', 'inputText', 'result', 'analysis', 'binding', 'graph', 'ir', 'sourceMap', 'regions', ...(published ? ['publication'] : [])]) ||
      !['coglatas.local-scoped-source-projection/1', 'coglatas.local-scoped-source-projection/2'].includes(projection.format) || new TextEncoder().encode(JSON.stringify(projection)).length > 4_194_304 ||
      typeof projection.inputText !== 'string' || !await verify('coglatas.compiler-input/1', projection.inputText, v.inputDigest) || !await readSnapshotMaterialization(v)) return fail();
  const input = obj(parse(projection.inputText)); const settings = obj(input['settings']);
  if (!equal(input, v.manifest['manifest']) || !equal(input['sourceContext'], v.context) || !equal(input['sourceDigest'], v.sourceDigest)) return fail();
  const [result, analysis, binding, graph, ir, map] = await Promise.all([
    frame(projection.result, 'coglatas.compiler-result/1'), frame(projection.analysis, 'coglatas.compiler-dependency-analysis/1'),
    frame(projection.binding, 'coglatas.semantic-binding-closure/1'), frame(projection.graph, 'coglatas.compiler-graph-closure/1'),
    frame(projection.ir, 'coglatas.project-ir-closure/1'), frame(projection.sourceMap, 'coglatas.semantic-closure-source-map/1')]);
  if (!equal(projection.result.digest, v.resultDigest) || !equal(result, v.result) || result['execution'] !== 'coglatas.completed' ||
      !list(result['stages'], 32).some(s => obj(s)['stageId'] === 'coglatas.dependency-analysis' && obj(s)['execution'] === 'coglatas.completed' &&
        obj(s)['coverage'] === 'coglatas.complete' && list(obj(s)['outputs'], 1024).some(d => equal(d, projection.analysis.digest)))) return fail();
  for (const [data, format] of [[analysis, 'coglatas.compiler-dependency-analysis/1'], [binding, 'coglatas.semantic-binding-closure/1'], [graph, 'coglatas.compiler-graph-closure/1'],
    [ir, 'coglatas.project-ir-closure/1'], [map, 'coglatas.semantic-closure-source-map/1']] as const)
    if (data['format'] !== format || !equal(data === binding ? data['inputDigest'] : data['rootInputDigest'], v.inputDigest)) return fail();
  if (analysis['state'] !== 'Evaluated' || binding['compilerInputId'] !== v.inputId || binding['state'] !== 'Complete' || binding['scope'] !== 'coglatas.declared-snapshots' ||
      graph['state'] !== 'Evaluated' || graph['structuralValidated'] !== true || graph['scope'] !== 'coglatas.declared-snapshots' ||
      !equal(analysis['bindingClosureDigest'], projection.binding.digest) || !equal(analysis['globalGraph'], graph) || !equal(analysis['scopedIrDigest'], projection.ir.digest) ||
      !equal(graph['bindingClosureDigest'], projection.binding.digest) || !equal(ir['sourceMapDigest'], projection.sourceMap.digest) ||
      [ir, map].some(d => d['compilerInputId'] !== v.inputId || !equal(d['bindingClosureDigest'], projection.binding.digest) || !equal(d['graphDigest'], projection.graph.digest))) return fail();
  const sourceBodies: { context: RevisionContext; text: unknown; digest: unknown }[] = [{ context: v.context, text: v.sourceText, digest: v.sourceDigest }, ...(v.materializedSnapshots == null ? [] : list(obj(v.materializedSnapshots)['entries'], 32)).map(obj).map(e =>
    ({ context: e['context'] as RevisionContext, text: obj(e['source'])['text'], digest: obj(e['source'])['digest'] }))];
  const pins = list(settings['authorizedExternalSnapshots'], 32).map(obj);
  const unique = (items: readonly unknown[]) => items.filter((item, i) => items.findIndex(other => equal(item, other)) === i);
  const contexts = unique([v.context, ...pins.map(pin => pin['context'])]);
  const bRegions = list(binding['regions'], 33).map(obj); const aRegions = list(analysis['regions'], 33).map(obj);
  const iRegions = list(ir['regions'], 33).map(obj); const mRegions = list(map['regions'], 33).map(obj); const transported = list(projection.regions, 33).map(obj);

  if ([bRegions, aRegions, iRegions, mRegions, transported].some(rows => rows.length !== contexts.length || unique(rows.map(row => row['context'] ?? obj(row['region'])['context'])).length !== contexts.length)) return fail();
  let publishedModel: Record<string, unknown> | null = null;
  let plan: { definition: Record<string, unknown>; digest: Digest; taskCount: number; dependencyCount: number } | null = null; let planNotProducedReason: string | null = null;
  if (published) {
    const payload = projection.publication!;
    if (!fields(payload, ['semanticModel', 'ir', 'sourceMap', ...(payload.plan ? ['plan'] : [])]) || obj(settings['semantics'])['version'] !== '2' || obj(settings['ir'])['version'] !== '2') return fail();
    const [model, producedIr, producedMap] = await Promise.all([frame(payload.semanticModel, 'coglatas.semantic-model/2'), frame(payload.ir, 'coglatas.project-ir/2'), frame(payload.sourceMap, 'coglatas.semantic-source-map/2')]);
    for (const [data, format] of [[model, 'coglatas.semantic-model/2'], [producedIr, 'coglatas.project-ir/2'], [producedMap, 'coglatas.semantic-source-map/2']] as const)
      if (data['format'] !== format || data['compilerInputId'] !== v.inputId || !equal(data['inputDigest'], v.inputDigest) || !equal(data['sourceDigest'], v.sourceDigest) || !equal(data['closureDigest'], projection.ir.digest)) return fail();
    if (!equal(producedIr['semanticModelDigest'], payload.semanticModel.digest) || !equal(producedMap['semanticModelDigest'], payload.semanticModel.digest) ||
        !equal(producedIr['sourceMapDigest'], payload.sourceMap.digest) || !equal(producedIr['regions'], ir['regions']) || !equal(producedMap['regions'], map['regions'])) return fail();
    const expected = [['coglatas.semantic-model', 'coglatas.semantic-model', payload.semanticModel], ['coglatas.ir', 'coglatas.project-ir', payload.ir], ['coglatas.source-map', 'coglatas.semantic-source-map', payload.sourceMap]] as const;
    const artifacts = list(result['artifacts'], 32).map(obj);
    if (artifacts.length !== expected.length || expected.some(([role, schemaId, data]) => artifacts.filter(a => a['role'] === role && a['schemaId'] === schemaId && a['version'] === 2 && equal(a['inputDigest'], v.inputDigest) && equal(a['digest'], data.digest)).length !== 1)) return fail();
    const stages = list(result['stages'], 32).map(obj).filter(stage => stage['stageId'] === 'coglatas.scoped-publication');
    if (stages.length !== 1 || stages[0]['version'] !== 2 || stages[0]['execution'] !== 'coglatas.completed' || stages[0]['coverage'] !== 'coglatas.complete' ||
        !equal(stages[0]['inputDigest'], v.inputDigest) || list(stages[0]['outputs'], 3).length !== 3 || expected.some(([, , data]) => !list(stages[0]['outputs'], 3).some(digest => equal(digest, data.digest)))) return fail();
    const requestedPlan = list(settings['targets'], 256).includes('coglatas.execution-plan-definition');
    const production = obj(producedIr['executionPlan']);
    const planStages = list(result['stages'], 32).map(obj).filter(stage => stage['stageId'] === 'coglatas.plan-definition');
    if (!requestedPlan) {
      if (payload.plan || planStages.length || !equal(production, { kind: 'coglatas.not-produced', reason: 'coglatas.analysis-and-plan-lowering-required' })) return fail();
    } else {
      if (planStages.length !== 1 || planStages[0]['version'] !== 1 || !equal(planStages[0]['inputDigest'], v.inputDigest)) return fail();
      if (production['kind'] === 'coglatas.produced') {
        const definition = await frame(payload.plan, 'coglatas.execution-plan-definition/1');
        const digest = payload.plan!.digest; const engine = obj(settings['scheduler']);
        if (!fields(production, ['kind', 'digest', 'definition']) || !equal(definition, production['definition']) || !equal(digest, production['digest']) ||
            !fields(definition, ['format', 'compilerInputId', 'inputDigest', 'sourceDigest', 'closureDigest', 'engine', 'regions', 'tasks', 'dependencies', 'definitionOrder', 'execution']) || definition['format'] !== 'coglatas.execution-plan-definition/1' || definition['compilerInputId'] !== v.inputId || !equal(definition['inputDigest'], v.inputDigest) ||
            !equal(definition['sourceDigest'], v.sourceDigest) || !equal(definition['closureDigest'], projection.ir.digest) || definition['execution'] !== 'coglatas.not-instantiated' ||
            !fields(engine, ['kind', 'artifact']) || !fields(definition['engine'], ['artifactId', 'version', 'digest']) || !dig(obj(definition['engine'])['digest']) || engine['kind'] !== 'coglatas.plan-lowering' || !equal(definition['engine'], engine['artifact']) || obj(definition['engine'])['artifactId'] !== 'coglatas.elapsed-finish-to-start-plan-lowerer' || obj(definition['engine'])['version'] !== '1' ||
            planStages[0]['execution'] !== 'coglatas.completed' || planStages[0]['coverage'] !== 'coglatas.complete' || !equal(planStages[0]['outputs'], [digest])) return fail();
        plan = { definition, digest, taskCount: list(definition['tasks']).length, dependencyCount: list(definition['dependencies']).length };
      } else {
        if (payload.plan || !fields(production, ['kind', 'reason']) || production['kind'] !== 'coglatas.not-produced' || typeof production['reason'] !== 'string' ||
            !production['reason'].startsWith('coglatas.plan.') || planStages[0]['execution'] !== 'coglatas.not-run' || planStages[0]['coverage'] !== 'coglatas.none' ||
            !equal(planStages[0]['outputs'], []) || planStages[0]['limitation'] !== production['reason'] || result['coverage'] === 'coglatas.complete' || result['verdict'] === 'coglatas.pass') return fail();
        planNotProducedReason = production['reason'];
      }
    }
    const regions = list(model['regions'], 33).map(obj);
    if (regions.length !== contexts.length || contexts.some(context => regions.filter(region => equal(region['context'], context)).length !== 1)) return fail();
    publishedModel = model;
  }
  const admitted: ScopedSourceRegion[] = []; const lowered: { region: ScopedSourceRegion; ir: Record<string, unknown> }[] = [];
  for (const context of contexts) {
    if (!isRevisionContextShape(context)) return fail();
    const one = (rows: Record<string, unknown>[], nested = false) => { const found = rows.filter(row => equal(nested ? obj(row['region'])['context'] : row['context'], context)); return found.length === 1 ? found[0] : fail(); };
    const body = sourceBodies.filter(body => equal(body.context, context)); if (body.length !== 1 || typeof body[0].text !== 'string' || !dig(body[0].digest)) return fail();
    const source = obj(parse(body[0].text));
    if (source['format'] !== 'coglatas.project-source/1' || source['schemaVersion'] !== 1 || source['canonicalEncodingVersion'] !== 1 ||
        source['tenantId'] !== context.branch.tenantId || source['projectId'] !== context.branch.projectId || !equal(source['context'], context) || !await verify('coglatas.project-source/1', body[0].text, body[0].digest)) return fail();
    if (sameContext(context, v.context) && (!equal(source, v.source) || !equal(input['sourceManifest'], source['manifest']))) return fail();
    const bs = obj(one(bRegions, true)['region']); const ar = obj(one(aRegions, true)['region']);
    if (!equal(bs['rootInputDigest'], v.inputDigest) || !equal(ar['rootInputDigest'], v.inputDigest)) return fail(); const is = one(iRegions); const ms = one(mRegions); const t = one(transported);
    const [regionalIr, regionalMap] = await Promise.all([frame(t['ir'], 'coglatas.project-ir-region/1'), frame(t['sourceMap'], 'coglatas.semantic-region-source-map/1')]);
    if (regionalIr['format'] !== 'coglatas.project-ir-region/1' || regionalMap['format'] !== 'coglatas.semantic-region-source-map/1' ||
        regionalMap['compilerInputId'] !== regionalIr['compilerInputId'] || !equal(regionalIr['sourceDigest'], body[0].digest) ||
        !equal(regionalIr['sourceMapDigest'], obj(t['sourceMap'])['digest']) || !equal(is['ir'], regionalIr) || !equal(ms['sourceMap'], regionalMap) ||
        !equal(is['irDigest'], obj(t['ir'])['digest']) || !equal(ms['sourceMapDigest'], obj(t['sourceMap'])['digest']) ||
        !equal(ar['ir'], regionalIr) || !equal(ar['sourceMap'], regionalMap) || !equal(ar['bindingRegionDigest'], one(bRegions, true)['digest']) ||
        !equal(is['bindingRegionDigest'], ar['bindingRegionDigest']) || !equal(bs['sourceDigest'], body[0].digest) ||
        !equal(ar['sourceDigest'], body[0].digest) || !equal(is['sourceDigest'], body[0].digest) || !equal(ms['sourceDigest'], body[0].digest) || bs['bindingComplete'] !== true) return fail();
    if (!sameContext(context, v.context)) {
      const matches = pins.filter(pin => equal(pin['context'], context) && equal(bs['snapshot'], { snapshotId: pin['snapshotId'], version: pin['version'] }));
      if (bs['isRoot'] !== false || matches.length !== 1 || !equal(matches[0]['digest'], body[0].digest)) return fail();
    } else if (bs['snapshot'] !== null || bs['isRoot'] !== true) return fail();
    if (publishedModel) {
      if (!fields(t, ['context', 'ir', 'sourceMap', 'semanticModel'])) return fail();
      const model = await frame(t['semanticModel'], 'coglatas.semantic-model/1');
      const publishedRegion = list(publishedModel['regions'], 33).map(obj).find(region => equal(region['context'], context))!;
      if (!equal(regionalIr['semanticModelDigest'], obj(t['semanticModel'])['digest']) || !equal(publishedRegion['semanticModelDigest'], obj(t['semanticModel'])['digest']) ||
          !equal(publishedRegion['semanticModel'], model) || !equal(publishedRegion['sourceDigest'], body[0].digest) || model['compilerInputId'] !== regionalIr['compilerInputId']) return fail();
      const originalRecords = sourceRecords(source, context);
      const ordered = (type: 'entity' | 'relation') => originalRecords.filter(row => row.recordType === type).sort((a, b) => (a.identity < b.identity ? -1 : a.identity > b.identity ? 1 : 0)).map(row => row.data);
      if (!equal(model['declarations'], ordered('entity')) || !equal(model['relations'], ordered('relation')) ||
          !equal(model['references'], list(bs['references'], 4096).map(reference => {
            const row = obj(reference); if (row['state'] !== 'Resolved') return fail();
            return { origin: row['origin'], state: 'coglatas.resolved', symbolKey: row['symbolKey'] };
          }))) return fail();
    } else if (!fields(t, ['context', 'ir', 'sourceMap'])) return fail();
    const records = sourceRecords(source, context); const nodes = list(regionalIr['nodes']).map(obj); const edges = list(regionalIr['relations']).map(obj);
    if (nodes.length !== records.filter(r => r.recordType === 'entity').length || edges.length !== records.filter(r => r.recordType === 'relation').length) return fail();
    for (const row of records) {
      const entries = list(regionalMap[row.recordType === 'entity' ? 'nodes' : 'relations']).map(obj).filter(entry => equal(obj(entry['location']), row.location));
      if (entries.length !== 1) return fail();
      if (row.recordType === 'entity') {
        const found = nodes.filter(node => node['sourceEntityId'] === row.identity); const key = await nodeIdentity(context, row.identity);
        if (found.length !== 1 || found[0]['irNodeId'] !== key.id || entries[0]['irNodeId'] !== key.id || !equal(found[0]['derivationDigest'], key.digest) ||
            found[0]['kind'] !== row.kind || !equal(found[0]['payload'], row.data['payload'])) return fail();
      } else if (edges.filter(edge => edge['relationId'] === row.identity).length !== 1 || entries[0]['relationId'] !== row.identity) return fail();
    }
    if (list(regionalMap['nodes']).length !== nodes.length || list(regionalMap['relations']).length !== edges.length) return fail();
    // Diagnostic Data has no standalone InputId field; the verified regional receipt binds these exact rows.
    const diagnostics = list(ar['diagnostics'], 4096).map(item => {
      const diagnostic = obj(item);
      if (ar['format'] !== 'coglatas.compiler-dependency-region/1' || !id(diagnostic['diagnosticId']) ||
          typeof diagnostic['code'] !== 'string' || typeof diagnostic['ruleId'] !== 'string' ||
          !Number.isSafeInteger(diagnostic['ruleVersion']) || !Number.isSafeInteger(diagnostic['stageOrdinal']) ||
          !isProjectLocationShape(diagnostic['primaryLocation']) || !sameContext(diagnostic['primaryLocation'].context, context) ||
          !['coglatas.error', 'coglatas.warning', 'coglatas.info', 'coglatas.suggestion'].includes(String(diagnostic['severity'])) || !object(diagnostic['evidence'])) return fail();
      return diagnostic as unknown as Diagnostic;
    });
    const region = { context, sourceText: body[0].text, records, diagnostics }; admitted.push(region); lowered.push({ region, ir: regionalIr });
  }
  for (const { region, ir: regionalIr } of lowered) for (const edge of list(regionalIr['relations']).map(obj)) {
    const original = region.records.find(row => row.recordType === 'relation' && row.identity === edge['relationId'])!;
    if (edge['kind'] !== original.kind || edge['descriptorVersion'] !== obj(original.data['descriptor'])['version'] || !equal(edge['payload'], original.data['payload'])) return fail();
    for (const side of ['source', 'target']) {
      const sourceEndpoint = obj(original.data[side]); const reference = obj(sourceEndpoint['reference']); const endpoint = obj(edge[side]);
      const context = reference['context']; if (!isRevisionContextShape(context) || !id(reference['entityId'])) return fail();
      const owner = admitted.find(r => sameContext(r.context, context)); const record = owner?.records.find(r => r.recordType === 'entity' && r.identity === reference['entityId']);
      if (!record || !equal(endpoint['context'], context) || endpoint['irNodeId'] !== (await nodeIdentity(context, reference['entityId'])).id ||
          endpoint['portId'] !== (sourceEndpoint['portId'] ?? null) || sourceEndpoint['portId'] && !list(obj(record.data['payload'])['ports']).some(p => obj(p)['portId'] === sourceEndpoint['portId'])) return fail();
    }
  }
  if (plan) {
    // Contexts contain only bounded protocol keys. This key is an index, never a content digest or semantic evaluator.
    const orderedContext = (value: unknown): unknown => Array.isArray(value) ? value.map(orderedContext) : object(value) ? Object.fromEntries(Object.keys(value).sort().map(key => [key, orderedContext(value[key])])) : value;
    const key = (context: unknown, id: unknown) => JSON.stringify([orderedContext(context), id]);
    const indexed = (rows: Record<string, unknown>[], makeKey: (row: Record<string, unknown>) => string) => {
      const result = new Map<string, Record<string, unknown>>();
      for (const row of rows) { const id = makeKey(row); if (result.has(id)) return fail(); result.set(id, row); } return result;
    };
    const tasks = list(plan.definition['tasks']).map(obj); const dependencies = list(plan.definition['dependencies']).map(obj);
    const contexts = list(plan.definition['regions'], 33).map(obj);
    if (contexts.some(row => !fields(row, ['context', 'sourceDigest'])) || contexts.length !== admitted.length || admitted.some(region => contexts.filter(row => equal(row['context'], region.context) &&
        equal(row['sourceDigest'], sourceBodies.find(body => equal(body.context, region.context))!.digest)).length !== 1)) return fail();
    const records = new Map(admitted.flatMap(region => region.records.map(row => [key(region.context, row.recordType + row.identity), row] as const)));
    if (tasks.some(row => !fields(row, ['context', 'irNodeId', 'sourceEntityId', 'location', 'duration', 'ports']))) return fail();
    const taskIndex = indexed(tasks, row => key(row['context'], row['irNodeId']));
    const expectedTasks = lowered.flatMap(({ region, ir }) => list(ir['nodes']).map(obj).filter(node => node['kind'] === 'coglatas.task').map(node => ({ region, node })));
    if (tasks.length !== expectedTasks.length) return fail();
    for (const { region, node } of expectedTasks) {
      const task = taskIndex.get(key(region.context, node['irNodeId']));
      if (!task || task['sourceEntityId'] !== node['sourceEntityId'] || !equal(task['location'], records.get(key(region.context, 'entity' + node['sourceEntityId']))?.location) ||
          !equal(task['duration'], obj(node['payload'])['duration']) || !equal(task['ports'], obj(node['payload'])['ports'])) return fail();
    }
    if (dependencies.some(row => !fields(row, ['owner', 'relationId', 'location', 'mode', 'lag', 'dependent', 'prerequisite']) ||
        !fields(row['dependent'], ['context', 'irNodeId']) || !fields(row['prerequisite'], ['context', 'irNodeId']))) return fail();
    const edgeIndex = indexed(dependencies, row => key(row['owner'], row['relationId']));
    const expectedEdges = lowered.flatMap(({ region, ir }) => list(ir['relations']).map(obj).filter(edge => edge['kind'] === 'coglatas.depends-on').map(edge => ({ region, edge })));
    if (dependencies.length !== expectedEdges.length) return fail();
    for (const { region, edge } of expectedEdges) {
      const dep = edgeIndex.get(key(region.context, edge['relationId']));
      if (!dep || !equal(dep['location'], records.get(key(region.context, 'relation' + edge['relationId']))?.location) || dep['mode'] !== obj(edge['payload'])['mode'] ||
          !equal(dep['lag'], obj(edge['payload'])['lag']) || !equal(dep['dependent'], { context: obj(edge['source'])['context'], irNodeId: obj(edge['source'])['irNodeId'] }) ||
          !equal(dep['prerequisite'], { context: obj(edge['target'])['context'], irNodeId: obj(edge['target'])['irNodeId'] })) return fail();
    }
    const order = list(plan.definition['definitionOrder']).map(obj);
    if (order.some(row => !fields(row, ['context', 'irNodeId'])) || order.length !== tasks.length) return fail();
    const orderIndex = indexed(order, row => key(row['context'], row['irNodeId']));
    if ([...taskIndex.keys()].some(key => !orderIndex.has(key))) return fail();
  }
  const resultAdmission = { basis: captured, projection, regions: admitted, publicationVerified: publishedModel !== null, plan, planNotProducedReason };
  if (!scopedSourcesMatch(value, resultAdmission)) return fail(); return freeze(resultAdmission);
}

export function resolveScopedSource(value: PreviewCase, admitted: AdmittedScopedSources, location: ProjectLocation): { region: ScopedSourceRegion; record: SourceRecord } | null {
  if (!scopedSourcesMatch(value, admitted) || !isProjectLocationShape(location)) return null;
  const region = admitted.regions.find(region => sameContext(region.context, location.context));
  if (!region) return null;
  const rows = region.records.filter(row => row.location.documentId === location.documentId && (location.entityId ? row.recordType === 'entity' && row.identity === location.entityId : row.recordType === 'relation' && row.identity === location.relationId));
  if (rows.length !== 1) return null;
  let target: unknown = rows[0].data;
  for (const [i, raw] of (location.propertyPath ? location.propertyPath.slice(1).split('/') : []).entries()) {
    const key = raw.replaceAll('~1', '/').replaceAll('~0', '~');
    if (object(target) && Object.hasOwn(target, key)) target = target[key];
    else if (Array.isArray(target) && /^(0|[1-9][0-9]*)$/u.test(key) && Number(key) < target.length) target = target[Number(key)];
    else if (Array.isArray(target) && i === 2 && location.propertyPath.startsWith('/payload/')) {
      const field = location.propertyPath.split('/')[2] === 'ports' ? 'portId' : location.propertyPath.split('/')[2] === 'assertions' && rows[0].kind === 'coglatas.project-test' ? 'key' : null;
      const members = field ? target.filter(item => object(item) && item[field] === key) : [];
      if (members.length !== 1) return null; target = members[0];
    } else return null;
  }
  if (location.textSpan) {
    const end = location.textSpan.start + location.textSpan.length; const text = region.sourceText;
    const boundary = (i: number) => i === 0 || i === text.length || !(text.charCodeAt(i - 1) >= 0xd800 && text.charCodeAt(i - 1) <= 0xdbff && text.charCodeAt(i) >= 0xdc00 && text.charCodeAt(i) <= 0xdfff);
    if (end > text.length || !boundary(location.textSpan.start) || !boundary(end)) return null;
  }
  return { region, record: rows[0] };
}
