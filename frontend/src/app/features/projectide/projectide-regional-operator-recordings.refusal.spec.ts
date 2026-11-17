/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { afterEach, vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readRootSource } from './projectide-root-source.reader';
import { readScopedSources } from './projectide-scoped-sources';
import { type RegionalOperatorRecordingProjection, type RegionalOperatorRecordingFrame, readRegionalOperatorRecording, readRecordedRegionalOperators,
  regionalOperatorRecordingMatches, recordedRegionalOperatorsMatch, regionalOperatorRunIdentitiesAreUnique } from './projectide-regional-operator-recordings.reader';
import type { OperatorTextFrame } from './projectide-operator-recording-structure';
vi.stubGlobal('crypto', webcrypto);
afterEach(() => vi.restoreAllMocks());
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/regional-operator-recordings-v1.json'), 'utf8'));
const labels = ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const;
const prefix = 'coglatas.local-regional-predicate-observation-';
const manifestDomain = prefix + 'compiler-recording-manifest/1';
const responseDomain = prefix + 'compiler-host-response/1';
const receiptDomain = prefix + 'compiler-recording-receipt/1';
const recordDomain = 'coglatas.local-regional-predicate-observation/1';
const snapshotDomain = 'coglatas.local-regional-predicate-observation-snapshot/1';
const fixture = (key = 'tests-regional-versioned-base') => readPreviewBundle(structuredClone(original)).cases.find(v => v.key === key)!;
const frame = (target: OperatorTextFrame, domain: string, text: string): void => {
  Object.assign(target, { text, digest: { algorithm: 'sha-256', value: createHash('sha256').update(domain + String.fromCharCode(0)).update(text).digest('hex') } });
};
/** Repair declaration/receipt links so a refusal reaches the semantic boundary, with actual graph subjects preserved. */
function linkEnvelopes(p: RegionalOperatorRecordingProjection): void {
  for (const label of labels) {
    const run = p[label], m = JSON.parse(run.manifest.text);
    const ancestor = label === 'diagnostic' ? p.original : label === 'budget' ? p.diagnostic : label === 'retry' ? p.cancelled : null;
    if (ancestor && m.origin) { m.origin.manifestDigest = ancestor.manifest.digest; m.origin.receiptDigest = ancestor.receipt.digest; }
    frame(run.manifest, manifestDomain, JSON.stringify(m));
    const r = JSON.parse(run.response.text); r.manifestDigest = run.manifest.digest; r.runId = m.runId; r.observationDigest = run.observation?.digest ?? null; r.captureState = run.observation ? JSON.parse(run.observation.text).state : null;
    frame(run.response, responseDomain, JSON.stringify(r));
    const receipt = JSON.parse(run.receipt.text); receipt.manifestDigest = run.manifest.digest; receipt.runId = m.runId;
    receipt.hostResponseDigest = run.response.digest; receipt.attemptId = r.attemptId; receipt.observationRecordDigest = run.observation?.digest ?? null; receipt.captureState = r.captureState;
    frame(run.receipt, receiptDomain, JSON.stringify(receipt));
  }
}
function changeObservation(run: RegionalOperatorRecordingFrame, change: (record: ReturnType<typeof JSON.parse>) => void): void {
  const record = JSON.parse(run.observation!.text); change(record); frame(run.observation!, recordDomain, JSON.stringify(record));
  for (const region of run.regions) for (const diagnostic of region.diagnostics) for (const choice of ['manifest', 'available', 'budget'] as const) {
    const data = JSON.parse(diagnostic[choice].frame.text); data.originalRecord.digest = run.observation!.digest; data.originalRecord.captureState = record.state;
    for (const evaluation of data.evaluations) evaluation.metadata = record.evaluations.find((e: { ordinal: number }) => e.ordinal === evaluation.ordinal) ?? evaluation.metadata;
    frame(diagnostic[choice].frame, snapshotDomain, JSON.stringify(data));
  }
}
async function refuses(change: (p: RegionalOperatorRecordingProjection) => void): Promise<void> {
  const value = fixture(); change(value.regionalOperatorRecordings!);
  const sources = (await readScopedSources(value))!; expect(sources).not.toBeNull();
  await expect(readRegionalOperatorRecording(value, sources)).rejects.toThrow();
}
describe('Regional invocation and original byte refusals', () => {
  it.each(labels)('checks a corrupted unselected request before admitting Manifest: %s', async label => {
    await refuses(p => frame(p[label].request, prefix + 'compiler-host-input/1', p[label].request.text.replace('ActualRegionalEvaluationsOnly', 'ForeignEvaluation')));
  });
  it.each(['source', 'input'] as const)('refuses a genuine foreign %s subject', async field => {
    await refuses(p => Object.assign(p[field], structuredClone(original.cases[0].regionalOperatorRecordings[field])));
  });
  it.each(['authority', 'identityScope', 'compiler', 'context', 'generation', 'origin-run', 'origin-attempt', 'lineage', 'cycle', 'session', 'duplicate-run', 'duplicate-attempt', 'role-collision'])('refuses rehashed invocation substitution: %s', async kind => {
    await refuses(p => {
      const m = JSON.parse(p.diagnostic.manifest.text), first = JSON.parse(p.original.manifest.text), response = JSON.parse(p.diagnostic.response.text);
      switch (kind) {
        case 'authority': m.authority = 'Approved'; break;
        case 'identityScope': m.identityScope = 'Global'; break;
        case 'compiler': m.compiler.version = 'foreign'; break;
        case 'context': m.context = original.cases[0].context; break;
        case 'generation': m.clientGeneration++; break;
        case 'origin-run': m.origin.runId = JSON.parse(p.cancelled.manifest.text).runId; break;
        case 'origin-attempt': m.origin.attemptId = JSON.parse(p.cancelled.response.text).attemptId; break;
        case 'lineage': m.knownLineage = []; break;
        case 'cycle': m.knownLineage = [m.runId]; break;
        case 'session': m.sessionId = JSON.parse(original.cases[0].regionalOperatorRecordings.original.manifest.text).sessionId; break;
        case 'duplicate-run': m.runId = first.runId; break;
        case 'duplicate-attempt': response.attemptId = JSON.parse(p.original.response.text).attemptId; break;
        case 'role-collision': response.attemptId = first.runId; break;
      }
      frame(p.diagnostic.manifest, manifestDomain, JSON.stringify(m)); frame(p.diagnostic.response, responseDomain, JSON.stringify(response)); linkEnvelopes(p);
    });
  });
  it.each(['exponent', 'fraction', 'negative-zero', 'unsafe', 'duplicate', 'escaped-duplicate'])('refuses original metadata token alias: %s', async kind => {
    await refuses(p => {
      const token = '"clientGeneration":7';
      const replacement = { exponent: '"clientGeneration":7e0', fraction: '"clientGeneration":7.0000000000000001', 'negative-zero': '"clientGeneration":-0',
        unsafe: '"clientGeneration":9007199254740993', duplicate: token + ',' + token, 'escaped-duplicate': token + ',"clientGenera' + String.fromCharCode(92) + 'u0074ion":7' }[kind]!;
      expect(p.original.manifest.text).toContain(token); frame(p.original.manifest, manifestDomain, p.original.manifest.text.replace(token, replacement));
    });
  });
  it('refuses a cancelled host attempt carrying an actual completed envelope', async () => {
    await refuses(p => { Object.assign(p.cancelled, { result: structuredClone(p.diagnostic.result), observation: structuredClone(p.diagnostic.observation) }); linkEnvelopes(p); });
  });
  it.each(['operatorBodies', 'evaluationFrames'] as const)('checks unselected retry original %s bytes', async map => {
    await refuses(p => { const body = p.retry.regions.flatMap(r => r.diagnostics).find(d => d.available[map].length)!.available[map][0]; Object.assign(body, { text: body.text + ' ' }); });
  });
  it.each(['manifest', 'budget'] as const)('refuses body disclosure through %s mode', async choice => {
    await refuses(p => { const d = p.diagnostic.regions.flatMap(r => r.diagnostics).find(d => d.available.operatorBodies.length)!; Object.assign(d[choice], { operatorBodies: structuredClone(d.available.operatorBodies) }); });
  });
  it.each(['retainedBodyBytes', 'retainedEvaluationFrameBytes', 'selectedEvaluations', 'maximumBodyBytes', 'state', 'diagnostic', 'binding', 'region'])('refuses rehashed false snapshot selection: %s', async field => {
    await refuses(p => {
      const target = p.retry.regions.flatMap(r => r.diagnostics).find(d => d.available.operatorBodies.length)!.available.frame, data = JSON.parse(target.text);
      if (field === 'state') data.state = 'ManifestOnly';
      else if (field === 'diagnostic') data.diagnostic.diagnosticId = '00000000-0000-4000-8000-000000000001';
      else if (field === 'binding') data.region.bindingDigest = p.retry.regions[0].binding.digest;
      else if (field === 'region') data.region.publishedDigest = p.retry.regions[0].region.digest;
      else data[field]++;
      frame(target, snapshotDomain, JSON.stringify(data));
    });
  });
  it.each(['ordinal', 'binding', 'source', 'context', 'first', 'end', 'published', 'retained', 'execution'])('refuses rehashed foreign actual-call metadata: %s', async kind => {
    await refuses(p => {
      changeObservation(p.diagnostic, record => {
        const call = record.regions[1];
        if (kind === 'ordinal') call.ordinal++;
        else if (kind === 'binding') call.bindingDigest = record.regions[0].bindingDigest;
        else if (kind === 'source') call.sourceDigest = record.regions[0].sourceDigest;
        else if (kind === 'context') call.context = record.regions[0].context;
        else if (kind === 'first') call.firstEvaluationOrdinal++;
        else if (kind === 'end') call.startedEvaluationExclusiveEnd++;
        else if (kind === 'published') call.publishedRegionDigest = record.regions[0].publishedRegionDigest;
        else if (kind === 'retained') call.retainedRegion = false;
        else call.regionalResult.execution = 'coglatas.cancelled';
      }); linkEnvelopes(p);
    });
  });
  it.each(['binding', 'published', 'source', 'context', 'rule', 'location', 'ordinal', 'links', 'diagnostic'])('refuses rehashed foreign observed-evaluation metadata: %s', async kind => {
    await refuses(p => {
      changeObservation(p.diagnostic, record => {
        const e = record.evaluations[0], other = record.regions[0];
        if (kind === 'binding') e.bindingRegionDigest = other.bindingDigest;
        else if (kind === 'published') e.publishedRegionDigest = other.publishedRegionDigest;
        else if (kind === 'source') e.sourceDigest = other.sourceDigest;
        else if (kind === 'context') e.context = other.context;
        else if (kind === 'rule') e.ruleId = 'coglatas.core.requirements';
        else if (kind === 'location') e.location.context = other.context;
        else if (kind === 'ordinal') e.ordinal++;
        else if (kind === 'links') e.regionalDiagnosticLinks++;
        else e.diagnostics[0].diagnosticId = '00000000-0000-4000-8000-000000000001';
      }); linkEnvelopes(p);
    });
  });
  it('rejects a copied region body from another captured version', async () => {
    await refuses(p => Object.assign(p.diagnostic.regions[2], structuredClone(p.diagnostic.regions[1])));
  });
  it('rejects an undeclared member', async () => { await refuses(p => Object.assign(p, { undeclaredRun: structuredClone(p.original) })); });
});

