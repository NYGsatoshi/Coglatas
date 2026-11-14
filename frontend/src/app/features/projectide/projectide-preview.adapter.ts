import { Injectable } from '@angular/core';
import {
  isPreviewBundleShape,
  isPreviewCaseShape,
  isProjectLocationShape,
  hasUniquePreviewIdentities,
} from './projectide-preview.validation';
import {
  PreviewBundle,
  PreviewCase,
  ProjectLocation,
  ReadState,
  SourceRecord,
  sameContext,
  sameDigest,
} from './projectide.contracts';

export class PreviewReadError extends Error {
  constructor(readonly state: ReadState) {
    super(state);
  }
}

/** Only accepts the bounded exporter profile. No inferred mapping of future Core versions. */
export function readPreviewBundle(raw: unknown): PreviewBundle {
  if (!isPreviewBundleShape(raw)) {
    throw new PreviewReadError('unsupported');
  }
  const bundle = raw;
  if (new Set(bundle.cases.map((item) => item.key)).size !== bundle.cases.length) {
    throw new PreviewReadError('unsupported');
  }
  for (const item of bundle.cases) {
    validatePreviewCaseBindings(item);
  }
  for (const item of bundle.cases) if (item.preparedRename && item.semanticInspection) validatePreparedDraftRename(bundle, item);
  for (const item of bundle.cases) if (item.preparedDiff && item.semanticInspection) validateSemanticDiffPair(bundle, item);
  for (const item of bundle.cases) if (item.preparedHistory) validatePreparedHistory(bundle, item);
  for (const item of bundle.cases) if (item.preparedRevert) validatePreparedRevert(bundle, item);
  return bundle;
}

/** Binds the Core-prepared candidate. The browser performs no inverse and proves no ancestry or authority. */
function validatePreparedRevert(bundle: PreviewBundle, current: PreviewCase): void {
  const prepared = current.preparedRevert!; const plan = prepared.plan;
  const parent = bundle.cases.find(item => item.key === prepared.parentCaseKey);
  const reverted = bundle.cases.find(item => item.key === prepared.revertedCaseKey);
  const candidate = bundle.cases.find(item => item.key === prepared.candidateCaseKey);
  if (!parent || !reverted || !candidate || candidate === current || parent === reverted || parent === current ||
      current.context.kind !== 'coglatas.committed' || candidate.context.kind !== 'coglatas.candidate' ||
      !sameContext(current.context, plan.currentContext) || !sameContext(parent.context, plan.parentContext) ||
      !sameContext(reverted.context, plan.revertedContext) || !sameContext(candidate.context, plan.candidateContext) ||
      !sameDigest(current.inputDigest, plan.currentInputDigest) || !sameDigest(parent.inputDigest, plan.parentInputDigest) ||
      !sameDigest(reverted.inputDigest, plan.revertedInputDigest) || !sameDigest(candidate.inputDigest, plan.candidateInputDigest) ||
      !sameContext(candidate.context.baseRevision, current.context) || !sameContext(candidate.context.capturedTargetHead, current.context) ||
      bundle.cases.some(item => item !== candidate && sameContext(item.context, candidate.context)))
    throw new PreviewReadError('wrong-scope');
  for (const item of [current, parent, reverted, candidate]) {
    if (item.freshness !== 'Current' || !sameJson(item.context.branch, current.context.branch)) throw new PreviewReadError('wrong-scope');
  }
}

