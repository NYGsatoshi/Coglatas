/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { readPreviewBundle } from './projectide-preview.adapter';
import { Diagnostic, PreviewCase } from './projectide.contracts';
import { diagnosticBasisPage } from './projectide-diagnostic-basis';

const bundle = readPreviewBundle(JSON.parse(readFileSync('public/assets/projectide/compiler-outputs-v1.json', 'utf8')));
const actual = bundle.cases.filter(c => c.result.diagnostics.length);
describe('Recorded diagnostic basis pages', () => {
  it.each(actual.map(c => [c.key, c] as const))('retains actual basis and separate result axes: %s', (_, c) => {
    const before = JSON.stringify(c.result);
    for (const d of c.result.diagnostics) {
      const page = diagnosticBasisPage(c, d)!;
      expect(page.diagnostic).toBe(d); expect(page.inputId).toBe(c.inputId); expect(page.resultId).toBe(c.resultId);
      expect(page.inputDigest).toEqual(c.inputDigest); expect(page.resultDigest).toEqual(c.resultDigest);
      expect(page.premises).toEqual(d.evidence.premises); expect(page.total).toBe(d.evidence.premises.length);
      expect(page.diagnostic.evidence.missingReason).toBe(d.evidence.missingReason);
      expect(page.logicalStructureRecorded).toBe(false); expect(page.causalStructureRecorded).toBe(false);
      expect(page.premiseBodiesResolved).toBe(false); expect(page.hasNext).toBe(false);
    }
    expect(JSON.stringify(c.result)).toBe(before);
  });
  it.each([1, 32, 33, 1023, 1024])('pages all %i bounded references without losses', count => {
    const d: Diagnostic = { ...actual[0].result.diagnostics[0], evidence: { kind: 'coglatas.observation', missingReason: null,
      premises: Array.from({length:count}, (_, i) => ({algorithm:'sha-256' as const, value:i.toString(16).padStart(64,'0')})) } };
    const c: PreviewCase = { ...actual[0], result: { ...actual[0].result, diagnostics: [d] } };
    const rows = [];
    for (let offset=0; offset<count; offset+=32) {
      const page = diagnosticBasisPage(c,d,offset)!;
      expect(page.premises.length).toBeLessThanOrEqual(32); expect(page.premises.length).toBeGreaterThan(0);
      expect(page.total).toBe(count); expect(page.offset).toBe(offset); expect(page.hasNext).toBe(offset+page.premises.length<count);
      rows.push(...page.premises);
    }
    expect(rows).toEqual(d.evidence.premises); expect(new Set(rows.map(d=>d.value)).size).toBe(count);
    for (const offset of [-32, 1, 32.1, NaN, Infinity, Number.MAX_SAFE_INTEGER, Math.ceil(count/32)*32]) expect(diagnosticBasisPage(c,d,offset)).toBeNull();
    expect(diagnosticBasisPage(c,{...d})).toBeNull();
  });
});
