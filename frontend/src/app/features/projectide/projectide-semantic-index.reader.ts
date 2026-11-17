import { BoundReference, Digest, InspectedSymbol, PreviewCase, ProjectLocation, sameContext, sameDigest } from './projectide.contracts';
import { AdmittedScopedSources, scopedSourcesMatch } from './projectide-scoped-sources';
import { isProjectLocationShape } from './projectide-preview.validation';
import { parseLocalTestJson as parse, verifyLocalTestDigest as verify } from './projectide-tests.reader';

export interface IndexedSymbol extends InspectedSymbol { readonly displayName: string | null; readonly isDocument: boolean; }
export interface IndexedCompletionPage { readonly state: 'Ready' | 'Unsupported'; readonly expectedKind: string | null; readonly indexCoverageComplete: boolean;
  readonly scanComplete: boolean; readonly scannedSymbols: number; readonly symbolKeys: readonly string[]; }
export interface IndexedReferencePage { readonly state: 'Ready'; readonly indexCoverageComplete: boolean; readonly scanComplete: boolean;
  readonly scannedReferences: number; readonly referenceIndices: readonly number[]; }
export interface SemanticIndexProjection {
  readonly format: 'coglatas.local-semantic-index-projection/1'; readonly resultDigest: Digest;
  readonly index: { readonly text: string; readonly digest: Digest };
  readonly definitions: readonly { readonly referenceIndex: number; readonly state: 'Ready' | 'Unavailable'; readonly targetKey: string | null; readonly resolution: 'Resolved' | null }[];
  readonly completions: readonly { readonly originKey: string; readonly pages: readonly IndexedCompletionPage[] }[];
  readonly referenceSearches: readonly { readonly targetKey: string; readonly pages: readonly IndexedReferencePage[] }[];
}
export interface AdmittedSemanticIndex { readonly projection: SemanticIndexProjection; readonly symbols: readonly IndexedSymbol[];
  readonly references: readonly BoundReference[]; readonly inputDigest: Digest; readonly indexCoverageComplete: true; }
