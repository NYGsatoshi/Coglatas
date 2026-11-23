import { Diagnostic, Digest, PreviewCase } from './projectide.contracts';

export const DIAGNOSTIC_BASIS_PAGE_SIZE = 32;
export interface DiagnosticBasisPage {
  readonly inputId: PreviewCase['inputId'];
  readonly inputDigest: Digest;
  readonly resultId: PreviewCase['resultId'];
  readonly resultDigest: Digest;
  readonly diagnostic: Diagnostic;
  readonly offset: number;
  readonly total: number;
  readonly premises: readonly Digest[];
  readonly hasNext: boolean;
  readonly logicalStructureRecorded: false;
  readonly causalStructureRecorded: false;
  readonly premiseBodiesResolved: false;
}

/** The caller supplies an admitted current case. Digest equality cannot substitute for ownership. */
export function diagnosticBasisPage(context: PreviewCase, diagnostic: Diagnostic, offset = 0): DiagnosticBasisPage | null {
  const basis = diagnostic.evidence;
  if (!context.result.diagnostics.includes(diagnostic) || !Number.isSafeInteger(offset) || offset < 0 ||
      offset % DIAGNOSTIC_BASIS_PAGE_SIZE !== 0 || offset > 0 && offset >= basis.premises.length) return null;
  const premises = Object.freeze(basis.premises.slice(offset, offset + DIAGNOSTIC_BASIS_PAGE_SIZE));
  return Object.freeze({ inputId: context.inputId, inputDigest: context.inputDigest,
    resultId: context.resultId, resultDigest: context.resultDigest, diagnostic, offset,
    total: basis.premises.length, premises, hasNext: offset + premises.length < basis.premises.length,
    logicalStructureRecorded: false, causalStructureRecorded: false, premiseBodiesResolved: false });
}
