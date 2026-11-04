import type { PreviewBundle, PreviewCase, ProjectLocation } from './projectide.contracts';

const record = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null && !Array.isArray(value);
const text = (value: unknown): value is string => typeof value === 'string';
const token = (value: unknown): value is string => text(value) && value.length > 0;
const positive = (value: unknown): boolean =>
  typeof value === 'number' && Number.isSafeInteger(value) && value > 0;
const nonnegative = (value: unknown): boolean =>
  typeof value === 'number' && Number.isSafeInteger(value) && value >= 0;
const oneOf = (value: unknown, values: readonly string[]): boolean =>
  text(value) && values.includes(value);
const list = (value: unknown, check: (item: unknown) => boolean, maximum: number): boolean =>
  Array.isArray(value) && value.length <= maximum && value.every(check);
const digest = (value: unknown): boolean =>
  record(value) &&
  value['algorithm'] === 'sha-256' &&
  text(value['value']) &&
  /^[a-f0-9]{64}$/u.test(value['value']);
const context = (value: unknown): boolean => {
  if (!record(value) || value['kind'] !== 'coglatas.committed' || !token(value['revisionId'])) {
    return false;
  }
  const branch = value['branch'];
  return (
    record(branch) &&
    token(branch['tenantId']) &&
    token(branch['projectId']) &&
    token(branch['branchId'])
  );
};

export function isProjectLocationShape(value: unknown): value is ProjectLocation {
  if (
    !record(value) ||
    value['format'] !== 'coglatas.project-location/1' ||
    value['schemaId'] !== 'coglatas.project-location' ||
    value['schemaVersion'] !== 1 ||
    value['canonicalEncodingVersion'] !== 1 ||
    !token(value['documentId']) ||
    !text(value['propertyPath']) ||
    !context(value['context'])
  ) {
    return false;
  }
  if (
    (value['entityId'] !== undefined && !token(value['entityId'])) ||
    (value['relationId'] !== undefined && !token(value['relationId']))
  ) {
    return false;
  }
  const span = value['textSpan'];
  return (
    span === undefined ||
    span === null ||
    (record(span) && nonnegative(span['start']) && nonnegative(span['length']))
  );
}

const execution = (value: unknown): boolean =>
  oneOf(value, [
    'coglatas.completed',
    'coglatas.not-run',
    'coglatas.cancelled',
    'coglatas.timed-out',
    'coglatas.resource-exhausted',
    'coglatas.internal-error',
  ]);
const coverage = (value: unknown): boolean =>
  oneOf(value, ['coglatas.complete', 'coglatas.partial', 'coglatas.none']);
const verdict = (value: unknown): boolean =>
  oneOf(value, [
    'coglatas.pass',
    'coglatas.fail',
    'coglatas.inconclusive',
    'coglatas.error',
    'coglatas.not-run',
  ]);
const evidence = (value: unknown): boolean =>
  record(value) &&
  oneOf(value['kind'], [
    'coglatas.model-proof',
    'coglatas.observation',
    'coglatas.human-approval',
    'coglatas.heuristic',
    'coglatas.missing',
  ]) &&
  list(value['premises'], digest, 1024) &&
  (value['kind'] === 'coglatas.missing'
    ? (value['premises'] as unknown[]).length === 0 && token(value['missingReason'])
    : (value['premises'] as unknown[]).length > 0 && value['missingReason'] === null);
const diagnostic = (value: unknown): boolean =>
  record(value) &&
  token(value['diagnosticId']) &&
  token(value['ruleId']) &&
  positive(value['ruleVersion']) &&
  token(value['code']) &&
  nonnegative(value['stageOrdinal']) &&
  oneOf(value['severity'], [
    'coglatas.error',
    'coglatas.warning',
    'coglatas.suggestion',
    'coglatas.info',
  ]) &&
  isProjectLocationShape(value['primaryLocation']) &&
  evidence(value['evidence']);
const sourceRecord = (value: unknown): boolean =>
  record(value) &&
  oneOf(value['recordType'], ['entity', 'relation']) &&
  token(value['identity']) &&
  token(value['kind']) &&
  oneOf(value['status'], ['Decoded', 'Unsupported', 'Invalid']) &&
  record(value['data']) &&
  isProjectLocationShape(value['location']);