/** Source bindings only; Core owns reconstruction, integrity validation and compaction semantics. */
function validatePreparedHistory(bundle: PreviewBundle, anchor: PreviewCase): void {
  const history = anchor.preparedHistory!; const rows = history.revisions; const path = history.reconstructionPath;
  const head = bundle.cases.find(item => item.key === history.headCaseKey);
  if (!head || !rows.length || !path.length || rows[0].caseKey !== anchor.key || rows[0].storage !== 'Checkpoint' ||
      !sameContext(path[0], anchor.context) || !sameContext(path[path.length - 1], head.context) ||
      new Set(rows.map(row => row.caseKey)).size !== rows.length) throw new PreviewReadError('wrong-scope');
  for (const row of rows) {
    const item = bundle.cases.find(item => item.key === row.caseKey);
    if (!item || item.freshness !== 'Current' || !sameContext(item.context, row.context) || !sameDigest(item.sourceDigest, row.sourceDigest) ||
        item.context.branch.tenantId !== anchor.context.branch.tenantId || item.context.branch.projectId !== anchor.context.branch.projectId ||
        rows.some(other => other !== row && sameContext(other.context, row.context))) throw new PreviewReadError('wrong-scope');
    for (const parent of row.parents) {
      const materialized = rows.find(other => sameContext(other.context, parent));
      if (sameContext(parent, row.context) || parent.branch.tenantId !== anchor.context.branch.tenantId || parent.branch.projectId !== anchor.context.branch.projectId ||
          (materialized && parent.branch.branchId === row.context.branch.branchId && materialized.branchSequence >= row.branchSequence))
        throw new PreviewReadError('wrong-scope');
    }
  }
  for (const [index, context] of path.entries()) {
    const row = rows.find(row => sameContext(row.context, context));
    if (!row || path.some((other, otherIndex) => otherIndex !== index && sameContext(other, context)) ||
        (index > 0 && (row.storage !== 'TypedDelta' || row.parents.length !== 1 ||
          context.branch.branchId !== path[index - 1].branch.branchId || !sameContext(row.parents[0], path[index - 1]))))
      throw new PreviewReadError('wrong-scope');
  }
}

/** Check projection provenance and original values; .NET alone computes semantic categories/completeness. */
function opaqueEnvelopePath(path: string, reserved: readonly string[]): boolean {
  if (!/^\/(?:[^/~]|~[01])*$/u.test(path)) return false;
  const field = path.slice(1).replace(/~1/gu, '/').replace(/~0/gu, '~');
  return !reserved.includes(field);
}

function validateSemanticDiffPair(bundle: PreviewBundle, before: PreviewCase): void {
  const prepared = before.preparedDiff!; const diff = prepared.comparison;
  const after = bundle.cases.find(item => item.key === prepared.afterCaseKey);
  if (!after || before.freshness !== 'Current' || after.freshness !== 'Current' ||
      !sameContext(before.context, diff.beforeContext) || !sameContext(after.context, diff.afterContext) ||
      before.context.branch.tenantId !== after.context.branch.tenantId || before.context.branch.projectId !== after.context.branch.projectId ||
      !sameDigest(before.inputDigest, diff.beforeInputDigest) || !sameDigest(after.inputDigest, diff.afterInputDigest) ||
      diff.noChanges !== (diff.state === 'Complete' && diff.changes.length === 0) ||
      (diff.state === 'Complete' && (!before.semanticInspection?.bindingComplete || !after.semanticInspection?.bindingComplete ||
        (sameContext(before.context, after.context) && !sameDigest(before.sourceDigest, after.sourceDigest)))))
    throw new PreviewReadError('wrong-scope');
  const seen = new Set<string>();
  for (const change of diff.changes) {
    const key = JSON.stringify([change.recordType, change.identity, change.path]);
    if (seen.has(key) || (change.kind === 'Added' && (change.beforePresent || !change.afterPresent)) ||
        (change.kind === 'Removed' && (!change.beforePresent || change.afterPresent)) ||
        (!change.beforePresent && !change.afterPresent)) throw new PreviewReadError('unsupported');
    seen.add(key);
    for (const side of ['before', 'after'] as const) {
      const item = side === 'before' ? before : after;
      const location = side === 'before' ? change.beforeLocation : change.afterLocation;
      const present = side === 'before' ? change.beforePresent : change.afterPresent;
      const projected = change[side];
      let raw: unknown;
      if (change.recordType === 'entity' || change.recordType === 'relation') {
        const records = change.recordType === 'entity' ? item.declarations : item.relations;
        const original = records.find(record => record.identity === change.identity);
        if (original) {
          if (!location || !sameJson(location, original.location)) throw new PreviewReadError('wrong-scope');
          raw = change.path === '/documentId' ? original.location.documentId : original.data;
        } else if (location !== null) throw new PreviewReadError('wrong-scope');
      } else if (change.recordType === 'document') {
        if (change.path !== '/logicalName' && (change.kind !== 'Uninterpreted' ||
            !opaqueEnvelopePath(change.path, ['documentId', 'logicalName', 'schemaId', 'schemaVersion', 'root']))) throw new PreviewReadError('unsupported');
        const document = item.source.documents.find(document => document.documentId === change.identity);
        if (document) {
          if (!location || location.documentId !== document.documentId || !sameContext(location.context, item.context) || location.entityId || location.relationId)
            throw new PreviewReadError('wrong-scope');
          raw = document;
        } else if (location !== null) throw new PreviewReadError('wrong-scope');
      } else {
        if (change.identity !== '' || location !== null || change.kind !== 'Uninterpreted' ||
            !opaqueEnvelopePath(change.path, ['format', 'schemaId', 'schemaVersion', 'canonicalEncodingVersion', 'tenantId', 'projectId', 'context', 'documents', 'declarations', 'relations']))
          throw new PreviewReadError('unsupported');
        raw = item.source;
      }
      if (change.path !== '/documentId' || (change.recordType !== 'entity' && change.recordType !== 'relation')) for (const part of change.path.split('/').slice(1)) {
        const key = part.replace(/~1/gu, '/').replace(/~0/gu, '~');
        raw = typeof raw === 'object' && raw !== null && Object.hasOwn(raw, key) ? (raw as Record<string, unknown>)[key] : undefined;
      }
      if (present !== (raw !== undefined) || (present ? !sameJson(raw, projected) : projected !== null)) throw new PreviewReadError('wrong-scope');
    }
  }
}

