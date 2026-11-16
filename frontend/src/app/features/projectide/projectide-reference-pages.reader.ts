import { BoundReference, Digest, InspectedSymbol, PreviewCase, sameDigest } from './projectide.contracts';

/** Optional offline projection of actual Core pages; it carries no cursor or authorization. */
export interface PreparedReferenceSearch {
  readonly targetKey: string;
  readonly inputDigest: Digest;
  readonly pages: readonly {
    readonly indexCoverageComplete: boolean;
    readonly scanComplete: boolean;
    readonly scannedReferences: number;
    readonly referenceIndices: readonly number[];
  }[];
}
export interface ReferenceSearchPage {
  readonly targetKey: string;
  readonly inputDigest: Digest;
  readonly pageIndex: number;
  readonly pageCount: number;
  readonly indexCoverageComplete: boolean;
  readonly scanComplete: boolean;
  readonly scannedReferences: number;
  readonly references: readonly BoundReference[];
}
const admitted = new WeakSet<PreviewCase>();
const owners = new WeakMap<ReferenceSearchPage, PreviewCase>();
const fail = (): never => { throw new Error('unsupported-reference-pages'); };
const fields = (value: unknown, names: readonly string[]): boolean => !!value && typeof value === 'object' &&
  !Array.isArray(value) && Object.keys(value).length === names.length && names.every(name => Object.hasOwn(value, name));

/** Retains the existing 4096-reference preview bound; every page matches the Core's 32/256 scan. */
export function admitPreparedReferenceSearches(value: PreviewCase): void {
  if (value.referenceSearches == null || admitted.has(value)) return;
  const inspection = value.semanticInspection; const groups = value.referenceSearches;
  // Legacy callers may omit inspection; optional query projections then remain inert.
  if (!inspection) return;
  if (!Array.isArray(groups) || groups.length > 32 || inspection.references.length > 4096 ||
      new Set(groups.map(group => group?.targetKey)).size !== groups.length) return fail();
  const coverage = inspection.bindingComplete && inspection.symbols.every(symbol => symbol.status === 'Decoded') &&
    new Set(inspection.symbols.map(symbol => symbol.key)).size === inspection.symbols.length;
  for (const group of groups) {
    if (!fields(group, ['targetKey', 'inputDigest', 'pages']) || typeof group.targetKey !== 'string' ||
        !fields(group.inputDigest, ['algorithm', 'value']) || !sameDigest(group.inputDigest, value.inputDigest) ||
        inspection.symbols.filter(symbol => symbol.key === group.targetKey && symbol.status === 'Decoded').length !== 1 ||
        !Array.isArray(group.pages) || group.pages.length < 1 || group.pages.length > inspection.references.length + 1) return fail();
    let offset = 0;
    for (const [pageIndex, page] of group.pages.entries()) {
      if (!fields(page, ['indexCoverageComplete', 'scanComplete', 'scannedReferences', 'referenceIndices']) ||
          !Array.isArray(page.referenceIndices)) return fail();
      const indices: number[] = []; let scanned = 0;
      while (offset < inspection.references.length && scanned < 256 && indices.length < 32) {
        const index = offset++; const reference = inspection.references[index]; scanned++;
        if (reference.state === 'Resolved' && reference.symbolKey === group.targetKey) indices.push(index);
      }
      const complete = offset === inspection.references.length;
      if (page.indexCoverageComplete !== coverage || page.scanComplete !== complete || page.scannedReferences !== scanned ||
          page.referenceIndices.length !== indices.length || page.referenceIndices.some((index, position) => index !== indices[position]) ||
          complete !== (pageIndex === group.pages.length - 1)) return fail();
    }
  }
  // Only the opt-in page profile is immutable. Legacy preview readers keep their existing behavior.
  const stack: { value: object; depth: number }[] = [{ value, depth: 0 }];
  const seen = new Set<object>(); const objects: object[] = [];
  while (stack.length) {
    const entry = stack.pop()!; if (seen.has(entry.value)) continue;
    if (entry.depth > 256 || seen.size >= 100000) return fail();
    seen.add(entry.value); objects.push(entry.value);
    for (const child of Object.values(entry.value)) if (child && typeof child === 'object') stack.push({ value: child, depth: entry.depth + 1 });
  }
  if (new TextEncoder().encode(JSON.stringify(value)).length > 16_777_216) return fail();
  for (const object of objects.reverse()) Object.freeze(object);
  admitted.add(value);
}
export function hasPreparedReferenceSearch(value: PreviewCase, symbol: InspectedSymbol): boolean {
  return admitted.has(value) && value.semanticInspection?.symbols.includes(symbol) === true &&
    value.referenceSearches?.some(group => group.targetKey === symbol.key) === true;
}
export function readReferenceSearchPage(value: PreviewCase, symbol: InspectedSymbol, pageIndex = 0): ReferenceSearchPage | null {
  if (!hasPreparedReferenceSearch(value, symbol) || !Number.isSafeInteger(pageIndex) || pageIndex < 0) return null;
  const group = value.referenceSearches!.find(group => group.targetKey === symbol.key)!;
  const page = group.pages[pageIndex]; if (!page) return null;
  const result: ReferenceSearchPage = Object.freeze({ targetKey: group.targetKey, inputDigest: group.inputDigest, pageIndex,
    pageCount: group.pages.length, indexCoverageComplete: page.indexCoverageComplete, scanComplete: page.scanComplete,
    scannedReferences: page.scannedReferences, references: Object.freeze(page.referenceIndices.map(index => value.semanticInspection!.references[index])) });
  owners.set(result, value); return result;
}
export function referenceSearchPageMatches(value: PreviewCase, page: ReferenceSearchPage): boolean {
  return admitted.has(value) && owners.get(page) === value;
}