const rule = (value: unknown): boolean =>
  record(value) &&
  token(value['ruleId']) &&
  positive(value['version']) &&
  execution(value['execution']) &&
  coverage(value['coverage']) &&
  verdict(value['verdict']) &&
  evidence(value['evidence']);
const artifact = (value: unknown): boolean =>
  record(value) && token(value['role']) && digest(value['digest']) && digest(value['inputDigest']);
const result = (value: unknown): boolean => {
  if (
    !record(value) ||
    value['format'] !== 'coglatas.compiler-result/1' ||
    value['resultFormatVersion'] !== 1 ||
    !token(value['compilerInputId']) ||
    !digest(value['inputDigest']) ||
    !execution(value['execution']) ||
    !coverage(value['coverage']) ||
    !verdict(value['verdict']) ||
    typeof value['evidenceComplete'] !== 'boolean' ||
    !list(value['diagnostics'], diagnostic, 20000) ||
    !list(value['rules'], rule, 1024) ||
    !list(value['artifacts'], artifact, 32)
  ) {
    return false;
  }
  const stamp = value['boundInputStamp'];
  return (
    record(stamp) &&
    context(stamp['sourceContext']) &&
    digest(stamp['sourceDigest']) &&
    digest(stamp['inputDigest'])
  );
};
const source = (value: unknown): boolean =>
  record(value) &&
  list(
    value['documents'],
    (document) =>
      record(document) && token(document['documentId']) && text(document['logicalName']),
    100000,
  );
const sourceMap = (value: unknown): boolean =>
  value === null ||
  (record(value) &&
    value['format'] === 'coglatas.semantic-source-map/1' &&
    token(value['compilerInputId']) &&
    list(
      value['nodes'],
      (entry) => record(entry) && isProjectLocationShape(entry['location']),
      100000,
    ) &&
    list(
      value['relations'],
      (entry) => record(entry) && isProjectLocationShape(entry['location']),
      100000,
    ));

/** Structural profile only. Core owns meaning, identities and canonical digest derivation. */
export function isPreviewCaseShape(value: unknown): value is PreviewCase {
  return (
    record(value) &&
    token(value['key']) &&
    text(value['projectName']) &&
    text(value['workspaceName']) &&
    token(value['workspaceId']) &&
    text(value['branchName']) &&
    context(value['context']) &&
    context(value['desiredContext']) &&
    source(value['source']) &&
    text(value['sourceText']) &&
    digest(value['sourceDigest']) &&
    digest(value['inputDigest']) &&
    token(value['inputId']) &&
    record(value['manifest']) &&
    result(value['result']) &&
    token(value['resultId']) &&
    digest(value['resultDigest']) &&
    oneOf(value['freshness'], ['Current', 'Stale', 'Undetermined']) &&
    list(value['changedDimensions'], text, 1024) &&
    list(value['declarations'], sourceRecord, 100000) &&
    list(value['relations'], sourceRecord, 100000) &&
    sourceMap(value['sourceMap']) &&
    (value['sourceMapDigest'] === null || digest(value['sourceMapDigest']))
  );
}

export function isPreviewBundleShape(value: unknown): value is PreviewBundle {
  return (
    record(value) &&
    value['format'] === 'coglatas.web-preview/1' &&
    value['mode'] === 'synthetic-fixture' &&
    token(value['coreProduct']) &&
    list(value['cases'], isPreviewCaseShape, 32)
  );
}

/** Ambiguous identities cannot be used as Angular tracking keys or semantic targets. */
export function hasUniquePreviewIdentities(value: PreviewCase): boolean {
  const unique = <T>(items: readonly T[], key: (item: T) => string) =>
    new Set(items.map(key)).size === items.length;
  return (
    unique(value.source.documents, (item) => item.documentId) &&
    unique(value.declarations, (item) => item.identity) &&
    unique(value.relations, (item) => item.identity) &&
    value.declarations.every((item) => item.recordType === 'entity') &&
    value.relations.every((item) => item.recordType === 'relation') &&
    unique(value.result.diagnostics, (item) => item.diagnosticId) &&
    unique(value.result.rules, (item) => item.ruleId) &&
    unique(value.result.artifacts, (item) => item.role)
  );
}
