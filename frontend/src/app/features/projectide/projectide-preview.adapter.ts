import { Injectable } from '@angular/core';
import { PreviewBundle, PreviewCase, ProjectLocation, ReadState, SourceRecord, sameContext, sameDigest } from './projectide.contracts';

export class PreviewReadError extends Error {
  constructor(readonly state: ReadState) { super(state); }
}

/** Only accepts the bounded exporter profile. No inferred mapping of future Core versions. */
export function readPreviewBundle(raw: unknown): PreviewBundle {
  if (!raw || typeof raw !== 'object') { throw new PreviewReadError('unsupported'); }
  const bundle = raw as PreviewBundle;
  if (bundle.format !== 'coglatas.web-preview/1' || bundle.mode !== 'synthetic-fixture' || !Array.isArray(bundle.cases) || bundle.cases.length > 32) { throw new PreviewReadError('unsupported'); }
  if (new Set(bundle.cases.map(item => item.key)).size !== bundle.cases.length) { throw new PreviewReadError('unsupported'); }
  for (const item of bundle.cases) {
    const r = item.result;
    if (item.context?.kind !== 'coglatas.committed' || item.desiredContext?.kind !== 'coglatas.committed' || r?.format !== 'coglatas.compiler-result/1' || r.resultFormatVersion !== 1 ||
        !['coglatas.completed', 'coglatas.not-run', 'coglatas.cancelled', 'coglatas.timed-out', 'coglatas.resource-exhausted', 'coglatas.internal-error'].includes(r.execution) ||
        !['coglatas.complete', 'coglatas.partial', 'coglatas.none'].includes(r.coverage) || !['coglatas.pass', 'coglatas.fail', 'coglatas.inconclusive', 'coglatas.error', 'coglatas.not-run'].includes(r.verdict) ||
        !['Current', 'Stale', 'Undetermined'].includes(item.freshness) || !Array.isArray(r.diagnostics) || !Array.isArray(item.declarations) || !Array.isArray(item.relations)) { throw new PreviewReadError('unsupported'); }
    if (!sameContext(item.context, r.boundInputStamp.sourceContext) || !sameDigest(item.sourceDigest, r.boundInputStamp.sourceDigest) ||
        item.inputId !== r.compilerInputId || !sameDigest(item.inputDigest, r.inputDigest) || !sameDigest(item.inputDigest, r.boundInputStamp.inputDigest) ||
        (item.freshness === 'Current' && !sameContext(item.context, item.desiredContext))) { throw new PreviewReadError('wrong-scope'); }
    if (item.sourceMap && (item.sourceMap.format !== 'coglatas.semantic-source-map/1' || item.sourceMap.compilerInputId !== item.inputId ||
        !item.sourceMapDigest || !r.artifacts.some(a => a.role === 'coglatas.source-map' && sameDigest(a.digest, item.sourceMapDigest!) && sameDigest(a.inputDigest, item.inputDigest)))) { throw new PreviewReadError('wrong-scope'); }
    for (const location of [...r.diagnostics.map(d => d.primaryLocation), ...item.declarations.map(d => d.location), ...item.relations.map(d => d.location)]) {
      if (!sameContext(item.context, location.context)) { throw new PreviewReadError('wrong-scope'); }
    }
    for (const entry of [...(item.sourceMap?.nodes ?? []), ...(item.sourceMap?.relations ?? [])]) {
      if (!sameContext(item.context, entry.location.context)) { throw new PreviewReadError('wrong-scope'); }
    }
  }
  return bundle;
}

@Injectable()
export class ProjectIdePreviewAdapter {
  async read(url: string, signal: AbortSignal): Promise<PreviewBundle> {
    const response = await fetch(url, { signal, credentials: 'same-origin', cache: 'no-store' });
    if (!response.ok) { throw new PreviewReadError(response.status === 403 || response.status === 401 ? 'denied' : response.status === 404 ? 'unavailable' : 'error'); }
    return readPreviewBundle(await response.json());
  }
}

/** Stable semantic IDs first; spans are optional UTF-16 coordinates of this exact canonical buffer. */
export function resolveLocation(value: PreviewCase, location: ProjectLocation): SourceRecord | 'unavailable' | 'stale' | 'wrong-scope' {
  if (location.format !== 'coglatas.project-location/1' || location.schemaVersion !== 1) { return 'unavailable'; }
  if (!sameContext(value.context, location.context)) { return 'wrong-scope'; }
  if (value.freshness !== 'Current' || !sameContext(value.context, value.desiredContext)) { return 'stale'; }
  if (!sameDigest(value.sourceDigest, value.result.boundInputStamp.sourceDigest) || !value.source.documents.some(d => d.documentId === location.documentId)) { return 'unavailable'; }
  const record = [...value.declarations, ...value.relations].find(r => r.location.documentId === location.documentId &&
    (location.entityId ? r.location.entityId === location.entityId : location.relationId ? r.location.relationId === location.relationId : false));
  if (!record) { return 'unavailable'; }
  if (value.sourceMap && ![...value.sourceMap.nodes, ...value.sourceMap.relations].some(entry => entry.location.documentId === location.documentId &&
      (location.entityId ? entry.location.entityId === location.entityId : entry.location.relationId === location.relationId))) { return 'unavailable'; }
  let target: unknown = record.data;
  if (location.propertyPath && (!location.propertyPath.startsWith('/') || /~(?![01])/u.test(location.propertyPath))) { return 'unavailable'; }
  for (const part of location.propertyPath ? location.propertyPath.slice(1).split('/') : []) {
    const key = part.replaceAll('~1', '/').replaceAll('~0', '~');
    if (!target || typeof target !== 'object' || !Object.hasOwn(target, key)) { return 'unavailable'; }
    target = (target as Record<string, unknown>)[key];
  }
  if (location.textSpan) {
    const { start, length } = location.textSpan;
    const boundary = (position: number) => position === 0 || position === value.sourceText.length || !(/[\uD800-\uDBFF]/u.test(value.sourceText[position - 1]) && /[\uDC00-\uDFFF]/u.test(value.sourceText[position]));
    if (!Number.isSafeInteger(start) || !Number.isSafeInteger(length) || start < 0 || length < 0 || start + length > value.sourceText.length || !boundary(start) || !boundary(start + length)) { return 'unavailable'; }
  }
  return record;
}
