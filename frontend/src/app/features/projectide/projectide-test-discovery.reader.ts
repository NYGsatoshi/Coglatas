import { Digest, PreviewCase, sameContext, sameDigest } from './projectide.contracts';
import { isProjectLocationShape, isRevisionContextShape } from './projectide-preview.validation';
import { AdmittedProjectTestDiscovery, LocalProjectTestDiscoveryProjection } from './projectide-test-discovery.contracts';
import { parseLocalTestJson, readSnapshotMaterialization, verifyLocalTestDigest } from './projectide-tests.reader';

const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string' && value.length <= 4_194_304;
const token = (value: unknown): value is string => text(value) && value.length > 0 && value.length <= 4096;
const integer = (value: unknown, maximum: number): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value <= maximum;
const digest = (value: unknown): value is Digest => record(value) && value['algorithm'] === 'sha-256' && typeof value['value'] === 'string' && /^[a-f0-9]{64}$/u.test(value['value']);
const fields = (value: Record<string, unknown>, names: readonly string[]) => Object.keys(value).length === names.length && names.every(name => Object.hasOwn(value, name));
const equal = (left: unknown, right: unknown): boolean => {
  if (left === right) return true;
  if (Array.isArray(left) && Array.isArray(right)) return left.length === right.length && left.every((item, index) => equal(item, right[index]));
  return record(left) && record(right) && Object.keys(left).length === Object.keys(right).length && Object.keys(left).every(key => Object.hasOwn(right, key) && equal(left[key], right[key]));
};
const fail = (): never => { throw new Error('unsupported-local-test-discovery'); };
function freeze<T>(value: T): T {
  if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); }
  return value;
}
type DiscoveryBinding = NonNullable<LocalProjectTestDiscoveryProjection['binding']>;
function bindingEvidence(value: unknown): value is DiscoveryBinding {
  return record(value) && fields(value, ['inputDigest', 'sourceDigest', 'bindingComplete', 'retainedTests', 'references']) &&
    digest(value['inputDigest']) && digest(value['sourceDigest']) && typeof value['bindingComplete'] === 'boolean' &&
    Array.isArray(value['retainedTests']) && value['retainedTests'].length <= 256 && value['retainedTests'].every(item =>
      record(item) && fields(item, ['testId', 'decodeStatus']) && token(item['testId']) && typeof item['decodeStatus'] === 'string' && ['Decoded', 'Invalid', 'Unsupported'].includes(item['decodeStatus'])) &&
    Array.isArray(value['references']) && value['references'].length <= 4096 && value['references'].every(item =>
      record(item) && fields(item, ['origin', 'state', 'symbolKey']) && isProjectLocationShape(item['origin']) &&
      typeof item['state'] === 'string' && ['Resolved', 'MissingWithinCompleteScope', 'UnknownDueToCoverage', 'Ambiguous', 'Unsupported', 'OutOfScope'].includes(item['state']) &&
      (item['state'] === 'Resolved' ? token(item['symbolKey']) : item['symbolKey'] === null));
}
function projection(value: unknown): value is LocalProjectTestDiscoveryProjection {
  return record(value) && fields(value, ['format', 'inputText', 'requestedInputDigest', 'binding', 'plan', 'definitions', 'inventory']) &&
    value['format'] === 'coglatas.local-test-discovery-projection/1' && text(value['inputText']) && digest(value['requestedInputDigest']) &&
    (value['binding'] === null || bindingEvidence(value['binding'])) &&
    record(value['plan']) && fields(value['plan'], ['text', 'digest']) && text(value['plan']['text']) && digest(value['plan']['digest']) &&
    Array.isArray(value['definitions']) && value['definitions'].length <= 256 && value['definitions'].every(item =>
      record(item) && fields(item, ['testId', 'text']) && token(item['testId']) && text(item['text'])) &&
    record(value['inventory']) && fields(value['inventory'], ['state', 'inputDigest', 'planDigest', 'discoveredTests', 'bindingComplete', 'evaluatorMatches', 'tests']) &&
    ['Ready', 'StaleInput', 'Unavailable', 'ResourceLimit'].includes(String(value['inventory']['state'])) &&
    integer(value['inventory']['discoveredTests'], 256) && typeof value['inventory']['bindingComplete'] === 'boolean' &&
    typeof value['inventory']['evaluatorMatches'] === 'boolean' && Array.isArray(value['inventory']['tests']) && value['inventory']['tests'].length <= 256 &&
    new TextEncoder().encode(JSON.stringify(value)).byteLength <= 4_194_304;
}
function basis(value: PreviewCase): string {
  return JSON.stringify({ context: value.context, desiredContext: value.desiredContext, freshness: value.freshness,
    inputId: value.inputId, inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, source: value.source, sourceText: value.sourceText,
    manifest: value.manifest, declarations: value.declarations, materializedSnapshots: value.materializedSnapshots,
    semanticInspection: value.semanticInspection ? { format: value.semanticInspection.format, context: value.semanticInspection.context,
      compilerInputId: value.semanticInspection.compilerInputId, inputDigest: value.semanticInspection.inputDigest, sourceDigest: value.semanticInspection.sourceDigest,
      bindingComplete: value.semanticInspection.bindingComplete,
      symbols: value.semanticInspection.symbols.filter(item => item.kind === 'coglatas.project-test' && sameContext(item.location.context, value.context)),
      references: value.semanticInspection.references.map(item => ({ origin: item.origin, state: item.state, symbolKey: item.symbolKey })) } : null, result: value.result, sourceMap: value.sourceMap, sourceMapDigest: value.sourceMapDigest });
}
export function localTestDiscoveryMatchesCase(value: PreviewCase, admitted: AdmittedProjectTestDiscovery): boolean {
  return value.freshness === 'Current' && sameContext(value.context, value.desiredContext) && basis(value) === admitted.basis &&
    equal(value.localProjectTestDiscovery, admitted.projection);
}