/** Validates a Core-prepared synthetic pair; it never authorizes or performs a runtime edit. */
function validatePreparedDraftRename(bundle: PreviewBundle, before: PreviewCase): void {
  const prepared = before.preparedRename!; const plan = prepared.plan;
  const after = bundle.cases.find(item => item.key === prepared.afterCaseKey);
  const context = before.context;
  if (!after || context.kind !== 'coglatas.candidate' || after.context.kind !== 'coglatas.candidate' ||
      !sameContext(context, plan.beforeContext) || !sameContext(after.context, plan.afterContext) ||
      !sameContext(plan.target.context, context) || context.proposalId !== after.context.proposalId ||
      context.candidateRevisionId === after.context.candidateRevisionId ||
      !sameContext(context.baseRevision, after.context.baseRevision) ||
      !sameContext(context.capturedTargetHead, after.context.capturedTargetHead) ||
      !sameDigest(before.inputDigest, plan.beforeInputDigest) || !sameDigest(after.inputDigest, plan.afterInputDigest) ||
      before.freshness !== 'Current' || after.freshness !== 'Current' || !before.sourceMap || !after.sourceMap)
    throw new PreviewReadError('wrong-scope');
  const symbol = before.semanticInspection?.symbols.find(item => sameJson(item.location, plan.target));
  const record = before.declarations.find(item => item.identity === plan.target.entityId && item.location.documentId === plan.target.documentId);
  const renamed = after.declarations.find(item => item.identity === plan.target.entityId && item.location.documentId === plan.target.documentId);
  if (!symbol || !after.semanticInspection || symbol.status !== 'Decoded' || symbol.location.propertyPath !== '' || !record || !renamed ||
      record.status !== 'Decoded' || renamed.status !== 'Decoded' || record.kind !== renamed.kind ||
      (record.data['displayName'] ?? null) !== plan.previousName || renamed.data['displayName'] !== plan.newName ||
      plan.referenceCoverageComplete !== before.semanticInspection?.bindingComplete)
    throw new PreviewReadError('unsupported');
  const references = before.semanticInspection!.references.filter(item => item.state === 'Resolved' && item.symbolKey === symbol.key);
  if (!sameJson(references.map(item => item.origin), plan.referenceOrigins)) throw new PreviewReadError('wrong-scope');
  const afterSymbol = after.semanticInspection.symbols.find(item => sameJson(item.location, { ...plan.target, context: after.context }));
  // This fixture reader admits stable origin paths; Core owns nested value identity derivation.
  const afterOrigins = after.semanticInspection.references.filter(item => item.state === 'Resolved' && item.symbolKey === afterSymbol?.key)
    .map(item => item.origin);
  if (!afterSymbol || afterSymbol.status !== 'Decoded' || afterSymbol.kind !== symbol.kind ||
      after.semanticInspection.bindingComplete !== plan.referenceCoverageComplete ||
      !sameJson(afterOrigins, plan.referenceOrigins.map(origin => ({ ...origin, context: after.context }))))
    throw new PreviewReadError('wrong-scope');
}

