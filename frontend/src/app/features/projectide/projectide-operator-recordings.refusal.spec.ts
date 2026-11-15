/// <reference types="node" />
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { createHash, webcrypto } from 'node:crypto';
import { vi } from 'vitest';
import { readPreviewBundle } from './projectide-preview.adapter';
import { readRootSource } from './projectide-root-source.reader';
import { OperatorRecordingFrame, OperatorRecordingProjection, readOperatorRecording } from './projectide-operator-recordings.reader';
import { OperatorTextFrame } from './projectide-operator-recording-structure';

vi.stubGlobal('crypto', webcrypto);
const original = JSON.parse(readFileSync(resolve('public/assets/projectide/operator-recordings-v1.json'), 'utf8'));
const labels = ['original', 'diagnostic', 'budget', 'cancelled', 'retry'] as const;
const frame = (target: OperatorTextFrame, domain: string, text: string): void => {
  Object.assign(target, { text, digest: { algorithm: 'sha-256', value: createHash('sha256').update(domain + String.fromCharCode(0)).update(text).digest('hex') } });
};
const edit = (target: OperatorTextFrame, domain: string, change: (text: string) => string): void => frame(target, domain, change(target.text));
const manifestDomain = 'coglatas.local-predicate-observation-compiler-recording-manifest/1';
const responseDomain = 'coglatas.local-predicate-observation-compiler-host-response/1';
const receiptDomain = 'coglatas.local-predicate-observation-compiler-recording-receipt/1';
const snapshotDomain = 'coglatas.local-initial-root-predicate-observation-snapshot/1';
/** Repair envelope digests so semantic refusals are not only stale-hash refusals. Original subjects are otherwise preserved. */
function linkEnvelopes(projection: OperatorRecordingProjection): void {
  for (const label of labels) {
    const run = projection[label]; const m = JSON.parse(run.manifest.text);
    const ancestor = label === 'diagnostic' ? projection.original : label === 'budget' ? projection.diagnostic : label === 'retry' ? projection.cancelled : null;
    if (ancestor && m.origin) { m.origin.manifestDigest = ancestor.manifest.digest; m.origin.receiptDigest = ancestor.receipt.digest; }
    frame(run.manifest, manifestDomain, JSON.stringify(m));
    const r = JSON.parse(run.response.text); r.manifestDigest = run.manifest.digest; r.runId = m.runId;
    r.observationDigest = run.observation?.digest ?? null;
    frame(run.response, responseDomain, JSON.stringify(r));
    const receipt = JSON.parse(run.receipt.text); receipt.manifestDigest = run.manifest.digest; receipt.runId = m.runId;
    receipt.hostResponseDigest = run.response.digest; receipt.attemptId = r.attemptId;
    receipt.observationRecordDigest = run.observation?.digest ?? null;
    frame(run.receipt, receiptDomain, JSON.stringify(receipt));
  }
}
async function refuses(change: (projection: OperatorRecordingProjection) => void, expectedError = 'unsupported-operator-recording', key = 'operator-nested-duplicates'): Promise<void> {
  const value = readPreviewBundle(structuredClone(original)).cases.find(v => v.key === key)!;
  change(value.operatorRecordings!);
  // Admission is created after corruption; a preexisting Source fingerprint cannot mask the deep reader check.
  const source = await readRootSource(value); expect(source).not.toBeNull();
  await expect(readOperatorRecording(value, source!)).rejects.toThrow(expectedError);
}
function changeManifest(run: OperatorRecordingFrame, change: (text: string) => string): void { edit(run.manifest, manifestDomain, change); }

