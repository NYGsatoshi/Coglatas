import { Digest, PreviewBundle, PreviewCase, ProjectLocation, RevisionContext, sameContext, sameDigest } from './projectide.contracts';
import { isProjectLocationShape } from './projectide-preview.validation';
import { parseLocalTestJson, readLocalTests, localTestsMatchCase, verifyLocalTestDigest } from './projectide-tests.reader';

export interface PreparedTestEdit {
  readonly afterCaseKey: string;
  readonly definitionText: string;
  readonly importedText: string;
  readonly plan: {
    readonly format: 'coglatas.local-test-edit/1';
    readonly beforeInputDigest: Digest; readonly afterInputDigest: Digest;
    readonly beforeSourceDigest: Digest; readonly afterSourceDigest: Digest;
    readonly beforeContext: RevisionContext; readonly afterContext: RevisionContext;
    readonly target: ProjectLocation;
    readonly beforeDeclarationDigest: Digest; readonly importedDeclarationDigest: Digest; readonly afterDeclarationDigest: Digest;
  };
}
const record = (value: unknown): value is Record<string, unknown> => typeof value === 'object' && value !== null && !Array.isArray(value);
const equal = (a: unknown, b: unknown): boolean => {
  if (a === b) return true;
  if (Array.isArray(a) && Array.isArray(b)) return a.length === b.length && a.every((item, i) => equal(item, b[i]));
  return record(a) && record(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(key => Object.hasOwn(b, key) && equal(a[key], b[key]));
};
const digest = (value: unknown): value is Digest => record(value) && value['algorithm'] === 'sha-256' && typeof value['value'] === 'string' && /^[a-f0-9]{64}$/u.test(value['value']);
const fail = (): never => { throw new Error('unsupported-prepared-test-edit'); };

/** Exact prepared Source replacement only; the browser does not perform an AST edit or grant a save. */
export function testEditPair(bundle: PreviewBundle, before: PreviewCase): { prepared: PreparedTestEdit; after: PreviewCase } | null {
  if (!bundle.cases.includes(before)) return fail();
  const raw: unknown = before.preparedTestEdit; if (raw == null) return null;
  if (!record(raw) || typeof raw['afterCaseKey'] !== 'string' || typeof raw['definitionText'] !== 'string' || typeof raw['importedText'] !== 'string' ||
      raw['definitionText'].length > 4_194_304 || raw['importedText'].length > 4_194_304 || !record(raw['plan'])) return fail();
  const p = raw['plan']; const after = bundle.cases.find(item => item.key === raw['afterCaseKey']);
  if (!after || before === after || p['format'] !== 'coglatas.local-test-edit/1' || !isProjectLocationShape(p['target']) ||
      p['target'].entityId === undefined || p['target'].relationId !== undefined || p['target'].propertyPath !== '' || p['target'].textSpan !== undefined ||
      !sameContext(p['target'].context, before.context) || !equal(p['beforeContext'], before.context) || !equal(p['afterContext'], after.context) ||
      before.context.kind !== 'coglatas.candidate' || after.context.kind !== 'coglatas.candidate' || before.freshness !== 'Current' || after.freshness !== 'Current' ||
      before.context.proposalId !== after.context.proposalId || before.context.candidateRevisionId === after.context.candidateRevisionId ||
      !equal(before.context.branch, after.context.branch) || !sameContext(before.context.baseRevision, after.context.baseRevision) ||
      !sameContext(before.context.capturedTargetHead, after.context.capturedTargetHead) ||
      !digest(p['beforeInputDigest']) || !sameDigest(p['beforeInputDigest'], before.inputDigest) || !digest(p['afterInputDigest']) || !sameDigest(p['afterInputDigest'], after.inputDigest) ||
      !digest(p['beforeSourceDigest']) || !sameDigest(p['beforeSourceDigest'], before.sourceDigest) || !digest(p['afterSourceDigest']) || !sameDigest(p['afterSourceDigest'], after.sourceDigest) ||
      !digest(p['beforeDeclarationDigest']) || !digest(p['importedDeclarationDigest']) || !digest(p['afterDeclarationDigest'])) return fail();
  const target = p['target'];
  const original = before.declarations.filter(item => item.identity === target.entityId && item.location.documentId === target.documentId);
  const replacement = after.declarations.filter(item => item.identity === target.entityId && item.location.documentId === target.documentId);
  const definition = parseLocalTestJson(raw['definitionText']); const imported = parseLocalTestJson(raw['importedText']);
  if (original.length !== 1 || replacement.length !== 1 || original[0].kind !== 'coglatas.project-test' || replacement[0].kind !== 'coglatas.project-test' ||
      !equal(definition, original[0].data) || !record(imported) || imported['entityId'] !== original[0].identity || imported['kind'] !== 'coglatas.project-test' ||
      imported['schemaVersion'] !== 1 || imported['documentId'] !== original[0].data['documentId']) return fail();
  const expected = structuredClone(imported); const payload = expected['payload'];
  if (!record(payload) || !Array.isArray(payload['assertions']) || payload['assertions'].length > 64) return fail();
  for (const assertion of payload['assertions']) {
    if (!record(assertion) || !record(assertion['target'])) return fail();
    if (equal(assertion['target']['context'], before.context)) assertion['target']['context'] = structuredClone(after.context);
    else if (equal(assertion['target']['context'], after.context)) return fail();
  }
  if (!equal(expected, replacement[0].data)) return fail();
  return { prepared: raw as unknown as PreparedTestEdit, after };
}

export async function readPreparedTestEdit(bundle: PreviewBundle, before: PreviewCase): Promise<{ prepared: PreparedTestEdit; after: PreviewCase; definitionText: string } | null> {
  const pair = testEditPair(bundle, before); if (!pair) return null;
  const captured = JSON.stringify(pair.prepared);
  const [first, second] = await Promise.all([readLocalTests(before), readLocalTests(pair.after)]);
  if (!first || !second || !localTestsMatchCase(before, first) || !localTestsMatchCase(pair.after, second)) return fail();
  const target = pair.prepared.plan.target.entityId!;
  const beforeDefinition = first.projection.declarations.find(item => item.testId === target);
  const afterDefinition = second.projection.declarations.find(item => item.testId === target);
  if (!beforeDefinition || pair.prepared.definitionText !== beforeDefinition.text || !afterDefinition || !(await Promise.all([
    verifyLocalTestDigest('coglatas.project-test-declaration/1', pair.prepared.definitionText, pair.prepared.plan.beforeDeclarationDigest),
    verifyLocalTestDigest('coglatas.project-test-declaration/1', pair.prepared.importedText, pair.prepared.plan.importedDeclarationDigest),
    verifyLocalTestDigest('coglatas.project-test-declaration/1', afterDefinition.text, pair.prepared.plan.afterDeclarationDigest),
  ])).every(Boolean) || captured !== JSON.stringify(before.preparedTestEdit) || testEditPair(bundle, before)?.after !== pair.after ||
      !localTestsMatchCase(before, first) || !localTestsMatchCase(pair.after, second)) return fail();
  return { ...pair, definitionText: pair.prepared.definitionText };
}
