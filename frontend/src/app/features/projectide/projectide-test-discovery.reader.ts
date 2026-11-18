import { Digest, PreviewCase, sameContext, sameDigest } from './projectide.contracts';
import { isProjectLocationShape } from './projectide-preview.validation';
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
function projection(value: unknown): value is LocalProjectTestDiscoveryProjection {
  return record(value) && fields(value, ['format', 'inputText', 'requestedInputDigest', 'plan', 'definitions', 'inventory']) &&
    value['format'] === 'coglatas.local-test-discovery-projection/1' && text(value['inputText']) && digest(value['requestedInputDigest']) &&
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
    manifest: value.manifest, declarations: value.declarations, relations: value.relations, materializedSnapshots: value.materializedSnapshots,
    inspection: value.semanticInspection, result: value.result, sourceMap: value.sourceMap, sourceMapDigest: value.sourceMapDigest });
}
export function localTestDiscoveryMatchesCase(value: PreviewCase, admitted: AdmittedProjectTestDiscovery): boolean {
  return value.freshness === 'Current' && sameContext(value.context, value.desiredContext) && basis(value) === admitted.basis &&
    equal(value.localProjectTestDiscovery, admitted.projection);
}

/** Admit original local Core inventory bytes and membership without requiring a Test run or evaluating an assertion. */
export async function readLocalTestDiscovery(value: PreviewCase): Promise<AdmittedProjectTestDiscovery | null> {
  if (value.localProjectTestDiscovery == null) return null;
  if (!projection(value.localProjectTestDiscovery)) return fail();
  const data = structuredClone(value.localProjectTestDiscovery); const capturedBasis = basis(value);
  if (new TextEncoder().encode(capturedBasis).byteLength > 4_194_304) return fail();
  const source = parseLocalTestJson(value.sourceText); const input = parseLocalTestJson(data.inputText); const plan = parseLocalTestJson(data.plan.text);
  if (!record(source) || !record(input) || !record(plan) || !equal(source, value.source) || !equal(input, value.manifest['manifest']) ||
      !equal(input['sourceDigest'], value.sourceDigest) || !equal(input['sourceContext'], value.context) || !equal(input['sourceManifest'], source['manifest']) ||
      plan['format'] !== 'coglatas.project-test-plan/1' || plan['compilerInputId'] !== value.inputId || !equal(plan['inputDigest'], value.inputDigest) ||
      typeof plan['withinLimits'] !== 'boolean' || typeof plan['discoveryComplete'] !== 'boolean' || typeof plan['bindingComplete'] !== 'boolean' ||
      !record(plan['limits']) || !integer(plan['limits']['MaximumTests'], 256) || !plan['limits']['MaximumTests'] ||
      !integer(plan['limits']['MaximumAssertions'], 1024) || !plan['limits']['MaximumAssertions'] || !Array.isArray(plan['tests']) || plan['tests'].length > 256 ||
      !Array.isArray(source['declarations']) || !Array.isArray(source['documents'])) return fail();
  const engine = plan['engine']; const settings = input['settings'];
  if (!record(engine) || engine['artifactId'] !== 'coglatas.synthetic-project-test-evaluator' || engine['version'] !== '1' || !digest(engine['digest']) ||
      engine['digest'].value !== 'ff7ca4ae92e2186d05eb83f5898637ce6eb2d01afbbb4f130b739b8aa93b5488' || !record(settings)) return fail();
  if (!Array.isArray(plan['references']) || plan['references'].length > 4096) return fail();
  if (value.semanticInspection) {
    if (plan['bindingComplete'] !== value.semanticInspection.bindingComplete ||
        !equal(plan['references'], value.semanticInspection.references.map(item => ({ origin: item.origin, state: item.state, symbolKey: item.symbolKey })))) return fail();
  } else if (plan['discoveryComplete'] || plan['bindingComplete'] || plan['references'].length) return fail();
  const snapshotsComplete = await readSnapshotMaterialization(value);
  if (plan['bindingComplete'] && !snapshotsComplete) return fail();
  const originals = [...source['declarations'], ...source['documents'].map(document => record(document) ? document['root'] : null)]
    .filter(item => record(item) && item['kind'] === 'coglatas.project-test');
  if (originals.length !== data.definitions.length || new Set(data.definitions.map(item => item.testId)).size !== data.definitions.length) return fail();
  const hashes: Promise<boolean>[] = [verifyLocalTestDigest('coglatas.project-source/1', value.sourceText, value.sourceDigest),
    verifyLocalTestDigest('coglatas.compiler-input/1', data.inputText, value.inputDigest), verifyLocalTestDigest('coglatas.project-test-plan/1', data.plan.text, data.plan.digest)];
  for (const definition of data.definitions) {
    const matches = originals.filter(original => record(original) && original['entityId'] === definition.testId);
    if (matches.length !== 1 || !equal(parseLocalTestJson(definition.text), matches[0])) return fail();
  }
  const inventory = data.inventory;
  const expectedState = !sameDigest(data.requestedInputDigest, value.inputDigest) ? 'StaleInput' : !plan['withinLimits'] ? 'ResourceLimit' : !plan['discoveryComplete'] ? 'Unavailable' : 'Ready';
  if (inventory.state !== expectedState || inventory.discoveredTests !== inventory.tests.length) return fail();
  if (expectedState !== 'Ready') {
    if (inventory.tests.length || inventory.inputDigest !== null || inventory.planDigest !== null || inventory.bindingComplete || inventory.evaluatorMatches) return fail();
  } else {
    if (!equal(inventory.inputDigest, value.inputDigest) || !equal(inventory.planDigest, data.plan.digest) || inventory.bindingComplete !== plan['bindingComplete'] ||
        inventory.evaluatorMatches !== equal(engine, settings['evaluator']) || inventory.tests.length !== originals.length || plan['tests'].length !== originals.length) return fail();
    const declarations = value.declarations.filter(item => item.kind === 'coglatas.project-test').sort((left, right) => left.identity < right.identity ? -1 : left.identity > right.identity ? 1 : 0);
    if (declarations.length !== originals.length || declarations.length > plan['limits']['MaximumTests']) return fail();
    let assertionCount = 0;
    for (const [index, row] of inventory.tests.entries()) {
      const declaration = declarations[index]; const definition = data.definitions.find(item => item.testId === declaration.identity); const entry = plan['tests'][index];
      if (!record(row) || !fields(row, ['testId', 'location', 'displayName', 'decodeStatus', 'assertionCount']) || !definition || !record(entry) ||
          row.testId !== declaration.identity || !['Decoded', 'Invalid', 'Unsupported'].includes(row.decodeStatus) || row.decodeStatus !== declaration.status ||
          !equal(parseLocalTestJson(definition.text), declaration.data) || entry['status'] !== declaration.status || !digest(entry['declarationDigest']) ||
          !isProjectLocationShape(row.location) || !equal(row.location, declaration.location) || row.location.entityId !== row.testId ||
          !sameContext(row.location.context, value.context) || row.location.relationId !== undefined || row.location.textSpan !== undefined || row.location.propertyPath !== '' ||
          row.displayName !== (typeof declaration.data['displayName'] === 'string' ? declaration.data['displayName'] : null)) return fail();
      const documentId = declaration.data['documentId'];
      const roots = source['documents'].filter(document => record(document) && record(document['root']) && document['root']['entityId'] === row.testId);
      const owner = typeof documentId === 'string' ? documentId : roots.length === 1 && record(roots[0]) ? roots[0]['documentId'] : null;
      if (owner !== row.location.documentId) return fail();
      const payload = declaration.data['payload']; const assertions = record(payload) ? payload['assertions'] : null;
      if (row.decodeStatus === 'Decoded') {
        if (!Array.isArray(assertions) || assertions.length > 64 || row.assertionCount !== assertions.length) return fail(); assertionCount += assertions.length;
      } else if (row.assertionCount !== null) return fail();
      hashes.push(verifyLocalTestDigest('coglatas.project-test-declaration/1', definition.text, entry['declarationDigest']));
    }
    if (assertionCount > plan['limits']['MaximumAssertions']) return fail();
  }
  if (!(await Promise.all(hashes)).every(Boolean)) return fail();
  const admitted = { basis: capturedBasis, projection: data, inventory };
  if (!localTestDiscoveryMatchesCase(value, admitted)) return fail();
  return freeze(admitted);
}