/** Reused by direct navigation so shape validation cannot bypass exact artifact binding. */
export function validatePreviewCaseBindings(item: PreviewCase): void {
  if (!hasUniquePreviewIdentities(item)) {
    throw new PreviewReadError('unsupported');
  }
  const documentIds = new Set(item.source.documents.map((document) => document.documentId));
  const declarations = new Map(item.declarations.map((declaration) => [declaration.identity, declaration]));
  const r = item.result;
  if (
    !sameContext(item.context, r.boundInputStamp.sourceContext) ||
    !sameDigest(item.sourceDigest, r.boundInputStamp.sourceDigest) ||
    item.inputId !== r.compilerInputId ||
    !sameDigest(item.inputDigest, r.inputDigest) ||
    !sameDigest(item.inputDigest, r.boundInputStamp.inputDigest) ||
    (item.freshness === 'Current' && !sameContext(item.context, item.desiredContext))
  ) {
    throw new PreviewReadError('wrong-scope');
  }
  const mapArtifact = r.artifacts.find((artifact) => artifact.role === 'coglatas.source-map');
  const published = item.scopedSources?.format === 'coglatas.local-scoped-source-projection/2';
  if (published || r.artifacts.some(artifact => artifact.version === 2)) {
    const data = item.scopedSources?.publication;
    const manifest = item.manifest['manifest'] as { settings?: { semantics?: { version?: string }; ir?: { version?: string } } };
    const expected = [['coglatas.semantic-model', 'coglatas.semantic-model', data?.semanticModel], ['coglatas.ir', 'coglatas.project-ir', data?.ir], ['coglatas.source-map', 'coglatas.semantic-source-map', data?.sourceMap]] as const;
    if (!published || !data || item.sourceMap !== null || item.sourceMapDigest !== null || manifest?.settings?.semantics?.version !== '2' || manifest?.settings?.ir?.version !== '2' ||
        r.artifacts.length !== 3 || expected.some(([role, schemaId, frame]) => !frame || typeof frame.text !== 'string' || !frame.digest ||
          r.artifacts.filter(artifact => artifact.role === role && artifact.schemaId === schemaId && artifact.version === 2 && sameDigest(artifact.inputDigest, item.inputDigest) && sameDigest(artifact.digest, frame.digest)).length !== 1))
      throw new PreviewReadError('wrong-scope');
  } else if (r.artifacts.some(artifact => artifact.version !== undefined && artifact.version !== 1)) throw new PreviewReadError('unsupported');
  else if (
    item.sourceMap
      ? item.sourceMap.format !== 'coglatas.semantic-source-map/1' ||
        item.sourceMap.compilerInputId !== item.inputId ||
        !item.sourceMapDigest ||
        !mapArtifact ||
        !sameDigest(mapArtifact.digest, item.sourceMapDigest) ||
        !sameDigest(mapArtifact.inputDigest, item.inputDigest)
      : !!mapArtifact || !!item.sourceMapDigest
  ) {
    throw new PreviewReadError('wrong-scope');
  }
  for (const location of [
    ...r.diagnostics.map((d) => d.primaryLocation),
    ...item.declarations.map((d) => d.location),
    ...item.relations.map((d) => d.location),
  ]) {
    if (
      !sameContext(item.context, location.context) ||
      !documentIds.has(location.documentId)
    ) {
      throw new PreviewReadError('wrong-scope');
    }
  }
  for (const record of [...item.declarations, ...item.relations]) {
    if (
      (record.recordType === 'entity'
        ? record.location.entityId
        : record.recordType === 'relation'
          ? record.location.relationId
          : null) !== record.identity
    ) {
      throw new PreviewReadError('wrong-scope');
    }
  }
  const inspection = item.semanticInspection;
  if (inspection) {
    if (inspection.compilerInputId !== item.inputId || !sameContext(inspection.context, item.context) ||
        !sameDigest(inspection.inputDigest, item.inputDigest) || !sameDigest(inspection.sourceDigest, item.sourceDigest) ||
        r.execution !== 'coglatas.completed') throw new PreviewReadError('wrong-scope');
    if (new Set(inspection.symbols.map((symbol) => symbol.key)).size !== inspection.symbols.length ||
        new Set(inspection.symbols.map((symbol) => [symbol.location.documentId, symbol.location.entityId, symbol.location.propertyPath].join(':'))).size !== inspection.symbols.length)
      throw new PreviewReadError('unsupported');
    const portIndexes = new Map<SourceRecord, Map<string, Record<string, unknown>>>();
    for (const symbol of inspection.symbols) {
      const location = symbol.location;
      let key: Record<string, unknown>;
      try {
        const parsed: unknown = JSON.parse(symbol.key);
        if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) throw new Error('Invalid symbol key');
        key = parsed as Record<string, unknown>;
      } catch { throw new PreviewReadError('unsupported'); }
      if (Object.keys(key).length !== 3 || !Object.hasOwn(key, 'portId') ||
          !sameJson(key['context'], item.context) || key['entityId'] !== location.entityId ||
          (location.propertyPath === '' ? key['portId'] !== null : location.propertyPath !== '/payload/ports/' + key['portId']))
        throw new PreviewReadError('wrong-scope');
      const declaration = location.entityId ? declarations.get(location.entityId) : undefined;
      if (!sameContext(location.context, item.context) || !declaration ||
          declaration.location.documentId !== location.documentId || location.relationId)
        throw new PreviewReadError('wrong-scope');
      if (location.propertyPath === '') {
        if (symbol.kind !== declaration.kind || symbol.status !== declaration.status || symbol.declaredType !== null)
          throw new PreviewReadError('unsupported');
      } else {
        let ports = portIndexes.get(declaration);
        if (!ports) {
          ports = new Map();
          const payload = declaration.data['payload'] as Record<string, unknown> | undefined;
          const entries = payload?.['ports'];
          if (Array.isArray(entries)) for (const entry of entries) {
            if (typeof entry !== 'object' || entry === null) continue;
            const candidate = entry as Record<string, unknown>;
            const path = '/payload/ports/' + candidate['portId'];
            if (!ports.has(path)) ports.set(path, candidate);
          }
          portIndexes.set(declaration, ports);
        }
        const port = ports.get(location.propertyPath);
        if (!port || symbol.status !== 'Decoded' || !symbol.declaredType ||
            symbol.kind !== symbol.declaredType['kind'] || !sameJson(symbol.declaredType, port['type']))
          throw new PreviewReadError('unsupported');
      }
    }
    for (const reference of inspection.references) {
      if (!sameContext(reference.origin.context, item.context) ||
          !documentIds.has(reference.origin.documentId) ||
          (reference.state === 'Resolved') !== (reference.symbolKey !== null)) throw new PreviewReadError('wrong-scope');
    }
  }
  // Legacy readers may omit the optional inspection. Suggestions then stay inert.
  const pages = inspection ? item.referenceCompletions ?? [] : [];
  if (new Set(pages.map((page) => page.originKey)).size !== pages.length) throw new PreviewReadError('unsupported');
  const symbols = new Map(inspection?.symbols.map((symbol) => [symbol.key, symbol]) ?? []);
  for (const page of pages) {
    const origin = symbols.get(page.originKey);
    if (!origin || !sameDigest(page.inputDigest, item.inputDigest) || origin.status !== 'Decoded' ||
        !origin.location.propertyPath.startsWith('/payload/ports/')) throw new PreviewReadError('wrong-scope');
    if (page.state === 'Ready') {
      const type = origin.declaredType;
      if (!type || Object.keys(type).length !== 5 || type['schemaVersion'] !== 1 ||
          typeof type['nullable'] !== 'boolean' || typeof type['acceptsUnknown'] !== 'boolean' ||
          type['kind'] !== 'coglatas.entity-reference' || type['expectedKind'] !== page.expectedKind ||
          page.coverageComplete !== inspection?.bindingComplete) throw new PreviewReadError('unsupported');
    } else if (page.candidates.length || page.coverageComplete || page.truncated) throw new PreviewReadError('unsupported');
    if (new Set(page.candidates.map((candidate) => candidate.symbolKey)).size !== page.candidates.length)
      throw new PreviewReadError('unsupported');
    for (const candidate of page.candidates) {
      const symbol = symbols.get(candidate.symbolKey);
      const declaration = symbol?.location.entityId ? declarations.get(symbol.location.entityId) : undefined;
      if (!symbol || symbol.status !== 'Decoded' || symbol.location.propertyPath !== '' || symbol.kind !== page.expectedKind ||
          !declaration || (declaration.data['displayName'] ?? null) !== candidate.displayName)
        throw new PreviewReadError('wrong-scope');
    }
  }
  const taskPages = inspection ? item.requirementTaskSuggestions ?? [] : [];
  if (new Set(taskPages.map(page => page.originKey)).size !== taskPages.length) throw new PreviewReadError('unsupported');
  for (const page of taskPages) {
    const origin = symbols.get(page.originKey);
    if (!origin || origin.kind !== 'coglatas.requirement' || origin.status !== 'Decoded' || origin.location.propertyPath !== '' ||
        !sameDigest(page.inputDigest, item.inputDigest) || page.candidateOnly !== true) throw new PreviewReadError('wrong-scope');
    if (page.state === 'Ready') {
      if (page.coverageComplete !== inspection?.bindingComplete) throw new PreviewReadError('unsupported');
    } else if (page.candidates.length || page.coverageComplete || page.truncated) throw new PreviewReadError('unsupported');
    if (new Set(page.candidates.map(candidate => candidate.symbolKey)).size !== page.candidates.length) throw new PreviewReadError('unsupported');
    for (const candidate of page.candidates) {
      const symbol = symbols.get(candidate.symbolKey);
      const declaration = symbol?.location.entityId ? declarations.get(symbol.location.entityId) : undefined;
      if (!symbol || symbol.kind !== 'coglatas.task' || symbol.status !== 'Decoded' || symbol.location.propertyPath !== '' ||
          !declaration || candidate.reason !== 'DeclaredTaskInSnapshot' || (declaration.data['displayName'] ?? null) !== candidate.displayName)
        throw new PreviewReadError('wrong-scope');
    }
  }
  for (const entry of [...(item.sourceMap?.nodes ?? []), ...(item.sourceMap?.relations ?? [])]) {
    if (
      !sameContext(item.context, entry.location.context) ||
      !documentIds.has(entry.location.documentId)
    ) {
      throw new PreviewReadError('wrong-scope');
    }
  }
}