describe('Exact regional client ownership across reads', () => {
  it('rejects cloned region and diagnostic objects, including equal digests and IDs', async () => {
    const value = fixture(), source = (await readScopedSources(value))!, recording = (await readRegionalOperatorRecording(value, source, 'diagnostic'))!;
    const region = recording.regions.find(r => r.diagnostics.some(d => d.code === 'coglatas.analysis.constraint-false'))!, diagnostic = region.diagnostics[0];
    const admitted = (await readRecordedRegionalOperators(value, source, recording, region, diagnostic, 'available'))!;
    expect(recordedRegionalOperatorsMatch(value, source, recording, region, diagnostic, admitted)).toBe(true);
    for (const foreign of [structuredClone(region), { ...region }]) await expect(readRecordedRegionalOperators(value, source, recording, foreign, diagnostic, 'available')).rejects.toThrow();
    for (const foreign of [structuredClone(diagnostic), { ...diagnostic }]) await expect(readRecordedRegionalOperators(value, source, recording, region, foreign, 'available')).rejects.toThrow();
    expect(recordedRegionalOperatorsMatch(value, source, recording, region, structuredClone(diagnostic), admitted)).toBe(false);
  });
  it('rejects actual members belonging to an earlier admission or other host run', async () => {
    const value = fixture(), source = (await readScopedSources(value))!, first = (await readRegionalOperatorRecording(value, source, 'diagnostic'))!;
    const region = first.regions.find(r => r.diagnostics.length)!, diagnostic = region.diagnostics[0];
    for (const label of ['diagnostic', 'retry'] as const) {
      const next = (await readRegionalOperatorRecording(value, source, label))!;
      await expect(readRecordedRegionalOperators(value, source, next, region, diagnostic, 'available')).rejects.toThrow();
    }
  });
  it('rejects a root summary diagnostic as an external regional diagnostic', async () => {
    const value = fixture(), source = (await readScopedSources(value))!, recording = (await readRegionalOperatorRecording(value, source, 'diagnostic'))!;
    const region = recording.regions.find(r => !r.isRoot && r.diagnostics.length)!;
    const summary = value.result.diagnostics.find(d => d.primaryLocation.context.branch.projectId === value.context.branch.projectId)!;
    expect(summary).toBeDefined(); await expect(readRecordedRegionalOperators(value, source, recording, region, summary, 'available')).rejects.toThrow();
  });
  it('invalidates recording and selected graphs when the admitted Source is lost', async () => {
    const value = fixture(), source = (await readScopedSources(value))!, recording = (await readRegionalOperatorRecording(value, source, 'diagnostic'))!;
    const region = recording.regions.find(r => r.diagnostics.length)!, diagnostic = region.diagnostics[0];
    const selected = (await readRecordedRegionalOperators(value, source, recording, region, diagnostic, 'available'))!;
    Object.defineProperty(value, 'sourceText', { value: value.sourceText + ' ' });
    expect(regionalOperatorRecordingMatches(value, source, recording)).toBe(false);
    expect(recordedRegionalOperatorsMatch(value, source, recording, region, diagnostic, selected)).toBe(false);
    await expect(readRecordedRegionalOperators(value, source, recording, region, diagnostic, 'available')).rejects.toThrow();
  });
  it('invalidates an equal projection replacement after admission', async () => {
    const value = fixture(), source = (await readRootSource(value))!, recording = (await readRegionalOperatorRecording(value, source))!;
    Object.defineProperty(value, 'regionalOperatorRecordings', { value: structuredClone(value.regionalOperatorRecordings) });
    expect(regionalOperatorRecordingMatches(value, source, recording)).toBe(false);
  });
  it('refuses projection replacement during an asynchronous original-byte hash read', async () => {
    const value = fixture(), source = (await readScopedSources(value))!;
    const pending = readRegionalOperatorRecording(value, source, 'diagnostic');
    Object.defineProperty(value, 'regionalOperatorRecordings', { value: structuredClone(value.regionalOperatorRecordings) });
    await expect(pending).rejects.toThrow();
  });
  it('checks cross-case run, attempt and session uniqueness', () => {
    const data = readPreviewBundle(structuredClone(original)); expect(regionalOperatorRunIdentitiesAreUnique(data.cases)).toBe(true);
    expect(regionalOperatorRunIdentitiesAreUnique([data.cases[0], data.cases[0]])).toBe(false);
  });
});

describe('Original metadata budget publication refuses partial call lists', () => {
  it('refuses a fully rehashed metadata gap retaining the original partial call metadata', async () => {
    await refuses(p => {
      changeObservation(p.original, record => { expect(record.evaluations).toEqual([]); expect(record.regions.length).toBeGreaterThan(0); record.state = 'MetadataBudgetExceeded'; });
      linkEnvelopes(p);
      const response = JSON.parse(p.original.response.text), receipt = JSON.parse(p.original.receipt.text);
      expect(response.captureState).toBe('MetadataBudgetExceeded'); expect(receipt.captureState).toBe('MetadataBudgetExceeded');
      for (const region of p.original.regions) for (const diagnostic of region.diagnostics) {
        const view = JSON.parse(diagnostic.available.frame.text); expect(view.originalRecord.digest).toEqual(p.original.observation!.digest);
        expect(view.originalRecord.captureState).toBe('MetadataBudgetExceeded'); expect(diagnostic.available.operatorBodies).toEqual([]);
      }
    });
  });
});