describe('Operator recording semantic refusals before any member admission', () => {
  it.each(labels)('rejects a corrupted unselected request: %s', async label => {
    await refuses(p => { edit(p[label].request, 'coglatas.local-predicate-observation-compiler-host-input/1', t => t.replace('ActualInitialRootEvaluationOnly', 'ForeignEvaluation')); });
  });
  it.each(['source', 'input'] as const)('rejects a foreign original %s frame even when its own digest is valid', async key => {
    await refuses(p => { const foreign = original.cases.find((v: { key: string }) => v.key === 'fail').operatorRecordings; Object.assign(p[key], structuredClone(foreign[key])); });
  });
  it.each(['authority', 'identityScope', 'compiler', 'context', 'generation', 'origin-run', 'origin-attempt', 'lineage', 'cycle', 'session', 'duplicate-run', 'duplicate-attempt', 'role-collision'])('rejects rehashed false invocation metadata: %s', async kind => {
    await refuses(p => {
      const m = JSON.parse(p.diagnostic.manifest.text); const originalManifest = JSON.parse(p.original.manifest.text);
      const originalResponse = JSON.parse(p.original.response.text);
      switch (kind) {
        case 'authority': m.authority = 'Approved'; break;
        case 'identityScope': m.identityScope = 'Global'; break;
        case 'compiler': m.compiler.version = 'foreign'; break;
        case 'context': m.context.revisionId = original.cases[0].context.revisionId; break;
        case 'generation': m.clientGeneration++; break;
        case 'origin-run': m.origin.runId = JSON.parse(p.cancelled.manifest.text).runId; break;
        case 'origin-attempt': m.origin.attemptId = JSON.parse(p.cancelled.response.text).attemptId; break;
        case 'lineage': m.knownLineage = []; break;
        case 'cycle': m.knownLineage = [m.runId]; break;
        case 'session': m.sessionId = JSON.parse(original.cases[0].operatorRecordings.original.manifest.text).sessionId; break;
        case 'duplicate-run': m.runId = originalManifest.runId; break;
        case 'duplicate-attempt': {
          const response = JSON.parse(p.diagnostic.response.text); response.attemptId = originalResponse.attemptId;
          frame(p.diagnostic.response, responseDomain, JSON.stringify(response)); break;
        }
        case 'role-collision': {
          const response = JSON.parse(p.diagnostic.response.text); response.attemptId = originalManifest.runId;
          frame(p.diagnostic.response, responseDomain, JSON.stringify(response)); break;
        }
      }
      frame(p.diagnostic.manifest, manifestDomain, JSON.stringify(m)); linkEnvelopes(p);
    });
  });
  it.each(['exponent', 'fraction', 'negative-zero', 'unsafe', 'duplicate', 'escaped-duplicate'])('rejects original numeric or duplicate-token aliases: %s', async kind => {
    await refuses(p => {
      const token = '"clientGeneration":7';
      const replacement = { exponent: '"clientGeneration":7e0', fraction: '"clientGeneration":7.0000000000000001',
        'negative-zero': '"clientGeneration":-0', unsafe: '"clientGeneration":9007199254740993',
        duplicate: token + ',' + token, 'escaped-duplicate': token + ',"clientGenera' + String.fromCharCode(92) + 'u0074ion":7' }[kind]!;
      changeManifest(p.original, text => { expect(text).toContain(token); return text.replace(token, replacement); });
    }, kind === 'duplicate' || kind === 'escaped-duplicate' ? 'unsupported-local-test-projection' : 'unsupported-operator-recording');
  });
  it('rejects a cancelled attempt carrying a genuine completed result', async () => {
    await refuses(p => { Object.assign(p.cancelled, { result: structuredClone(p.diagnostic.result), observation: structuredClone(p.diagnostic.observation) }); linkEnvelopes(p); });
  });
  it('rejects a validly hashed materialization outside this explicit empty-materialization recipe', async () => {
    await refuses(p => { edit(p.original.materializations, 'coglatas.host-materializations/1', text => text.replace('"snapshots":[]', '"snapshots":[{}]')); });
  });
  it.each(['operatorBodies', 'evaluationFrames'] as const)('rejects a corrupt unselected retry %s before original Manifest admission', async map => {
    await refuses(p => { const body = p.retry.diagnostics[0].available[map][0]; Object.assign(body, { text: body.text + ' ' }); });
  });
  it.each(['manifest', 'budget'] as const)('rejects body disclosure in the %s snapshot', async choice => {
    await refuses(p => { Object.assign(p.diagnostic.diagnostics[0][choice], { operatorBodies: structuredClone(p.diagnostic.diagnostics[0].available.operatorBodies) }); });
  });
  it.each(['retainedBodyBytes', 'retainedEvaluationFrameBytes', 'selectedEvaluations', 'maximumBodyBytes', 'state', 'diagnostic'])('rejects semantically false rehashed selected snapshot metadata: %s', async field => {
    await refuses(p => {
      const target = p.retry.diagnostics[0].available.frame; const data = JSON.parse(target.text);
      if (field === 'state') data.state = 'ManifestOnly';
      else if (field === 'diagnostic') data.diagnostic.diagnosticId = '00000000-0000-4000-8000-000000000001';
      else data[field]++;
      frame(target, snapshotDomain, JSON.stringify(data));
    });
  });
  it('rejects an extra recording member rather than silently ignoring it', async () => {
    await refuses(p => Object.assign(p, { undeclaredRun: structuredClone(p.original) }));
  });
});

describe('Requirement condition graph bindings', () => {
  for (const key of ['operator-requirement-false-nested', 'operator-requirement-true-short-circuit', 'operator-requirement-unknown']) {
    it.each(['foreign-rule', 'missing-evidence-link', 'no-actual-evaluation'])('refuses rehashed false Requirement observation: ' + key + ' %s', async kind => {
      await refuses(p => {
        const run = p.diagnostic; const record = JSON.parse(run.observation!.text);
        if (kind === 'foreign-rule') record.evaluations[0].ruleId = 'coglatas.core.constraints';
        else if (kind === 'missing-evidence-link') {
          const missing = run.diagnostics.find(d => JSON.parse(d.diagnostic.text).code === 'coglatas.analysis.requirement-evidence-unavailable')!;
          record.evaluations[0].originalRootDiagnosticLinks = 1;
          record.evaluations[0].publishedDiagnostics = [{ diagnosticId: JSON.parse(missing.diagnostic.text).diagnosticId, digest: missing.diagnostic.digest }];
        } else Object.assign(record, { state: 'NoActualEvaluations', startedEvaluations: 0, completedEvaluations: 0, observedNodes: 0, retainedBodyBytes: 0, evaluations: [] });
        frame(run.observation!, 'coglatas.local-initial-root-predicate-observation/1', JSON.stringify(record)); linkEnvelopes(p);
      }, 'unsupported-operator-recording', key);
    });
  }
});