const owners = new WeakMap<AdmittedSemanticIndex, { value: PreviewCase; sources: AdmittedScopedSources; fingerprint: string }>();
const object = (v: unknown): v is Record<string, unknown> => !!v && typeof v === 'object' && !Array.isArray(v);
const fail = (): never => { throw new Error('unsupported-semantic-index'); };
const obj = (v: unknown): Record<string, unknown> => object(v) ? v : fail();
const list = (v: unknown): unknown[] => Array.isArray(v) && v.length <= 100_000 ? v : fail();
const fields = (v: unknown, names: readonly string[]) => object(v) && Object.keys(v).length === names.length && names.every(n => Object.hasOwn(v, n));
const digest = (v: unknown): v is Digest => fields(v, ['algorithm', 'value']) && obj(v)['algorithm'] === 'sha-256' && typeof obj(v)['value'] === 'string' && /^[a-f0-9]{64}$/u.test(String(obj(v)['value']));
const equal = (a: unknown, b: unknown): boolean => a === b || Array.isArray(a) && Array.isArray(b) && a.length === b.length && a.every((v, i) => equal(v, b[i])) ||
  object(a) && object(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(k => Object.hasOwn(b, k) && equal(a[k], b[k]));
const quote = (s: string): string => '"' + s.replace(/["\\\u0000-\u001f]/gu, ch => ch === '"' || ch === '\\' ? '\\' + ch : '\\u' + ch.charCodeAt(0).toString(16).padStart(4, '0')) + '"';
const scalarCompare = (a: string, b: string): number => { const x = [...a], y = [...b]; for (let i = 0; i < Math.min(x.length, y.length); i++) {
  const delta = x[i].codePointAt(0)! - y[i].codePointAt(0)!; if (delta) return delta; } return x.length - y.length; };
const canonical = (v: unknown): string => typeof v === 'string' ? quote(v) : Array.isArray(v) ? '[' + v.map(canonical).join(',') + ']' :
  object(v) ? '{' + Object.keys(v).sort(scalarCompare).map(k => quote(k) + ':' + canonical(v[k])).join(',') + '}' : JSON.stringify(v) ?? fail();
const freeze = <T>(v: T): T => { if (v && typeof v === 'object') { Object.values(v).forEach(freeze); Object.freeze(v); } return v; };
const ordinal = (a: string, b: string): number => a < b ? -1 : a > b ? 1 : 0;
export function semanticIndexMatches(value: PreviewCase, sources: AdmittedScopedSources, index: AdmittedSemanticIndex): boolean {
  const owner = owners.get(index);
  return owner?.value === value && owner.sources === sources && scopedSourcesMatch(value, sources) && owner.fingerprint === JSON.stringify(value);
}

/** Original-byte/hash/scope and query membership validation only. Core owns all semantic resolutions and candidates. */
export async function readSemanticIndex(value: PreviewCase, sources: AdmittedScopedSources): Promise<AdmittedSemanticIndex | null> {
  if (value.semanticIndex == null || !scopedSourcesMatch(value, sources)) return null;
  const fingerprint = JSON.stringify(value); const projection = structuredClone(value.semanticIndex);
  if (!fields(projection, ['format', 'resultDigest', 'index', 'definitions', 'completions', 'referenceSearches']) ||
      projection.format !== 'coglatas.local-semantic-index-projection/1' || !digest(projection.resultDigest) || !sameDigest(projection.resultDigest, value.resultDigest) ||
      !fields(projection.index, ['text', 'digest']) || !digest(projection.index.digest) || typeof projection.index.text !== 'string' ||
      new TextEncoder().encode(JSON.stringify(projection)).length > 4_194_304 || !await verify('coglatas.semantic-index/1', projection.index.text, projection.index.digest)) return fail();
  const data = obj(parse(projection.index.text));
  if (!fields(data, ['format', 'compilerInputId', 'rootInputDigest', 'bindingClosureDigest', 'state', 'includesDeclaredSnapshots', 'indexCoverageComplete', 'regions', 'symbols', 'references']) ||
      data['format'] !== 'coglatas.semantic-index/1' || data['compilerInputId'] !== value.inputId || !equal(data['rootInputDigest'], value.inputDigest) ||
      !equal(data['bindingClosureDigest'], sources.projection.binding.digest) || canonical(data) !== projection.index.text) return fail();
  if (data['state'] !== 'Complete' || data['includesDeclaredSnapshots'] !== true || data['indexCoverageComplete'] !== true) return null;
  let work = 1_000_000;
  const charge = (n = 1): void => { work -= n; if (work < 0) return fail(); };
  const binding = obj(parse(sources.projection.binding.text)); const regions = list(binding['regions']).map(obj);
  const indexRegions = list(data['regions']).map(obj);
  if (regions.length !== sources.regions.length || indexRegions.length !== regions.length) return fail();
  const expected = new Map<string, IndexedSymbol>();
  const add = (row: IndexedSymbol): void => { charge(); if (expected.has(row.key)) return fail(); expected.set(row.key, row); };
  for (const region of sources.regions) {
    charge(); const actual = regions.filter(r => sameContext(obj(r['region'])['context'] as ProjectLocation['context'], region.context));
    const projected = indexRegions.filter(r => equal(r['context'], region.context));
    if (actual.length !== 1 || projected.length !== 1 || !fields(projected[0], ['context', 'sourceDigest', 'bindingRegionDigest', 'bindingComplete']) ||
        !equal(projected[0]['sourceDigest'], obj(actual[0]['region'])['sourceDigest']) || !equal(projected[0]['bindingRegionDigest'], actual[0]['digest']) || projected[0]['bindingComplete'] !== true) return fail();
    for (const record of region.records.filter(r => r.recordType === 'entity')) {
      charge(); const key = canonical({ context: region.context, entityId: record.identity, portId: null });
      const displayName = typeof record.data['displayName'] === 'string' ? record.data['displayName'] : null;
      add({ key, kind: record.kind, status: 'Decoded', location: record.location, declaredType: null, displayName, isDocument: false });
      if (record.kind === 'coglatas.task') for (const raw of list(obj(record.data['payload'])['ports'])) {
        charge(); const port = obj(raw); const type = obj(port['type']);
        const location = { ...record.location, propertyPath: '/payload/ports/' + port['portId'] };
        add({ key: canonical({ context: region.context, entityId: record.identity, portId: port['portId'] }), kind: String(type['kind']), status: 'Decoded', location,
          declaredType: type, displayName, isDocument: false });
      }
    }
    const source = obj(parse(region.sourceText));
    for (const raw of list(source['documents'])) {
      charge(); const document = obj(raw); const location = { format: 'coglatas.project-location/1', schemaId: 'coglatas.project-location', schemaVersion: 1,
        canonicalEncodingVersion: 1, documentId: document['documentId'], context: region.context, propertyPath: '' };
      if (!isProjectLocationShape(location) || typeof document['logicalName'] !== 'string') return fail();
      add({ key: canonical({ context: region.context, documentId: document['documentId'] }), kind: String(document['schemaId']), status: 'Decoded', location,
        declaredType: null, displayName: document['logicalName'], isDocument: true });
    }
  }
  const symbols = list(data['symbols']) as unknown as IndexedSymbol[];
  if (symbols.length !== expected.size) return fail();
  let previous: string | null = null;
  for (const symbol of symbols) {
    charge(); if (!fields(symbol, ['key', 'kind', 'status', 'location', 'declaredType', 'displayName', 'isDocument']) || typeof symbol.key !== 'string' ||
        previous !== null && ordinal(previous, symbol.key) >= 0 || !equal(symbol, expected.get(symbol.key))) return fail(); previous = symbol.key;
  }
  const references = list(data['references']) as unknown as BoundReference[];
  const actualReferences = regions.flatMap(r => list(obj(r['region'])['references'])).map(obj).sort((a, b) => ordinal(canonical(a['origin']), canonical(b['origin'])));
  charge(actualReferences.length + references.length);
  if (!equal(references, actualReferences) || references.some(r => r.state !== 'Resolved' || typeof r.symbolKey !== 'string' || !expected.has(r.symbolKey))) return fail();
  const originCounts = new Map<string, number>(); for (const r of references) { const key = canonical(r.origin); originCounts.set(key, (originCounts.get(key) ?? 0) + 1); }
  if (list(projection.definitions).length !== references.length) return fail();
  for (const [i, d] of projection.definitions.entries()) {
    charge(); const unique = originCounts.get(canonical(references[i].origin)) === 1;
    if (!fields(d, ['referenceIndex', 'state', 'targetKey', 'resolution']) || d.referenceIndex !== i || d.state !== (unique ? 'Ready' : 'Unavailable') ||
        d.targetKey !== (unique ? references[i].symbolKey : null) || d.resolution !== (unique ? 'Resolved' : null)) return fail();
  }
  const referenceOffsets = new Map<string, number[]>();
  for (const [i, reference] of references.entries()) {
    charge(); const key = reference.symbolKey; if (typeof key !== 'string') return fail();
    const rows = referenceOffsets.get(key) ?? []; rows.push(i); referenceOffsets.set(key, rows);
  }
  if (list(projection.referenceSearches).length !== symbols.length || new Set(projection.referenceSearches.map(g => g.targetKey)).size !== symbols.length) return fail();
  for (const group of projection.referenceSearches) {
    charge(); if (!fields(group, ['targetKey', 'pages']) || !expected.has(group.targetKey)) return fail();
    const pages = list(group.pages); if (!pages.length || pages.length > references.length + 1) return fail(); let offset = 0; let match = 0;
    const matches = referenceOffsets.get(group.targetKey) ?? [];
    for (const [i, raw] of pages.entries()) {
      charge(); const page = raw as IndexedReferencePage; const indices: number[] = []; const end = Math.min(offset + 256, references.length);
      while (match < matches.length && matches[match] < end && indices.length < 32) { charge(); indices.push(matches[match++]); }
      const next = indices.length === 32 ? indices[31] + 1 : end; const scanned = next - offset; offset = next;
      const complete = offset === references.length;
      if (!fields(page, ['state', 'indexCoverageComplete', 'scanComplete', 'scannedReferences', 'referenceIndices']) || page.state !== 'Ready' ||
          page.indexCoverageComplete !== true || page.scanComplete !== complete || page.scannedReferences !== scanned || !equal(page.referenceIndices, indices) || complete !== (i === pages.length - 1)) return fail();
    }
  }
  const candidateOffsets = new Map<string, number[]>();
  for (const [i, symbol] of symbols.entries()) {
    charge(); if (symbol.isDocument || symbol.declaredType !== null) continue;
    const rows = candidateOffsets.get(symbol.kind) ?? []; rows.push(i); candidateOffsets.set(symbol.kind, rows);
  }
  const ports = symbols.filter(s => !s.isDocument && s.declaredType !== null);
  if (list(projection.completions).length !== ports.length || new Set(projection.completions.map(g => g.originKey)).size !== ports.length) return fail();
  for (const group of projection.completions) {
    charge(); const port = ports.find(s => s.key === group.originKey); if (!fields(group, ['originKey', 'pages']) || !port) return fail();
    const pages = list(group.pages); if (!pages.length || pages.length > symbols.length + 1) return fail();
    const type = obj(port.declaredType); const ready = type['kind'] === 'coglatas.entity-reference' && type['schemaVersion'] === 1 && typeof type['expectedKind'] === 'string'; let offset = 0; let match = 0;
    const matches = ready ? candidateOffsets.get(String(type['expectedKind'])) ?? [] : [];
    for (const [i, raw] of pages.entries()) {
      const page = raw as IndexedCompletionPage;
      if (!fields(page, ['state', 'expectedKind', 'indexCoverageComplete', 'scanComplete', 'scannedSymbols', 'symbolKeys'])) return fail();
      if (!ready) {
        if (pages.length !== 1 || page.state !== 'Unsupported' || page.expectedKind !== null || page.indexCoverageComplete !== false || page.scanComplete !== false || page.scannedSymbols !== 0 || !equal(page.symbolKeys, [])) return fail();
        continue;
      }
      charge(); const indices: number[] = []; const end = Math.min(offset + 256, symbols.length);
      while (match < matches.length && matches[match] < end && indices.length < 32) { charge(); indices.push(matches[match++]); }
      const next = indices.length === 32 ? indices[31] + 1 : end; const scanned = next - offset; offset = next;
      const keys = indices.map(i => symbols[i].key);
      const complete = offset === symbols.length;
      if (page.state !== 'Ready' || page.expectedKind !== type['expectedKind'] || page.indexCoverageComplete !== true || page.scanComplete !== complete || page.scannedSymbols !== scanned || !equal(page.symbolKeys, keys) || complete !== (i === pages.length - 1)) return fail();
    }
  }
  if (!scopedSourcesMatch(value, sources) || fingerprint !== JSON.stringify(value)) return null;
  const result: AdmittedSemanticIndex = freeze({ projection, symbols, references, inputDigest: value.inputDigest, indexCoverageComplete: true });
  owners.set(result, { value, sources, fingerprint }); return result;
}
