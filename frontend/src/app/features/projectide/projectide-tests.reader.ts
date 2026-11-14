import { Digest, PreviewCase, sameContext, sameDigest } from './projectide.contracts';
import { isProjectLocationShape } from './projectide-preview.validation';
import { AdmittedLocalTests, LocalProjectTestProjection, LocalProjectTestRun } from './projectide-tests.contracts';

const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string' && value.length <= 4_194_304;
const token = (value: unknown): value is string => text(value) && value.length > 0 && value.length <= 4096;
const integer = (value: unknown, maximum: number): value is number => typeof value === 'number' && Number.isSafeInteger(value) && value >= 0 && value <= maximum;
const digest = (value: unknown): value is Digest => record(value) && value['algorithm'] === 'sha-256' && typeof value['value'] === 'string' && /^[a-f0-9]{64}$/u.test(value['value']);
const verdicts = ['Pass', 'Fail', 'Inconclusive', 'Error', 'NotRun'];
const oneOf = (value: unknown, allowed: readonly string[]) => typeof value === 'string' && allowed.includes(value);
const equal = (a: unknown, b: unknown): boolean => {
  if (a === b) return true;
  if (Array.isArray(a) && Array.isArray(b)) return a.length === b.length && a.every((item, i) => equal(item, b[i]));
  if (!record(a) || !record(b)) return false;
  return Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(key => Object.hasOwn(b, key) && equal(a[key], b[key]));
};
const fail = (): never => { throw new Error('unsupported-local-test-projection'); };
function validUnicode(text: string): boolean {
  for (let i = 0; i < text.length; i++) {
    const code = text.charCodeAt(i);
    if (code >= 0xd800 && code <= 0xdbff) { const low = text.charCodeAt(++i); if (!(low >= 0xdc00 && low <= 0xdfff)) return false; }
    else if (code >= 0xdc00 && code <= 0xdfff) return false;
  }
  return true;
}
function parse(text: string): unknown {
  const value: unknown = JSON.parse(text);
  const frames: { keys: Set<string> | null; key: boolean }[] = [];
  // JSON.parse validates syntax but discards duplicate members. Keep that Core rejection boundary.
  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (char === '"') {
      const start = i++;
      while (i < text.length && text[i] !== '"') { if (text[i] === String.fromCharCode(92)) i++; i++; }
      const frame = frames.at(-1);
      if (frame?.keys && frame.key) {
        const key: string = JSON.parse(text.slice(start, i + 1));
        if (!validUnicode(key) || frame.keys.has(key)) return fail(); frame.keys.add(key); frame.key = false;
      }
    } else if (char === '{' || char === '[') frames.push({ keys: char === '{' ? new Set<string>() : null, key: true });
    else if (char === '}' || char === ']') frames.pop();
    else if (char === ',' && frames.at(-1)?.keys) frames.at(-1)!.key = true;
  }
  const pending = [{ value, depth: 0 }]; let nodes = 0;
  while (pending.length) {
    const entry = pending.pop()!;
    if (++nodes > 100_000 || entry.depth > 64 || typeof entry.value === 'string' && !validUnicode(entry.value) ||
        typeof entry.value === 'number' && !Number.isFinite(entry.value)) return fail();
    if (entry.value && typeof entry.value === 'object') for (const child of Object.values(entry.value)) pending.push({ value: child, depth: entry.depth + 1 });
  }
  return value;
}
const envelope = (value: unknown): value is { text: string; digest: Digest } => record(value) && text(value['text']) && digest(value['digest']);
const keyedTexts = (value: unknown, maximum: number, assertion = false): boolean => Array.isArray(value) && value.length <= maximum && value.every(item =>
  record(item) && token(item['testId']) && text(item['text']) && (!assertion || token(item['key'])));
function projection(value: unknown): value is LocalProjectTestProjection {
  return record(value) && value['format'] === 'coglatas.local-test-projection/1' && text(value['inputText']) && envelope(value['plan']) && envelope(value['result']) &&
    keyedTexts(value['declarations'], 256) && keyedTexts(value['assertions'], 1024, true) &&
    new TextEncoder().encode(JSON.stringify(value)).byteLength <= 4_194_304;
}
async function matchesDigest(domain: string, value: string, expected: Digest): Promise<boolean> {
  const bytes = new TextEncoder().encode(domain + '\0' + value);
  const hash = await crypto.subtle.digest('SHA-256', bytes);
  return Array.from(new Uint8Array(hash), byte => byte.toString(16).padStart(2, '0')).join('') === expected.value;
}
function freeze<T>(value: T): T {
  if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); }
  return value;
}