/** Admit one detached Core inventory snapshot; no Test execution or access grant. */
export async function readLocalTestDiscovery(current: PreviewCase): Promise<AdmittedProjectTestDiscovery | null> {
  if (current.localProjectTestDiscovery == null) return null;
  if (!projection(current.localProjectTestDiscovery)) return fail();
  const capturedBasis = basis(current);
  if (new TextEncoder().encode(capturedBasis).byteLength > 4_194_304) return fail();
  const data = structuredClone(current.localProjectTestDiscovery);
  const value = { ...JSON.parse(capturedBasis), localProjectTestDiscovery: data } as PreviewCase;
  const source = parseLocalTestJson(value.sourceText); const input = parseLocalTestJson(data.inputText); const plan = parseLocalTestJson(data.plan.text);
  if (!record(source) || !record(input) || !record(plan) || !equal(source, value.source) || !equal(input, value.manifest['manifest']) ||
      !equal(source['context'], value.context) || source['tenantId'] !== value.context.branch.tenantId || source['projectId'] !== value.context.branch.projectId ||
      !equal(input['sourceDigest'], value.sourceDigest) || !equal(input['sourceContext'], value.context) || !equal(input['sourceManifest'], source['manifest']) ||
      plan['format'] !== 'coglatas.project-test-plan/1' || plan['compilerInputId'] !== value.inputId || !equal(plan['inputDigest'], value.inputDigest) ||
      typeof plan['withinLimits'] !== 'boolean' || typeof plan['discoveryComplete'] !== 'boolean' || typeof plan['bindingComplete'] !== 'boolean' ||
      !record(plan['limits']) || !integer(plan['limits']['MaximumTests'], 256) || !plan['limits']['MaximumTests'] ||
      !integer(plan['limits']['MaximumAssertions'], 1024) || !plan['limits']['MaximumAssertions'] ||
      !Array.isArray(plan['tests']) || plan['tests'].length > 256 ||
      !Array.isArray(source['declarations']) || !Array.isArray(source['documents'])) return fail();
  const engine = plan['engine']; const settings = input['settings'];
  if (!record(engine) || engine['artifactId'] !== 'coglatas.synthetic-project-test-evaluator' || engine['version'] !== '1' || !digest(engine['digest']) ||
      engine['digest'].value !== 'ff7ca4ae92e2186d05eb83f5898637ce6eb2d01afbbb4f130b739b8aa93b5488' || !record(settings)) return fail();
  if (value.semanticInspection && (value.semanticInspection.compilerInputId !== value.inputId ||
      !equal(value.semanticInspection.inputDigest, value.inputDigest) || !equal(value.semanticInspection.sourceDigest, value.sourceDigest) ||
      !equal(value.semanticInspection.context, value.context))) return fail();
  const inspectionBinding = value.semanticInspection ? {
    inputDigest: value.inputDigest, sourceDigest: value.sourceDigest,
    bindingComplete: value.semanticInspection.bindingComplete,
    retainedTests: value.semanticInspection.symbols.filter(item => item.kind === 'coglatas.project-test' && sameContext(item.location.context, value.context))
      .map(item => ({ testId: item.location.entityId, decodeStatus: item.status })),
    references: value.semanticInspection.references.map(item => ({ origin: item.origin, state: item.state, symbolKey: item.symbolKey }))
  } : null;
  const binding = data.binding ?? inspectionBinding ?? fail();
  if (!bindingEvidence(binding) || !equal(binding.inputDigest, value.inputDigest) || !equal(binding.sourceDigest, value.sourceDigest) ||
      binding.bindingComplete !== plan['bindingComplete'] || !equal(binding.references, plan['references'])) return fail();
  const orderedTests = (tests: DiscoveryBinding['retainedTests']) => [...tests].sort((a, b) => a.testId < b.testId ? -1 : a.testId > b.testId ? 1 : 0);
  if (inspectionBinding && (!bindingEvidence(inspectionBinding) || binding.bindingComplete !== inspectionBinding.bindingComplete ||
      !equal(binding.references, inspectionBinding.references) || !equal(orderedTests(binding.retainedTests), orderedTests(inspectionBinding.retainedTests)))) return fail();
  const snapshotsComplete = await readSnapshotMaterialization(value);
  if (binding.bindingComplete && !snapshotsComplete) return fail();
  const contexts: PreviewCase['context'][] = [value.context];
  const materialized = value.materializedSnapshots;
  if (record(materialized) && Array.isArray(materialized['entries'])) for (const item of materialized['entries']) {
    if (record(item) && item['admission'] === 'Available' && isRevisionContextShape(item['context'])) contexts.push(item['context']);
  }
  if (binding.references.some(item => !contexts.some(context => sameContext(item.origin.context, context)))) return fail();
  const originals = [...source['declarations'], ...source['documents'].map(document => record(document) ? document['root'] : null)]
    .filter(item => record(item) && item['kind'] === 'coglatas.project-test');
  if (originals.length !== data.definitions.length || new Set(data.definitions.map(item => item.testId)).size !== data.definitions.length ||
      plan['discoveryComplete'] !== (plan['tests'].length === originals.length)) return fail();
  const hashes = [() => verifyLocalTestDigest('coglatas.project-source/1', value.sourceText, value.sourceDigest),
    () => verifyLocalTestDigest('coglatas.compiler-input/1', data.inputText, value.inputDigest),
    () => verifyLocalTestDigest('coglatas.project-test-plan/1', data.plan.text, data.plan.digest)];
  const definitions = await Promise.all(data.definitions.map(async definition => {
    const matches = originals.filter(original => record(original) && original['entityId'] === definition.testId);
    if (matches.length !== 1 || !equal(parseLocalTestJson(definition.text), matches[0])) return fail();
    const hash = await crypto.subtle.digest('SHA-256', new TextEncoder().encode('coglatas.project-test-declaration/1\0' + definition.text));
    return { ...definition, digest: Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('') };
  }));
  const declarations = value.declarations.filter(item => item.kind === 'coglatas.project-test')
    .sort((left, right) => left.identity < right.identity ? -1 : left.identity > right.identity ? 1 : 0);
  const retained = new Map(binding.retainedTests.map(item => [item.testId, item.decodeStatus]));
  if (retained.size !== binding.retainedTests.length || plan['tests'].length !== retained.size) return fail();
  const entries = new Map<string, { status: string; assertionCount: number | null }>();
  let assertionCount = 0; let previous = '';
  for (const entry of plan['tests']) {
    if (!record(entry) || !fields(entry, ['declarationDigest', 'status']) || !digest(entry['declarationDigest']) ||
        !['Decoded', 'Invalid', 'Unsupported'].includes(String(entry['status']))) return fail();
    const declarationDigest = entry['declarationDigest'];
    const matches = definitions.filter(item => item.digest === declarationDigest.value);
    if (matches.length !== 1) return fail();
    const definition = matches[0]; const declaration = declarations.find(item => item.identity === definition.testId);
    if (!declaration || retained.get(definition.testId) !== entry['status'] || declaration.status !== entry['status'] || !equal(parseLocalTestJson(definition.text), declaration.data) ||
        definition.testId <= previous || entries.has(definition.testId)) return fail();
    previous = definition.testId;
    let count: number | null = null;
    if (entry['status'] === 'Decoded') {
      const payload = declaration.data['payload']; const assertions = record(payload) ? payload['assertions'] : null;
      if (declaration.data['schemaVersion'] !== 1 || !Array.isArray(assertions) || assertions.length > 64) return fail();
      count = assertions.length; assertionCount += count;
    }
    entries.set(definition.testId, { status: declaration.status, assertionCount: count });
  }
  if (plan['withinLimits'] !== (originals.length <= plan['limits']['MaximumTests'] && assertionCount <= plan['limits']['MaximumAssertions'])) return fail();
  const inventory = data.inventory;
  const expectedState = !sameDigest(data.requestedInputDigest, value.inputDigest) ? 'StaleInput' : !plan['withinLimits'] ? 'ResourceLimit' : !plan['discoveryComplete'] ? 'Unavailable' : 'Ready';
  if (inventory.state !== expectedState || inventory.discoveredTests !== inventory.tests.length) return fail();
  if (expectedState !== 'Ready') {
    if (inventory.tests.length || inventory.inputDigest !== null || inventory.planDigest !== null || inventory.bindingComplete || inventory.evaluatorMatches) return fail();
  } else {
    if (!equal(inventory.inputDigest, value.inputDigest) || !equal(inventory.planDigest, data.plan.digest) ||
        inventory.bindingComplete !== binding.bindingComplete || inventory.evaluatorMatches !== equal(engine, settings['evaluator']) ||
        inventory.tests.length !== originals.length || declarations.length !== originals.length) return fail();
    for (const [index, row] of inventory.tests.entries()) {
      const declaration = declarations[index]; const entry = entries.get(declaration.identity);
      if (!record(row) || !fields(row, ['testId', 'location', 'displayName', 'decodeStatus', 'assertionCount']) || !entry ||
          row.testId !== declaration.identity || row.decodeStatus !== entry.status || row.assertionCount !== entry.assertionCount ||
          !isProjectLocationShape(row.location) || !equal(row.location, declaration.location) || row.location.entityId !== row.testId ||
          !sameContext(row.location.context, value.context) || row.location.relationId !== undefined || row.location.textSpan !== undefined || row.location.propertyPath !== '' ||
          row.displayName !== (typeof declaration.data['displayName'] === 'string' ? declaration.data['displayName'] : null)) return fail();
      const documentId = declaration.data['documentId'];
      const roots = source['documents'].filter(document => record(document) && record(document['root']) && document['root']['entityId'] === row.testId);
      const owner = typeof documentId === 'string' ? documentId : roots.length === 1 && record(roots[0]) ? roots[0]['documentId'] : null;
      if (owner !== row.location.documentId) return fail();
    }
  }
  if (!(await Promise.all(hashes.map(check => check()))).every(Boolean)) return fail();
  const admitted = { basis: capturedBasis, projection: data, inventory };
  if (!localTestDiscoveryMatchesCase(current, admitted)) return fail();
  return freeze(admitted);
}