function sameJson(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  if (Array.isArray(a) && Array.isArray(b)) return a.length === b.length && a.every((item, index) => sameJson(item, b[index]));
  if (typeof a !== 'object' || a === null || typeof b !== 'object' || b === null || Array.isArray(a) || Array.isArray(b)) return false;
  const left = a as Record<string, unknown>; const right = b as Record<string, unknown>;
  return Object.keys(left).length === Object.keys(right).length && Object.keys(left).every((key) => Object.hasOwn(right, key) && sameJson(left[key], right[key]));
}

@Injectable()
export class ProjectIdePreviewAdapter {
  async read(url: string, signal: AbortSignal): Promise<PreviewBundle> {
    const response = await fetch(url, { signal, credentials: 'same-origin', cache: 'no-store' });
    if (!response.ok) {
      throw new PreviewReadError(
        response.status === 403 || response.status === 401
          ? 'denied'
          : response.status === 404
            ? 'unavailable'
            : 'error',
      );
    }
    return readPreviewBundle(await response.json());
  }
}

/** Stable semantic IDs first; spans are optional UTF-16 coordinates of this exact canonical buffer. */
export function resolveLocation(
  value: PreviewCase,
  location: ProjectLocation,
  requireSourceMap = false,
): SourceRecord | 'unavailable' | 'stale' | 'wrong-scope' | 'unsupported' | 'invalid-location' {
  if (!isPreviewCaseShape(value) || !isProjectLocationShape(location)) {
    return 'unavailable';
  }
  if (!sameContext(value.context, location.context)) {
    return 'wrong-scope';
  }
  try {
    validatePreviewCaseBindings(value);
  } catch (error) {
    return error instanceof PreviewReadError && error.state === 'wrong-scope'
      ? 'wrong-scope'
      : 'unavailable';
  }
  if (value.freshness !== 'Current' || !sameContext(value.context, value.desiredContext)) {
    return 'stale';
  }
  if (
    !sameDigest(value.sourceDigest, value.result.boundInputStamp.sourceDigest) ||
    !value.source.documents.some((d) => d.documentId === location.documentId)
  ) {
    return 'unavailable';
  }
  if (requireSourceMap && !value.sourceMap) return 'unavailable';
  if (!location.entityId && !location.relationId) return 'unsupported';
  const records = [...value.declarations, ...value.relations].filter(
    (r) =>
      r.location.documentId === location.documentId &&
      (location.entityId
        ? r.recordType === 'entity' && r.location.entityId === location.entityId
        : location.relationId
          ? r.recordType === 'relation' && r.location.relationId === location.relationId
          : false),
  );
  if (records.length !== 1) {
    return 'unavailable';
  }
  const record = records[0];
  if (
    value.sourceMap
  ) {
    const entries = [...value.sourceMap.nodes, ...value.sourceMap.relations].filter(
      (entry) =>
        entry.location.documentId === location.documentId &&
        (location.entityId
          ? entry.location.entityId === location.entityId
          : entry.location.relationId === location.relationId),
    );
    if (entries.length !== 1 || (requireSourceMap && !sameJson(entries[0].location, record.location))) {
      return 'unavailable';
    }
  }
  let target: unknown = record.data;
  if (
    location.propertyPath &&
    (!location.propertyPath.startsWith('/') || /~(?![01])/u.test(location.propertyPath))
  ) {
    return 'unavailable';
  }
  const segments = location.propertyPath ? location.propertyPath.slice(1).split('/') : [];
  for (const [index, part] of segments.entries()) {
    const key = part.replaceAll('~1', '/').replaceAll('~0', '~');
    if (Array.isArray(target) && !/^(0|[1-9][0-9]*)$/u.test(key)) {
      if (index !== 2 || segments[0] !== 'payload') return 'invalid-location';
      const field = segments[1] === 'ports' ? 'portId'
        : segments[1] === 'assertions' && record.kind === 'coglatas.project-test' ? 'key' : null;
      if (!field) return 'invalid-location';
      const ports = target.filter(
        (item: unknown) => typeof item === 'object' && item !== null && Object.hasOwn(item, field) &&
          (item as Record<string, unknown>)[field] === key,
      );
      if (ports.length !== 1) return 'invalid-location';
      target = ports[0];
      continue;
    }
    if (!target || typeof target !== 'object' || !Object.hasOwn(target, key)) {
      return 'invalid-location';
    }
    target = (target as Record<string, unknown>)[key];
  }
  if (location.textSpan) {
    const { start, length } = location.textSpan;
    const boundary = (position: number) =>
      position === 0 ||
      position === value.sourceText.length ||
      !(
        /[\uD800-\uDBFF]/u.test(value.sourceText[position - 1]) &&
        /[\uDC00-\uDFFF]/u.test(value.sourceText[position])
      );
    if (
      !Number.isSafeInteger(start) ||
      !Number.isSafeInteger(length) ||
      start < 0 ||
      length < 0 ||
      start + length > value.sourceText.length ||
      !boundary(start) ||
      !boundary(start + length)
    ) {
      return 'invalid-location';
    }
  }
  return record;
}