function basis(value: PreviewCase): string {
  return JSON.stringify({ context: value.context, desiredContext: value.desiredContext, freshness: value.freshness,
    inputId: value.inputId, inputDigest: value.inputDigest, sourceDigest: value.sourceDigest, source: value.source, sourceText: value.sourceText, manifest: value.manifest, declarations: value.declarations,
    relations: value.relations, inspection: value.semanticInspection, result: value.result, sourceMap: value.sourceMap, sourceMapDigest: value.sourceMapDigest });
}

/** Matches the immutable admitted local projection; it grants no producer trust or current authority. */
export function localTestsMatchCase(value: PreviewCase, admitted: AdmittedLocalTests): boolean {
  return value.freshness === 'Current' && sameContext(value.context, value.desiredContext) &&
    basis(value) === admitted.basis && equal(value.localProjectTests, admitted.projection) && admitted.run.compilerInputId === value.inputId &&
    sameDigest(admitted.run.inputDigest, value.inputDigest) && sameDigest(admitted.run.sourceDigest, value.sourceDigest);
}

/** Verifies original Core bytes and Source membership. Does not evaluate assertions in JavaScript. */
export async function readLocalTests(value: PreviewCase): Promise<AdmittedLocalTests | null> {
  if (value.localProjectTests == null) return null;
  if (!projection(value.localProjectTests)) return fail();
  const data = structuredClone(value.localProjectTests); const capturedBasis = basis(value);
  if (new TextEncoder().encode(capturedBasis).byteLength > 4_194_304) return fail();
  const originalSource = parse(value.sourceText); const originalInput = parse(data.inputText);
  if (!record(originalSource) || !record(originalInput) || !equal(originalSource, value.source) || !equal(originalInput, value.manifest['manifest']) ||
      !equal(originalInput['sourceDigest'], value.sourceDigest) || !equal(originalInput['sourceContext'], value.context) ||
      !equal(originalInput['sourceManifest'], originalSource['manifest'])) return fail();
  const plan: unknown = parse(data.plan.text); const run: unknown = parse(data.result.text);
  if (!record(plan) || !record(run) || plan['format'] !== 'coglatas.project-test-plan/1' || run['format'] !== 'coglatas.project-test-run/1' ||
      plan['compilerInputId'] !== value.inputId || run['compilerInputId'] !== value.inputId || !digest(plan['inputDigest']) || !digest(run['inputDigest']) ||
      !sameDigest(plan['inputDigest'], value.inputDigest) || !sameDigest(run['inputDigest'], value.inputDigest) || !digest(run['sourceDigest']) ||
      !sameDigest(run['sourceDigest'], value.sourceDigest) || !digest(run['planDigest']) || !sameDigest(run['planDigest'], data.plan.digest) ||
      !oneOf(run['execution'], ['Completed', 'NotRun', 'Cancelled', 'TimedOut', 'ResourceExhausted', 'InternalError']) ||
      !oneOf(run['coverage'], ['Complete', 'Partial', 'None']) || !oneOf(run['verdict'], verdicts) || !oneOf(run['freshness'], ['Current', 'Stale', 'Undetermined']) ||
      !oneOf(run['evidenceKind'], ['coglatas.model-proof', 'coglatas.missing']) || !token(run['reason']) ||
      !integer(run['discoveredTests'], 256) || !integer(run['executedAssertions'], 1024) || !Array.isArray(run['tests']) || run['tests'].length > 256 ||
      !Array.isArray(plan['tests']) || plan['tests'].length > 256 || typeof plan['withinLimits'] !== 'boolean' ||
      typeof plan['discoveryComplete'] !== 'boolean' || typeof plan['bindingComplete'] !== 'boolean' || !record(plan['limits']) ||
      !integer(plan['limits']['MaximumTests'], 256) || plan['limits']['MaximumTests'] === 0 ||
      !integer(plan['limits']['MaximumAssertions'], 1024) || plan['limits']['MaximumAssertions'] === 0) return fail();
  const engine = plan['engine'];
  if (!record(engine) || engine['artifactId'] !== 'coglatas.synthetic-project-test-evaluator' || engine['version'] !== '1' || !digest(engine['digest']) ||
      engine['digest'].value !== 'ff7ca4ae92e2186d05eb83f5898637ce6eb2d01afbbb4f130b739b8aa93b5488') return fail();
  if (!value.semanticInspection || plan['bindingComplete'] !== value.semanticInspection.bindingComplete ||
      !Array.isArray(plan['references']) || plan['references'].length > 4096 ||
      !equal(plan['references'], value.semanticInspection.references.map(item => ({ origin: item.origin, state: item.state, symbolKey: item.symbolKey })))) return fail();
  const tests = value.declarations.filter(item => item.kind === 'coglatas.project-test').sort((a, b) => a.identity < b.identity ? -1 : a.identity > b.identity ? 1 : 0);
  if (run['discoveredTests'] !== tests.length || data.declarations.length !== tests.length || plan['tests'].length !== tests.length ||
      new Set(data.declarations.map(item => item.testId)).size !== tests.length ||
      new Set(data.assertions.map(item => item.testId + '\0' + item.key)).size !== data.assertions.length) return fail();
  const sourceDeclarations = originalSource['declarations']; const sourceDocuments = originalSource['documents'];
  if (!Array.isArray(sourceDeclarations) || !Array.isArray(sourceDocuments)) return fail();
  const sourceTests = [...sourceDeclarations, ...sourceDocuments.map(document => record(document) ? document['root'] : null)]
    .filter(item => record(item) && item['kind'] === 'coglatas.project-test');
  if (sourceTests.length !== tests.length || tests.some(test => sourceTests.filter(item => record(item) && item['entityId'] === test.identity && equal(item, test.data)).length !== 1)) return fail();
  for (const test of tests) {
    const explicitDocument = test.data['documentId'];
    const roots = sourceDocuments.filter(document => record(document) && record(document['root']) && document['root']['entityId'] === test.identity);
    const owner = typeof explicitDocument === 'string' ? explicitDocument : roots.length === 1 && record(roots[0]) ? roots[0]['documentId'] : null;
    if (owner !== test.location.documentId || !sameContext(test.location.context, value.context) ||
        test.status === 'Decoded' && test.data['schemaVersion'] !== 1) return fail();
  }
  const hashes: Promise<boolean>[] = [matchesDigest('coglatas.project-source/1', value.sourceText, value.sourceDigest),
    matchesDigest('coglatas.compiler-input/1', data.inputText, value.inputDigest), matchesDigest('coglatas.project-test-plan/1', data.plan.text, data.plan.digest),
    matchesDigest('coglatas.project-test-run/1', data.result.text, data.result.digest)];
  for (const [index, declaration] of tests.entries()) {
    const original = data.declarations[index]; const entry = plan['tests'][index];
    if (!original || original.testId !== declaration.identity || !equal(parse(original.text), declaration.data) || !record(entry) ||
        entry['status'] !== declaration.status || !digest(entry['declarationDigest'])) return fail();
    hashes.push(matchesDigest('coglatas.project-test-declaration/1', original.text, entry['declarationDigest']));
  }
  if (plan['withinLimits'] !== (tests.length <= plan['limits']['MaximumTests'] && data.assertions.length <= plan['limits']['MaximumAssertions'])) return fail();
  const originals = new Map<string, { value: Record<string, unknown>; text: string }>();
  for (const original of data.assertions) {
    const declaration = tests.find(item => item.identity === original.testId && item.status === 'Decoded');
    const payload = declaration?.data['payload']; const items = record(payload) ? payload['assertions'] : null;
    const matches = Array.isArray(items) ? items.filter(item => record(item) && item['key'] === original.key) : [];
    const parsed: unknown = parse(original.text);
    if (matches.length !== 1 || !record(parsed) || !equal(parsed, matches[0])) return fail();
    originals.set(original.testId + '\0' + original.key, { value: parsed, text: original.text });
  }
  for (const declaration of tests.filter(item => item.status === 'Decoded')) {
    const payload = declaration.data['payload']; const items = record(payload) ? payload['assertions'] : null;
    if (!Array.isArray(items) || items.length > 64 || items.some(item => !record(item) || !token(item['key']) ||
      !originals.has(declaration.identity + '\0' + item['key']))) return fail();
  }
  let executed = 0; let assertions = 0; let hasFailure = false; let hasError = false;
  const seenTests = new Set<string>();
  for (const test of run['tests']) {
    if (!record(test) || !token(test['testId']) || seenTests.has(test['testId']) || !oneOf(test['verdict'], verdicts) || !token(test['reason']) ||
        !Array.isArray(test['assertions']) || test['assertions'].length > 64) return fail();
    seenTests.add(test['testId']); const declaration = tests.find(item => item.identity === test['testId']);
    if (!declaration) return fail();
    hasFailure ||= test['verdict'] === 'Fail'; hasError ||= test['verdict'] === 'Error';
    const paths = new Set<string>();
    for (const assertion of test['assertions']) {
      if (!record(assertion) || !isProjectLocationShape(assertion['location']) || !oneOf(assertion['verdict'], verdicts) || !token(assertion['reason']) ||
          !digest(assertion['assertionDigest']) || !(assertion['actualDigest'] === null || digest(assertion['actualDigest']))) return fail();
      const location = assertion['location']; const segments = location.propertyPath.split('/');
      if (segments.length !== 4 || segments[1] !== 'payload' || segments[2] !== 'assertions' || paths.has(location.propertyPath) ||
          location.entityId !== test['testId'] || location.relationId !== undefined || location.textSpan !== undefined ||
          location.documentId !== declaration.location.documentId || !sameContext(location.context, value.context)) return fail();
      paths.add(location.propertyPath);
      const key = segments[3].replaceAll('~1', '/').replaceAll('~0', '~'); const original = originals.get(test['testId'] + '\0' + key);
      if (!original) return fail();
      hashes.push(matchesDigest('coglatas.project-test-assertion/1', original.text, assertion['assertionDigest']));
      executed += assertion['verdict'] !== 'NotRun' ? 1 : 0; assertions++;
      hasFailure ||= assertion['verdict'] === 'Fail'; hasError ||= assertion['verdict'] === 'Error';
      if (test['verdict'] === 'Pass' && assertion['verdict'] !== 'Pass') return fail();
    }
    if (test['verdict'] === 'Pass' && (test['assertions'].length === 0 ||
        test['assertions'].length !== data.assertions.filter(item => item.testId === test['testId']).length)) return fail();
  }
  if (assertions > 1024 || executed !== run['executedAssertions'] || hasError && run['verdict'] !== 'Error' ||
      hasFailure && !['Fail', 'Error'].includes(run['verdict'] as string) || run['verdict'] === 'Fail' && !hasFailure || run['verdict'] === 'Error' && !hasError ||
      (['Pass', 'Fail'].includes(run['verdict'] as string) ? run['evidenceKind'] !== 'coglatas.model-proof' : run['evidenceKind'] !== 'coglatas.missing')) return fail();
  const manifest = value.manifest['manifest']; const settings = record(manifest) ? manifest['settings'] : null; const evaluator = record(settings) ? settings['evaluator'] : null;
  if (run['verdict'] === 'Pass' && (run['execution'] !== 'Completed' || run['coverage'] !== 'Complete' || run['freshness'] !== 'Current' ||
      !executed || run['tests'].length !== tests.length || run['tests'].some(test => !record(test) || test['verdict'] !== 'Pass') ||
      !plan['withinLimits'] || !plan['discoveryComplete'] || !plan['bindingComplete'] || !equal(engine, evaluator))) return fail();
  if (!equal(engine, evaluator) && (run['verdict'] !== 'NotRun' || !['NotRun', 'Cancelled'].includes(run['execution'] as string) || run['tests'].length)) return fail();
  if (!(await Promise.all(hashes)).every(Boolean)) return fail();
  const admitted: AdmittedLocalTests = { basis: capturedBasis, projection: data, run: run as unknown as LocalProjectTestRun };
  if (!localTestsMatchCase(value, admitted)) return fail();
  return freeze(admitted);
}
