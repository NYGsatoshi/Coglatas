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
  return bundle;
}

/** Reused by direct navigation so shape validation cannot bypass exact artifact binding. */
function validatePreviewCaseBindings(item: PreviewCase): void {
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
  if (
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
    value.sourceMap &&
    [...value.sourceMap.nodes, ...value.sourceMap.relations].filter(
      (entry) =>
        entry.location.documentId === location.documentId &&
        (location.entityId
          ? entry.location.entityId === location.entityId
          : entry.location.relationId === location.relationId),
    ).length !== 1
  ) {
    return 'unavailable';
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
      if (index !== 2 || segments[0] !== 'payload' || segments[1] !== 'ports') return 'invalid-location';
      const ports = target.filter(
        (item: unknown) => typeof item === 'object' && item !== null &&
          (item as Record<string, unknown>)['portId'] === key,
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
